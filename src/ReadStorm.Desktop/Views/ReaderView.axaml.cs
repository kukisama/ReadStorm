using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Linq;
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
            listBox.SelectedIndex = index;
            listBox.ScrollIntoView(index);
            Dispatcher.UIThread.Post(() => CenterListBoxItem(listBox, index), DispatcherPriority.Background);
            Dispatcher.UIThread.Post(() => CenterListBoxItem(listBox, index), DispatcherPriority.Render);
        }
    }

    private static void CenterListBoxItem(ListBox listBox, int index)
    {
        var scroller = listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (scroller is null)
            return;

        var container = listBox.ContainerFromIndex(index) as Control;
        if (container is null)
            return;

        var point = container.TranslatePoint(default, scroller);
        if (point is null)
            return;

        var targetY = scroller.Offset.Y + point.Value.Y - (scroller.Viewport.Height - container.Bounds.Height) / 2d;
        if (targetY < 0) targetY = 0;
        scroller.Offset = new Vector(scroller.Offset.X, targetY);
    }

    private void TocChapter_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button button) return;

        if (button.Tag is int index && index >= 0 && index < _vm.ReaderChapters.Count)
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
            case Key.Up:
            case Key.PageUp:
                _vm.PreviousChapterCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down:
            case Key.PageDown:
                _vm.NextChapterCommand.Execute(null);
                e.Handled = true;
                break;
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

        // 左半区：上一页
        if (ratio < 0.5)
        {
            _vm.PreviousPageCommand.Execute(null);
            return;
        }

        // 右半区：下一页
        _vm.NextPageCommand.Execute(null);
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
