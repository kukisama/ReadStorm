using Avalonia.Controls;
using ReadStorm.Desktop.ViewModels;

namespace ReadStorm.Desktop.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnWindowSizeChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _vm = DataContext as MainWindowViewModel;
    }

    /// <summary>
    /// 根据窗口宽度动态调整子 ViewModel 的列数参数。
    /// </summary>
    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm is null) return;

        var availableWidth = Math.Max(0, e.NewSize.Width - 60);

        // TOC：每列期望最小宽度 200px
        var tocCols = (int)(availableWidth / 200);
        _vm.Reader.TocColumnCount = Math.Clamp(tocCols, 2, 5);

        // 书架大图：每列期望宽度约 240px，限制 3~5 列
        var shelfCols = (int)(availableWidth / 240);
        _vm.Bookshelf.BookshelfLargeColumnCount = Math.Clamp(shelfCols, 3, 5);
    }
}