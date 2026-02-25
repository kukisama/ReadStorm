package com.readstorm.app.domain.models

enum class DownloadTaskStatus(val value: Int) {
    Queued(1),
    Downloading(2),
    Succeeded(3),
    Failed(4),
    Cancelled(5),
    Paused(6);

    companion object {
        fun fromValue(value: Int): DownloadTaskStatus =
            entries.firstOrNull { it.value == value } ?: Queued
    }
}
