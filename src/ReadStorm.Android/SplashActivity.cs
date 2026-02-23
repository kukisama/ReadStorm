using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;

namespace ReadStorm.Android;

[Activity(
    Theme = "@style/MyTheme.Launch",
    MainLauncher = false,
    NoHistory = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode)]
public class SplashActivity : Activity
{
    private const int MinSplashDurationMs = 300;
    private Handler? _mainHandler;
    private bool _navigated;
    private Java.Lang.IRunnable? _navigateRunnable;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // 防止“已有任务在前台时从桌面再次点击图标”导致重复实例。
        // 不能仅依据 BroughtToFront 判断，否则部分机型会在冷启动时误判并直接退出。
        if (!IsTaskRoot
            && Intent is { } launchIntent
            && string.Equals(launchIntent.Action, Intent.ActionMain, StringComparison.Ordinal)
            && launchIntent.HasCategory(Intent.CategoryLauncher))
        {
            Finish();
            return;
        }

        // legado 风格：开屏页先真实展示全屏图，再进入主界面。
        _mainHandler = new Handler(Looper.MainLooper!);

        var splashImage = new ImageView(this);
        splashImage.SetScaleType(ImageView.ScaleType.CenterCrop);
        splashImage.SetImageResource(Resource.Drawable.boot);
        SetContentView(splashImage);

        _navigateRunnable = new Java.Lang.Runnable(NavigateToMain);
        _mainHandler.PostDelayed(_navigateRunnable, MinSplashDurationMs);
    }

    protected override void OnDestroy()
    {
        if (_mainHandler is not null && _navigateRunnable is not null)
        {
            _mainHandler.RemoveCallbacks(_navigateRunnable);
            _navigateRunnable.Dispose();
            _navigateRunnable = null;
        }
        base.OnDestroy();
    }

    private void NavigateToMain()
    {
        if (_navigated || IsFinishing || IsDestroyed)
            return;

        _navigated = true;

        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        StartActivity(intent);
        Finish();
    }
}
