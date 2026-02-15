namespace ReadStorm.Domain.Models;

/// <summary>书源诊断结果，用于可视化诊断面板。</summary>
public sealed class SourceDiagnosticResult
{
    public int SourceId { get; init; }

    public string SourceName { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public bool SearchRuleFound { get; set; }

    public bool TocRuleFound { get; set; }

    public bool ChapterRuleFound { get; set; }

    public int HttpStatusCode { get; set; }

    public string HttpStatusMessage { get; set; } = string.Empty;

    public int SearchResultCount { get; set; }

    public int TocItemCount { get; set; }

    public string TocSelector { get; set; } = string.Empty;

    public string ChapterContentSelector { get; set; } = string.Empty;

    public string SampleChapterText { get; set; } = string.Empty;

    public bool IsHealthy => SearchRuleFound && TocRuleFound && ChapterRuleFound && HttpStatusCode is >= 200 and < 400;

    public string Summary { get; set; } = string.Empty;

    public List<string> DiagnosticLines { get; } = [];
}
