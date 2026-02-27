package com.readstorm.app.ui.viewmodels

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import com.readstorm.app.application.abstractions.IBookRepository
import com.readstorm.app.domain.models.*
import com.readstorm.app.infrastructure.services.AppLogger
import android.content.Intent
import android.graphics.Typeface
import android.net.Uri
import kotlinx.coroutines.*
import kotlin.math.abs
import kotlin.math.floor
import kotlin.math.max
import kotlin.math.roundToInt

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

    private val _sortedSwitchSources = MutableLiveData<List<SourceItem>>(emptyList())
    val sortedSwitchSources: LiveData<List<SourceItem>> = _sortedSwitchSources

    // ── Selected Book ──
    var selectedDbBook: BookEntity? = null
        private set

    // ── Chapter Data ──
    private var currentBookChapters = mutableListOf<Pair<String, String>>() // (title, displayContent)
    private var readerCurrentChapterIndex = 0

    // ── Pagination (CJK width model) ──
    private var _chapterPages = mutableListOf<List<String>>()
    private var currentPageIndex = 0
    private var totalPages = 0
    private var _linesPerPage = 20
    private var _effectiveLineHeight = 30.0

    // ── Navigation Flags ──
    private var _suppressReaderIndexChangedNavigation = false
    private var _isApplyingPersistedState = false
    private var _pendingRefreshWhenTocClosed = false

    // ── Debounce ──
    private var readingStateSaveCts: Job? = null
    private var viewportRebuildJob: Job? = null
    private val coroutineScope = CoroutineScope(Dispatchers.Main + SupervisorJob())

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

    // ── Font Map ──
    private val _readerTypeface = MutableLiveData<Typeface>(Typeface.DEFAULT)
    val readerTypeface: LiveData<Typeface> = _readerTypeface

    val fontNames: List<String> get() = FONT_MAP.keys.toList()

    // ── Auto Prefetch Settings ──
    var readerAutoPrefetchEnabled: Boolean = true
    var readerPrefetchBatchSize: Int = 10
    var readerPrefetchLowWatermark: Int = 4

    // ── Viewport Size ──
    private var viewportWidth: Double = 0.0
    private var viewportHeight: Double = 0.0

    // ── Paper Presets ──
    val paperPresets = PaperPreset.defaults

    companion object {
        private const val EFFECTIVE_LINE_HEIGHT_FACTOR = 1.12
        private const val MIN_CHARS_PER_LINE = 6
        private const val VIEWPORT_REBUILD_DEBOUNCE_MS = 500L
        private const val READING_STATE_SAVE_DEBOUNCE_MS = 400L

        /** Android 常见中文字体映射 */
        private val FONT_MAP: Map<String, Typeface> = linkedMapOf(
            "默认" to Typeface.DEFAULT,
            "Serif" to Typeface.SERIF,
            "Sans-Serif" to Typeface.SANS_SERIF,
            "Monospace" to Typeface.MONOSPACE,
            "宋体" to tryCreateTypeface("NotoSerifCJK-Regular.ttc", Typeface.SERIF),
            "黑体" to tryCreateTypeface("NotoSansCJK-Regular.ttc", Typeface.DEFAULT),
            "楷体" to tryCreateTypeface("DroidSansFallback.ttf", Typeface.SERIF),
        )

        private fun tryCreateTypeface(fontFileName: String, fallback: Typeface): Typeface {
            return try {
                Typeface.create(fontFileName, Typeface.NORMAL) ?: fallback
            } catch (_: Exception) {
                fallback
            }
        }
    }

    // ==================== CJK Width Model ====================

    /** 宽度单位：全角=1.0，半角=0.5 */
    private fun getCharWidthUnits(ch: Char): Double {
        if (ch.code <= 0x007F) return 0.5
        if (ch in '\uFF61'..'\uFF9F') return 0.5
        return 1.0
    }

    private data class ParagraphUnit(val text: String, val isParagraphBreak: Boolean)

    /** 识别逻辑段落：空行后、显式缩进或首行为段落起点 */
    private fun buildLogicalParagraphs(content: String): List<ParagraphUnit> {
        val lines = content.split('\n')
        val list = mutableListOf<ParagraphUnit>()
        var previousWasBlank = true
        for (raw in lines) {
            val line = raw.trimEnd('\r')
            if (line.isBlank()) { previousWasBlank = true; continue }
            val trimmed = line.trim()
            val hasIndent = raw.startsWith("\u3000\u3000") || (raw.isNotEmpty() && raw[0].isWhitespace())
            val isParagraphBreak = previousWasBlank || hasIndent || list.isEmpty()
            list.add(ParagraphUnit(trimmed, isParagraphBreak))
            previousWasBlank = false
        }
        return if (list.isEmpty()) listOf(ParagraphUnit("", true)) else list
    }

    /** 按CJK宽度模型折行 */
    private fun wrapParagraphToLines(paragraph: String, charsPerLine: Int): List<String> {
        if (paragraph.isEmpty()) return listOf("")
        if (charsPerLine <= 0) return listOf(paragraph)
        val result = mutableListOf<String>()
        var index = 0
        while (index < paragraph.length) {
            val lineStart = index
            var usedUnits = 0.0
            while (index < paragraph.length) {
                val w = getCharWidthUnits(paragraph[index])
                if (usedUnits + w > charsPerLine) break
                usedUnits += w
                index++
            }
            if (index == lineStart) index++
            result.add(paragraph.substring(lineStart, index))
        }
        return result
    }

    /** 构建当前章节的完整显示行 */
    private fun buildDisplayLinesForCurrentChapter(charsPerLine: Int, includeHeader: Boolean): List<String> {
        val result = mutableListOf<String>()
        val content = currentBookChapters.getOrNull(readerCurrentChapterIndex)?.second ?: return result
        if (includeHeader) {
            val chapterTitle = currentBookChapters.getOrNull(readerCurrentChapterIndex)?.first
            if (!chapterTitle.isNullOrBlank()) {
                result.add("\u231C${chapterTitle}\u231D")
                result.add("")
            }
        }
        val paragraphs = buildLogicalParagraphs(content)
        val spacingLines = if (_effectiveLineHeight > 0)
            max(0, (readerParagraphSpacing.toDouble() / _effectiveLineHeight).roundToInt()) else 0
        for (i in paragraphs.indices) {
            val unit = paragraphs[i]
            val raw = unit.text
            val paragraph = when {
                raw.startsWith("\u3000\u3000") -> raw
                unit.isParagraphBreak -> "\u3000\u3000$raw"
                else -> raw
            }
            result.addAll(wrapParagraphToLines(paragraph, charsPerLine))
            if (spacingLines > 0 && unit.isParagraphBreak && i < paragraphs.size - 1) {
                repeat(spacingLines) { result.add("") }
            }
        }
        return result
    }

    /** 估算每行可容纳的CJK字符数 */
    private fun estimateCharsPerLine(): Int {
        val contentMaxWidth = if (readerContentMaxWidth > 0) readerContentMaxWidth.toDouble() else 860.0
        var availableWidth = minOf(if (viewportWidth > 0) viewportWidth else 800.0, contentMaxWidth)
        availableWidth -= readerSidePaddingPx * 2 + max(0.0, readerHorizontalInnerReservePx)
        if (availableWidth < 100) availableWidth = 100.0
        val hanCharWidth = if (readerFontSize > 0) readerFontSize.toDouble() else 14.0
        val rawChars = floor(availableWidth / hanCharWidth).toInt()
        return max(MIN_CHARS_PER_LINE, rawChars)
    }

    /** 1.5倍规则：保守行容量 */
    private fun calculateConservativeLineCapacity(availableHeight: Double, lineHeight: Double): Int {
        var lines = 0; var used = 0.0
        while (true) {
            val nextUsed = used + lineHeight
            if (nextUsed > availableHeight) break
            lines++; used = nextUsed
            if (availableHeight - used < 1.5 * lineHeight) break
        }
        return max(1, lines)
    }

    /** 根据视口和排版参数计算每页行数 */
    private fun recalculateLinesPerPage() {
        val bottomReserve = max(0.0, readerBottomStatusBarReservePx)
        val availableHeight = viewportHeight - readerTopReservePx - readerBottomReservePx - bottomReserve - 40
        val safeHeight = if (availableHeight <= 0) 400.0 else availableHeight
        _effectiveLineHeight = max(
            if (readerLineHeight > 0) readerLineHeight.toDouble() else 30.0,
            (if (readerFontSize > 0) readerFontSize.toDouble() else 15.0) * EFFECTIVE_LINE_HEIGHT_FACTOR
        )
        if (safeHeight <= _effectiveLineHeight) { _linesPerPage = 1; return }
        _linesPerPage = calculateConservativeLineCapacity(safeHeight, _effectiveLineHeight)
    }

    /** 将当前章节内容按行容量分页 */
    private fun rebuildChapterPages() {
        _chapterPages.clear()
        val content = currentBookChapters.getOrNull(readerCurrentChapterIndex)?.second
        if (content.isNullOrEmpty() || _linesPerPage <= 0) { totalPages = 0; return }
        val displayLines = buildDisplayLinesForCurrentChapter(estimateCharsPerLine(), includeHeader = true)
        if (displayLines.isEmpty()) { totalPages = 0; return }
        var currentPage = mutableListOf<String>()
        var currentLineCount = 0
        for (line in displayLines) {
            if (currentLineCount >= _linesPerPage) {
                _chapterPages.add(currentPage.toList())
                currentPage = mutableListOf(); currentLineCount = 0
            }
            currentPage.add(line); currentLineCount++
        }
        if (currentPage.isNotEmpty()) _chapterPages.add(currentPage.toList())
        totalPages = _chapterPages.size
        if (totalPages == 0) totalPages = 1
    }

    // ==================== Chapter Status Tags ====================

    private fun buildChapterStatusTag(status: ChapterStatus): String = when (status) {
        ChapterStatus.Done -> ""
        ChapterStatus.Failed -> "\u274C "
        ChapterStatus.Downloading -> "\u23F3 "
        else -> "\u2B1C "
    }

    private fun buildChapterDisplayContent(chapter: ChapterEntity): String = when (chapter.status) {
        ChapterStatus.Done -> chapter.content ?: ""
        ChapterStatus.Failed -> "\uFF08\u4E0B\u8F7D\u5931\u8D25\uFF1A${chapter.error}\n\n\u70B9\u51FB\u4E0A\u65B9\u300C\u5237\u65B0\u7AE0\u8282\u300D\u53EF\u5728\u91CD\u65B0\u4E0B\u8F7D\u540E\u67E5\u770B\uFF09"
        ChapterStatus.Downloading -> "\uFF08\u6B63\u5728\u4E0B\u8F7D\u4E2D\u2026\uFF09"
        else -> "\uFF08\u7B49\u5F85\u4E0B\u8F7D\uFF09"
    }

    private fun isPlaceholderChapterContent(content: String?): Boolean {
        if (content.isNullOrBlank()) return true
        return content.startsWith("\uFF08\u7B49\u5F85\u4E0B\u8F7D\uFF09") ||
                content.startsWith("\uFF08\u6B63\u5728\u4E0B\u8F7D\u4E2D") ||
                content.startsWith("\uFF08\u4E0B\u8F7D\u5931\u8D25\uFF1A")
    }

    // ==================== Anchor Text ====================

    private fun buildCurrentPageAnchorText(): String {
        if (_chapterPages.isEmpty() || currentPageIndex < 0 || currentPageIndex >= _chapterPages.size) return ""
        return _chapterPages[currentPageIndex]
            .map { it.trim() }
            .firstOrNull { it.isNotBlank() && !it.startsWith("\u231C") }
            ?: ""
    }

    private fun buildCurrentPagePreviewText(): String {
        if (_chapterPages.isEmpty() || currentPageIndex < 0 || currentPageIndex >= _chapterPages.size) return ""
        val merged = _chapterPages[currentPageIndex]
            .map { it.trim() }
            .filter { it.isNotBlank() && !it.startsWith("\u231C") }
            .joinToString("")
        return if (merged.length <= 48) merged else merged.take(48) + "\u2026"
    }

    private fun findPageByAnchorText(anchorText: String): Int {
        if (anchorText.isBlank() || _chapterPages.isEmpty()) return -1
        for (i in _chapterPages.indices) {
            if (_chapterPages[i].any { it.contains(anchorText) }) return i
        }
        return -1
    }

    // ==================== Open Book ====================

    suspend fun openBook(book: BookEntity) {
        selectedDbBook = book
        _readerTitle.postValue("\u300A${book.title}\u300B- ${book.author}")
        parent.setStatusMessage("正在加载《${book.title}》…")

        try {
            loadDbBookChapters(book)
            loadBookmarks(book.id)
            restoreReadingState(book.id)
            refreshSortedSwitchSources()
            updateProgressDisplay()
            checkCurrentPageBookmark()
            queueAutoPrefetch("open")
            parent.setStatusMessage("《${book.title}》加载完成（${currentBookChapters.size} 章）")
        } catch (e: Exception) {
            parent.setStatusMessage("加载书籍失败：${e.message}")
            AppLogger.log("Reader", "openBook error: ${e.message}")
        }
    }

    private suspend fun loadDbBookChapters(book: BookEntity, preserveCurrentSelection: Boolean = false) {
        val chapters = bookRepo.getChapters(book.id).sortedBy { it.indexNo }
        _readerChapters.postValue(chapters.mapIndexed { idx, ch ->
            ReaderChapterItem(ch.indexNo, ch.title, "${buildChapterStatusTag(ch.status)}${ch.title}")
        })
        currentBookChapters.clear()
        chapters.forEach { ch ->
            currentBookChapters.add(ch.title to buildChapterDisplayContent(ch))
        }
        if (chapters.isNotEmpty()) {
            val preferred = book.readChapterIndex.coerceIn(0, chapters.size - 1)
            val startIndex = if (preserveCurrentSelection &&
                readerCurrentChapterIndex in 0 until chapters.size
            ) readerCurrentChapterIndex else preferred
            _suppressReaderIndexChangedNavigation = true
            readerCurrentChapterIndex = startIndex
            _suppressReaderIndexChangedNavigation = false
            _currentChapterTitleDisplay.postValue(currentBookChapters[startIndex].first)
            recalculateLinesPerPage()
            rebuildChapterPages()
            currentPageIndex = 0
            showCurrentPage()
        } else {
            _readerParagraphs.postValue(listOf("（章节目录尚未加载，请等待下载开始或点击「续传」）"))
        }
    }

    // ==================== Navigation ====================

    /** 跳转到指定章节，支持跳到末页。跨章时先查库获取最新内容。 */
    suspend fun navigateToChapter(chapterIndex: Int, goToLastPage: Boolean = false, prefetchReason: String? = null) {
        val idx = chapterIndex.coerceIn(0, currentBookChapters.size - 1)
        if (currentBookChapters.isEmpty()) return

        var chapterTitle = currentBookChapters[idx].first
        var displayContent = currentBookChapters[idx].second
        var hasReadyContent = !isPlaceholderChapterContent(displayContent)

        // DB lookup for fresh content
        val book = selectedDbBook
        if (book != null) {
            try {
                val freshChapter = bookRepo.getChapter(book.id, idx)
                if (freshChapter != null) {
                    chapterTitle = freshChapter.title
                    displayContent = buildChapterDisplayContent(freshChapter)
                    hasReadyContent = freshChapter.status == ChapterStatus.Done &&
                            !freshChapter.content.isNullOrBlank()
                    currentBookChapters[idx] = chapterTitle to displayContent
                    val tocList = _readerChapters.value?.toMutableList()
                    if (tocList != null && idx < tocList.size) {
                        tocList[idx] = ReaderChapterItem(idx, chapterTitle,
                            "${buildChapterStatusTag(freshChapter.status)}$chapterTitle")
                        _readerChapters.postValue(tocList)
                    }
                }
            } catch (e: Exception) {
                AppLogger.log("Reader", "DB lookup failed for chapter $idx: ${e.message}")
            }
        }

        _suppressReaderIndexChangedNavigation = true
        readerCurrentChapterIndex = idx
        _suppressReaderIndexChangedNavigation = false
        _currentChapterTitleDisplay.postValue(chapterTitle)

        recalculateLinesPerPage()
        rebuildChapterPages()
        currentPageIndex = if (goToLastPage) max(0, totalPages - 1) else 0
        showCurrentPage()
        _isTocOverlayVisible.postValue(false)

        // Save progress
        if (book != null) {
            try {
                bookRepo.updateReadProgress(book.id, idx, chapterTitle)
                selectedDbBook?.readChapterIndex = idx
                selectedDbBook?.readChapterTitle = chapterTitle
                parent.bookshelf.markBookshelfDirty()
            } catch (e: Exception) {
                AppLogger.log("Reader", "SaveDbProgress error: ${e.message}")
            }
            if (hasReadyContent) queueAutoPrefetch("low-watermark")
            else queueAutoPrefetch(if (prefetchReason.isNullOrBlank()) "jump" else prefetchReason)
        }
    }

    fun nextPage() {
        if (currentPageIndex < totalPages - 1) {
            currentPageIndex++
            showCurrentPage()
        } else if (readerCurrentChapterIndex < currentBookChapters.size - 1) {
            coroutineScope.launch { navigateToChapter(readerCurrentChapterIndex + 1, goToLastPage = false) }
        }
    }

    /** 翻到上一页；若当前章节已到首页，则自动回到上一章末页。 */
    fun previousPage() {
        if (currentPageIndex > 0) {
            currentPageIndex--
            showCurrentPage()
        } else if (readerCurrentChapterIndex > 0) {
            coroutineScope.launch { navigateToChapter(readerCurrentChapterIndex - 1, goToLastPage = true) }
        }
    }

    fun nextChapter() {
        if (readerCurrentChapterIndex < currentBookChapters.size - 1) {
            coroutineScope.launch { navigateToChapter(readerCurrentChapterIndex + 1) }
        } else {
            parent.setStatusMessage("已经是最后一章。")
        }
    }

    fun previousChapter() {
        if (readerCurrentChapterIndex > 0) {
            coroutineScope.launch { navigateToChapter(readerCurrentChapterIndex - 1) }
        } else {
            parent.setStatusMessage("已经是第一章。")
        }
    }

    fun selectTocChapter(indexNo: Int) {
        val targetIdx = currentBookChapters.indices.firstOrNull { idx ->
            (_readerChapters.value?.getOrNull(idx)?.indexNo ?: -1) == indexNo
        } ?: return
        coroutineScope.launch { navigateToChapter(targetIdx, prefetchReason = "manual-priority") }
        _isTocOverlayVisible.postValue(false)
    }

    // ==================== Viewport ====================

    fun updateViewportSize(width: Double, height: Double) {
        if (width <= 0 || height <= 0) return
        val isFirstValid = viewportWidth <= 0 || viewportHeight <= 0
        if (isFirstValid) {
            viewportWidth = width; viewportHeight = height
            recalculateLinesPerPage()
            if (currentBookChapters.isNotEmpty()) {
                val savedPage = currentPageIndex
                rebuildChapterPages()
                currentPageIndex = savedPage.coerceIn(0, max(0, totalPages - 1))
                showCurrentPage()
            }
            return
        }
        if (abs(viewportWidth - width) < 1 && abs(viewportHeight - height) < 1) return
        viewportWidth = width; viewportHeight = height
        viewportRebuildJob?.cancel()
        viewportRebuildJob = coroutineScope.launch {
            delay(VIEWPORT_REBUILD_DEBOUNCE_MS)
            recalculateLinesPerPage()
            if (currentBookChapters.isNotEmpty()) {
                val savedPage = currentPageIndex
                rebuildChapterPages()
                currentPageIndex = savedPage.coerceIn(0, max(0, totalPages - 1))
                showCurrentPage()
            }
        }
    }

    // ==================== Page Display ====================

    private fun showCurrentPage() {
        if (_chapterPages.isEmpty() || currentPageIndex < 0 || currentPageIndex >= _chapterPages.size) {
            updatePageProgressDisplay()
            checkCurrentPageBookmark()
            return
        }
        _readerParagraphs.postValue(_chapterPages[currentPageIndex])
        updatePageProgressDisplay()
        checkCurrentPageBookmark()
        queuePersistReadingState()
    }

    private fun updateProgressDisplay() {
        val total = currentBookChapters.size
        if (total <= 0) { _readerProgressDisplay.postValue(""); return }
        val current = (readerCurrentChapterIndex + 1).coerceIn(1, total)
        val percent = (100.0 * current / total).toInt()
        _readerProgressDisplay.postValue("第 $current/$total 章 ($percent%)")
    }

    private fun updatePageProgressDisplay() {
        if (totalPages <= 0) { _pageProgressDisplay.postValue(""); return }
        _pageProgressDisplay.postValue("第 ${currentPageIndex + 1}/$totalPages 页")
    }

    // ==================== TOC / Bookmark Panel ====================

    fun toggleTocOverlay() {
        val current = _isTocOverlayVisible.value ?: false
        _isTocOverlayVisible.postValue(!current)
        if (!current) _isBookmarkPanelVisible.postValue(false)
        if (current && _pendingRefreshWhenTocClosed) {
            _pendingRefreshWhenTocClosed = false
            coroutineScope.launch { refreshCurrentBookFromDownloadSignal(selectedDbBook?.id) }
        }
    }

    fun showTocPanel() {
        _isBookmarkPanelVisible.postValue(false)
    }

    fun showBookmarkPanel() {
        _isBookmarkPanelVisible.postValue(true)
    }

    // ==================== Bookmarks (with anchor text) ====================

    suspend fun toggleBookmark() {
        val book = selectedDbBook ?: return
        val isBookmarked = _isCurrentPageBookmarked.value ?: false

        if (isBookmarked) {
            bookRepo.deleteReadingBookmark(book.id, readerCurrentChapterIndex, currentPageIndex)
            parent.setStatusMessage("已删除书签：第 ${readerCurrentChapterIndex + 1} 章 第 ${currentPageIndex + 1} 页")
        } else {
            val chapterTitle = currentBookChapters.getOrNull(readerCurrentChapterIndex)?.first ?: ""
            val previewText = buildCurrentPagePreviewText()
            val anchorText = buildCurrentPageAnchorText()
            val bookmark = ReadingBookmarkEntity(
                bookId = book.id,
                chapterIndex = readerCurrentChapterIndex,
                pageIndex = currentPageIndex,
                chapterTitle = chapterTitle,
                previewText = previewText.take(100),
                anchorText = anchorText,
                createdAt = System.currentTimeMillis()
            )
            bookRepo.upsertReadingBookmark(bookmark)
            parent.setStatusMessage("已添加书签：第 ${readerCurrentChapterIndex + 1} 章 第 ${currentPageIndex + 1} 页")
        }

        loadBookmarks(book.id)
        checkCurrentPageBookmark()
    }

    fun jumpToBookmark(bookmark: ReadingBookmarkEntity) {
        coroutineScope.launch {
            if (bookmark.chapterIndex < 0 || bookmark.chapterIndex >= currentBookChapters.size) return@launch
            navigateToChapter(bookmark.chapterIndex)
            currentPageIndex = bookmark.pageIndex.coerceIn(0, max(0, totalPages - 1))
            if (!bookmark.anchorText.isNullOrBlank()) {
                val anchorPage = findPageByAnchorText(bookmark.anchorText!!)
                if (anchorPage >= 0) currentPageIndex = anchorPage
            }
            showCurrentPage()
            _isTocOverlayVisible.postValue(false)
            parent.setStatusMessage("已跳转书签：第 ${bookmark.chapterIndex + 1} 章 第 ${currentPageIndex + 1} 页")
        }
    }

    suspend fun removeBookmark(bookmark: ReadingBookmarkEntity) {
        val book = selectedDbBook ?: return
        bookRepo.deleteReadingBookmark(book.id, bookmark.chapterIndex, bookmark.pageIndex)
        loadBookmarks(book.id)
        checkCurrentPageBookmark()
        parent.setStatusMessage("已删除书签。")
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

    // ==================== Reading State (debounced + anchor) ====================

    private fun queuePersistReadingState() {
        if (_isApplyingPersistedState || selectedDbBook == null || currentBookChapters.isEmpty()) return
        readingStateSaveCts?.cancel()
        readingStateSaveCts = coroutineScope.launch {
            try {
                delay(READING_STATE_SAVE_DEBOUNCE_MS)
                val book = selectedDbBook ?: return@launch
                val state = ReadingStateEntity(
                    bookId = book.id,
                    chapterIndex = readerCurrentChapterIndex,
                    pageIndex = currentPageIndex,
                    anchorText = buildCurrentPageAnchorText(),
                    layoutFingerprint = buildLayoutFingerprint(),
                    updatedAt = System.currentTimeMillis()
                )
                withContext(Dispatchers.IO) {
                    bookRepo.upsertReadingState(state)
                    bookRepo.updateReadProgress(
                        book.id, readerCurrentChapterIndex,
                        currentBookChapters.getOrNull(readerCurrentChapterIndex)?.first ?: ""
                    )
                }
            } catch (_: CancellationException) {
                // debounce cancelled
            } catch (e: Exception) {
                AppLogger.log("Reader", "SaveReadingState error: ${e.message}")
            }
        }
    }

    /** For backward compatibility */
    fun saveReadingState() {
        queuePersistReadingState()
    }

    private suspend fun restoreReadingState(bookId: String) {
        val state = bookRepo.getReadingState(bookId) ?: return
        if (currentBookChapters.isEmpty()) return
        _isApplyingPersistedState = true
        try {
            val chapterIndex = state.chapterIndex.coerceIn(0, currentBookChapters.size - 1)
            if (chapterIndex != readerCurrentChapterIndex) {
                navigateToChapter(chapterIndex)
            }
            var targetPage = state.pageIndex.coerceIn(0, max(0, totalPages - 1))
            if (!state.anchorText.isNullOrBlank()) {
                val anchorPage = findPageByAnchorText(state.anchorText!!)
                if (anchorPage >= 0) targetPage = anchorPage
            }
            currentPageIndex = targetPage
            showCurrentPage()
        } finally {
            _isApplyingPersistedState = false
        }
    }

    private fun buildLayoutFingerprint(): String =
        "${readerFontSize}|${readerLineHeight}|${readerContentMaxWidth}|${readerSidePaddingPx.toInt()}|${readerHorizontalInnerReservePx.toInt()}|${viewportWidth.toInt()}|${viewportHeight.toInt()}"

    // ==================== Reader Settings ====================

    fun applyPaperPreset(preset: PaperPreset) {
        readerBackground = preset.background
        readerForeground = preset.foreground
        isDarkMode = preset.background.startsWith("#1") || preset.background.startsWith("#0")
        parent.settings.queueAutoSaveSettings()
    }

    // ==================== Source Switching ====================

    fun refreshSortedSwitchSources() {
        val sorted = parent.sources
            .filter { it.id > 0 && it.searchSupported }
            .sortedWith(
                compareByDescending<SourceItem> { it.isHealthy == true }
                    .thenByDescending { it.isHealthy == null }
                    .thenBy { it.id }
            )
        _sortedSwitchSources.postValue(sorted)
    }

    fun switchSource(sourceItem: SourceItem) {
        if (sourceItem.id <= 0) return
        if (_isSourceSwitching.value == true) return
        val book = selectedDbBook ?: run { parent.setStatusMessage("当前未打开任何书籍。"); return }
        if (readerCurrentChapterIndex < 0 || readerCurrentChapterIndex >= currentBookChapters.size) {
            parent.setStatusMessage("当前没有正在阅读的章节。"); return
        }
        val chapterTitle = currentBookChapters[readerCurrentChapterIndex].first
        _isSourceSwitching.postValue(true)
        parent.setStatusMessage("换源中：正在从 ${sourceItem.name} 获取「$chapterTitle」…")

        coroutineScope.launch {
            try {
                val (success, content, message) = parent.downloadBookUseCase.fetchChapterFromSource(
                    book, chapterTitle, sourceItem.id
                )
                if (success && content.isNotBlank()) {
                    currentBookChapters[readerCurrentChapterIndex] = chapterTitle to content
                    withContext(Dispatchers.IO) {
                        bookRepo.updateChapter(book.id, readerCurrentChapterIndex, ChapterStatus.Done, content, null)
                    }
                    rebuildChapterPages()
                    currentPageIndex = 0
                    showCurrentPage()
                    parent.setStatusMessage(message)
                } else {
                    parent.setStatusMessage("换源未成功，已保留原文。$message")
                }
            } catch (e: Exception) {
                parent.setStatusMessage("换源失败，已保留原文。${e.message}")
            } finally {
                _isSourceSwitching.postValue(false)
            }
        }
    }

    // ==================== Refresh from Download Signal ====================

    suspend fun refreshCurrentBookFromDownloadSignal(bookId: String?) {
        if (bookId.isNullOrBlank()) return
        val book = selectedDbBook ?: return
        if (!book.id.equals(bookId, ignoreCase = true)) return

        if (_isTocOverlayVisible.value == true) {
            _pendingRefreshWhenTocClosed = true
            return
        }

        try {
            // Full refresh if no chapters loaded or showing initial placeholder
            if (currentBookChapters.isEmpty() ||
                currentBookChapters.getOrNull(readerCurrentChapterIndex)?.second
                    ?.startsWith("（章节目录尚未加载") == true
            ) {
                val freshBook = bookRepo.getBook(book.id)
                if (freshBook != null) {
                    selectedDbBook?.doneChapters = freshBook.doneChapters
                    selectedDbBook?.totalChapters = freshBook.totalChapters
                }
                loadDbBookChapters(book, preserveCurrentSelection = true)
                updateProgressDisplay()
                return
            }

            // Partial refresh: only current chapter if it's a placeholder
            val currentContent = currentBookChapters.getOrNull(readerCurrentChapterIndex)?.second
            if (!isPlaceholderChapterContent(currentContent)) return

            val freshBook = bookRepo.getBook(book.id)
            if (freshBook != null) {
                selectedDbBook?.doneChapters = freshBook.doneChapters
                selectedDbBook?.totalChapters = freshBook.totalChapters
            }

            val chapter = bookRepo.getChapter(book.id, readerCurrentChapterIndex) ?: return
            val displayContent = buildChapterDisplayContent(chapter)
            val wasPlaceholder = isPlaceholderChapterContent(currentBookChapters[readerCurrentChapterIndex].second)
            currentBookChapters[readerCurrentChapterIndex] = chapter.title to displayContent

            // Update TOC item
            val tocList = _readerChapters.value?.toMutableList()
            if (tocList != null && readerCurrentChapterIndex < tocList.size) {
                tocList[readerCurrentChapterIndex] = ReaderChapterItem(
                    readerCurrentChapterIndex, chapter.title,
                    "${buildChapterStatusTag(chapter.status)}${chapter.title}"
                )
                _readerChapters.postValue(tocList)
            }

            rebuildChapterPages()
            currentPageIndex = currentPageIndex.coerceIn(0, max(0, totalPages - 1))
            showCurrentPage()
            updateProgressDisplay()

            if (readerAutoPrefetchEnabled && wasPlaceholder && chapter.status == ChapterStatus.Done) {
                queueAutoPrefetch("foreground-direct")
            }
        } catch (e: Exception) {
            AppLogger.log("Reader", "refreshCurrentBookFromDownloadSignal error: ${e.message}")
        }
    }

    fun needsCurrentChapterForegroundRefresh(): Boolean {
        if (currentBookChapters.isEmpty()) return false
        val (_, content) = currentBookChapters.getOrNull(readerCurrentChapterIndex) ?: return false
        return isPlaceholderChapterContent(content)
    }

    // ==================== Auto Prefetch ====================

    private fun queueAutoPrefetch(trigger: String) {
        val book = selectedDbBook ?: return
        if (!readerAutoPrefetchEnabled) return
        AppLogger.log("Reader.AutoPrefetch", "trigger=$trigger, chapter=${readerCurrentChapterIndex + 1}")

        coroutineScope.launch {
            try {
                delay(350)
                if (trigger == "foreground-direct") {
                    val take = max(1, readerPrefetchBatchSize)
                    parent.searchDownload.queueOrReplaceAutoPrefetch(book, readerCurrentChapterIndex, take, trigger)
                    return@launch
                }
                val plan = parent.autoDownloadPlanner.buildPlan(
                    book.id, readerCurrentChapterIndex,
                    max(1, readerPrefetchBatchSize), max(1, readerPrefetchLowWatermark)
                )
                if (plan.shouldQueueWindow) {
                    parent.searchDownload.queueOrReplaceAutoPrefetch(
                        book, plan.windowStartIndex, plan.windowTakeCount, trigger
                    )
                } else if (plan.hasGap && plan.firstGapIndex >= 0) {
                    parent.searchDownload.queueOrReplaceAutoPrefetch(
                        book, plan.firstGapIndex, max(1, readerPrefetchBatchSize), "gap-fill"
                    )
                }
            } catch (_: CancellationException) { }
            catch (e: Exception) { AppLogger.log("Reader.AutoPrefetch", "error: ${e.message}") }
        }
    }

    // ==================== Font Selection ====================

    /**
     * 切换字体名 → 更新 Typeface LiveData → 重排分页 → 自动保存
     */
    fun changeFont(fontName: String) {
        selectedFontName = fontName
        _readerTypeface.value = FONT_MAP[fontName] ?: Typeface.DEFAULT
        onReaderLayoutPropertyChanged()
    }

    // ==================== Property Change Handlers ====================

    /**
     * 当字号、行高、段距、最大宽度、padding 等影响排版的属性变化时调用。
     * 重新计算行容量 → 重建分页 → clamp 页码 → 刷新显示 → 自动保存设置。
     */
    fun onReaderLayoutPropertyChanged() {
        if (_chapterPages.isEmpty()) return
        rebuildChapterPages()
        currentPageIndex = currentPageIndex.coerceIn(0, max(0, totalPages - 1))
        showCurrentPage()
        parent.settings.queueAutoSaveSettings()
    }

    // ==================== Open Book Webpage ====================

    /**
     * 在系统浏览器中打开当前书籍的目录网页地址。
     */
    fun openBookWebPage() {
        val url = selectedDbBook?.tocUrl
        if (url.isNullOrBlank()) {
            parent.setStatusMessage("当前书籍没有关联的网页地址。")
            return
        }
        try {
            val context = parent.getApplication<android.app.Application>()
            val intent = Intent(Intent.ACTION_VIEW, Uri.parse(url)).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(intent)
            parent.setStatusMessage("已打开网页：$url")
        } catch (e: Exception) {
            parent.setStatusMessage("打开书籍网页失败：${e.message}")
        }
    }

    /** Cleanup coroutine scope when ViewModel is cleared */
    fun onCleared() {
        coroutineScope.cancel()
    }
}
