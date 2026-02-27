package com.readstorm.app.infrastructure.services

import kotlinx.coroutines.delay
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Proxy
import java.net.URI
import java.util.concurrent.TimeUnit

object RuleHttpHelper {

    private const val USER_AGENT =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36"
    private const val HEALTH_CHECK_IO_TIMEOUT_SECONDS = 4L
    private const val INITIAL_RETRY_DELAY_MS = 300L

    // ── Proxy Configuration ──
    @Volatile var proxyEnabled: Boolean = false
    @Volatile var proxyHost: String = ""
    @Volatile var proxyPort: Int = 0

    private fun getProxy(): Proxy? {
        if (!proxyEnabled || proxyHost.isBlank() || proxyPort <= 0) return null
        return try {
            Proxy(Proxy.Type.HTTP, InetSocketAddress(proxyHost, proxyPort))
        } catch (_: Exception) { null }
    }

    private fun OkHttpClient.Builder.applyProxy(): OkHttpClient.Builder {
        val p = getProxy()
        if (p != null) proxy(p)
        return this
    }

    fun createHttpClient(timeoutSeconds: Long = 15): OkHttpClient =
        OkHttpClient.Builder()
            .connectTimeout(timeoutSeconds, TimeUnit.SECONDS)
            .readTimeout(timeoutSeconds, TimeUnit.SECONDS)
            .writeTimeout(timeoutSeconds, TimeUnit.SECONDS)
            .applyProxy()
            .addInterceptor { chain ->
                val req = chain.request().newBuilder()
                    .header("User-Agent", USER_AGENT)
                    .build()
                chain.proceed(req)
            }
            .build()

    fun createSearchHttpClient(timeoutSeconds: Long = 15): OkHttpClient =
        OkHttpClient.Builder()
            .connectTimeout(timeoutSeconds, TimeUnit.SECONDS)
            .readTimeout(timeoutSeconds, TimeUnit.SECONDS)
            .writeTimeout(timeoutSeconds, TimeUnit.SECONDS)
            .applyProxy()
            .addInterceptor { chain ->
                val req = chain.request().newBuilder()
                    .header("User-Agent", "ReadStorm/0.1 Mozilla/5.0")
                    .build()
                chain.proceed(req)
            }
            .build()

    fun createHealthCheckHttpClient(perSourceTimeoutSeconds: Long = 3): OkHttpClient =
        OkHttpClient.Builder()
            .connectTimeout(perSourceTimeoutSeconds, TimeUnit.SECONDS)
            .readTimeout(HEALTH_CHECK_IO_TIMEOUT_SECONDS, TimeUnit.SECONDS)
            .writeTimeout(HEALTH_CHECK_IO_TIMEOUT_SECONDS, TimeUnit.SECONDS)
            .applyProxy()
            .addInterceptor { chain ->
                val req = chain.request().newBuilder()
                    .header("User-Agent", USER_AGENT)
                    .build()
                chain.proceed(req)
            }
            .build()

    suspend fun sendWithSimpleRetry(
        client: OkHttpClient,
        request: Request,
        maxAttempts: Int = 3
    ): Response {
        var delayMs = INITIAL_RETRY_DELAY_MS
        var lastException: Exception? = null

        for (attempt in 1..maxAttempts) {
            try {
                val response = client.newCall(request).execute()
                if (response.code in 500..599 && attempt < maxAttempts) {
                    response.close()
                    delay(delayMs)
                    delayMs *= 2
                    continue
                }
                return response
            } catch (e: IOException) {
                lastException = e
                if (attempt < maxAttempts) {
                    delay(delayMs)
                    delayMs *= 2
                }
            }
        }

        throw lastException ?: IOException("Request failed after $maxAttempts attempts")
    }

    fun resolveUrl(baseUrl: String?, relativeUrl: String?): String {
        if (relativeUrl.isNullOrBlank()) return ""
        if (relativeUrl.startsWith("http://") || relativeUrl.startsWith("https://")) {
            return relativeUrl
        }
        if (baseUrl.isNullOrBlank()) return relativeUrl

        return try {
            val base = URI(baseUrl)
            base.resolve(relativeUrl).toString()
        } catch (_: Exception) {
            relativeUrl
        }
    }

    fun normalizeSelector(selector: String?): String {
        if (selector.isNullOrBlank()) return ""
        return selector.trim()
    }
}
