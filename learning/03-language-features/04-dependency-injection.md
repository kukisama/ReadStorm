# 3.4 依赖注入

[← 上一章：LINQ 与集合](03-linq-and-collections.md) | [返回首页](../README.md) | [下一章：清洁架构详解 →](../04-architecture/01-clean-architecture.md)

---

## 什么是依赖注入

依赖注入（Dependency Injection, DI）是一种设计模式，核心思想是：**对象不自己创建依赖，而是从外部接收依赖**。

### 没有 DI 的代码

```csharp
// ❌ 紧耦合：ViewModel 直接创建具体的服务
public class SearchViewModel
{
    private readonly SqliteBookRepository _repo = new SqliteBookRepository();
    private readonly RuleBasedSearchBooksUseCase _search = new(...);

    // 问题：无法替换实现、无法测试
}
```

### 使用 DI 的代码

```csharp
// ✅ 松耦合：通过构造函数注入接口
public class SearchViewModel
{
    private readonly IBookRepository _repo;
    private readonly ISearchBooksUseCase _search;

    public SearchViewModel(IBookRepository repo, ISearchBooksUseCase search)
    {
        _repo = repo;
        _search = search;
    }
    // 好处：可以替换实现、方便测试
}
```

---

## ReadStorm 中的 DI 实现

ReadStorm 使用 `Microsoft.Extensions.DependencyInjection`——.NET 官方的 DI 容器。

### 服务注册

在应用启动时，将接口和实现类的映射关系注册到 DI 容器：

```csharp
// 服务注册示例
var services = new ServiceCollection();

// 注册基础设施服务
services.AddSingleton<IBookRepository, SqliteBookRepository>();
services.AddSingleton<ISearchBooksUseCase, RuleBasedSearchBooksUseCase>();
services.AddSingleton<IDownloadBookUseCase, RuleBasedDownloadBookUseCase>();
services.AddSingleton<IBookshelfUseCase, JsonFileBookshelfUseCase>();
services.AddSingleton<IRuleCatalogUseCase, EmbeddedRuleCatalogUseCase>();
services.AddSingleton<IAppSettingsUseCase, JsonFileAppSettingsUseCase>();

// 注册 ViewModel
services.AddTransient<SearchDownloadViewModel>();
services.AddTransient<BookshelfViewModel>();
services.AddTransient<ReaderViewModel>();

// 构建服务提供者
var serviceProvider = services.BuildServiceProvider();
```

### 服务生命周期

| 生命周期 | 说明 | ReadStorm 使用场景 |
|----------|------|-------------------|
| `Singleton` | 全局单例 | 数据库仓库、设置服务 |
| `Scoped` | 每个作用域一个实例 | Web 场景用，桌面应用少用 |
| `Transient` | 每次请求新实例 | ViewModel |

### 服务解析

```csharp
// 从容器中获取服务
var searchViewModel = serviceProvider.GetRequiredService<SearchDownloadViewModel>();

// GetRequiredService - 找不到会抛异常（推荐）
// GetService - 找不到返回 null
```

---

## DI 在 ReadStorm 架构中的角色

```
┌───────────────────────────┐
│        UI 层              │
│  注入 ViewModel           │
│  ViewModel 注入 UseCase   │
├───────────────────────────┤
│      DI 容器              │  ← 管理所有依赖关系
│  接口 → 实现的映射表      │
├───────────────────────────┤
│     Infrastructure        │
│  提供接口的具体实现        │
├───────────────────────────┤
│     Application           │
│  定义接口（IXxxUseCase）   │
├───────────────────────────┤
│       Domain              │
│  纯数据模型               │
└───────────────────────────┘
```

**工作流程**：

1. Application 层定义 `ISearchBooksUseCase` 接口
2. Infrastructure 层实现 `RuleBasedSearchBooksUseCase`
3. 启动时注册：`services.AddSingleton<ISearchBooksUseCase, RuleBasedSearchBooksUseCase>()`
4. ViewModel 构造函数声明需要 `ISearchBooksUseCase`
5. DI 容器自动将实现注入到 ViewModel

---

## DI 的好处

### 1. 可测试性

```csharp
// 测试时可以注入 Mock 实现
var mockSearch = new MockSearchBooksUseCase();
var viewModel = new SearchDownloadViewModel(mockSearch, ...);

// 验证 ViewModel 行为，不依赖真实网络请求
```

### 2. 可替换性

```csharp
// 切换实现只需修改注册，不改业务代码
// 比如从文件存储切换到数据库存储
services.AddSingleton<IBookshelfUseCase, SqliteBookshelfUseCase>();  // 替换
```

### 3. 关注点分离

每个类只关心自己的职责，不关心依赖怎么创建。

> 💡 关于 DI 如何融入整体架构，参见 [4.1 清洁架构详解](../04-architecture/01-clean-architecture.md)

---

## 小结

- DI 是现代 .NET 应用的基础设施
- ReadStorm 用 `Microsoft.Extensions.DependencyInjection` 管理所有依赖
- 接口在 Application 层定义，实现在 Infrastructure 层提供
- DI 使得代码可测试、可替换、职责清晰

---

[← 上一章：LINQ 与集合](03-linq-and-collections.md) | [返回首页](../README.md) | [下一章：清洁架构详解 →](../04-architecture/01-clean-architecture.md)
