using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.ViewModels;

public sealed partial class RuleEditorViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;
    private readonly IRuleEditorUseCase _ruleEditorUseCase;
    private bool _suppressRuleSelectionLoad;

    private static readonly JsonSerializerOptions s_jsonWrite = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions s_jsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private const int MaxHtmlDumpLength = 30000;

    public RuleEditorViewModel(MainWindowViewModel parent, IRuleEditorUseCase ruleEditorUseCase)
    {
        _parent = parent;
        _ruleEditorUseCase = ruleEditorUseCase;
    }

    // ==================== Properties ====================

    /// <summary>当前正在编辑的规则对象，AXAML 表单直接绑定到其各属性。</summary>
    [ObservableProperty]
    private FullBookSourceRule? currentRule;

    /// <summary>当前选中的规则 ID。</summary>
    [ObservableProperty]
    private RuleListItem? ruleEditorSelectedRule;

    /// <summary>规则测试关键字。</summary>
    [ObservableProperty]
    private string ruleTestKeyword = string.Empty;

    /// <summary>测试搜索结果预览。</summary>
    [ObservableProperty]
    private string ruleTestSearchPreview = string.Empty;

    /// <summary>测试目录预览。</summary>
    [ObservableProperty]
    private string ruleTestTocPreview = string.Empty;

    /// <summary>测试正文预览。</summary>
    [ObservableProperty]
    private string ruleTestContentPreview = string.Empty;

    /// <summary>测试状态信息。</summary>
    [ObservableProperty]
    private string ruleTestStatus = string.Empty;

    /// <summary>测试诊断日志。</summary>
    [ObservableProperty]
    private string ruleTestDiagnostics = string.Empty;

    /// <summary>规则测试中。</summary>
    [ObservableProperty]
    private bool isRuleTesting;

    /// <summary>规则保存中。</summary>
    [ObservableProperty]
    private bool isRuleSaving;

    /// <summary>当前规则是否有用户覆盖（已被修改过）。</summary>
    [ObservableProperty]
    private bool ruleHasUserOverride;

    /// <summary>规则编辑器当前子页签：0=配置, 1=预览。</summary>
    [ObservableProperty]
    private int ruleEditorSubTab;

    /// <summary>
    /// 保存规则后用于通知 View 恢复输入焦点。
    /// 每次自增触发 PropertyChanged。
    /// </summary>
    [ObservableProperty]
    private int ruleEditorRefocusVersion;

    // ==================== Collections ====================

    /// <summary>所有规则列表。</summary>
    public ObservableCollection<RuleListItem> RuleEditorRules { get; } = [];

    // ==================== Helpers ====================

    /// <summary>确保规则的所有子区段不为 null，以便 AXAML 双向绑定。</summary>
    private static void EnsureSubSections(FullBookSourceRule rule)
    {
        rule.Search ??= new RuleSearchSection();
        rule.Book ??= new RuleBookSection();
        rule.Toc ??= new RuleTocSection();
        rule.Chapter ??= new RuleChapterSection();
    }

    private static string ExtractBracketUrl(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(.+)\]$");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    // ==================== Commands ====================

    /// <summary>加载所有规则到列表。</summary>
    [RelayCommand]
    private async Task LoadRuleListAsync()
    {
        try
        {
            var rules = await _ruleEditorUseCase.LoadAllAsync();
            var healthLookup = _parent.Sources
                .Where(s => s.Id > 0)
                .ToDictionary(s => s.Id, s => s.IsHealthy);

            RuleEditorRules.Clear();
            foreach (var r in rules)
            {
                healthLookup.TryGetValue(r.Id, out var healthy);
                RuleEditorRules.Add(new RuleListItem(r.Id, r.Name, r.Url, r.Search is not null, healthy));
            }
            _parent.StatusMessage = $"规则列表已加载：共 {rules.Count} 条";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"加载规则失败：{ex.Message}";
        }
    }

    /// <summary>保存当前编辑的规则。</summary>
    [RelayCommand]
    private async Task SaveRuleAsync()
    {
        if (CurrentRule is null)
        {
            _parent.StatusMessage = "没有正在编辑的规则。";
            return;
        }

        if (CurrentRule.Id <= 0)
        {
            _parent.StatusMessage = "规则 ID 必须为正整数。";
            return;
        }

        IsRuleSaving = true;
        try
        {
            await _ruleEditorUseCase.SaveAsync(CurrentRule);
            RuleHasUserOverride = _ruleEditorUseCase.HasUserOverride(CurrentRule.Id);
            UpsertRuleEditorListItem(CurrentRule);
            RuleEditorRefocusVersion++;
            _parent.StatusMessage = $"规则 {CurrentRule.Id}（{CurrentRule.Name}）已保存。";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"保存规则失败：{ex.Message}";
        }
        finally
        {
            IsRuleSaving = false;
        }
    }

    /// <summary>恢复当前规则为内置默认值。</summary>
    [RelayCommand]
    private async Task ResetRuleToDefaultAsync()
    {
        if (CurrentRule is null)
        {
            _parent.StatusMessage = "没有正在编辑的规则。";
            return;
        }

        var ruleId = CurrentRule.Id;
        try
        {
            var ok = await _ruleEditorUseCase.ResetToDefaultAsync(ruleId);
            if (!ok)
            {
                _parent.StatusMessage = $"规则 {ruleId} 没有用户覆盖或没有内置默认值，无需恢复。";
                return;
            }

            // 重新加载默认版本
            var defaultRule = await _ruleEditorUseCase.LoadAsync(ruleId);
            if (defaultRule is not null)
            {
                EnsureSubSections(defaultRule);
                CurrentRule = defaultRule;
                UpsertRuleEditorListItem(defaultRule);
            }

            RuleHasUserOverride = false;
            _parent.StatusMessage = $"规则 {ruleId} 已恢复为内置默认值。";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"恢复默认值失败：{ex.Message}";
        }
    }

    /// <summary>新建一条空规则。</summary>
    [RelayCommand]
    private async Task NewRuleAsync()
    {
        try
        {
            var nextId = await _ruleEditorUseCase.GetNextAvailableIdAsync();
            var template = new FullBookSourceRule
            {
                Id = nextId,
                Name = $"新书源-{nextId}",
                Url = "https://",
                Type = "html",
                Language = "zh_CN",
                Search = new RuleSearchSection(),
                Book = new RuleBookSection(),
                Toc = new RuleTocSection(),
                Chapter = new RuleChapterSection(),
            };
            CurrentRule = template;
            _parent.StatusMessage = $"已创建新规则模板，ID={nextId}。编辑后请点击【保存】。";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"创建规则失败：{ex.Message}";
        }
    }

    /// <summary>复制当前选中的规则为新规则。</summary>
    [RelayCommand]
    private async Task CopyRuleAsync()
    {
        if (CurrentRule is null)
        {
            _parent.StatusMessage = "请先选中一条规则再复制。";
            return;
        }

        try
        {
            // 深复制：序列化再反序列化
            var json = JsonSerializer.Serialize(CurrentRule, s_jsonWrite);
            var copy = JsonSerializer.Deserialize<FullBookSourceRule>(json, s_jsonRead);
            if (copy is null)
            {
                _parent.StatusMessage = "复制失败。";
                return;
            }

            var nextId = await _ruleEditorUseCase.GetNextAvailableIdAsync();
            copy.Id = nextId;
            copy.Name = $"{copy.Name}（副本）";
            EnsureSubSections(copy);
            CurrentRule = copy;
            _parent.StatusMessage = $"已复制为新规则 ID={nextId}，编辑后请点击【保存】。";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"复制规则失败：{ex.Message}";
        }
    }

    /// <summary>删除当前选中的规则。</summary>
    [RelayCommand]
    private async Task DeleteRuleAsync()
    {
        if (RuleEditorSelectedRule is null)
        {
            _parent.StatusMessage = "请先选中一条规则。";
            return;
        }

        var confirmed = await Views.DialogHelper.ConfirmAsync(
            "确认删除",
            $"确定要删除规则 {RuleEditorSelectedRule.Id}（{RuleEditorSelectedRule.Name}）吗？\n此操作不可恢复。");
        if (!confirmed) return;

        try
        {
            var deleted = await _ruleEditorUseCase.DeleteAsync(RuleEditorSelectedRule.Id);
            if (deleted)
            {
                _parent.StatusMessage = $"规则 {RuleEditorSelectedRule.Id}（{RuleEditorSelectedRule.Name}）已删除。";
                CurrentRule = null;
                RuleEditorSelectedRule = null;
                await LoadRuleListAsync();
            }
            else
            {
                _parent.StatusMessage = $"规则 {RuleEditorSelectedRule.Id} 未找到文件。";
            }
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"删除规则失败：{ex.Message}";
        }
    }

    /// <summary>运行完整测试：搜索 → 目录 → 第一章。</summary>
    [RelayCommand]
    private async Task TestRuleAsync()
    {
        if (CurrentRule is null)
        {
            _parent.StatusMessage = "请先加载或编辑一条规则。";
            return;
        }

        var rule = CurrentRule;

        IsRuleTesting = true;
        RuleTestSearchPreview = string.Empty;
        RuleTestTocPreview = string.Empty;
        RuleTestContentPreview = string.Empty;
        RuleTestDiagnostics = string.Empty;
        RuleTestStatus = "测试中… 第 1/3 步：搜索";

        var diagAll = new StringBuilder();

        try
        {
            // 1. 搜索
            var keyword = string.IsNullOrWhiteSpace(RuleTestKeyword) ? "诡秘之主" : RuleTestKeyword;
            var searchResult = await _ruleEditorUseCase.TestSearchAsync(rule, keyword);
            RuleTestSearchPreview = searchResult.Success
                ? string.Join("\n", searchResult.SearchItems.Take(20))
                : searchResult.Message;
            diagAll.AppendLine("=== 搜索 ===");
            foreach (var line in searchResult.DiagnosticLines) diagAll.AppendLine(line);
            diagAll.AppendLine(searchResult.Message);

            if (!searchResult.Success || searchResult.SearchItems.Count == 0)
            {
                RuleTestStatus = $"搜索未返回结果，测试终止。({searchResult.ElapsedMs}ms)";
                RuleTestDiagnostics = diagAll.ToString();
                // 自动切到预览子页
                RuleEditorSubTab = 1;
                return;
            }

            // 从第一个搜索结果提取 URL
            var firstItem = searchResult.SearchItems[0];
            var urlMatch = System.Text.RegularExpressions.Regex.Match(firstItem, @"\[(.+)\]$");
            if (!urlMatch.Success)
            {
                RuleTestStatus = "无法从搜索结果中提取 URL";
                RuleTestDiagnostics = diagAll.ToString();
                RuleEditorSubTab = 1;
                return;
            }

            var bookUrl = urlMatch.Groups[1].Value;
            if (!bookUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(rule.Url))
            {
                if (Uri.TryCreate(rule.Url, UriKind.Absolute, out var baseUri)
                    && Uri.TryCreate(baseUri, bookUrl, out var abs))
                    bookUrl = abs.ToString();
            }

            // 2. 目录
            RuleTestStatus = "测试中… 第 2/3 步：目录";
            var tocResult = await _ruleEditorUseCase.TestTocAsync(rule, bookUrl);
            RuleTestTocPreview = tocResult.Success
                ? string.Join("\n", tocResult.TocItems.Take(30))
                : tocResult.Message;
            diagAll.AppendLine("\n=== 目录 ===");
            foreach (var line in tocResult.DiagnosticLines) diagAll.AppendLine(line);
            diagAll.AppendLine(tocResult.Message);

            if (!tocResult.Success || tocResult.TocItems.Count == 0)
            {
                RuleTestStatus = $"目录解析失败，测试终止。({tocResult.ElapsedMs}ms)";
                RuleTestDiagnostics = diagAll.ToString();
                RuleEditorSubTab = 1;
                return;
            }

            // 3. 第一章正文
            RuleTestStatus = "测试中… 第 3/3 步：正文";
            // ContentPreview 存储的是第一章 URL
            var chapterUrlFromToc = tocResult.ContentPreview;
            if (string.IsNullOrWhiteSpace(chapterUrlFromToc))
            {
                // 尝试从第一个 toc item 提取
                var tocUrlMatch = System.Text.RegularExpressions.Regex.Match(tocResult.TocItems[0], @"\[(.+)\]$");
                chapterUrlFromToc = tocUrlMatch.Success ? tocUrlMatch.Groups[1].Value : string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(chapterUrlFromToc))
            {
                var chapterResult = await _ruleEditorUseCase.TestChapterAsync(rule, chapterUrlFromToc);
                RuleTestContentPreview = chapterResult.Success
                    ? chapterResult.ContentPreview
                    : chapterResult.Message;
                diagAll.AppendLine("\n=== 正文 ===");
                foreach (var line in chapterResult.DiagnosticLines) diagAll.AppendLine(line);
                diagAll.AppendLine(chapterResult.Message);

                RuleTestStatus = chapterResult.Success
                    ? $"✅ 测试完成：搜索={searchResult.SearchItems.Count}条, 目录={tocResult.TocItems.Count}章, 正文={chapterResult.ContentPreview.Length}字"
                    : $"正文提取失败：{chapterResult.Message}";
            }
            else
            {
                RuleTestStatus = $"✅ 搜索+目录成功，但无法提取第一章 URL";
            }

            RuleTestDiagnostics = diagAll.ToString();
            // 自动切到预览子页
            RuleEditorSubTab = 1;
        }
        catch (Exception ex)
        {
            RuleTestStatus = $"测试异常：{ex.Message}";
            RuleTestDiagnostics = diagAll.ToString();
        }
        finally
        {
            IsRuleTesting = false;
        }
    }

    /// <summary>
    /// 运行一次完整调试，并将详细报告复制到剪贴板（便于提交给 AI 分析）。
    /// </summary>
    [RelayCommand]
    private async Task DebugRuleAsync()
    {
        if (CurrentRule is null)
        {
            _parent.StatusMessage = "请先加载或编辑一条规则。";
            return;
        }

        var rule = CurrentRule;
        IsRuleTesting = true;
        RuleTestDiagnostics = string.Empty;
        RuleTestStatus = "Debug 中… 第 1/3 步：搜索";

        var report = new StringBuilder();
        report.AppendLine("# ReadStorm 规则调试报告");
        report.AppendLine();
        report.AppendLine("> **生成时间**: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        report.AppendLine("> ");
        report.AppendLine($"> **规则 ID**: {rule.Id}");
        report.AppendLine("> ");
        report.AppendLine($"> **规则名称**: {rule.Name}");
        report.AppendLine("> ");
        report.AppendLine($"> **站点 URL**: {rule.Url}");
        report.AppendLine();
        report.AppendLine("---");
        report.AppendLine();
        report.AppendLine("## 1. 规则 JSON 定义");
        report.AppendLine();
        report.AppendLine("以下是当前正在调试的完整规则配置（JSON 格式）。请检查各字段是否与目标站点的实际页面结构匹配。");
        report.AppendLine();
        report.AppendLine("```json");
        report.AppendLine(JsonSerializer.Serialize(rule, s_jsonWrite));
        report.AppendLine("```");
        report.AppendLine();

        try
        {
            var keyword = string.IsNullOrWhiteSpace(RuleTestKeyword) ? "诡秘之主" : RuleTestKeyword;
            report.AppendLine("## 2. 测试参数");
            report.AppendLine();
            report.AppendLine($"- **搜索关键字**: `{keyword}`");
            report.AppendLine();
            report.AppendLine("---");
            report.AppendLine();

            // 1) 搜索
            var searchResult = await _ruleEditorUseCase.TestSearchAsync(rule, keyword);
            AppendDebugStep(report, 3, "搜索测试", "使用关键字在目标站点上执行搜索请求，验证搜索规则的 URL、选择器是否能正确提取书籍列表。", searchResult, searchResult.SearchItems);

            if (!searchResult.Success || searchResult.SearchItems.Count == 0)
            {
                RuleTestStatus = $"Debug 终止：搜索未返回结果（{searchResult.ElapsedMs}ms）";
                RuleTestDiagnostics = report.ToString();
                RuleEditorSubTab = 1;
                await CopyDebugReportToClipboardAsync(report.ToString());
                return;
            }

            var firstItem = searchResult.SearchItems[0];
            var bookUrl = ExtractBracketUrl(firstItem);
            if (!bookUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(rule.Url)
                && Uri.TryCreate(rule.Url, UriKind.Absolute, out var baseUri)
                && Uri.TryCreate(baseUri, bookUrl, out var abs))
            {
                bookUrl = abs.ToString();
            }

            report.AppendLine("---");
            report.AppendLine();
            report.AppendLine("## 4. 中间数据：首个书籍 URL");
            report.AppendLine();
            report.AppendLine("从搜索结果的第一项中提取的书籍详情页 URL，将作为下一步目录测试的入口。");
            report.AppendLine();
            report.AppendLine("```");
            report.AppendLine(bookUrl);
            report.AppendLine("```");
            report.AppendLine();

            // 2) 目录
            RuleTestStatus = "Debug 中… 第 2/3 步：目录";
            var tocResult = await _ruleEditorUseCase.TestTocAsync(rule, bookUrl);
            AppendDebugStep(report, 5, "目录测试", "访问书籍详情页，提取章节目录列表。验证目录选择器能否正确匹配章节标题和链接。", tocResult, tocResult.TocItems);

            if (!tocResult.Success || tocResult.TocItems.Count == 0)
            {
                RuleTestStatus = $"Debug 终止：目录为空（{tocResult.ElapsedMs}ms）";
                RuleTestDiagnostics = report.ToString();
                RuleEditorSubTab = 1;
                await CopyDebugReportToClipboardAsync(report.ToString());
                return;
            }

            var chapterUrl = tocResult.ContentPreview;
            if (string.IsNullOrWhiteSpace(chapterUrl))
            {
                chapterUrl = ExtractBracketUrl(tocResult.TocItems[0]);
            }

            report.AppendLine("---");
            report.AppendLine();
            report.AppendLine("## 6. 中间数据：首章 URL");
            report.AppendLine();
            report.AppendLine("从目录的第一个章节中提取的正文页 URL，将用于正文内容提取测试。");
            report.AppendLine();
            report.AppendLine("```");
            report.AppendLine(chapterUrl);
            report.AppendLine("```");
            report.AppendLine();

            // 3) 正文
            RuleTestStatus = "Debug 中… 第 3/3 步：正文";
            var chapterResult = await _ruleEditorUseCase.TestChapterAsync(rule, chapterUrl);
            AppendDebugStep(report, 7, "正文测试", "访问某一章的页面，提取正文内容。验证正文选择器能否正确获取章节文字。", chapterResult, []);

            RuleTestStatus = chapterResult.Success
                ? "✅ Debug 完成，详细报告已复制到剪贴板。"
                : "⚠️ Debug 完成（正文提取失败），详细报告已复制到剪贴板。";

            RuleTestSearchPreview = string.Join("\n", searchResult.SearchItems.Take(20));
            RuleTestTocPreview = string.Join("\n", tocResult.TocItems.Take(30));
            RuleTestContentPreview = chapterResult.Success ? chapterResult.ContentPreview : chapterResult.Message;
            RuleTestDiagnostics = report.ToString();
            RuleEditorSubTab = 1;

            await CopyDebugReportToClipboardAsync(report.ToString());
        }
        catch (Exception ex)
        {
            report.AppendLine("## ❌ 异常信息");
            report.AppendLine();
            report.AppendLine("调试过程中发生未捕获的异常：");
            report.AppendLine();
            report.AppendLine("```");
            report.AppendLine(ex.ToString());
            report.AppendLine("```");
            RuleTestStatus = $"Debug 异常：{ex.Message}";
            RuleTestDiagnostics = report.ToString();
            RuleEditorSubTab = 1;
            await CopyDebugReportToClipboardAsync(report.ToString());
        }
        finally
        {
            IsRuleTesting = false;
        }
    }

    // ==================== Partial methods ====================

    /// <summary>选中规则后加载其对象到编辑器表单。</summary>
    async partial void OnRuleEditorSelectedRuleChanged(RuleListItem? value)
    {
        if (_suppressRuleSelectionLoad)
        {
            return;
        }

        if (value is null) { CurrentRule = null; RuleHasUserOverride = false; return; }
        try
        {
            var rule = await _ruleEditorUseCase.LoadAsync(value.Id);
            if (rule is not null)
            {
                EnsureSubSections(rule);
                CurrentRule = rule;
                RuleHasUserOverride = _ruleEditorUseCase.HasUserOverride(value.Id);
            }
            else
            {
                _parent.StatusMessage = $"未找到 rule-{value.Id}.json";
            }
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"加载规则失败：{ex.Message}";
        }
    }

    // ==================== Internal / Private methods ====================

    /// <summary>根据首页书源健康状态，同步规则处理页左侧列表状态。</summary>
    internal void SyncRuleEditorRuleHealthFromSources()
    {
        if (RuleEditorRules.Count == 0) return;

        var lookup = _parent.Sources
            .Where(s => s.Id > 0)
            .ToDictionary(s => s.Id, s => s.IsHealthy);

        foreach (var item in RuleEditorRules)
        {
            item.IsHealthy = lookup.TryGetValue(item.Id, out var healthy)
                ? healthy
                : null;
        }
    }

    /// <summary>
    /// 保存/恢复后仅就地更新左侧规则列表，避免整表重载导致编辑区失焦。
    /// </summary>
    private void UpsertRuleEditorListItem(FullBookSourceRule rule)
    {
        var index = -1;
        for (var i = 0; i < RuleEditorRules.Count; i++)
        {
            if (RuleEditorRules[i].Id == rule.Id)
            {
                index = i;
                break;
            }
        }

        bool? healthy = null;
        if (index >= 0)
        {
            healthy = RuleEditorRules[index].IsHealthy;
        }
        else
        {
            var src = _parent.Sources.FirstOrDefault(s => s.Id == rule.Id);
            healthy = src?.IsHealthy;
        }

        var updated = new RuleListItem(rule.Id, rule.Name, rule.Url, rule.Search is not null, healthy);
        if (index >= 0)
        {
            RuleEditorRules[index] = updated;
        }
        else
        {
            RuleEditorRules.Add(updated);
        }

        if (RuleEditorSelectedRule?.Id == rule.Id)
        {
            _suppressRuleSelectionLoad = true;
            try
            {
                RuleEditorSelectedRule = updated;
            }
            finally
            {
                _suppressRuleSelectionLoad = false;
            }
        }
    }

    private async Task CopyDebugReportToClipboardAsync(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow?.Clipboard is null)
        {
            _parent.StatusMessage = "Debug 报告已生成，但未能访问剪贴板（已显示在预览诊断中）。";
            return;
        }

        await desktop.MainWindow.Clipboard.SetTextAsync(text);
    }

    private static void AppendDebugStep(
        StringBuilder report,
        int sectionNo,
        string stepName,
        string stepDescription,
        RuleTestResult result,
        IReadOnlyList<string> items)
    {
        report.AppendLine($"## {sectionNo}. {stepName}");
        report.AppendLine();
        report.AppendLine(stepDescription);
        report.AppendLine();

        // ── 测试结果概览 ──
        var statusEmoji = result.Success ? "✅" : "❌";
        report.AppendLine($"### {sectionNo}.1 测试结果");
        report.AppendLine();
        report.AppendLine($"| 项目 | 值 |");
        report.AppendLine($"| --- | --- |");
        report.AppendLine($"| 状态 | {statusEmoji} {(result.Success ? "成功" : "失败")} |");
        report.AppendLine($"| 耗时 | {result.ElapsedMs} ms |");
        report.AppendLine($"| 消息 | {(string.IsNullOrWhiteSpace(result.Message) ? "（无）" : result.Message)} |");
        report.AppendLine();

        // ── 请求信息 ──
        report.AppendLine($"### {sectionNo}.2 HTTP 请求");
        report.AppendLine();
        report.AppendLine("该步骤实际发出的网络请求：");
        report.AppendLine();
        report.AppendLine("```http");
        report.AppendLine($"{result.RequestMethod} {result.RequestUrl}");
        if (!string.IsNullOrWhiteSpace(result.RequestBody))
        {
            report.AppendLine();
            report.AppendLine(result.RequestBody);
        }
        report.AppendLine("```");
        report.AppendLine();

        // ── 使用的 CSS 选择器 ──
        if (result.SelectorLines.Count > 0)
        {
            report.AppendLine($"### {sectionNo}.3 CSS 选择器");
            report.AppendLine();
            report.AppendLine("规则中配置的选择器，AngleSharp 将使用这些选择器在返回的 HTML 中查找目标元素：");
            report.AppendLine();
            report.AppendLine("```css");
            foreach (var line in result.SelectorLines)
            {
                report.AppendLine(line);
            }
            report.AppendLine("```");
            report.AppendLine();
        }

        // ── 诊断详情 ──
        if (result.DiagnosticLines.Count > 0)
        {
            report.AppendLine($"### {sectionNo}.4 诊断详情");
            report.AppendLine();
            report.AppendLine("以下是执行过程中记录的诊断信息，有助于定位选择器匹配失败的原因：");
            report.AppendLine();
            foreach (var line in result.DiagnosticLines)
            {
                report.AppendLine($"- {line}");
            }
            report.AppendLine();
        }

        // ── 匹配结果列表 ──
        if (items.Count > 0)
        {
            report.AppendLine($"### {sectionNo}.5 匹配结果（共 {items.Count} 项）");
            report.AppendLine();
            report.AppendLine("通过选择器成功提取的条目如下。格式：`标题 - 作者 [URL]` 或 `章节名 [URL]`。");
            report.AppendLine();
            var displayCount = Math.Min(items.Count, 50);
            for (int i = 0; i < displayCount; i++)
            {
                report.AppendLine($"{i + 1}. {items[i]}");
            }
            if (items.Count > displayCount)
            {
                report.AppendLine($"\n> …… 还有 {items.Count - displayCount} 项未显示");
            }
            report.AppendLine();
        }
        else if (result.Success)
        {
            report.AppendLine($"### {sectionNo}.5 匹配结果");
            report.AppendLine();
            report.AppendLine("此步骤无列表输出（正文步骤仅输出文本内容）。");
            report.AppendLine();
        }

        // ── 正文内容预览 ──
        if (!string.IsNullOrWhiteSpace(result.ContentPreview))
        {
            report.AppendLine($"### {sectionNo}.6 内容预览");
            report.AppendLine();
            report.AppendLine("提取到的正文文本片段（前 500 字符）：");
            report.AppendLine();
            report.AppendLine("```text");
            var preview = result.ContentPreview.Length > 500
                ? result.ContentPreview[..500] + "\n…（已截断）"
                : result.ContentPreview;
            report.AppendLine(preview);
            report.AppendLine("```");
            report.AppendLine();
        }

        // ── 命中的 HTML 片段 ──
        report.AppendLine($"### {sectionNo}.7 命中的 HTML 片段");
        report.AppendLine();
        if (!string.IsNullOrWhiteSpace(result.MatchedHtml))
        {
            report.AppendLine($"选择器匹配到的 **第一个** DOM 节点的 OuterHtml（长度 {result.MatchedHtml.Length} 字符）。");
            report.AppendLine("如果这个片段的结构不是你期望的，说明选择器可能需要调整。");
            report.AppendLine();
            var matchedDump = result.MatchedHtml.Length > MaxHtmlDumpLength
                ? result.MatchedHtml[..MaxHtmlDumpLength] + "\n<!-- ……已截断，共 " + result.MatchedHtml.Length + " 字符 -->"
                : result.MatchedHtml;
            report.AppendLine("```html");
            report.AppendLine(matchedDump);
            report.AppendLine("```");
        }
        else
        {
            report.AppendLine("**未命中任何 HTML 节点。** 请检查选择器是否正确，或站点页面结构是否已变更。");
        }
        report.AppendLine();

        // ── 原始 HTML ──
        report.AppendLine($"### {sectionNo}.8 原始 HTML");
        report.AppendLine();
        if (!string.IsNullOrWhiteSpace(result.RawHtml))
        {
            report.AppendLine($"服务器返回的完整 HTML 页面（长度 {result.RawHtml.Length} 字符）。");
            report.AppendLine("可以在这段 HTML 中搜索目标内容，以确认选择器应该怎样编写。");
            report.AppendLine();
            var rawDump = result.RawHtml.Length > MaxHtmlDumpLength
                ? result.RawHtml[..MaxHtmlDumpLength] + "\n<!-- ……已截断，共 " + result.RawHtml.Length + " 字符 -->"
                : result.RawHtml;
            report.AppendLine("```html");
            report.AppendLine(rawDump);
            report.AppendLine("```");
        }
        else
        {
            report.AppendLine("未获取到原始 HTML（可能是请求失败或网络超时）。");
        }
        report.AppendLine();
    }
}
