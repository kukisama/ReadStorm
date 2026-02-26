# 5.3 SQLite 数据访问

[← 上一章：ViewModel 与数据绑定](02-viewmodel-databinding.md) | [返回首页](../README.md) | [下一章：HTTP 与 HTML 解析 →](04-http-and-html-parsing.md)

---

## 为什么选择 SQLite

ReadStorm 需要在本地存储图书信息、章节内容和阅读状态。SQLite 是理想选择：

- **嵌入式**：不需要安装数据库服务器
- **跨平台**：Windows、Linux、macOS、Android 都支持
- **轻量级**：整个数据库就是一个文件
- **性能好**：对于本地应用的数据量绰绰有余

---

## Microsoft.Data.Sqlite

ReadStorm 使用 `Microsoft.Data.Sqlite` 包——微软官方的轻量级 SQLite ADO.NET 提供程序。

### 基本连接

```csharp
using Microsoft.Data.Sqlite;

// 创建连接
using var connection = new SqliteConnection("Data Source=readstorm.db");
await connection.OpenAsync();

// 执行查询
using var command = connection.CreateCommand();
command.CommandText = "SELECT * FROM books WHERE id = @id";
command.Parameters.AddWithValue("@id", bookId);

using var reader = await command.ExecuteReaderAsync();
while (await reader.ReadAsync())
{
    var title = reader.GetString(reader.GetOrdinal("title"));
    var author = reader.GetString(reader.GetOrdinal("author"));
}
```

> ⚠️ **注意**：始终使用参数化查询（`@id`），防止 SQL 注入攻击。

---

## ReadStorm 的数据仓库模式

### 接口定义（Application 层）

```csharp
// IBookRepository.cs
public interface IBookRepository
{
    Task<BookEntity?> GetByIdAsync(string id);
    Task<List<BookEntity>> GetAllAsync();
    Task SaveAsync(BookEntity book);
    Task DeleteAsync(string id);
    Task<List<ChapterEntity>> GetChaptersAsync(string bookId);
    Task SaveChapterAsync(ChapterEntity chapter);
    Task SaveChaptersAsync(IEnumerable<ChapterEntity> chapters);
}
```

### 实现（Infrastructure 层）

```csharp
// SqliteBookRepository.cs
public class SqliteBookRepository : IBookRepository
{
    private readonly string _connectionString;

    public SqliteBookRepository(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS books (
                id TEXT PRIMARY KEY,
                title TEXT NOT NULL,
                author TEXT,
                source_name TEXT,
                source_url TEXT,
                cover_url TEXT,
                chapter_count INTEGER DEFAULT 0,
                create_time TEXT,
                update_time TEXT
            );

            CREATE TABLE IF NOT EXISTS chapters (
                id TEXT PRIMARY KEY,
                book_id TEXT NOT NULL,
                title TEXT NOT NULL,
                content TEXT,
                index_num INTEGER,
                status INTEGER DEFAULT 0,
                FOREIGN KEY (book_id) REFERENCES books(id)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public async Task<BookEntity?> GetByIdAsync(string id)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM books WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return MapToBookEntity(reader);
        }
        return null;
    }

    public async Task SaveAsync(BookEntity book)
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO books
            (id, title, author, source_name, source_url, cover_url,
             chapter_count, create_time, update_time)
            VALUES
            (@id, @title, @author, @source_name, @source_url, @cover_url,
             @chapter_count, @create_time, @update_time)
            """;
        // 参数绑定...
        await cmd.ExecuteNonQueryAsync();
    }
}
```

---

## 数据库路径管理

不同平台的数据库存储路径不同：

```csharp
// WorkDirectoryManager.cs 管理工作目录
public class WorkDirectoryManager
{
    public string GetWorkDirectory()
    {
        // 回退链策略（Android 兼容）
        var path = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrEmpty(path))
            path = Environment.GetFolderPath(
                Environment.SpecialFolder.Personal);

        if (string.IsNullOrEmpty(path))
            path = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrEmpty(path))
            path = AppContext.BaseDirectory;

        return Path.Combine(path, "ReadStorm");
    }
}
```

> ⚠️ **注意**：Android 上 `Environment.SpecialFolder.MyDocuments` 返回空字符串，必须使用回退链。详见 [8.2 Android 特有问题](../08-troubleshooting/02-android-specific-issues.md)。

---

## 事务处理

批量保存章节时使用事务提升性能：

```csharp
public async Task SaveChaptersAsync(IEnumerable<ChapterEntity> chapters)
{
    using var conn = new SqliteConnection(_connectionString);
    await conn.OpenAsync();

    using var transaction = conn.BeginTransaction();
    try
    {
        foreach (var chapter in chapters)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "INSERT OR REPLACE INTO chapters ...";
            // 参数绑定
            await cmd.ExecuteNonQueryAsync();
        }
        transaction.Commit();
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
```

---

## 小结

- SQLite 是本地应用的最佳嵌入式数据库选择
- `Microsoft.Data.Sqlite` 提供轻量级的 ADO.NET 接口
- 始终使用参数化查询防止 SQL 注入
- 注意跨平台路径差异（特别是 Android）
- 批量操作使用事务提升性能

---

[← 上一章：ViewModel 与数据绑定](02-viewmodel-databinding.md) | [返回首页](../README.md) | [下一章：HTTP 与 HTML 解析 →](04-http-and-html-parsing.md)
