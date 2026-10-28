// Program entry point: opens the window (MainWindow) with the app's view model.
//
// The window's code is in this project; the rest of the program (reading the simulator's files,
// Encore Decks, PDFs…) is the SimuDeckParser.Core library.

using Avalonia;
using Avalonia.Media;

namespace SimuDeckParser.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .ConfigureFonts(fonts => fonts.AddFontCollection(new EmbeddedFonts()))
            .With(new FontManagerOptions { DefaultFamilyName = EmbeddedFonts.NotoSans })
            .LogToTrace();
}

/// <summary>
/// The fonts compiled into the program (Assets/Fonts), so the window looks the same everywhere
/// and needs no system fonts.
/// </summary>
public sealed class EmbeddedFonts() : Avalonia.Media.Fonts.EmbeddedFontCollection(
    new Uri("fonts:SimuDeckParser", UriKind.Absolute),
    new Uri("avares://SimuDeckParser/Assets/Fonts", UriKind.Absolute))
{
    public const string NotoSans = "fonts:SimuDeckParser#Noto Sans";
}
