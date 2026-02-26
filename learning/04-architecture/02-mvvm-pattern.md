# 4.2 MVVM 模式实践

[← 上一章：清洁架构详解](01-clean-architecture.md) | [返回首页](../README.md) | [下一章：领域建模 →](03-domain-modeling.md)

---

## 什么是 MVVM

MVVM（Model-View-ViewModel）是 Avalonia / WPF 应用的标准架构模式：

```
┌──────────┐     数据绑定      ┌──────────────┐    调用服务    ┌─────────┐
│   View   │ ←──────────────→ │  ViewModel   │ ───────────→ │  Model  │
│  (AXAML) │   双向绑定        │   (C# 类)    │              │(领域层)  │
└──────────┘                  └──────────────┘              └─────────┘
```

- **Model**：数据和业务逻辑（Domain + Application + Infrastructure）
- **View**：UI 界面（AXAML 文件）
- **ViewModel**：View 和 Model 之间的桥梁

---

## CommunityToolkit.Mvvm

ReadStorm 使用 `CommunityToolkit.Mvvm`（微软官方 MVVM 工具包），它通过源生成器大幅简化 ViewModel 的编写。

### 可观察属性（Observable Properties）

```csharp
// 使用 [ObservableProperty] 自动生成属性变更通知
public partial class SearchDownloadViewModel : ObservableObject
{
    [ObservableProperty]
    private string _searchKeyword = "";

    [ObservableProperty]
    private bool _isSearching;

    // 源生成器自动生成：
    // public string SearchKeyword
    // {
    //     get => _searchKeyword;
    //     set => SetProperty(ref _searchKeyword, value);
    // }
}
```

### 命令（Commands）

```csharp
public partial class SearchDownloadViewModel : ObservableObject
{
    // [RelayCommand] 自动生成 ICommand 属性
    [RelayCommand]
    private async Task SearchAsync()
    {
        IsSearching = true;
        try
        {
            var results = await _searchUseCase.SearchAsync(SearchKeyword, _cts.Token);
            // 更新结果
        }
        finally
        {
            IsSearching = false;
        }
    }

    // 源生成器自动生成：
    // public IAsyncRelayCommand SearchCommand { get; }
}
```

### 属性变更联动

```csharp
// 当某个属性变化时自动通知其他属性更新
[ObservableProperty]
[NotifyPropertyChangedFor(nameof(CanSearch))]
private string _searchKeyword = "";

public bool CanSearch => !string.IsNullOrWhiteSpace(SearchKeyword);
```

---

## ReadStorm 的 ViewModel 清单

| ViewModel | 职责 | 视图 |
|-----------|------|------|
| `MainWindowViewModel` | 主窗口/导航控制 | MainWindow / MainView |
| `SearchDownloadViewModel` | 搜索和下载管理 | SearchView |
| `BookshelfViewModel` | 书架管理 | BookshelfView |
| `ReaderViewModel` | 阅读器 | ReaderView |
| `SettingsViewModel` | 应用设置 | SettingsView |
| `DiagnosticViewModel` | 源诊断 | DiagnosticView |
| `RuleEditorViewModel` | 规则编辑器 | RuleEditorView |

---

## 数据绑定实战

### 文本绑定

```xml
<!-- View (AXAML) -->
<TextBox Text="{Binding SearchKeyword}" Watermark="输入书名..." />
<TextBlock Text="{Binding StatusMessage}" />
```

```csharp
// ViewModel
[ObservableProperty]
private string _searchKeyword = "";

[ObservableProperty]
private string _statusMessage = "就绪";
```

### 命令绑定

```xml
<Button Content="搜索"
        Command="{Binding SearchCommand}"
        IsEnabled="{Binding !IsSearching}" />
```

### 列表绑定

```xml
<ListBox ItemsSource="{Binding SearchResults}"
         SelectedItem="{Binding SelectedResult}">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel>
                <TextBlock Text="{Binding BookName}" FontWeight="Bold" />
                <TextBlock Text="{Binding Author}" Opacity="0.6" />
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

```csharp
// ViewModel
public ObservableCollection<SearchResult> SearchResults { get; } = new();

[ObservableProperty]
private SearchResult? _selectedResult;
```

### 可见性绑定

```xml
<!-- 搜索中显示加载指示器 -->
<ProgressBar IsVisible="{Binding IsSearching}" IsIndeterminate="True" />

<!-- 无结果时显示提示 -->
<TextBlock Text="暂无结果"
           IsVisible="{Binding !HasResults}" />
```

---

## ViewModel 的典型结构

```csharp
public partial class BookshelfViewModel : ObservableObject
{
    // 1. 依赖注入的服务
    private readonly IBookshelfUseCase _bookshelf;
    private readonly IBookRepository _repo;

    // 2. 构造函数接收依赖
    public BookshelfViewModel(IBookshelfUseCase bookshelf, IBookRepository repo)
    {
        _bookshelf = bookshelf;
        _repo = repo;
    }

    // 3. 可观察属性（UI 绑定）
    [ObservableProperty]
    private bool _isLoading;

    // 4. 集合属性
    public ObservableCollection<BookEntity> Books { get; } = new();

    // 5. 命令
    [RelayCommand]
    private async Task LoadBooksAsync()
    {
        IsLoading = true;
        try
        {
            var books = await _bookshelf.GetAllBooksAsync();
            Books.Clear();
            foreach (var book in books)
                Books.Add(book);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // 6. 计算属性
    public bool HasBooks => Books.Count > 0;
}
```

> 💡 更多数据绑定的细节参见 [5.2 ViewModel 与数据绑定](../05-development/02-viewmodel-databinding.md)

---

## 小结

- MVVM 将 UI（View）和逻辑（ViewModel）完全分离
- CommunityToolkit.Mvvm 通过源生成器简化代码量
- `[ObservableProperty]` 自动处理属性通知
- `[RelayCommand]` 自动生成命令绑定
- `ObservableCollection<T>` 实现列表与 UI 的自动同步

---

[← 上一章：清洁架构详解](01-clean-architecture.md) | [返回首页](../README.md) | [下一章：领域建模 →](03-domain-modeling.md)
