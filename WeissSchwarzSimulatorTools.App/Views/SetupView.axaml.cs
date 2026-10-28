using Avalonia.Controls;
using Avalonia.Input;
using WeissSchwarzSimulatorTools.App.ViewModels;

namespace WeissSchwarzSimulatorTools.App.Views;

public partial class SetupView : UserControl
{
    public SetupView() => InitializeComponent();

    private void OnPathKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel { Setup.CanLoad: true } vm) vm.LoadFromSetupCommand.Execute(null);
    }
}
