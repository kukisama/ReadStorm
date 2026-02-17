using System.Runtime.InteropServices;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 提供跨平台安全的文件路径比较策略。
/// Windows / macOS (HFS+) 文件系统不区分大小写 → OrdinalIgnoreCase；
/// Linux 区分大小写 → Ordinal。
/// </summary>
internal static class PlatformPathComparer
{
    /// <summary>适用于文件路径比较的 <see cref="StringComparison"/>。</summary>
    public static StringComparison PathComparison { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    /// <summary>适用于文件路径去重 / 字典键的 <see cref="StringComparer"/>。</summary>
    public static StringComparer PathComparer { get; } =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
}
