package com.readstorm.app.ui.compose.screens

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.outlined.OpenInNew
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.livedata.observeAsState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.readstorm.app.ui.viewmodels.MainViewModel

/**
 * "关于"页面 Compose Screen。
 */
@Composable
fun AboutScreen(mainViewModel: MainViewModel) {
    val context = LocalContext.current
    val version by mainViewModel.settings.aboutVersion.observeAsState("未知版本")
    val aboutContent by mainViewModel.settings.aboutContent.observeAsState("暂无版本说明。")

    val versionName = remember {
        try {
            context.packageManager.getPackageInfo(context.packageName, 0).versionName ?: "1.0.0"
        } catch (_: Exception) { "1.0.0" }
    }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        horizontalAlignment = Alignment.CenterHorizontally
    ) {
        Spacer(Modifier.height(24.dp))

        // App name
        Text(
            text = "ReadStorm",
            style = MaterialTheme.typography.headlineMedium,
            color = MaterialTheme.colorScheme.primary
        )

        Spacer(Modifier.height(8.dp))

        // Version
        Text(
            text = "版本 $versionName",
            style = MaterialTheme.typography.bodyLarge,
            color = MaterialTheme.colorScheme.onSurfaceVariant
        )

        Spacer(Modifier.height(24.dp))

        // GitHub button
        OutlinedButton(
            onClick = {
                val intent = Intent(Intent.ACTION_VIEW,
                    Uri.parse("https://github.com/clayouuz/ReadStorm"))
                context.startActivity(intent)
            },
            shape = MaterialTheme.shapes.small
        ) {
            Icon(
                imageVector = Icons.AutoMirrored.Outlined.OpenInNew,
                contentDescription = null,
                modifier = Modifier.size(18.dp)
            )
            Spacer(Modifier.width(8.dp))
            Text("GitHub 项目主页")
        }

        Spacer(Modifier.height(32.dp))

        // Release notes
        Card(
            modifier = Modifier.fillMaxWidth(),
            shape = MaterialTheme.shapes.medium,
            colors = CardDefaults.cardColors(
                containerColor = MaterialTheme.colorScheme.surfaceVariant
            )
        ) {
            Column(modifier = Modifier.padding(16.dp)) {
                Text(
                    text = "更新日志",
                    style = MaterialTheme.typography.titleMedium,
                    color = MaterialTheme.colorScheme.onSurface,
                    modifier = Modifier.padding(bottom = 12.dp)
                )

                val releaseNotes = remember {
                    try {
                        context.assets.open("RELEASE_NOTES.md").bufferedReader().readText()
                    } catch (_: Exception) {
                        "暂无更新日志"
                    }
                }

                Text(
                    text = releaseNotes,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant
                )
            }
        }

        Spacer(Modifier.height(16.dp))
    }
}
