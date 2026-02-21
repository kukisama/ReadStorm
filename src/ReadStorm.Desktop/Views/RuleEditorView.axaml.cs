using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ReadStorm.Desktop.ViewModels;

namespace ReadStorm.Desktop.Views;

public partial class RuleEditorView : UserControl
{
    private RuleEditorViewModel? _vm;
    private Control? _lastRuleEditorInput;

    public RuleEditorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnRuleEditorPropertyChanged;

        _vm = DataContext as RuleEditorViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnRuleEditorPropertyChanged;
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

    private async void OnAddFilterTextFromSelectionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;

        var previewBox = this.FindControl<TextBox>("RuleTestContentPreviewBox");
        var selectedText = previewBox?.SelectedText;
        await _vm.AppendSelectedTextToFilterAndSaveAsync(selectedText);
    }
}
