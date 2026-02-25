package com.readstorm.app.domain.models

import java.util.UUID

data class SearchResult(
    val id: UUID = UUID.randomUUID(),
    val title: String,
    val author: String,
    val sourceId: Int,
    val sourceName: String,
    val url: String,
    val latestChapter: String,
    val updatedAt: Long
)
