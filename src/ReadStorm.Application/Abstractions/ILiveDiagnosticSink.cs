namespace ReadStorm.Application.Abstractions;

/// <summary>
/// 实时诊断日志推送接口。
/// Android 端实现为 UI 日志面板，桌面端可不注册（默认空操作）。
/// </summary>
public interface ILiveDiagnosticSink
{
    /// <summary>追加一行日志到 UI。</summary>
    void Append(string line);

    /// <summary>清空已有日志。</summary>
    void Clear();
}
