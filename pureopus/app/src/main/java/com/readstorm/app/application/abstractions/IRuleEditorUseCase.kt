package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.FullBookSourceRule

data class RuleTestResult(
    val success: Boolean = false,
    val message: String = "",
    val requestUrl: String = "",
    val requestMethod: String = "",
    val requestBody: String = "",
    val selectorLines: List<String> = emptyList(),
    val searchItems: List<String> = emptyList(),
    val tocItems: List<String> = emptyList(),
    val contentPreview: String = "",
    val rawHtml: String = "",
    val matchedHtml: String = "",
    val elapsedMs: Long = 0,
    val diagnosticLines: List<String> = emptyList()
)

interface IRuleEditorUseCase {
    suspend fun loadAll(): List<FullBookSourceRule>

    suspend fun load(ruleId: Int): FullBookSourceRule?

    suspend fun save(rule: FullBookSourceRule)

    suspend fun delete(ruleId: Int): Boolean

    suspend fun resetToDefault(ruleId: Int): Boolean

    fun hasUserOverride(ruleId: Int): Boolean

    suspend fun getNextAvailableId(): Int

    suspend fun testSearch(rule: FullBookSourceRule, keyword: String): RuleTestResult

    suspend fun testToc(rule: FullBookSourceRule, bookUrl: String): RuleTestResult

    suspend fun testChapter(rule: FullBookSourceRule, chapterUrl: String): RuleTestResult
}
