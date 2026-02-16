using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ReadStorm.Desktop.ViewModels;
using ReadStorm.Domain.Models;

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

    // ====== 根据窗口宽度计算 TOC 列数（2-4） ======
    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_vm is null) return;

        // 减去边距/padding
        var availableWidth = Math.Max(0, e.NewSize.Width - 60);

        // TOC：每列期望最小宽度 200px
        var tocCols = (int)(availableWidth / 200);
        _vm.TocColumnCount = Math.Clamp(tocCols, 2, 4);

        // 书架大图：每列期望宽度约 240px，限制 3~5 列
        var shelfCols = (int)(availableWidth / 240);
        _vm.BookshelfLargeColumnCount = Math.Clamp(shelfCols, 3, 5);
    }

    // ====== ViewModel PropertyChanged → scroll to top ======
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;

        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.ReaderScrollVersion))
        {
            Dispatcher.UIThread.Post(() =>
            {
                var sv = this.FindControl<ScrollViewer>("ReaderScrollViewer");
                sv?.ScrollToHome();
            });
        }
    }

    // ====== Double-click search result → queue download + flash ======
    private void SearchResult_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || sender is not Control { DataContext: SearchResult result }) return;

        _vm.QueueDownloadFromSearchResult(result);

        if (sender is Border border)
        {
            FlashBorder(border);
        }
    }

    // ====== Double-click bookshelf → open book + switch to reader ======
    private void DbBook_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || sender is not Control { DataContext: BookEntity book }) return;
        _ = _vm.OpenDbBookCommand.ExecuteAsync(book);
    }

    // ====== TOC chapter item clicked ======
    private void TocChapter_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button { Content: string chapterTitle }) return;

        var index = _vm.ReaderChapters.IndexOf(chapterTitle);
        if (index >= 0)
        {
            _vm.SelectTocChapterCommand.Execute(index);
        }
    }

    // ====== Paper preset tapped → apply ======
    private void PaperPreset_Tapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || sender is not Border { Tag: PaperPreset preset }) return;
        _vm.ApplyPaperPresetCommand.Execute(preset);
    }

    // ====== Flash animation for visual feedback ======
    private static void FlashBorder(Border border)
    {
        var original = border.Background;
        border.Background = new SolidColorBrush(Color.FromArgb(120, 59, 130, 246)); // blue flash

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            border.Background = original;
            timer.Stop();
        };
        timer.Start();
    }
}