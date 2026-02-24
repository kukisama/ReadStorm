package com.readstorm.app.domain.models

data class ChapterEntity(
    var id: Long = 0L,
    var bookId: String = "",
    var indexNo: Int = 0,
    var title: String = "",
    var content: String? = null,
    var status: ChapterStatus = ChapterStatus.Pending,
    var sourceId: Int = 0,
    var sourceUrl: String = "",
    var error: String? = null,
    var updatedAt: Long = System.currentTimeMillis()
)
