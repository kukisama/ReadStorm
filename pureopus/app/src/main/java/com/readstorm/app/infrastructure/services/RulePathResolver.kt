package com.readstorm.app.infrastructure.services

import android.content.Context

object RulePathResolver {

    fun getUserRulesDirectory(context: Context): String =
        WorkDirectoryManager.getUserRulesDirectory(context)

    fun getEmbeddedRuleNames(context: Context): List<String> {
        return try {
            context.assets.list("")
                ?.filter { it.startsWith("rule-") && it.endsWith(".json") }
                ?.sorted()
                ?: emptyList()
        } catch (_: Exception) {
            emptyList()
        }
    }
}
