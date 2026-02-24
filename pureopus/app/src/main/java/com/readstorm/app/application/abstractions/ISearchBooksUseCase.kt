package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.SearchResult

interface ISearchBooksUseCase {
    suspend fun execute(keyword: String, sourceId: Int? = null): List<SearchResult>
}
