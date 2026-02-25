package com.readstorm.app.infrastructure.services

import android.content.Context
import android.os.Build
import android.os.Environment
import java.io.File

object WorkDirectoryManager {

    const val DATABASE_NAME = "readstorm.db"

    fun getDefaultWorkDirectory(context: Context): String {
        val dir = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            context.getExternalFilesDir(null)
                ?: context.filesDir
        } else {
            @Suppress("DEPRECATION")
            File(
                Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS),
                "ReadStorm"
            )
        }
        if (!dir.exists()) dir.mkdirs()
        return dir.absolutePath
    }

    fun getDatabasePath(workDir: String): String =
        "$workDir${File.separator}$DATABASE_NAME"

    fun getSettingsFilePath(context: Context): String {
        val workDir = getDefaultWorkDirectory(context)
        return "$workDir${File.separator}settings.json"
    }

    fun getLogsDirectory(workDir: String): String {
        val logsDir = "$workDir${File.separator}logs"
        File(logsDir).let { if (!it.exists()) it.mkdirs() }
        return logsDir
    }

    fun getUserRulesDirectory(context: Context): String {
        val dir = File(context.filesDir, "rules")
        if (!dir.exists()) dir.mkdirs()
        return dir.absolutePath
    }
}
