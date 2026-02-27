package com.readstorm.app.infrastructure.services

import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.util.concurrent.ConcurrentHashMap

class SourceDownloadQueue {

    private val locks = ConcurrentHashMap<Int, Mutex>()

    suspend fun <T> enqueue(sourceId: Int, work: suspend () -> T): T {
        val mutex = locks.getOrPut(sourceId) { Mutex() }
        return mutex.withLock { work() }
    }

    fun clear() {
        locks.clear()
    }
}
