# 3.1 C# 核心语言特性

[← 上一章：编译与运行](../02-environment/03-build-and-run.md) | [返回首页](../README.md) | [下一章：异步编程模式 →](02-async-await-pattern.md)

---

## 概述

C# 13（.NET 10）是一门功能丰富的现代语言。本章聚焦 ReadStorm 中实际使用的核心特性。

---

## 类型系统基础

### 值类型与引用类型

```csharp
// 值类型 - 存储在栈上，赋值是复制
int count = 10;
double price = 9.99;
bool isActive = true;
enum ChapterStatus { Pending, Downloading, Done, Failed }

// 引用类型 - 存储在堆上，赋值是引用传递
string title = "ReadStorm";
BookEntity book = new BookEntity();
List<string> chapters = new List<string>();
```

### 可空引用类型（Nullable Reference Types）

C# 8.0+ 引入的重要特性，ReadStorm 全面启用：

```csharp
// 不可空 - 编译器保证不为 null
string bookName = "测试";

// 可空 - 明确标记可能为 null
string? author = null;

// 使用时需要检查
if (author != null)
{
    Console.WriteLine(author.Length);
}

// 或使用 null 条件运算符
int? len = author?.Length;
string displayName = author ?? "未知作者";
```

---

## 记录类型（Record Types）

C# 9.0+ 引入，用于定义不可变的数据对象：

```csharp
// ReadStorm 中的实际使用
public record SearchResult(
    string BookName,
    string Author,
    string SourceName,
    string BookUrl,
    string? CoverUrl
);

// 自动获得：
// - 构造函数
// - 属性（只读）
// - Equals / GetHashCode
// - ToString
// - 解构赋值
// - with 表达式

var result = new SearchResult("书名", "作者", "来源", "url", null);
var modified = result with { Author = "新作者" };
```

---

## 模式匹配（Pattern Matching）

C# 7.0+ 持续增强的强大特性：

```csharp
// is 模式
if (obj is string text)
{
    Console.WriteLine(text.Length);
}

// switch 表达式（C# 8.0+）
string statusText = status switch
{
    DownloadTaskStatus.Pending => "等待中",
    DownloadTaskStatus.Downloading => "下载中",
    DownloadTaskStatus.Completed => "已完成",
    DownloadTaskStatus.Failed => "失败",
    _ => "未知"
};

// 属性模式
if (task is { Status: DownloadTaskStatus.Failed, RetryCount: > 3 })
{
    // 处理多次重试失败的任务
}
```

---

## 属性和自动属性

```csharp
// 自动属性
public string Title { get; set; }

// 只读自动属性
public string Id { get; }

// init 访问器（C# 9.0+）- 只能在构造时设置
public string Name { get; init; }

// 计算属性
public string DisplayTitle => $"{Title} - {Author}";

// 带验证的属性（在 ViewModel 中常用）
private string _searchKeyword = "";
public string SearchKeyword
{
    get => _searchKeyword;
    set => SetProperty(ref _searchKeyword, value);
}
```

---

## 集合与初始化

```csharp
// 列表初始化
var books = new List<BookEntity>
{
    new() { Title = "书1" },
    new() { Title = "书2" }
};

// 字典初始化
var settings = new Dictionary<string, string>
{
    ["theme"] = "dark",
    ["fontSize"] = "16"
};

// 集合表达式（C# 12+）
int[] numbers = [1, 2, 3, 4, 5];
List<string> names = ["Alice", "Bob"];
```

---

## 字符串处理

```csharp
// 字符串插值
string msg = $"找到 {count} 本书";

// 原始字符串（C# 11+）
string json = """
    {
        "name": "ReadStorm",
        "version": "1.4.0"
    }
    """;

// 字符串比较（跨平台注意）
bool isEqual = string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
```

---

## 异常处理

```csharp
try
{
    var content = await httpClient.GetStringAsync(url);
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    // 特定条件捕获
    logger.Log($"页面未找到: {url}");
}
catch (OperationCanceledException)
{
    // 操作被取消（用户主动）
}
catch (Exception ex)
{
    // 其他异常
    logger.Log($"请求失败: {ex.Message}");
}
finally
{
    // 清理资源
}
```

---

## using 声明

```csharp
// 传统写法
using (var connection = new SqliteConnection(connStr))
{
    // 使用 connection
} // 自动释放

// using 声明（C# 8.0+）—— 作用域结束时自动释放
using var connection = new SqliteConnection(connStr);
// 使用 connection
// 方法结束时自动释放
```

---

## 小结

ReadStorm 中最常用的 C# 特性：

| 特性 | 使用场景 |
|------|----------|
| 可空引用类型 | 全项目启用，减少 NullReferenceException |
| 记录类型 | SearchResult 等数据传输对象 |
| 模式匹配 | 状态判断、类型检查 |
| 字符串插值 | 日志输出、UI 显示 |
| using 声明 | 数据库连接、HTTP 客户端 |

> 💡 接下来深入了解 ReadStorm 中最重要的语言特性——[异步编程模式](02-async-await-pattern.md)

---

[← 上一章：编译与运行](../02-environment/03-build-and-run.md) | [返回首页](../README.md) | [下一章：异步编程模式 →](02-async-await-pattern.md)
