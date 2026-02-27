package com.readstorm.app.ui.compose.screens

import android.graphics.Color as AndroidColor
import androidx.compose.animation.*
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.ArrowBack
import androidx.compose.material.icons.automirrored.outlined.ArrowForward
import androidx.compose.material.icons.automirrored.outlined.List
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.livedata.observeAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.layout.onSizeChanged
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.readstorm.app.ui.viewmodels.MainViewModel
import kotlinx.coroutines.launch

/**
 * Compose 阅读器页面。
 * 支持：章节阅读 / 翻页 / 目录 / 书签 / 纸张主题
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ReaderScreen(mainViewModel: MainViewModel) {
    val reader = mainViewModel.reader
    val scope = rememberCoroutineScope()
    val density = LocalDensity.current

    // ── 观测 ViewModel 状态 ──
    val title by reader.readerTitle.observeAsState("")
    val paragraphs by reader.readerParagraphs.observeAsState(emptyList())
    val chapterProgress by reader.readerProgressDisplay.observeAsState("")
    val pageProgress by reader.pageProgressDisplay.observeAsState("")
    val chapters by reader.readerChapters.observeAsState(emptyList())
    val bookmarks by reader.readerBookmarks.observeAsState(emptyList())
    val isCurrentBookmarked by reader.isCurrentPageBookmarked.observeAsState(false)
    val isTocVisible by reader.isTocOverlayVisible.observeAsState(false)
    val isBookmarkPanel by reader.isBookmarkPanelVisible.observeAsState(false)
    val chapterTitle by reader.currentChapterTitleDisplay.observeAsState("")

    // ── 本地 UI 状态 ──
    var showBars by remember { mutableStateOf(true) }
    var showThemeSheet by remember { mutableStateOf(false) }

    // 纸张颜色
    val bgColor = remember(reader.readerBackground) {
        try { Color(AndroidColor.parseColor(reader.readerBackground)) } catch (_: Exception) { Color(0xFFFFFBF0) }
    }
    val fgColor = remember(reader.readerForeground) {
        try { Color(AndroidColor.parseColor(reader.readerForeground)) } catch (_: Exception) { Color(0xFF1E293B) }
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(bgColor)
            .onSizeChanged { size ->
                val w = with(density) { size.width.toDp().value.toDouble() }
                val h = with(density) { size.height.toDp().value.toDouble() }
                reader.updateViewportSize(w, h)
            }
    ) {
        // ── 阅读内容区 ──
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(
                    top = if (showBars) 56.dp else 8.dp,
                    bottom = if (showBars) 100.dp else 8.dp,
                    start = reader.readerSidePaddingPx.dp,
                    end = reader.readerSidePaddingPx.dp
                )
                .pointerInput(Unit) {
                    detectTapGestures { offset ->
                        val width = size.width.toFloat()
                        when {
                            offset.x < width * 0.3f -> reader.previousPage()
                            offset.x > width * 0.7f -> reader.nextPage()
                            else -> showBars = !showBars
                        }
                    }
                }
        ) {
            // 章节标题
            if (chapterTitle.isNotEmpty()) {
                Text(
                    text = chapterTitle,
                    color = fgColor.copy(alpha = 0.5f),
                    fontSize = 11.sp,
                    modifier = Modifier.padding(bottom = 4.dp)
                )
            }

            // 阅读正文
            Column(
                modifier = Modifier
                    .weight(1f)
                    .verticalScroll(rememberScrollState())
            ) {
                paragraphs.forEach { para ->
                    Text(
                        text = para,
                        color = fgColor,
                        fontSize = reader.readerFontSize.sp,
                        lineHeight = reader.readerLineHeight.sp,
                        modifier = Modifier.padding(bottom = (reader.readerParagraphSpacing).dp)
                    )
                }
            }

            // 底部页码
            Text(
                text = "$pageProgress · $chapterProgress",
                color = fgColor.copy(alpha = 0.4f),
                fontSize = 11.sp,
                textAlign = TextAlign.Center,
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(top = 4.dp)
            )
        }

        // ── 顶部栏 ──
        AnimatedVisibility(
            visible = showBars,
            enter = slideInVertically(initialOffsetY = { -it }) + fadeIn(),
            exit = slideOutVertically(targetOffsetY = { -it }) + fadeOut(),
            modifier = Modifier.align(Alignment.TopCenter)
        ) {
            Surface(
                color = MaterialTheme.colorScheme.surface.copy(alpha = 0.95f),
                tonalElevation = 2.dp,
                shadowElevation = 2.dp
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .statusBarsPadding()
                        .padding(horizontal = 8.dp, vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Text(
                        text = title,
                        style = MaterialTheme.typography.titleMedium,
                        color = MaterialTheme.colorScheme.onSurface,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f)
                    )

                    IconButton(onClick = { reader.toggleTocOverlay() }) {
                        Icon(Icons.AutoMirrored.Outlined.List, "目录")
                    }

                    IconButton(onClick = { scope.launch { reader.toggleBookmark() } }) {
                        Icon(
                            if (isCurrentBookmarked) Icons.Outlined.Bookmark
                            else Icons.Outlined.BookmarkBorder,
                            "书签"
                        )
                    }
                }
            }
        }

        // ── 底部控制栏 ──
        AnimatedVisibility(
            visible = showBars,
            enter = slideInVertically(initialOffsetY = { it }) + fadeIn(),
            exit = slideOutVertically(targetOffsetY = { it }) + fadeOut(),
            modifier = Modifier.align(Alignment.BottomCenter)
        ) {
            Surface(
                color = MaterialTheme.colorScheme.surface.copy(alpha = 0.95f),
                tonalElevation = 2.dp,
                shadowElevation = 2.dp
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .navigationBarsPadding()
                        .padding(horizontal = 16.dp, vertical = 8.dp)
                ) {
                    // 翻页按钮行
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        TextButton(onClick = { reader.previousChapter() }) {
                            Icon(Icons.AutoMirrored.Outlined.ArrowBack, null, Modifier.size(16.dp))
                            Spacer(Modifier.width(4.dp))
                            Text("上一章")
                        }

                        Text(
                            text = "第 $chapterProgress 章 · $pageProgress 页",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant
                        )

                        TextButton(onClick = { reader.nextChapter() }) {
                            Text("下一章")
                            Spacer(Modifier.width(4.dp))
                            Icon(Icons.AutoMirrored.Outlined.ArrowForward, null, Modifier.size(16.dp))
                        }
                    }

                    Spacer(Modifier.height(4.dp))

                    // 主题按钮行
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceEvenly,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        reader.paperPresets.forEach { preset ->
                            val presetBg = try { Color(AndroidColor.parseColor(preset.background)) } catch (_: Exception) { Color.White }
                            val isSelected = preset.background == reader.readerBackground
                            FilledTonalButton(
                                onClick = {
                                    reader.applyPaperPreset(preset)
                                },
                                modifier = Modifier.size(40.dp),
                                shape = MaterialTheme.shapes.small,
                                contentPadding = PaddingValues(0.dp),
                                colors = ButtonDefaults.filledTonalButtonColors(
                                    containerColor = presetBg
                                ),
                                border = if (isSelected) ButtonDefaults.outlinedButtonBorder(enabled = true) else null
                            ) {
                                if (isSelected) {
                                    Icon(Icons.Outlined.Check, null, Modifier.size(14.dp), tint = MaterialTheme.colorScheme.primary)
                                }
                            }
                        }
                    }
                }
            }
        }

        // ── 目录/书签侧边栏 ──
        AnimatedVisibility(
            visible = isTocVisible,
            enter = slideInHorizontally(initialOffsetX = { -it }) + fadeIn(),
            exit = slideOutHorizontally(targetOffsetX = { -it }) + fadeOut()
        ) {
            Surface(
                modifier = Modifier
                    .fillMaxHeight()
                    .fillMaxWidth(0.75f),
                color = MaterialTheme.colorScheme.surface,
                tonalElevation = 4.dp,
                shadowElevation = 8.dp
            ) {
                Column(modifier = Modifier.fillMaxSize()) {
                    // Tab: 目录 / 书签
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .statusBarsPadding()
                            .padding(8.dp),
                        horizontalArrangement = Arrangement.spacedBy(8.dp)
                    ) {
                        FilterChip(
                            selected = !isBookmarkPanel,
                            onClick = { reader.showTocPanel() },
                            label = { Text("目录 (${chapters.size})") }
                        )
                        FilterChip(
                            selected = isBookmarkPanel,
                            onClick = { reader.showBookmarkPanel() },
                            label = { Text("书签 (${bookmarks.size})") }
                        )
                        Spacer(Modifier.weight(1f))
                        IconButton(onClick = { reader.toggleTocOverlay() }) {
                            Icon(Icons.Outlined.Close, "关闭")
                        }
                    }

                    HorizontalDivider()

                    if (!isBookmarkPanel) {
                        // 目录列表
                        LazyColumn(modifier = Modifier.fillMaxSize()) {
                            items(chapters) { ch ->
                                ListItem(
                                    headlineContent = {
                                        Text(
                                            text = ch.displayTitle,
                                            style = MaterialTheme.typography.bodyMedium,
                                            maxLines = 2,
                                            overflow = TextOverflow.Ellipsis
                                        )
                                    },
                                    modifier = Modifier.clickable {
                                        reader.selectTocChapter(ch.indexNo)
                                    }
                                )
                            }
                        }
                    } else {
                        // 书签列表
                        if (bookmarks.isEmpty()) {
                            Box(
                                modifier = Modifier.fillMaxSize(),
                                contentAlignment = Alignment.Center
                            ) {
                                Text("暂无书签", color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        } else {
                            LazyColumn(modifier = Modifier.fillMaxSize()) {
                                items(bookmarks) { bm ->
                                    ListItem(
                                        headlineContent = {
                                            Text(
                                                text = bm.display,
                                                style = MaterialTheme.typography.bodyMedium
                                            )
                                        },
                                        supportingContent = {
                                            if (!bm.previewText.isNullOrBlank()) {
                                                Text(
                                                    text = bm.previewText!!,
                                                    style = MaterialTheme.typography.bodySmall,
                                                    maxLines = 2,
                                                    overflow = TextOverflow.Ellipsis
                                                )
                                            }
                                        },
                                        trailingContent = {
                                            IconButton(onClick = {
                                                scope.launch { reader.removeBookmark(bm) }
                                            }) {
                                                Icon(Icons.Outlined.Delete, "删除", Modifier.size(18.dp))
                                            }
                                        },
                                        modifier = Modifier.clickable {
                                            reader.jumpToBookmark(bm)
                                        }
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
