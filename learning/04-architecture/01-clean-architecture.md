# 4.1 清洁架构详解

[← 上一章：依赖注入](../03-language-features/04-dependency-injection.md) | [返回首页](../README.md) | [下一章：MVVM 模式实践 →](02-mvvm-pattern.md)

---

## 什么是清洁架构

清洁架构（Clean Architecture）由 Robert C. Martin（Uncle Bob）提出，核心原则是 **依赖方向只能从外向内**。内层不知道外层的存在，外层通过接口与内层通信。

---

## ReadStorm 的四层架构

```
┌─────────────────────────────────────────────────┐
│                    UI 层                         │
│         Desktop / Android 平台代码               │
│   MainWindow.axaml    MainActivity.cs            │
│   SearchView.axaml    Views/                     │
├─────────────────────────────────────────────────┤
│               基础设施层                          │
│         Infrastructure                           │
│   SqliteBookRepository    RuleFileLoader         │
│   EpubExporter            AppLogger              │
├─────────────────────────────────────────────────┤
│                应用层                             │
│          Application                             │
│   ISearchBooksUseCase     IBookRepository        │
│   IDownloadBookUseCase    IBookshelfUseCase       │
├─────────────────────────────────────────────────┤
│                领域层                             │
│            Domain                                │
│   BookEntity    ChapterEntity    SearchResult     │
│   DownloadTask  BookSourceRule   AppSettings      │
└─────────────────────────────────────────────────┘
              ↑ 依赖方向（只能从外到内）
```

---

## 各层职责详解

### Domain 层（核心）

**原则**：零外部依赖，纯 C# 类。

```csharp
// BookEntity.cs - 图书实体
public class BookEntity
{
    public string Id { get; set; }
    public string Title { get; set; }
    public string Author { get; set; }
    public string SourceName { get; set; }
    public int ChapterCount { get; set; }
    public DateTime CreateTime { get; set; }
}
```

**为什么**：Domain 是最稳定的层，它定义了 "业务是什么"，不应因技术选型变化而改变。换数据库、换 UI 框架，Domain 层不需要动。

### Application 层（接口）

**原则**：只定义接口，不实现。

```csharp
// ISearchBooksUseCase.cs - 搜索用例接口
public interface ISearchBooksUseCase
{
    Task<List<SearchResult>> SearchAsync(
        string keyword,
        BookSourceRule rule,
        CancellationToken ct);
}

// IBookRepository.cs - 数据仓库接口
public interface IBookRepository
{
    Task<BookEntity?> GetByIdAsync(string id);
    Task SaveAsync(BookEntity book);
    Task<List<BookEntity>> GetAllAsync();
}
```

**为什么**：Application 层定义了 "系统能做什么"。ViewModel 只依赖这些接口，不关心具体实现。这意味着你可以随时替换 SQLite 为其他数据库，ViewModel 完全不需要修改。

### Infrastructure 层（实现）

**原则**：实现 Application 层的接口，处理所有外部交互。

```csharp
// SqliteBookRepository.cs - SQLite 实现
public class SqliteBookRepository : IBookRepository
{
    public async Task<BookEntity?> GetByIdAsync(string id)
    {
        using var connection = new SqliteConnection(_connStr);
        // SQLite 查询实现
    }

    public async Task SaveAsync(BookEntity book)
    {
        using var connection = new SqliteConnection(_connStr);
        // SQLite 插入/更新实现
    }
}
```

**为什么**：所有 "脏活"（网络请求、数据库操作、文件读写）都在这里。这些代码最容易变化，但变化不会影响内层。

### UI 层（展示）

**原则**：只负责界面展示和用户交互，业务逻辑通过 ViewModel 处理。

```xml
<!-- SearchView.axaml - 搜索界面 -->
<TextBox Text="{Binding SearchKeyword}" />
<Button Command="{Binding SearchCommand}" Content="搜索" />
<ListBox ItemsSource="{Binding SearchResults}" />
```

---

## 依赖反转原则

清洁架构的核心是 **依赖反转**：

```
传统方式（❌ 高层依赖低层）：
  ViewModel → SqliteBookRepository

清洁架构（✅ 都依赖抽象）：
  ViewModel → IBookRepository ← SqliteBookRepository
```

ViewModel 依赖 `IBookRepository`（接口），`SqliteBookRepository` 实现 `IBookRepository`。两者都不直接依赖对方，而是都依赖抽象。

> 💡 依赖注入（DI）正是实现依赖反转的技术手段，参见 [3.4 依赖注入](../03-language-features/04-dependency-injection.md)

---

## 数据流动方向

以搜索功能为例：

```
用户操作                            数据流方向
────────────────────────────────────────────
输入关键词 → SearchView             UI → ViewModel
           → SearchDownloadViewModel 调用
           → ISearchBooksUseCase     Application 接口
           → RuleBasedSearchBooksUseCase  Infrastructure 实现
           → HTTP 请求 → 解析 HTML  外部交互
           ← List<SearchResult>     返回数据
           ← 更新 SearchResults     ViewModel → UI
           ← 列表刷新              UI 自动更新
```

---

## 清洁架构的好处

| 好处 | 说明 | ReadStorm 体现 |
|------|------|----------------|
| **可测试** | 内层无外部依赖，易于单元测试 | 可 Mock 接口测试 ViewModel |
| **可维护** | 改变实现不影响业务逻辑 | 切换数据库只改 Infrastructure |
| **可复用** | Domain/Application 可在其他项目使用 | pureopus Kotlin 复用了领域模型概念 |
| **可扩展** | 新功能通过新接口和实现添加 | 添加新书源只需新的 Rule |

---

## 小结

- ReadStorm 采用四层清洁架构：Domain → Application → Infrastructure → UI
- 依赖方向严格从外到内
- 依赖反转通过 DI 容器实现
- 这个架构保证了代码的可测试性、可维护性和可扩展性

---

[← 上一章：依赖注入](../03-language-features/04-dependency-injection.md) | [返回首页](../README.md) | [下一章：MVVM 模式实践 →](02-mvvm-pattern.md)
