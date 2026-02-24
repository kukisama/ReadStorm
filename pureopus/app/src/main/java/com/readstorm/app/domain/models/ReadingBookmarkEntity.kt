package com.readstorm.app.domain.models

data class ReadingBookmarkEntity(
    var bookId: String = "",
    var chapterIndex: Int = 0,
    var pageIndex: Int = 0,
    var chapterTitle: String = "",
    var previewText: String? = null,
    var anchorText: String? = null,
    var createdAt: Long = System.currentTimeMillis()
) {
    val display: String
        get() = "$chapterTitle · 第 ${pageIndex + 1} 页"
}
