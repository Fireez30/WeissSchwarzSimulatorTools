// Sleeves: the card-back images of the simulator's Sleeves folder. A deck file names the one it
// uses (see DeckFiles).
//
// Users can add their own from a file on disk or a link: the image is checked (it must be a JPEG
// or PNG the simulator can show) and copied under a free name, never overwriting a sleeve that
// is already there.

using SkiaSharp;

namespace SimuDeckParser.Simulator;

/// <summary>A sleeve image of the simulator's Sleeves folder.</summary>
/// <param name="Name">File name, as deck files refer to it: RZ Emilia 1.jpg.</param>
public sealed record Sleeve(string Name, string Path)
{
    /// <summary>The name shown to users: the file name without its extension.</summary>
    public static string DisplayName(string fileName)
    {
        foreach (var extension in new[] { ".jpg", ".jpeg", ".png" })
            if (fileName.EndsWith(extension, StringComparison.Ordinal))
                return fileName[..^extension.Length];
        return fileName;
    }
}

/// <summary>Where a sleeve image comes from, as the user typed it.</summary>
public abstract record SleeveSource
{
    public sealed record Url(string Link) : SleeveSource;

    public sealed record File(string Path) : SleeveSource;

    /// <summary>
    /// A link (http://, https://) or a path; file:// links and the quotes a file manager adds
    /// when copying a path are accepted. Null when nothing was typed.
    /// </summary>
    public static SleeveSource? Parse(string text)
    {
        text = text.Trim().Trim('"', '\'').Trim();
        if (text.Length == 0) return null;
        var lower = text.ToLowerInvariant();
        if (lower.StartsWith("http://", StringComparison.Ordinal) || lower.StartsWith("https://", StringComparison.Ordinal))
            return new Url(text);
        if (!lower.StartsWith("file://", StringComparison.Ordinal))
            return new File(text);
        var rest = text["file://".Length..];
        // file:///C:/x.png on Windows: the slash before the drive letter is not part.
        if (rest.Length >= 3 && rest[0] == '/' && rest[2] == ':')
            rest = rest[1..];
        return new File(TextHelpers.PercentDecode(rest));
    }

    /// <summary>The file name the source suggests, without extension; may be empty.</summary>
    internal string Stem()
    {
        string last;
        if (this is Url url)
        {
            var path = url.Link.Split('?', '#')[0];
            var scheme = path.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) path = path[(scheme + 3)..];
            // Only the part after the host names a file.
            var slash = path.IndexOf('/');
            last = slash < 0 ? "" : TextHelpers.PercentDecode(path[(slash + 1)..].Split('/')[^1]);
        }
        else
        {
            last = System.IO.Path.GetFileName(((File)this).Path);
        }
        var dot = last.LastIndexOf('.');
        return dot < 0 ? last : last[..dot];
    }

    /// <summary>The image's bytes: read from disk or downloaded.</summary>
    internal async Task<byte[]> ReadAsync()
    {
        switch (this)
        {
            case File { Path: var path }:
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    if (Directory.Exists(path)) throw new UserException($"{path} is not a file");
                    throw new UserException($"there is no file at {path}");
                }
                if (info.Length > Sleeves.MaxImageBytes)
                    throw new UserException($"the image is larger than {Sleeves.MaxImageBytes / 1024 / 1024} MB");
                return Errors.WithContext($"reading {path}", () => System.IO.File.ReadAllBytes(path));
            case Url { Link: var link }:
                try
                {
                    return await Http.GetBytesAsync(link, Sleeves.MaxImageBytes);
                }
                catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException or UserException)
                {
                    throw Errors.Context($"downloading {link}", e);
                }
            default:
                throw new InvalidOperationException();
        }
    }
}

public static class Sleeves
{
    /// <summary>Largest image accepted, so a wrong link does not fill the disk.</summary>
    internal const long MaxImageBytes = 25 * 1024 * 1024;
    /// <summary>Name of a sleeve whose source tells nothing useful.</summary>
    private const string FallbackName = "Sleeve";

    /// <summary>Every sleeve image in <paramref name="dir"/>, sorted by name (a missing folder has no sleeves).</summary>
    public static List<Sleeve> List(string dir)
    {
        try
        {
            return Directory.GetFiles(dir)
                .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png")
                .Select(p => new Sleeve(Path.GetFileName(p), p))
                .OrderBy(s => s.Name.ToLowerInvariant(), StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Copies the image of <paramref name="source"/> into <paramref name="dir"/> (created if
    /// missing) and returns the new sleeve. <paramref name="name"/> is the sleeve's name without
    /// extension; empty, the source's file name is used. A taken name gets a " (2)", " (3)"… suffix.
    /// </summary>
    public static async Task<Sleeve> AddAsync(string dir, SleeveSource source, string name)
    {
        var bytes = await source.ReadAsync();
        var extension = ImageExtension(bytes);
        if (name.Trim().Length == 0) name = source.Stem();
        var stem = DeckFiles.FileStemFor(name);
        // FileStemFor names nameless decks; a sleeve gets a name of its own.
        if (name.Trim().Trim('.').Trim().Length == 0) stem = FallbackName;
        Errors.WithContext($"creating {dir}", () => Directory.CreateDirectory(dir));
        var path = FreePath(dir, stem, extension);
        Errors.WithContext($"writing {path}", () => File.WriteAllBytes(path, bytes));
        return new Sleeve(Path.GetFileName(path), path);
    }

    /// <summary>jpg or png for an image the simulator can show; an error for anything else.</summary>
    internal static string ImageExtension(byte[] bytes)
    {
        var extension = GuessFormat(bytes) switch
        {
            "JPEG" => "jpg",
            "PNG" => "png",
            null => throw new UserException("this is not an image (a web page instead of the picture itself?)"),
            var other => throw new UserException($"{other} images are not supported: use a JPEG or PNG"),
        };
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        using var bitmap = codec is null ? null : new SKBitmap(codec.Info);
        if (codec is null || bitmap is null || codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new UserException("the image is damaged and cannot be read");
        return extension;
    }

    /// <summary>The image format <paramref name="bytes"/> start with, from its signature; null when it is no image.</summary>
    private static string? GuessFormat(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "JPEG",
        [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => "PNG",
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => "GIF",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "WEBP",
        [(byte)'B', (byte)'M', ..] => "BMP",
        [(byte)'I', (byte)'I', 0x2A, 0x00, ..] or [(byte)'M', (byte)'M', 0x00, 0x2A, ..] => "TIFF",
        [0x00, 0x00, 0x01, 0x00, ..] => "ICO",
        [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f', ..] => "AVIF",
        [(byte)'q', (byte)'o', (byte)'i', (byte)'f', ..] => "QOI",
        _ => null,
    };

    /// <summary>
    /// dir/stem.extension, or dir/stem (2).extension… when a sleeve already has that name
    /// (whatever its extension or case: the decks name sleeves case-insensitively).
    /// </summary>
    private static string FreePath(string dir, string stem, string extension)
    {
        var taken = Directory.EnumerateFileSystemEntries(dir)
            .Select(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant())
            .ToHashSet();
        var candidate = stem;
        for (var n = 2; taken.Contains(candidate.ToLowerInvariant()); n++)
            candidate = $"{stem} ({n})";
        return Path.Combine(dir, $"{candidate}.{extension}");
    }
}
