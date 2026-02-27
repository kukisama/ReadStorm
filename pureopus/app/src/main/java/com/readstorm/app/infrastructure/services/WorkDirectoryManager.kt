package com.readstorm.app.infrastructure.services

import android.content.ContentValues
import android.content.Context
import android.os.Build
import android.os.Environment
import android.provider.MediaStore
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

    fun exportToPublicDownloads(
        context: Context,
        sourceFile: File,
        displayName: String = sourceFile.name,
        subDir: String = "ReadStorm"
    ): String {
        require(sourceFile.exists()) { "源文件不存在：${sourceFile.absolutePath}" }

        return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            val resolver = context.contentResolver
            val values = ContentValues().apply {
                put(MediaStore.Downloads.DISPLAY_NAME, displayName)
                put(MediaStore.Downloads.MIME_TYPE, "application/octet-stream")
                put(MediaStore.Downloads.RELATIVE_PATH, "${Environment.DIRECTORY_DOWNLOADS}/$subDir")
            }
            val uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values)
                ?: throw IllegalStateException("无法创建下载项")

            resolver.openOutputStream(uri)?.use { output ->
                sourceFile.inputStream().use { input -> input.copyTo(output) }
            } ?: throw IllegalStateException("无法写入下载项")

            uri.toString()
        } else {
            @Suppress("DEPRECATION")
            val publicDir = File(
                Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS),
                subDir
            )
            if (!publicDir.exists()) publicDir.mkdirs()
            val target = File(publicDir, displayName)
            sourceFile.copyTo(target, overwrite = true)
            target.absolutePath
        }
    }
}
