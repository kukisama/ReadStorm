package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.BookEntity
import com.readstorm.app.domain.models.CoverCandidate

interface ICoverUseCase {
    suspend fun refreshCover(book: BookEntity): String

    suspend fun getCoverCandidates(book: BookEntity): List<CoverCandidate>

    suspend fun applyCoverCandidate(book: BookEntity, candidate: CoverCandidate): String
}
