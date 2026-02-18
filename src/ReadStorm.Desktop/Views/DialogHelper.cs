using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace ReadStorm.Desktop.Views;

/// <summary>
/// 统一确认对话框 helper。
/// 所有破坏性操作（删除书籍、删除规则、删除下载任务）都应通过此类发起确认。
/// </summary>
public static class DialogHelper
{
    /// <summary>
    /// 弹出确认对话框。返回 true 表示用户点击「确定」，false 表示取消。
    /// </summary>
    public static async Task<bool> ConfirmAsync(string title, string message)
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null) return true; // 无法获取窗口时放行，不阻断操作

        var dialog = new Window
        {
            Title = title,
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.BorderOnly,
        };

        var result = false;

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(20, 20, 20, 10),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        var okButton = new Button
        {
            Content = "确定",
            MinWidth = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        okButton.Click += (_, _) => { result = true; dialog.Close(); };

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 80,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        cancelButton.Click += (_, _) => { result = false; dialog.Close(); };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 12,
            Margin = new Thickness(0, 10, 0, 20),
            Children = { okButton, cancelButton },
        };

        var content = new DockPanel
        {
            Children =
            {
                buttonPanel,
                messageBlock,
            },
        };
        DockPanel.SetDock(buttonPanel, Dock.Bottom);

        dialog.Content = content;

        await dialog.ShowDialog(mainWindow);
        return result;
    }

    private static Window? GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
