package com.readstorm.app.infrastructure.services

import android.content.Context
import com.google.gson.Gson
import com.readstorm.app.domain.models.FullBookSourceRule
import java.io.File

object RuleFileLoader {

    private val gson = Gson()

    fun loadAllRules(context: Context): List<FullBookSourceRule> {
        val rules = mutableListOf<FullBookSourceRule>()

        // Load embedded rules from assets
        for (name in RulePathResolver.getEmbeddedRuleNames(context)) {
            try {
                context.assets.open(name).bufferedReader().use { reader ->
                    val rule = gson.fromJson(reader, FullBookSourceRule::class.java)
                    if (rule != null) rules.add(rule)
                }
            } catch (_: Exception) {
                // Skip malformed rule files
            }
        }

        // Load user rules from files directory
        val userDir = File(RulePathResolver.getUserRulesDirectory(context))
        if (userDir.exists() && userDir.isDirectory) {
            userDir.listFiles { file ->
                file.isFile && file.name.startsWith("rule-") && file.name.endsWith(".json")
            }?.forEach { file ->
                try {
                    val rule = gson.fromJson(file.readText(), FullBookSourceRule::class.java)
                    if (rule != null) {
                        // User rules override embedded rules with the same id
                        rules.removeAll { it.id == rule.id }
                        rules.add(rule)
                    }
                } catch (_: Exception) {
                    // Skip malformed rule files
                }
            }
        }

        return rules.sortedBy { it.id }
    }

    fun loadRule(context: Context, ruleId: Int): FullBookSourceRule? {
        // Check user rules first
        val userFile = File(
            RulePathResolver.getUserRulesDirectory(context),
            "rule-$ruleId.json"
        )
        if (userFile.exists()) {
            try {
                return gson.fromJson(userFile.readText(), FullBookSourceRule::class.java)
            } catch (_: Exception) {
                // Fall through to embedded rules
            }
        }

        // Check embedded rules in assets
        val assetName = "rule-$ruleId.json"
        return try {
            context.assets.open(assetName).bufferedReader().use { reader ->
                gson.fromJson(reader, FullBookSourceRule::class.java)
            }
        } catch (_: Exception) {
            null
        }
    }
}
