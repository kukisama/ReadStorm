package com.readstorm.app.ui.compose.screens

import android.graphics.BitmapFactory
import android.util.Base64
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.LibraryBooks
import androidx.compose.material.icons.automirrored.outlined.MenuBook
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.livedata.observeAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.DpOffset
import androidx.compose.ui.unit.dp
import com.readstorm.app.domain.models.BookEntity
import com.readstorm.app.ui.compose.components.RsEmptyState
import com.readstorm.app.ui.viewmodels.MainViewModel
import kotlinx.coroutines.launch

/**
 * "书架"页面 Compose Screen。
 */
@Composable
fun BookshelfScreen(mainViewModel: MainViewModel) {
    val books by mainViewModel.bookshelf.filteredDbBooks.observeAsState(emptyList())
    val scope = rememberCoroutineScope()

    var filterText by remember { mutableStateOf("") }

    Column(modifier = Modifier.fillMaxSize()) {
        // Top bar: filter + check updates
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            OutlinedTextField(
                value = filterText,
                onValueChange = {
                    filterText = it
                    mainViewModel.bookshelf.bookshelfFilterText = it
                },
                placeholder = { Text("搜索书架") },
                modifier = Modifier.weight(1f),
                singleLine = true,
                leadingIcon = {
                    Icon(Icons.Outlined.Search, contentDescription = null, modifier = Modifier.size(20.dp))
                }
            )
            IconButton(onClick = {
                scope.launch { mainViewModel.bookshelf.checkAllNewChapters() }
            }) {
                Icon(Icons.Outlined.Refresh, contentDescription = "检查更新")
            }
        }

        // Grid
        if (books.isEmpty()) {
            RsEmptyState(
                message = "书架为空",
                description = "搜索并下载书籍后将显示在此",
                icon = Icons.AutoMirrored.Outlined.LibraryBooks
            )
        } else {
            LazyVerticalGrid(
                columns = GridCells.Fixed(2),
                modifier = Modifier.fillMaxSize(),
                contentPadding = PaddingValues(horizontal = 12.dp, vertical = 4.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                items(books, key = { it.id }) { book ->
                    BookGridCard(
                        book = book,
                        onClick = { mainViewModel.openDbBookAndSwitchToReader(book) },
                        onResumeDownload = { scope.launch { mainViewModel.bookshelf.resumeBookDownload(book) } },
                        onCheckUpdate = { scope.launch { mainViewModel.bookshelf.checkNewChapters(book) } },
                        onExport = { scope.launch { mainViewModel.bookshelf.exportDbBook(book) } },
                        onRefreshCover = { scope.launch { mainViewModel.bookshelf.refreshCover(book) } },
                        onDelete = { scope.launch { mainViewModel.bookshelf.removeDbBook(book) } }
                    )
                }
            }
        }
    }
}

@OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)
@Composable
private fun BookGridCard(
    book: BookEntity,
    onClick: () -> Unit,
    onResumeDownload: () -> Unit,
    onCheckUpdate: () -> Unit,
    onExport: () -> Unit,
    onRefreshCover: () -> Unit,
    onDelete: () -> Unit
) {
    val progressPercent = book.progressPercent.coerceIn(0, 100)

    var showMenu by remember { mutableStateOf(false) }

    Card(
        modifier = Modifier
            .fillMaxWidth()
            .combinedClickable(
                onClick = onClick,
                onLongClick = { showMenu = true }
            ),
        shape = MaterialTheme.shapes.medium,
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(defaultElevation = 2.dp)
    ) {
        Column {
            // Cover area
            Box(
                modifier = Modifier
                    .fillMaxWidth()
                    .aspectRatio(3f / 4f)
                    .background(MaterialTheme.colorScheme.surfaceVariant),
                contentAlignment = Alignment.Center
            ) {
                val coverBitmap = remember(book.coverImage) {
                    if (book.hasCover && !book.coverImage.isNullOrBlank()) {
                        try {
                            val bytes = Base64.decode(book.coverImage, Base64.DEFAULT)
                            BitmapFactory.decodeByteArray(bytes, 0, bytes.size)?.asImageBitmap()
                        } catch (_: Exception) { null }
                    } else null
                }

                if (coverBitmap != null) {
                    Image(
                        bitmap = coverBitmap,
                        contentDescription = book.title,
                        modifier = Modifier.fillMaxSize(),
                        contentScale = ContentScale.Crop
                    )
                } else {
                    // Placeholder
                    Column(
                        horizontalAlignment = Alignment.CenterHorizontally,
                        verticalArrangement = Arrangement.Center
                    ) {
                        Text(
                            text = book.titleInitial,
                            style = MaterialTheme.typography.headlineLarge,
                            color = MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.5f)
                        )
                    }
                }
            }

            // Info
            Column(modifier = Modifier.padding(8.dp)) {
                Text(
                    text = book.title,
                    style = MaterialTheme.typography.titleSmall,
                    color = MaterialTheme.colorScheme.onSurface,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                Text(
                    text = book.author,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis
                )
                Spacer(Modifier.height(4.dp))

                // Progress bar + percent
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(4.dp)
                ) {
                    LinearProgressIndicator(
                        progress = { progressPercent / 100f },
                        modifier = Modifier
                            .weight(1f)
                            .height(4.dp),
                        color = MaterialTheme.colorScheme.primary,
                        trackColor = MaterialTheme.colorScheme.surfaceVariant
                    )
                    Text(
                        text = "${progressPercent}%",
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant
                    )
                }
            }
        }

        // Context menu
        DropdownMenu(
            expanded = showMenu,
            onDismissRequest = { showMenu = false },
            offset = DpOffset(8.dp, 0.dp)
        ) {
            DropdownMenuItem(
                text = { Text("继续阅读") },
                onClick = { showMenu = false; onClick() },
                leadingIcon = { Icon(Icons.AutoMirrored.Outlined.MenuBook, contentDescription = null) }
            )
            DropdownMenuItem(
                text = { Text("续传下载") },
                onClick = { showMenu = false; onResumeDownload() },
                leadingIcon = { Icon(Icons.Outlined.CloudDownload, contentDescription = null) }
            )
            DropdownMenuItem(
                text = { Text("检查更新") },
                onClick = { showMenu = false; onCheckUpdate() },
                leadingIcon = { Icon(Icons.Outlined.Refresh, contentDescription = null) }
            )
            DropdownMenuItem(
                text = { Text("导出") },
                onClick = { showMenu = false; onExport() },
                leadingIcon = { Icon(Icons.Outlined.FileDownload, contentDescription = null) }
            )
            DropdownMenuItem(
                text = { Text("刷新封面") },
                onClick = { showMenu = false; onRefreshCover() },
                leadingIcon = { Icon(Icons.Outlined.Image, contentDescription = null) }
            )
            HorizontalDivider()
            DropdownMenuItem(
                text = { Text("删除书籍", color = MaterialTheme.colorScheme.error) },
                onClick = { showMenu = false; onDelete() },
                leadingIcon = {
                    Icon(Icons.Outlined.Delete, contentDescription = null,
                        tint = MaterialTheme.colorScheme.error)
                }
            )
        }
    }
}
