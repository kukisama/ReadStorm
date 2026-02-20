using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ReadStorm.Desktop.ViewModels;

namespace ReadStorm.Desktop.Views;

public partial class ReaderView : UserControl
{
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
        if (e.PropertyName == nameof(ReaderViewModel.ReaderScrollVersion))
        {
            Dispatcher.UIThread.Post(() =>
            {
                var sv = this.FindControl<ScrollViewer>("ReaderScrollViewer");
                sv?.ScrollToHome();
            });
        }

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
            case Key.PageUp:
                _vm.PreviousChapterCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.PageDown:
                _vm.NextChapterCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
