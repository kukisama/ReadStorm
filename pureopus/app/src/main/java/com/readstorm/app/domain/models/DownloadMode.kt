package com.readstorm.app.domain.models

enum class DownloadMode(val value: Int) {
    FullBook(1),
    Range(2),
    LatestN(3);

    companion object {
        fun fromValue(value: Int): DownloadMode =
            entries.firstOrNull { it.value == value } ?: FullBook
    }
}
