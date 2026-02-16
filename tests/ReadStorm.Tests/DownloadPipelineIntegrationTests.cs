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

        var sut = new RuleBasedDownloadBookUseCase(settingsUseCase, new SqliteBookRepository(Path.Combine(Path.GetTempPath(), $"readstorm-test-{Guid.NewGuid():N}.db")), httpClient: httpClient, ruleDirectories: [ruleDir]);

        var selectedBook = new SearchResult(
            Guid.NewGuid(),
            "集成测试小说",
            "测试作者",
            501,
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
}
