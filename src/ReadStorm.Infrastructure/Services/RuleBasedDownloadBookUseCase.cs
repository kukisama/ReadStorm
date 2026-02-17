using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class RuleBasedDownloadBookUseCase : IDownloadBookUseCase
{
    private const int MaxErrorTraceLines = 18;

    private static readonly Lock LogFileLock = new();

    private readonly IAppSettingsUseCase _settingsUseCase;
    private readonly IBookRepository _bookRepo;
    private readonly ISearchBooksUseCase? _searchUseCase;
    private readonly CoverService _coverService;
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<string> _ruleDirectories;

    public RuleBasedDownloadBookUseCase(
        IAppSettingsUseCase settingsUseCase,
        IBookRepository bookRepo,
        CoverService coverService,
        ISearchBooksUseCase? searchUseCase = null,
        HttpClient? httpClient = null,
        IReadOnlyList<string>? ruleDirectories = null)
    {
        _settingsUseCase = settingsUseCase;
        _bookRepo = bookRepo;
        _coverService = coverService;
        _searchUseCase = searchUseCase;
        _httpClient = httpClient ?? RuleHttpHelper.CreateHttpClient();
        _ruleDirectories = ruleDirectories ?? RulePathResolver.ResolveAllRuleDirectories();
    }

    public async Task QueueAsync(
        DownloadTask task,
        SearchResult selectedBook,
        DownloadMode mode,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>(capacity: 256);

        void Trace(string message)
        {
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            diagnostics.Add(line);
            AppendDiagnosticLog(line);
        }

        try
        {
            Trace($"[download-start] taskId={task.Id}, sourceId={selectedBook.SourceId}, book='{selectedBook.Title}', author='{selectedBook.Author}', mode={mode}");
            task.TransitionTo(DownloadTaskStatus.Downloading);

            if (string.IsNullOrWhiteSpace(selectedBook.Url))
            {
                throw new InvalidOperationException("当前搜索结果未包含书籍详情 URL，无法下载。");
            }

            Trace($"[input] bookUrl={selectedBook.Url}");

            var settings = await _settingsUseCase.LoadAsync(cancellationToken);
            Trace($"[settings] downloadPath='{settings.DownloadPath}', exportFormat='{settings.ExportFormat}', proxyEnabled={settings.ProxyEnabled}");

            var rule = await LoadRuleAsync(selectedBook.SourceId, cancellationToken)
                       ?? throw new InvalidOperationException($"未找到书源 {selectedBook.SourceId} 对应规则文件。");
            Trace($"[rule] sourceId={rule.Id}, baseUrl='{rule.Url}', toc-item='{rule.Toc?.Item}', chapter-content='{rule.Chapter?.Content}'");

            // ====== 1. 查找或创建 BookEntity ======
            var tocChapters = await FetchTocAsync(rule, selectedBook, mode, cancellationToken, Trace);
            Trace($"[toc] parsedChapters={tocChapters.Count}");
            if (tocChapters.Count == 0)
            {
                Trace("[skip] 目录为空，不写入书架");
                task.ErrorKind = DownloadErrorKind.Unknown;
                task.Error = "章节目录为空，未加入书架。";
                task.TransitionTo(DownloadTaskStatus.Failed);
                return;
            }

            // 先生成 BookEntity，但不立即写入数据库
            var bookEntity = await _bookRepo.FindBookAsync(selectedBook.Title, selectedBook.Author, cancellationToken);
            var isNewBook = bookEntity is null;
            if (bookEntity is null)
            {
                bookEntity = new BookEntity
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Title = selectedBook.Title,
                    Author = selectedBook.Author,
                    SourceId = selectedBook.SourceId,
                    TocUrl = selectedBook.Url,
                    CreatedAt = DateTimeOffset.Now.ToString("o"),
                    UpdatedAt = DateTimeOffset.Now.ToString("o"),
                };

                // 尝试提取封面：先试原站，失败（含反盗链）则自动找起点
                try
                {
                    var tocHtml = await FetchHtmlByUrlAsync(selectedBook.Url, cancellationToken, Trace);
                    var allCandidates = _coverService.ExtractCoverCandidatesFromHtml(tocHtml, selectedBook.Url).ToList();

                    // 把起点候选也加进来，这样原站全部失败后会自动尝试起点
                    var qidianCandidates = await _coverService.GetQidianCandidatesAsync(bookEntity.Title, cancellationToken);
                    foreach (var qc in qidianCandidates)
                    {
                        if (allCandidates.Any(x => string.Equals(x.ImageUrl, qc.ImageUrl, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        qc.Index = allCandidates.Count + 1;
                        allCandidates.Add(qc);
                    }

                    var coverFound = false;
                    foreach (var coverCandidate in allCandidates)
                    {
                        var referer = CoverService.GetReferer(selectedBook.Url, coverCandidate);
                        var (coverBytes, coverExt, failReason) = await _coverService.DownloadCoverAsync(coverCandidate.ImageUrl, referer, cancellationToken);
                        if (coverBytes.Length == 0)
                        {
                            Trace($"[cover] 候选失败: [{coverCandidate.Index}] {coverCandidate.Rule} — {failReason}");
                            continue;
                        }

                        bookEntity.CoverUrl = coverCandidate.ImageUrl;
                        bookEntity.CoverBlob = coverBytes;
                        bookEntity.CoverImage = string.Empty;
                        bookEntity.CoverRule = $"auto:{coverCandidate.Rule}|{coverCandidate.HtmlSnippet}";
                        Trace($"[cover] rule='{coverCandidate.Rule}', url='{coverCandidate.ImageUrl}', blobLen={bookEntity.CoverBlob.Length}");
                        var localPath = CoverService.SaveToLocal(bookEntity, coverBytes, coverExt);
                        Trace($"[cover] local='{localPath}'");
                        coverFound = true;
                        break;
                    }

                    if (!coverFound)
                    {
                        Trace($"[cover] 所有候选下载失败（包括起点），共 {allCandidates.Count} 个");
                    }
                }
                catch (Exception coverEx)
                {
                    Trace($"[cover] 封面提取失败: {coverEx.Message}");
                }
            }
            else
            {
                Trace($"[db] found existing BookEntity id={bookEntity.Id}, done={bookEntity.DoneChapters}/{bookEntity.TotalChapters}");
            }
            task.BookId = bookEntity.Id;

            // 必须先写入 book（外键约束），后续如果全部失败会删除
            await _bookRepo.UpsertBookAsync(bookEntity, cancellationToken);

            // 将目录转换为 ChapterEntity（status=Pending, content=null）
            var chapterEntities = tocChapters.Select((ch, i) => new ChapterEntity
            {
                BookId = bookEntity.Id,
                IndexNo = i,
                Title = ch.Title,
                Status = ChapterStatus.Pending,
                SourceId = selectedBook.SourceId,
                SourceUrl = ch.Url,
            }).ToList();
            await _bookRepo.InsertChaptersAsync(bookEntity.Id, chapterEntities, cancellationToken);

            // 容错：若历史数据存在“章节缺洞”（total 大于已存章节，但无 Pending/Failed），主动回填缺失索引为 Pending
            var allChaptersAfterInsert = await _bookRepo.GetChaptersAsync(bookEntity.Id, cancellationToken);
            var existingIndexes = allChaptersAfterInsert.Select(c => c.IndexNo).ToHashSet();
            var missingChapterEntities = tocChapters
                .Select((ch, i) => new { Chapter = ch, Index = i })
                .Where(x => !existingIndexes.Contains(x.Index))
                .Select(x => new ChapterEntity
                {
                    BookId = bookEntity.Id,
                    IndexNo = x.Index,
                    Title = x.Chapter.Title,
                    Status = ChapterStatus.Pending,
                    SourceId = selectedBook.SourceId,
                    SourceUrl = x.Chapter.Url,
                })
                .ToList();
            if (missingChapterEntities.Count > 0)
            {
                await _bookRepo.InsertChaptersAsync(bookEntity.Id, missingChapterEntities, cancellationToken);
                Trace($"[chapter-repair] missingRows={missingChapterEntities.Count}, repairedIndexes={string.Join(',', missingChapterEntities.Take(10).Select(c => c.IndexNo + 1))}");
            }

            // ====== 3. 下载 Pending/Failed 章节 ======
            var pendingChapters = await _bookRepo.GetChaptersByStatusAsync(bookEntity.Id, ChapterStatus.Pending, cancellationToken);
            var failedChapters = await _bookRepo.GetChaptersByStatusAsync(bookEntity.Id, ChapterStatus.Failed, cancellationToken);
            var downloadingChapters = await _bookRepo.GetChaptersByStatusAsync(bookEntity.Id, ChapterStatus.Downloading, cancellationToken);
            var toDownload = pendingChapters
                .Concat(failedChapters)
                .Concat(downloadingChapters)
                .GroupBy(c => c.IndexNo)
                .Select(g => g.First())
                .OrderBy(c => c.IndexNo)
                .ToList();
            Trace($"[download] pending={pendingChapters.Count}, failed={failedChapters.Count}, downloading={downloadingChapters.Count}, toDownload={toDownload.Count}");

            if (toDownload.Count == 0)
            {
                Trace("[download] all chapters already done, skipping to export");
            }

            var skippedChapters = new List<string>();
            var random = new Random();
            var totalChapters = tocChapters.Count;
            for (var i = 0; i < toDownload.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chapter = toDownload[i];
                Trace($"[chapter-fetch-start] index={chapter.IndexNo + 1}/{totalChapters}, title='{chapter.Title}', url='{chapter.SourceUrl}'");

                // 标记为 Downloading
                await _bookRepo.UpdateChapterAsync(bookEntity.Id, chapter.IndexNo, ChapterStatus.Downloading, null, null, cancellationToken);

                try
                {
                    var content = await FetchChapterContentAsync(rule, chapter.SourceUrl, cancellationToken, Trace);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        Trace($"[chapter-fetch-empty] index={chapter.IndexNo + 1}/{totalChapters}, title='{chapter.Title}'");
                        await _bookRepo.UpdateChapterAsync(bookEntity.Id, chapter.IndexNo, ChapterStatus.Failed, null, "内容为空", cancellationToken);
                        skippedChapters.Add($"#{chapter.IndexNo + 1} {chapter.Title} (内容为空)");
                        continue;
                    }

                    // 保存到 DB
                    await _bookRepo.UpdateChapterAsync(bookEntity.Id, chapter.IndexNo, ChapterStatus.Done, content, null, cancellationToken);

                    // 更新 UI 进度
                    var doneCount = await _bookRepo.CountDoneChaptersAsync(bookEntity.Id, cancellationToken);
                    var percent = (int)Math.Round(((double)doneCount / totalChapters) * 100d);
                    task.UpdateProgress(percent);
                    task.UpdateChapterProgress(doneCount, totalChapters, chapter.Title);
                    Trace($"[chapter-fetch-ok] index={chapter.IndexNo + 1}/{totalChapters}, chars={content.Length}, done={doneCount}, progress={percent}%");
                }
                catch (OperationCanceledException)
                {
                    // 用户取消：把当前章节恢复为 Pending
                    await _bookRepo.UpdateChapterAsync(bookEntity.Id, chapter.IndexNo, ChapterStatus.Pending, null, null, CancellationToken.None);
                    throw;
                }
                catch (Exception ex)
                {
                    Trace($"[chapter-fetch-failed] index={chapter.IndexNo + 1}/{totalChapters}, title='{chapter.Title}', error={ex.Message}");
                    await _bookRepo.UpdateChapterAsync(bookEntity.Id, chapter.IndexNo, ChapterStatus.Failed, null, ex.Message, cancellationToken);
                    skippedChapters.Add($"#{chapter.IndexNo + 1} {chapter.Title} ({ex.Message})");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }

                // 每章之间随机延迟
                if (i < toDownload.Count - 1)
                {
                    var intervalMs = random.Next(settings.MinIntervalMs, settings.MaxIntervalMs + 1);
                    await Task.Delay(intervalMs, cancellationToken);
                }
            }

            if (skippedChapters.Count > 0)
            {
                Trace($"[chapter-skipped] {skippedChapters.Count} 章失败/为空：{string.Join("; ", skippedChapters.Take(10))}");
            }

            // ====== 4. 导出（从 DB 中读取已完成的章节） ======
            var doneContents = await _bookRepo.GetDoneChapterContentsAsync(bookEntity.Id, cancellationToken);
            Trace($"[chapter-summary] doneChapters={doneContents.Count}, totalChapters={totalChapters}");
            if (doneContents.Count == 0)
            {
                Trace("[skip] 正文全部失败，从书架移除");
                if (isNewBook)
                    await _bookRepo.DeleteBookAsync(bookEntity.Id, cancellationToken);
                task.ErrorKind = DownloadErrorKind.Unknown;
                task.Error = "正文抓取结果为空，未加入书架。";
                task.TransitionTo(DownloadTaskStatus.Failed);
                return;
            }

            // 更新书架记录
            bookEntity.TotalChapters = tocChapters.Count;
            bookEntity.DoneChapters = doneContents.Count;
            await _bookRepo.UpsertBookAsync(bookEntity, cancellationToken);
            Trace($"[db] chapters inserted, total={tocChapters.Count}, alreadyDone={bookEntity.DoneChapters}");

            var outputPath = string.Equals(settings.ExportFormat, "epub", StringComparison.OrdinalIgnoreCase)
                ? await EpubExporter.ExportAsync(settings.DownloadPath, selectedBook.Title, selectedBook.Author, selectedBook.SourceId, doneContents, cancellationToken)
                : await ExportTxtAsync(settings, selectedBook, doneContents, cancellationToken);
            task.OutputFilePath = outputPath;
            task.TransitionTo(DownloadTaskStatus.Succeeded);
            Trace($"[export-ok] output='{outputPath}', finalStatus={task.CurrentStatus}");
            Trace($"[download-end] taskId={task.Id}, elapsed={(DateTimeOffset.Now - task.EnqueuedAt).TotalSeconds:F2}s");
            return;
        }
        catch (OperationCanceledException)
        {
            task.TransitionTo(DownloadTaskStatus.Cancelled);
            task.ErrorKind = DownloadErrorKind.Cancelled;
            task.Error = "任务已取消（已下载的章节已保存到数据库）";
            Trace($"[download-cancelled] taskId={task.Id}");
            return;
        }
        catch (Exception ex)
        {
            if (task.CurrentStatus == DownloadTaskStatus.Queued)
            {
                task.TransitionTo(DownloadTaskStatus.Failed);
            }
            else if (task.CurrentStatus == DownloadTaskStatus.Downloading)
            {
                task.TransitionTo(DownloadTaskStatus.Failed);
            }

            task.ErrorKind = Classify(ex);
            task.Error = BuildErrorWithDiagnostics(ex.Message, diagnostics);
            Trace($"[download-failed] taskId={task.Id}, kind={task.ErrorKind}, error={ex.Message}");
            return;
        }
    }

    /// <inheritdoc />
    public async Task<int> CheckNewChaptersAsync(BookEntity book, CancellationToken cancellationToken = default)
    {
        var rule = await LoadRuleAsync(book.SourceId, cancellationToken);
        if (rule is null) return 0;

        var searchResult = new SearchResult(
            Guid.Empty, book.Title, book.Author, book.SourceId,
            string.Empty,
            book.TocUrl, string.Empty, DateTimeOffset.Now);

        var tocChapters = await FetchTocAsync(rule, searchResult, DownloadMode.FullBook, cancellationToken, quickCheck: true);
        var existingChapters = await _bookRepo.GetChaptersAsync(book.Id, cancellationToken);
        var existingCount = existingChapters.Count;

        if (tocChapters.Count <= existingCount)
            return 0;

        var newChapters = tocChapters.Skip(existingCount).Select((ch, i) => new ChapterEntity
        {
            BookId = book.Id,
            IndexNo = existingCount + i,
            Title = ch.Title,
            Status = ChapterStatus.Pending,
            SourceId = book.SourceId,
            SourceUrl = ch.Url,
        }).ToList();

        await _bookRepo.InsertChaptersAsync(book.Id, newChapters, cancellationToken);

        book.TotalChapters = tocChapters.Count;
        await _bookRepo.UpsertBookAsync(book, cancellationToken);

        return newChapters.Count;
    }

    private Task<RuleFileDto?> LoadRuleAsync(int sourceId, CancellationToken cancellationToken)
        => RuleFileLoader.LoadRuleAsync(_ruleDirectories, sourceId, cancellationToken);

    private async Task<IReadOnlyList<ChapterRef>> FetchTocAsync(
        RuleFileDto rule,
        SearchResult book,
        DownloadMode mode,
        CancellationToken cancellationToken,
        Action<string>? trace = null,
        bool quickCheck = false)
    {
        // 决定使用的 HTML 抓取方法：quickCheck 走轻量单次请求，否则走重试
        Func<string, CancellationToken, Action<string>?, Task<string>> fetchHtml =
            quickCheck ? FetchHtmlOnceAsync : FetchHtmlByUrlAsync;

        var toc = rule.Toc;
        if (toc is null || string.IsNullOrWhiteSpace(toc.Item))
        {
            trace?.Invoke("[toc] rule.toc.item is empty");
            return [];
        }

        var firstUrl = BuildTocUrl(rule, book.Url);
        if (string.IsNullOrWhiteSpace(firstUrl))
        {
            trace?.Invoke("[toc] first toc url is empty");
            return [];
        }

        trace?.Invoke($"[toc] firstPageUrl='{firstUrl}'");

        var pageUrls = new List<string> { firstUrl };
        var firstHtml = await fetchHtml(firstUrl, cancellationToken, trace);
        if (string.IsNullOrWhiteSpace(firstHtml))
        {
            trace?.Invoke("[toc] first page html is empty");
            return [];
        }

        if (toc.Pagination && !string.IsNullOrWhiteSpace(toc.NextPage))
        {
            var extraUrls = ExtractNextPageUrls(firstHtml, firstUrl, toc.NextPage, 2);
            pageUrls.AddRange(extraUrls);
            trace?.Invoke($"[toc] pagination enabled, extraPages={extraUrls.Count}");
        }

        var allItems = new List<ChapterRef>();
        foreach (var pageUrl in pageUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            trace?.Invoke($"[toc-page] parsing='{pageUrl}'");
            var html = pageUrl == firstUrl ? firstHtml : await fetchHtml(pageUrl, cancellationToken, trace);
            if (string.IsNullOrWhiteSpace(html))
            {
                trace?.Invoke($"[toc-page] empty html, skip='{pageUrl}'");
                continue;
            }

            var refs = ParseTocPage(html, pageUrl, toc.Item, toc.Offset ?? 0);
            trace?.Invoke($"[toc-page] parsedItems={refs.Count}, page='{pageUrl}'");
            allItems.AddRange(refs);
        }

        allItems = allItems
            .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (toc.Desc)
        {
            allItems.Reverse();
            trace?.Invoke("[toc] chapter order reversed by rule.desc=true");
        }

        var limited = mode switch
        {
            DownloadMode.FullBook => allItems.ToList(),
            DownloadMode.LatestN => allItems.TakeLast(20).ToList(),
            _ => allItems.Take(40).ToList(),
        };

        for (var i = 0; i < limited.Count; i++)
        {
            limited[i] = limited[i] with { Order = i + 1 };
        }

        trace?.Invoke($"[toc] deduplicated={allItems.Count}, mode={mode}, selected={limited.Count}");

        return limited;
    }

    private static List<ChapterRef> ParseTocPage(string html, string pageUrl, string itemSelectorRaw, int offset)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        var itemSelector = RuleFileLoader.NormalizeSelector(itemSelectorRaw);
        if (string.IsNullOrWhiteSpace(itemSelector))
        {
            return [];
        }

        var nodes = doc.QuerySelectorAll(itemSelector).ToList();
        if (offset > 0 && nodes.Count > offset)
        {
            nodes = nodes.Skip(offset).ToList();
        }
        else if (offset < 0 && nodes.Count > -offset)
        {
            nodes = nodes.Take(nodes.Count + offset).ToList();
        }

        var result = new List<ChapterRef>(nodes.Count);
        foreach (var node in nodes)
        {
            var title = node.TextContent?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var href = node.GetAttribute("href") ?? string.Empty;
            var url = RuleFileLoader.ResolveUrl(pageUrl, href);
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            result.Add(new ChapterRef(0, title, url));
        }

        return result;
    }

    private async Task<string> FetchChapterContentAsync(
        RuleFileDto rule,
        string chapterUrl,
        CancellationToken cancellationToken,
        Action<string>? trace = null)
    {
        var chapter = rule.Chapter;
        if (chapter is null || string.IsNullOrWhiteSpace(chapter.Content))
        {
            trace?.Invoke($"[chapter] content selector empty, url='{chapterUrl}'");
            return string.Empty;
        }

        var contentSelector = RuleFileLoader.NormalizeSelector(chapter.Content);
        if (string.IsNullOrWhiteSpace(contentSelector))
        {
            trace?.Invoke($"[chapter] normalized selector empty, raw='{chapter.Content}', url='{chapterUrl}'");
            return string.Empty;
        }

        var html = await FetchHtmlByUrlAsync(chapterUrl, cancellationToken, trace);
        if (string.IsNullOrWhiteSpace(html))
        {
            trace?.Invoke($"[chapter] html empty, url='{chapterUrl}'");
            return string.Empty;
        }

        var pages = new List<string> { html };
        if (chapter.Pagination && !string.IsNullOrWhiteSpace(chapter.NextPage))
        {
            var nextUrls = ExtractNextPageUrls(html, chapterUrl, chapter.NextPage, 5);
            trace?.Invoke($"[chapter] pagination nextPages={nextUrls.Count}, url='{chapterUrl}'");
            foreach (var next in nextUrls)
            {
                var nextHtml = await FetchHtmlByUrlAsync(next, cancellationToken, trace);
                if (!string.IsNullOrWhiteSpace(nextHtml))
                {
                    pages.Add(nextHtml);
                }
            }
        }

        var full = new StringBuilder();
        foreach (var page in pages)
        {
            var text = ExtractTextFromContent(page, contentSelector);
            if (!string.IsNullOrWhiteSpace(text))
            {
                full.AppendLine(text);
            }
        }

        var content = full.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(chapter.FilterTxt))
        {
            try
            {
                content = Regex.Replace(content, chapter.FilterTxt, string.Empty, RegexOptions.Multiline);
                trace?.Invoke($"[chapter] filter applied, pattern='{chapter.FilterTxt}', url='{chapterUrl}'");
            }
            catch
            {
                // ignore bad regex from rule
                trace?.Invoke($"[chapter] bad filter regex ignored, pattern='{chapter.FilterTxt}', url='{chapterUrl}'");
            }
        }

        trace?.Invoke($"[chapter] extractedChars={content.Length}, pages={pages.Count}, url='{chapterUrl}'");

        return content.Trim();
    }

    private static string ExtractTextFromContent(string html, string contentSelector)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);
        var node = doc.QuerySelector(contentSelector);
        if (node is null)
        {
            return string.Empty;
        }

        var innerHtml = node.InnerHtml
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase);

        var textDoc = parser.ParseDocument($"<div>{innerHtml}</div>");
        var text = textDoc.Body?.TextContent ?? string.Empty;
        text = Regex.Replace(text, "\\n{3,}", "\n\n");
        return text.Trim();
    }

    private static async Task<string> ExportTxtAsync(
        AppSettings settings,
        SearchResult book,
        IReadOnlyList<(string Title, string Content)> chapters,
        CancellationToken cancellationToken)
    {
        var workDir = WorkDirectoryManager.NormalizeAndMigrateWorkDirectory(settings.DownloadPath);
        var downloadPath = WorkDirectoryManager.GetDownloadsDirectory(workDir);
        Directory.CreateDirectory(downloadPath);

        var safeName = SanitizeFileName($"{book.Title}({book.Author}).txt");
        var outputPath = Path.Combine(downloadPath, safeName);

        await using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
        await writer.WriteLineAsync($"书名：{book.Title}");
        await writer.WriteLineAsync($"作者：{book.Author}");
        await writer.WriteLineAsync($"书源：{book.SourceId}");
        await writer.WriteLineAsync();

        foreach (var chapter in chapters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await writer.WriteLineAsync(chapter.Title);
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(chapter.Content);
            await writer.WriteLineAsync();
        }

        return outputPath;
    }

    private static string BuildTocUrl(RuleFileDto rule, string bookUrl)
    {
        var toc = rule.Toc;
        if (toc is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(toc.Url) && toc.Url.Contains("%s", StringComparison.Ordinal))
        {
            var path = Uri.TryCreate(bookUrl, UriKind.Absolute, out var uri)
                ? uri.AbsolutePath
                : bookUrl;

            var idMatch = Regex.Match(path, "(\\d+)(?!.*\\d)");
            if (idMatch.Success)
            {
                return toc.Url.Replace("%s", idMatch.Groups[1].Value, StringComparison.Ordinal);
            }
        }

        return bookUrl;
    }

    private static List<string> ExtractNextPageUrls(string html, string pageUrl, string selectorRaw, int maxPages)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);
        var selector = RuleFileLoader.NormalizeSelector(selectorRaw);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return [];
        }

        var result = new List<string>();
        foreach (var node in doc.QuerySelectorAll(selector))
        {
            if (result.Count >= maxPages)
            {
                break;
            }

            var href = node.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                href = node.GetAttribute("value");
            }

            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var url = RuleFileLoader.ResolveUrl(pageUrl, href);
            if (string.IsNullOrWhiteSpace(url) || result.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(url);
        }

        return result;
    }

    private async Task<string> FetchHtmlByUrlAsync(string url, CancellationToken cancellationToken, Action<string>? trace = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendWithRetryAsync(request, cancellationToken, trace);
        if (!response.IsSuccessStatusCode)
        {
            trace?.Invoke($"[http] GET {url} -> {(int)response.StatusCode} {response.StatusCode}");
            return string.Empty;
        }

        trace?.Invoke($"[http] GET {url} -> {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>轻量 HTTP GET，不重试，8 秒超时，用于检查新章节等对速度敏感的场景。</summary>
    private async Task<string> FetchHtmlOnceAsync(string url, CancellationToken cancellationToken, Action<string>? trace = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            trace?.Invoke($"[http-quick] GET {url}");
            using var response = await _httpClient.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                trace?.Invoke($"[http-quick] {url} -> {(int)response.StatusCode}");
                return string.Empty;
            }
            return await response.Content.ReadAsStringAsync(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            trace?.Invoke($"[http-quick] {url} -> {ex.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var ch in fileName)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }

    /// <summary>Rate-limit 相关 HTTP 状态码，遇到时使用较长的退避延迟。</summary>
    private static bool IsRateLimitOrBlock(int statusCode)
        => statusCode is 429 or 444 or 403 or 503;

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        Action<string>? trace = null)
    {
        const int maxAttempts = 5;
        var delay = TimeSpan.FromSeconds(3); // 起步 3 秒，防止被限流

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var clone = await RuleHttpHelper.CloneRequestAsync(request, cancellationToken);
                trace?.Invoke($"[http-attempt] method={clone.Method}, url={clone.RequestUri}, attempt={attempt}/{maxAttempts}");
                var response = await _httpClient.SendAsync(clone, cancellationToken);
                var code = (int)response.StatusCode;

                // 5xx 或限流/封禁状态码 → 重试
                if ((code >= 500 || IsRateLimitOrBlock(code)) && attempt < maxAttempts)
                {
                    var retryDelay = IsRateLimitOrBlock(code)
                        ? TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30))  // 限流退避更激进
                        : delay;
                    trace?.Invoke($"[http-retry] status={code}, waitMs={retryDelay.TotalMilliseconds:F0}, attempt={attempt}");
                    response.Dispose();
                    await Task.Delay(retryDelay, cancellationToken);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
                    continue;
                }

                return response;
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                trace?.Invoke($"[http-retry] HttpRequestException, waitMs={delay.TotalMilliseconds:F0}, attempt={attempt}");
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
            catch (TaskCanceledException) when (attempt < maxAttempts)
            {
                trace?.Invoke($"[http-retry] TaskCanceledException, waitMs={delay.TotalMilliseconds:F0}, attempt={attempt}");
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        var fallback = await RuleHttpHelper.CloneRequestAsync(request, cancellationToken);
        trace?.Invoke($"[http-fallback] method={fallback.Method}, url={fallback.RequestUri}");
        return await _httpClient.SendAsync(fallback, cancellationToken);
    }

    private static string BuildErrorWithDiagnostics(string message, IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return message;
        }

        var tail = string.Join(Environment.NewLine, diagnostics.TakeLast(MaxErrorTraceLines));
        return $"{message}{Environment.NewLine}[诊断片段]{Environment.NewLine}{tail}";
    }

    public static string GetLogFilePath()
    {
        var workDir = WorkDirectoryManager.GetCurrentWorkDirectoryFromSettings();
        var logDir = WorkDirectoryManager.GetLogsDirectory(workDir);
        Directory.CreateDirectory(logDir);
        return Path.Combine(logDir, "readstorm-download.log");
    }

    private static void AppendDiagnosticLog(string line)
    {
        try
        {
            lock (LogFileLock)
            {
                File.AppendAllText(GetLogFilePath(), line + Environment.NewLine, Encoding.UTF8);
            }

            Console.WriteLine(line);
        }
        catch
        {
            // 避免日志写入异常影响主流程。
        }
    }

    private sealed record ChapterRef(int Order, string Title, string Url);

    private static DownloadErrorKind Classify(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => DownloadErrorKind.Network,
            JsonException => DownloadErrorKind.Rule,
            RegexParseException => DownloadErrorKind.Rule,
            IOException => DownloadErrorKind.IO,
            InvalidOperationException => DownloadErrorKind.Parse,
            OperationCanceledException => DownloadErrorKind.Cancelled,
            _ => DownloadErrorKind.Unknown,
        };
    }

    /// <inheritdoc />
    public async Task<(bool Success, string Content, string Message)> FetchChapterFromSourceAsync(
        BookEntity book,
        string chapterTitle,
        int sourceId,
        CancellationToken cancellationToken = default)
    {
        // 1. 加载目标书源规则
        var rule = await LoadRuleAsync(sourceId, cancellationToken);
        if (rule is null)
            return (false, string.Empty, $"书源 {sourceId} 的规则文件不存在");

        if (_searchUseCase is null)
            return (false, string.Empty, "换源功能不可用（未注入搜索服务）");

        // 2. 使用搜索用例查找同名书
        var searchResults = await _searchUseCase.ExecuteAsync(book.Title, sourceId, cancellationToken);
        if (searchResults.Count == 0)
            return (false, string.Empty, $"书源 {sourceId} 未搜到《{book.Title}》");

        // 尝试匹配同名同作者
        var matched = searchResults.FirstOrDefault(r =>
            string.Equals(r.Title?.Trim(), book.Title?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Author?.Trim(), book.Author?.Trim(), StringComparison.OrdinalIgnoreCase));
        matched ??= searchResults.FirstOrDefault(r =>
            string.Equals(r.Title?.Trim(), book.Title?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (matched is null)
            return (false, string.Empty, $"书源 {sourceId} 未找到《{book.Title}》（搜到 {searchResults.Count} 本但均不匹配）");

        // 3. 抓取目录
        var tocChapters = await FetchTocAsync(rule, matched, DownloadMode.FullBook, cancellationToken);
        if (tocChapters.Count == 0)
            return (false, string.Empty, $"书源 {sourceId} 目录为空");

        // 4. 按标题模糊匹配章节
        var normalizedTarget = NormalizeChapterTitle(chapterTitle);
        var bestMatch = tocChapters
            .Select((ch, i) => (Chapter: ch, Index: i, Score: ChapterTitleSimilarity(NormalizeChapterTitle(ch.Title), normalizedTarget)))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        if (bestMatch.Score < 0.5)
            return (false, string.Empty, $"书源 {sourceId} 未找到匹配章节「{chapterTitle}」(最佳匹配：{bestMatch.Chapter?.Title}, score={bestMatch.Score:F2})");

        // 5. 下载章节内容
        var content = await FetchChapterContentAsync(rule, bestMatch.Chapter.Url, cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return (false, string.Empty, $"书源 {sourceId} 章节内容为空");

        return (true, content, $"✅ 已从书源 {sourceId} 获取「{bestMatch.Chapter.Title}」({content.Length} 字)");
    }

    /// <summary>标准化章节标题（去除空格、标点）用于模糊匹配。</summary>
    private static string NormalizeChapterTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        // 移除常见前缀标记和空白
        return Regex.Replace(title.Trim(), @"[\s　\u3000·・\-—]+", string.Empty);
    }

    /// <summary>简易章节标题相似度（0~1）。</summary>
    private static double ChapterTitleSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        if (a == b) return 1.0;
        if (a.Contains(b) || b.Contains(a)) return 0.9;

        // LCS ratio
        var lcs = LongestCommonSubsequence(a, b);
        return (double)lcs / Math.Max(a.Length, b.Length);
    }

    private static int LongestCommonSubsequence(string a, string b)
    {
        var m = a.Length;
        var n = b.Length;
        var prev = new int[n + 1];
        var curr = new int[n + 1];
        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                curr[j] = a[i - 1] == b[j - 1]
                    ? prev[j - 1] + 1
                    : Math.Max(prev[j], curr[j - 1]);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[n];
    }
}
