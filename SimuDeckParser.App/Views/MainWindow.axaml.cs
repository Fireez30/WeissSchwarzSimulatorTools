using Avalonia.Controls;
using Avalonia.Input;
using SimuDeckParser.App.ViewModels;

namespace SimuDeckParser.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm) vm.QuitConfirmed += Close;
        };
        KeyDown += OnKeyDown;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>Asks before closing the window over a deck with unsaved changes.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (Vm is { } vm && !vm.CanClose()) e.Cancel = true;
        base.OnClosing(e);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || Vm is not { } vm) return;
        if (vm.HasErrorDialog) vm.CloseErrorDialogCommand.Execute(null);
        else if (vm.Dialog is { CanDismiss: true } dialog) dialog.Close();
        else return;
        e.Handled = true;
    }

    private void OnScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm?.Dialog is { CanDismiss: true } dialog) dialog.Close();
    }

    private void OnErrorScrimPressed(object? sender, PointerPressedEventArgs e) => Vm?.CloseErrorDialogCommand.Execute(null);
}
