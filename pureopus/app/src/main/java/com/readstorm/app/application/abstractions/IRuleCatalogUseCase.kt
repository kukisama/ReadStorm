package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.BookSourceRule

interface IRuleCatalogUseCase {
    suspend fun getAll(): List<BookSourceRule>
}
