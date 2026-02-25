package com.readstorm.app.domain.models

data class CoverCandidate(
    var index: Int = 0,
    var imageUrl: String = "",
    var rule: String = "",
    var htmlSnippet: String = ""
) {
    val display: String
        get() = "[$index] $rule | $imageUrl"
}
