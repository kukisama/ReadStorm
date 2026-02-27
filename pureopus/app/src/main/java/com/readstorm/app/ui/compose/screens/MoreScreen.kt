package com.readstorm.app.ui.compose.screens

import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.unit.dp
import com.readstorm.app.ui.compose.theme.ReadStormTheme

/**
 * "更多"页面 Compose Screen。
 * 提供导航入口：诊断 / 规则 / 设置 / 关于 / 日志
 */
@Composable
fun MoreScreen(
    onNavigate: (String) -> Unit = {}
) {
    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp)
    ) {
        Text(
            text = "更多功能",
            style = MaterialTheme.typography.headlineSmall,
            color = MaterialTheme.colorScheme.onSurface,
            modifier = Modifier.padding(bottom = 8.dp)
        )

        MoreMenuItem(
            icon = Icons.Outlined.MonitorHeart,
            title = "诊断",
            subtitle = "检测书源可用性",
            onClick = { onNavigate("diagnostic") }
        )
        MoreMenuItem(
            icon = Icons.Outlined.EditNote,
            title = "规则编辑",
            subtitle = "管理和测试书源规则",
            onClick = { onNavigate("rules") }
        )
        MoreMenuItem(
            icon = Icons.Outlined.Settings,
            title = "设置",
            subtitle = "下载、阅读器、高级选项",
            onClick = { onNavigate("settings") }
        )
        MoreMenuItem(
            icon = Icons.Outlined.Info,
            title = "关于",
            subtitle = "版本信息与更新日志",
            onClick = { onNavigate("about") }
        )
        MoreMenuItem(
            icon = Icons.Outlined.Description,
            title = "日志",
            subtitle = "查看运行日志",
            onClick = { onNavigate("log") }
        )
    }
}

@Composable
private fun MoreMenuItem(
    icon: ImageVector,
    title: String,
    subtitle: String,
    onClick: () -> Unit
) {
    Card(
        onClick = onClick,
        modifier = Modifier.fillMaxWidth(),
        shape = MaterialTheme.shapes.medium,
        colors = CardDefaults.cardColors(
            containerColor = MaterialTheme.colorScheme.surface
        ),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp)
    ) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(16.dp),
            horizontalArrangement = Arrangement.spacedBy(16.dp)
        ) {
            Icon(
                imageVector = icon,
                contentDescription = title,
                tint = MaterialTheme.colorScheme.primary,
                modifier = Modifier.size(28.dp)
            )
            Column(modifier = Modifier.weight(1f)) {
                Text(
                    text = title,
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.onSurface
                )
                Text(
                    text = subtitle,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
            Icon(
                imageVector = Icons.Outlined.ChevronRight,
                contentDescription = null,
                tint = MaterialTheme.colorScheme.onSurfaceVariant
            )
        }
    }
}
