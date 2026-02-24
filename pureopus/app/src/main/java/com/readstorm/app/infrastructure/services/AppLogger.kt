package com.readstorm.app.infrastructure.services

import android.util.Log
import java.io.File
import java.io.FileWriter
import java.io.PrintWriter
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

object AppLogger {

    private var logFile: File? = null
    private val dateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss.SSS", Locale.US)

    @Synchronized
    fun init(logsDir: String) {
        val dir = File(logsDir)
        if (!dir.exists()) dir.mkdirs()
        val fileName = SimpleDateFormat("yyyy-MM-dd", Locale.US).format(Date())
        logFile = File(dir, "readstorm-$fileName.log")
    }

    @Synchronized
    fun log(tag: String, message: String) {
        Log.d(tag, message)
        val file = logFile ?: return
        try {
            PrintWriter(FileWriter(file, true)).use { writer ->
                writer.println("${dateFormat.format(Date())} [$tag] $message")
            }
        } catch (_: Exception) {
            // Silently ignore write failures to avoid infinite recursion
        }
    }

    fun getLogContent(): String {
        val file = logFile ?: return ""
        return try {
            if (file.exists()) file.readText() else ""
        } catch (_: Exception) {
            ""
        }
    }
}
