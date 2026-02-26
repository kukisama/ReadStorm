package com.readstorm.app.infrastructure.services

import android.content.Context
import com.readstorm.app.application.abstractions.ISourceDiagnosticUseCase
import com.readstorm.app.domain.models.SourceDiagnosticResult
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request

class RuleBasedSourceDiagnosticUseCase(
    private val context: Context
) : ISourceDiagnosticUseCase {

    private val httpClient: OkHttpClient = RuleHttpHelper.createHttpClient(timeoutSeconds = 8)

    override suspend fun diagnose(sourceId: Int, testKeyword: String): SourceDiagnosticResult =
        withContext(Dispatchers.IO) {
            val result = SourceDiagnosticResult(sourceId = sourceId)

            try {
                val rule = RuleFileLoader.loadRule(context, sourceId)
                if (rule == null) {
                    result.diagnosticLines.add("❌ 未找到 sourceId=$sourceId 的规则文件")
                    return@withContext result
                }

                result.sourceName = rule.name
                result.baseUrl = rule.url

                // Check rule sections
                result.searchRuleFound = rule.search != null
                result.tocRuleFound = rule.toc != null
                result.chapterRuleFound = rule.chapter != null

                result.diagnosticLines.add("📋 书源: ${rule.name} (id=$sourceId)")
                result.diagnosticLines.add("🔗 基础URL: ${rule.url}")
                result.diagnosticLines.add("🔎 搜索规则: ${if (result.searchRuleFound) "✅" else "❌"}")
                result.diagnosticLines.add("📑 目录规则: ${if (result.tocRuleFound) "✅" else "❌"}")
                result.diagnosticLines.add("📖 章节规则: ${if (result.chapterRuleFound) "✅" else "❌"}")

                // Test HTTP connectivity
                if (rule.url.isNotBlank()) {
                    try {
                        val request = Request.Builder().url(rule.url).build()
                        val response = httpClient.newCall(request).execute()
                        response.use { resp ->
                            result.httpStatusCode = resp.code
                            result.httpStatusMessage = resp.message
                            result.diagnosticLines.add("🌐 HTTP状态: ${resp.code} ${resp.message}")
                        }
                    } catch (e: Exception) {
                        result.diagnosticLines.add("🌐 HTTP连接失败: ${e.message}")
                    }
                }

                // Test search if available
                if (rule.search != null && testKeyword.isNotBlank()) {
                    try {
                        val searchUseCase = RuleBasedSearchBooksUseCase(context)
                        val results = searchUseCase.execute(testKeyword, sourceId)
                        result.searchResultCount = results.size
                        result.diagnosticLines.add("🔍 搜索结果: ${results.size} 条")
                    } catch (e: Exception) {
                        result.diagnosticLines.add("🔍 搜索失败: ${e.message}")
                    }
                }

                // Report TOC selector
                if (rule.toc != null && rule.toc!!.item.isNotBlank()) {
                    result.tocSelector = rule.toc!!.item
                    result.diagnosticLines.add("📑 目录选择器: ${rule.toc!!.item}")
                }

                if (rule.chapter != null && rule.chapter!!.content.isNotBlank()) {
                    result.chapterContentSelector = rule.chapter!!.content
                    result.diagnosticLines.add("📖 内容选择器: ${rule.chapter!!.content}")
                }

            } catch (e: Exception) {
                result.diagnosticLines.add("❌ 诊断异常: ${e.message}")
            }

            result
        }
}
