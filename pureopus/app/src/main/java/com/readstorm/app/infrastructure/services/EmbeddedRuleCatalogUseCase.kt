package com.readstorm.app.infrastructure.services

import android.content.Context
import com.readstorm.app.application.abstractions.IRuleCatalogUseCase
import com.readstorm.app.domain.models.BookSourceRule

class EmbeddedRuleCatalogUseCase(private val context: Context) : IRuleCatalogUseCase {

    override suspend fun getAll(): List<BookSourceRule> {
        val allRules = RuleFileLoader.loadAllRules(context)
        return allRules
            .filter { !isTestRule(it.id, it.name, it.url) }
            .map { rule ->
                BookSourceRule(
                    id = rule.id,
                    name = rule.name.ifBlank { "Rule-${rule.id}" },
                    url = rule.url,
                    searchSupported = rule.search != null
                )
            }
            .sortedBy { it.id }
    }

    private fun isTestRule(id: Int, name: String, url: String): Boolean {
        if (id <= 0) return true
        if (name.contains("template", ignoreCase = true) ||
            name.contains("unavailable", ignoreCase = true) ||
            name.contains("示例", ignoreCase = true)
        ) return true
        if (url.contains("example-source", ignoreCase = true)) return true
        return false
    }
}
