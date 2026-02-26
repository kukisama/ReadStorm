# 1.1 C# 与 .NET 平台概述

[← 返回首页](../README.md) | [下一章：Avalonia 框架介绍 →](02-avalonia-framework.md)

---

## 什么是 C#

C# 是微软推出的面向对象编程语言，诞生于 2000 年，由 Anders Hejlsberg（也是 Delphi 和 TypeScript 的设计者）主导设计。它融合了 C++ 的强大和 Java 的简洁，并持续演进，截至 .NET 10 已经发展到 C# 13。

### 核心特点

- **强类型语言**：编译期类型检查，减少运行时错误
- **面向对象**：完整的 OOP 支持（封装、继承、多态）
- **现代语法**：模式匹配、记录类型、可空引用类型等现代特性
- **自动内存管理**：GC（垃圾回收器）自动管理内存
- **跨平台**：通过 .NET 实现 Windows / Linux / macOS / Android / iOS 全平台运行

### 在 ReadStorm 中的实际运用

ReadStorm 使用 C# 编写所有业务逻辑，从领域模型到 UI 交互：

```csharp
// 领域模型示例 - 使用记录类型简洁定义数据结构
public record SearchResult(
    string BookName,
    string Author,
    string SourceName,
    string BookUrl
);

// 异步操作示例 - 搜索图书
public async Task<List<SearchResult>> SearchAsync(string keyword, CancellationToken ct)
{
    var results = await _searchService.SearchBooksAsync(keyword, ct);
    return results.ToList();
}
```

---

## 什么是 .NET

.NET 是 C# 的运行时平台（Runtime），你可以理解为 "C# 的引擎"。

### .NET 的演进历史

| 时期 | 名称 | 特点 |
|------|------|------|
| 2002-2015 | .NET Framework | 仅限 Windows |
| 2016-2019 | .NET Core 1.0-3.1 | 跨平台，开源 |
| 2020 | .NET 5 | 统一品牌，告别 "Core" |
| 2021-2025 | .NET 6-9 | 持续迭代，性能提升 |
| 2026 | **.NET 10** | **ReadStorm 当前使用版本**，LTS 长期支持 |

### 为什么选择 .NET 10

- **长期支持（LTS）**：3 年官方安全更新
- **性能卓越**：JIT 编译 + AOT 支持，接近原生性能
- **生态成熟**：NuGet 上数十万个包可用
- **跨平台统一**：一套代码，多平台运行

---

## C# 与 .NET 的关系

简单理解：

```
C#（语言） → 编译器（Roslyn） → IL 中间代码 → .NET 运行时 → 各平台执行
```

- **C#** 是你写代码用的语言
- **.NET SDK** 是编译和构建工具
- **.NET Runtime** 是运行时引擎
- **NuGet** 是包管理器（类似 npm、pip）

---

## ReadStorm 用到的 .NET 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET SDK | 10.0 | 编译构建 |
| C# | 13 | 编程语言 |
| Avalonia | 11.3 | 跨平台 UI |
| CommunityToolkit.Mvvm | 8.2 | MVVM 框架 |
| Microsoft.Data.Sqlite | 9.0 | 数据库 |
| AngleSharp | 1.1 | HTML 解析 |
| xUnit | 2.9 | 单元测试 |

---

## 小结

- C# 是一门现代的、强类型的面向对象语言
- .NET 10 是它的运行时平台，支持全平台部署
- ReadStorm 充分利用了 C# 的语言特性和 .NET 生态

> 💡 **提示**：如果你之前用过 Java、TypeScript 或 Kotlin，上手 C# 会非常快——它们在语法和概念上有大量相似之处。

---

[← 返回首页](../README.md) | [下一章：Avalonia 框架介绍 →](02-avalonia-framework.md)
