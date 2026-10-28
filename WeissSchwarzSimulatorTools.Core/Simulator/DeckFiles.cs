// Simulator decks: one plain-text file per deck in <root>/Decks:
//
//     # WeissSim Deck
//     Name: Konosuba Aqua and Eris
//     Date: 12:00  06/15/2010
//     Sleeve: RZ Emilia 1.jpg
//
//     Cards:
//     KS/W49-E001
//     KS/W49-E001
//
// Sleeve names an image of the simulator's Sleeves folder (see Sleeves); it is empty when the
// deck has none.
//
// AI_*.txt files are the AI opponents' decks, copied there by the simulator.

using System.Globalization;
using System.Text;

namespace WeissSchwarzSimulatorTools.Simulator;

/// <summary>
/// A deck file as saved in the simulator. Its cards are only codes; to get the cards themselves,
/// see <see cref="CardIndex.ResolveDeck"/>.
/// </summary>
public sealed record DeckFile
{
    public string Name { get; init; } = "";
    public string Date { get; init; } = "";
    /// <summary>File name of the deck's sleeve image in the simulator's Sleeves folder.</summary>
    public string Sleeve { get; init; } = "";
    public IReadOnlyList<string> Codes { get; init; } = [];
    public string Path { get; init; } = "";
    /// <summary>Deck of an AI opponent.</summary>
    public bool Ai { get; init; }

    /// <summary>When the deck was saved; null when its date is missing or not in the simulator's format.</summary>
    public DateTime? SavedAt =>
        DateTime.TryParseExact(Date.Trim(), DeckFiles.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    public bool Equals(DeckFile? other) =>
        other is not null && Name == other.Name && Date == other.Date && Sleeve == other.Sleeve
        && Codes.SequenceEqual(other.Codes) && Path == other.Path && Ai == other.Ai;

    public override int GetHashCode() => HashCode.Combine(Name, Date, Path);
}

public static class DeckFiles
{
    private const string Header = "# WeissSim Deck";
    private const string AiPrefix = "AI_";

    /// <summary>The simulator's deck date format, HH:MM  MM/DD/YYYY.</summary>
    internal const string DateFormat = "HH:mm  MM/dd/yyyy";

    /// <summary>Parses a deck file; the name falls back to the file name.</summary>
    public static DeckFile Parse(string content, string path)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        string name = "", date = "", sleeve = "";
        var codes = new List<string>();
        var inCards = false;
        foreach (var raw in TextHelpers.Lines(content))
        {
            var line = raw.TrimStart('﻿').Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (inCards) codes.Add(line);
            else if (line.StartsWith("Name:", StringComparison.Ordinal)) name = line[5..].Trim();
            else if (line.StartsWith("Date:", StringComparison.Ordinal)) date = line[5..].Trim();
            else if (line.StartsWith("Sleeve:", StringComparison.Ordinal)) sleeve = line[7..].Trim();
            else if (line == "Cards:") inCards = true;
        }
        return new DeckFile
        {
            Name = name.Length == 0 ? stem : name,
            Date = date,
            Sleeve = sleeve,
            Codes = codes,
            Ai = stem.StartsWith(AiPrefix, StringComparison.Ordinal),
            Path = path,
        };
    }

    /// <summary>Every deck in <paramref name="dir"/>, sorted by name (a missing folder has no decks).</summary>
    public static List<DeckFile> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var files = Errors.WithContext($"reading {dir}", () => Directory.GetFiles(dir));
        var decks = new List<DeckFile>();
        foreach (var path in files)
        {
            var fileName = System.IO.Path.GetFileName(path);
            if (System.IO.Path.GetExtension(path) != ".txt" || fileName.StartsWith('.')) continue;
            try
            {
                decks.Add(Parse(Encoding.UTF8.GetString(File.ReadAllBytes(path)), path));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        return decks.OrderBy(d => d.Ai).ThenBy(d => d.Name.ToLowerInvariant(), StringComparer.Ordinal).ToList();
    }

    /// <summary>The content of a deck file, in the simulator's format.</summary>
    public static string Format(string name, string date, string sleeve, IEnumerable<string> codes)
    {
        // The simulator writes Sleeve: with nothing after the colon when the deck has none.
        sleeve = sleeve.Trim();
        var sleevePart = sleeve.Length == 0 ? "" : " " + sleeve;
        var output = new StringBuilder($"{Header}\nName: {name}\nDate: {date}\nSleeve:{sleevePart}\n\nCards:\n");
        foreach (var code in codes)
            output.Append(code).Append('\n');
        return output.ToString();
    }

    /// <summary><paramref name="name"/> without the characters that are not allowed in file names on any system.</summary>
    public static string FileStemFor(string name)
    {
        var cleaned = new string(name.Select(c => c switch
        {
            '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' => '-',
            _ when char.IsControl(c) => ' ',
            _ => c,
        }).ToArray()).Trim().Trim('.').Trim();
        return cleaned.Length == 0 ? "Imported deck" : cleaned;
    }

    /// <summary>
    /// Writes a new deck file, never overwriting one: a taken name gets a " (2)", " (3)"… suffix,
    /// which is also used as the deck name. Returns the saved deck.
    /// </summary>
    public static DeckFile SaveNew(string dir, string name, string date, string sleeve, IReadOnlyList<string> codes)
    {
        Errors.WithContext($"creating {dir}", () => Directory.CreateDirectory(dir));
        var stem = FileStemFor(name);
        bool Taken(string n)
        {
            try
            {
                return List(dir).Any(d => d.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            }
            catch (UserException)
            {
                return false;
            }
        }
        // Try "Name", then "Name (2)", "Name (3)"… until neither the file nor the name is taken.
        var finalName = name.Trim();
        var path = System.IO.Path.Combine(dir, $"{stem}.txt");
        var suffix = 2;
        while (File.Exists(path) || Directory.Exists(path) || Taken(finalName))
        {
            finalName = $"{name.Trim()} ({suffix})";
            path = System.IO.Path.Combine(dir, $"{stem} ({suffix}).txt");
            suffix++;
        }
        if (finalName.Length == 0) finalName = stem;
        Errors.WithContext($"writing {path}", () => File.WriteAllText(path, Format(finalName, date, sleeve, codes)));
        return new DeckFile { Name = finalName, Date = date, Sleeve = sleeve, Codes = codes.ToList(), Path = path };
    }

    /// <summary>
    /// Rewrites an existing deck file. When the name changed, the file is renamed to match it if
    /// that file name is free. Fails when another deck already uses the new name.
    /// </summary>
    public static DeckFile Update(string path, string name, string date, string sleeve, IReadOnlyList<string> codes)
    {
        name = name.Trim();
        var dir = System.IO.Path.GetDirectoryName(path) is { Length: > 0 } d ? d : ".";
        if (List(dir).Any(deck => deck.Path != path && deck.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new UserException($"another deck is already named \"{name}\"");
        var renamed = System.IO.Path.Combine(dir, $"{FileStemFor(name)}.txt");
        var target = path;
        if (renamed != path && !File.Exists(renamed) && !Directory.Exists(renamed))
        {
            Errors.WithContext($"renaming {path}", () => File.Move(path, renamed));
            target = renamed;
        }
        Errors.WithContext($"writing {target}", () => File.WriteAllText(target, Format(name, date, sleeve, codes)));
        return new DeckFile { Name = name, Date = date, Sleeve = sleeve, Codes = codes.ToList(), Path = target };
    }

    /// <summary>Current time in the simulator's deck date format, HH:MM  MM/DD/YYYY.</summary>
    public static string NowDate() => DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture);
}
