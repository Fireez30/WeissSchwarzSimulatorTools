using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>Opening files, folders and links with the system's programs, and the clipboard.</summary>
public static class Shell
{
    private static TopLevel? Top =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>Opens a link in the browser. Returns false when it could not.</summary>
    public static async Task<bool> OpenUrl(string url) =>
        Top is { } top && await top.Launcher.LaunchUriAsync(new Uri(url));

    /// <summary>Opens a file or a folder with the system's program for it.</summary>
    public static async Task<bool> OpenPath(string path)
    {
        if (Top is not { } top) return false;
        return Directory.Exists(path)
            ? await top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path))
            : await top.Launcher.LaunchFileInfoAsync(new FileInfo(path));
    }

    public static async Task Copy(string text)
    {
        if (Top?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    /// <summary>The user's Downloads folder (the home folder when there is none).</summary>
    public static string Downloads()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!OperatingSystem.IsWindows())
        {
            // ~/.config/user-dirs.dirs: XDG_DOWNLOAD_DIR="$HOME/Downloads"
            var config = Path.Combine(Settings.ConfigDir(), "user-dirs.dirs");
            try
            {
                foreach (var line in File.ReadLines(config))
                {
                    if (!line.StartsWith("XDG_DOWNLOAD_DIR=", StringComparison.Ordinal)) continue;
                    var dir = line["XDG_DOWNLOAD_DIR=".Length..].Trim('"').Replace("$HOME", home);
                    if (Directory.Exists(dir)) return dir;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        var downloads = Path.Combine(home, "Downloads");
        return Directory.Exists(downloads) ? downloads : home;
    }
}
