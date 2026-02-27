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
import okhttp3.OkHttpClient
import okhttp3.Request

/**
 * 轻量级健康检查：仅 GET 书源首页 URL，检查 HTTP 状态码 200–399。
 * 不加载规则文件、不构造搜索请求，确保秒级返回。
 */
class FastSourceHealthCheckUseCase(private val context: Context) : ISourceHealthCheckUseCase {

    private val httpClient: OkHttpClient = RuleHttpHelper.createHealthCheckHttpClient(perSourceTimeoutSeconds = 3)

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
                val request = Request.Builder().url(url).get().build()
                val response = httpClient.newCall(request).execute()
                response.use { resp ->
                    SourceHealthResult(sourceId, resp.code in 200..399)
                }
            } catch (_: Exception) {
                SourceHealthResult(sourceId, false)
            }
        }
}
