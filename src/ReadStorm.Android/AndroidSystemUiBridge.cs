using Android.App;
using Android.Graphics.Drawables;
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

    public static bool TryHandleVolumeKeyPaging(Keycode keyCode)
    {
        if (_mainViewRef is null || !_mainViewRef.TryGetTarget(out var mainView))
            return false;

        return mainView.HandleVolumeKeyPaging(keyCode);
    }

    /// <summary>
    /// 切换阅读器刘海/沉浸模式。
    /// enabled=true 时：Avalonia 边到边显示 + 禁用自动安全区域内边距 + Android ShortEdges 刘海模式。
    /// hideStatusBar=true 时：隐藏系统状态栏（时间/电量图标），实现更纯粹沉浸阅读。
    /// readerBackgroundHex 允许将刘海区域渲染为正文背景色，避免顶部发暗割裂感。
    /// </summary>
    public static void ApplyReaderCutoutMode(bool enabled, bool hideStatusBar = false, string? readerBackgroundHex = null)
    {
        var statusBarColor = ResolveStatusBarColor(enabled, readerBackgroundHex);
        System.Diagnostics.Trace.WriteLine(
            $"[ReadStorm][Cutout][Apply:Start] enabled={enabled} hideStatus={hideStatusBar} readerBg={readerBackgroundHex ?? "(null)"} resolved=#{statusBarColor.A:X2}{statusBarColor.R:X2}{statusBarColor.G:X2}{statusBarColor.B:X2}");

        // ── 1. Avalonia InsetsManager：控制边到边显示和安全区域 ──
        if (_mainViewRef is not null && _mainViewRef.TryGetTarget(out var mainView))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var topLevel = TopLevel.GetTopLevel(mainView);
                    if (topLevel?.InsetsManager is not { } insetsManager)
                    {
                        System.Diagnostics.Trace.WriteLine("[ReadStorm][Cutout][Apply:Avalonia] skipped (InsetsManager unavailable)");
                        return;
                    }

                    insetsManager.DisplayEdgeToEdgePreference = enabled;
                    insetsManager.SystemBarColor = statusBarColor;

                    // 禁用自动安全区域内边距，让阅读内容真正延伸到刘海区域
                    mainView.SetValue(TopLevel.AutoSafeAreaPaddingProperty, !enabled);

                    System.Diagnostics.Trace.WriteLine(
                        $"[ReadStorm][Cutout][Apply:Avalonia] edgeToEdge={enabled} autoSafeArea={!enabled} systemBar=#{statusBarColor.A:X2}{statusBarColor.R:X2}{statusBarColor.G:X2}{statusBarColor.B:X2}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[ReadStorm] ApplyReaderCutoutMode(Avalonia) failed: {ex.Message}");
                }
            });
        }
        else
        {
            System.Diagnostics.Trace.WriteLine("[ReadStorm][Cutout][Apply:Avalonia] skipped (MainView ref unavailable)");
        }

        // ── 2. Android 原生：设置 LayoutInDisplayCutoutMode (API 28+) ──
        if (_activityRef is not null && _activityRef.TryGetTarget(out var activity))
        {
            activity.RunOnUiThread(() =>
            {
                try
                {
                    var window = activity.Window;
                    if (window is null)
                    {
                        System.Diagnostics.Trace.WriteLine("[ReadStorm][Cutout][Apply:Android] skipped (window is null)");
                        return;
                    }

                    var sdkInt = (int)global::Android.OS.Build.VERSION.SdkInt;
                    System.Diagnostics.Trace.WriteLine(
                        $"[ReadStorm][Cutout][Apply:Android] sdk={sdkInt} enabled={enabled} hideStatus={hideStatusBar}");

                    // 强制状态栏颜色与阅读背景一致，避免顶部 1~2px 亮线在深色阅读背景下被放大感知。
                    // Android 35+ 这两个 API 被标记过时，但在当前版本仍可用；
                    // 为保持 Android 12/15 视觉一致，这里继续使用并做局部告警抑制。
#pragma warning disable CA1422
                    // 边到边模式下，显式关闭 decor fits，避免系统为状态栏区域保留独立背景层。
                    if (OperatingSystem.IsAndroidVersionAtLeast(30))
                    {
#pragma warning disable CA1416
                        window.SetDecorFitsSystemWindows(!enabled);
#pragma warning restore CA1416
                    }

                    window.SetStatusBarColor(global::Android.Graphics.Color.Argb(
                        statusBarColor.A,
                        statusBarColor.R,
                        statusBarColor.G,
                        statusBarColor.B));

                    window.SetNavigationBarColor(global::Android.Graphics.Color.Argb(
                        statusBarColor.A,
                        statusBarColor.R,
                        statusBarColor.G,
                        statusBarColor.B));

                    // 部分机型顶部 1~2px 来自 DecorView/Window 背景透出，统一刷成阅读背景色。
                    var nativeBg = global::Android.Graphics.Color.Argb(
                        statusBarColor.A,
                        statusBarColor.R,
                        statusBarColor.G,
                        statusBarColor.B);
                    window.DecorView?.SetBackgroundColor(nativeBg);
                    window.SetBackgroundDrawable(new ColorDrawable(nativeBg));

                    if (OperatingSystem.IsAndroidVersionAtLeast(29))
                    {
#pragma warning disable CA1416
                        window.StatusBarContrastEnforced = false;
#pragma warning restore CA1416
                    }
#pragma warning restore CA1422

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

#pragma warning disable CA1422
                    if (enabled)
                    {
                        window.AddFlags(WindowManagerFlags.LayoutNoLimits);
                    }
                    else
                    {
                        window.ClearFlags(WindowManagerFlags.LayoutNoLimits);
                    }
#pragma warning restore CA1422

                    if (enabled && hideStatusBar)
                    {
                        System.Diagnostics.Trace.WriteLine("[ReadStorm][Cutout][Apply:Android] action=HideStatusBar");
                        if (OperatingSystem.IsAndroidVersionAtLeast(30))
                        {
                            window.InsetsController?.Hide(WindowInsets.Type.StatusBars());
                        }
                        else
                        {
#pragma warning disable CA1422
                            var decorView = window.DecorView;
                            if (decorView is null)
                            {
                                System.Diagnostics.Trace.WriteLine("[ReadStorm][Cutout][Apply:Android] skipped legacy hide flags (DecorView unavailable)");
                                return;
                            }

                            window.AddFlags(WindowManagerFlags.Fullscreen);
                            decorView.SystemUiFlags =
                                SystemUiFlags.ImmersiveSticky
                                | SystemUiFlags.LayoutStable
                                | SystemUiFlags.LayoutFullscreen
                                | SystemUiFlags.Fullscreen;
#pragma warning restore CA1422
                        }
                    }
                    else
                    {
                        System.Diagnostics.Trace.WriteLine("[ReadStorm][Cutout][Apply:Android] action=ShowStatusBar");
                        if (OperatingSystem.IsAndroidVersionAtLeast(30))
                        {
                            window.InsetsController?.Show(WindowInsets.Type.StatusBars());
                        }
                        else
                        {
                            var decorView = window.DecorView;
                            if (decorView is null)
                            {
                                System.Diagnostics.Trace.WriteLine("[ReadStorm][Cutout][Apply:Android] skipped legacy flags (DecorView unavailable)");
                                return;
                            }

                            if (enabled)
                            {
                                decorView.SystemUiFlags =
                                    SystemUiFlags.LayoutStable | SystemUiFlags.LayoutFullscreen;
                            }
                            else
                            {
                                decorView.SystemUiFlags = SystemUiFlags.Visible;
                            }
                            window.ClearFlags(WindowManagerFlags.Fullscreen);
                        }
                    }

                    System.Diagnostics.Trace.WriteLine(
                        $"[ReadStorm][Cutout][Apply:Done] enabled={enabled} hideStatus={hideStatusBar} color=#{statusBarColor.A:X2}{statusBarColor.R:X2}{statusBarColor.G:X2}{statusBarColor.B:X2}");
#pragma warning restore CA1422
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        $"[ReadStorm] ApplyReaderCutoutMode(Android) failed: {ex.Message}");
                }
            });
        }
        else
        {
            System.Diagnostics.Trace.WriteLine("[ReadStorm][Cutout][Apply:Android] skipped (Activity ref unavailable)");
        }
    }

    private static Color ResolveStatusBarColor(bool enabled, string? readerBackgroundHex)
    {
        if (!string.IsNullOrWhiteSpace(readerBackgroundHex)
            && Color.TryParse(readerBackgroundHex, out var bgColor))
        {
            return bgColor;
        }

        return Color.Parse("#1E293B");
    }
}
