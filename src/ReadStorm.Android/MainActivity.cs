using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace ReadStorm.Android;

[Activity(
    Label = "ReadStorm",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<ReadStorm.Desktop.App>
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestExternalStoragePermissionIfNeeded();
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
    /// Android 11+（API 30）需要 MANAGE_EXTERNAL_STORAGE 权限才能写入共享存储（Documents/）。
    /// 若未授权，自动跳转到系统"所有文件访问"设置页面。
    /// </summary>
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
    /// Android 11+（API 30）需要 MANAGE_EXTERNAL_STORAGE 权限才能写入公共 Documents/。
    /// API 23-29 需要 WRITE_EXTERNAL_STORAGE 运行时权限。
    /// </summary>
    private void RequestExternalStoragePermissionIfNeeded()
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                if (!global::Android.OS.Environment.IsExternalStorageManager)
                {
                    var intent = new global::Android.Content.Intent(
                        global::Android.Provider.Settings.ActionManageAllFilesAccessPermission);
                    StartActivity(intent);
                }
            }
            else if (OperatingSystem.IsAndroidVersionAtLeast(23))
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
