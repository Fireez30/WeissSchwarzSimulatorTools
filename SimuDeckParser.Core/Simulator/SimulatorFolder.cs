// Where things live in a simulator install, and finding installs on this computer.
//
//     <root>/CardContent/Cards/<serie>/SingleSetData.txt   series and their sets   SeriesFile
//     <root>/CardContent/Cards/<set folder>/CardData.txt   the cards of a set      CardFile
//     <root>/CardContent/Cards/<set folder>/*.jpg          the cards' artwork      CardImages
//     <root>/<Game>_Data/StreamingAssets/CommonEffects…    shared effect texts     CommonEffects
//     <root>/Decks/<deck>.txt                              decks                   DeckFiles
//     <root>/Sleeves/*.jpg|png                             sleeve images           Sleeves

namespace SimuDeckParser.Simulator;

/// <summary>The root folder of a simulator install: the one that contains CardContent and Decks.</summary>
public sealed record SimulatorFolder(string Root)
{
    /// <summary>Names of the common effects file, newest first (it was renamed by a simulator update).</summary>
    private static readonly string[] CommonEffectsFiles = ["CommonEffects_USER_REFERENCE.txt", "CommonEffects(copy).txt"];

    /// <summary><paramref name="root"/> if it is a simulator folder, null otherwise.</summary>
    public static SimulatorFolder? At(string root) =>
        Directory.Exists(Path.Combine(root, "CardContent", "Cards")) ? new SimulatorFolder(root) : null;

    /// <summary>
    /// The simulator folder containing <paramref name="path"/>: users often pick a subfolder such
    /// as Decks, CardContent or Weiss Schwarz_Data, so the parents are tried too.
    /// </summary>
    public static SimulatorFolder? Find(string path)
    {
        string? current = Path.TrimEndingDirectorySeparator(path);
        for (var i = 0; i < 4 && !string.IsNullOrEmpty(current); i++)
        {
            if (At(current) is { } found) return found;
            current = Path.GetDirectoryName(current);
        }
        return null;
    }

    public string Cards => Path.Combine(Root, "CardContent", "Cards");

    public string Decks => Path.Combine(Root, "Decks");

    /// <summary>Sleeve images the simulator offers; may not exist yet.</summary>
    public string Sleeves => Path.Combine(Root, "Sleeves");

    /// <summary>
    /// &lt;root&gt;/&lt;Game&gt;_Data/StreamingAssets, if present. Unity games keep their data files
    /// in a folder named after the game, so the name is not fixed.
    /// </summary>
    public string? StreamingAssets()
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(Root)
                .Where(p => p.EndsWith("_Data", StringComparison.Ordinal))
                .Select(p => Path.Combine(p, "StreamingAssets"))
                .Where(Directory.Exists)
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public string? CommonEffects() =>
        StreamingAssets() is { } assets
            ? CommonEffectsFiles.Select(f => Path.Combine(assets, f)).FirstOrDefault(File.Exists)
            : null;

    /// <summary>Foil artwork folder of older simulators; may not exist.</summary>
    public string AlternateArtwork => Path.Combine(StreamingAssets() ?? Root, "AlternateArtwork");

    /// <summary>
    /// Reads every card of the install: the common effects first (cards refer to them), then
    /// every serie with its sets and cards. Takes a few seconds on a full install: run it off the
    /// UI thread.
    /// </summary>
    public List<Serie> LoadSeries()
    {
        var effects = CommonEffects() is { } path ? Simulator.CommonEffects.Load(path) : new Simulator.CommonEffects();
        return SeriesFile.LoadSeries(Cards, AlternateArtwork, effects);
    }

    /// <summary>
    /// Looks for simulator installs in the usual places: folders under the home folder whose
    /// names suggest games, and Steam libraries. Returns at most 6, sorted. Reads many folders:
    /// run it off the UI thread.
    /// </summary>
    public static List<string> DetectInstalls()
    {
        var found = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0)
        {
            Search(home, 0, found);
            foreach (var steam in new[] { ".local/share/Steam", ".steam/steam" })
                Search(Path.Combine(home, steam, "steamapps", "common"), 2, found);
        }
        if (OperatingSystem.IsWindows())
        {
            foreach (var steam in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
                Search(Path.Combine(steam, "steamapps", "common"), 2, found);
        }
        return found
            .Select(Canonical)
            .Distinct()
            .Order(StringComparer.Ordinal)
            .Take(6)
            .ToList();
    }

    private static string Canonical(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return new DirectoryInfo(full).ResolveLinkTarget(true)?.FullName ?? full;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    /// <summary>
    /// Adds the simulator folders under <paramref name="dir"/> to <paramref name="found"/>. Below
    /// the first level, only folders whose name suggests games are opened, and the search stops 3
    /// levels down.
    /// </summary>
    internal static void Search(string dir, int depth, List<string> found)
    {
        string[] hints = ["weiss", "schwarz", "simu", "game", "jeu", "steam", "ws", "share"];
        IEnumerable<DirectoryInfo> entries;
        try
        {
            entries = new DirectoryInfo(dir).EnumerateDirectories().ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return;
        }
        foreach (var entry in entries)
        {
            // Rust's file_type() does not follow symbolic links.
            if (entry.LinkTarget is not null) continue;
            var name = entry.Name.ToLowerInvariant();
            if (name.StartsWith('.') && name != ".local") continue;
            if (At(entry.FullName) is not null)
                found.Add(entry.FullName);
            else if (depth < 3 && (depth == 0 || hints.Any(name.Contains)))
                Search(entry.FullName, depth + 1, found);
        }
    }
}
