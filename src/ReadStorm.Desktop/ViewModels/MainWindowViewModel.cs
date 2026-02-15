using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISearchBooksUseCase _searchBooksUseCase;
    private readonly IDownloadBookUseCase _downloadBookUseCase;
    private readonly IAppSettingsUseCase _appSettingsUseCase;
    private readonly IRuleCatalogUseCase _ruleCatalogUseCase;

    public MainWindowViewModel(
        ISearchBooksUseCase searchBooksUseCase,
        IDownloadBookUseCase downloadBookUseCase,
        IAppSettingsUseCase appSettingsUseCase,
        IRuleCatalogUseCase ruleCatalogUseCase)
    {
        _searchBooksUseCase = searchBooksUseCase;
        _downloadBookUseCase = downloadBookUseCase;
        _appSettingsUseCase = appSettingsUseCase;
        _ruleCatalogUseCase = ruleCatalogUseCase;

        Title = "ReadStorm - 下载器重构M0";
        StatusMessage = "就绪：可先用假数据验证 UI 与流程。";

        _ = LoadSettingsAsync();
        _ = LoadRuleStatsAsync();
    }

    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    public ObservableCollection<DownloadTask> DownloadTasks { get; } = [];

    public ObservableCollection<BookSourceRule> Sources { get; } = [];

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private int availableSourceCount;

    [ObservableProperty]
    private string searchKeyword = "诡秘之主";

    [ObservableProperty]
    private int selectedSourceId;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private SearchResult? selectedSearchResult;

    [ObservableProperty]
    private string downloadPath = "downloads";

    [ObservableProperty]
    private int maxConcurrency = 6;

    [ObservableProperty]
    private int minIntervalMs = 200;

    [ObservableProperty]
    private int maxIntervalMs = 400;

    [ObservableProperty]
    private string exportFormat = "epub";

    [ObservableProperty]
    private bool proxyEnabled;

    [ObservableProperty]
    private string proxyHost = "127.0.0.1";

    [ObservableProperty]
    private int proxyPort = 7890;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword) || IsSearching)
        {
            return;
        }

        try
        {
            IsSearching = true;
            StatusMessage = "搜索中...";
            SearchResults.Clear();

            int? sourceId = SelectedSourceId > 0 ? SelectedSourceId : null;
            var results = await _searchBooksUseCase.ExecuteAsync(SearchKeyword.Trim(), sourceId);
            foreach (var item in results)
            {
                SearchResults.Add(item);
            }

            var selectedSourceText = SelectedSourceId > 0 ? $"书源 {SelectedSourceId}" : "全部书源";
            if (SearchResults.Count == 0 && SelectedSourceId > 0)
            {
                StatusMessage = $"搜索完成（{selectedSourceText}）：0 条。该书源当前可能限流/规则不兼容，请切换书源重试。";
            }
            else
            {
                StatusMessage = $"搜索完成（{selectedSourceText}）：共 {SearchResults.Count} 条";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"搜索失败：{ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task QueueDownloadAsync()
    {
        if (SelectedSearchResult is null)
        {
            StatusMessage = "请先在搜索结果中选择一本书。";
            return;
        }

        if (SelectedSearchResult.Url.Contains("example.com", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "当前是示例搜索结果，不支持真实下载。请切换具体书源重新搜索。";
            return;
        }

        try
        {
            var task = new DownloadTask
            {
                Id = Guid.NewGuid(),
                BookTitle = SelectedSearchResult.Title,
                Author = SelectedSearchResult.Author,
                Mode = DownloadMode.FullBook,
                EnqueuedAt = DateTimeOffset.Now,
            };

            DownloadTasks.Insert(0, task);
            StatusMessage = $"已加入下载队列：《{task.BookTitle}》，开始下载...";

            await _downloadBookUseCase.QueueAsync(task, SelectedSearchResult, DownloadMode.FullBook);

            var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "readstorm-download.log");
            if (task.CurrentStatus == DownloadTaskStatus.Succeeded)
            {
                StatusMessage = $"下载完成：《{task.BookTitle}》。调试日志：{logPath}";
            }
            else
            {
                StatusMessage = $"下载失败（{task.ErrorKind}）：{task.Error}。调试日志：{logPath}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加入下载失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var settings = new AppSettings
        {
            DownloadPath = DownloadPath,
            MaxConcurrency = MaxConcurrency,
            MinIntervalMs = MinIntervalMs,
            MaxIntervalMs = MaxIntervalMs,
            ExportFormat = ExportFormat,
            ProxyEnabled = ProxyEnabled,
            ProxyHost = ProxyHost,
            ProxyPort = ProxyPort,
        };

        await _appSettingsUseCase.SaveAsync(settings);
        StatusMessage = "设置已保存到本地用户配置文件。";
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _appSettingsUseCase.LoadAsync();
        DownloadPath = settings.DownloadPath;
        MaxConcurrency = settings.MaxConcurrency;
        MinIntervalMs = settings.MinIntervalMs;
        MaxIntervalMs = settings.MaxIntervalMs;
        ExportFormat = settings.ExportFormat;
        ProxyEnabled = settings.ProxyEnabled;
        ProxyHost = settings.ProxyHost;
        ProxyPort = settings.ProxyPort;
    }

    private async Task LoadRuleStatsAsync()
    {
        var rules = await _ruleCatalogUseCase.GetAllAsync();
        Sources.Clear();
        Sources.Add(new BookSourceRule
        {
            Id = 0,
            Name = "全部书源",
            Url = string.Empty,
            SearchSupported = true,
        });

        foreach (var rule in rules)
        {
            Sources.Add(rule);
        }

        SelectedSourceId = 0;
        AvailableSourceCount = rules.Count;
        StatusMessage = $"就绪：已加载 {AvailableSourceCount} 条书源规则，可切换测试。";
    }
}
