package com.readstorm.app.domain.models

import java.util.UUID

data class BookEntity(
    var id: String = UUID.randomUUID().toString(),
    var title: String = "",
    var author: String = "",
    var sourceId: Int = 0,
    var tocUrl: String = "",
    var totalChapters: Int = 0,
    var doneChapters: Int = 0,
    var readChapterIndex: Int = 0,
    var readChapterTitle: String = "",
    var createdAt: Long = System.currentTimeMillis(),
    var updatedAt: Long = System.currentTimeMillis(),
    var readAt: Long = 0L,
    var coverUrl: String? = null,
    var coverImage: String? = null,
    var coverBlob: ByteArray? = null,
    var coverRule: String? = null
) {
    val progressPercent: Int
        get() = if (totalChapters > 0) {
            (100.0 * doneChapters.coerceIn(0, totalChapters) / totalChapters).toInt()
        } else 0

    val isComplete: Boolean
        get() = totalChapters > 0 && doneChapters >= totalChapters

    val titleInitial: String
        get() = if (title.isBlank()) "書" else title.take(1)

    val hasCover: Boolean
        get() = (coverBlob != null && coverBlob!!.isNotEmpty()) ||
                !coverImage.isNullOrBlank()

    val canResume: Boolean
        get() = !isComplete && !isDownloading

    @Transient
    var isDownloading: Boolean = false

    override fun equals(other: Any?): Boolean {
        if (this === other) return true
        if (other !is BookEntity) return false
        return id == other.id &&
                title == other.title &&
                author == other.author &&
                sourceId == other.sourceId &&
                tocUrl == other.tocUrl &&
                totalChapters == other.totalChapters &&
                doneChapters == other.doneChapters &&
                readChapterIndex == other.readChapterIndex &&
                readChapterTitle == other.readChapterTitle &&
                createdAt == other.createdAt &&
                updatedAt == other.updatedAt &&
                readAt == other.readAt &&
                coverUrl == other.coverUrl &&
                coverImage == other.coverImage &&
                coverBlob.contentEquals(other.coverBlob) &&
                coverRule == other.coverRule
    }

    override fun hashCode(): Int {
        var result = id.hashCode()
        result = 31 * result + title.hashCode()
        result = 31 * result + author.hashCode()
        result = 31 * result + sourceId
        result = 31 * result + tocUrl.hashCode()
        result = 31 * result + totalChapters
        result = 31 * result + doneChapters
        result = 31 * result + readChapterIndex
        result = 31 * result + readChapterTitle.hashCode()
        result = 31 * result + createdAt.hashCode()
        result = 31 * result + updatedAt.hashCode()
        result = 31 * result + readAt.hashCode()
        result = 31 * result + (coverUrl?.hashCode() ?: 0)
        result = 31 * result + (coverImage?.hashCode() ?: 0)
        result = 31 * result + (coverBlob?.contentHashCode() ?: 0)
        result = 31 * result + (coverRule?.hashCode() ?: 0)
        return result
    }
}
