// CardContent/Cards/<serie>/SingleSetData.txt -> serie name and sets, each pointing at a card
// folder:
//
//     Series: Angel Beats!
//     Folder: Angel Beats/W31
//     Prefix: AB/W31
//     SetName: Re:Edit (W31)

namespace WeissSchwarzSimulatorTools.Simulator;

public static class SeriesFile
{
    /// <summary>One Folder: block of the file, before its cards are read.</summary>
    private sealed class SetBlock
    {
        public string Folder = "";
        public string Prefix = "";
        public string Name = "";
        public string? File;
    }

    /// <summary>
    /// Reads a serie folder: its display name (Series:, else the folder name) and its sets. Null
    /// when the folder has no SingleSetData.txt (plain card folders such as Key/).
    /// </summary>
    public static Serie? LoadSerie(string seriePath, string cardsPath, string alternateArtwork, CommonEffects effects)
    {
        byte[] bytes;
        try
        {
            bytes = System.IO.File.ReadAllBytes(Path.Combine(seriePath, "SingleSetData.txt"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(seriePath));
        // Each Folder: starts a set; Prefix: (possibly repeated), SetName: and FileName: (card
        // file other than CardData.txt) describe it.
        var blocks = new List<SetBlock>();
        foreach (var raw in TextHelpers.Lines(TextHelpers.DecodeText(bytes)))
        {
            var line = raw.Trim();
            if (line.StartsWith("Series:", StringComparison.Ordinal))
            {
                var rest = line[7..].Trim();
                if (rest.Length > 0) name = rest;
            }
            else if (line.StartsWith("Folder:", StringComparison.Ordinal))
            {
                blocks.Add(new SetBlock { Folder = line[7..].Trim() });
            }
            else if (blocks.Count > 0)
            {
                var block = blocks[^1];
                if (line.StartsWith("Prefix:", StringComparison.Ordinal))
                {
                    if (block.Prefix.Length == 0) block.Prefix = line[7..].Trim();
                }
                else if (line.StartsWith("SetName:", StringComparison.Ordinal))
                {
                    block.Name = line[8..].Trim();
                }
                else if (line.StartsWith("FileName:", StringComparison.Ordinal))
                {
                    var file = line[9..].Trim();
                    block.File = file.Length > 0 ? file : null;
                }
            }
        }
        var sets = blocks
            .Where(b => b.Folder.Length > 0)
            .Select(b =>
            {
                // Folders are relative to the Cards root, not to the serie.
                var folder = Path.Combine(cardsPath, b.Folder);
                return new CardSet
                {
                    Key = b.Prefix,
                    Name = new[] { b.Name, b.Prefix, b.Folder }.First(n => n.Length > 0),
                    Cards = CardFile.Load(folder, b.File, alternateArtwork, effects),
                    Path = folder,
                };
            })
            .Where(set => set.Cards.Count > 0)
            .ToList();
        if (sets.Count == 0) return null;
        return new Serie { Name = name, Path = seriePath, Sets = sets };
    }

    /// <summary>
    /// Every non-hidden folder under <paramref name="cardsPath"/> with a SingleSetData.txt is a
    /// serie, sorted by name. Series are parsed in parallel.
    /// </summary>
    public static List<Serie> LoadSeries(string cardsPath, string alternateArtwork, CommonEffects effects)
    {
        var folders = new DirectoryInfo(cardsPath).EnumerateDirectories()
            .Where(d => !d.Name.StartsWith('.'))
            .Select(d => d.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();
        return folders
            .AsParallel()
            .AsOrdered()
            .Select(path => LoadSerie(path, cardsPath, alternateArtwork, effects))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList()
            .OrderBy(s => s.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();
    }
}
