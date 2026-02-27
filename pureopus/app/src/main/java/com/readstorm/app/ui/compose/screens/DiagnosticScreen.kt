package com.readstorm.app.ui.compose.screens

import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.livedata.observeAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.readstorm.app.ui.compose.components.RsEmptyState
import com.readstorm.app.ui.compose.components.RsLoading
import com.readstorm.app.ui.viewmodels.MainViewModel
import kotlinx.coroutines.launch

/**
 * "诊断"页面 Compose Screen。
 */
@Composable
fun DiagnosticScreen(mainViewModel: MainViewModel) {
    val isDiagnosing by mainViewModel.diagnostic.isDiagnosing.observeAsState(false)
    val summary by mainViewModel.diagnostic.diagnosticSummary.observeAsState("")
    val diagnosticLines by mainViewModel.diagnostic.diagnosticLines.observeAsState(emptyList())
    val sourceNames by mainViewModel.diagnostic.diagnosticSourceNames.observeAsState(emptyList())
    val selectedSource by mainViewModel.diagnostic.selectedDiagnosticSource.observeAsState()
    val scope = rememberCoroutineScope()

    Column(modifier = Modifier.fillMaxSize()) {
        // Header + button
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            Text(
                text = "书源诊断",
                style = MaterialTheme.typography.titleLarge,
                modifier = Modifier.weight(1f)
            )
            Button(
                onClick = { mainViewModel.diagnostic.launchBatchDiagnostic() },
                enabled = !isDiagnosing,
                shape = MaterialTheme.shapes.small
            ) {
                Icon(Icons.Outlined.PlayArrow, contentDescription = null, modifier = Modifier.size(18.dp))
                Spacer(Modifier.width(4.dp))
                Text("全部诊断")
            }
        }

        // Summary
        if (summary.isNotEmpty()) {
            Card(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 4.dp),
                shape = MaterialTheme.shapes.medium,
                colors = CardDefaults.cardColors(
                    containerColor = MaterialTheme.colorScheme.primaryContainer
                )
            ) {
                Text(
                    text = summary,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onPrimaryContainer,
                    modifier = Modifier.padding(12.dp)
                )
            }
        }

        if (isDiagnosing) {
            RsLoading(message = "正在诊断书源…")
        } else if (sourceNames.isEmpty() && diagnosticLines.isEmpty()) {
            RsEmptyState(
                message = "点击「全部诊断」开始",
                description = "将逐一检测所有书源的可用性",
                icon = Icons.Outlined.MonitorHeart
            )
        } else {
            if (diagnosticLines.isNotEmpty()) {
                Card(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 4.dp),
                    shape = MaterialTheme.shapes.medium,
                    colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surfaceVariant)
                ) {
                    Column(
                        modifier = Modifier
                            .fillMaxWidth()
                            .heightIn(min = 120.dp, max = 240.dp)
                            .verticalScroll(rememberScrollState())
                            .padding(12.dp)
                    ) {
                        Text(
                            text = "诊断详情",
                            style = MaterialTheme.typography.titleSmall,
                            color = MaterialTheme.colorScheme.primary
                        )
                        Spacer(Modifier.height(6.dp))
                        diagnosticLines.forEach { line ->
                            val lineColor = when {
                                line.contains("Error", ignoreCase = true) ||
                                    line.contains("失败") -> MaterialTheme.colorScheme.error
                                line.contains("Warning", ignoreCase = true) ||
                                    line.contains("警告") -> MaterialTheme.colorScheme.tertiary
                                else -> MaterialTheme.colorScheme.onSurfaceVariant
                            }
                            Text(
                                text = line,
                                style = MaterialTheme.typography.bodySmall,
                                color = lineColor,
                                modifier = Modifier.padding(vertical = 2.dp)
                            )
                        }
                    }
                }
            }

            // Source list + lines
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                contentPadding = PaddingValues(horizontal = 16.dp, vertical = 4.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                // Source names
                if (sourceNames.isNotEmpty()) {
                    items(sourceNames) { name ->
                        val selected = selectedSource == name
                        Card(
                            onClick = {
                                mainViewModel.diagnostic.setSelectedDiagnosticSource(name)
                            },
                            modifier = Modifier.fillMaxWidth(),
                            shape = MaterialTheme.shapes.small,
                            colors = CardDefaults.cardColors(
                                containerColor = if (selected) {
                                    MaterialTheme.colorScheme.primaryContainer
                                } else {
                                    MaterialTheme.colorScheme.surface
                                }
                            )
                        ) {
                            Text(
                                text = name,
                                style = MaterialTheme.typography.bodyMedium,
                                modifier = Modifier.padding(12.dp)
                            )
                        }
                    }
                }
            }
        }
    }
}
