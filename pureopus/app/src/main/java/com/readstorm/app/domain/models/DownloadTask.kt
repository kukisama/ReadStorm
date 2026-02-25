package com.readstorm.app.domain.models

import java.util.UUID

class DownloadTask(
    val id: String = UUID.randomUUID().toString(),
    var bookId: String = "",
    val bookTitle: String = "",
    val author: String = "",
    val mode: DownloadMode = DownloadMode.FullBook,
    val enqueuedAt: Long = System.currentTimeMillis(),
    var sourceSearchResult: SearchResult? = null,
    var rangeStartIndex: Int? = null,
    var rangeTakeCount: Int? = null,
    var isAutoPrefetch: Boolean = false,
    var autoPrefetchReason: String = ""
) {
    private val listeners = mutableListOf<(String) -> Unit>()
    private val _stateHistory = mutableListOf(DownloadTaskStatus.Queued)

    val stateHistory: List<DownloadTaskStatus> get() = _stateHistory

    var currentStatus: DownloadTaskStatus = DownloadTaskStatus.Queued
        private set(value) {
            if (field != value) {
                field = value
                notifyChanged(
                    "status", "canRetry", "canCancel",
                    "canPause", "canResume", "canDelete"
                )
            }
        }

    val status: String get() = currentStatus.name

    val canRetry: Boolean
        get() = currentStatus == DownloadTaskStatus.Failed ||
                currentStatus == DownloadTaskStatus.Cancelled

    val canCancel: Boolean
        get() = currentStatus == DownloadTaskStatus.Queued ||
                currentStatus == DownloadTaskStatus.Downloading ||
                currentStatus == DownloadTaskStatus.Paused

    val canPause: Boolean
        get() = currentStatus == DownloadTaskStatus.Downloading

    val canResume: Boolean
        get() = currentStatus == DownloadTaskStatus.Paused

    val canDelete: Boolean
        get() = currentStatus != DownloadTaskStatus.Downloading

    var progressPercent: Int = 0
        private set(value) {
            if (field != value) {
                field = value
                notifyChanged("progressPercent")
            }
        }

    var currentChapterIndex: Int = 0
        private set(value) {
            if (field != value) {
                field = value
                notifyChanged("currentChapterIndex", "chapterProgressDisplay")
            }
        }

    var totalChapterCount: Int = 0
        private set(value) {
            if (field != value) {
                field = value
                notifyChanged("totalChapterCount", "chapterProgressDisplay")
            }
        }

    var currentChapterTitle: String = ""
        private set(value) {
            if (field != value) {
                field = value
                notifyChanged("currentChapterTitle", "chapterProgressDisplay")
            }
        }

    val chapterProgressDisplay: String
        get() = if (totalChapterCount > 0) {
            val clamped = currentChapterIndex.coerceIn(0, totalChapterCount)
            "$clamped/$totalChapterCount $currentChapterTitle"
        } else ""

    var startedAt: Long? = null
        private set(value) {
            if (field != value) {
                field = value
                notifyChanged("startedAt")
            }
        }

    var completedAt: Long? = null
        private set(value) {
            if (field != value) {
                field = value
                notifyChanged("completedAt")
            }
        }

    var outputFilePath: String = ""
        set(value) {
            if (field != value) {
                field = value
                notifyChanged("outputFilePath")
            }
        }

    var error: String? = null
        set(value) {
            if (field != value) {
                field = value
                notifyChanged("error", "errorDisplay")
            }
        }

    var errorKind: DownloadErrorKind = DownloadErrorKind.None
        set(value) {
            if (field != value) {
                field = value
                notifyChanged("errorKind", "errorKindDisplay")
            }
        }

    val errorKindDisplay: String
        get() = if (errorKind == DownloadErrorKind.None) "" else "错误类型：$errorKind"

    val errorDisplay: String
        get() = if (error.isNullOrBlank()) "" else "错误：$error"

    var retryCount: Int = 0
        private set(value) {
            if (field != value) {
                field = value
                notifyChanged("retryCount")
            }
        }

    val autoPrefetchTagDisplay: String
        get() {
            if (!isAutoPrefetch) return ""
            val reason = when (autoPrefetchReason) {
                "open" -> "打开书籍"
                "jump" -> "跳章"
                "manual-priority" -> "手动选章优先"
                "low-watermark" -> "低水位补拉"
                "gap-fill" -> "缺口补齐"
                "foreground-direct" -> "前台单章直下"
                else -> autoPrefetchReason.ifBlank { "自动预取" }
            }
            return "自动预取 · $reason"
        }

    fun addPropertyChangeListener(listener: (String) -> Unit) {
        listeners.add(listener)
    }

    fun removePropertyChangeListener(listener: (String) -> Unit) {
        listeners.remove(listener)
    }

    fun transitionTo(nextStatus: DownloadTaskStatus) {
        require(isAllowed(currentStatus, nextStatus)) {
            "非法状态流转: $currentStatus -> $nextStatus"
        }
        currentStatus = nextStatus
        _stateHistory.add(nextStatus)
        notifyChanged("stateHistory")

        if (nextStatus == DownloadTaskStatus.Downloading) {
            if (startedAt == null) startedAt = System.currentTimeMillis()
        }

        if (nextStatus == DownloadTaskStatus.Succeeded ||
            nextStatus == DownloadTaskStatus.Failed ||
            nextStatus == DownloadTaskStatus.Cancelled
        ) {
            completedAt = System.currentTimeMillis()
            if (nextStatus == DownloadTaskStatus.Succeeded) {
                progressPercent = 100
            }
        }
    }

    fun resetForRetry() {
        require(canRetry) { "当前状态 $currentStatus 不允许重试。" }
        retryCount++
        error = null
        errorKind = DownloadErrorKind.None
        progressPercent = 0
        completedAt = null
        currentStatus = DownloadTaskStatus.Queued
        _stateHistory.add(DownloadTaskStatus.Queued)
        notifyChanged("stateHistory")
    }

    fun overrideToPaused() {
        currentStatus = DownloadTaskStatus.Paused
        error = null
        errorKind = DownloadErrorKind.None
        _stateHistory.add(DownloadTaskStatus.Paused)
        notifyChanged(
            "status", "canRetry", "canCancel",
            "canPause", "canResume", "canDelete", "stateHistory"
        )
    }

    fun resetForResume() {
        require(currentStatus == DownloadTaskStatus.Paused) {
            "当前状态 $currentStatus 不允许恢复。"
        }
        currentStatus = DownloadTaskStatus.Queued
        error = null
        errorKind = DownloadErrorKind.None
        completedAt = null
        _stateHistory.add(DownloadTaskStatus.Queued)
        notifyChanged(
            "status", "canRetry", "canCancel",
            "canPause", "canResume", "canDelete", "stateHistory"
        )
    }

    fun updateProgress(percent: Int) {
        progressPercent = percent.coerceIn(0, 100)
    }

    fun updateChapterProgress(currentIndex: Int, totalCount: Int, chapterTitle: String) {
        val safeTotal = maxOf(0, totalCount)
        val safeCurrent = if (safeTotal > 0) {
            currentIndex.coerceIn(0, safeTotal)
        } else {
            maxOf(0, currentIndex)
        }
        currentChapterIndex = safeCurrent
        totalChapterCount = safeTotal
        currentChapterTitle = chapterTitle
    }

    private fun notifyChanged(vararg propertyNames: String) {
        val snapshot = listeners.toList()
        for (name in propertyNames) {
            for (listener in snapshot) {
                listener(name)
            }
        }
    }

    companion object {
        private fun isAllowed(current: DownloadTaskStatus, next: DownloadTaskStatus): Boolean =
            when (current) {
                DownloadTaskStatus.Queued -> next == DownloadTaskStatus.Downloading ||
                        next == DownloadTaskStatus.Cancelled ||
                        next == DownloadTaskStatus.Failed

                DownloadTaskStatus.Downloading -> next == DownloadTaskStatus.Succeeded ||
                        next == DownloadTaskStatus.Failed ||
                        next == DownloadTaskStatus.Cancelled ||
                        next == DownloadTaskStatus.Paused

                DownloadTaskStatus.Paused -> next == DownloadTaskStatus.Downloading ||
                        next == DownloadTaskStatus.Cancelled

                DownloadTaskStatus.Succeeded,
                DownloadTaskStatus.Failed,
                DownloadTaskStatus.Cancelled -> false
            }
    }
}
