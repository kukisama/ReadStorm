package com.readstorm.app.infrastructure.services

import android.content.Context
import com.google.gson.Gson
import com.google.gson.GsonBuilder
import com.readstorm.app.application.abstractions.IAppSettingsUseCase
import com.readstorm.app.domain.models.AppSettings
import java.io.File

class JsonFileAppSettingsUseCase(context: Context) : IAppSettingsUseCase {

    private val filePath: String = WorkDirectoryManager.getSettingsFilePath(context)
    private val gson: Gson = GsonBuilder().setPrettyPrinting().create()

    override suspend fun load(): AppSettings {
        val file = File(filePath)
        if (!file.exists()) return AppSettings()
        return try {
            val json = file.readText()
            gson.fromJson(json, AppSettings::class.java) ?: AppSettings()
        } catch (_: Exception) {
            AppSettings()
        }
    }

    override suspend fun save(settings: AppSettings) {
        val file = File(filePath)
        file.parentFile?.let { if (!it.exists()) it.mkdirs() }
        file.writeText(gson.toJson(settings))
    }
}
