package com.readstorm.app.domain.models

data class BookSourceRule(
    val id: Int,
    val name: String = "",
    val url: String = "",
    val searchSupported: Boolean = true
)
