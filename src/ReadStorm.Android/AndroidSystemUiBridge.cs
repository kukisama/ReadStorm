using Android.App;
using Android.Views;
using Avalonia.Controls;
using Avalonia.Media;
using ReadStorm.Desktop.Views;

namespace ReadStorm.Android;

/// <summary>
/// Android 系统 UI 适配桥接：
/// 使用 Avalonia InsetsManager 实现边到边沉浸显示（刘海区域扩展），
/// 同时设置 Android 原生 LayoutInDisplayCutoutMode 确保系统层面也许可刘海内容。
/// </summary>
internal static class AndroidSystemUiBridge
{
    private static WeakReference<Activity>? _activityRef;
    private static WeakReference<MainView>? _mainViewRef;

    public static void RegisterActivity(Activity activity)
    {
        _activityRef = new WeakReference<Activity>(activity);
    }

    public static void RegisterMainView(MainView mainView)
    {
        _mainViewRef = new WeakReference<MainView>(mainView);
    }

    public static bool TryHandleBackNavigation()
    {
        if (_mainViewRef is null || !_mainViewRef.TryGetTarget(out var mainView))
            return false;

        return mainView.HandleSystemBackNavigation();
    }

    /// <summary>
    /// 切换阅读器刘海/沉浸模式。
    /// enabled=true 时：Avalonia 边到边显示 + 禁用自动安全区域内边距 + Android ShortEdges 刘海模式。
    /// enabled=false 时：恢复正常布局。
    /// </summary>
    public static void ApplyReaderCutoutMode(bool enabled)
    {
        // ── 1. Avalonia InsetsManager：控制边到边显示和安全区域 ──
        if (_mainViewRef is not null && _mainViewRef.TryGetTarget(out var mainView))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var topLevel = TopLevel.GetTopLevel(mainView);
                    if (topLevel?.InsetsManager is not { } insetsManager) return;

                    insetsManager.DisplayEdgeToEdgePreference = enabled;
                    insetsManager.SystemBarColor = enabled
                        ? Colors.Transparent
                        : Color.Parse("#1E293B");

                    // 禁用自动安全区域内边距，让阅读内容真正延伸到刘海区域
                    mainView.SetValue(TopLevel.AutoSafeAreaPaddingProperty, !enabled);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[ReadStorm] ApplyReaderCutoutMode(Avalonia) failed: {ex.Message}");
                }
            });
        }

        // ── 2. Android 原生：设置 LayoutInDisplayCutoutMode (API 28+) ──
        if (_activityRef is not null && _activityRef.TryGetTarget(out var activity))
        {
            activity.RunOnUiThread(() =>
            {
                try
                {
                    var window = activity.Window;
                    if (window is null) return;

                    if (OperatingSystem.IsAndroidVersionAtLeast(28))
                    {
                        var lp = window.Attributes;
                        if (lp is not null)
                        {
                            lp.LayoutInDisplayCutoutMode = enabled
                                ? LayoutInDisplayCutoutMode.ShortEdges
                                : LayoutInDisplayCutoutMode.Default;
                            window.Attributes = lp;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[ReadStorm] ApplyReaderCutoutMode(Android) failed: {ex.Message}");
                }
            });
        }
    }
}
