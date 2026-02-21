using System.Reflection;
using System.Text;

namespace ReadStorm.Infrastructure.Services;

public sealed record AboutInfo(string Version, string Content);

public static class AboutInfoProvider
{
    private static readonly Lazy<AboutInfo> Cached = new(LoadInternal);

    public static AboutInfo Get() => Cached.Value;

    private static AboutInfo LoadInternal()
    {
        try
        {
            var asm = typeof(AboutInfoProvider).Assembly;
            const string resourceName = "ReadStorm.Infrastructure.ReleaseNotes.md";

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null)
                return new AboutInfo("未知版本", "未找到 RELEASE_NOTES 内容。");

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var markdown = reader.ReadToEnd();

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            var builder = new StringBuilder();
            var version = "未知版本";

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();

                if (line.StartsWith("### 运行前提", StringComparison.Ordinal))
                    break;

                if (version == "未知版本"
                    && line.StartsWith("# ", StringComparison.Ordinal)
                    && line.Contains('v', StringComparison.OrdinalIgnoreCase))
                {
                    // 例如：# ReadStorm v1.1.0
                    var idx = line.IndexOf('v');
                    if (idx >= 0 && idx < line.Length - 1)
                        version = line[(idx + 1)..].Trim();
                }

                builder.AppendLine(raw);
            }

            var content = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(content))
                content = "暂无版本说明。";

            return new AboutInfo(version, content);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("AboutInfoProvider.Load", ex);
            return new AboutInfo("未知版本", "读取版本说明失败。\n" + ex.Message);
        }
    }
}
