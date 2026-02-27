package com.readstorm.app.ui.viewmodels

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import com.readstorm.app.application.abstractions.IAppSettingsUseCase
import com.readstorm.app.application.abstractions.IBookRepository
import com.readstorm.app.domain.models.*
import com.readstorm.app.infrastructure.services.AppLogger
import com.readstorm.app.infrastructure.services.EpubExporter
import com.readstorm.app.infrastructure.services.WorkDirectoryManager
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.io.File
import java.util.UUID

class BookshelfViewModel(
    private val parent: MainViewModel,
    private val bookRepo: IBookRepository,
    private val appSettingsUseCase: IAppSettingsUseCase
) {
    // ── Observable State ──
    private val _dbBooks = MutableLiveData<List<BookEntity>>(emptyList())
    val dbBooks: LiveData<List<BookEntity>> = _dbBooks

    private val _filteredDbBooks = MutableLiveData<List<BookEntity>>(emptyList())
    val filteredDbBooks: LiveData<List<BookEntity>> = _filteredDbBooks

    var bookshelfFilterText: String = ""
        set(value) {
            field = value
            applyBookshelfFilter()
        }

    var bookshelfSortMode: String = "最近阅读"
        set(value) {
            field = value
            applyBookshelfFilter()
        }

    private var bookshelfDirty = true
    private var lastBookshelfRefreshAt = 0L
    private val refreshLock = Mutex()

    /** 待确认删除的书籍 — UI 层观察此值弹出确认对话框 */
    private val _pendingDeleteBook = MutableLiveData<BookEntity?>(null)
    val pendingDeleteBook: LiveData<BookEntity?> = _pendingDeleteBook

    companion object {
        val SORT_OPTIONS = listOf("最近阅读", "书名", "作者", "下载进度")
    }

    // ── Init ──

    suspend fun init() {
        refreshDbBooks()
        // Auto resume incomplete downloads if enabled
        try {
            val settings = appSettingsUseCase.load()
            if (settings.autoResumeAndRefreshOnStartup) {
                resumeIncompleteDownloads()
            }
        } catch (e: Exception) {
            AppLogger.log("Bookshelf", "Init error: ${e.message}")
        }
        // Auto-fetch missing covers in background
        CoroutineScope(Dispatchers.IO + SupervisorJob()).launch {
            autoFetchMissingCovers()
        }
    }

    // ── Refresh ──

    suspend fun refreshDbBooks() {
        refreshLock.withLock {
            try {
                val books = bookRepo.getAllBooks()
                val sorted = books.sortedWith(
                    compareByDescending<BookEntity> { it.readAt }
                        .thenByDescending { it.createdAt }
                )

                // Get active download tasks for IsDownloading status
                val activeTasks = parent.searchDownload.downloadTasks.value ?: emptyList()

                sorted.forEach { b ->
                    if (b.doneChapters > b.totalChapters) {
                        b.totalChapters = b.doneChapters
                    }
                    // Set IsDownloading flag based on active download tasks
                    b.isDownloading = activeTasks.any { t ->
                        t.bookId == b.id &&
                        (t.currentStatus == DownloadTaskStatus.Queued || t.currentStatus == DownloadTaskStatus.Downloading)
                    }
                }

                _dbBooks.postValue(sorted)
                bookshelfDirty = false
                lastBookshelfRefreshAt = System.currentTimeMillis()
                applyBookshelfFilterCore(sorted)
            } catch (e: Exception) {
                parent.setStatusMessage("书架刷新失败：${e.message}")
            }
        }
    }

    suspend fun refreshDbBooksIfNeeded(force: Boolean = false) {
        val tooSoon = System.currentTimeMillis() - lastBookshelfRefreshAt < 1000
        if (!force && !bookshelfDirty && tooSoon) return
        refreshDbBooks()
    }

    fun markBookshelfDirty() {
        bookshelfDirty = true
    }

    // ── Filter & Sort ──

    fun applyBookshelfFilter() {
        applyBookshelfFilterCore(_dbBooks.value ?: emptyList())
    }

    private fun applyBookshelfFilterCore(source: List<BookEntity>) {
        var filtered = source.toList()

        // Search filter
        val keyword = bookshelfFilterText.trim()
        if (keyword.isNotEmpty()) {
            filtered = filtered.filter { book ->
                book.title.contains(keyword, ignoreCase = true) ||
                book.author.contains(keyword, ignoreCase = true)
            }
        }

        // Sort
        filtered = when (bookshelfSortMode) {
            "书名" -> filtered.sortedBy { it.title.lowercase() }
            "作者" -> filtered.sortedBy { it.author.lowercase() }
            "下载进度" -> filtered.sortedByDescending { it.progressPercent }
            else -> filtered.sortedWith(
                compareByDescending<BookEntity> { it.readAt }
                    .thenByDescending { it.createdAt }
            )
        }

        _filteredDbBooks.postValue(filtered)
    }

    // ── Commands ──

    suspend fun openDbBook(book: BookEntity) {
        try {
            parent.reader.openBook(book)
            parent.setReaderTabVisible(true)
            parent.setSelectedTabIndex(TabIndex.READER)
        } catch (e: Exception) {
            parent.setStatusMessage("打开失败：${e.message}")
        }
    }

    suspend fun resumeBookDownload(book: BookEntity) {
        if (book.isComplete) {
            parent.setStatusMessage("《${book.title}》已全部下载完成，无需续传。")
            return
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
            mode = DownloadMode.FullBook,
            enqueuedAt = System.currentTimeMillis()
        ).apply {
            sourceSearchResult = searchResult
        }

        parent.searchDownload.queueDownloadTask(task, searchResult)
        parent.setStatusMessage("继续下载：《${book.title}》（剩余 ${book.totalChapters - book.doneChapters} 章）")
    }

    suspend fun exportDbBook(book: BookEntity) {
        try {
            val doneContents = bookRepo.getDoneChapterContents(book.id)
            if (doneContents.isEmpty()) {
                parent.setStatusMessage("《${book.title}》尚无已下载章节，无法导出。")
                return
            }

            val context = parent.getApplication<android.app.Application>()
            val workDir = WorkDirectoryManager.getDefaultWorkDirectory(context)
            val settings = appSettingsUseCase.load()

            if (settings.exportFormat == "epub") {
                // EPUB export
                val chapters = doneContents.map { it.title to (it.content ?: "") }
                val outputPath = EpubExporter.export(workDir, book.title, book.author, book.sourceId, chapters)
                val exported = WorkDirectoryManager.exportToPublicDownloads(
                    context = context,
                    sourceFile = File(outputPath),
                    displayName = File(outputPath).name,
                    subDir = "ReadStorm"
                )
                parent.setStatusMessage("EPUB 已导出到系统下载目录：$exported（${doneContents.size} 章）")
            } else {
                // TXT export (default)
                val downloadPath = File(workDir, "downloads").also { it.mkdirs() }
                val safeName = "${book.title}(${book.author}).txt"
                    .replace(Regex("[/\\\\:*?\"<>|]"), "_")
                val outputFile = File(downloadPath, safeName)

                outputFile.bufferedWriter(Charsets.UTF_8).use { writer ->
                    writer.appendLine("书名：${book.title}")
                    writer.appendLine("作者：${book.author}")
                    writer.appendLine("已下载：${doneContents.size}/${book.totalChapters} 章")
                    writer.newLine()

                    doneContents.forEach { chapter ->
                        writer.appendLine(chapter.title)
                        writer.newLine()
                        writer.appendLine(chapter.content ?: "")
                        writer.newLine()
                    }
                }

                val exported = WorkDirectoryManager.exportToPublicDownloads(
                    context = context,
                    sourceFile = outputFile,
                    displayName = outputFile.name,
                    subDir = "ReadStorm"
                )
                parent.setStatusMessage("TXT 已导出到系统下载目录：$exported（${doneContents.size} 章）")
            }
        } catch (e: Exception) {
            parent.setStatusMessage("导出失败：${e.message}")
        }
    }

    suspend fun removeDbBook(book: BookEntity) {
        // 触发确认事件，UI 层观察 pendingDeleteBook 弹出对话框
        _pendingDeleteBook.postValue(book)
    }

    /** UI 层确认删除后调用 */
    suspend fun confirmRemoveDbBook() {
        val book = _pendingDeleteBook.value ?: return
        _pendingDeleteBook.postValue(null)
        try {
            bookRepo.deleteBook(book.id)
            val current = _dbBooks.value?.toMutableList() ?: mutableListOf()
            current.removeAll { it.id == book.id }
            _dbBooks.postValue(current)
            applyBookshelfFilter()
            parent.setStatusMessage("已从书架移除：《${book.title}》")
        } catch (e: Exception) {
            parent.setStatusMessage("移除失败：${e.message}")
        }
    }

    /** UI 层取消删除后调用 */
    fun cancelRemoveDbBook() {
        _pendingDeleteBook.postValue(null)
    }

    suspend fun checkNewChapters(book: BookEntity) {
        try {
            parent.setStatusMessage("正在检查《${book.title}》的新章节…")
            val newCount = parent.downloadBookUseCase.checkNewChapters(book)
            if (newCount > 0) {
                parent.setStatusMessage("《${book.title}》发现 $newCount 个新章节，正在自动续传…")
                refreshDbBooks()
                resumeBookDownload(book)
            } else {
                parent.setStatusMessage("《${book.title}》目录无更新。")
            }
        } catch (e: Exception) {
            parent.setStatusMessage("检查新章节失败：${e.message}")
        }
    }

    suspend fun checkAllNewChapters() {
        val books = _dbBooks.value ?: emptyList()
        if (books.isEmpty()) {
            parent.setStatusMessage("书架为空，无需检查。")
            return
        }

        parent.setStatusMessage("正在检查所有书籍的新章节…")
        var totalNew = 0
        var checkedCount = 0
        var failedCount = 0

        for (book in books) {
            try {
                val newCount = parent.downloadBookUseCase.checkNewChapters(book)
                if (newCount > 0) {
                    totalNew += newCount
                    resumeBookDownload(book)
                }
                checkedCount++
            } catch (_: Exception) {
                failedCount++
            }
        }

        refreshDbBooks()
        val failedInfo = if (failedCount > 0) "，$failedCount 本检查失败" else ""
        if (totalNew > 0) {
            parent.setStatusMessage("全部检查完成，共发现 $totalNew 个新章节，已自动续传。")
        } else {
            parent.setStatusMessage("全部检查完成，没有发现新章节。$failedInfo")
        }
    }

    suspend fun refreshCover(book: BookEntity) {
        try {
            parent.setStatusMessage("正在刷新《${book.title}》的封面…")
            val result = parent.coverService.refreshCover(book)
            parent.setStatusMessage(result)
            refreshDbBooks()
        } catch (e: Exception) {
            parent.setStatusMessage("封面刷新失败：${e.message}")
        }
    }

    // ── Private Helpers ──

    /** Auto-fetch covers for books missing cover images */
    private suspend fun autoFetchMissingCovers() {
        try {
            val booksNeedCover = (_dbBooks.value ?: emptyList())
                .filter { !it.hasCover && it.tocUrl.isNotBlank() }
            if (booksNeedCover.isEmpty()) return

            parent.setStatusMessage("正在为 ${booksNeedCover.size} 本书补抓封面…")
            for (book in booksNeedCover) {
                try {
                    parent.coverService.refreshCover(book)
                } catch (_: Exception) { /* single failure doesn't block others */ }
            }
            refreshDbBooks()
            parent.setStatusMessage("封面补抓完成（共 ${booksNeedCover.size} 本）。")
        } catch (_: Exception) { /* don't let cover fetch break init */ }
    }

    private suspend fun resumeIncompleteDownloads() {
        try {
            val books = _dbBooks.value ?: return
            val incomplete = books.filter { !it.isComplete }
            if (incomplete.isEmpty()) return

            parent.setStatusMessage("正在恢复 ${incomplete.size} 个未完成的下载任务…")

            incomplete.forEach { book ->
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
                    mode = DownloadMode.FullBook,
                    enqueuedAt = System.currentTimeMillis()
                ).apply {
                    sourceSearchResult = searchResult
                }
                parent.searchDownload.queueDownloadTask(task, searchResult)
            }

            parent.setStatusMessage("已恢复 ${incomplete.size} 个未完成的下载任务。")
        } catch (e: Exception) {
            parent.setStatusMessage("恢复下载任务失败：${e.message}")
        }
    }
}
