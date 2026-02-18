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
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // 不调用 .WithInterFont()：Inter 字体不含 CJK 字形，
        // Button ContentPresenter 不触发 Avalonia 的字形回退，导致中文按钮显示方块。
        // 移除后应用使用系统默认字体（Roboto + Noto CJK），天然支持中文。
        return base.CustomizeAppBuilder(builder)
            .LogToTrace();
    }
}
