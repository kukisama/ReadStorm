using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace ReadStorm.Android;

[Activity(
    Label = "ReadStorm",
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
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // 防止从桌面重复唤起时创建多余实例
        if ((Intent?.Flags ?? 0).HasFlag(ActivityFlags.BroughtToFront))
        {
            Finish();
            return;
        }

        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        StartActivity(intent);
        Finish();
    }
}
