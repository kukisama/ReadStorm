package com.readstorm.app.domain.models

enum class DownloadErrorKind(val value: Int) {
    None(0),
    Network(1),
    Rule(2),
    Parse(3),
    IO(4),
    Cancelled(5),
    Unknown(99);

    companion object {
        fun fromValue(value: Int): DownloadErrorKind =
            entries.firstOrNull { it.value == value } ?: None
    }
}
