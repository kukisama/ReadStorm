package com.readstorm.app.infrastructure.services

import android.content.Context
import com.google.gson.GsonBuilder
import com.readstorm.app.application.abstractions.IRuleEditorUseCase
import com.readstorm.app.application.abstractions.RuleTestResult
import com.readstorm.app.domain.models.FullBookSourceRule
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.Request
import org.jsoup.Jsoup
import java.io.File

class FileBasedRuleEditorUseCase(
    private val context: Context
) : IRuleEditorUseCase {

    private val gson = GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create()
    private val httpClient = RuleHttpHelper.createHttpClient()

    override suspend fun loadAll(): List<FullBookSourceRule> =
        RuleFileLoader.loadAllRules(context)

    override suspend fun load(ruleId: Int): FullBookSourceRule? =
        RuleFileLoader.loadRule(context, ruleId)

    override suspend fun save(rule: FullBookSourceRule): Unit = withContext(Dispatchers.IO) {
        val userDir = File(RulePathResolver.getUserRulesDirectory(context))
        if (!userDir.exists()) userDir.mkdirs()
        val file = File(userDir, rule.fileName)
        file.writeText(gson.toJson(rule))
    }

    override suspend fun delete(ruleId: Int): Boolean = withContext(Dispatchers.IO) {
        val file = File(RulePathResolver.getUserRulesDirectory(context), "rule-$ruleId.json")
        if (file.exists()) file.delete() else false
    }

    override suspend fun resetToDefault(ruleId: Int): Boolean = withContext(Dispatchers.IO) {
        val userFile = File(RulePathResolver.getUserRulesDirectory(context), "rule-$ruleId.json")
        if (userFile.exists()) {
            userFile.delete()
            true
        } else {
            false
        }
    }

    override fun hasUserOverride(ruleId: Int): Boolean {
        val file = File(RulePathResolver.getUserRulesDirectory(context), "rule-$ruleId.json")
        return file.exists()
    }

    override suspend fun getNextAvailableId(): Int {
        val allRules = loadAll()
        return if (allRules.isEmpty()) 1 else allRules.maxOf { it.id } + 1
    }

    override suspend fun testSearch(rule: FullBookSourceRule, keyword: String): RuleTestResult =
        withContext(Dispatchers.IO) {
            val startTime = System.currentTimeMillis()
            val diagnosticLines = mutableListOf<String>()
            try {
                val search = rule.search ?: return@withContext RuleTestResult(
                    message = "该规则没有搜索配置"
                )

                val url = (search.url.ifBlank { rule.url }).replace("%s",
                    java.net.URLEncoder.encode(keyword, "UTF-8"))
                diagnosticLines.add("请求URL: $url")

                val request = Request.Builder().url(url).build()
                val response = httpClient.newCall(request).execute()
                val html = response.use { it.body?.string() ?: "" }

                val doc = Jsoup.parse(html, url)
                val resultSelector = RuleHttpHelper.normalizeSelector(search.result)
                val bookNameSelector = RuleHttpHelper.normalizeSelector(search.bookName)

                val items = if (resultSelector.isNotBlank() && bookNameSelector.isNotBlank()) {
                    doc.select(resultSelector).take(20).mapNotNull { row ->
                        val name = row.selectFirst(bookNameSelector)?.text()?.trim()
                        if (name.isNullOrBlank()) null else name
                    }
                } else emptyList()

                RuleTestResult(
                    success = items.isNotEmpty(),
                    message = if (items.isNotEmpty()) "找到 ${items.size} 个结果" else "未找到结果",
                    requestUrl = url,
                    requestMethod = search.method,
                    searchItems = items,
                    rawHtml = html.take(30000),
                    elapsedMs = System.currentTimeMillis() - startTime,
                    diagnosticLines = diagnosticLines
                )
            } catch (e: Exception) {
                RuleTestResult(
                    message = "搜索测试失败: ${e.message}",
                    elapsedMs = System.currentTimeMillis() - startTime,
                    diagnosticLines = diagnosticLines
                )
            }
        }

    override suspend fun testToc(rule: FullBookSourceRule, bookUrl: String): RuleTestResult =
        withContext(Dispatchers.IO) {
            val startTime = System.currentTimeMillis()
            val diagnosticLines = mutableListOf<String>()
            try {
                val toc = rule.toc ?: return@withContext RuleTestResult(message = "该规则没有目录配置")
                val tocUrl = toc.url.ifBlank { bookUrl }
                diagnosticLines.add("目录URL: $tocUrl")

                val request = Request.Builder().url(tocUrl).build()
                val response = httpClient.newCall(request).execute()
                val html = response.use { it.body?.string() ?: "" }

                val doc = Jsoup.parse(html, tocUrl)
                val itemSelector = RuleHttpHelper.normalizeSelector(toc.item)

                val items = if (itemSelector.isNotBlank()) {
                    doc.select(itemSelector).take(30).map { it.text().trim() }
                        .filter { it.isNotBlank() }
                } else emptyList()

                RuleTestResult(
                    success = items.isNotEmpty(),
                    message = if (items.isNotEmpty()) "找到 ${items.size} 个章节" else "未找到章节",
                    requestUrl = tocUrl,
                    tocItems = items,
                    rawHtml = html.take(30000),
                    elapsedMs = System.currentTimeMillis() - startTime,
                    diagnosticLines = diagnosticLines
                )
            } catch (e: Exception) {
                RuleTestResult(
                    message = "目录测试失败: ${e.message}",
                    elapsedMs = System.currentTimeMillis() - startTime,
                    diagnosticLines = diagnosticLines
                )
            }
        }

    override suspend fun testChapter(rule: FullBookSourceRule, chapterUrl: String): RuleTestResult =
        withContext(Dispatchers.IO) {
            val startTime = System.currentTimeMillis()
            val diagnosticLines = mutableListOf<String>()
            try {
                val chapter = rule.chapter
                    ?: return@withContext RuleTestResult(message = "该规则没有章节配置")

                diagnosticLines.add("章节URL: $chapterUrl")

                val request = Request.Builder().url(chapterUrl).build()
                val response = httpClient.newCall(request).execute()
                val html = response.use { it.body?.string() ?: "" }

                val doc = Jsoup.parse(html, chapterUrl)
                val contentSelector = RuleHttpHelper.normalizeSelector(chapter.content)
                val contentElement = if (contentSelector.isNotBlank()) {
                    doc.selectFirst(contentSelector)
                } else null

                val preview = contentElement?.text()?.take(500) ?: ""

                RuleTestResult(
                    success = preview.isNotBlank(),
                    message = if (preview.isNotBlank()) "成功获取章节内容 (${preview.length}字)" else "未获取到内容",
                    requestUrl = chapterUrl,
                    contentPreview = preview,
                    rawHtml = html.take(30000),
                    elapsedMs = System.currentTimeMillis() - startTime,
                    diagnosticLines = diagnosticLines
                )
            } catch (e: Exception) {
                RuleTestResult(
                    message = "章节测试失败: ${e.message}",
                    elapsedMs = System.currentTimeMillis() - startTime,
                    diagnosticLines = diagnosticLines
                )
            }
        }
}
