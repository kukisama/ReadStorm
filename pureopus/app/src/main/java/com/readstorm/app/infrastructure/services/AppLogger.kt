package com.readstorm.app.infrastructure.services

import android.util.Log
import java.io.BufferedWriter
import java.io.File
import java.io.FileWriter
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object AppLogger {

    @Volatile
    var isEnabled: Boolean = false

    private var logFile: File? = null
    private var writer: BufferedWriter? = null
    private val dateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US)

    @Synchronized
    fun init(logsDir: String) {
        close()
        val dir = File(logsDir)
        if (!dir.exists()) dir.mkdirs()
        val fileName = SimpleDateFormat("yyyy-MM-dd", Locale.US).format(Date())
        val file = File(dir, "readstorm-$fileName.log")
        logFile = file
        try {
            writer = BufferedWriter(FileWriter(file, true))
        } catch (_: Exception) {
            // Continue without file logging
        }
    }

    @Synchronized
    fun log(tag: String, message: String) {
        if (isEnabled) {
            Log.d(tag, message)
        }
        try {
            writer?.apply {
                write("${dateFormat.format(Date())} [$tag] $message")
                newLine()
                flush()
            }
        } catch (_: Exception) {
            // Silently ignore write failures to avoid infinite recursion
        }
    }

    @Synchronized
    fun getLogContent(): String {
        val file = logFile ?: return ""
        // Flush pending writes before reading
        try { writer?.flush() } catch (_: Exception) {}
        return try {
            if (file.exists()) file.readText() else ""
        } catch (_: Exception) {
            ""
        }
    }

    @Synchronized
    fun getCurrentLogFilePath(): String? {
        return logFile?.absolutePath
    }

    @Synchronized
    fun clearLogs() {
        val file = logFile ?: return
        try { writer?.flush() } catch (_: Exception) {}
        try {
            FileWriter(file, false).use { it.write("") }
        } catch (_: Exception) {
            // ignore clear failures
        }
    }

    @Synchronized
    private fun close() {
        try { writer?.close() } catch (_: Exception) {}
        writer = null
    }
}
