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
import java.net.URLEncoder

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
        private const val MOBILE_UA = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36"
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
                    val referer = getCoverReferer(book.tocUrl, candidate)
                    val bytes = downloadCoverBytes(candidate.imageUrl, referer)
                    if (bytes != null && isValidImage(bytes)) {
                        book.coverBlob = bytes
                        book.coverUrl = candidate.imageUrl
                        book.coverRule = candidate.rule
                        bookRepo.upsertBook(book)
                        return@withContext "封面已更新：${candidate.rule}"
                    }
                } catch (e: Exception) {
                    AppLogger.log("CoverService", "Candidate ${candidate.imageUrl} failed: ${e.message}")
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

            // Qidian mobile fallback
            try {
                val qidianCandidates = getQidianSearchCoverCandidates(book.title)
                for (qc in qidianCandidates) {
                    if (candidates.none { it.imageUrl.equals(qc.imageUrl, ignoreCase = true) }) {
                        candidates.add(CoverCandidate(index++, qc.imageUrl, qc.rule, qc.htmlSnippet))
                    }
                }
            } catch (_: Exception) {
                // Qidian fallback failed, ignore
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

    private suspend fun downloadCoverBytes(imageUrl: String, referer: String? = null): ByteArray? {
        return try {
            withTimeout(COVER_DOWNLOAD_TIMEOUT_MS) {
                val builder = Request.Builder().url(imageUrl)
                    .header("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8")
                    .header("Cache-Control", "no-cache")
                    .header("Pragma", "no-cache")
                if (!referer.isNullOrBlank()) {
                    builder.header("Referer", referer)
                    try {
                        val uri = java.net.URI(referer)
                        builder.header("Origin", "${uri.scheme}://${uri.host}")
                    } catch (_: Exception) { }
                }
                val response = httpClient.newCall(builder.build()).execute()
                response.use { resp ->
                    if (!resp.isSuccessful) null
                    else {
                        val bytes = resp.body?.bytes()
                        if (bytes != null && bytes.size > 500_000) null else bytes
                    }
                }
            }
        } catch (e: Exception) {
            AppLogger.log("CoverService", "Download cover failed for $imageUrl: ${e.message}")
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

    /**
     * 获取封面 Referer：起点候选用搜索页 URL（存在 htmlSnippet 中），其他用 TocUrl。
     */
    private fun getCoverReferer(tocUrl: String, candidate: CoverCandidate): String {
        return if (candidate.rule.startsWith("qidian:", ignoreCase = true)) {
            candidate.htmlSnippet
        } else {
            tocUrl
        }
    }

    /**
     * 从起点移动端搜索页提取封面候选（兜底策略）。
     * 移动端反爬比 PC 端宽松，通过 m.qidian.com/soushu/{title}.html 搜索。
     *
     * 策略顺序：
     * 1. Jsoup 解析 img 标签，找 CDN 封面图
     * 2. 正则匹配 bookcover.yuewen.com 的 URL
     * 3. 提取 bookId 并构造 CDN 封面 URL
     */
    private suspend fun getQidianSearchCoverCandidates(title: String): List<CoverCandidate> {
        if (title.isBlank()) return emptyList()

        return try {
            withTimeout(COVER_DOWNLOAD_TIMEOUT_MS) {
                val encoded = URLEncoder.encode(title.trim(), "UTF-8")
                val searchUrl = "https://m.qidian.com/soushu/$encoded.html"

                val request = Request.Builder()
                    .url(searchUrl)
                    .header("User-Agent", MOBILE_UA)
                    .header("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8")
                    .header("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8")
                    .header("Referer", "https://m.qidian.com/")
                    .build()

                val response = httpClient.newCall(request).execute()
                val html = response.use { resp ->
                    if (!resp.isSuccessful) {
                        AppLogger.log("CoverService", "[qidian] HTTP ${resp.code}: $searchUrl")
                        return@withTimeout emptyList()
                    }
                    resp.body?.string() ?: ""
                }

                if (html.isBlank()) {
                    AppLogger.log("CoverService", "[qidian] Empty HTML: $searchUrl")
                    return@withTimeout emptyList()
                }
                if (html.length < 1000) {
                    AppLogger.log("CoverService", "[qidian] Short response (anti-crawl?): ${html.take(500)}")
                }

                val candidates = mutableListOf<CoverCandidate>()

                // 策略 1: Jsoup 解析 img 标签，找起点封面 CDN
                try {
                    val doc = Jsoup.parse(html, searchUrl)
                    for (img in doc.select("img")) {
                        val raw = img.attr("src").ifBlank { img.attr("data-src") }
                            .ifBlank { img.attr("data-original") }
                        if (raw.isBlank()) continue
                        val abs = RuleHttpHelper.resolveUrl(searchUrl, raw)
                        if (isLikelyCoverUrl(abs)) {
                            candidates.add(CoverCandidate(0, abs, "qidian:search:img", searchUrl))
                            break // 只取第一个
                        }
                    }
                } catch (e: Exception) {
                    AppLogger.log("CoverService", "[qidian] Strategy 1 (img) error: ${e.message}")
                }

                // 策略 2: 正则从 HTML 源码中提取封面 CDN 链接
                if (candidates.isEmpty()) {
                    try {
                        val regex = Regex("""https?://bookcover\.yuewen\.com/qdbimg/[^\\"'\s<>]+""", RegexOption.IGNORE_CASE)
                        val match = regex.find(html)
                        if (match != null) {
                            val abs = RuleHttpHelper.resolveUrl(searchUrl, match.value)
                            if (isLikelyCoverUrl(abs)) {
                                candidates.add(CoverCandidate(0, abs, "qidian:search:regex-cdn", searchUrl))
                            }
                        }
                    } catch (e: Exception) {
                        AppLogger.log("CoverService", "[qidian] Strategy 2 (regex) error: ${e.message}")
                    }
                }

                // 策略 3: 提取 bookId，构造 CDN 封面 URL
                if (candidates.isEmpty()) {
                    try {
                        val bookIdMatch = Regex("""/book/(\d{5,15})""", RegexOption.IGNORE_CASE).find(html)
                        if (bookIdMatch != null) {
                            val bookId = bookIdMatch.groupValues[1]
                            AppLogger.log("CoverService", "[qidian] Extracted bookId=$bookId")
                            candidates.add(
                                CoverCandidate(
                                    0,
                                    "https://bookcover.yuewen.com/qdbimg/349573/$bookId/300.webp",
                                    "qidian:search:bookid-cdn",
                                    searchUrl
                                )
                            )
                        }
                    } catch (e: Exception) {
                        AppLogger.log("CoverService", "[qidian] Strategy 3 (bookId) error: ${e.message}")
                    }
                }

                AppLogger.log("CoverService", "[qidian] Final candidates: ${candidates.size}")
                candidates
            }
        } catch (e: Exception) {
            AppLogger.log("CoverService", "[qidian] Error: ${e.message}")
            emptyList()
        }
    }

    /**
     * 判断 URL 是否像封面图片（排除用户头像等干扰项）。
     */
    private fun isLikelyCoverUrl(url: String?): Boolean {
        if (url.isNullOrBlank()) return false
        val lower = url.lowercase()

        // 起点搜索页的用户头像占位图，不是书封面
        if (lower.contains("/images/user.") || lower.contains("user.bcb60")) return false

        return lower.contains("bookcover") ||
               lower.contains("qdbimg") ||
               lower.contains("cover") ||
               lower.endsWith(".jpg") ||
               lower.endsWith(".jpeg") ||
               lower.endsWith(".png") ||
               lower.endsWith(".webp")
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
