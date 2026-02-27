package com.readstorm.app.infrastructure.services

import android.content.Context
import com.readstorm.app.application.abstractions.IRuleCatalogUseCase
import com.readstorm.app.application.abstractions.ISearchBooksUseCase
import com.readstorm.app.domain.models.SearchResult
import kotlinx.coroutines.*
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit

/**
 * Hybrid search: delegates to RuleBasedSearchBooksUseCase for single-source,
 * or searches all searchable sources concurrently when sourceId is null/0.
 */
class HybridSearchBooksUseCase(
    private val context: Context,
    private val catalog: IRuleCatalogUseCase
) : ISearchBooksUseCase {

    private val ruleBased = RuleBasedSearchBooksUseCase(context)

    companion object {
        private const val MAX_CONCURRENT_SOURCES = 5
        private const val PER_SOURCE_TIMEOUT_MS = 12_000L
    }

    override suspend fun execute(keyword: String, sourceId: Int?): List<SearchResult> {
        if (keyword.isBlank()) return emptyList()

        // Single source search
        if (sourceId != null && sourceId > 0) {
            return try {
                ruleBased.execute(keyword, sourceId)
            } catch (e: Exception) {
                AppLogger.log("HybridSearch", "Single source search failed (sourceId=$sourceId): ${e.message}")
                emptyList()
            }
        }

        // All sources search
        return searchAllSources(keyword)
    }

    private suspend fun searchAllSources(keyword: String): List<SearchResult> {
        val allRules = catalog.getAll()
        val searchableRules = allRules.filter { it.searchSupported && it.id > 0 }
        if (searchableRules.isEmpty()) return emptyList()

        val semaphore = Semaphore(MAX_CONCURRENT_SOURCES)

        val results = coroutineScope {
            searchableRules.map { rule ->
                async(Dispatchers.IO) {
                    semaphore.withPermit {
                        try {
                            withTimeout(PER_SOURCE_TIMEOUT_MS) {
                                ruleBased.execute(keyword, rule.id)
                            }
                        } catch (e: Exception) {
                            AppLogger.log("HybridSearch", "Source ${rule.id} failed: ${e.message}")
                            emptyList()
                        }
                    }
                }
            }.awaitAll()
        }

        // Deduplicate by title|author, take top 100
        return results.flatten()
            .distinctBy { "${it.title}|${it.author}".lowercase() }
            .take(100)
    }
}
