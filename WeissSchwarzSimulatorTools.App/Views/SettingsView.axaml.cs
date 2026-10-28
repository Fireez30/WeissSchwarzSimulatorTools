using Avalonia.Controls;
using Avalonia.Input;
using WeissSchwarzSimulatorTools.App.ViewModels;

namespace WeissSchwarzSimulatorTools.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel { Account.CanLink: true } vm) vm.Account.LinkCommand.Execute(null);
    }
}
