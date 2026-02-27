package com.readstorm.app.infrastructure.services

import android.content.Context
import com.readstorm.app.application.abstractions.ISearchBooksUseCase
import com.readstorm.app.domain.models.SearchResult
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.FormBody
import okhttp3.OkHttpClient
import okhttp3.Request
import org.jsoup.Jsoup
import java.net.URLEncoder
import java.util.UUID

class RuleBasedSearchBooksUseCase(private val context: Context) : ISearchBooksUseCase {

    private val httpClient: OkHttpClient = RuleHttpHelper.createSearchHttpClient()

    override suspend fun execute(keyword: String, sourceId: Int?): List<SearchResult> {
        if (keyword.isBlank() || sourceId == null || sourceId <= 0) return emptyList()

        val rule = RuleFileLoader.loadRule(context, sourceId) ?: return emptyList()
        val search = rule.search ?: return emptyList()

        return try {
            val firstPage = fetchSearchPage(rule, search, keyword)
            if (firstPage.html.isBlank()) return emptyList()

            val allPages = mutableListOf(firstPage)
            val nextPages = extractPaginationUrls(search, firstPage, keyword)
            for (url in nextPages) {
                val html = fetchHtmlByUrl(url)
                if (html.isNotBlank()) {
                    allPages.add(SearchPageContent(html, url))
                }
            }

            allPages.flatMap { page -> parseSearchResults(rule, page) }
                .distinctBy { "${it.title}|${it.author}".lowercase() }
                .take(50)
        } catch (_: Exception) {
            emptyList()
        }
    }

    private suspend fun fetchSearchPage(
        rule: com.readstorm.app.domain.models.FullBookSourceRule,
        search: com.readstorm.app.domain.models.RuleSearchSection,
        keyword: String
    ): SearchPageContent = withContext(Dispatchers.IO) {
        val rawUrl = (search.url.ifBlank { rule.url }).ifBlank { return@withContext SearchPageContent("", "") }
        val url = replaceKeywordForUrl(rawUrl, keyword)

        val isPost = search.method.equals("post", ignoreCase = true)
        val requestBuilder = Request.Builder().url(url)

        if (search.cookies.isNotBlank()) {
            requestBuilder.header("Cookie", search.cookies)
        }

        if (isPost) {
            val formData = buildFormData(search.data, keyword)
            val formBody = FormBody.Builder().apply {
                formData.forEach { (k, v) -> add(k, v) }
            }.build()
            requestBuilder.post(formBody)
        }

        val response = RuleHttpHelper.sendWithSimpleRetry(httpClient, requestBuilder.build())
        response.use { resp ->
            if (!resp.isSuccessful) return@withContext SearchPageContent("", url)
            SearchPageContent(resp.body?.string() ?: "", url)
        }
    }

    private suspend fun fetchHtmlByUrl(url: String): String = withContext(Dispatchers.IO) {
        try {
            val request = Request.Builder().url(url).build()
            val response = RuleHttpHelper.sendWithSimpleRetry(httpClient, request)
            response.use { resp ->
                if (!resp.isSuccessful) "" else resp.body?.string() ?: ""
            }
        } catch (_: Exception) {
            ""
        }
    }

    private fun parseSearchResults(
        rule: com.readstorm.app.domain.models.FullBookSourceRule,
        page: SearchPageContent
    ): List<SearchResult> {
        val search = rule.search ?: return emptyList()
        val resultSelector = RuleHttpHelper.normalizeSelector(search.result)
        val bookNameSelector = RuleHttpHelper.normalizeSelector(search.bookName)
        if (resultSelector.isBlank() || bookNameSelector.isBlank()) return emptyList()

        val doc = Jsoup.parse(page.html, page.pageUrl)
        val rows = doc.select(resultSelector)
        val authorSelector = RuleHttpHelper.normalizeSelector(search.author)
        val latestChapterSelector = RuleHttpHelper.normalizeSelector(search.latestChapter)

        return rows.mapNotNull { row ->
            val bookNode = row.selectFirst(bookNameSelector) ?: return@mapNotNull null
            val title = bookNode.text().trim()
            if (title.isBlank()) return@mapNotNull null

            val relativeHref = bookNode.attr("href")
            val bookUrl = RuleHttpHelper.resolveUrl(page.pageUrl, relativeHref)

            val author = if (authorSelector.isNotBlank()) {
                row.selectFirst(authorSelector)?.text()?.trim() ?: "未知作者"
            } else "未知作者"

            val latestChapter = if (latestChapterSelector.isNotBlank()) {
                row.selectFirst(latestChapterSelector)?.text()?.trim() ?: "/"
            } else "/"

            SearchResult(
                id = UUID.randomUUID(),
                title = title,
                author = author,
                sourceId = rule.id,
                sourceName = rule.name,
                url = bookUrl,
                latestChapter = latestChapter,
                updatedAt = System.currentTimeMillis()
            )
        }
    }

    private fun extractPaginationUrls(
        search: com.readstorm.app.domain.models.RuleSearchSection,
        firstPage: SearchPageContent,
        keyword: String
    ): List<String> {
        if (!search.pagination || search.nextPage.isBlank()) return emptyList()
        val selector = RuleHttpHelper.normalizeSelector(search.nextPage)
        if (selector.isBlank()) return emptyList()

        val doc = Jsoup.parse(firstPage.html, firstPage.pageUrl)
        val limitPage = if (search.limitPage > 1) search.limitPage else 3
        val maxExtraPages = maxOf(0, limitPage - 1)

        val urls = linkedSetOf<String>()
        for (node in doc.select(selector)) {
            if (urls.size >= maxExtraPages) break
            var href = node.attr("href").ifBlank { node.attr("value") }
            if (href.isBlank()) continue
            href = replaceKeywordForUrl(href, keyword)
            val absoluteUrl = RuleHttpHelper.resolveUrl(firstPage.pageUrl, href)
            if (absoluteUrl.isBlank() || absoluteUrl.equals(firstPage.pageUrl, ignoreCase = true)) continue
            urls.add(absoluteUrl)
        }
        return urls.toList()
    }

    private fun buildFormData(template: String?, keyword: String): Map<String, String> {
        if (template.isNullOrBlank()) return emptyMap()
        var body = template.trim()
        if (body.startsWith('{') && body.endsWith('}')) {
            body = body.substring(1, body.length - 1)
        }
        val result = mutableMapOf<String, String>()
        body.split(',').map { it.trim() }.filter { it.isNotBlank() }.forEach { pair ->
            val idx = pair.indexOf(':')
            if (idx > 0 && idx < pair.length - 1) {
                val key = pair.substring(0, idx).trim()
                var value = pair.substring(idx + 1).trim().trim('"', '\'', ' ')
                value = value.replace("%s", keyword)
                if (key.isNotBlank()) result[key] = value
            }
        }
        return result
    }

    private fun replaceKeywordForUrl(template: String, keyword: String): String =
        template.replace("%s", URLEncoder.encode(keyword, "UTF-8"))

    private data class SearchPageContent(val html: String, val pageUrl: String)
}
