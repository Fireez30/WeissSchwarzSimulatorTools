using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SimuDeckParser.App.ViewModels;

namespace SimuDeckParser.App.Views;

public partial class ImportDialog : UserControl
{
    public ImportDialog() => InitializeComponent();

    private void OnLinkAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) =>
        Dispatcher.UIThread.Post(() => Link.Focus(), DispatcherPriority.Background);

    private void OnLinkKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ImportDialogViewModel { CanImport: true } vm) vm.ImportCommand.Execute(null);
    }
}
