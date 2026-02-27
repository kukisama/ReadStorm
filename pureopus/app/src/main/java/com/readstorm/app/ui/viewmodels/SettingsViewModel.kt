package com.readstorm.app.ui.viewmodels

import android.content.Intent
import android.net.Uri
import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import com.readstorm.app.application.abstractions.IAppSettingsUseCase
import com.readstorm.app.domain.models.AppSettings
import com.readstorm.app.infrastructure.services.AppLogger
import com.readstorm.app.infrastructure.services.WorkDirectoryManager
import kotlinx.coroutines.*
import java.io.File

class SettingsViewModel(
    private val parent: MainViewModel,
    private val appSettingsUseCase: IAppSettingsUseCase
) {
    // ── Observable Properties ──
    private val _downloadPath = MutableLiveData("")
    val downloadPath: LiveData<String> = _downloadPath

    private val _saveFeedback = MutableLiveData("")
    val saveFeedback: LiveData<String> = _saveFeedback

    private val _aboutVersion = MutableLiveData("未知版本")
    val aboutVersion: LiveData<String> = _aboutVersion

    private val _aboutContent = MutableLiveData("暂无版本说明。")
    val aboutContent: LiveData<String> = _aboutContent

    // ── Settings Fields ──
    var maxConcurrency: Int = 10
    var aggregateSearchMaxConcurrency: Int = 10
    var minIntervalMs: Int = 200
    var maxIntervalMs: Int = 400
    var exportFormat: String = "txt"
    var enableDiagnosticLog: Boolean = false
    var autoResumeAndRefreshOnStartup: Boolean = false
    var readerAutoPrefetchEnabled: Boolean = true
    var readerPrefetchBatchSize: Int = 10
    var readerPrefetchLowWatermark: Int = 4
    var bookshelfProgressLeftPaddingPx: Int = 5
    var bookshelfProgressRightPaddingPx: Int = 5
    var bookshelfProgressTotalWidthPx: Int = 106
    var bookshelfProgressMinWidthPx: Int = 72

    private var isLoadingSettings = false
    private var autoSaveJob: Job? = null

    companion object {
        val EXPORT_FORMAT_OPTIONS = listOf("txt", "epub")
    }

    // ── Load Settings ──

    suspend fun loadSettings() {
        isLoadingSettings = true
        try {
            val settings = appSettingsUseCase.load()
            val context = parent.getApplication<android.app.Application>()
            val workDir = WorkDirectoryManager.getDefaultWorkDirectory(context)
            _downloadPath.postValue(workDir)

            maxConcurrency = settings.maxConcurrency
            aggregateSearchMaxConcurrency = settings.aggregateSearchMaxConcurrency
            minIntervalMs = settings.minIntervalMs
            maxIntervalMs = settings.maxIntervalMs
            exportFormat = settings.exportFormat
            enableDiagnosticLog = settings.enableDiagnosticLog
            autoResumeAndRefreshOnStartup = settings.autoResumeAndRefreshOnStartup
            readerAutoPrefetchEnabled = settings.readerAutoPrefetchEnabled
            readerPrefetchBatchSize = settings.readerPrefetchBatchSize
            readerPrefetchLowWatermark = settings.readerPrefetchLowWatermark
            bookshelfProgressLeftPaddingPx = settings.bookshelfProgressLeftPaddingPx
            bookshelfProgressRightPaddingPx = settings.bookshelfProgressRightPaddingPx
            bookshelfProgressTotalWidthPx = settings.bookshelfProgressTotalWidthPx
            bookshelfProgressMinWidthPx = settings.bookshelfProgressMinWidthPx

            // Sync reader settings
            val reader = parent.reader
            reader.readerFontSize = settings.readerFontSize
            reader.readerLineHeight = settings.readerLineHeight
            reader.readerParagraphSpacing = settings.readerParagraphSpacing
            reader.readerBackground = settings.readerBackground
            reader.readerForeground = settings.readerForeground
            reader.isDarkMode = settings.readerDarkMode
            reader.readerExtendIntoCutout = settings.readerExtendIntoCutout
            reader.readerContentMaxWidth = settings.readerContentMaxWidth
            reader.readerUseVolumeKeyPaging = settings.readerUseVolumeKeyPaging
            reader.readerUseSwipePaging = settings.readerUseSwipePaging
            reader.readerHideSystemStatusBar = settings.readerHideSystemStatusBar
            reader.readerAutoPrefetchEnabled = settings.readerAutoPrefetchEnabled
            reader.readerPrefetchBatchSize = settings.readerPrefetchBatchSize
            reader.readerPrefetchLowWatermark = settings.readerPrefetchLowWatermark
            reader.readerTopReservePx = settings.readerTopReservePx.toDouble()
            reader.readerBottomReservePx = settings.readerBottomReservePx.toDouble()
            reader.readerBottomStatusBarReservePx = settings.readerBottomStatusBarReservePx.toDouble()
            reader.readerHorizontalInnerReservePx = settings.readerHorizontalInnerReservePx.toDouble()
            reader.readerSidePaddingPx = settings.readerSidePaddingPx.toDouble()

            // Load about info
            loadAboutInfo()

            AppLogger.isEnabled = settings.enableDiagnosticLog
        } finally {
            isLoadingSettings = false
        }
    }

    // ── Save Settings ──

    suspend fun saveSettings(showStatus: Boolean = true) {
        val reader = parent.reader
        val settings = AppSettings(
            downloadPath = _downloadPath.value ?: "",
            maxConcurrency = maxConcurrency,
            aggregateSearchMaxConcurrency = aggregateSearchMaxConcurrency,
            minIntervalMs = minIntervalMs,
            maxIntervalMs = maxIntervalMs,
            exportFormat = exportFormat,
            enableDiagnosticLog = enableDiagnosticLog,
            autoResumeAndRefreshOnStartup = autoResumeAndRefreshOnStartup,
            readerAutoPrefetchEnabled = readerAutoPrefetchEnabled,
            readerPrefetchBatchSize = readerPrefetchBatchSize,
            readerPrefetchLowWatermark = readerPrefetchLowWatermark,
            readerFontSize = reader.readerFontSize,
            readerFontName = reader.selectedFontName,
            readerLineHeight = reader.readerLineHeight,
            readerParagraphSpacing = reader.readerParagraphSpacing,
            readerBackground = reader.readerBackground,
            readerForeground = reader.readerForeground,
            readerDarkMode = reader.isDarkMode,
            readerExtendIntoCutout = reader.readerExtendIntoCutout,
            readerContentMaxWidth = reader.readerContentMaxWidth,
            readerUseVolumeKeyPaging = reader.readerUseVolumeKeyPaging,
            readerUseSwipePaging = reader.readerUseSwipePaging,
            readerHideSystemStatusBar = reader.readerHideSystemStatusBar,
            readerTopReservePx = reader.readerTopReservePx.toInt(),
            readerBottomReservePx = reader.readerBottomReservePx.toInt(),
            readerBottomStatusBarReservePx = reader.readerBottomStatusBarReservePx.toInt(),
            readerHorizontalInnerReservePx = reader.readerHorizontalInnerReservePx.toInt(),
            readerSidePaddingPx = reader.readerSidePaddingPx.toInt(),
            bookshelfProgressLeftPaddingPx = bookshelfProgressLeftPaddingPx,
            bookshelfProgressRightPaddingPx = bookshelfProgressRightPaddingPx,
            bookshelfProgressTotalWidthPx = bookshelfProgressTotalWidthPx,
            bookshelfProgressMinWidthPx = bookshelfProgressMinWidthPx
        )
        appSettingsUseCase.save(settings)
        AppLogger.isEnabled = settings.enableDiagnosticLog

        if (showStatus) {
            parent.setStatusMessage("设置已保存到本地用户配置文件。")
            _saveFeedback.postValue("✔ 已保存")
            delay(2000)
            _saveFeedback.postValue("")
        }
    }

    fun queueAutoSaveSettings() {
        if (isLoadingSettings) return
        autoSaveJob?.cancel()
        autoSaveJob = CoroutineScope(Dispatchers.IO).launch {
            try {
                delay(500)
                saveSettings(showStatus = false)
            } catch (_: CancellationException) {
            } catch (e: Exception) {
                AppLogger.log("Settings", "AutoSave error: ${e.message}")
            }
        }
    }

    // ── Export Functions ──

    suspend fun exportDiagnosticLog() {
        try {
            val logPath = AppLogger.getCurrentLogFilePath()
            if (logPath.isNullOrBlank()) {
                parent.setStatusMessage("日志文件未初始化。")
                return
            }
            val logFile = File(logPath)

            if (!logFile.exists()) {
                parent.setStatusMessage("日志文件不存在，可能尚未开启诊断日志或无日志内容。")
                return
            }
            val context = parent.getApplication<android.app.Application>()
            val exported = WorkDirectoryManager.exportToPublicDownloads(
                context = context,
                sourceFile = logFile,
                displayName = logFile.name,
                subDir = "ReadStorm"
            )
            parent.setStatusMessage("日志已导出到系统下载目录：$exported")
        } catch (e: Exception) {
            parent.setStatusMessage("导出日志失败：${e.message}")
        }
    }

    suspend fun exportDatabase() {
        try {
            val context = parent.getApplication<android.app.Application>()
            val workDir = WorkDirectoryManager.getDefaultWorkDirectory(context)
            val dbPath = WorkDirectoryManager.getDatabasePath(workDir)
            val dbFile = File(dbPath)

            if (!dbFile.exists()) {
                parent.setStatusMessage("数据库文件不存在，可能尚未初始化。")
                return
            }
            val exported = WorkDirectoryManager.exportToPublicDownloads(
                context = context,
                sourceFile = dbFile,
                displayName = dbFile.name,
                subDir = "ReadStorm"
            )
            parent.setStatusMessage("数据库已导出到系统下载目录：$exported")
        } catch (e: Exception) {
            parent.setStatusMessage("导出数据库失败：${e.message}")
        }
    }

    fun openProjectRepository() {
        try {
            val context = parent.getApplication<android.app.Application>()
            val uri = Uri.parse("https://github.com/kukisama/readstorm")
            val intent = Intent(Intent.ACTION_VIEW, uri).apply {
                addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            }
            context.startActivity(intent)
            parent.setStatusMessage("已在浏览器打开项目主页。")
        } catch (e: Exception) {
            parent.setStatusMessage("打开项目主页失败：${e.message}")
        }
    }

    // ── About Info ──

    private fun loadAboutInfo() {
        try {
            val context = parent.getApplication<android.app.Application>()
            val packageInfo = context.packageManager.getPackageInfo(context.packageName, 0)
            _aboutVersion.postValue(packageInfo.versionName ?: "未知版本")

            // Load release notes from assets
            try {
                val releaseNotes = context.assets.open("RELEASE_NOTES.md").bufferedReader().use { reader ->
                    val fullContent = reader.readText()
                    val endIndex = fullContent.indexOf("### 运行前提")
                    if (endIndex > 0) fullContent.substring(0, endIndex).trim()
                    else fullContent.trim()
                }
                _aboutContent.postValue(releaseNotes)
            } catch (_: Exception) {
                _aboutContent.postValue("暂无版本说明。")
            }
        } catch (_: Exception) {
            _aboutVersion.postValue("未知版本")
        }
    }
}
