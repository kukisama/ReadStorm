using Avalonia.Controls;
using Avalonia.Input;
using ReadStorm.Desktop.ViewModels;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.Views;

public partial class BookshelfView : UserControl
{
    public BookshelfView()
    {
        InitializeComponent();
    }

    private BookshelfViewModel? Vm => DataContext as BookshelfViewModel;

    private void DbBook_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (Vm is null || sender is not Control { DataContext: BookEntity book }) return;

        // 通过 Bookshelf VM 的 OpenDbBookCommand 打开，它会自动切换到阅读 Tab
        if (Vm.OpenDbBookCommand.CanExecute(book))
            Vm.OpenDbBookCommand.Execute(book);
    }
}
