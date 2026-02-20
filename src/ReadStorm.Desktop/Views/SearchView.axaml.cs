using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ReadStorm.Desktop.ViewModels;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
    }

    private SearchDownloadViewModel? Vm => DataContext as SearchDownloadViewModel;

    private void SearchResult_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is null || sender is not Control { DataContext: SearchResult result }) return;

        Vm.QueueDownloadFromSearchResult(result);

        if (sender is Border border)
        {
            FlashBorder(border);
        }
    }

    private void SearchKeyword_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && Vm is not null)
        {
            Vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static void FlashBorder(Border border)
    {
        var original = border.Background;
        border.Background = new SolidColorBrush(Color.FromArgb(120, 59, 130, 246));

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) =>
        {
            border.Background = original;
            timer.Stop();
        };
        timer.Start();
    }
}
