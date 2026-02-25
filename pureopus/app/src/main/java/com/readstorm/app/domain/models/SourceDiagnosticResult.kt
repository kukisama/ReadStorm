package com.readstorm.app.domain.models

data class SourceDiagnosticResult(
    val sourceId: Int = 0,
    var sourceName: String = "",
    var baseUrl: String = "",
    var searchRuleFound: Boolean = false,
    var tocRuleFound: Boolean = false,
    var chapterRuleFound: Boolean = false,
    var httpStatusCode: Int = 0,
    var httpStatusMessage: String = "",
    var searchResultCount: Int = 0,
    var tocItemCount: Int = 0,
    var tocSelector: String = "",
    var chapterContentSelector: String = "",
    var sampleChapterText: String = "",
    var summary: String = "",
    val diagnosticLines: MutableList<String> = mutableListOf()
) {
    val isHealthy: Boolean
        get() = searchRuleFound && tocRuleFound && chapterRuleFound &&
                httpStatusCode in 200..<400
}
