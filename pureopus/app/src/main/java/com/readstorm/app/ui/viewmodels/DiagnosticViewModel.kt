package com.readstorm.app.ui.viewmodels

import androidx.lifecycle.LiveData
import androidx.lifecycle.MutableLiveData

class DiagnosticViewModel(
    private val parent: MainViewModel
) {
    private val _isDiagnosing = MutableLiveData(false)
    val isDiagnosing: LiveData<Boolean> = _isDiagnosing

    private val _diagnosticSummary = MutableLiveData("")
    val diagnosticSummary: LiveData<String> = _diagnosticSummary

    private val _selectedDiagnosticSource = MutableLiveData<String?>(null)
    val selectedDiagnosticSource: LiveData<String?> = _selectedDiagnosticSource

    private val _diagnosticSourceNames = MutableLiveData<List<String>>(emptyList())
    val diagnosticSourceNames: LiveData<List<String>> = _diagnosticSourceNames

    private val _diagnosticLines = MutableLiveData<List<String>>(emptyList())
    val diagnosticLines: LiveData<List<String>> = _diagnosticLines

    private val diagnosticResults = mutableMapOf<Int, com.readstorm.app.domain.models.SourceDiagnosticResult>()

    fun setSelectedDiagnosticSource(source: String?) {
        _selectedDiagnosticSource.postValue(source)
        if (source == null) {
            _diagnosticLines.postValue(emptyList())
            return
        }

        val match = Regex("\\[(\\d+)\\]").find(source)
        if (match != null) {
            val id = match.groupValues[1].toIntOrNull()
            if (id != null && diagnosticResults.containsKey(id)) {
                val result = diagnosticResults[id]!!
                val lines = mutableListOf<String>()
                lines.add("[${result.sourceName}] ${result.summary} | HTTP=${result.httpStatusCode} | " +
                    "搜索=${result.searchResultCount}条 | 目录selector='${result.tocSelector}' " +
                    "| 章节selector='${result.chapterContentSelector}'")
                lines.add("─".repeat(60))
                lines.addAll(result.diagnosticLines)
                _diagnosticLines.postValue(lines)
            }
        }
    }

    suspend fun runBatchDiagnostic() {
        try {
            _isDiagnosing.postValue(true)
            _diagnosticSummary.postValue("正在批量诊断所有书源…")
            _diagnosticLines.postValue(emptyList())
            diagnosticResults.clear()
            _diagnosticSourceNames.postValue(emptyList())

            val rules = parent.sources.filter { it.id > 0 }
            val total = rules.size

            // TODO: Wire to ISourceDiagnosticUseCase when implemented
            // For now, show placeholder result
            _diagnosticSummary.postValue("批量诊断功能将在书源诊断服务实现后可用。共 $total 个书源待诊断。")
            parent.setStatusMessage("批量诊断完成（待实现）：$total 个书源")
        } catch (e: Exception) {
            _diagnosticSummary.postValue("诊断异常：${e.message}")
            parent.setStatusMessage("诊断失败：${e.message}")
        } finally {
            _isDiagnosing.postValue(false)
        }
    }
}
