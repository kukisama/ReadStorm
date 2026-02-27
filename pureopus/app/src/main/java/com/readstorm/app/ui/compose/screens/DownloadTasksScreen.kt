package com.readstorm.app.ui.compose.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.livedata.observeAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.readstorm.app.domain.models.DownloadTask
import com.readstorm.app.domain.models.DownloadTaskStatus
import com.readstorm.app.ui.compose.components.RsEmptyState
import com.readstorm.app.ui.viewmodels.MainViewModel

/**
 * "下载任务"页面 Compose Screen。
 */
@Composable
fun DownloadTasksScreen(mainViewModel: MainViewModel) {
    val tasks by mainViewModel.searchDownload.filteredDownloadTasks.observeAsState(emptyList())
    val summary by mainViewModel.searchDownload.activeDownloadSummary.observeAsState("")
    // 观测版本号以打破 Compose 结构相等性检查，强制重组
    val taskVersion by mainViewModel.searchDownload.taskListVersion.observeAsState(0)

    Column(modifier = Modifier.fillMaxSize()) {
        // Summary + controls
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            if (summary.isNotEmpty()) {
                Text(
                    text = summary,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.weight(1f)
                )
            } else {
                Spacer(Modifier.weight(1f))
            }

            OutlinedButton(
                onClick = { mainViewModel.searchDownload.stopAllDownloads() },
                shape = MaterialTheme.shapes.small
            ) {
                Icon(Icons.Outlined.PauseCircle, contentDescription = null, modifier = Modifier.size(16.dp))
                Spacer(Modifier.width(4.dp))
                Text("全部停止", style = MaterialTheme.typography.labelMedium)
            }

            Button(
                onClick = { mainViewModel.searchDownload.startAllDownloads() },
                shape = MaterialTheme.shapes.small
            ) {
                Icon(Icons.Outlined.PlayCircle, contentDescription = null, modifier = Modifier.size(16.dp))
                Spacer(Modifier.width(4.dp))
                Text("全部开始", style = MaterialTheme.typography.labelMedium)
            }
        }

        // Task list
        if (tasks.isEmpty()) {
            RsEmptyState(
                message = "暂无下载任务",
                description = "搜索并加入队列后任务将显示在此",
                icon = Icons.Outlined.CloudDownload
            )
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                contentPadding = PaddingValues(horizontal = 16.dp, vertical = 4.dp),
                verticalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                items(tasks, key = { "${it.id}_$taskVersion" }) { task ->
                    DownloadTaskCard(
                        task = task,
                        onPause = { mainViewModel.searchDownload.pauseDownload(task) },
                        onResume = { mainViewModel.searchDownload.resumeDownload(task) },
                        onRetry = { mainViewModel.searchDownload.retryDownload(task) },
                        onCancel = { mainViewModel.searchDownload.cancelDownload(task) },
                        onDelete = { mainViewModel.searchDownload.deleteDownload(task) }
                    )
                }
            }
        }
    }
}

@Composable
private fun DownloadTaskCard(
    task: DownloadTask,
    onPause: () -> Unit,
    onResume: () -> Unit,
    onRetry: () -> Unit,
    onCancel: () -> Unit,
    onDelete: () -> Unit
) {
    val progressPercent = task.progressPercent.coerceIn(0, 100)

    val statusColor = when (task.currentStatus) {
        DownloadTaskStatus.Downloading -> MaterialTheme.colorScheme.primary
        DownloadTaskStatus.Succeeded -> MaterialTheme.colorScheme.tertiary
        DownloadTaskStatus.Failed -> MaterialTheme.colorScheme.error
        DownloadTaskStatus.Paused -> MaterialTheme.colorScheme.secondary
        DownloadTaskStatus.Cancelled -> MaterialTheme.colorScheme.onSurfaceVariant
        else -> MaterialTheme.colorScheme.onSurfaceVariant
    }

    Card(
        modifier = Modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.medium,
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp)
    ) {
        Column(modifier = Modifier.padding(12.dp)) {
            // Title + status
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Text(
                    text = task.bookTitle,
                    style = MaterialTheme.typography.titleSmall,
                    color = MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.weight(1f)
                )
                Text(
                    text = task.status,
                    style = MaterialTheme.typography.labelSmall,
                    color = statusColor
                )
            }

            Spacer(Modifier.height(4.dp))

            // Progress
            Text(
                text = task.chapterProgressDisplay,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Spacer(Modifier.height(4.dp))

            LinearProgressIndicator(
                progress = { progressPercent / 100f },
                modifier = Modifier.fillMaxWidth(),
                color = statusColor,
                trackColor = MaterialTheme.colorScheme.surfaceVariant
            )

            Spacer(Modifier.height(4.dp))

            // Auto prefetch tag
            val prefetchTag = task.autoPrefetchTagDisplay
            if (prefetchTag.isNotEmpty()) {
                Text(
                    text = prefetchTag,
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.secondary
                )
                Spacer(Modifier.height(4.dp))
            }

            // Progress percent
            Text(
                text = "进度：${progressPercent}%",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant
            )

            Spacer(Modifier.height(8.dp))

            // Action buttons
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                if (task.canPause) {
                    IconButton(onClick = onPause, modifier = Modifier.size(32.dp)) {
                        Icon(Icons.Outlined.Pause, "暂停", modifier = Modifier.size(20.dp))
                    }
                }
                if (task.canResume) {
                    IconButton(onClick = onResume, modifier = Modifier.size(32.dp)) {
                        Icon(Icons.Outlined.PlayArrow, "恢复", modifier = Modifier.size(20.dp))
                    }
                }
                if (task.canRetry) {
                    IconButton(onClick = onRetry, modifier = Modifier.size(32.dp)) {
                        Icon(Icons.Outlined.Refresh, "重试", modifier = Modifier.size(20.dp))
                    }
                }
                if (task.canCancel) {
                    IconButton(onClick = onCancel, modifier = Modifier.size(32.dp)) {
                        Icon(Icons.Outlined.Cancel, "取消", modifier = Modifier.size(20.dp))
                    }
                }
                if (task.canDelete) {
                    IconButton(onClick = onDelete, modifier = Modifier.size(32.dp)) {
                        Icon(Icons.Outlined.Delete, "删除", modifier = Modifier.size(20.dp))
                    }
                }
            }
        }
    }
}
