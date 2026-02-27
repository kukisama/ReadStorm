package com.readstorm.app.infrastructure.services

import android.content.Context
import com.readstorm.app.application.abstractions.IBookRepository
import com.readstorm.app.application.abstractions.ICoverUseCase
import com.readstorm.app.domain.models.BookEntity
import com.readstorm.app.domain.models.CoverCandidate
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import okhttp3.OkHttpClient
import okhttp3.Request
import org.jsoup.Jsoup

/**
 * Cover service: extracts cover URLs from book HTML pages,
 * downloads and validates cover images, stores as BLOB in database.
 */
class CoverService(
    private val context: Context,
    private val bookRepo: IBookRepository
) : ICoverUseCase {

    private val httpClient: OkHttpClient = RuleHttpHelper.createHttpClient()

    companion object {
        private const val COVER_DOWNLOAD_TIMEOUT_MS = 6_000L
        private val JPEG_MAGIC = byteArrayOf(0xFF.toByte(), 0xD8.toByte(), 0xFF.toByte())
        private val PNG_MAGIC = byteArrayOf(0x89.toByte(), 0x50, 0x4E, 0x47)
        private val GIF_MAGIC = "GIF8".toByteArray()
        private val WEBP_MAGIC = "RIFF".toByteArray()
    }

    override suspend fun refreshCover(book: BookEntity): String = withContext(Dispatchers.IO) {
        try {
            val candidates = getCoverCandidates(book)
            if (candidates.isEmpty()) {
                return@withContext "未找到封面候选项"
            }

            for (candidate in candidates) {
                try {
                    val bytes = downloadCoverBytes(candidate.imageUrl)
                    if (bytes != null && isValidImage(bytes)) {
                        book.coverBlob = bytes
                        book.coverUrl = candidate.imageUrl
                        book.coverRule = candidate.rule
                        bookRepo.upsertBook(book)
                        return@withContext "封面已更新：${candidate.rule}"
                    }
                } catch (_: Exception) {
                    continue
                }
            }

            "所有候选项均下载失败"
        } catch (e: Exception) {
            "封面刷新失败：${e.message}"
        }
    }

    override suspend fun getCoverCandidates(book: BookEntity): List<CoverCandidate> =
        withContext(Dispatchers.IO) {
            val candidates = mutableListOf<CoverCandidate>()
            var index = 0

            // Extract from book's TocUrl page
            if (book.tocUrl.isNotBlank()) {
                try {
                    val html = fetchHtml(book.tocUrl)
                    if (html.isNotBlank()) {
                        val doc = Jsoup.parse(html, book.tocUrl)

                        // 1. meta og:image
                        val ogImage = doc.selectFirst("meta[property=og:image]")?.attr("content")
                        if (!ogImage.isNullOrBlank()) {
                            val url = RuleHttpHelper.resolveUrl(book.tocUrl, ogImage)
                            if (url.isNotBlank()) {
                                candidates.add(CoverCandidate(index++, url, "og:image", "<meta og:image>"))
                            }
                        }

                        // 2. img tags with cover-related attributes
                        val coverSelectors = listOf(
                            "img[id*=cover]", "img[class*=cover]",
                            "img[id*=bookimg]", "img[class*=bookimg]",
                            "img[id*=pic]", "img[class*=pic]",
                            "#fmimg img", ".book img", ".cover img",
                            "#maininfo img", ".bookinfo img"
                        )
                        for (selector in coverSelectors) {
                            try {
                                val img = doc.selectFirst(selector) ?: continue
                                val src = img.attr("src").ifBlank { img.attr("data-src") }
                                if (src.isBlank()) continue
                                val url = RuleHttpHelper.resolveUrl(book.tocUrl, src)
                                if (url.isNotBlank() && candidates.none { it.imageUrl == url }) {
                                    candidates.add(CoverCandidate(index++, url, selector, img.outerHtml().take(200)))
                                }
                            } catch (_: Exception) {
                                continue
                            }
                        }
                    }
                } catch (_: Exception) {
                    // Page fetch failed, continue with other methods
                }
            }

            // Extract from book source rule's coverUrl if available
            try {
                val rule = RuleFileLoader.loadRule(context, book.sourceId)
                val coverSelector = rule?.book?.coverUrl
                if (!coverSelector.isNullOrBlank() && book.tocUrl.isNotBlank()) {
                    val html = fetchHtml(book.tocUrl)
                    if (html.isNotBlank()) {
                        val doc = Jsoup.parse(html, book.tocUrl)
                        val normalizedSelector = RuleHttpHelper.normalizeSelector(coverSelector)
                        if (normalizedSelector.isNotBlank()) {
                            val element = doc.selectFirst(normalizedSelector)
                            val src = element?.attr("src")
                                ?: element?.attr("data-src")
                                ?: element?.attr("content")
                            if (!src.isNullOrBlank()) {
                                val url = RuleHttpHelper.resolveUrl(book.tocUrl, src)
                                if (url.isNotBlank() && candidates.none { it.imageUrl == url }) {
                                    candidates.add(CoverCandidate(index++, url, "rule:coverUrl", normalizedSelector))
                                }
                            }
                        }
                    }
                }
            } catch (_: Exception) {
                // Rule-based extraction failed
            }

            candidates
        }

    override suspend fun applyCoverCandidate(book: BookEntity, candidate: CoverCandidate): String =
        withContext(Dispatchers.IO) {
            try {
                val bytes = downloadCoverBytes(candidate.imageUrl)
                if (bytes != null && isValidImage(bytes)) {
                    book.coverBlob = bytes
                    book.coverUrl = candidate.imageUrl
                    book.coverRule = candidate.rule
                    bookRepo.upsertBook(book)
                    "封面已应用：${candidate.rule}"
                } else {
                    "封面下载失败或格式无效"
                }
            } catch (e: Exception) {
                "应用封面失败：${e.message}"
            }
        }

    private suspend fun downloadCoverBytes(imageUrl: String): ByteArray? {
        return try {
            withTimeout(COVER_DOWNLOAD_TIMEOUT_MS) {
                val request = Request.Builder().url(imageUrl).build()
                val response = httpClient.newCall(request).execute()
                response.use { resp ->
                    if (!resp.isSuccessful) null
                    else resp.body?.bytes()
                }
            }
        } catch (_: Exception) {
            null
        }
    }

    private fun fetchHtml(url: String): String {
        val request = Request.Builder().url(url).build()
        val response = httpClient.newCall(request).execute()
        return response.use { resp ->
            if (!resp.isSuccessful) "" else resp.body?.string() ?: ""
        }
    }

    private fun isValidImage(bytes: ByteArray): Boolean {
        if (bytes.size < 8) return false
        return startsWith(bytes, JPEG_MAGIC) ||
               startsWith(bytes, PNG_MAGIC) ||
               startsWith(bytes, GIF_MAGIC) ||
               startsWith(bytes, WEBP_MAGIC)
    }

    private fun startsWith(data: ByteArray, prefix: ByteArray): Boolean {
        if (data.size < prefix.size) return false
        return prefix.indices.all { data[it] == prefix[it] }
    }
}
