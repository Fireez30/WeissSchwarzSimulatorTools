// Clicks through every screen and saves screenshots, in both themes: run it before and after a
// change to compare.
//
//     SIMULATOR_ROOT=/path/to/simulator SCREENSHOT_DIR=/some/dir dotnet test WeissSchwarzSimulatorTools.App.Tests
//
// Without SIMULATOR_ROOT, the test does nothing. The simulator folder is only read; the settings
// are a temporary file.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using WeissSchwarzSimulatorTools.App;
using WeissSchwarzSimulatorTools.App.ViewModels;
using WeissSchwarzSimulatorTools.App.Views;

[assembly: AvaloniaTestApplication(typeof(WeissSchwarzSimulatorTools.App.Tests.TestApp))]

namespace WeissSchwarzSimulatorTools.App.Tests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .ConfigureFonts(fonts => fonts.AddFontCollection(new EmbeddedFonts()))
            .With(new FontManagerOptions { DefaultFamilyName = EmbeddedFonts.NotoSans });
}

public class Screenshots
{
    private static string OutputDir =>
        Environment.GetEnvironmentVariable("SCREENSHOT_DIR") is { Length: > 0 } dir ? dir : Path.Combine(Path.GetTempPath(), "sdp-screenshots");

    /// <summary>Runs the UI's jobs until <paramref name="done"/> (background work posts back to the UI thread).</summary>
    private static void WaitUntil(Func<bool> done, int seconds = 60)
    {
        var until = DateTime.Now.AddSeconds(seconds);
        while (!done())
        {
            if (DateTime.Now > until) throw new TimeoutException("the UI did not get there in time");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Save(Window window, string name)
    {
        // Pictures load in the background: give them a moment.
        for (var i = 0; i < 15; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(30);
        }
        Directory.CreateDirectory(OutputDir);
        window.CaptureRenderedFrame()?.Save(Path.Combine(OutputDir, $"{name}.png"));
    }

    [AvaloniaFact]
    public void EveryScreen()
    {
        // Nothing to take without a simulator (AvaloniaFact cannot be skipped at run time).
        var root = Environment.GetEnvironmentVariable("SIMULATOR_ROOT");
        if (string.IsNullOrEmpty(root)) return;
        var settingsDir = Directory.CreateTempSubdirectory("sdp-settings-");
        var settings = new Settings(Path.Combine(settingsDir.FullName, "settings.conf"));
        settings.Set(Settings.SimulatorPath, root!);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = theme;
            var vm = new MainViewModel(settings);
            Application.Current!.RequestedThemeVariant = theme;
            var window = new MainWindow { DataContext = vm, Width = 1600, Height = 1000 };
            window.Show();
            var suffix = theme == ThemeVariant.Dark ? "dark" : "light";
            WaitUntil(() => vm.IsMain);
            vm.SwitchTo(Screen.Cards);
            Save(window, $"cards-{suffix}");

            vm.Cards.Pane.Pin(vm.Cards.Shown[3].Card);
            Save(window, $"cards-pinned-{suffix}");
            vm.Cards.ShowListCommand.Execute(null);
            Save(window, $"cards-list-{suffix}");
            vm.Cards.ShowGridCommand.Execute(null);

            vm.SwitchTo(Screen.Decks);
            Save(window, $"decks-{suffix}");
            vm.Decks.SelectedOwn = vm.Decks.OwnDecks.FirstOrDefault();
            Save(window, $"decks-open-{suffix}");
            vm.Decks.EditCommand.Execute(null);
            Save(window, $"deck-editor-{suffix}");
            vm.Decks.Editor?.CancelCommand.Execute(null);
            vm.Decks.PrintProxiesCommand.Execute(null);
            Save(window, $"dialog-proxies-{suffix}");
            vm.Dialog?.Close();
            vm.Decks.ImportCommand.Execute(null);
            Save(window, $"dialog-import-{suffix}");
            vm.Dialog?.Close();

            vm.SwitchTo(Screen.Damage);
            vm.Damage.SelectedOwn = vm.Damage.OwnDecks.FirstOrDefault();
            WaitUntil(() => vm.Damage.HasPicked);
            var slots = vm.Damage.Slots;
            for (var i = 0; i < slots.Count; i++) slots[i].Choice = slots[i].Choices[Math.Min(i + 1, slots[i].Choices.Count - 1)];
            vm.Damage.RunCommand.Execute(null);
            WaitUntil(() => vm.Damage.HasResults);
            Save(window, $"damage-{suffix}");

            vm.SwitchTo(Screen.Encore);
            Save(window, $"encore-{suffix}");
            vm.SwitchTo(Screen.Settings);
            Save(window, $"settings-{suffix}");
            vm.Notify(new Notice.Error("Could not do it", "Something went wrong: a long explanation of what happened."));
            Save(window, $"error-{suffix}");
            window.Close();
        }
        settingsDir.Delete(true);
    }
}
