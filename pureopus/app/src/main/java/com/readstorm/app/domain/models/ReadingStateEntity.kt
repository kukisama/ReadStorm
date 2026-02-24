package com.readstorm.app.domain.models

data class ReadingStateEntity(
    var bookId: String = "",
    var chapterIndex: Int = 0,
    var pageIndex: Int = 0,
    var anchorText: String? = null,
    var layoutFingerprint: String? = null,
    var updatedAt: Long = System.currentTimeMillis()
)
