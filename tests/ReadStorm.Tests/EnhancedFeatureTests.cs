using System.IO.Compression;
using System.Text;
using System.Text.Json;
using ReadStorm.Domain.Models;
using ReadStorm.Infrastructure.Services;

namespace ReadStorm.Tests;

public class EnhancedFeatureTests
{
    // ==================== DownloadTask Retry & State Tests ====================

    [Fact]
    public void DownloadTask_CanRetry_ShouldBeTrueOnlyWhenFailedOrCancelled()
    {
        var task = new DownloadTask { BookTitle = "测试", Author = "作者" };
        Assert.False(task.CanRetry); // Queued

        task.TransitionTo(DownloadTaskStatus.Downloading);
        Assert.False(task.CanRetry);

        task.TransitionTo(DownloadTaskStatus.Failed);
        Assert.True(task.CanRetry);
    }

    [Fact]
    public void DownloadTask_CanCancel_ShouldBeTrueWhenQueuedOrDownloading()
    {
        var task = new DownloadTask { BookTitle = "测试", Author = "作者" };
        Assert.True(task.CanCancel); // Queued

        task.TransitionTo(DownloadTaskStatus.Downloading);
        Assert.True(task.CanCancel);

        task.TransitionTo(DownloadTaskStatus.Succeeded);
        Assert.False(task.CanCancel);
    }

    [Fact]
    public void DownloadTask_ResetForRetry_ShouldResetToQueued()
    {
        var task = new DownloadTask { BookTitle = "测试", Author = "作者" };
        task.TransitionTo(DownloadTaskStatus.Downloading);
        task.TransitionTo(DownloadTaskStatus.Failed);
        task.Error = "某个错误";
        task.ErrorKind = DownloadErrorKind.Network;

        task.ResetForRetry();

        Assert.Equal(DownloadTaskStatus.Queued, task.CurrentStatus);
        Assert.Equal(1, task.RetryCount);
        Assert.Empty(task.Error);
        Assert.Equal(DownloadErrorKind.None, task.ErrorKind);
        Assert.Equal(0, task.ProgressPercent);
    }

    [Fact]
    public void DownloadTask_ResetForRetry_ShouldThrowWhenNotFailedOrCancelled()
    {
        var task = new DownloadTask { BookTitle = "测试", Author = "作者" };
        Assert.Throws<InvalidOperationException>(() => task.ResetForRetry());
    }

    [Fact]
    public void DownloadTask_Paused_ShouldAllowResumeAndCancel()
    {
        var task = new DownloadTask { BookTitle = "测试", Author = "作者" };
        task.TransitionTo(DownloadTaskStatus.Downloading);
        task.TransitionTo(DownloadTaskStatus.Paused);

        Assert.Equal(DownloadTaskStatus.Paused, task.CurrentStatus);
        Assert.True(task.CanCancel);

        // Can resume (Paused -> Downloading)
        task.TransitionTo(DownloadTaskStatus.Downloading);
        Assert.Equal(DownloadTaskStatus.Downloading, task.CurrentStatus);
    }

    [Fact]
    public void DownloadTask_SourceSearchResult_ShouldBeStoredForRetry()
    {
        var searchResult = new SearchResult(
            Guid.NewGuid(), "测试书", "测试作者", 1, "测试书源", "https://example.com/book/1", "第1章", DateTimeOffset.Now);

        var task = new DownloadTask
        {
            BookTitle = "测试书",
            Author = "测试作者",
            SourceSearchResult = searchResult,
        };

        Assert.NotNull(task.SourceSearchResult);
        Assert.Equal("测试书", task.SourceSearchResult.Title);
    }

    // ==================== EPUB Export Tests ====================

    [Fact]
    public async Task EpubExporter_ShouldCreateValidEpubFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"readstorm-epub-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var chapters = new List<(string Title, string Content)>
        {
            ("第一章 开始", "这是第一章的内容。\n段落二。"),
            ("第二章 继续", "这是第二章的内容。\n另一段。"),
        };

        var outputPath = await EpubExporter.ExportAsync(
            tempDir, "测试小说", "测试作者", 1, chapters);

        Assert.True(File.Exists(outputPath));
        Assert.EndsWith(".epub", outputPath);

        // Verify it's a valid ZIP file with expected entries
        using var archive = ZipFile.OpenRead(outputPath);
        var entryNames = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("mimetype", entryNames);
        Assert.Contains("META-INF/container.xml", entryNames);
        Assert.Contains("OEBPS/content.opf", entryNames);
        Assert.Contains("OEBPS/toc.ncx", entryNames);
        Assert.Contains("OEBPS/toc.xhtml", entryNames);
        Assert.Contains("OEBPS/chapter-1.xhtml", entryNames);
        Assert.Contains("OEBPS/chapter-2.xhtml", entryNames);

        // Verify chapter content
        var chapter1Entry = archive.GetEntry("OEBPS/chapter-1.xhtml");
        using var reader = new StreamReader(chapter1Entry!.Open());
        var chapter1Content = await reader.ReadToEndAsync();
        Assert.Contains("第一章 开始", chapter1Content);
        Assert.Contains("这是第一章的内容。", chapter1Content);

