package com.readstorm.app.ui.viewmodels

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData
import com.readstorm.app.domain.models.FullBookSourceRule
import com.readstorm.app.infrastructure.services.RuleFileLoader

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
        _ruleTestStatus.postValue("测试中…")
        try {
            // Step 1: Test search
            val searchResult = parent.ruleEditorUseCase.testSearch(rule, keyword)
            _ruleTestSearchPreview.postValue(
                if (searchResult.searchItems.isNotEmpty())
                    searchResult.searchItems.joinToString("\n")
                else "无搜索结果"
            )

            // Step 2: Test TOC (use first search result URL if available)
            if (searchResult.searchItems.isNotEmpty() && searchResult.requestUrl.isNotBlank()) {
                val tocResult = parent.ruleEditorUseCase.testToc(rule, searchResult.requestUrl)
                _ruleTestTocPreview.postValue(
                    if (tocResult.tocItems.isNotEmpty())
                        tocResult.tocItems.joinToString("\n")
                    else "无目录结果"
                )
            }

            val diagnostics = searchResult.diagnosticLines.joinToString("\n")
            _ruleTestDiagnostics.postValue(diagnostics)
            _ruleTestStatus.postValue(
                if (searchResult.success) "测试通过 (${searchResult.elapsedMs}ms)"
                else "测试结果：${searchResult.message}"
            )
        } catch (e: Exception) {
            _ruleTestStatus.postValue("测试失败：${e.message}")
        } finally {
            _isRuleTesting.postValue(false)
        }
    }

    suspend fun debugRule() {
        val rule = _currentRule.value ?: return
        val keyword = _ruleTestKeyword.value ?: return
        _isRuleTesting.postValue(true)
        _ruleTestStatus.postValue("Debug 中…")
        try {
            val searchResult = parent.ruleEditorUseCase.testSearch(rule, keyword)
            val sb = StringBuilder()
            sb.appendLine("=== Search Debug ===")
            sb.appendLine("URL: ${searchResult.requestUrl}")
            sb.appendLine("Method: ${searchResult.requestMethod}")
            sb.appendLine("结果数: ${searchResult.searchItems.size}")
            sb.appendLine("耗时: ${searchResult.elapsedMs}ms")
            sb.appendLine()
            searchResult.diagnosticLines.forEach { sb.appendLine(it) }
            sb.appendLine()
            sb.appendLine("=== Raw HTML (前2000字) ===")
            sb.appendLine(searchResult.rawHtml.take(2000))

            _ruleTestDiagnostics.postValue(sb.toString())
            _ruleTestStatus.postValue("Debug 完成")
        } catch (e: Exception) {
            _ruleTestStatus.postValue("Debug 失败：${e.message}")
        } finally {
            _isRuleTesting.postValue(false)
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
