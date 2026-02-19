using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;

namespace ReadStorm.Android.ViewModels;

/// <summary>
/// Android 专用实时日志 ViewModel。
/// 实现 <see cref="ILiveDiagnosticSink"/>，所有基础设施层的诊断日志都会推送到此处。
/// </summary>
/// <remarks>
/// 性能优化策略：
/// <list type="bullet">
///   <item>仅保留尾部 <see cref="TailLines"/> 行，避免 TextBlock 渲染整本书的日志</item>
///   <item>200ms 节流批量刷新，避免高频更新阻塞 UI 线程</item>
///   <item>入队 → 定时器合并 → 一次性刷 UI，减少 PropertyChanged 触发次数</item>
/// </list>
/// </remarks>
public sealed partial class LogViewModel : ObservableObject, ILiveDiagnosticSink
{
    /// <summary>屏幕可见保留行数（约一屏多一点）。</summary>
    private const int TailLines = 200;

    /// <summary>UI 刷新节流间隔（毫秒）。</summary>
    private const int ThrottleMs = 200;

    private readonly object _lock = new();
    private readonly Queue<string> _pending = new();
    private readonly LinkedList<string> _tail = new();
    private bool _timerScheduled;

    [ObservableProperty]
    private string logText = string.Empty;

    public void Append(string line)
    {
        lock (_lock)
        {
            _pending.Enqueue(line);
            if (!_timerScheduled)
            {
                _timerScheduled = true;
                Dispatcher.UIThread.Post(FlushPending, DispatcherPriority.Background);
            }
        }
    }

    /// <summary>
    /// 将待处理队列合并到尾部缓冲，截断到 <see cref="TailLines"/> 行后一次性更新 UI。
    /// </summary>
    private void FlushPending()
    {
        // 延迟一小段时间，让更多日志累积
        Task.Delay(ThrottleMs).ContinueWith(_ =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                string[] batch;
                lock (_lock)
                {
                    batch = [.. _pending];
                    _pending.Clear();
                    _timerScheduled = false;
                }

                if (batch.Length == 0) return;

                foreach (var line in batch)
                {
                    _tail.AddLast(line);
                }

                // 只保留尾部 N 行
                while (_tail.Count > TailLines)
                {
                    _tail.RemoveFirst();
                }

                // 一次性拼接并更新
                var sb = new StringBuilder(_tail.Count * 120);
                foreach (var l in _tail)
                {
                    sb.AppendLine(l);
                }

                LogText = sb.ToString();
            });
        });
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pending.Clear();
        }

        Dispatcher.UIThread.Post(() =>
        {
            _tail.Clear();
            LogText = string.Empty;
        });
    }

    [RelayCommand]
    private void ClearLog() => Clear();
}
