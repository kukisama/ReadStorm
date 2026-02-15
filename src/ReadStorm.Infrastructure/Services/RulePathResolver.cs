namespace ReadStorm.Infrastructure.Services;

internal static class RulePathResolver
{
    public static IReadOnlyList<string> ResolveDefaultRuleDirectories()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddIfExists(candidates, Path.Combine(AppContext.BaseDirectory, "rules"));
        AddIfExists(candidates, Path.Combine(Directory.GetCurrentDirectory(), "rules"));

        foreach (var parent in EnumerateParents(AppContext.BaseDirectory)
                     .Concat(EnumerateParents(Directory.GetCurrentDirectory()))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddIfExists(candidates, Path.Combine(parent, "src", "ReadStorm.Rules", "rules"));
            AddIfExists(candidates, Path.Combine(parent, "参考文档", "novel-main", "novel-main", "src", "main", "resources", "rule"));
        }

        return candidates.ToList();
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

    private static void AddIfExists(ISet<string> set, string path)
    {
        if (Directory.Exists(path))
        {
            set.Add(path);
        }
    }
}
