package com.readstorm.app.ui.compose.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Save
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.livedata.observeAsState
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.readstorm.app.ui.viewmodels.MainViewModel
import com.readstorm.app.ui.viewmodels.SettingsViewModel
import kotlinx.coroutines.launch

/**
 * "设置"页面 Compose Screen。
 */
@Composable
fun SettingsScreen(mainViewModel: MainViewModel) {
    val s = mainViewModel.settings
    val downloadPath by s.downloadPath.observeAsState("")
    val saveFeedback by s.saveFeedback.observeAsState("")
    val scope = rememberCoroutineScope()

    // Local state bound to ViewModel fields
    var maxConcurrency by remember { mutableStateOf(s.maxConcurrency.toString()) }
    var searchConcurrency by remember { mutableStateOf(s.aggregateSearchMaxConcurrency.toString()) }
    var enableDiagnosticLog by remember { mutableStateOf(s.enableDiagnosticLog) }
    var autoResume by remember { mutableStateOf(s.autoResumeAndRefreshOnStartup) }
    var autoPrefetch by remember { mutableStateOf(s.readerAutoPrefetchEnabled) }
    var prefetchBatchSize by remember { mutableStateOf(s.readerPrefetchBatchSize.toString()) }
    var lowWatermark by remember { mutableStateOf(s.readerPrefetchLowWatermark.toString()) }
    var progressLeftPadding by remember { mutableStateOf(s.bookshelfProgressLeftPaddingPx.toString()) }
    var progressRightPadding by remember { mutableStateOf(s.bookshelfProgressRightPaddingPx.toString()) }
    var progressTotalWidth by remember { mutableStateOf(s.bookshelfProgressTotalWidthPx.toString()) }
    var progressMinWidth by remember { mutableStateOf(s.bookshelfProgressMinWidthPx.toString()) }
    var exportFormatIndex by remember { mutableStateOf(if (s.exportFormat == "epub") 1 else 0) }
    val exportFormats = SettingsViewModel.EXPORT_FORMAT_OPTIONS

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        // ── 工作目录 ──
        SettingSectionHeader("工作目录")
        OutlinedTextField(
            value = downloadPath,
            onValueChange = {},
            label = { Text("下载路径") },
            readOnly = true,
            modifier = Modifier.fillMaxWidth(),
            singleLine = true
        )

        // ── 下载并发 ──
        SettingSectionHeader("下载设置")
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedTextField(
                value = maxConcurrency,
                onValueChange = { maxConcurrency = it },
                label = { Text("下载并发") },
                modifier = Modifier.weight(1f),
                singleLine = true
            )
            OutlinedTextField(
                value = searchConcurrency,
                onValueChange = { searchConcurrency = it },
                label = { Text("搜索并发") },
                modifier = Modifier.weight(1f),
                singleLine = true
            )
        }

        // ── 导出格式 ──
        SettingSectionHeader("导出设置")
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            exportFormats.forEachIndexed { index, format ->
                FilterChip(
                    selected = exportFormatIndex == index,
                    onClick = { exportFormatIndex = index },
                    label = { Text(format.uppercase()) }
                )
            }
        }

        // ── 开关选项 ──
        SettingSectionHeader("功能开关")
        SettingSwitchRow("启用诊断日志", enableDiagnosticLog) { enableDiagnosticLog = it }
        SettingSwitchRow("启动时自动恢复下载", autoResume) { autoResume = it }
        SettingSwitchRow("阅读器自动预取", autoPrefetch) { autoPrefetch = it }

        // ── 预取设置 ──
        if (autoPrefetch) {
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                OutlinedTextField(
                    value = prefetchBatchSize,
                    onValueChange = { prefetchBatchSize = it },
                    label = { Text("预取批量") },
                    modifier = Modifier.weight(1f),
                    singleLine = true
                )
                OutlinedTextField(
                    value = lowWatermark,
                    onValueChange = { lowWatermark = it },
                    label = { Text("低水位") },
                    modifier = Modifier.weight(1f),
                    singleLine = true
                )
            }
        }

        // ── 书架进度条 ──
        SettingSectionHeader("书架进度条")
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedTextField(
                value = progressLeftPadding,
                onValueChange = { progressLeftPadding = it },
                label = { Text("左边距") },
                modifier = Modifier.weight(1f),
                singleLine = true
            )
            OutlinedTextField(
                value = progressRightPadding,
                onValueChange = { progressRightPadding = it },
                label = { Text("右边距") },
                modifier = Modifier.weight(1f),
                singleLine = true
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            OutlinedTextField(
                value = progressTotalWidth,
                onValueChange = { progressTotalWidth = it },
                label = { Text("总宽度") },
                modifier = Modifier.weight(1f),
                singleLine = true
            )
            OutlinedTextField(
                value = progressMinWidth,
                onValueChange = { progressMinWidth = it },
                label = { Text("最小宽度") },
                modifier = Modifier.weight(1f),
                singleLine = true
            )
        }

        // ── 操作按钮 ──
        Spacer(Modifier.height(8.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Button(
                onClick = {
                    // Collect and save
                    s.maxConcurrency = maxConcurrency.toIntOrNull() ?: s.maxConcurrency
                    s.aggregateSearchMaxConcurrency = searchConcurrency.toIntOrNull() ?: s.aggregateSearchMaxConcurrency
                    s.exportFormat = exportFormats[exportFormatIndex]
                    s.enableDiagnosticLog = enableDiagnosticLog
                    s.autoResumeAndRefreshOnStartup = autoResume
                    s.readerAutoPrefetchEnabled = autoPrefetch
                    s.readerPrefetchBatchSize = prefetchBatchSize.toIntOrNull() ?: s.readerPrefetchBatchSize
                    s.readerPrefetchLowWatermark = lowWatermark.toIntOrNull() ?: s.readerPrefetchLowWatermark
                    s.bookshelfProgressLeftPaddingPx = progressLeftPadding.toIntOrNull() ?: s.bookshelfProgressLeftPaddingPx
                    s.bookshelfProgressRightPaddingPx = progressRightPadding.toIntOrNull() ?: s.bookshelfProgressRightPaddingPx
                    s.bookshelfProgressTotalWidthPx = progressTotalWidth.toIntOrNull() ?: s.bookshelfProgressTotalWidthPx
                    s.bookshelfProgressMinWidthPx = progressMinWidth.toIntOrNull() ?: s.bookshelfProgressMinWidthPx
                    scope.launch { s.saveSettings() }
                },
                shape = MaterialTheme.shapes.small
            ) {
                Icon(Icons.Outlined.Save, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(4.dp))
                Text("保存设置")
            }

            OutlinedButton(
                onClick = { scope.launch { s.exportDiagnosticLog() } },
                shape = MaterialTheme.shapes.small
            ) {
                Text("导出日志")
            }

            OutlinedButton(
                onClick = { scope.launch { s.exportDatabase() } },
                shape = MaterialTheme.shapes.small
            ) {
                Text("导出数据库")
            }
        }

        // Feedback
        if (!saveFeedback.isNullOrEmpty()) {
            Text(
                text = saveFeedback,
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.primary,
                modifier = Modifier.padding(top = 4.dp)
            )
        }

        Spacer(Modifier.height(16.dp))
    }
}

@Composable
private fun SettingSectionHeader(title: String) {
    Text(
        text = title,
        style = MaterialTheme.typography.titleSmall,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(top = 8.dp)
    )
}

@Composable
private fun SettingSwitchRow(
    label: String,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween
    ) {
        Text(
            text = label,
            style = MaterialTheme.typography.bodyLarge,
            color = MaterialTheme.colorScheme.onSurface,
            modifier = Modifier.weight(1f)
        )
        Switch(checked = checked, onCheckedChange = onCheckedChange)
    }
}
