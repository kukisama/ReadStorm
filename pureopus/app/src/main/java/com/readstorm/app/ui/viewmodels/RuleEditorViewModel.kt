package com.readstorm.app.ui.viewmodels

import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import com.readstorm.app.application.abstractions.RuleTestResult
import com.readstorm.app.domain.models.FullBookSourceRule
import com.readstorm.app.infrastructure.services.AppLogger
import com.readstorm.app.infrastructure.services.RuleFileLoader
import java.text.SimpleDateFormat
import java.util.*

class RuleEditorViewModel(
    private val parent: MainViewModel
) {
    private val _ruleEditorRules = MutableLiveData<List<RuleListItem>>(emptyList())
    val ruleEditorRules: LiveData<List<RuleListItem>> = _ruleEditorRules

    private val _ruleEditorSelectedRule = MutableLiveData<RuleListItem?>(null)
    val ruleEditorSelectedRule: LiveData<RuleListItem?> = _ruleEditorSelectedRule

    private val _currentRule = MutableLiveData<FullBookSourceRule?>(null)
    val currentRule: LiveData<FullBookSourceRule?> = _currentRule

    private val _ruleTestKeyword = MutableLiveData("")
    val ruleTestKeyword: LiveData<String> = _ruleTestKeyword

    private val _ruleTestStatus = MutableLiveData("")
    val ruleTestStatus: LiveData<String> = _ruleTestStatus

    private val _isRuleTesting = MutableLiveData(false)
    val isRuleTesting: LiveData<Boolean> = _isRuleTesting

    private val _isRuleSaving = MutableLiveData(false)
    val isRuleSaving: LiveData<Boolean> = _isRuleSaving

    private val _ruleTestDiagnostics = MutableLiveData("")
    val ruleTestDiagnostics: LiveData<String> = _ruleTestDiagnostics

    private val _ruleTestSearchPreview = MutableLiveData("")
    val ruleTestSearchPreview: LiveData<String> = _ruleTestSearchPreview

    private val _ruleTestTocPreview = MutableLiveData("")
    val ruleTestTocPreview: LiveData<String> = _ruleTestTocPreview

    private val _ruleTestContentPreview = MutableLiveData("")
    val ruleTestContentPreview: LiveData<String> = _ruleTestContentPreview

    private val _ruleHasUserOverride = MutableLiveData(false)
    val ruleHasUserOverride: LiveData<Boolean> = _ruleHasUserOverride

    private var allRules = listOf<FullBookSourceRule>()

    fun setRuleTestKeyword(keyword: String) {
        _ruleTestKeyword.postValue(keyword)
    }

    fun updateCurrentRule(rule: FullBookSourceRule?) {
        _currentRule.postValue(rule)
    }

    // ── Load Rule List ──

    suspend fun loadRuleList() {
        try {
            allRules = parent.ruleEditorUseCase.loadAll()

            val items = allRules.map { rule ->
                val sourceItem = parent.sources.find { it.id == rule.id }
                RuleListItem(
                    id = rule.id,
                    name = rule.name,
                    url = rule.url,
                    hasSearch = !rule.search?.url.isNullOrBlank(),
                    isHealthy = sourceItem?.isHealthy
                )
            }
            _ruleEditorRules.postValue(items)

            if (items.isNotEmpty()) {
                selectRule(items.first())
            }
        } catch (e: Exception) {
            parent.setStatusMessage("加载规则列表失败：${e.message}")
        }
    }

    fun selectRule(item: RuleListItem) {
        _ruleEditorSelectedRule.postValue(item)
        val rule = allRules.find { it.id == item.id }
        _currentRule.postValue(rule)
        _ruleHasUserOverride.postValue(parent.ruleEditorUseCase.hasUserOverride(item.id))

        // Clear test results
        _ruleTestDiagnostics.postValue("")
        _ruleTestSearchPreview.postValue("")
        _ruleTestTocPreview.postValue("")
        _ruleTestContentPreview.postValue("")
        _ruleTestStatus.postValue("")
    }

    // ── Rule Operations ──

    suspend fun saveRule() {
        val rule = _currentRule.value ?: return
        _isRuleSaving.postValue(true)
        try {
            parent.ruleEditorUseCase.save(rule)
            parent.setStatusMessage("规则已保存：${rule.name}")
            loadRuleList()
        } catch (e: Exception) {
            parent.setStatusMessage("保存失败：${e.message}")
        } finally {
            _isRuleSaving.postValue(false)
        }
    }

    suspend fun testRule() {
        val rule = _currentRule.value ?: return
        val keyword = _ruleTestKeyword.value ?: return
        if (keyword.isBlank()) {
            parent.setStatusMessage("请输入测试关键字。")
            return
        }

        _isRuleTesting.postValue(true)
        _ruleTestSearchPreview.postValue("")
        _ruleTestTocPreview.postValue("")
        _ruleTestContentPreview.postValue("")
        _ruleTestDiagnostics.postValue("")
        _ruleTestStatus.postValue("测试中… 第 1/3 步：搜索")

        val diagAll = StringBuilder()

        try {
            // Step 1: Test search
            val searchResult = parent.ruleEditorUseCase.testSearch(rule, keyword)
            _ruleTestSearchPreview.postValue(
                if (searchResult.searchItems.isNotEmpty())
                    searchResult.searchItems.joinToString("\n")
                else "无搜索结果"
            )
            diagAll.appendLine("=== 搜索 ===")
            searchResult.diagnosticLines.forEach { diagAll.appendLine(it) }
            diagAll.appendLine(searchResult.message)

            if (!searchResult.success || searchResult.searchItems.isEmpty()) {
                _ruleTestStatus.postValue("搜索未返回结果，测试终止。(${searchResult.elapsedMs}ms)")
                _ruleTestDiagnostics.postValue(diagAll.toString())
                return
            }

            // Extract URL from first search result: "title [url]"
            val firstItem = searchResult.searchItems[0]
            val urlMatch = Regex("\\[(.+)]$").find(firstItem)
            if (urlMatch == null) {
                _ruleTestStatus.postValue("无法从搜索结果中提取 URL")
                _ruleTestDiagnostics.postValue(diagAll.toString())
                return
            }

            var bookUrl = urlMatch.groupValues[1]
            if (!bookUrl.startsWith("http", ignoreCase = true) && rule.url.isNotBlank()) {
                try {
                    bookUrl = java.net.URL(java.net.URL(rule.url), bookUrl).toString()
                } catch (_: Exception) { }
            }

            // Step 2: Test TOC
            _ruleTestStatus.postValue("测试中… 第 2/3 步：目录")
            val tocResult = parent.ruleEditorUseCase.testToc(rule, bookUrl)
            _ruleTestTocPreview.postValue(
                if (tocResult.tocItems.isNotEmpty())
                    tocResult.tocItems.joinToString("\n")
                else "无目录结果"
            )
            diagAll.appendLine("\n=== 目录 ===")
            tocResult.diagnosticLines.forEach { diagAll.appendLine(it) }
            diagAll.appendLine(tocResult.message)

            if (!tocResult.success || tocResult.tocItems.isEmpty()) {
                _ruleTestStatus.postValue("目录解析失败，测试终止。(${tocResult.elapsedMs}ms)")
                _ruleTestDiagnostics.postValue(diagAll.toString())
                return
            }

            // Step 3: Test first chapter content
            _ruleTestStatus.postValue("测试中… 第 3/3 步：正文")
            var chapterUrl = tocResult.contentPreview
            if (chapterUrl.isBlank()) {
                // Try extracting from first toc item
                val tocUrlMatch = Regex("\\[(.+)]$").find(tocResult.tocItems[0])
                chapterUrl = tocUrlMatch?.groupValues?.get(1) ?: ""
            }

            if (chapterUrl.isNotBlank()) {
                val chapterResult = parent.ruleEditorUseCase.testChapter(rule, chapterUrl)
                _ruleTestContentPreview.postValue(
                    if (chapterResult.success) chapterResult.contentPreview
                    else chapterResult.message
                )
                diagAll.appendLine("\n=== 正文 ===")
                chapterResult.diagnosticLines.forEach { diagAll.appendLine(it) }
                diagAll.appendLine(chapterResult.message)

                _ruleTestStatus.postValue(
                    if (chapterResult.success)
                        "✅ 测试完成：搜索=${searchResult.searchItems.size}条, 目录=${tocResult.tocItems.size}章, 正文=${chapterResult.contentPreview.length}字"
                    else "正文提取失败：${chapterResult.message}"
                )
            } else {
                _ruleTestStatus.postValue("✅ 搜索+目录成功，但无法提取第一章 URL")
            }

            _ruleTestDiagnostics.postValue(diagAll.toString())
        } catch (e: Exception) {
            _ruleTestStatus.postValue("测试失败：${e.message}")
            _ruleTestDiagnostics.postValue(diagAll.toString())
        } finally {
            _isRuleTesting.postValue(false)
        }
    }

    /**
     * 三步调试：搜索 → 目录 → 正文。生成完整 Markdown 报告并复制到剪贴板。
     */
    suspend fun debugRule() {
        val rule = _currentRule.value ?: return
        val keyword = _ruleTestKeyword.value?.takeIf { it.isNotBlank() } ?: "诡秘之主"
        _isRuleTesting.postValue(true)
        _ruleTestStatus.postValue("Debug 中… 第 1/3 步：搜索")

        val report = StringBuilder()
        val dateFormat = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.getDefault())
        report.appendLine("# ReadStorm 规则调试报告")
        report.appendLine()
        report.appendLine("> **生成时间**: ${dateFormat.format(Date())}")
        report.appendLine("> **规则 ID**: ${rule.id}")
        report.appendLine("> **规则名称**: ${rule.name}")
        report.appendLine("> **站点 URL**: ${rule.url}")
        report.appendLine()
        report.appendLine("---")
        report.appendLine()
        report.appendLine("## 1. 测试参数")
        report.appendLine()
        report.appendLine("- **搜索关键字**: `$keyword`")
        report.appendLine()

        try {
            // ── Step 1: 搜索 ──
            val searchResult = parent.ruleEditorUseCase.testSearch(rule, keyword)
            appendDebugStep(report, 2, "搜索测试",
                "使用关键字在目标站点上执行搜索请求，验证搜索规则的 URL、选择器是否能正确提取书籍列表。",
                searchResult, searchResult.searchItems)

            _ruleTestSearchPreview.postValue(
                if (searchResult.searchItems.isNotEmpty())
                    searchResult.searchItems.take(20).joinToString("\n")
                else "无搜索结果"
            )

            if (!searchResult.success || searchResult.searchItems.isEmpty()) {
                _ruleTestStatus.postValue("Debug 终止：搜索未返回结果 (${searchResult.elapsedMs}ms)")
                _ruleTestDiagnostics.postValue(report.toString())
                copyToClipboard(report.toString())
                return
            }

            // 提取首个书籍 URL
            val firstItem = searchResult.searchItems[0]
            val bookUrl = extractBracketUrl(firstItem, rule.url)
            report.appendLine("---")
            report.appendLine()
            report.appendLine("## 3. 中间数据：首个书籍 URL")
            report.appendLine()
            report.appendLine("```")
            report.appendLine(bookUrl)
            report.appendLine("```")
            report.appendLine()

            // ── Step 2: 目录 ──
            _ruleTestStatus.postValue("Debug 中… 第 2/3 步：目录")
            val tocResult = parent.ruleEditorUseCase.testToc(rule, bookUrl)
            appendDebugStep(report, 4, "目录测试",
                "访问书籍详情页，提取章节目录列表。验证目录选择器能否正确匹配章节标题和链接。",
                tocResult, tocResult.tocItems)

            _ruleTestTocPreview.postValue(
                if (tocResult.tocItems.isNotEmpty())
                    tocResult.tocItems.take(30).joinToString("\n")
                else "无目录结果"
            )

            if (!tocResult.success || tocResult.tocItems.isEmpty()) {
                _ruleTestStatus.postValue("Debug 终止：目录为空 (${tocResult.elapsedMs}ms)")
                _ruleTestDiagnostics.postValue(report.toString())
                copyToClipboard(report.toString())
                return
            }

            // 提取首章 URL
            val chapterUrl = tocResult.contentPreview.takeIf { it.isNotBlank() }
                ?: extractBracketUrl(tocResult.tocItems[0], rule.url)
            report.appendLine("---")
            report.appendLine()
            report.appendLine("## 5. 中间数据：首章 URL")
            report.appendLine()
            report.appendLine("```")
            report.appendLine(chapterUrl)
            report.appendLine("```")
            report.appendLine()

            // ── Step 3: 正文 ──
            _ruleTestStatus.postValue("Debug 中… 第 3/3 步：正文")
            val chapterResult = parent.ruleEditorUseCase.testChapter(rule, chapterUrl)
            appendDebugStep(report, 6, "正文测试",
                "访问某一章的页面，提取正文内容。验证正文选择器能否正确获取章节文字。",
                chapterResult, emptyList())

            _ruleTestContentPreview.postValue(
                if (chapterResult.success) chapterResult.contentPreview
                else chapterResult.message
            )

            val finalStatus = if (chapterResult.success)
                "✅ Debug 完成，报告已复制到剪贴板"
            else
                "⚠️ Debug 完成（正文提取失败），报告已复制到剪贴板"
            _ruleTestStatus.postValue(finalStatus)
            _ruleTestDiagnostics.postValue(report.toString())
            copyToClipboard(report.toString())

        } catch (e: Exception) {
            report.appendLine("## ❌ 异常信息")
            report.appendLine()
            report.appendLine("```")
            report.appendLine(e.stackTraceToString())
            report.appendLine("```")
            _ruleTestStatus.postValue("Debug 异常：${e.message}")
            _ruleTestDiagnostics.postValue(report.toString())
            copyToClipboard(report.toString())
            AppLogger.log("RuleEditor", "debugRule error: ${e.stackTraceToString()}")
        } finally {
            _isRuleTesting.postValue(false)
        }
    }

    private fun appendDebugStep(
        report: StringBuilder,
        sectionNo: Int,
        stepName: String,
        stepDescription: String,
        result: RuleTestResult,
        items: List<String>
    ) {
        val statusEmoji = if (result.success) "✅" else "❌"
        report.appendLine("## $sectionNo. $stepName")
        report.appendLine()
        report.appendLine(stepDescription)
        report.appendLine()

        // 测试结果概览
        report.appendLine("### $sectionNo.1 测试结果")
        report.appendLine()
        report.appendLine("| 项目 | 值 |")
        report.appendLine("| --- | --- |")
        report.appendLine("| 状态 | $statusEmoji ${if (result.success) "成功" else "失败"} |")
        report.appendLine("| 耗时 | ${result.elapsedMs} ms |")
        report.appendLine("| 消息 | ${result.message.ifBlank { "（无）" }} |")
        report.appendLine()

        // HTTP 请求
        report.appendLine("### $sectionNo.2 HTTP 请求")
        report.appendLine()
        report.appendLine("```http")
        report.appendLine("${result.requestMethod} ${result.requestUrl}")
        if (result.requestBody.isNotBlank()) {
            report.appendLine()
            report.appendLine(result.requestBody)
        }
        report.appendLine("```")
        report.appendLine()

        // CSS 选择器
        if (result.selectorLines.isNotEmpty()) {
            report.appendLine("### $sectionNo.3 CSS 选择器")
            report.appendLine()
            report.appendLine("```css")
            result.selectorLines.forEach { report.appendLine(it) }
            report.appendLine("```")
            report.appendLine()
        }

        // 诊断详情
        if (result.diagnosticLines.isNotEmpty()) {
            report.appendLine("### $sectionNo.4 诊断详情")
            report.appendLine()
            result.diagnosticLines.forEach { report.appendLine("- $it") }
            report.appendLine()
        }

        // 匹配结果
        if (items.isNotEmpty()) {
            report.appendLine("### $sectionNo.5 匹配结果（共 ${items.size} 项）")
            report.appendLine()
            val displayCount = minOf(items.size, 50)
            items.take(displayCount).forEachIndexed { i, item ->
                report.appendLine("${i + 1}. $item")
            }
            if (items.size > displayCount) {
                report.appendLine("\n> …… 还有 ${items.size - displayCount} 项未显示")
            }
            report.appendLine()
        }

        // 内容预览
        if (result.contentPreview.isNotBlank()) {
            report.appendLine("### $sectionNo.6 内容预览")
            report.appendLine()
            report.appendLine("```text")
            val preview = if (result.contentPreview.length > 500)
                result.contentPreview.take(500) + "\n…（已截断）"
            else result.contentPreview
            report.appendLine(preview)
            report.appendLine("```")
            report.appendLine()
        }

        // 命中 HTML
        report.appendLine("### $sectionNo.7 命中的 HTML 片段")
        report.appendLine()
        if (result.matchedHtml.isNotBlank()) {
            report.appendLine("```html")
            val dump = if (result.matchedHtml.length > 3000)
                result.matchedHtml.take(3000) + "\n<!-- ……已截断，共 ${result.matchedHtml.length} 字符 -->"
            else result.matchedHtml
            report.appendLine(dump)
            report.appendLine("```")
        } else {
            report.appendLine("（无匹配 HTML）")
        }
        report.appendLine()
    }

    private fun extractBracketUrl(item: String, baseUrl: String): String {
        val match = Regex("\\[(.+?)\\]").findAll(item).lastOrNull()
        val raw = match?.groupValues?.get(1)?.trim() ?: item
        return if (raw.startsWith("http")) raw
        else {
            try {
                java.net.URL(java.net.URL(baseUrl), raw).toString()
            } catch (_: Exception) { raw }
        }
    }

    private fun copyToClipboard(text: String) {
        try {
            val context = parent.getApplication<android.app.Application>()
            val clipboard = context.getSystemService(Context.CLIPBOARD_SERVICE) as? ClipboardManager
            clipboard?.setPrimaryClip(ClipData.newPlainText("Debug Report", text))
        } catch (e: Exception) {
            AppLogger.log("RuleEditor", "复制到剪贴板失败: ${e.message}")
        }
    }

    fun newRule() {
        val nextId = (allRules.maxOfOrNull { it.id } ?: 0) + 1
        val newRule = FullBookSourceRule(id = nextId, url = "", name = "新规则")
        _currentRule.postValue(newRule)
        parent.setStatusMessage("已创建新规则模板（ID=$nextId），请编辑后保存。")
    }

    fun copyRule() {
        val current = _currentRule.value ?: return
        val nextId = (allRules.maxOfOrNull { it.id } ?: 0) + 1
        val copy = current.copy(id = nextId, name = "${current.name} (副本)")
        _currentRule.postValue(copy)
        parent.setStatusMessage("已复制规则：${copy.name}（ID=$nextId），请编辑后保存。")
    }

    suspend fun deleteRule() {
        val rule = _currentRule.value ?: return
        try {
            val deleted = parent.ruleEditorUseCase.delete(rule.id)
            if (deleted) {
                parent.setStatusMessage("已删除规则：${rule.name}")
                loadRuleList()
            } else {
                parent.setStatusMessage("未找到要删除的规则文件。")
            }
        } catch (e: Exception) {
            parent.setStatusMessage("删除失败：${e.message}")
        }
    }

    suspend fun resetRuleToDefault() {
        val rule = _currentRule.value ?: return
        try {
            val reset = parent.ruleEditorUseCase.resetToDefault(rule.id)
            if (reset) {
                parent.setStatusMessage("已恢复默认：${rule.name}")
                loadRuleList()
            } else {
                parent.setStatusMessage("该规则没有用户覆写。")
            }
        } catch (e: Exception) {
            parent.setStatusMessage("恢复默认失败：${e.message}")
        }
    }

    // ── Sync health from sources ──

    fun syncRuleEditorRuleHealthFromSources() {
        val items = _ruleEditorRules.value?.toMutableList() ?: return
        items.forEachIndexed { index, item ->
            val sourceItem = parent.sources.find { it.id == item.id }
            if (sourceItem != null) {
                items[index] = item.copy(isHealthy = sourceItem.isHealthy)
            }
        }
        _ruleEditorRules.postValue(items)
    }
}
