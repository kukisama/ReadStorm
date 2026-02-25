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
            val context = parent.getApplication<android.app.Application>()
            allRules = RuleFileLoader.loadAllRules(context)

            val items = allRules.map { rule ->
                val sourceItem = parent.sources.find { it.id == rule.id }
                RuleListItem(
                    id = rule.id,
                    name = rule.name,
                    url = rule.url,
                    hasSearch = !rule.search.url.isNullOrBlank(),
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
        _ruleHasUserOverride.postValue(false)

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
            // TODO: Wire to IRuleEditorUseCase.save
            parent.setStatusMessage("保存规则功能将在规则编辑器服务实现后可用。")
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
            // TODO: Wire to IRuleEditorUseCase.testSearch/testToc/testChapter
            _ruleTestStatus.postValue("测试功能将在规则编辑器服务实现后可用。")
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
            // TODO: Wire to IRuleEditorUseCase for full debug
            _ruleTestStatus.postValue("Debug 功能将在规则编辑器服务实现后可用。")
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
        // TODO: Wire to IRuleEditorUseCase.delete
        parent.setStatusMessage("删除规则功能将在规则编辑器服务实现后可用。")
    }

    suspend fun resetRuleToDefault() {
        val rule = _currentRule.value ?: return
        // TODO: Wire to IRuleEditorUseCase.resetToDefault
        parent.setStatusMessage("恢复默认功能将在规则编辑器服务实现后可用。")
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
