using System.Net;
using System.Net.Http;
using System.Text;
using ReadStorm.Domain.Models;
using ReadStorm.Infrastructure.Services;

namespace ReadStorm.Tests;

public class DownloadPipelineIntegrationTests
{
    [Fact]
    public async Task RuleBasedDownload_ShouldExportTxt_FromFixtureHtmlPipeline()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"readstorm-it-{Guid.NewGuid():N}");
        var ruleDir = Path.Combine(tempRoot, "rules");
        var outputDir = Path.Combine(tempRoot, "downloads");
        Directory.CreateDirectory(ruleDir);
        Directory.CreateDirectory(outputDir);

        var ruleFile = Path.Combine(ruleDir, "rule-501.json");
        await File.WriteAllTextAsync(ruleFile, """
        {
          "id": 501,
          "url": "https://test.local/",
          "toc": {
            "item": "#toc a",
            "offset": 0,
            "desc": false
          },
          "chapter": {
            "content": "#content",
            "filterTxt": "广告词"
          }
        }
        """);

        var settingsPath = Path.Combine(tempRoot, "settings.json");
        var settingsUseCase = new JsonFileAppSettingsUseCase(settingsPath);
        await settingsUseCase.SaveAsync(new AppSettings
        {
            DownloadPath = outputDir,
            ExportFormat = "txt",
        });

        var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://test.local/book/1001.html"] = """
            <html><body>
              <div id='toc'>
                <a href='/chapter-1.html'>第一章 开始</a>
                <a href='/chapter-2.html'>第二章 继续</a>
              </div>
            </body></html>
            """,
            ["https://test.local/chapter-1.html"] = """
            <html><body><div id='content'>第一章正文<br>广告词</div></body></html>
            """,
            ["https://test.local/chapter-2.html"] = """
            <html><body><div id='content'>第二章正文</div></body></html>
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
            Guid.NewGuid(),
            "集成测试小说",
            "测试作者",
            501,
            "集成测试书源",
            "https://test.local/book/1001.html",
            "第二章 继续",
            DateTimeOffset.Now);

        var task = new DownloadTask
        {
            Id = Guid.NewGuid(),
            BookTitle = selectedBook.Title,
            Author = selectedBook.Author,
            Mode = DownloadMode.FullBook,
            EnqueuedAt = DateTimeOffset.Now,
        };

        await sut.QueueAsync(task, selectedBook, DownloadMode.FullBook);

        Assert.Equal(DownloadTaskStatus.Succeeded, task.CurrentStatus);
        Assert.True(File.Exists(task.OutputFilePath));

        var text = await File.ReadAllTextAsync(task.OutputFilePath);
        Assert.Contains("第一章 开始", text);
        Assert.Contains("第一章正文", text);
        Assert.Contains("第二章正文", text);
        Assert.DoesNotContain("广告词", text);
    }

        [Fact]
        public async Task RuleBasedDownload_ShouldDeduplicateDuplicateChapterTitles_InToc()
        {
                var tempRoot = Path.Combine(Path.GetTempPath(), $"readstorm-it-dedup-{Guid.NewGuid():N}");
                var ruleDir = Path.Combine(tempRoot, "rules");
                var outputDir = Path.Combine(tempRoot, "downloads");
                Directory.CreateDirectory(ruleDir);
                Directory.CreateDirectory(outputDir);

                var ruleFile = Path.Combine(ruleDir, "rule-503.json");
                await File.WriteAllTextAsync(ruleFile, """
                {
                    "id": 503,
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
                        ExportFormat = "txt",
                });

                var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                        ["https://test.local/book/3001.html"] = """
                        <html><body>
                            <div id='toc'>
                                <a href='/chapter-60-a.html'>第60章 虎豹骑VS雇佣兵2</a>
                                <a href='/chapter-60-b.html'>第60章 虎豹骑VS雇佣兵2</a>
                                <a href='/chapter-61.html'>第61章 新章节</a>
                            </div>
                        </body></html>
                        """,
                        ["https://test.local/chapter-60-a.html"] = """
                        <html><body><div id='content'>第60章 正文A</div></body></html>
                        """,
                        ["https://test.local/chapter-60-b.html"] = """
                        <html><body><div id='content'>第60章 正文B</div></body></html>
                        """,
                        ["https://test.local/chapter-61.html"] = """
                        <html><body><div id='content'>第61章 正文</div></body></html>
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
                        Guid.NewGuid(),
                        "去重测试小说",
                        "测试作者",
                        503,
                        "集成测试书源",
                        "https://test.local/book/3001.html",
                        "第61章 新章节",
                        DateTimeOffset.Now);

                var task = new DownloadTask
                {
                        Id = Guid.NewGuid(),
                        BookTitle = selectedBook.Title,
                        Author = selectedBook.Author,
                        Mode = DownloadMode.FullBook,
                        EnqueuedAt = DateTimeOffset.Now,
                };

                await sut.QueueAsync(task, selectedBook, DownloadMode.FullBook);

                Assert.Equal(DownloadTaskStatus.Succeeded, task.CurrentStatus);
                var text = await File.ReadAllTextAsync(task.OutputFilePath);
                Assert.Equal(1, CountOccurrences(text, "第60章 虎豹骑VS雇佣兵2"));
                Assert.Contains("第61章 新章节", text);
        }

        [Fact]
        public async Task RuleBasedDownload_RangeMode_ShouldOnlyDownloadSpecifiedWindow()
        {
                var tempRoot = Path.Combine(Path.GetTempPath(), $"readstorm-it-range-{Guid.NewGuid():N}");
                var ruleDir = Path.Combine(tempRoot, "rules");
                var outputDir = Path.Combine(tempRoot, "downloads");
                Directory.CreateDirectory(ruleDir);
                Directory.CreateDirectory(outputDir);

                var ruleFile = Path.Combine(ruleDir, "rule-502.json");
                await File.WriteAllTextAsync(ruleFile, """
                {
                    "id": 502,
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
                        ExportFormat = "txt",
                });

                var responses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                        ["https://test.local/book/2001.html"] = """
                        <html><body>
                            <div id='toc'>
                                <a href='/chapter-1.html'>第一章 A</a>
                                <a href='/chapter-2.html'>第二章 B</a>
                                <a href='/chapter-3.html'>第三章 C</a>
                            </div>
                        </body></html>
                        """,
                        ["https://test.local/chapter-1.html"] = """
                        <html><body><div id='content'>正文A</div></body></html>
                        """,
                        ["https://test.local/chapter-2.html"] = """
                        <html><body><div id='content'>正文B</div></body></html>
                        """,
                        ["https://test.local/chapter-3.html"] = """
                        <html><body><div id='content'>正文C</div></body></html>
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
                        Guid.NewGuid(),
                        "范围测试小说",
                        "测试作者",
                        502,
                        "集成测试书源",
                        "https://test.local/book/2001.html",
                        "第三章 C",
                        DateTimeOffset.Now);

                var task = new DownloadTask
                {
                        Id = Guid.NewGuid(),
                        BookTitle = selectedBook.Title,
                        Author = selectedBook.Author,
                        Mode = DownloadMode.Range,
                        EnqueuedAt = DateTimeOffset.Now,
                        RangeStartIndex = 1,
                        RangeTakeCount = 1,
                        IsAutoPrefetch = true,
                        AutoPrefetchReason = "test-range",
                };

                await sut.QueueAsync(task, selectedBook, DownloadMode.Range);

                Assert.Equal(DownloadTaskStatus.Succeeded, task.CurrentStatus);
                Assert.True(File.Exists(task.OutputFilePath));

                var text = await File.ReadAllTextAsync(task.OutputFilePath);
                Assert.Contains("第二章 B", text);
                Assert.Contains("正文B", text);
                Assert.DoesNotContain("第一章 A", text);
                Assert.DoesNotContain("正文A", text);
                Assert.DoesNotContain("第三章 C", text);
                Assert.DoesNotContain("正文C", text);
        }

    private sealed class FakeHttpMessageHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? string.Empty;
            if (responses.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/html"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found", Encoding.UTF8, "text/plain"),
            });
        }
    }

    private static int CountOccurrences(string text, string token)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
