package com.readstorm.app.ui.viewmodels

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import com.readstorm.app.application.abstractions.IBookRepository
import com.readstorm.app.domain.models.*
import com.readstorm.app.infrastructure.services.AppLogger
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.GlobalScope
import kotlinx.coroutines.launch
import kotlin.math.max
import kotlin.math.min

class ReaderViewModel(
    private val parent: MainViewModel,
    private val bookRepo: IBookRepository
) {
    // ── Reader State ──
    private val _readerTitle = MutableLiveData("")
    val readerTitle: LiveData<String> = _readerTitle

    private val _readerParagraphs = MutableLiveData<List<String>>(emptyList())
    val readerParagraphs: LiveData<List<String>> = _readerParagraphs

    private val _readerProgressDisplay = MutableLiveData("")
    val readerProgressDisplay: LiveData<String> = _readerProgressDisplay

    private val _pageProgressDisplay = MutableLiveData("")
    val pageProgressDisplay: LiveData<String> = _pageProgressDisplay

    private val _readerChapters = MutableLiveData<List<ReaderChapterItem>>(emptyList())
    val readerChapters: LiveData<List<ReaderChapterItem>> = _readerChapters

    private val _readerBookmarks = MutableLiveData<List<ReadingBookmarkEntity>>(emptyList())
    val readerBookmarks: LiveData<List<ReadingBookmarkEntity>> = _readerBookmarks

    private val _isTocOverlayVisible = MutableLiveData(false)
    val isTocOverlayVisible: LiveData<Boolean> = _isTocOverlayVisible

    private val _isBookmarkPanelVisible = MutableLiveData(false)
    val isBookmarkPanelVisible: LiveData<Boolean> = _isBookmarkPanelVisible

    private val _isSourceSwitching = MutableLiveData(false)
    val isSourceSwitching: LiveData<Boolean> = _isSourceSwitching

    private val _isCurrentPageBookmarked = MutableLiveData(false)
    val isCurrentPageBookmarked: LiveData<Boolean> = _isCurrentPageBookmarked

    private val _currentChapterTitleDisplay = MutableLiveData("")
    val currentChapterTitleDisplay: LiveData<String> = _currentChapterTitleDisplay

    // ── Selected Book ──
    var selectedDbBook: BookEntity? = null
        private set

    // ── Chapter Data ──
    private var currentBookChapters = listOf<Pair<String, String>>() // (title, content)
    private var readerCurrentChapterIndex = 0

    // ── Pagination ──
    private var allPageLines = listOf<List<String>>() // All pages of current chapter
    private var currentPageIndex = 0
    private var totalPages = 0

    // ── Reader Settings (from Settings sync) ──
    var readerFontSize: Int = 20
    var readerLineHeight: Int = 36
    var readerParagraphSpacing: Int = 0
    var readerBackground: String = "#FFFBF0"
    var readerForeground: String = "#1E293B"
    var isDarkMode: Boolean = false
    var readerExtendIntoCutout: Boolean = false
    var readerContentMaxWidth: Int = 0
    var readerUseVolumeKeyPaging: Boolean = false
    var readerUseSwipePaging: Boolean = false
    var readerHideSystemStatusBar: Boolean = false
    var readerTopReservePx: Double = 4.0
    var readerBottomReservePx: Double = 0.0
    var readerBottomStatusBarReservePx: Double = 0.0
    var readerHorizontalInnerReservePx: Double = 0.0
    var readerSidePaddingPx: Double = 12.0
    var selectedFontName: String = "默认"

    // ── Auto Prefetch Settings ──
    var readerAutoPrefetchEnabled: Boolean = true
    var readerPrefetchBatchSize: Int = 10
    var readerPrefetchLowWatermark: Int = 4

    // ── Viewport Size ──
    private var viewportWidth: Double = 0.0
    private var viewportHeight: Double = 0.0

    // ── Paper Presets ──
    val paperPresets = PaperPreset.defaults

    // ── Open Book ──

    suspend fun openBook(book: BookEntity) {
        selectedDbBook = book
        _readerTitle.postValue(book.title)
        parent.setStatusMessage("正在加载《${book.title}》…")

        try {
            val chapters = bookRepo.getChapters(book.id)
            currentBookChapters = chapters.sortedBy { it.indexNo }.map { it.title to (it.content ?: "") }

            // Build TOC
            val tocItems = chapters.sortedBy { it.indexNo }.mapIndexed { index, ch ->
                val prefix = if (ch.status == ChapterStatus.Done) "" else "[未下载] "
                ReaderChapterItem(ch.indexNo, ch.title, "$prefix${ch.title}")
            }
            _readerChapters.postValue(tocItems)

            // Load reading state
            val state = bookRepo.getReadingState(book.id)
            readerCurrentChapterIndex = state?.chapterIndex ?: book.readChapterIndex
            currentPageIndex = state?.pageIndex ?: 0

            // Load bookmarks
            loadBookmarks(book.id)

            // Navigate to current chapter
            navigateToChapter(readerCurrentChapterIndex)

            // Update read timestamp
            bookRepo.updateReadProgress(book.id, readerCurrentChapterIndex,
                currentBookChapters.getOrNull(readerCurrentChapterIndex)?.first ?: "")

            parent.setStatusMessage("《${book.title}》加载完成（${currentBookChapters.size} 章）")
        } catch (e: Exception) {
            parent.setStatusMessage("加载书籍失败：${e.message}")
            AppLogger.log("Reader", "openBook error: ${e.message}")
        }
    }

    // ── Chapter Navigation ──

    fun navigateToChapter(chapterIndex: Int) {
        val idx = chapterIndex.coerceIn(0, currentBookChapters.size - 1)
        if (currentBookChapters.isEmpty()) return

        readerCurrentChapterIndex = idx
        currentPageIndex = 0
        val (title, content) = currentBookChapters[idx]

        _currentChapterTitleDisplay.postValue(title)

        // Simple pagination: split content into paragraphs
        val paragraphs = if (content.isBlank()) {
            listOf("[本章内容尚未下载]")
        } else {
            content.split("\n").filter { it.isNotBlank() }
        }

        // For now, show all paragraphs as a single page (full pagination requires viewport measurement)
        allPageLines = listOf(paragraphs)
        totalPages = 1

        _readerParagraphs.postValue(paragraphs)
        updateProgressDisplay()
        checkCurrentPageBookmark()

        // Save reading state
        saveReadingState()
    }

    fun nextPage() {
        if (currentPageIndex < totalPages - 1) {
            currentPageIndex++
            showCurrentPage()
        } else {
            nextChapter()
        }
    }

    fun previousPage() {
        if (currentPageIndex > 0) {
            currentPageIndex--
            showCurrentPage()
        } else {
            previousChapter()
        }
    }

    fun nextChapter() {
        if (readerCurrentChapterIndex < currentBookChapters.size - 1) {
            navigateToChapter(readerCurrentChapterIndex + 1)
        } else {
            parent.setStatusMessage("已经是最后一章。")
        }
    }

    fun previousChapter() {
        if (readerCurrentChapterIndex > 0) {
            navigateToChapter(readerCurrentChapterIndex - 1)
        } else {
            parent.setStatusMessage("已经是第一章。")
        }
    }

    fun selectTocChapter(indexNo: Int) {
        val targetIdx = currentBookChapters.indices.firstOrNull { idx ->
            (_readerChapters.value?.getOrNull(idx)?.indexNo ?: -1) == indexNo
        } ?: return
        navigateToChapter(targetIdx)
        _isTocOverlayVisible.postValue(false)
    }

    // ── Viewport ──

    fun updateViewportSize(width: Double, height: Double) {
        viewportWidth = width
        viewportHeight = height
        // Recalculate pagination when viewport changes
        if (currentBookChapters.isNotEmpty()) {
            repaginate()
        }
    }

    private fun repaginate() {
        // Calculate lines per page based on viewport height and line height
        val availableHeight = viewportHeight - readerTopReservePx - readerBottomReservePx - readerBottomStatusBarReservePx - 40 // status bar
        val effectiveLineHeight = readerLineHeight * 1.12
        val linesPerPage = max(3, (availableHeight / effectiveLineHeight).toInt())

        val (_, content) = currentBookChapters.getOrNull(readerCurrentChapterIndex) ?: return
        val paragraphs = if (content.isBlank()) {
            listOf("[本章内容尚未下载]")
        } else {
            content.split("\n").filter { it.isNotBlank() }
        }

        // Calculate chars per line based on viewport width
        val availableWidth = viewportWidth - readerSidePaddingPx * 2 - readerHorizontalInnerReservePx
        val charsPerLine = max(6, (availableWidth / (readerFontSize * 0.55)).toInt())

        // Word wrap and paginate
        val wrappedLines = mutableListOf<String>()
        paragraphs.forEach { para ->
            val lines = wrapText(para, charsPerLine)
            wrappedLines.addAll(lines)
        }

        // Split into pages
        val pages = mutableListOf<List<String>>()
        var i = 0
        while (i < wrappedLines.size) {
            val end = min(i + linesPerPage, wrappedLines.size)
            pages.add(wrappedLines.subList(i, end).toList())
            i = end
        }

        if (pages.isEmpty()) pages.add(listOf("[空白页]"))

        allPageLines = pages
        totalPages = pages.size
        currentPageIndex = currentPageIndex.coerceIn(0, totalPages - 1)
        showCurrentPage()
    }

    private fun wrapText(text: String, charsPerLine: Int): List<String> {
        if (text.length <= charsPerLine) return listOf(text)
        val lines = mutableListOf<String>()
        var remaining = text
        while (remaining.isNotEmpty()) {
            val take = min(charsPerLine, remaining.length)
            lines.add(remaining.substring(0, take))
            remaining = remaining.substring(take)
        }
        return lines
    }

    private fun showCurrentPage() {
        if (allPageLines.isEmpty()) return
        val page = allPageLines[currentPageIndex.coerceIn(0, allPageLines.size - 1)]
        _readerParagraphs.postValue(page)
        updateProgressDisplay()
        checkCurrentPageBookmark()
        saveReadingState()
    }

    // ── Progress Display ──

    private fun updateProgressDisplay() {
        val chapterProgress = if (currentBookChapters.isNotEmpty()) {
            "${readerCurrentChapterIndex + 1}/${currentBookChapters.size}"
        } else "0/0"
        _readerProgressDisplay.postValue(chapterProgress)

        val pageProgress = if (totalPages > 1) {
            "${currentPageIndex + 1}/$totalPages"
        } else "1/1"
        _pageProgressDisplay.postValue(pageProgress)
    }

    // ── TOC / Bookmark Panel ──

    fun toggleTocOverlay() {
        val current = _isTocOverlayVisible.value ?: false
        _isTocOverlayVisible.postValue(!current)
        if (!current) {
            _isBookmarkPanelVisible.postValue(false)
        }
    }

    fun showTocPanel() {
        _isBookmarkPanelVisible.postValue(false)
    }

    fun showBookmarkPanel() {
        _isBookmarkPanelVisible.postValue(true)
    }

    // ── Bookmarks ──

    suspend fun toggleBookmark() {
        val book = selectedDbBook ?: return
        val isBookmarked = _isCurrentPageBookmarked.value ?: false

        if (isBookmarked) {
            bookRepo.deleteReadingBookmark(book.id, readerCurrentChapterIndex, currentPageIndex)
        } else {
            val chapterTitle = currentBookChapters.getOrNull(readerCurrentChapterIndex)?.first ?: ""
            val previewText = (_readerParagraphs.value ?: emptyList()).take(2).joinToString(" ")
            val bookmark = ReadingBookmarkEntity(
                bookId = book.id,
                chapterIndex = readerCurrentChapterIndex,
                pageIndex = currentPageIndex,
                chapterTitle = chapterTitle,
                previewText = previewText.take(100),
                anchorText = null,
                createdAt = System.currentTimeMillis()
            )
            bookRepo.upsertReadingBookmark(bookmark)
        }

        loadBookmarks(book.id)
        checkCurrentPageBookmark()
    }

    fun jumpToBookmark(bookmark: ReadingBookmarkEntity) {
        navigateToChapter(bookmark.chapterIndex)
        currentPageIndex = bookmark.pageIndex.coerceIn(0, max(0, totalPages - 1))
        showCurrentPage()
        _isTocOverlayVisible.postValue(false)
    }

    suspend fun removeBookmark(bookmark: ReadingBookmarkEntity) {
        val book = selectedDbBook ?: return
        bookRepo.deleteReadingBookmark(book.id, bookmark.chapterIndex, bookmark.pageIndex)
        loadBookmarks(book.id)
        checkCurrentPageBookmark()
    }

    private suspend fun loadBookmarks(bookId: String) {
        try {
            val bookmarks = bookRepo.getReadingBookmarks(bookId)
            _readerBookmarks.postValue(bookmarks)
        } catch (e: Exception) {
            AppLogger.log("Reader", "loadBookmarks error: ${e.message}")
        }
    }

    private fun checkCurrentPageBookmark() {
        val bookmarks = _readerBookmarks.value ?: emptyList()
        val isBookmarked = bookmarks.any {
            it.chapterIndex == readerCurrentChapterIndex && it.pageIndex == currentPageIndex
        }
        _isCurrentPageBookmarked.postValue(isBookmarked)
    }

    // ── Reading State Persistence ──

    private fun saveReadingState() {
        val book = selectedDbBook ?: return
        val state = ReadingStateEntity(
            bookId = book.id,
            chapterIndex = readerCurrentChapterIndex,
            pageIndex = currentPageIndex,
            anchorText = null,
            layoutFingerprint = "${readerFontSize}_${readerLineHeight}_${viewportWidth.toInt()}x${viewportHeight.toInt()}",
            updatedAt = System.currentTimeMillis()
        )
        // Fire and forget - save in background
        try {
            GlobalScope.launch(Dispatchers.IO) {
                bookRepo.upsertReadingState(state)
                bookRepo.updateReadProgress(book.id, readerCurrentChapterIndex,
                    currentBookChapters.getOrNull(readerCurrentChapterIndex)?.first ?: "")
            }
        } catch (_: Exception) { }
    }

    // ── Reader Settings ──

    fun applyPaperPreset(preset: PaperPreset) {
        readerBackground = preset.background
        readerForeground = preset.foreground
        isDarkMode = preset.background.startsWith("#1") || preset.background.startsWith("#0")
        parent.settings.queueAutoSaveSettings()
    }

    // ── Refresh from download signal ──

    suspend fun refreshCurrentBookFromDownloadSignal(bookId: String?) {
        if (bookId.isNullOrBlank()) return
        val book = selectedDbBook ?: return
        if (!book.id.equals(bookId, ignoreCase = true)) return

        try {
            val chapters = bookRepo.getChapters(book.id)
            currentBookChapters = chapters.sortedBy { it.indexNo }.map { it.title to (it.content ?: "") }

            // Refresh TOC
            val tocItems = chapters.sortedBy { it.indexNo }.mapIndexed { _, ch ->
                val prefix = if (ch.status == ChapterStatus.Done) "" else "[未下载] "
                ReaderChapterItem(ch.indexNo, ch.title, "$prefix${ch.title}")
            }
            _readerChapters.postValue(tocItems)

            // Re-render current page
            if (readerCurrentChapterIndex < currentBookChapters.size) {
                navigateToChapter(readerCurrentChapterIndex)
            }
        } catch (e: Exception) {
            AppLogger.log("Reader", "refreshCurrentBookFromDownloadSignal error: ${e.message}")
        }
    }

    fun needsCurrentChapterForegroundRefresh(): Boolean {
        if (currentBookChapters.isEmpty()) return false
        val (_, content) = currentBookChapters.getOrNull(readerCurrentChapterIndex) ?: return false
        return content.isBlank()
    }

    fun refreshSortedSwitchSources() {
        // Source switching: show healthy sources first in potential source list
        // This is used when reader needs to switch to a different source for a chapter
    }
}
