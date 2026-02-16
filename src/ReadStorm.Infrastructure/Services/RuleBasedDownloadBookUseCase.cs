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
    private const int CoverTimeoutSeconds = 6;

    private static readonly Lock LogFileLock = new();
    private static readonly string CoverLogPath = Path.Combine(AppContext.BaseDirectory, "logs", "cover.log");

    private readonly IAppSettingsUseCase _settingsUseCase;
    private readonly IBookRepository _bookRepo;
    private readonly ISearchBooksUseCase? _searchUseCase;
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<string> _ruleDirectories;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public RuleBasedDownloadBookUseCase(
        IAppSettingsUseCase settingsUseCase,
        IBookRepository bookRepo,
        ISearchBooksUseCase? searchUseCase = null,
        HttpClient? httpClient = null,
        IReadOnlyList<string>? ruleDirectories = null)
    {
        _settingsUseCase = settingsUseCase;
        _bookRepo = bookRepo;
        _searchUseCase = searchUseCase;
        _httpClient = httpClient ?? CreateHttpClient();
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
                    var allCandidates = ExtractCoverCandidates(tocHtml, selectedBook.Url).ToList();

                    // 把起点候选也加进来，这样原站全部失败后会自动尝试起点
                    var qidianCandidates = await GetQidianSearchCoverCandidatesAsync(bookEntity.Title, cancellationToken);
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
                        var referer = GetCoverReferer(selectedBook.Url, coverCandidate);
                        var (coverBytes, coverExt, failReason) = await DownloadCoverBytesAsync(coverCandidate.ImageUrl, referer, cancellationToken);
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
                        var localPath = SaveCoverToLocal(bookEntity, coverBytes, coverExt);
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

    private async Task<RuleFileDto?> LoadRuleAsync(int sourceId, CancellationToken cancellationToken)
    {
        var filePath = _ruleDirectories
            .Select(dir => Path.Combine(dir, $"rule-{sourceId}.json"))
            .FirstOrDefault(File.Exists);

        if (filePath is null)
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<RuleFileDto>(stream, JsonOptions, cancellationToken);
    }

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

        var itemSelector = NormalizeSelector(itemSelectorRaw);
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
            var url = ResolveUrl(pageUrl, href);
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

        var contentSelector = NormalizeSelector(chapter.Content);
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
        var downloadPath = settings.DownloadPath;
        if (!Path.IsPathRooted(downloadPath))
        {
            downloadPath = Path.Combine(AppContext.BaseDirectory, downloadPath);
        }

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
        var selector = NormalizeSelector(selectorRaw);
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

            var url = ResolveUrl(pageUrl, href);
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

    private static string NormalizeSelector(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return string.Empty;
        }

        var idx = selector.IndexOf("@js:", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            selector = selector[..idx];
        }

        return selector.Trim();
    }

    private static string ResolveUrl(string baseUrl, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            && Uri.TryCreate(baseUri, url, out var merged))
        {
            return merged.ToString();
        }

        return string.Empty;
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

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        return client;
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
                var clone = await CloneRequestAsync(request, cancellationToken);
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

        var fallback = await CloneRequestAsync(request, cancellationToken);
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
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
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

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

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

    private sealed record ChapterRef(int Order, string Title, string Url);

    private sealed class RuleFileDto
    {
        public int Id { get; set; }

        public string? Url { get; set; }

        public RuleTocDto? Toc { get; set; }

        public RuleChapterDto? Chapter { get; set; }
    }

    private sealed class RuleTocDto
    {
        public string? Url { get; set; }

        public string? Item { get; set; }

        public bool Pagination { get; set; }

        public string? NextPage { get; set; }

        public int? Offset { get; set; }

        public bool Desc { get; set; }
    }

    private sealed class RuleChapterDto
    {
        public string? Content { get; set; }

        public bool Pagination { get; set; }

        public string? NextPage { get; set; }

        public string? FilterTxt { get; set; }
    }

    // ==================== 封面提取 ====================

    private static void CoverLog(string message)
    {
        try
        {
            var dir = Path.GetDirectoryName(CoverLogPath)!;
            Directory.CreateDirectory(dir);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (LogFileLock)
            {
                File.AppendAllText(CoverLogPath, line);
            }
        }
        catch
        {
            // 日志写入失败不影响主流程
        }
    }

    /// <summary>封面专用的快速 HTTP GET，短超时、不重试。</summary>
    private async Task<HttpResponseMessage> SendCoverRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(CoverTimeoutSeconds));
        return await _httpClient.SendAsync(request, cts.Token);
    }

    /// <summary>封面专用的快速获取 HTML，短超时、不重试。</summary>
    private async Task<string> FetchCoverHtmlAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendCoverRequestAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            CoverLog($"[fetch] GET {url} -> {(int)response.StatusCode}");
            return string.Empty;
        }
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <inheritdoc />
    public async Task<string> RefreshCoverAsync(BookEntity book, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.Append($"[封面诊断] 《{book.Title}》");
        CoverLog($"========== 开始刷新封面: 《{book.Title}》 TocUrl={book.TocUrl} ==========");

        var candidates = await GetCoverCandidatesAsync(book, cancellationToken);
        sb.Append($"  候选数量:{candidates.Count}");
        CoverLog($"候选数量: {candidates.Count}");
        if (candidates.Count == 0)
        {
            sb.Append("  ❌ 未找到可用候选图片");
            CoverLog("❌ 未找到可用候选图片");
            return sb.ToString();
        }

        foreach (var candidate in candidates)
        {
            CoverLog($"尝试候选 [{candidate.Index}] {candidate.Rule}: {candidate.ImageUrl}");
            var referer = GetCoverReferer(book.TocUrl, candidate);
            var (bytes, extension, failReason) = await DownloadCoverBytesAsync(candidate.ImageUrl, referer, cancellationToken);
            if (bytes.Length == 0)
            {
                sb.Append($"  候选失败:[{candidate.Index}]{candidate.Rule}—{failReason}");
                CoverLog($"候选失败 [{candidate.Index}] {candidate.Rule}: {failReason}");
                continue;
            }

            book.CoverUrl = candidate.ImageUrl;
            book.CoverBlob = bytes;
            book.CoverImage = string.Empty;
            book.CoverRule = $"auto:{candidate.Rule}|{candidate.HtmlSnippet}";
            await _bookRepo.UpsertBookAsync(book, cancellationToken);
            SaveCoverToLocal(book, bytes, extension);

            sb.Append($"  ✅ [{candidate.Index}]{candidate.Rule} blob={bytes.Length}B");
            CoverLog($"✅ 成功 [{candidate.Index}] {candidate.Rule} blob={bytes.Length}B url={candidate.ImageUrl}");
            return sb.ToString();
        }

        sb.Append("  ❌ 所有候选下载失败");
        CoverLog("❌ 所有候选下载失败");
        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CoverCandidate>> GetCoverCandidatesAsync(BookEntity book, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(book.TocUrl)) return [];
        var html = await FetchCoverHtmlAsync(book.TocUrl, cancellationToken);
        CoverLog($"TocUrl HTML长度={html.Length} url={book.TocUrl}");

        var merged = ExtractCoverCandidates(html, book.TocUrl).ToList();
        var qidianFallbacks = await GetQidianSearchCoverCandidatesAsync(book.Title, cancellationToken);

        foreach (var candidate in qidianFallbacks)
        {
            if (merged.Any(x => string.Equals(x.ImageUrl, candidate.ImageUrl, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            candidate.Index = merged.Count + 1;
            merged.Add(candidate);
        }

        return merged;
    }

    /// <inheritdoc />
    public async Task<string> ApplyCoverCandidateAsync(BookEntity book, CoverCandidate candidate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(candidate.ImageUrl))
        {
            return "❌ 候选图片URL为空。";
        }

        var referer = GetCoverReferer(book.TocUrl, candidate);
        var (bytes, extension, failReason) = await DownloadCoverBytesAsync(candidate.ImageUrl, referer, cancellationToken);
        if (bytes.Length == 0)
        {
            return $"❌ 下载封面失败：{failReason}";
        }

        book.CoverUrl = candidate.ImageUrl;
        book.CoverBlob = bytes;
        book.CoverImage = string.Empty;
        book.CoverRule = $"manual:{candidate.Rule}|{candidate.HtmlSnippet}";
        await _bookRepo.UpsertBookAsync(book, cancellationToken);
        var localPath = SaveCoverToLocal(book, bytes, extension);
        return $"✅ 已设置封面，blobLen={bytes.Length}，本地文件：{localPath}";
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

    private static IReadOnlyList<CoverCandidate> ExtractCoverCandidates(string html, string pageUrl)
    {
        var result = new List<CoverCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parser = new HtmlParser();
            using var doc = parser.ParseDocument(html);

            void AddCandidate(string? rawUrl, string rule, string htmlSnippet)
            {
                if (string.IsNullOrWhiteSpace(rawUrl)) return;
                var abs = ResolveUrl(pageUrl, rawUrl);
                if (string.IsNullOrWhiteSpace(abs)) return;
                if (!seen.Add(abs)) return;

                result.Add(new CoverCandidate
                {
                    Index = result.Count + 1,
                    ImageUrl = abs,
                    Rule = rule,
                    HtmlSnippet = htmlSnippet.Length > 1000 ? htmlSnippet[..1000] : htmlSnippet,
                });
            }

            var metaOg = doc.QuerySelector("meta[property='og:image']");
            AddCandidate(metaOg?.GetAttribute("content"), "meta[property='og:image']", metaOg?.OuterHtml ?? string.Empty);

            var metaOgName = doc.QuerySelector("meta[name='og:image']");
            AddCandidate(metaOgName?.GetAttribute("content"), "meta[name='og:image']", metaOgName?.OuterHtml ?? string.Empty);

            foreach (var img in doc.QuerySelectorAll("img"))
            {
                var src = img.GetAttribute("src")
                       ?? img.GetAttribute("data-src")
                       ?? img.GetAttribute("data-original");

                if (string.IsNullOrWhiteSpace(src)) continue;

                var lower = src.ToLowerInvariant();
                if (lower.Contains(".jpg") || lower.Contains(".jpeg") || lower.Contains(".png") || lower.Contains(".webp") || lower.Contains("cover") || lower.Contains("book"))
                {
                    AddCandidate(src, "img[src/data-src/data-original]", img.OuterHtml);
                }
            }

            // 回退：如果上面没有命中，取页面前 20 个 img 的首批
            if (result.Count == 0)
            {
                foreach (var img in doc.QuerySelectorAll("img").Take(20))
                {
                    var src = img.GetAttribute("src")
                           ?? img.GetAttribute("data-src")
                           ?? img.GetAttribute("data-original");
                    AddCandidate(src, "img:first-batch", img.OuterHtml);
                    if (result.Count >= 12) break;
                }
            }
        }
        catch
        {
            // ignore parse errors
        }

        return result;
    }

    private async Task<IReadOnlyList<CoverCandidate>> GetQidianSearchCoverCandidatesAsync(string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title)) return [];

        try
        {
            var encoded = Uri.EscapeDataString(title.Trim());

            // 使用起点移动端搜索，反爬比 PC 端宽松
            var searchUrl = $"https://m.qidian.com/soushu/{encoded}.html";

            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            // 移动端 UA
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            request.Headers.TryAddWithoutValidation("Referer", "https://m.qidian.com/");

            using var response = await SendCoverRequestAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                CoverLog($"[qidian] 搜索页 HTTP {(int)response.StatusCode}: {searchUrl}");
                return [];
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                CoverLog($"[qidian] 搜索页返回空 HTML: {searchUrl}");
                return [];
            }

            CoverLog($"[qidian] 搜索页 HTML 长度={html.Length}, url={searchUrl}");
            if (html.Length < 1000)
            {
                // 短响应通常是反爬验证页，记录内容便于诊断
                CoverLog($"[qidian] 短响应内容: {html[..Math.Min(html.Length, 500)]}");
            }

            var candidates = new List<CoverCandidate>();

            // 策略1: AngleSharp解析 img 标签
            var parser = new HtmlParser();
            using var doc = parser.ParseDocument(html);

            // 遍历所有 img，找封面 CDN 图片
            foreach (var img in doc.QuerySelectorAll("img"))
            {
                var raw = img.GetAttribute("src")
                          ?? img.GetAttribute("data-src")
                          ?? img.GetAttribute("data-original");
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var abs = ResolveUrl(searchUrl, raw);
                if (IsLikelyCoverUrl(abs))
                {
                    candidates.Add(new CoverCandidate
                    {
                        Index = candidates.Count + 1,
                        ImageUrl = abs!,
                        Rule = "qidian:search:img",
                        HtmlSnippet = searchUrl,
                    });
                    break; // 只取第一个
                }
            }

            // 策略2: 正则从 HTML 源码中提取封面CDN链接（起点搜索页可能由JS渲染，img标签可能不在初始HTML中）
            if (candidates.Count == 0)
            {
                var regex = new Regex(
                    @"https?://bookcover\.yuewen\.com/qdbimg/[^\""""'\s<>]+",
                    RegexOptions.IgnoreCase);
                var matches = regex.Matches(html);
                CoverLog($"[qidian] 正则匹配 bookcover.yuewen.com 结果数: {matches.Count}");

                foreach (Match m in matches)
                {
                    var abs = ResolveUrl(searchUrl, m.Value);
                    if (IsLikelyCoverUrl(abs))
                    {
                        candidates.Add(new CoverCandidate
                        {
                            Index = candidates.Count + 1,
                            ImageUrl = abs!,
                            Rule = "qidian:search:regex-cdn",
                            HtmlSnippet = searchUrl,
                        });
                        break; // 只取第一个
                    }
                }
            }

            // 策略3: 尝试从搜索结果获取 bookId，然后构建常见尺寸封面URL
            if (candidates.Count == 0)
            {
                var bookIdMatch = Regex.Match(html, @"/book/(\d{5,15})", RegexOptions.IgnoreCase);
                if (bookIdMatch.Success)
                {
                    var bookId = bookIdMatch.Groups[1].Value;
                    CoverLog($"[qidian] 提取到 bookId={bookId}");

                    // 起点封面CDN常见模式：通过搜索结果 bookId 构造不同尺寸的候选
                    var cdnPatterns = new[]
                    {
                        $"https://bookcover.yuewen.com/qdbimg/349573/{bookId}/300.webp",
                        $"https://bookcover.yuewen.com/qdbimg/349573/{bookId}/150.webp",
                        $"https://bookcover.yuewen.com/qdbimg/349573/{bookId}/180.webp",
                    };
                    foreach (var url in cdnPatterns)
                    {
                        candidates.Add(new CoverCandidate
                        {
                            Index = candidates.Count + 1,
                            ImageUrl = url,
                            Rule = "qidian:search:bookid-cdn",
                            HtmlSnippet = searchUrl,
                        });
                        break; // 只取一个尺寸
                    }
                }
            }

            CoverLog($"[qidian] 最终候选数: {candidates.Count}");
            return candidates;
        }
        catch (OperationCanceledException)
        {
            CoverLog($"[qidian] 超时 ({CoverTimeoutSeconds}s)");
            return [];
        }
        catch (Exception ex)
        {
            CoverLog($"[qidian] 异常: {ex.Message}");
            return [];
        }
    }

    private static string GetCoverReferer(string tocUrl, CoverCandidate candidate)
    {
        if (candidate.Rule.StartsWith("qidian:", StringComparison.OrdinalIgnoreCase))
        {
            return candidate.HtmlSnippet;
        }

        return tocUrl;
    }

    /// <summary>下载封面图片原始字节（用于 BLOB 存储）并推断扩展名。只尝试一次，不做多 Referer 兜底。</summary>
    private async Task<(byte[] Bytes, string Extension, string FailReason)> DownloadCoverBytesAsync(string coverUrl, string? refererUrl, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, coverUrl);
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");

            if (Uri.TryCreate(refererUrl, UriKind.Absolute, out var refUri))
            {
                request.Headers.Referrer = refUri;
                request.Headers.TryAddWithoutValidation("Origin", $"{refUri.Scheme}://{refUri.Host}");
            }

            using var response = await SendCoverRequestAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return ([], ".jpg", $"HTTP {(int)response.StatusCode}");

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
                return ([], ".jpg", "响应体为空");
            if (bytes.Length > 500_000)
                return ([], ".jpg", $"文件过大 ({bytes.Length} 字节)");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!IsLikelyImagePayload(bytes, mediaType))
                return ([], ".jpg", $"非图片内容 (type={mediaType ?? "null"}, head={BitConverter.ToString(bytes[..Math.Min(4, bytes.Length)])})");

            var ext = GuessImageExtension(coverUrl, mediaType, bytes);
            CoverLog($"[download] 成功 {coverUrl} -> {bytes.Length}B {ext}");
            return (bytes, ext, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return ([], ".jpg", $"超时 ({CoverTimeoutSeconds}s)");
        }
        catch (Exception ex)
        {
            return ([], ".jpg", $"异常: {ex.Message}");
        }
    }


    private static string SaveCoverToLocal(BookEntity book, byte[] bytes, string extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
        var dir = Path.Combine(AppContext.BaseDirectory, "covers");
        Directory.CreateDirectory(dir);

        var shortId = string.IsNullOrWhiteSpace(book.Id)
            ? "book"
            : (book.Id.Length > 8 ? book.Id[..8] : book.Id);
        var fileName = $"{SanitizeFileName(book.Title)}-{shortId}{ext}";
        var filePath = Path.Combine(dir, fileName);
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }

    private static string GuessImageExtension(string coverUrl, string? mediaType, byte[]? bytes = null)
    {
        if (bytes is { Length: > 11 })
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return ".gif";
            if (bytes[0] == 0x42 && bytes[1] == 0x4D) return ".bmp";
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return ".webp";
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var mt = mediaType.ToLowerInvariant();
            if (mt.Contains("png")) return ".png";
            if (mt.Contains("webp")) return ".webp";
            if (mt.Contains("gif")) return ".gif";
            if (mt.Contains("bmp")) return ".bmp";
            if (mt.Contains("jpeg") || mt.Contains("jpg")) return ".jpg";
        }

        try
        {
            if (Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri))
            {
                var ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                if (ext is ".jpg" or ".jpeg") return ".jpg";
                if (ext is ".png" or ".webp" or ".gif" or ".bmp") return ext;
            }
        }
        catch
        {
            // ignore and fallback
        }

        return ".jpg";
    }

    private static bool IsLikelyImagePayload(byte[] bytes, string? mediaType)
    {
        if (bytes.Length < 4) return false;

        var isJpeg = bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        var isPng = bytes.Length > 7 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                                  && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
        var isGif = bytes.Length > 5 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46
                                  && bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61;
        var isBmp = bytes.Length > 1 && bytes[0] == 0x42 && bytes[1] == 0x4D;
        var isWebp = bytes.Length > 11
                     && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                     && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;

        if (isJpeg || isPng || isGif || isBmp || isWebp) return true;

        // 若 content-type 明确是 text/html，几乎可以判定为反盗链返回页。
        if (!string.IsNullOrWhiteSpace(mediaType)
            && mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return false;
    }

    private static bool IsLikelyCoverUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var lower = url.ToLowerInvariant();

        // 起点搜索页常见用户头像占位，不是书封面。
        if (lower.Contains("/images/user.") || lower.Contains("user.bcb60")) return false;

        return lower.Contains("bookcover")
               || lower.Contains("qdbimg")
               || lower.Contains("cover")
               || lower.EndsWith(".jpg")
               || lower.EndsWith(".jpeg")
               || lower.EndsWith(".png")
               || lower.EndsWith(".webp");
    }
}
