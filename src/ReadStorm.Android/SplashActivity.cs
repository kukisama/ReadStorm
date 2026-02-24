using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace ReadStorm.Android;

[Activity(
    Theme = "@style/MyTheme.Launch",
    MainLauncher = true,
    NoHistory = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode)]
public class SplashActivity : Activity
{
    private const int MinSplashDurationMs = 300;
    // 裁切偏移系数（0..1）：0=顶部对齐，0.5=居中，1=底部对齐。
    // 当需要纵向裁切时，值越大表示“上面裁得更多、下面保留更多”。
    private const float PortraitCropFocusY = 0.5f;
    private const float LandscapeCropFocusY = 0.8f;

    private Handler? _mainHandler;
    private bool _navigated;
    private Java.Lang.IRunnable? _navigateRunnable;
    private ImageView? _splashImage;

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

        _splashImage = new ImageView(this);
        _splashImage.SetScaleType(ImageView.ScaleType.Matrix);
        _splashImage.SetImageResource(ResolveSplashDrawableResource());
        _splashImage.Post(ApplySplashImageCrop);
        SetContentView(_splashImage);

        _navigateRunnable = new Java.Lang.Runnable(NavigateToMain);
        _mainHandler.PostDelayed(_navigateRunnable, MinSplashDurationMs);
    }

    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        if (_splashImage is null)
            return;

        _splashImage.SetImageResource(ResolveSplashDrawableResource());
        _splashImage.Post(ApplySplashImageCrop);
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

    private int ResolveSplashDrawableResource()
    {
        var isLandscape = Resources?.Configuration?.Orientation == global::Android.Content.Res.Orientation.Landscape;
        if (!isLandscape)
            return Resource.Drawable.boot;

        // 双图可选支持：若存在 boot_land 则横屏优先使用；不存在则自动回退 boot。
        var landscapeId = Resources?.GetIdentifier("boot_land", "drawable", PackageName) ?? 0;
        return landscapeId != 0 ? landscapeId : Resource.Drawable.boot;
    }

    private void ApplySplashImageCrop()
    {
        if (_splashImage is null)
            return;

        var drawable = _splashImage.Drawable;
        var viewWidth = _splashImage.Width;
        var viewHeight = _splashImage.Height;
        var drawableWidth = drawable?.IntrinsicWidth ?? 0;
        var drawableHeight = drawable?.IntrinsicHeight ?? 0;

        if (drawable is null || viewWidth <= 0 || viewHeight <= 0 || drawableWidth <= 0 || drawableHeight <= 0)
        {
            _splashImage.SetScaleType(ImageView.ScaleType.CenterCrop);
            return;
        }

        var scale = Math.Max(viewWidth / (float)drawableWidth, viewHeight / (float)drawableHeight);
        var scaledWidth = drawableWidth * scale;
        var scaledHeight = drawableHeight * scale;

        var dx = (viewWidth - scaledWidth) / 2f;
        var dy = (viewHeight - scaledHeight) / 2f;

        var isLandscape = Resources?.Configuration?.Orientation == global::Android.Content.Res.Orientation.Landscape;
        var focusY = isLandscape ? LandscapeCropFocusY : PortraitCropFocusY;
        focusY = Math.Clamp(focusY, 0f, 1f);
        if (scaledHeight > viewHeight)
        {
            // 纵向有溢出时使用可调焦点：
            // 0.8 => 顶部裁切更多，底部保留更多。
            dy = (viewHeight - scaledHeight) * focusY;
        }

        var matrix = new Matrix();
        matrix.SetScale(scale, scale);
        matrix.PostTranslate(dx, dy);

        _splashImage.SetScaleType(ImageView.ScaleType.Matrix);
        _splashImage.ImageMatrix = matrix;
    }
}
