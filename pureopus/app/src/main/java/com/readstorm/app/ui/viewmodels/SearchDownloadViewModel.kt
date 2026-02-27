package com.readstorm.app.ui.viewmodels

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import com.readstorm.app.application.abstractions.IBookRepository
import com.readstorm.app.domain.models.*
import com.readstorm.app.infrastructure.services.AppLogger
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.atomic.AtomicBoolean
import java.util.concurrent.atomic.AtomicInteger

class SearchDownloadViewModel(
    private val parent: MainViewModel,
    private val bookRepo: IBookRepository
) {
    // ── Observable Properties ──
    private val _searchKeyword = MutableLiveData("")
    val searchKeyword: LiveData<String> = _searchKeyword

    private val _isSearching = MutableLiveData(false)
    val isSearching: LiveData<Boolean> = _isSearching

    private val _isCheckingHealth = MutableLiveData(false)
    val isCheckingHealth: LiveData<Boolean> = _isCheckingHealth

    private val _hasNoSearchResults = MutableLiveData(false)
    val hasNoSearchResults: LiveData<Boolean> = _hasNoSearchResults

    private val _selectedSearchResult = MutableLiveData<SearchResult?>(null)
    val selectedSearchResult: LiveData<SearchResult?> = _selectedSearchResult

    private val _activeDownloadSummary = MutableLiveData("")
    val activeDownloadSummary: LiveData<String> = _activeDownloadSummary

    private val _searchResults = MutableLiveData<List<SearchResult>>(emptyList())
    val searchResults: LiveData<List<SearchResult>> = _searchResults

    private val _downloadTasks = MutableLiveData<List<DownloadTask>>(emptyList())
    val downloadTasks: LiveData<List<DownloadTask>> = _downloadTasks

    private val _filteredDownloadTasks = MutableLiveData<List<DownloadTask>>(emptyList())
    val filteredDownloadTasks: LiveData<List<DownloadTask>> = _filteredDownloadTasks

    /** 每次 applyTaskFilter 递增，Compose 端观测此值以强制重组 */
    private val _taskListVersion = MutableLiveData(0)
    val taskListVersion: LiveData<Int> = _taskListVersion
    private var taskVersionCounter = 0

    /** 待确认删除的下载任务 — UI 层观察此值弹出确认对话框 */
    private val _pendingDeleteTask = MutableLiveData<DownloadTask?>(null)
    val pendingDeleteTask: LiveData<DownloadTask?> = _pendingDeleteTask

    var selectedSourceId: Int = 0
    var taskFilterStatus: String = "全部"
        set(value) {
            field = value
            applyTaskFilter()
        }

    private val downloadTaskList = mutableListOf<DownloadTask>()
    private val downloadJobs = ConcurrentHashMap<String, Job>()
    private val taskListeners = ConcurrentHashMap<String, (String) -> Unit>()
    private val pauseRequested = mutableSetOf<String>()
    private var searchJob: Job? = null
    private val sourceHealthAutoRefreshed = AtomicBoolean(false)

    // ── New fields for aggregate search & auto-prefetch management ──
    private val searchSerial = AtomicInteger(0)
    private val autoPrefetchTaskByBook = ConcurrentHashMap<String, String>()
    private val coroutineScope = CoroutineScope(Dispatchers.Main + SupervisorJob())

    companion object {
        val TASK_FILTER_OPTIONS = listOf("全部", "排队中", "下载中", "已完成", "已失败", "已取消", "已暂停")

        private fun mapFilterToStatus(filter: String): DownloadTaskStatus? = when (filter) {
            "排队中" -> DownloadTaskStatus.Queued
            "下载中" -> DownloadTaskStatus.Downloading
            "已完成" -> DownloadTaskStatus.Succeeded
            "已失败" -> DownloadTaskStatus.Failed
            "已取消" -> DownloadTaskStatus.Cancelled
            "已暂停" -> DownloadTaskStatus.Paused
            else -> null
        }
    }

    fun setSearchKeyword(keyword: String) {
        _searchKeyword.postValue(keyword)
    }

    fun setSelectedSearchResult(result: SearchResult?) {
        _selectedSearchResult.postValue(result)
    }

    // ── Search (with aggregate + dedup + serial protection) ──

    suspend fun search(keyword: String) {
        if (keyword.isBlank()) return

        searchJob?.cancel()
        val serial = searchSerial.incrementAndGet()

        try {
            _isSearching.postValue(true)
            _hasNoSearchResults.postValue(false)
            parent.setStatusMessage("搜索中...")

            val trimmed = keyword.trim()
            var selectedSourceText: String

            val resultList = mutableListOf<SearchResult>()

            if (selectedSourceId > 0) {
                // Single source search
                selectedSourceText = "书源 $selectedSourceId"
                val results = parent.searchBooksUseCase.execute(trimmed, selectedSourceId)
                val src = parent.sources.firstOrNull { it.id == results.firstOrNull()?.sourceId }
                results.forEach { item ->
                    val srcName = src?.name ?: "书源${item.sourceId}"
                    resultList.add(item.copy(sourceName = srcName))
                }
            } else {
                // Aggregate search: iterate healthy sources, per-source limit 3, dedup
                val healthySources = parent.sources
                    .filter { it.id > 0 && it.isHealthy == true && it.searchSupported }

                if (healthySources.isEmpty()) {
                    _searchResults.postValue(emptyList())
                    parent.setStatusMessage("搜索完成（全部书源(健康)）：0 条。当前没有可用的绿色节点，请先刷新书源健康状态。")
                    return
                }

                val perSourceLimit = 3
                val maxConcurrent = (parent.settings.aggregateSearchMaxConcurrency).coerceIn(1, 64)
                val semaphore = Semaphore(maxConcurrent)

                val perSourceResults = coroutineScope {
                    healthySources.map { src ->
                        async {
                            semaphore.withPermit {
                                try {
                                    val one = parent.searchBooksUseCase.execute(trimmed, src.id)
                                    one.take(perSourceLimit).map { it.copy(sourceName = src.name) }
                                } catch (_: CancellationException) {
                                    emptyList()
                                } catch (e: Exception) {
                                    AppLogger.log("Search.PerSource:${src.name}", "error: ${e.message}")
                                    emptyList()
                                }
                            }
                        }
                    }.awaitAll()
                }

                // Dedup by Title|Author|SourceId
                val merged = perSourceResults
                    .flatten()
                    .groupBy { "${it.title}|${it.author}|${it.sourceId}".lowercase() }
                    .values
                    .map { it.first() }

                resultList.addAll(merged)
                selectedSourceText = "全部书源(健康:${healthySources.size}源,每源前${perSourceLimit}条)"
            }

            // Only apply if still the latest serial
            if (serial != searchSerial.get()) return

            _searchResults.postValue(resultList)

            if (resultList.isEmpty() && selectedSourceId > 0) {
                parent.setStatusMessage("搜索完成（$selectedSourceText）：0 条。该书源当前可能限流/规则不兼容，请切换书源重试。")
            } else {
                parent.setStatusMessage("搜索完成（${if (selectedSourceId > 0) selectedSourceText else "全部书源(健康)"}）：共 ${resultList.size} 条")
            }
        } catch (_: CancellationException) {
            // Cancelled by next search, silent
        } catch (e: Exception) {
            parent.setStatusMessage("搜索失败：${e.message}")
        } finally {
            if (serial == searchSerial.get()) {
                _isSearching.postValue(false)
                _hasNoSearchResults.postValue((_searchResults.value ?: emptyList()).isEmpty())
            }
        }
    }

    // ── Health Check ──

    suspend fun refreshSourceHealth() {
        if (_isCheckingHealth.value == true) return
        _isCheckingHealth.postValue(true)
        try {
            val rules = parent.sources
                .filter { it.id > 0 }
                .map { com.readstorm.app.domain.models.BookSourceRule(
                    id = it.id, name = it.name, url = it.url, searchSupported = it.searchSupported
                ) }

            val results = parent.healthCheckUseCase.checkAll(rules)
            val lookup = results.associateBy({ it.sourceId }, { it.isReachable })

            parent.sources.forEach { source ->
                lookup[source.id]?.let { source.isHealthy = it }
            }
            parent.notifySourcesChanged()

            val ok = results.count { it.isReachable }
            parent.setStatusMessage("书源健康检测完成：$ok/${results.size} 可达")

            parent.ruleEditor.syncRuleEditorRuleHealthFromSources()
        } catch (e: Exception) {
            parent.setStatusMessage("书源健康检测失败：${e.message}")
        } finally {
            _isCheckingHealth.postValue(false)
        }
    }

    suspend fun refreshSourceHealthOnceOnScreenEnter() {
        if (sourceHealthAutoRefreshed.compareAndSet(false, true)) {
            refreshSourceHealth()
        }
    }

    // ── Download Queue ──

    fun queueDownload() {
        val selected = _selectedSearchResult.value ?: run {
            parent.setStatusMessage("请先在搜索结果中选择一本书。")
            return
        }

        if (selected.url.isBlank()) {
            parent.setStatusMessage("搜索结果缺少书籍 URL，无法下载。")
            return
        }

        // Duplicate detection: prevent queueing same book from same source if already active
        if (hasActiveDuplicateSearchTask(selected)) {
            parent.setStatusMessage("已存在同名同书源的运行中任务：《${selected.title}》，请等待当前任务完成或先停止后再入队。")
            return
        }

        val task = DownloadTask(
            id = UUID.randomUUID().toString(),
            bookId = "",
            bookTitle = selected.title,
            author = selected.author,
            mode = DownloadMode.FullBook,
            enqueuedAt = System.currentTimeMillis()
        ).apply {
            sourceSearchResult = selected
        }

        downloadTaskList.add(0, task)
        attachTaskListener(task)
        applyTaskFilter()
        parent.setStatusMessage("已加入下载队列：《${task.bookTitle}》")

        startDownload(task, selected)
    }

    private fun startDownload(task: DownloadTask, selected: SearchResult) {
        val job = CoroutineScope(Dispatchers.IO + SupervisorJob()).launch {
            // Periodic DB refresh during download (every 1s)
            val refreshJob = launch {
                periodicRefreshDbBooks(task)
            }
            try {
                parent.downloadQueue.enqueue(selected.sourceId) {
                    parent.downloadBookUseCase.queue(task, selected, task.mode)
                }
            } catch (e: CancellationException) {
                val wasPaused = synchronized(pauseRequested) { pauseRequested.remove(task.id) }
                if (wasPaused && task.currentStatus == DownloadTaskStatus.Cancelled) {
                    task.overrideToPaused()
                }
            } catch (e: Exception) {
                task.error = e.message ?: "未知错误"
                if (task.currentStatus == DownloadTaskStatus.Downloading ||
                    task.currentStatus == DownloadTaskStatus.Queued) {
                    task.transitionTo(DownloadTaskStatus.Failed)
                }
                AppLogger.log("SearchDownload", "下载异常[${task.bookTitle}]: ${e.stackTraceToString()}")
            } finally {
                refreshJob.cancel()
                downloadJobs.remove(task.id)
                withContext(Dispatchers.Main) { onDownloadCompleted(task) }
            }
        }
        downloadJobs[task.id] = job
    }

    /** Periodic refresh of reader & bookshelf during download */
    private suspend fun periodicRefreshDbBooks(task: DownloadTask) {
        try {
            while (true) {
                delay(1000)
                val currentBook = parent.reader.selectedDbBook
                if (currentBook != null) {
                    val needsForeground = parent.reader.needsCurrentChapterForegroundRefresh()
                    val hasActiveForCurrent = downloadTaskList.any { t ->
                        val sameBook = (!t.bookId.isNullOrBlank() && t.bookId.equals(currentBook.id, ignoreCase = true))
                                || (t.bookId.isNullOrBlank()
                                && t.bookTitle.equals(currentBook.title, ignoreCase = true)
                                && t.author.equals(currentBook.author, ignoreCase = true))
                        sameBook && (t.currentStatus == DownloadTaskStatus.Queued || t.currentStatus == DownloadTaskStatus.Downloading)
                    }
                    if (needsForeground || hasActiveForCurrent) {
                        parent.reader.refreshCurrentBookFromDownloadSignal(currentBook.id)
                    }
                }
                // Lazy bookshelf refresh
                parent.bookshelf.markBookshelfDirty()
            }
        } catch (_: CancellationException) { }
    }

    /** Called when a download task completes (success or failure) */
    private suspend fun onDownloadCompleted(task: DownloadTask) {
        // Clean up auto-prefetch tracking
        if (task.isAutoPrefetch && !task.bookId.isNullOrBlank()) {
            val mappedId = autoPrefetchTaskByBook[task.bookId!!]
            if (mappedId == task.id) {
                autoPrefetchTaskByBook.remove(task.bookId!!)
            }
        }

        applyTaskFilter()

        if (task.currentStatus == DownloadTaskStatus.Succeeded) {
            if (task.isAutoPrefetch) {
                parent.setStatusMessage("自动预取完成：《${task.bookTitle}》 ${(task.rangeStartIndex ?: 0) + 1}-${(task.rangeStartIndex ?: 0) + (task.rangeTakeCount ?: 0)}")
            } else {
                parent.setStatusMessage("下载完成：《${task.bookTitle}》")
            }
        } else if (!task.isAutoPrefetch) {
            parent.setStatusMessage("下载结束（${task.currentStatus}）：${task.error}")
        }

        markBookshelfDirty()
        parent.reader.refreshCurrentBookFromDownloadSignal(task.bookId)
    }

    fun pauseDownload(task: DownloadTask) {
        if (!task.canPause) return
        synchronized(pauseRequested) { pauseRequested.add(task.id) }
        downloadJobs[task.id]?.cancel()
        task.transitionTo(DownloadTaskStatus.Paused)
        applyTaskFilter()
        parent.setStatusMessage("已暂停：《${task.bookTitle}》")
    }

    fun resumeDownload(task: DownloadTask) {
        if (!task.canResume) return
        task.resetForResume()
        applyTaskFilter()
        parent.setStatusMessage("恢复下载：《${task.bookTitle}》")
        task.sourceSearchResult?.let { startDownload(task, it) }
    }

    fun retryDownload(task: DownloadTask) {
        if (!task.canRetry) return
        task.resetForRetry()
        applyTaskFilter()
        parent.setStatusMessage("正在重试（第${task.retryCount}次）：《${task.bookTitle}》...")
        task.sourceSearchResult?.let { startDownload(task, it) }
    }

    fun cancelDownload(task: DownloadTask) {
        if (!task.canCancel) return
        downloadJobs[task.id]?.cancel()
        task.transitionTo(DownloadTaskStatus.Cancelled)
        task.error = "用户手动取消"
        applyTaskFilter()
        parent.setStatusMessage("已取消：《${task.bookTitle}》")
    }

    fun deleteDownload(task: DownloadTask) {
        if (!task.canDelete) return
        // 触发确认事件，UI 层观察 pendingDeleteTask 弹出对话框
        _pendingDeleteTask.postValue(task)
    }

    /** UI 层确认删除后调用 */
    fun confirmDeleteDownload() {
        val task = _pendingDeleteTask.value ?: return
        _pendingDeleteTask.postValue(null)
        downloadJobs[task.id]?.cancel()
        detachTaskListener(task)
        downloadTaskList.remove(task)
        applyTaskFilter()
        parent.setStatusMessage("已删除任务：《${task.bookTitle}》")
    }

    /** UI 层取消删除后调用 */
    fun cancelDeleteDownload() {
        _pendingDeleteTask.postValue(null)
    }

    fun stopAllDownloads() {
        val active = downloadTaskList.filter { it.canPause }
        if (active.isEmpty()) {
            parent.setStatusMessage("没有可停止的下载任务。")
            return
        }
        active.forEach { task ->
            synchronized(pauseRequested) { pauseRequested.add(task.id) }
            downloadJobs[task.id]?.cancel()
            task.transitionTo(DownloadTaskStatus.Paused)
        }
        applyTaskFilter()
        parent.setStatusMessage("已全部停止：${active.size} 个任务。")
    }

    fun startAllDownloads() {
        val paused = downloadTaskList.filter { it.canResume }
        if (paused.isEmpty()) {
            parent.setStatusMessage("没有可恢复的下载任务。")
            return
        }
        paused.forEach { task ->
            task.resetForResume()
            task.sourceSearchResult?.let { startDownload(task, it) }
        }
        applyTaskFilter()
        parent.setStatusMessage("已全部恢复：${paused.size} 个任务。")
    }

    // ── Auto Prefetch API (with conflict management) ──

    fun queueOrReplaceAutoPrefetch(book: BookEntity, startIndex: Int, takeCount: Int, reason: String) {
        if (book.id.isBlank() || book.tocUrl.isBlank()) return

        val start = maxOf(0, startIndex)
        val take = maxOf(1, takeCount)

        val isPriorityTrigger = reason.equals("jump", ignoreCase = true)
                || reason.equals("force-current", ignoreCase = true)
                || reason.equals("foreground-direct", ignoreCase = true)
                || reason.equals("manual-priority", ignoreCase = true)

        AppLogger.log("SearchDownload.AutoPrefetch.Request",
            "bookId=${book.id}, reason=$reason, priority=$isPriorityTrigger, start=${start + 1}, take=$take")

        // Check for existing identical range task
        val existingSameRange = downloadTaskList.firstOrNull { t ->
            val sameBook = (!t.bookId.isNullOrBlank() && t.bookId.equals(book.id, ignoreCase = true))
                    || (t.bookId.isNullOrBlank() && t.bookTitle.equals(book.title, ignoreCase = true)
                    && t.author.equals(book.author, ignoreCase = true))
            sameBook && (t.currentStatus == DownloadTaskStatus.Queued || t.currentStatus == DownloadTaskStatus.Downloading)
                    && t.mode == DownloadMode.Range && t.rangeStartIndex == start && t.rangeTakeCount == take
        }
        if (existingSameRange != null) {
            AppLogger.log("SearchDownload.AutoPrefetch.Skip",
                "bookId=${book.id}, reason=$reason, start=${start + 1}, take=$take, duplicateTaskId=${existingSameRange.id}")
            return
        }

        if (!isPriorityTrigger) {
            removeAutoPrefetchTasksForBook(book.id, "自动预取重排：$reason")
        } else {
            removeConflictingTasksForBook(book, "跳章优先重排：$reason", removeAllModes = true)
        }

        val searchResult = SearchResult(
            id = UUID.randomUUID(),
            title = book.title,
            author = book.author,
            sourceId = book.sourceId,
            sourceName = "",
            url = book.tocUrl,
            latestChapter = "",
            updatedAt = System.currentTimeMillis()
        )

        val task = DownloadTask(
            id = UUID.randomUUID().toString(),
            bookId = book.id,
            bookTitle = book.title,
            author = book.author,
            mode = DownloadMode.Range,
            enqueuedAt = System.currentTimeMillis()
        ).apply {
            sourceSearchResult = searchResult
            rangeStartIndex = start
            rangeTakeCount = take
            isAutoPrefetch = true
            autoPrefetchReason = reason
        }

        autoPrefetchTaskByBook[book.id] = task.id
        downloadTaskList.add(0, task)
        applyTaskFilter()
        AppLogger.log("SearchDownload.AutoPrefetch.Enqueue",
            "bookId=${book.id}, taskId=${task.id}, reason=$reason, start=${start + 1}, take=$take")
        task.sourceSearchResult?.let { startDownload(task, it) }
    }

    fun queueDownloadTask(task: DownloadTask, searchResult: SearchResult) {
        downloadTaskList.add(0, task)
        attachTaskListener(task)
        applyTaskFilter()
        startDownload(task, searchResult)
    }

    private fun attachTaskListener(task: DownloadTask) {
        if (taskListeners.containsKey(task.id)) return
        val listener: (String) -> Unit = {
            applyTaskFilter()
        }
        task.addPropertyChangeListener(listener)
        taskListeners[task.id] = listener
    }

    private fun detachTaskListener(task: DownloadTask) {
        val listener = taskListeners.remove(task.id) ?: return
        task.removePropertyChangeListener(listener)
    }

    // ── Filter ──

    fun applyTaskFilter() {
        val status = mapFilterToStatus(taskFilterStatus)
        val filtered = if (status == null) {
            downloadTaskList.toList()
        } else {
            downloadTaskList.filter { it.currentStatus == status }
        }
        _filteredDownloadTasks.postValue(filtered)
        _downloadTasks.postValue(downloadTaskList.toList())
        _taskListVersion.postValue(++taskVersionCounter)
        updateActiveDownloadSummary()
    }

    private fun updateActiveDownloadSummary() {
        val active = downloadTaskList.count {
            it.currentStatus == DownloadTaskStatus.Downloading || it.currentStatus == DownloadTaskStatus.Queued
        }
        val summary = when {
            active > 0 -> "下载中 $active/${downloadTaskList.size}"
            downloadTaskList.isNotEmpty() -> "任务 ${downloadTaskList.size}"
            else -> ""
        }
        _activeDownloadSummary.postValue(summary)
    }

    // ── Private Helpers ──

    private fun hasActiveDuplicateSearchTask(target: SearchResult): Boolean {
        return downloadTaskList.any { t ->
            (t.currentStatus == DownloadTaskStatus.Queued || t.currentStatus == DownloadTaskStatus.Downloading)
                    && !t.isAutoPrefetch
                    && t.bookTitle.equals(target.title, ignoreCase = true)
                    && t.sourceSearchResult != null
                    && t.sourceSearchResult!!.sourceId == target.sourceId
        }
    }

    private fun removeAutoPrefetchTasksForBook(bookId: String, cancelReason: String) {
        if (bookId.isBlank()) return
        val staleTasks = downloadTaskList.filter { it.isAutoPrefetch && it.bookId.equals(bookId, ignoreCase = true) }
        for (stale in staleTasks) {
            downloadJobs[stale.id]?.cancel()
            stale.error = cancelReason
            AppLogger.log("SearchDownload.AutoPrefetch.Remove",
                "bookId=$bookId, taskId=${stale.id}, reason=$cancelReason, status=${stale.currentStatus}")
            downloadTaskList.remove(stale)
        }
        autoPrefetchTaskByBook.remove(bookId)
    }

    private fun removeConflictingTasksForBook(book: BookEntity, cancelReason: String, removeAllModes: Boolean) {
        if (book.id.isBlank() && (book.title.isBlank() || book.author.isBlank())) return
        val conflicts = downloadTaskList.filter { t -> isConflictingTask(t, book, removeAllModes) }
        for (task in conflicts) {
            downloadJobs[task.id]?.cancel()
            task.error = cancelReason
            AppLogger.log("SearchDownload.AutoPrefetch.RemoveConflict",
                "bookId=${book.id}, taskId=${task.id}, removeAllModes=$removeAllModes, reason=$cancelReason, status=${task.currentStatus}")
            downloadTaskList.remove(task)
            if (task.isAutoPrefetch && !task.bookId.isNullOrBlank()) {
                autoPrefetchTaskByBook.remove(task.bookId!!)
            }
        }
        applyTaskFilter()
    }

    private fun isConflictingTask(task: DownloadTask, book: BookEntity, removeAllModes: Boolean): Boolean {
        val sameBook = (!task.bookId.isNullOrBlank() && !book.id.isBlank()
                && task.bookId.equals(book.id, ignoreCase = true))
                || (task.bookId.isNullOrBlank()
                && task.bookTitle.equals(book.title, ignoreCase = true)
                && task.author.equals(book.author, ignoreCase = true))
        if (!sameBook) return false
        return if (removeAllModes) true else task.isAutoPrefetch
    }

    fun markBookshelfDirty() {
        parent.bookshelf.markBookshelfDirty()
    }
}
