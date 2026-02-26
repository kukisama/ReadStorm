# 4.3 领域建模

[← 上一章：MVVM 模式实践](02-mvvm-pattern.md) | [返回首页](../README.md) | [下一章：设计决策与取舍 →](04-design-decisions.md)

---

## 领域模型概述

ReadStorm 的领域层定义了阅读应用的核心业务概念。每个模型都是一个纯 C# 类，不依赖任何外部框架。

---

## 核心实体

### 图书（BookEntity）

```csharp
public class BookEntity
{
    public string Id { get; set; }           // 唯一标识
    public string Title { get; set; }        // 书名
    public string Author { get; set; }       // 作者
    public string SourceName { get; set; }   // 来源名称
    public string SourceUrl { get; set; }    // 来源链接
    public string? CoverUrl { get; set; }    // 封面 URL
    public int ChapterCount { get; set; }    // 章节数
    public DateTime CreateTime { get; set; } // 添加时间
    public DateTime UpdateTime { get; set; } // 更新时间
}
```

### 章节（ChapterEntity）

```csharp
public class ChapterEntity
{
    public string Id { get; set; }           // 章节 ID
    public string BookId { get; set; }       // 所属图书 ID
    public string Title { get; set; }        // 章节标题
    public string? Content { get; set; }     // 章节内容
    public int Index { get; set; }           // 章节序号
    public ChapterStatus Status { get; set; } // 下载状态
}
```

### 搜索结果（SearchResult）

```csharp
public record SearchResult(
    string BookName,
    string Author,
    string SourceName,
    string BookUrl,
    string? CoverUrl
);
```

> 💡 注意 `SearchResult` 使用 `record` 类型——搜索结果是不可变的值对象，`record` 自动提供值相等比较和不可变语义。

### 下载任务（DownloadTask）

```csharp
public class DownloadTask
{
    public string BookId { get; set; }
    public string SourceName { get; set; }
    public DownloadTaskStatus Status { get; set; }
    public int TotalChapters { get; set; }
    public int DownloadedChapters { get; set; }
    public string? ErrorMessage { get; set; }
}
```

---

## 枚举与状态

### 章节状态

```csharp
public enum ChapterStatus
{
    Pending,       // 等待下载
    Downloading,   // 下载中
    Done,          // 已完成
    Failed         // 下载失败
}
```

### 下载任务状态

```csharp
public enum DownloadTaskStatus
{
    Pending,       // 等待开始
    Downloading,   // 下载进行中
    Paused,        // 已暂停
    Completed,     // 全部完成
    Failed         // 失败
}
```

---

## 书源规则（BookSourceRule）

ReadStorm 最独特的领域概念——通过 JSON 规则定义如何从不同网站抓取图书数据：

```csharp
public class BookSourceRule
{
    public string Name { get; set; }              // 书源名称
    public string BaseUrl { get; set; }           // 基础 URL
    public string SearchUrl { get; set; }         // 搜索 URL 模板
    public string SearchResultSelector { get; set; } // 搜索结果 CSS 选择器
    public string BookNameSelector { get; set; }  // 书名选择器
    public string ChapterListSelector { get; set; } // 章节列表选择器
    public string ContentSelector { get; set; }   // 正文选择器
    // ... 更多选择器
}
```

> 💡 规则引擎的详细设计参见 [5.5 规则引擎设计](../05-development/05-rules-engine.md)

---

## 实体关系

```
BookEntity (图书)
  ├── 1:N ── ChapterEntity (章节)
  ├── 1:1 ── ReadingStateEntity (阅读状态)
  ├── 1:N ── ReadingBookmarkEntity (书签)
  └── 1:1 ── DownloadTask (下载任务)

BookSourceRule (书源规则)
  └── 用于 ── SearchResult (搜索结果)
                └── 创建 ── BookEntity + DownloadTask

AppSettings (应用设置)
  └── 全局配置
```

---

## 建模原则

ReadStorm 的领域建模遵循以下原则：

| 原则 | 说明 | 示例 |
|------|------|------|
| **无依赖** | Domain 层不引用任何外部 NuGet 包 | 只用 .NET 基础类型 |
| **表达业务** | 类名和属性名反映业务概念 | `BookEntity` 不是 `DataRow` |
| **状态枚举化** | 用枚举表示有限状态 | `ChapterStatus`、`DownloadTaskStatus` |
| **值对象用 record** | 不可变数据用 record | `SearchResult` |
| **可序列化** | 模型可以直接 JSON 序列化 | 存储和传输方便 |

---

## 小结

- 领域层是整个应用最稳定的核心
- 实体反映真实业务概念：图书、章节、搜索结果、下载任务
- 书源规则（BookSourceRule）是 ReadStorm 的独特设计
- 建模原则：无依赖、表达业务、状态枚举化

---

[← 上一章：MVVM 模式实践](02-mvvm-pattern.md) | [返回首页](../README.md) | [下一章：设计决策与取舍 →](04-design-decisions.md)
