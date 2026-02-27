package com.readstorm.app.infrastructure.services

import android.content.Context
import com.readstorm.app.application.abstractions.ISourceHealthCheckUseCase
import com.readstorm.app.application.abstractions.SourceHealthResult
import com.readstorm.app.domain.models.BookSourceRule
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext
import okhttp3.FormBody
import okhttp3.OkHttpClient
import okhttp3.Request
import java.net.URLEncoder

class FastSourceHealthCheckUseCase(private val context: Context) : ISourceHealthCheckUseCase {

    private val httpClient: OkHttpClient = RuleHttpHelper.createHealthCheckHttpClient()

    override suspend fun checkAll(sources: List<BookSourceRule>): List<SourceHealthResult> =
        coroutineScope {
            sources
                .filter { it.id > 0 && it.url.isNotBlank() }
                .map { source -> async { pingOne(source.id, source.url) } }
                .awaitAll()
        }

    private suspend fun pingOne(sourceId: Int, url: String): SourceHealthResult =
        withContext(Dispatchers.IO) {
            try {
                val request = buildSearchProbeRequest(sourceId, url, "测试")
                    ?: return@withContext SourceHealthResult(sourceId, false)
                val response = httpClient.newCall(request).execute()
                response.use { resp ->
                    SourceHealthResult(sourceId, resp.code in 200..399)
                }
            } catch (_: Exception) {
                SourceHealthResult(sourceId, false)
            }
        }

    private fun buildSearchProbeRequest(sourceId: Int, fallbackUrl: String, keyword: String): Request? {
        val rule = try {
            RuleFileLoader.loadRule(context, sourceId)
        } catch (_: Exception) {
            null
        }
        val search = rule?.search
        if (search == null || search.url.isBlank()) {
            return Request.Builder().url(fallbackUrl).get().build()
        }

        val escapedKeyword = URLEncoder.encode(keyword, "UTF-8")
        val resolvedUrl = if (search.url.contains("%s")) {
            search.url.replace("%s", escapedKeyword)
        } else search.url

        val isPost = search.method.equals("post", ignoreCase = true)
        val builder = Request.Builder().url(resolvedUrl)

        if (search.cookies.isNotBlank()) {
            builder.header("Cookie", search.cookies)
        }

        if (isPost) {
            val payload = buildFormBody(search.data, keyword)
            builder.post(payload)
        }

        return builder.build()
    }

    private fun buildFormBody(rawData: String?, keyword: String): FormBody {
        val fb = FormBody.Builder()
        if (rawData.isNullOrBlank()) {
            fb.add("searchkey", keyword)
            return fb.build()
        }

        var text = rawData.trim()
        if (text.startsWith("{") && text.endsWith("}") && text.length >= 2) {
            text = text.substring(1, text.length - 1)
        }

        val parts = text.split(',').map { it.trim() }.filter { it.isNotBlank() }
        var hasPairs = false
        for (part in parts) {
            val idx = part.indexOf(':')
            if (idx <= 0) continue
            val key = part.substring(0, idx).trim().trim('\'', '"')
            var value = part.substring(idx + 1).trim().trim('\'', '"')
            if (key.isBlank()) continue
            value = value.replace("%s", keyword)
            fb.add(key, value)
            hasPairs = true
        }

        if (!hasPairs) {
            fb.add("searchkey", keyword)
        }

        return fb.build()
    }
}
