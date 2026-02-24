package com.readstorm.app.domain.models

data class ReaderAutoDownloadPlan(
    var shouldQueueWindow: Boolean = false,
    var windowStartIndex: Int = 0,
    var windowTakeCount: Int = 0,
    var consecutiveDoneAfterAnchor: Int = 0,
    var hasGap: Boolean = false,
    var firstGapIndex: Int = -1
)
