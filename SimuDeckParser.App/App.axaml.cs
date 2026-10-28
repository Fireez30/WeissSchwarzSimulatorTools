using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using SimuDeckParser.App.ViewModels;
using SimuDeckParser.App.Views;

namespace SimuDeckParser.App;

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
