package com.readstorm.app.ui.compose.screens

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Refresh
import androidx.compose.material.icons.outlined.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.livedata.observeAsState
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.readstorm.app.domain.models.SearchResult
import com.readstorm.app.ui.compose.components.RsEmptyState
import com.readstorm.app.ui.compose.components.RsLoading
import com.readstorm.app.ui.viewmodels.MainViewModel
import kotlinx.coroutines.launch

/**
 * "搜索"页面 Compose Screen。
 */
@Composable
fun SearchScreen(mainViewModel: MainViewModel) {
    val searchResults by mainViewModel.searchDownload.searchResults.observeAsState(emptyList())
    val isSearching by mainViewModel.searchDownload.isSearching.observeAsState(false)
    val hasNoResults by mainViewModel.searchDownload.hasNoSearchResults.observeAsState(false)
    val selectedResult by mainViewModel.searchDownload.selectedSearchResult.observeAsState(null)
    val availableSourceCount by mainViewModel.availableSourceCount.observeAsState(0)
    val sourcesVersion by mainViewModel.sourcesVersion.observeAsState(0)
    val scope = rememberCoroutineScope()

    var keyword by remember { mutableStateOf("") }
    var selectedSourceIndex by remember { mutableIntStateOf(0) }
    var sourceExpanded by remember { mutableStateOf(false) }

    LaunchedEffect(sourcesVersion, availableSourceCount) {
        val sources = mainViewModel.sources
        if (sources.isEmpty()) return@LaunchedEffect

        val selectedId = mainViewModel.searchDownload.selectedSourceId
        val byIdIndex = if (selectedId > 0) sources.indexOfFirst { it.id == selectedId } else -1
        selectedSourceIndex = when {
            byIdIndex >= 0 -> byIdIndex
            selectedSourceIndex in sources.indices -> selectedSourceIndex
            else -> 0
        }
        mainViewModel.searchDownload.selectedSourceId = sources[selectedSourceIndex].id
    }

    LaunchedEffect(Unit) {
        mainViewModel.searchDownload.refreshSourceHealthOnceOnScreenEnter()
    }

    Column(modifier = Modifier.fillMaxSize()) {
        // Search bar
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            OutlinedTextField(
                value = keyword,
                onValueChange = { keyword = it },
                placeholder = { Text("输入书名或作者") },
                modifier = Modifier.weight(1f),
                singleLine = true,
                trailingIcon = {
                    IconButton(onClick = {
                        if (keyword.isNotBlank()) {
                            mainViewModel.searchDownload.setSearchKeyword(keyword)
                            scope.launch { mainViewModel.searchDownload.search(keyword) }
                        }
                    }) {
                        Icon(Icons.Outlined.Search, contentDescription = "搜索")
                    }
                }
            )
        }

        // Source selector + actions
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            // Source dropdown
            Box(modifier = Modifier.weight(1f)) {
                val sources = mainViewModel.sources
                val selectedSource = sources.getOrNull(selectedSourceIndex)
                OutlinedButton(
                    onClick = { sourceExpanded = true },
                    shape = MaterialTheme.shapes.small
                ) {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(6.dp)
                    ) {
                        SourceHealthDot(selectedSource?.isHealthy)
                        Text(
                            selectedSource?.name ?: "全部书源",
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis
                        )
                    }
                }
                DropdownMenu(
                    expanded = sourceExpanded,
                    onDismissRequest = { sourceExpanded = false }
                ) {
                    sources.forEachIndexed { index, source ->
                        DropdownMenuItem(
                            text = {
                                Row(
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.spacedBy(6.dp)
                                ) {
                                    SourceHealthDot(source.isHealthy)
                                    Text(if (source.id == 0) source.name else "${source.name} (#${source.id})")
                                }
                            },
                            onClick = {
                                selectedSourceIndex = index
                                mainViewModel.searchDownload.selectedSourceId = source.id
                                sourceExpanded = false
                            }
                        )
                    }
                }
            }

            // Refresh health
            IconButton(onClick = {
                scope.launch { mainViewModel.searchDownload.refreshSourceHealth() }
            }) {
                Icon(Icons.Outlined.Refresh, contentDescription = "刷新书源")
            }

            // Queue download
            Button(
                onClick = { mainViewModel.searchDownload.queueDownload() },
                shape = MaterialTheme.shapes.small,
                enabled = selectedResult != null
            ) {
                Text("加入队列")
            }
        }

        Spacer(Modifier.height(8.dp))

        // Content area
        when {
            isSearching -> {
                RsLoading(message = "搜索中…")
            }
            hasNoResults -> {
                RsEmptyState(
                    message = "没有搜索结果",
                    description = "请尝试更换关键词或切换书源"
                )
            }
            searchResults.isNotEmpty() -> {
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    contentPadding = PaddingValues(horizontal = 16.dp, vertical = 4.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    items(searchResults, key = { it.id }) { result ->
                        SearchResultCard(
                            result = result,
                            isSelected = selectedResult?.id == result.id,
                            onClick = {
                                mainViewModel.searchDownload.setSelectedSearchResult(result)
                            }
                        )
                    }
                }
            }
            else -> {
                RsEmptyState(
                    message = "搜索书籍",
                    description = "输入关键词开始搜索",
                    icon = Icons.Outlined.Search
                )
            }
        }
    }
}

@Composable
private fun SourceHealthDot(isHealthy: Boolean?) {
    val dotColor = when (isHealthy) {
        true -> Color(0xFF16A34A)
        false -> Color(0xFFDC2626)
        null -> MaterialTheme.colorScheme.onSurfaceVariant.copy(alpha = 0.6f)
    }
    Text("●", color = dotColor)
}

@Composable
private fun SearchResultCard(
    result: SearchResult,
    isSelected: Boolean,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.medium,
        colors = CardDefaults.cardColors(
            containerColor = if (isSelected)
                MaterialTheme.colorScheme.primaryContainer
            else MaterialTheme.colorScheme.surface
        ),
        elevation = CardDefaults.cardElevation(
            defaultElevation = if (isSelected) 2.dp else 1.dp
        )
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            Text(
                text = result.title,
                style = MaterialTheme.typography.titleMedium,
                color = if (isSelected)
                    MaterialTheme.colorScheme.onPrimaryContainer
                else MaterialTheme.colorScheme.onSurface,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
            Spacer(Modifier.height(4.dp))
            Text(
                text = "作者：${result.author}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Text(
                text = "来源：${result.sourceName}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )
            Text(
                text = "最新章节：${result.latestChapter}",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis
            )
        }
    }
}