        // Verify nav has links
        var navEntry = archive.GetEntry("OEBPS/toc.xhtml");
        using var navReader = new StreamReader(navEntry!.Open());
        var navContent = await navReader.ReadToEndAsync();
        Assert.Contains("chapter-1.xhtml", navContent);
        Assert.Contains("chapter-2.xhtml", navContent);
    }

    // ==================== Bookshelf Persistence Tests ====================

    [Fact]
    public async Task BookshelfUseCase_ShouldAddAndRetrieveBooks()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"readstorm-shelf-{Guid.NewGuid():N}.json");
        var sut = new JsonFileBookshelfUseCase(filePath);

        var book = new BookRecord
        {
            Id = Guid.NewGuid(),
            Title = "测试小说",
            Author = "测试作者",
            FilePath = "/tmp/test.txt",
            Format = "txt",
        };

        await sut.AddAsync(book);
        var books = await sut.GetAllAsync();

        Assert.Single(books);
        Assert.Equal("测试小说", books[0].Title);
        Assert.Equal("测试作者", books[0].Author);
    }

    [Fact]
    public async Task BookshelfUseCase_ShouldRemoveBook()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"readstorm-shelf-{Guid.NewGuid():N}.json");
        var sut = new JsonFileBookshelfUseCase(filePath);

        var book = new BookRecord
        {
            Id = Guid.NewGuid(),
            Title = "待删除",
            Author = "作者",
            FilePath = "/tmp/delete.txt",
        };

        await sut.AddAsync(book);
        await sut.RemoveAsync(book.Id);
        var books = await sut.GetAllAsync();

        Assert.Empty(books);
    }

    [Fact]
    public async Task BookshelfUseCase_ShouldUpdateProgress()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"readstorm-shelf-{Guid.NewGuid():N}.json");
        var sut = new JsonFileBookshelfUseCase(filePath);

        var book = new BookRecord
        {
            Id = Guid.NewGuid(),
            Title = "进度测试",
            Author = "作者",
            FilePath = "/tmp/progress.txt",
        };

        await sut.AddAsync(book);

        var progress = new ReadingProgress
        {
            CurrentChapterIndex = 5,
            CurrentChapterTitle = "第六章",
            LastReadAt = DateTimeOffset.Now,
        };

        await sut.UpdateProgressAsync(book.Id, progress);
        var books = await sut.GetAllAsync();

        Assert.Single(books);
        Assert.Equal(5, books[0].Progress.CurrentChapterIndex);
        Assert.Equal("第六章", books[0].Progress.CurrentChapterTitle);
    }

    [Fact]
    public async Task BookshelfUseCase_ShouldReturnEmpty_WhenFileDoesNotExist()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"readstorm-shelf-{Guid.NewGuid():N}.json");
        var sut = new JsonFileBookshelfUseCase(filePath);

        var books = await sut.GetAllAsync();

        Assert.Empty(books);
    }

    // ==================== Download Pipeline with EPUB Export ====================

    [Fact]
    public async Task RuleBasedDownload_ShouldExportEpub_WhenExportFormatIsEpub()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"readstorm-epub-dl-{Guid.NewGuid():N}");
        var ruleDir = Path.Combine(tempRoot, "rules");
        var outputDir = Path.Combine(tempRoot, "downloads");
        Directory.CreateDirectory(ruleDir);
        Directory.CreateDirectory(outputDir);

        var ruleFile = Path.Combine(ruleDir, "rule-601.json");
        await File.WriteAllTextAsync(ruleFile, """
        {
          "id": 601,
          "url": "https://test.local/",
          "toc": {
            "item": "#toc a",
            "offset": 0,
            "desc": false
          },
          "chapter": {
            "content": "#content"
          }
        }
        """);

        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var settingsUseCase = new JsonFileAppSettingsUseCase(settingsPath);
        await settingsUseCase.SaveAsync(new AppSettings
        {
            DownloadPath = outputDir,
            ExportFormat = "epub",
        });

        var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://test.local/book/601.html"] = """
            <html><body>
              <div id='toc'>
                <a href='https://test.local/ch-1.html'>第一章 测试</a>
              </div>
            </body></html>
            """,
            ["https://test.local/ch-1.html"] = """
            <html><body><div id='content'>EPUB测试正文</div></body></html>
            """,
        };

        var httpClient = new HttpClient(new FakeHttpMessageHandler(responses))
        {
            Timeout = TimeSpan.FromSeconds(3),
        };

        var bookRepo = new SqliteBookRepository(Path.Combine(Path.GetTempPath(), $"readstorm-test-{Guid.NewGuid():N}.db"));
        var coverService = new CoverService(bookRepo, httpClient);

        var sut = new RuleBasedDownloadBookUseCase(settingsUseCase, bookRepo, coverService, httpClient: httpClient, ruleDirectories: [ruleDir]);

        var selectedBook = new SearchResult(
            Guid.NewGuid(), "EPUB测试小说", "EPUB作者", 601,
            "EPUB测试书源",
            "https://test.local/book/601.html", "第一章 测试", DateTimeOffset.Now);

        var task = new DownloadTask
        {
            BookTitle = selectedBook.Title,
            Author = selectedBook.Author,
            Mode = DownloadMode.FullBook,
        };

        await sut.QueueAsync(task, selectedBook, DownloadMode.FullBook);

        Assert.Equal(DownloadTaskStatus.Succeeded, task.CurrentStatus);
        Assert.True(File.Exists(task.OutputFilePath));
        Assert.EndsWith(".epub", task.OutputFilePath);

        // Verify EPUB is a valid zip
        using var archive = ZipFile.OpenRead(task.OutputFilePath);
        Assert.Contains(archive.Entries, e => e.FullName == "OEBPS/chapter-1.xhtml");
    }

    // ==================== Shared Fake Handler ====================

    private sealed class FakeHttpMessageHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (responses.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/html"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
            });
        }
    }
}
