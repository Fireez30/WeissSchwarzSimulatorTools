// Finding the artwork of a card in the simulator's folders.
//
// The card data rarely says where a card's picture is: it is found by trying the file names the
// simulator uses (KS_W49_E001.jpg for KS/W49-E001), with the same code rewrites as CardCodes
// (-E, -TE, -PE), case-insensitively.

namespace WeissSchwarzSimulatorTools.Simulator;

/// <summary>
/// Artwork files (.jpg, .jpeg, .png) of one folder, by lowercase file stem, so a set's folder is
/// listed once instead of probing every spelling of every card.
/// </summary>
public sealed class ImageDir
{
    private readonly Dictionary<string, string> _byStem = new(StringComparer.Ordinal);

    public static ImageDir Read(string dir)
    {
        var images = new ImageDir();
        string[] files;
        try
        {
            files = Directory.GetFiles(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return images;
        }
        var pictures = files
            .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png")
            // Deterministic pick when two files differ only by case or extension (jpg first).
            .Order(StringComparer.Ordinal);
        foreach (var path in pictures)
            images._byStem.TryAdd(Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), path);
        return images;
    }

    internal string? Get(string stem) => _byStem.GetValueOrDefault(stem.ToLowerInvariant());
}

public static class CardImages
{
    /// <summary>
    /// Image file stems tried for a card, in order: the card's Image line, then the code as-is,
    /// -E, -TE and -PE, then the same without the series prefix (W107_E001 for BTR/W107-001),
    /// then the card number alone (001).
    /// </summary>
    private static List<string> ImageStems(string key, string? hint)
    {
        static IEnumerable<string> Rewrites(string code) =>
            new[]
            {
                code,
                code.Replace("-", "-E", StringComparison.Ordinal),
                code.Replace("-T", "-TE", StringComparison.Ordinal),
                code.Replace("-P", "-PE", StringComparison.Ordinal),
            }.Select(TextHelpers.ImageStem);

        var output = new List<string>();
        if (hint is not null)
        {
            output.Add(hint.Trim());
            output.Add(TextHelpers.ImageStem(hint));
        }
        output.AddRange(Rewrites(key));
        var slash = key.IndexOf('/');
        if (slash >= 0)
            output.AddRange(Rewrites(key[(slash + 1)..]));
        var dash = key.LastIndexOf('-');
        if (dash >= 0)
            output.Add(key[(dash + 1)..]);
        return output;
    }

    /// <summary>
    /// Finds the artwork for a card: the set folder first, then (older simulators) the alternate
    /// artwork folders &lt;set&gt; (JP foils) and EN&lt;set&gt; (EN foils), where &lt;set&gt; comes
    /// from ABC/&lt;set&gt;-001.
    /// </summary>
    public static string? Resolve(ImageDir setImages, string key, string? hint, string alternateArtwork)
    {
        var stems = ImageStems(key, hint);
        foreach (var stem in stems)
            if (setImages.Get(stem) is { } found)
                return found;
        if (!Directory.Exists(alternateArtwork))
            return null;

        var foilSet = "";
        var parts = key.Split('/');
        if (parts.Length > 1 && parts[1].IndexOf('-') is var dash and >= 0)
            foilSet = parts[1][..dash];
        foreach (var folder in new[] { Path.Combine(alternateArtwork, foilSet), Path.Combine(alternateArtwork, "EN" + foilSet) })
        {
            foreach (var stem in stems)
            {
                foreach (var candidate in new[] { Path.Combine(folder, stem + ".jpg"), Path.Combine(folder, stem.ToLowerInvariant() + ".jpg") })
                    if (File.Exists(candidate))
                        return candidate;
            }
        }
        return null;
    }
}
