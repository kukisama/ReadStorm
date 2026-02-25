package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.BookSourceRule

data class SourceHealthResult(
    val sourceId: Int,
    val isReachable: Boolean
)

interface ISourceHealthCheckUseCase {
    suspend fun checkAll(sources: List<BookSourceRule>): List<SourceHealthResult>
}
