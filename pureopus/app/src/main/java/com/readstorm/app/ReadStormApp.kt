package com.readstorm.app

import android.app.Application
import com.readstorm.app.infrastructure.services.AppLogger
import com.readstorm.app.infrastructure.services.WorkDirectoryManager
import java.io.File

class ReadStormApp : Application() {

    companion object {
        private const val TAG = "ReadStormApp"
        private const val PREF_NAME = "readstorm_prefs"
        private const val KEY_RULES_DEPLOYED = "bundled_rules_deployed"
    }

    override fun onCreate() {
        super.onCreate()

        val workDir = WorkDirectoryManager.getDefaultWorkDirectory(this)
        val logsDir = WorkDirectoryManager.getLogsDirectory(workDir)
        AppLogger.init(logsDir)
        AppLogger.log(TAG, "Application starting, workDir=$workDir")

        deployBundledRulesIfNeeded()
        setupUncaughtExceptionHandler()
    }

    private fun deployBundledRulesIfNeeded() {
        val prefs = getSharedPreferences(PREF_NAME, MODE_PRIVATE)
        if (prefs.getBoolean(KEY_RULES_DEPLOYED, false)) return

        try {
            val rulesDir = WorkDirectoryManager.getUserRulesDirectory(this)
            // Embedded rules are in root assets, named rule-*.json
            val assetFiles = try {
                assets.list("")
                    ?.filter { it.startsWith("rule-") && it.endsWith(".json") }
                    ?: emptyList()
            } catch (_: Exception) {
                emptyList()
            }

            for (fileName in assetFiles) {
                val target = File(rulesDir, fileName)
                if (!target.exists()) {
                    assets.open(fileName).use { input ->
                        target.outputStream().use { output ->
                            input.copyTo(output)
                        }
                    }
                }
            }

            prefs.edit().putBoolean(KEY_RULES_DEPLOYED, true).apply()
            AppLogger.log(TAG, "Bundled rules deployed: ${assetFiles.size} file(s)")
        } catch (e: Exception) {
            AppLogger.log(TAG, "Failed to deploy bundled rules: ${e.message}")
        }
    }

    private fun setupUncaughtExceptionHandler() {
        val defaultHandler = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { thread, throwable ->
            try {
                AppLogger.log(
                    "CRASH",
                    "Uncaught exception on ${thread.name}: ${throwable.stackTraceToString()}"
                )
            } catch (_: Exception) {
                // Last-resort: avoid infinite recursion
            }
            defaultHandler?.uncaughtException(thread, throwable)
        }
    }
}
