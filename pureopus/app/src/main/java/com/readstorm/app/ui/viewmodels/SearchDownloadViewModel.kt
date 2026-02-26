package com.readstorm.app.ui.viewmodels

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import com.readstorm.app.application.abstractions.IBookRepository
import com.readstorm.app.domain.models.*
import com.readstorm.app.infrastructure.services.AppLogger
import kotlinx.coroutines.*
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap

class SearchDownloadViewModel(
    private val parent: MainViewModel,
    private val bookRepo: IBookRepository
) {
    // ── Observable Properties ──
    private val _searchKeyword = MutableLiveData("")
    val searchKeyword: LiveData<String> = _searchKeyword

    private val _isSearching = MutableLiveData(false)
    val isSearching: LiveData<Boolean> = _isSearching

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

    var selectedSourceId: Int = 0
    var taskFilterStatus: String = "全部"
        set(value) {
            field = value
            applyTaskFilter()
        }

    private val downloadTaskList = mutableListOf<DownloadTask>()
    private val downloadJobs = ConcurrentHashMap<String, Job>()
    private val pauseRequested = mutableSetOf<String>()
    private var searchJob: Job? = null

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

    // ── Search ──

    suspend fun search(keyword: String) {
        if (keyword.isBlank()) return

        searchJob?.cancel()
        try {
            _isSearching.postValue(true)
            _hasNoSearchResults.postValue(false)
            parent.setStatusMessage("搜索中...")

            val sourceId = if (selectedSourceId > 0) selectedSourceId else null
            val results = parent.searchBooksUseCase.execute(keyword, sourceId)
            _searchResults.postValue(results)

            if (results.isEmpty()) {
                parent.setStatusMessage("搜索完成：0 条结果。")
            } else {
                parent.setStatusMessage("搜索完成：共 ${results.size} 条")
            }
        } catch (e: CancellationException) {
            // Cancelled by next search, silent
        } catch (e: Exception) {
            parent.setStatusMessage("搜索失败：${e.message}")
        } finally {
            _isSearching.postValue(false)
            _hasNoSearchResults.postValue((_searchResults.value ?: emptyList()).isEmpty())
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
        applyTaskFilter()
        parent.setStatusMessage("已加入下载队列：《${task.bookTitle}》")

        startDownload(task, selected)
    }

    private fun startDownload(task: DownloadTask, selected: SearchResult) {
        val scope = CoroutineScope(Dispatchers.IO + SupervisorJob())
        val job = scope.launch {
            try {
                parent.downloadQueue.enqueue(selected.sourceId) {
                    parent.downloadBookUseCase.queue(task, selected, task.mode)
                }
                markBookshelfDirty()
            } catch (e: CancellationException) {
                if (task.status == DownloadTaskStatus.Downloading) {
                    task.transitionTo(DownloadTaskStatus.Paused)
                }
            } catch (e: Exception) {
                task.error = e.message ?: "未知错误"
                task.transitionTo(DownloadTaskStatus.Failed)
                parent.setStatusMessage("下载失败：${e.message}")
            } finally {
                applyTaskFilter()
            }
        }
        downloadJobs[task.id] = job
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
        downloadJobs[task.id]?.cancel()
        downloadTaskList.remove(task)
        applyTaskFilter()
        parent.setStatusMessage("已删除任务：《${task.bookTitle}》")
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

    // ── Auto Prefetch API ──

    fun queueOrReplaceAutoPrefetch(book: BookEntity, startIndex: Int, takeCount: Int, reason: String) {
        if (book.id.isBlank() || book.tocUrl.isBlank()) return

        val start = maxOf(0, startIndex)
        val take = maxOf(1, takeCount)

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

        downloadTaskList.add(0, task)
        applyTaskFilter()
        AppLogger.log("SearchDownload", "AutoPrefetch enqueued: bookId=${book.id}, reason=$reason, start=${start + 1}, take=$take")
        task.sourceSearchResult?.let { startDownload(task, it) }
    }

    fun queueDownloadTask(task: DownloadTask, searchResult: SearchResult) {
        downloadTaskList.add(0, task)
        applyTaskFilter()
        startDownload(task, searchResult)
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

    fun markBookshelfDirty() {
        parent.bookshelf.markBookshelfDirty()
    }
}
