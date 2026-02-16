using ReadStorm.Infrastructure.Services;
using ReadStorm.Domain.Models;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("=== Testing Real Search (Source 2: shuhaige.net) ===");

var search = new RuleBasedSearchBooksUseCase();
var results = await search.ExecuteAsync("三国", 2);
Console.WriteLine($"Results: {results.Count}");

if (results.Count > 0)
{
    foreach (var r in results.Take(3))
        Console.WriteLine($"  [{r.SourceId}] {r.Title} by {r.Author} => {r.Url}");
    
    Console.WriteLine("\n=== Testing Real Download (LatestN mode = 20 chapters) ===");
    var tempSettings = Path.Combine(Path.GetTempPath(), $"rs-test-{Guid.NewGuid():N}.json");
    var settings = new JsonFileAppSettingsUseCase(tempSettings);
    var tempOut = Path.Combine(Path.GetTempPath(), "readstorm-download-test");
    await settings.SaveAsync(new AppSettings { DownloadPath = tempOut, ExportFormat = "txt" });
    
    var downloader = new RuleBasedDownloadBookUseCase(settings);
    var task = new DownloadTask
    {
        Id = Guid.NewGuid(),
        BookTitle = results[0].Title,
        Author = results[0].Author,
        Mode = DownloadMode.LatestN,
        EnqueuedAt = DateTimeOffset.Now,
        SourceSearchResult = results[0],
    };
    Console.WriteLine($"Downloading: {results[0].Title}");
    await downloader.QueueAsync(task, results[0], DownloadMode.LatestN);
    Console.WriteLine($"Status: {task.CurrentStatus}");
    Console.WriteLine($"Progress: {task.ProgressPercent}%");
    Console.WriteLine($"Output: {task.OutputFilePath}");
    if (!string.IsNullOrEmpty(task.Error)) Console.WriteLine($"Error: {task.Error}");
}
else
{
    Console.WriteLine("No results - testing source 8 (dxmwx.org)...");
    results = await search.ExecuteAsync("三国", 8);
    Console.WriteLine($"Source 8 Results: {results.Count}");
    foreach (var r in results.Take(3))
        Console.WriteLine($"  [{r.SourceId}] {r.Title} by {r.Author} => {r.Url}");
}
