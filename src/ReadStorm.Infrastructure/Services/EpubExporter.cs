using System.IO.Compression;
using System.Text;

namespace ReadStorm.Infrastructure.Services;

/// <summary>轻量 EPUB 3 生成器，无外部依赖。</summary>
public static class EpubExporter
{
    public static async Task<string> ExportAsync(
        string workDirectory,
        string title,
        string author,
        int sourceId,
        IReadOnlyList<(string Title, string Content)> chapters,
        CancellationToken cancellationToken = default)
    {
        var workDir = WorkDirectoryManager.NormalizeAndMigrateWorkDirectory(workDirectory);
        var downloadPath = WorkDirectoryManager.GetDownloadsDirectory(workDir);
        Directory.CreateDirectory(downloadPath);

        var safeName = SanitizeFileName($"{title}({author}).epub");
        var outputPath = Path.Combine(downloadPath, safeName);

        await using var fileStream = File.Create(outputPath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

        // mimetype (must be first, uncompressed)
        var mimetypeEntry = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        await using (var writer = new StreamWriter(mimetypeEntry.Open(), Encoding.ASCII))
        {
            await writer.WriteAsync("application/epub+zip");
        }

        // META-INF/container.xml
        var containerEntry = archive.CreateEntry("META-INF/container.xml");
        await using (var writer = new StreamWriter(containerEntry.Open(), Encoding.UTF8))
        {
            await writer.WriteAsync(ContainerXml);
        }

        // OEBPS/content.opf
        var opfContent = BuildContentOpf(title, author, chapters);
        var opfEntry = archive.CreateEntry("OEBPS/content.opf");
        await using (var writer = new StreamWriter(opfEntry.Open(), Encoding.UTF8))
        {
            await writer.WriteAsync(opfContent);
        }

        // OEBPS/toc.ncx
        var ncxContent = BuildTocNcx(title, chapters);
        var ncxEntry = archive.CreateEntry("OEBPS/toc.ncx");
        await using (var writer = new StreamWriter(ncxEntry.Open(), Encoding.UTF8))
        {
            await writer.WriteAsync(ncxContent);
        }

        // OEBPS/toc.xhtml (nav)
        var navContent = BuildNavXhtml(chapters);
        var navEntry = archive.CreateEntry("OEBPS/toc.xhtml");
        await using (var writer = new StreamWriter(navEntry.Open(), Encoding.UTF8))
        {
            await writer.WriteAsync(navContent);
        }

        // OEBPS/chapter-N.xhtml
        for (var i = 0; i < chapters.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapter = chapters[i];
            var chapterHtml = BuildChapterXhtml(chapter.Title, chapter.Content);
            var chapterEntry = archive.CreateEntry($"OEBPS/chapter-{i + 1}.xhtml");
            await using var writer = new StreamWriter(chapterEntry.Open(), Encoding.UTF8);
            await writer.WriteAsync(chapterHtml);
        }

        return outputPath;
    }

    private const string ContainerXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles>
            <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
          </rootfiles>
        </container>
        """;

    private static string BuildContentOpf(string title, string author, IReadOnlyList<(string Title, string Content)> chapters)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="BookId" version="3.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            """);
        sb.AppendLine($"    <dc:identifier id=\"BookId\">urn:uuid:{Guid.NewGuid()}</dc:identifier>");
        sb.AppendLine($"    <dc:title>{EscapeXml(title)}</dc:title>");
        sb.AppendLine($"    <dc:creator>{EscapeXml(author)}</dc:creator>");
        sb.AppendLine($"    <dc:language>zh</dc:language>");
        sb.AppendLine($"    <meta property=\"dcterms:modified\">{DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>");
        sb.AppendLine("  </metadata>");
        sb.AppendLine("  <manifest>");
        sb.AppendLine("    <item id=\"ncx\" href=\"toc.ncx\" media-type=\"application/x-dtbncx+xml\"/>");
        sb.AppendLine("    <item id=\"nav\" href=\"toc.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");

        for (var i = 0; i < chapters.Count; i++)
        {
            sb.AppendLine($"    <item id=\"chapter-{i + 1}\" href=\"chapter-{i + 1}.xhtml\" media-type=\"application/xhtml+xml\"/>");
        }

        sb.AppendLine("  </manifest>");
        sb.AppendLine("  <spine toc=\"ncx\">");
        for (var i = 0; i < chapters.Count; i++)
        {
            sb.AppendLine($"    <itemref idref=\"chapter-{i + 1}\"/>");
        }

        sb.AppendLine("  </spine>");
        sb.AppendLine("</package>");
        return sb.ToString();
    }

    private static string BuildTocNcx(string title, IReadOnlyList<(string Title, string Content)> chapters)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            <?xml version="1.0" encoding="UTF-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
              <head>
                <meta name="dtb:depth" content="1"/>
              </head>
            """);
        sb.AppendLine($"  <docTitle><text>{EscapeXml(title)}</text></docTitle>");
        sb.AppendLine("  <navMap>");
        for (var i = 0; i < chapters.Count; i++)
        {
            sb.AppendLine($"    <navPoint id=\"navPoint-{i + 1}\" playOrder=\"{i + 1}\">");
            sb.AppendLine($"      <navLabel><text>{EscapeXml(chapters[i].Title)}</text></navLabel>");
            sb.AppendLine($"      <content src=\"chapter-{i + 1}.xhtml\"/>");
            sb.AppendLine("    </navPoint>");
        }

        sb.AppendLine("  </navMap>");
        sb.AppendLine("</ncx>");
        return sb.ToString();
    }

    private static string BuildNavXhtml(IReadOnlyList<(string Title, string Content)> chapters)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
            <head><title>目录</title></head>
            <body>
              <nav epub:type="toc">
                <h1>目录</h1>
                <ol>
            """);
        for (var i = 0; i < chapters.Count; i++)
        {
            sb.AppendLine($"      <li><a href=\"chapter-{i + 1}.xhtml\">{EscapeXml(chapters[i].Title)}</a></li>");
        }

        sb.AppendLine("    </ol>");
        sb.AppendLine("  </nav>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string BuildChapterXhtml(string title, string content)
    {
        var paragraphs = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        sb.AppendLine("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE html>
            <html xmlns="http://www.w3.org/1999/xhtml">
            <head>
            """);
        sb.AppendLine($"  <title>{EscapeXml(title)}</title>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"  <h2>{EscapeXml(title)}</h2>");
        foreach (var p in paragraphs)
        {
            var trimmed = p.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                sb.AppendLine($"  <p>{EscapeXml(trimmed)}</p>");
            }
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
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
}
