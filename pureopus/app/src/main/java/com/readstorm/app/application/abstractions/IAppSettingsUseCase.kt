package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.AppSettings

interface IAppSettingsUseCase {
    suspend fun load(): AppSettings

    suspend fun save(settings: AppSettings)
}
