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
    private Control? _lastRuleEditorInput;

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
        _vm.Reader.TocColumnCount = Math.Clamp(tocCols, 2, 4);

        // 书架大图：每列期望宽度约 240px，限制 3~5 列
        var shelfCols = (int)(availableWidth / 240);
        _vm.Bookshelf.BookshelfLargeColumnCount = Math.Clamp(shelfCols, 3, 5);
    }

    // ====== ViewModel PropertyChanged → scroll to top ======
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Reader.PropertyChanged -= OnReaderPropertyChanged;
            _vm.RuleEditor.PropertyChanged -= OnRuleEditorPropertyChanged;
        }

        _vm = DataContext as MainWindowViewModel;
        if (_vm is not null)
        {
            _vm.Reader.PropertyChanged += OnReaderPropertyChanged;
            _vm.RuleEditor.PropertyChanged += OnRuleEditorPropertyChanged;
        }
    }

    private void OnReaderPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.ReaderScrollVersion))
        {
            Dispatcher.UIThread.Post(() =>
            {
                var sv = this.FindControl<ScrollViewer>("ReaderScrollViewer");
                sv?.ScrollToHome();
            });
        }

        // 打开目录时自动滚动到当前章节
        if (e.PropertyName == nameof(ReaderViewModel.IsTocOverlayVisible)
            && _vm?.Reader.IsTocOverlayVisible == true)
        {
            Dispatcher.UIThread.Post(ScrollTocToCurrentChapter, DispatcherPriority.Loaded);
        }
    }

    private void OnRuleEditorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RuleEditorViewModel.RuleEditorRefocusVersion))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_lastRuleEditorInput is { IsVisible: true, IsEnabled: true })
                {
                    _lastRuleEditorInput.Focus();
                }
            });
        }
    }

    private void RuleEditorInput_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (e.Source is Control ctrl && IsRuleEditorFocusableInput(ctrl))
        {
            _lastRuleEditorInput = ctrl;
        }
    }

    private static bool IsRuleEditorFocusableInput(Control ctrl)
    {
        return ctrl is TextBox or ComboBox or CheckBox or NumericUpDown;
    }

    // ====== Double-click search result → queue download + flash ======
    private void SearchResult_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || sender is not Control { DataContext: SearchResult result }) return;

        _vm.SearchDownload.QueueDownloadFromSearchResult(result);

        if (sender is Border border)
        {
            FlashBorder(border);
        }
    }

    // ====== Enter key in search box → trigger search ======
    private void SearchKeyword_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && _vm is not null)
        {
            _vm.SearchDownload.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ====== Double-click bookshelf → open book + switch to reader ======
    private void DbBook_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || sender is not Control { DataContext: BookEntity book }) return;
        _ = _vm.OpenDbBookAndSwitchToReaderAsync(book);
    }

    // ====== TOC chapter item clicked ======
    private void TocChapter_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button { Content: string chapterTitle }) return;

        var index = _vm.Reader.ReaderChapters.IndexOf(chapterTitle);
        if (index >= 0)
        {
            _vm.Reader.SelectTocChapterCommand.Execute(index);
        }
    }

    // ====== TOC ListBox selection changed → navigate to chapter ======
    private void TocListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 仅在用户主动点击时触发（非代码设置）
        if (sender is ListBox listBox && _vm is not null && listBox.SelectedIndex >= 0)
        {
            // 不在此处导航，由 TocChapter_Click 处理
        }
    }

    // ====== 自动滚动目录到当前章节 ======
    private void ScrollTocToCurrentChapter()
    {
        if (_vm is null) return;

        var listBox = this.FindControl<ListBox>("TocListBox");
        if (listBox is null) return;

        var index = _vm.Reader.ReaderCurrentChapterIndex;
        if (index >= 0 && index < _vm.Reader.ReaderChapters.Count)
        {
            listBox.ScrollIntoView(index);
        }
    }

    // ====== Paper preset tapped → apply ======
    private void PaperPreset_Tapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || sender is not Border { Tag: PaperPreset preset }) return;
        _vm.Reader.ApplyPaperPresetCommand.Execute(preset);
    }

    // ====== 阅读器区域键盘快捷键：← / → 或 PageUp / PageDown 切换章节 ======
    private void ReaderGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;

        // 如果焦点在 TextBox / NumericUpDown / ComboBox 等输入控件中，不拦截
        if (e.Source is TextBox or NumericUpDown or ComboBox) return;

        switch (e.Key)
        {
            case Key.Left:
            case Key.PageUp:
                _vm.Reader.PreviousChapterCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.PageDown:
                _vm.Reader.NextChapterCommand.Execute(null);
                e.Handled = true;
                break;
        }
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