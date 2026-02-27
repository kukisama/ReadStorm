package com.readstorm.app.infrastructure.services

import android.content.Context

object AboutInfoProvider {

    data class AboutInfo(
        val version: String,
        val markdown: String
    )

    private var cached: AboutInfo? = null

    fun get(context: Context): AboutInfo {
        cached?.let { return it }

        val markdown = try {
            context.assets.open("RELEASE_NOTES.md").bufferedReader().use { it.readText() }
        } catch (_: Exception) {
            ""
        }

        val versionRegex = Regex("""#\s*ReadStorm\s+v([\d.]+)""")
        val version = versionRegex.find(markdown)?.groupValues?.get(1) ?: "0.0.0"

        val endMarker = "### 运行前提"
        val trimmed = if (markdown.contains(endMarker)) {
            markdown.substringBefore(endMarker).trimEnd()
        } else {
            markdown
        }

        val info = AboutInfo(version, trimmed)
        cached = info
        return info
    }
}
