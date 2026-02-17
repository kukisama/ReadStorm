using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.ViewModels;

public sealed partial class DiagnosticViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;
    private readonly ISourceDiagnosticUseCase _sourceDiagnosticUseCase;
    private readonly Dictionary<int, SourceDiagnosticResult> _diagnosticResults = new();

    public DiagnosticViewModel(MainWindowViewModel parent, ISourceDiagnosticUseCase sourceDiagnosticUseCase)
    {
        _parent = parent;
        _sourceDiagnosticUseCase = sourceDiagnosticUseCase;
    }

    [ObservableProperty]
    private bool isDiagnosing;

    [ObservableProperty]
    private string diagnosticSummary = string.Empty;

    [ObservableProperty]
    private string? selectedDiagnosticSource;

    partial void OnSelectedDiagnosticSourceChanged(string? value)
    {
        DiagnosticLines.Clear();
        if (value is null) return;

        var match = System.Text.RegularExpressions.Regex.Match(value, @"\[(\d+)\]");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var id)
            && _diagnosticResults.TryGetValue(id, out var result))
        {
            var header = $"[{result.SourceName}] {result.Summary} | HTTP={result.HttpStatusCode} | " +
                         $"搜索={result.SearchResultCount}条 | 目录selector='{result.TocSelector}' " +
                         $"| 章节selector='{result.ChapterContentSelector}'";
            DiagnosticLines.Add(header);
            DiagnosticLines.Add(new string('─', 60));
            foreach (var line in result.DiagnosticLines)
                DiagnosticLines.Add(line);
        }
    }

    public ObservableCollection<string> DiagnosticSourceNames { get; } = [];
    public ObservableCollection<string> DiagnosticLines { get; } = [];

    [RelayCommand]
    private async Task RunBatchDiagnosticAsync()
    {
        try
        {
            IsDiagnosing = true;
            DiagnosticSummary = "正在批量诊断所有书源…";
            DiagnosticLines.Clear();
            _diagnosticResults.Clear();
            DiagnosticSourceNames.Clear();

            var rules = _parent.Sources.Where(s => s.Id > 0).ToList();
            var total = rules.Count;
            var completed = 0;
            var healthy = 0;

            var tasks = rules.Select(async source =>
            {
                var result = await _sourceDiagnosticUseCase.DiagnoseAsync(source.Id, "测试");
                Interlocked.Increment(ref completed);
                if (result.IsHealthy) Interlocked.Increment(ref healthy);
                return result;
            });

            var results = await Task.WhenAll(tasks);
            foreach (var r in results.OrderBy(r => r.SourceId))
            {
                _diagnosticResults[r.SourceId] = r;
                var prefix = r.IsHealthy ? "🟢" : "🔴";
                DiagnosticSourceNames.Add($"{prefix} [{r.SourceId}] {r.SourceName}");
            }

            DiagnosticSummary = $"批量诊断完成：{healthy}/{total} 个书源正常";
            _parent.StatusMessage = DiagnosticSummary;
            if (DiagnosticSourceNames.Count > 0)
                SelectedDiagnosticSource = DiagnosticSourceNames[0];
        }
        catch (Exception ex)
        {
            DiagnosticSummary = $"诊断异常：{ex.Message}";
            _parent.StatusMessage = $"诊断失败：{ex.Message}";
        }
        finally { IsDiagnosing = false; }
    }
}
