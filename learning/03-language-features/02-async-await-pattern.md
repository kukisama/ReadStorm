# 3.2 异步编程模式

[← 上一章：C# 核心特性](01-csharp-core-features.md) | [返回首页](../README.md) | [下一章：LINQ 与集合 →](03-linq-and-collections.md)

---

## 为什么需要异步

ReadStorm 是一个 I/O 密集型应用——搜索图书需要网络请求、下载章节需要等待服务器响应、读取数据需要磁盘操作。如果这些操作在 UI 线程上同步执行，应用会 **卡死**（界面无响应）。

```
同步模式（❌ 用户体验差）：
用户点击搜索 → UI 冻结 → 等待 3 秒 → 返回结果 → UI 恢复

异步模式（✅ 流畅体验）：
用户点击搜索 → 显示加载动画 → 后台请求 → 返回结果 → 更新 UI
```

---

## async/await 基础

### 基本语法

```csharp
// async 标记方法为异步
// await 等待异步操作完成（不阻塞线程）
public async Task<List<SearchResult>> SearchBooksAsync(string keyword)
{
    // await 让出线程，等待网络请求完成
    var html = await _httpClient.GetStringAsync(url);

    // 请求完成后继续执行
    var results = ParseResults(html);
    return results;
}
```

### 返回类型

```csharp
// 有返回值
async Task<string> GetContentAsync() { ... }

// 无返回值
async Task SaveDataAsync() { ... }

// 事件处理器（不推荐在其他场景使用）
async void OnButtonClick(object sender, EventArgs e) { ... }
```

> ⚠️ **注意**：除了事件处理器，永远不要用 `async void`。它无法被 await，异常会直接导致进程崩溃。

---

## CancellationToken - 取消操作

ReadStorm 大量使用 CancellationToken 来支持用户取消操作：

```csharp
// 创建取消令牌源
private CancellationTokenSource? _cts;

// 开始搜索
public async Task StartSearch(string keyword)
{
    // 取消之前的搜索
    _cts?.Cancel();
    _cts = new CancellationTokenSource();

    try
    {
        var results = await _searchUseCase.SearchAsync(keyword, _cts.Token);
        SearchResults = results;
    }
    catch (OperationCanceledException)
    {
        // 用户取消，正常退出
    }
}

// 用户点击取消
public void CancelSearch()
{
    _cts?.Cancel();
}
```

**在 ReadStorm 中的实际应用**：

- 搜索时切换关键词，自动取消上一次搜索
- 下载时用户暂停/取消，通过 CTS 通知下载链路
- 应用退出时取消所有进行中的操作

---

## 并发控制

### SemaphoreSlim - 控制并发数

```csharp
// ReadStorm 中下载队列的并发控制思路
// 同一书源串行下载，不同书源可并行
private readonly SemaphoreSlim _semaphore = new(1, 1);

public async Task DownloadAsync(DownloadTask task, CancellationToken ct)
{
    await _semaphore.WaitAsync(ct);
    try
    {
        await DoDownloadAsync(task, ct);
    }
    finally
    {
        _semaphore.Release();
    }
}
```

### Task.WhenAll - 并行执行

```csharp
// 聚合搜索：同时向多个书源发起搜索请求
public async Task<List<SearchResult>> AggregateSearchAsync(
    string keyword, CancellationToken ct)
{
    var tasks = sources.Select(source =>
        SearchFromSourceAsync(source, keyword, ct));

    var results = await Task.WhenAll(tasks);
    return results.SelectMany(r => r).ToList();
}
```

---

## 异步中的常见陷阱

### 1. 死锁（Deadlock）

```csharp
// ❌ 错误：在异步方法上调用 .Result 或 .Wait()
var result = GetDataAsync().Result;  // 可能死锁！

// ✅ 正确：使用 await
var result = await GetDataAsync();
```

### 2. 异步 void

```csharp
// ❌ 错误：异步 void 方法
async void ProcessData() { ... }  // 异常无法捕获！

// ✅ 正确：返回 Task
async Task ProcessData() { ... }
```

### 3. 忘记 await

```csharp
// ❌ 错误：忘记 await，任务在后台静默失败
SaveToDatabase(data);  // 编译警告但不报错

// ✅ 正确：await 确保完成
await SaveToDatabase(data);
```

### 4. ConfigureAwait

```csharp
// 在非 UI 代码（如 Infrastructure 层）中
// 使用 ConfigureAwait(false) 避免不必要的上下文切换
var data = await httpClient.GetStringAsync(url)
    .ConfigureAwait(false);
```

---

## ReadStorm 中的异步模式总结

| 场景 | 模式 | 示例 |
|------|------|------|
| 网络请求 | async/await + CancellationToken | 搜索、下载章节 |
| 数据库操作 | async/await | 保存书架、读取书签 |
| 并行搜索 | Task.WhenAll | 聚合搜索多个书源 |
| 串行下载 | SemaphoreSlim(1,1) | 同书源章节顺序下载 |
| 用户取消 | CancellationTokenSource | 取消搜索/下载 |
| UI 更新 | Dispatcher.InvokeAsync | 后台线程更新 UI |

---

## 小结

- `async/await` 是 C# 异步编程的核心，让异步代码像同步一样可读
- `CancellationToken` 用于优雅地取消操作
- 并发控制（`SemaphoreSlim`、`Task.WhenAll`）根据场景选择
- 避免 `async void`、`.Result`、忘记 `await` 等常见陷阱

> 💡 异步编程在 ReadStorm 中无处不在，熟练掌握是参与项目开发的前提。

---

[← 上一章：C# 核心特性](01-csharp-core-features.md) | [返回首页](../README.md) | [下一章：LINQ 与集合 →](03-linq-and-collections.md)
