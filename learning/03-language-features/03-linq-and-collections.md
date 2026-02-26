# 3.3 LINQ 与集合操作

[← 上一章：异步编程模式](02-async-await-pattern.md) | [返回首页](../README.md) | [下一章：依赖注入 →](04-dependency-injection.md)

---

## 什么是 LINQ

LINQ（Language Integrated Query）是 C# 的集成查询语言，让你可以用统一的语法查询和操作各种数据源——内存集合、数据库、XML 等。

ReadStorm 中大量使用 LINQ 处理搜索结果、章节列表、下载队列等数据。

---

## 基础操作

### Where - 过滤

```csharp
// 筛选已完成的下载任务
var completed = tasks.Where(t => t.Status == DownloadTaskStatus.Completed);

// 多条件过滤
var failed = tasks.Where(t =>
    t.Status == DownloadTaskStatus.Failed && t.RetryCount < 3);
```

### Select - 转换/映射

```csharp
// 提取书名列表
var bookNames = books.Select(b => b.Title);

// 转换为显示用的 ViewModel
var items = searchResults.Select(r => new SearchResultItem
{
    DisplayName = $"{r.BookName} - {r.Author}",
    Source = r.SourceName,
    Url = r.BookUrl
});
```

### OrderBy / OrderByDescending - 排序

```csharp
// 按书名排序
var sorted = books.OrderBy(b => b.Title);

// 按更新时间倒序
var recent = books.OrderByDescending(b => b.UpdateTime);

// 多级排序
var organized = books
    .OrderBy(b => b.Author)
    .ThenBy(b => b.Title);
```

### GroupBy - 分组

```csharp
// 按书源分组
var grouped = searchResults.GroupBy(r => r.SourceName);

foreach (var group in grouped)
{
    Console.WriteLine($"来源: {group.Key}, 结果数: {group.Count()}");
}
```

---

## 聚合操作

```csharp
// 计数
int count = tasks.Count(t => t.Status == DownloadTaskStatus.Pending);

// 求和
int totalChapters = books.Sum(b => b.ChapterCount);

// 是否存在
bool hasFailure = tasks.Any(t => t.Status == DownloadTaskStatus.Failed);

// 全部满足
bool allDone = tasks.All(t => t.Status == DownloadTaskStatus.Completed);

// 第一个匹配（没有则抛异常）
var first = books.First(b => b.Title == "目标书名");

// 第一个匹配（没有则返回 null）
var firstOrNull = books.FirstOrDefault(b => b.Title == "目标书名");
```

---

## 链式操作

LINQ 的强大之处在于链式组合：

```csharp
// ReadStorm 典型场景：处理搜索结果
var displayResults = rawResults
    .Where(r => !string.IsNullOrEmpty(r.BookName))     // 过滤空结果
    .GroupBy(r => r.BookName)                            // 按书名分组
    .Select(g => g.First())                              // 去重（取每组第一个）
    .OrderBy(r => r.BookName)                            // 排序
    .Take(50)                                            // 限制数量
    .ToList();                                           // 执行查询
```

---

## 常用集合类型

| 类型 | 用途 | 线程安全 |
|------|------|:--------:|
| `List<T>` | 通用列表 | ❌ |
| `Dictionary<K,V>` | 键值对查找 | ❌ |
| `HashSet<T>` | 去重集合 | ❌ |
| `Queue<T>` | 先进先出队列 | ❌ |
| `ObservableCollection<T>` | 带变更通知的列表（UI 绑定） | ❌ |
| `ConcurrentDictionary<K,V>` | 并发安全字典 | ✅ |
| `ConcurrentQueue<T>` | 并发安全队列 | ✅ |

### ObservableCollection 在 UI 绑定中的使用

```csharp
// ViewModel 中使用 ObservableCollection 与 UI 绑定
public ObservableCollection<SearchResult> SearchResults { get; } = new();

// 添加结果时 UI 自动更新
SearchResults.Add(newResult);

// 清空时 UI 自动更新
SearchResults.Clear();
```

---

## 延迟执行 vs 立即执行

```csharp
// 延迟执行 - 查询定义，不立即执行
var query = books.Where(b => b.Author == "作者");
// 此时没有实际执行过滤

// 立即执行 - 调用 ToList/ToArray/Count 等触发执行
var result = query.ToList();  // 此时才执行过滤
int count = query.Count();    // 每次都重新执行
```

> 💡 **提示**：如果需要多次使用同一查询结果，先用 `.ToList()` 缓存，避免重复计算。

---

## 性能注意事项

```csharp
// ❌ 低效：每次 Count 都遍历
if (list.Count() > 0) { ... }

// ✅ 高效：Any 找到第一个就返回
if (list.Any()) { ... }

// ❌ 低效：先排序再取第一个
var max = list.OrderByDescending(x => x.Value).First();

// ✅ 高效：直接取最大值
var max = list.MaxBy(x => x.Value);
```

---

## 小结

- LINQ 是 C# 处理数据集合的核心工具
- 链式操作让数据处理代码简洁可读
- `ObservableCollection<T>` 是 MVVM 中 UI 绑定的标配
- 注意延迟执行和性能优化

---

[← 上一章：异步编程模式](02-async-await-pattern.md) | [返回首页](../README.md) | [下一章：依赖注入 →](04-dependency-injection.md)
