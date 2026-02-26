# 7.2 编写测试用例

[← 上一章：测试策略](01-testing-strategy.md) | [返回首页](../README.md) | [下一章：常见问题总览 →](../08-troubleshooting/01-common-issues.md)

---

## xUnit 基础

### Fact - 无参数测试

```csharp
using Xunit;

public class BookEntityTests
{
    [Fact]
    public void NewBook_ShouldHaveDefaultValues()
    {
        var book = new BookEntity();

        Assert.NotNull(book);
        Assert.Equal(0, book.ChapterCount);
    }
}
```

### Theory - 参数化测试

```csharp
public class UrlResolverTests
{
    [Theory]
    [InlineData("https://example.com", "/path", "https://example.com/path")]
    [InlineData("https://a.com/dir/", "page.html", "https://a.com/dir/page.html")]
    [InlineData("https://b.com", "https://c.com/x", "https://c.com/x")]
    public void ResolveUrl_ShouldCombineCorrectly(
        string baseUrl, string relative, string expected)
    {
        var result = UrlResolver.Resolve(baseUrl, relative);
        Assert.Equal(expected, result);
    }
}
```

---

## 常用断言

```csharp
// 相等
Assert.Equal(expected, actual);
Assert.NotEqual(unexpected, actual);

// 空值
Assert.Null(obj);
Assert.NotNull(obj);

// 布尔
Assert.True(condition);
Assert.False(condition);

// 字符串
Assert.Contains("keyword", text);
Assert.StartsWith("prefix", text);
Assert.Empty(text);

// 集合
Assert.Empty(list);
Assert.Single(list);
Assert.Contains(item, list);
Assert.All(list, item => Assert.NotNull(item));

// 异常
Assert.Throws<ArgumentNullException>(() => Method(null));
await Assert.ThrowsAsync<OperationCanceledException>(
    () => AsyncMethod(cancelledToken));

// 类型
Assert.IsType<SqliteBookRepository>(service);
Assert.IsAssignableFrom<IBookRepository>(service);
```

---

## 测试异步代码

```csharp
public class SearchUseCaseTests
{
    [Fact]
    public async Task SearchAsync_WithValidKeyword_ReturnsResults()
    {
        // Arrange
        var useCase = CreateSearchUseCase();
        var keyword = "测试";
        var cts = new CancellationTokenSource();

        // Act
        var results = await useCase.SearchAsync(keyword, cts.Token);

        // Assert
        Assert.NotEmpty(results);
        Assert.All(results, r => Assert.NotEmpty(r.BookName));
    }

    [Fact]
    public async Task SearchAsync_WhenCancelled_ThrowsOperationCancelled()
    {
        var useCase = CreateSearchUseCase();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // 立即取消

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => useCase.SearchAsync("test", cts.Token));
    }
}
```

---

## 测试组织

### Arrange-Act-Assert (AAA) 模式

```csharp
[Fact]
public void ChapterStatus_FromPending_CanTransitionToDownloading()
{
    // Arrange - 准备测试数据
    var chapter = new ChapterEntity
    {
        Status = ChapterStatus.Pending
    };

    // Act - 执行被测操作
    chapter.Status = ChapterStatus.Downloading;

    // Assert - 验证结果
    Assert.Equal(ChapterStatus.Downloading, chapter.Status);
}
```

### 测试辅助方法

```csharp
public class TestHelpers
{
    public static BookEntity CreateTestBook(string title = "测试书名")
    {
        return new BookEntity
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Author = "测试作者",
            SourceName = "测试书源",
            CreateTime = DateTime.UtcNow
        };
    }

    public static List<ChapterEntity> CreateTestChapters(string bookId, int count)
    {
        return Enumerable.Range(1, count)
            .Select(i => new ChapterEntity
            {
                Id = $"{bookId}_ch{i}",
                BookId = bookId,
                Title = $"第{i}章",
                Index = i,
                Status = ChapterStatus.Pending
            })
            .ToList();
    }
}
```

---

## 集成测试

需要真实资源（如数据库）的测试：

```csharp
public class SqliteBookRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteBookRepository _repo;

    public SqliteBookRepositoryTests()
    {
        // 每个测试使用临时数据库
        _dbPath = Path.GetTempFileName();
        _repo = new SqliteBookRepository(_dbPath);
    }

    [Fact]
    public async Task SaveAndGet_ShouldReturnSameBook()
    {
        var book = TestHelpers.CreateTestBook();

        await _repo.SaveAsync(book);
        var loaded = await _repo.GetByIdAsync(book.Id);

        Assert.NotNull(loaded);
        Assert.Equal(book.Title, loaded.Title);
    }

    public void Dispose()
    {
        // 清理临时数据库
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }
}
```

---

## 测试最佳实践

| 实践 | 说明 |
|------|------|
| 每个测试独立 | 不依赖其他测试的执行顺序 |
| 快速执行 | 单元测试应在毫秒级完成 |
| 有意义的命名 | 测试名表达意图 |
| 单一断言焦点 | 每个测试验证一个行为 |
| 使用 AAA 模式 | Arrange-Act-Assert 结构清晰 |
| 避免测试实现 | 测试行为，不测试内部实现细节 |

---

## 小结

- xUnit 提供 `[Fact]` 和 `[Theory]` 两种测试方式
- 使用 AAA 模式组织测试代码
- 异步测试使用 `async Task` 返回类型
- 集成测试使用 `IDisposable` 清理资源
- 关注行为测试而非实现细节

---

[← 上一章：测试策略](01-testing-strategy.md) | [返回首页](../README.md) | [下一章：常见问题总览 →](../08-troubleshooting/01-common-issues.md)
