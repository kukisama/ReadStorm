using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ReadStorm.Desktop.ViewModels;

namespace ReadStorm.Desktop.Views;

public partial class ReaderView : UserControl
{
    private WindowState _beforeFullScreenState = WindowState.Normal;

    public ReaderView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ReaderViewModel? _vm;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnReaderPropertyChanged;

        _vm = DataContext as ReaderViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnReaderPropertyChanged;
    }

    private void OnReaderPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReaderViewModel.IsTocOverlayVisible)
            && _vm?.IsTocOverlayVisible == true)
        {
            Dispatcher.UIThread.Post(ScrollTocToCurrentChapter, DispatcherPriority.Loaded);
        }
    }

    private void ScrollTocToCurrentChapter()
    {
        if (_vm is null) return;

        var listBox = this.FindControl<ListBox>("TocListBox");
        if (listBox is null) return;

        var index = _vm.ReaderCurrentChapterIndex;
        if (index >= 0 && index < _vm.ReaderChapters.Count)
        {
            listBox.ScrollIntoView(index);
        }
    }

    private void TocChapter_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button { Content: string chapterTitle }) return;

        var index = _vm.ReaderChapters.IndexOf(chapterTitle);
        if (index >= 0)
        {
            _vm.SelectTocChapterCommand.Execute(index);
        }
    }

    private void PaperPreset_Tapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || sender is not Border { Tag: PaperPreset preset }) return;
        _vm.ApplyPaperPresetCommand.Execute(preset);
    }

    private void ReaderGrid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;

        if (e.Source is TextBox or NumericUpDown or ComboBox) return;

        switch (e.Key)
        {
            case Key.Left:
                _vm.PreviousPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                _vm.NextPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.PageUp:
            case Key.Up:
                _vm.PreviousPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.PageDown:
            case Key.Down:
            case Key.Space:
                _vm.NextPageCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.F11:
                ToggleReaderFullscreen();
                e.Handled = true;
                break;
        }
    }

    private void OnReaderContentTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is not Panel panel) return;

        var pos = e.GetPosition(panel);
        var width = panel.Bounds.Width;
        if (width <= 0) return;

        var ratio = pos.X / width;

        // 左 1/3：上一页
        if (ratio < 1.0 / 3.0)
        {
            _vm.PreviousPageCommand.Execute(null);
            return;
        }

        // 右 1/3：下一页
        if (ratio > 2.0 / 3.0)
        {
            _vm.NextPageCommand.Execute(null);
            return;
        }

        // 中间区域：目录菜单
        _vm.ToggleTocOverlayCommand.Execute(null);
    }

    /// <summary>视口尺寸变化时通知 ViewModel 重新计算分页。</summary>
    private void OnReaderContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        _vm?.UpdateViewportSize(e.NewSize.Width, e.NewSize.Height);
    }

    private void ToggleReaderFullscreen()
    {
        if (TopLevel.GetTopLevel(this) is not Window window) return;

        if (window.WindowState == WindowState.FullScreen)
        {
            window.WindowState = _beforeFullScreenState;
            return;
        }

        _beforeFullScreenState = window.WindowState;
        window.WindowState = WindowState.FullScreen;
    }
}
