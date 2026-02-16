namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 规则文件路径解析器。
/// 采用分层策略：用户目录（可写） > 内置默认目录（只读）。
/// </summary>
internal static class RulePathResolver
{
    private static readonly string UserRulesDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ReadStorm", "rules");

    /// <summary>用户数据目录，用于保存用户修改过的规则。始终存在（自动创建）。</summary>
    public static string GetUserRulesDirectory()
    {
        Directory.CreateDirectory(UserRulesDir);
        return UserRulesDir;
    }

    /// <summary>
    /// 返回内置（只读）规则目录列表，用作默认值来源。
    /// 包括 bin/rules、ReadStorm.Rules/rules、参考文档等。
    /// </summary>
    public static IReadOnlyList<string> ResolveBuiltinRuleDirectories()
    {
        var candidates = new List<string>();

        TryAdd(candidates, Path.Combine(AppContext.BaseDirectory, "rules"));
        TryAdd(candidates, Path.Combine(Directory.GetCurrentDirectory(), "rules"));

        foreach (var parent in EnumerateParents(AppContext.BaseDirectory)
                     .Concat(EnumerateParents(Directory.GetCurrentDirectory()))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            TryAdd(candidates, Path.Combine(parent, "src", "ReadStorm.Rules", "rules"));
            TryAdd(candidates, Path.Combine(parent, "参考文档", "novel-main", "novel-main", "src", "main", "resources", "rule"));
        }

        return candidates;
    }

    /// <summary>
    /// 返回所有规则目录，用户目录在最前面（优先级最高）。
    /// </summary>
    public static IReadOnlyList<string> ResolveAllRuleDirectories()
    {
        var all = new List<string> { GetUserRulesDirectory() };
        all.AddRange(ResolveBuiltinRuleDirectories());
        return all;
    }

    private static IEnumerable<string> EnumerateParents(string startPath)
    {
        var full = Path.GetFullPath(startPath);
        var current = new DirectoryInfo(full);

        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }

    private static void TryAdd(List<string> list, string path)
    {
        if (Directory.Exists(path)
            && !list.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(path);
        }
    }
}
