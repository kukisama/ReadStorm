package com.readstorm.app.domain.models

enum class ChapterStatus(val value: Int) {
    Pending(0),
    Downloading(1),
    Done(2),
    Failed(3);

    companion object {
        fun fromValue(value: Int): ChapterStatus =
            entries.firstOrNull { it.value == value } ?: Pending
    }
}
