// Tiny key:value settings file inside the OS-specific config folder,
// e.g. %APPDATA%\weiss_simulator_parser\settings.conf on Windows,
// ~/.config/weiss_simulator_parser/settings.conf on Linux and
// ~/Library/Application Support/weiss_simulator_parser/settings.conf on macOS.

namespace SimuDeckParser;

/// <summary>The settings file. Every call reads or rewrites the file: it is a handful of lines.</summary>
public sealed class Settings
{
    // The keys of the settings file.

    /// <summary>Root folder of the simulator install.</summary>
    public const string SimulatorPath = "simulator_data_path";
    /// <summary>
    /// Name of the linked Encore Decks account. Its session is in the system credential store,
    /// never in this file.
    /// </summary>
    public const string EncoreUser = "encoredecks_user";
    /// <summary>system, light or dark.</summary>
    public const string Theme = "ui_theme";
    /// <summary>The screen opened at startup: the last one used, cards or decks.</summary>
    public const string StartScreen = "ui_screen";
    /// <summary>How the Decks screen sorts its list: the last order used, name or date.</summary>
    public const string DeckSort = "ui_deck_sort";
    /// <summary>The serie the card list opens at startup: the last one shown.</summary>
    public const string Serie = "ui_serie";

    public string Path { get; }

    public Settings(string path) => Path = path;

    public static Settings OpenDefault() =>
        new(System.IO.Path.Combine(ConfigDir(), "weiss_simulator_parser", "settings.conf"));

    /// <summary>The OS config folder, as Rust's <c>dirs::config_dir</c> gives it.</summary>
    public static string ConfigDir()
    {
        if (OperatingSystem.IsMacOS())
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library", "Application Support");
        // %APPDATA% on Windows, $XDG_CONFIG_HOME or ~/.config on Linux.
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return dir.Length > 0 ? dir : ".";
    }

    /// <summary>Every key:value line of the file (none when it does not exist yet).</summary>
    public SortedDictionary<string, string> All()
    {
        var all = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string content;
        try
        {
            content = File.ReadAllText(Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return all;
        }
        foreach (var line in TextHelpers.Lines(content))
        {
            var trimmed = line.Trim();
            var colon = trimmed.IndexOf(':');
            if (colon < 0) continue;
            all[trimmed[..colon].Trim()] = trimmed[(colon + 1)..].Trim();
        }
        return all;
    }

    /// <summary>The value of <paramref name="key"/>; null when it is missing or empty.</summary>
    public string? Get(string key) => All().TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    public void Set(string key, string value)
    {
        var all = All();
        all[key] = value;
        Write(all);
    }

    public void Delete(string key)
    {
        var all = All();
        if (all.Remove(key))
            Write(all);
    }

    private void Write(SortedDictionary<string, string> all)
    {
        if (System.IO.Path.GetDirectoryName(Path) is { Length: > 0 } dir)
            Directory.CreateDirectory(dir);
        File.WriteAllText(Path, string.Concat(all.Select(kv => $"{kv.Key}:{kv.Value}\n")));
        // Personal settings: readable by their owner only.
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
