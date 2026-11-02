using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using WeissSchwarzSimulatorTools.App.ViewModels;

namespace WeissSchwarzSimulatorTools.App.Views;

public partial class FolderNameDialog : UserControl
{
    public FolderNameDialog() => InitializeComponent();

    private void OnNameAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) =>
        Dispatcher.UIThread.Post(() =>
        {
            NameField.Focus();
            NameField.SelectAll();
        }, DispatcherPriority.Background);

    private void OnNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is FolderNameDialogViewModel { CanConfirm: true } vm) vm.ConfirmCommand.Execute(null);
    }
}
