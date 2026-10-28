using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WeissSchwarzSimulatorTools.App.ViewModels;
using WeissSchwarzSimulatorTools.App.Views;

namespace WeissSchwarzSimulatorTools.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow { DataContext = new MainViewModel(Settings.OpenDefault()) };
        base.OnFrameworkInitializationCompleted();
    }
}
