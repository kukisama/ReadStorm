package com.readstorm.app.ui.viewmodels

import com.readstorm.app.domain.models.BookSourceRule

data class SourceItem(
    val id: Int,
    val name: String,
    val url: String,
    val searchSupported: Boolean,
    var isHealthy: Boolean? = null  // null=unknown, true=reachable, false=unreachable
) {
    val healthDot: String get() = when (isHealthy) {
        true -> "●"
        false -> "●"
        null -> "○"
    }
    val healthColor: String get() = when (isHealthy) {
        true -> "#22C55E"
        false -> "#EF4444"
        null -> "#9CA3AF"
    }
    val displayName: String get() = "$healthDot $name"

    companion object {
        fun fromRule(rule: BookSourceRule) = SourceItem(
            id = rule.id, name = rule.name, url = rule.url, searchSupported = rule.searchSupported
        )
    }
}
