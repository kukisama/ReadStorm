# 5.2 ViewModel 与数据绑定

[← 上一章：Avalonia UI 开发](01-avalonia-ui-development.md) | [返回首页](../README.md) | [下一章：SQLite 数据访问 →](03-sqlite-data-access.md)

---

## 数据绑定概述

数据绑定是 MVVM 模式的核心机制——它将 View（AXAML）中的控件属性与 ViewModel 中的数据属性自动连接起来，实现 UI 和逻辑的解耦。

```
┌──────────────┐   Binding    ┌──────────────────┐
│  TextBox     │ ←──────────→ │  ViewModel       │
│  .Text       │   双向同步    │  .SearchKeyword  │
└──────────────┘              └──────────────────┘
```

---

## 绑定模式

| 模式 | 语法 | 说明 |
|------|------|------|
| 单向 | `{Binding Path, Mode=OneWay}` | 数据 → UI（默认） |
| 双向 | `{Binding Path, Mode=TwoWay}` | 数据 ↔ UI |
| 单次 | `{Binding Path, Mode=OneTime}` | 只绑定一次 |
| 单向到源 | `{Binding Path, Mode=OneWayToSource}` | UI → 数据 |

```xml
<!-- 文本输入框通常用双向绑定 -->
<TextBox Text="{Binding SearchKeyword, Mode=TwoWay}" />

<!-- 只读显示用单向绑定（默认） -->
<TextBlock Text="{Binding StatusMessage}" />

<!-- Slider 等控件也用双向绑定 -->
<Slider Value="{Binding FontSize}" Minimum="12" Maximum="36" />
```

---

## CommunityToolkit.Mvvm 实战

### 源生成器属性

```csharp
public partial class ReaderViewModel : ObservableObject
{
    // [ObservableProperty] 自动生成 public 属性 + 变更通知
    [ObservableProperty]
    private string _currentChapterTitle = "";

    [ObservableProperty]
    private string _chapterContent = "";

    [ObservableProperty]
    private double _fontSize = 18.0;

    [ObservableProperty]
    private bool _isLoading;

    // 联动通知：当 CurrentChapterIndex 改变时，
    // 同时通知 HasPreviousChapter 和 HasNextChapter
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreviousChapter))]
    [NotifyPropertyChangedFor(nameof(HasNextChapter))]
    private int _currentChapterIndex;

    // 计算属性
    public bool HasPreviousChapter => CurrentChapterIndex > 0;
    public bool HasNextChapter => CurrentChapterIndex < TotalChapters - 1;
}
```

### 命令绑定

```csharp
public partial class SearchDownloadViewModel : ObservableObject
{
    // 同步命令
    [RelayCommand]
    private void ClearResults()
    {
        SearchResults.Clear();
        StatusMessage = "已清空";
    }

    // 异步命令
    [RelayCommand]
    private async Task SearchAsync(CancellationToken ct)
    {
        IsSearching = true;
        try
        {
            var results = await _searchUseCase.SearchAsync(SearchKeyword, ct);
            SearchResults.Clear();
            foreach (var r in results)
                SearchResults.Add(r);
            StatusMessage = $"找到 {results.Count} 个结果";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "搜索已取消";
        }
        finally
        {
            IsSearching = false;
        }
    }

    // 带参数的命令
    [RelayCommand]
    private async Task DownloadBookAsync(SearchResult book)
    {
        await _downloadUseCase.StartDownloadAsync(book);
    }
}
```

对应的 AXAML：

```xml
<Button Content="搜索" Command="{Binding SearchCommand}" />
<Button Content="清空" Command="{Binding ClearResultsCommand}" />
<Button Content="下载" Command="{Binding DownloadBookCommand}"
        CommandParameter="{Binding SelectedResult}" />
```

---

## ObservableCollection 与 UI 同步

```csharp
// 使用 ObservableCollection，增删元素时 UI 自动刷新
public ObservableCollection<SearchResult> SearchResults { get; } = new();

// 添加 - UI 自动显示新项
SearchResults.Add(result);

// 移除 - UI 自动移除对应项
SearchResults.Remove(result);

// 清空 - UI 自动清空列表
SearchResults.Clear();
```

> ⚠️ **注意**：`ObservableCollection` 通知的是集合变化（增删），不是元素属性变化。如果需要元素内部属性变化也通知 UI，元素类型本身也需要实现 `INotifyPropertyChanged`。

---

## 数据转换器（Converters）

当数据类型和 UI 需要的类型不匹配时，使用转换器：

```csharp
// 布尔值到可见性转换
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType,
        object parameter, CultureInfo culture)
    {
        return (bool)value ? true : false;
    }

    public object ConvertBack(object value, Type targetType,
        object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

```xml
<!-- 使用转换器 -->
<UserControl.Resources>
    <converters:BoolToVisibilityConverter x:Key="BoolToVis" />
</UserControl.Resources>

<ProgressBar IsVisible="{Binding IsLoading, Converter={StaticResource BoolToVis}}" />
```

> 💡 Avalonia 支持直接在绑定中使用 `!` 反转布尔值：`IsVisible="{Binding !IsLoading}"`

---

## DataContext 设置

### 方式 1：在代码中设置

```csharp
// View 的 code-behind
public SearchView()
{
    InitializeComponent();
    DataContext = new SearchDownloadViewModel(...);
}
```

### 方式 2：通过 DI 容器

```csharp
// App 启动时通过 DI 创建并注入
var viewModel = serviceProvider.GetRequiredService<SearchDownloadViewModel>();
searchView.DataContext = viewModel;
```

### 方式 3：设计时数据

```xml
<!-- 用于 IDE 预览和智能提示 -->
<UserControl x:DataType="vm:SearchDownloadViewModel"
             d:DataContext="{x:Static vm:DesignData.SearchViewModel}">
```

---

## 小结

- 数据绑定是 MVVM 的核心，连接 UI 和 ViewModel
- CommunityToolkit.Mvvm 的 `[ObservableProperty]` 和 `[RelayCommand]` 大幅简化代码
- `ObservableCollection<T>` 实现列表自动同步
- 转换器解决类型不匹配问题
- DataContext 是绑定的数据源

> 💡 更多 MVVM 模式的讨论参见 [4.2 MVVM 模式实践](../04-architecture/02-mvvm-pattern.md)

---

[← 上一章：Avalonia UI 开发](01-avalonia-ui-development.md) | [返回首页](../README.md) | [下一章：SQLite 数据访问 →](03-sqlite-data-access.md)
