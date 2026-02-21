using Android.Content.PM;
using Android.Views;
using Avalonia;
using Avalonia.Android;

namespace ReadStorm.Android;

[Activity(
    Label = "ReadStorm",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@mipmap/ic_launcher",
    MainLauncher = false,
    Exported = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<ReadStorm.Desktop.App>
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        AndroidSystemUiBridge.RegisterActivity(this);
        // API 23-28 需要运行时权限，API 29+ 使用应用专属目录无需权限
        RequestWritePermissionIfNeeded();
    }

    protected override void OnResume()
    {
        base.OnResume();
        AndroidSystemUiBridge.RegisterActivity(this);
    }

    public override void OnBackPressed()
    {
        if (AndroidSystemUiBridge.TryHandleBackNavigation())
            return;

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            Finish();
            return;
        }

#pragma warning disable CA1422
        base.OnBackPressed();
#pragma warning restore CA1422
    }

    public override bool OnKeyDown([global::Android.Runtime.GeneratedEnum] Keycode keyCode, KeyEvent? e)
    {
        if (keyCode is Keycode.VolumeDown or Keycode.VolumeUp)
        {
            if (AndroidSystemUiBridge.TryHandleVolumeKeyPaging(keyCode))
                return true;
        }

        return base.OnKeyDown(keyCode, e);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // 不调用 .WithInterFont()：Inter 字体不含 CJK 字形，
        // Button ContentPresenter 不触发 Avalonia 的字形回退，导致中文按钮显示方块。
        // 移除后应用使用系统默认字体（Roboto + Noto CJK），天然支持中文。
        return base.CustomizeAppBuilder(builder)
            .LogToTrace();
    }

    /// <summary>
    /// 权限授予后重试外部日志目录设置。
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("android23.0")]
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, global::Android.Content.PM.Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == 1001
            && grantResults.Length > 0
            && grantResults[0] == global::Android.Content.PM.Permission.Granted)
        {
            ReadStorm.Desktop.App.SetupExternalLogDirectory();
        }
    }

    /// <summary>
    /// API 23-28 需要 WRITE_EXTERNAL_STORAGE 运行时权限。
    /// API 29+（Scoped Storage）使用应用专属外部目录（GetExternalFilesDir），无需任何权限。
    /// </summary>
    private void RequestWritePermissionIfNeeded()
    {
        try
        {
            // API 29+ 不需要权限，应用专属目录可直接写入
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                return;

            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                if (CheckSelfPermission(global::Android.Manifest.Permission.WriteExternalStorage)
                    != global::Android.Content.PM.Permission.Granted)
                {
                    RequestPermissions(
                        [global::Android.Manifest.Permission.WriteExternalStorage],
                        1001);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[ReadStorm] RequestStoragePermission failed: {ex.Message}");
        }
    }
}
