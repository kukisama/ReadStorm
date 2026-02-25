package com.readstorm.app.ui.viewmodels

data class RuleListItem(
    val id: Int,
    val name: String,
    val url: String,
    val hasSearch: Boolean,
    var isHealthy: Boolean? = null
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
    val display: String get() = "[$id] $name"
}
