// Simulator decks: one plain-text file per deck in <root>/Decks:
//
//     # WeissSim Deck
//     Name: Konosuba Aqua and Eris
//     Date: 12:00  06/15/2010
//     Sleeve: RZ Emilia 1.jpg
//     Tag: Salvage
//     Tag: Blue
//
//     Cards:
//     KS/W49-E001
//     KS/W49-E001
//
// Sleeve names an image of the simulator's Sleeves folder (see Sleeves); it is empty when the
// deck has none. A deck has up to MaxTags Tag: lines (none when it has no tags).
//
// AI_*.txt files are the AI opponents' decks, copied there by the simulator.
//
// Decks can be sorted into folders and subfolders of Decks: the simulator shows the same tree.
// A folder is named by its path relative to Decks, with / between its parts ("ReZero/Test"); the
// Decks folder itself is "". Hidden folders (".aidecks_cache", the simulator's) are not deck
// folders. Deck names only need to be unique inside their folder.

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
    /// <summary>The deck's tags, at most <see cref="DeckFiles.MaxTags"/>.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Codes { get; init; } = [];
    public string Path { get; init; } = "";
    /// <summary>The deck's folder, relative to the Decks folder ("" for Decks itself, "ReZero/Test" for a subfolder).</summary>
    public string Folder { get; init; } = "";
    /// <summary>Deck of an AI opponent.</summary>
    public bool Ai { get; init; }

    /// <summary>When the deck was saved; null when its date is missing or not in the simulator's format.</summary>
    public DateTime? SavedAt =>
        DateTime.TryParseExact(Date.Trim(), DeckFiles.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    public bool Equals(DeckFile? other) =>
        other is not null && Name == other.Name && Date == other.Date && Sleeve == other.Sleeve
        && Tags.SequenceEqual(other.Tags) && Codes.SequenceEqual(other.Codes) && Path == other.Path && Folder == other.Folder && Ai == other.Ai;

    public override int GetHashCode() => HashCode.Combine(Name, Date, Path);
}

public static class DeckFiles
{
    private const string Header = "# WeissSim Deck";
    private const string AiPrefix = "AI_";

    /// <summary>The simulator's deck date format, HH:MM  MM/DD/YYYY.</summary>
    internal const string DateFormat = "HH:mm  MM/dd/yyyy";

    /// <summary>The most tags a deck can have in the simulator.</summary>
    public const int MaxTags = 4;

    /// <summary>Parses a deck file; the name falls back to the file name.</summary>
    public static DeckFile Parse(string content, string path)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(path);
        string name = "", date = "", sleeve = "";
        var tags = new List<string>();
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
            else if (line.StartsWith("Tag:", StringComparison.Ordinal)) tags.Add(line[4..].Trim());
            else if (line == "Cards:") inCards = true;
        }
        return new DeckFile
        {
            Name = name.Length == 0 ? stem : name,
            Date = date,
            Sleeve = sleeve,
            Tags = CleanTags(tags),
            Codes = codes,
            Ai = stem.StartsWith(AiPrefix, StringComparison.Ordinal),
            Path = path,
        };
    }

    /// <summary>
    /// Every deck in the Decks folder <paramref name="root"/> and its subfolders, sorted by name
    /// (a missing folder has no decks).
    /// </summary>
    public static List<DeckFile> List(string root)
    {
        if (!Directory.Exists(root)) return [];
        var decks = new List<DeckFile>();
        foreach (var folder in Folders(root).Prepend(""))
        {
            // A subfolder that cannot be read is skipped, not the whole list.
            try
            {
                decks.AddRange(ListIn(FolderPath(root, folder)).Select(d => d with { Folder = folder }));
            }
            catch (UserException) when (folder.Length > 0)
            {
            }
        }
        return decks.OrderBy(d => d.Ai).ThenBy(d => d.Name.ToLowerInvariant(), StringComparer.Ordinal).ThenBy(d => d.Folder, StringComparer.Ordinal).ToList();
    }

    /// <summary>The decks directly in <paramref name="dir"/>, not in its subfolders.</summary>
    private static List<DeckFile> ListIn(string dir)
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
        return decks;
    }

    // Folders.

    /// <summary>Every deck folder under <paramref name="root"/>, at any depth, sorted (parents before their subfolders).</summary>
    public static List<string> Folders(string root)
    {
        var found = new List<string>();
        void Walk(string dir, string relative)
        {
            string[] subdirs;
            try
            {
                subdirs = Directory.GetDirectories(dir);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;
            }
            foreach (var sub in subdirs.OrderBy(d => System.IO.Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var name = System.IO.Path.GetFileName(sub);
                if (name.StartsWith('.')) continue;
                // Links could loop, or lead out of the Decks folder.
                if (new DirectoryInfo(sub).LinkTarget is not null) continue;
                var path = JoinFolder(relative, name);
                found.Add(path);
                Walk(sub, path);
            }
        }
        if (Directory.Exists(root)) Walk(root, "");
        return found;
    }

    /// <summary>"ReZero" + "Test" -> "ReZero/Test"; "" + "ReZero" -> "ReZero".</summary>
    public static string JoinFolder(string parent, string name) => parent.Length == 0 ? name : $"{parent}/{name}";

    /// <summary>The parent of a folder: "ReZero/Test" -> "ReZero", "ReZero" -> "".</summary>
    public static string ParentFolder(string folder)
    {
        var slash = folder.LastIndexOf('/');
        return slash < 0 ? "" : folder[..slash];
    }

    /// <summary>The last part of a folder's path: "ReZero/Test" -> "Test".</summary>
    public static string FolderName(string folder) => folder[(folder.LastIndexOf('/') + 1)..];

    /// <summary>Whether <paramref name="folder"/> is <paramref name="ancestor"/> or one of its subfolders.</summary>
    public static bool IsInFolder(string folder, string ancestor) =>
        ancestor.Length == 0 || folder == ancestor || folder.StartsWith(ancestor + "/", StringComparison.Ordinal);

    /// <summary>
    /// The full path of the deck folder <paramref name="folder"/> under <paramref name="root"/>.
    /// Fails for a path that could lead out of it, or to a hidden folder ("..", "." or empty parts).
    /// </summary>
    public static string FolderPath(string root, string folder)
    {
        if (folder.Length == 0) return root;
        var parts = folder.Split('/');
        var path = System.IO.Path.Combine([root, .. parts]);
        var inside = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        if (parts.Any(p => p.Trim().Length == 0 || p.StartsWith('.') || p.Contains('\\'))
            || !System.IO.Path.GetFullPath(path).StartsWith(inside, StringComparison.Ordinal))
            throw new UserException($"\"{folder}\" is not a deck folder");
        return path;
    }

    private static readonly string[] ReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>
    /// Why <paramref name="name"/> cannot name a deck folder, or null when it can. Names must work on
    /// Linux and Windows alike, since the simulator runs on both.
    /// </summary>
    public static string? FolderNameProblem(string name)
    {
        if (name.Trim().Length == 0) return "Give the folder a name.";
        if (name != name.Trim()) return "A folder name cannot start or end with a space.";
        if (name.StartsWith('.')) return "A folder name cannot start with a dot.";
        if (name.EndsWith('.')) return "A folder name cannot end with a dot.";
        if (name.Any(c => c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(c)))
            return "A folder name cannot hold any of / \\ : * ? \" < > |";
        if (ReservedNames.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase))
            return $"\"{name}\" is reserved on Windows.";
        return null;
    }

    /// <summary>Fails unless <paramref name="name"/> is free in <paramref name="dir"/> (ignoring case, as on Windows).</summary>
    private static void CheckFree(string dir, string name, string? except = null)
    {
        if (!Directory.Exists(dir)) return;
        var taken = Errors.WithContext($"reading {dir}", () => Directory.GetFileSystemEntries(dir))
            .Select(System.IO.Path.GetFileName)
            .Any(n => n is not null && n != except && n.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (taken) throw new UserException($"\"{name}\" already exists in this folder");
    }

    private static void CheckName(string name)
    {
        if (FolderNameProblem(name) is { } problem) throw new UserException(problem.TrimEnd('.'));
    }

    private static void CheckExists(string root, string folder)
    {
        if (folder.Length == 0 || !Directory.Exists(FolderPath(root, folder)))
            throw new UserException($"the folder \"{folder}\" does not exist");
    }

    /// <summary>Creates the folder <paramref name="name"/> in <paramref name="parent"/>. Returns the new folder.</summary>
    public static string CreateFolder(string root, string parent, string name)
    {
        CheckName(name);
        var parentDir = FolderPath(root, parent);
        CheckFree(parentDir, name);
        var folder = JoinFolder(parent, name);
        Errors.WithContext($"creating {name}", () => Directory.CreateDirectory(FolderPath(root, folder)));
        return folder;
    }

    /// <summary>Renames a folder, keeping it where it is. Returns the renamed folder.</summary>
    public static string RenameFolder(string root, string folder, string name)
    {
        CheckName(name);
        CheckExists(root, folder);
        var parent = ParentFolder(folder);
        var renamed = JoinFolder(parent, name);
        if (renamed == folder) return folder;
        CheckFree(FolderPath(root, parent), name, except: FolderName(folder));
        var (from, to) = (FolderPath(root, folder), FolderPath(root, renamed));
        Errors.WithContext($"renaming {from}", () =>
        {
            if (from.Equals(to, StringComparison.OrdinalIgnoreCase))
            {
                // Only the case changes: Windows needs a step through another name.
                var step = $"{from}.renaming";
                Directory.Move(from, step);
                Directory.Move(step, to);
            }
            else
                Directory.Move(from, to);
        });
        return renamed;
    }

    /// <summary>Moves a folder, with everything in it, into <paramref name="parent"/>. Returns the moved folder.</summary>
    public static string MoveFolder(string root, string folder, string parent)
    {
        CheckExists(root, folder);
        if (IsInFolder(parent, folder)) throw new UserException("a folder cannot be moved into itself");
        var moved = JoinFolder(parent, FolderName(folder));
        if (moved == folder) return folder;
        var parentDir = FolderPath(root, parent);
        if (!Directory.Exists(parentDir)) throw new UserException($"the folder \"{parent}\" does not exist");
        CheckFree(parentDir, FolderName(folder));
        var (from, to) = (FolderPath(root, folder), FolderPath(root, moved));
        Errors.WithContext($"moving {from}", () => Directory.Move(from, to));
        return moved;
    }

    /// <summary>Whether a folder holds no file at all (empty subfolders are fine).</summary>
    public static bool IsEmptyFolder(string root, string folder) =>
        !Errors.WithContext($"reading {folder}", () =>
            Directory.EnumerateFiles(FolderPath(root, folder), "*", SearchOption.AllDirectories).Any());

    /// <summary>Deletes a folder that holds no file (decks are never deleted with their folder).</summary>
    public static void DeleteFolder(string root, string folder)
    {
        CheckExists(root, folder);
        if (!IsEmptyFolder(root, folder))
            throw new UserException($"\"{FolderName(folder)}\" is not empty: move its decks out first");
        Errors.WithContext($"deleting {folder}", () => Directory.Delete(FolderPath(root, folder), true));
    }

    /// <summary>
    /// Moves the deck file at <paramref name="path"/> into <paramref name="folder"/>, keeping its
    /// file name. Fails when that folder already has the file or a deck of the same name.
    /// </summary>
    public static DeckFile MoveDeck(string root, string path, string folder)
    {
        var dir = FolderPath(root, folder);
        if (!Directory.Exists(dir)) throw new UserException($"the folder \"{folder}\" does not exist");
        var deck = Errors.WithContext($"reading {path}", () => Parse(Encoding.UTF8.GetString(File.ReadAllBytes(path)), path));
        var target = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(path));
        if (System.IO.Path.GetFullPath(target) == System.IO.Path.GetFullPath(path)) return deck with { Folder = folder };
        CheckFree(dir, System.IO.Path.GetFileName(path));
        if (ListIn(dir).Any(d => d.Name.Equals(deck.Name, StringComparison.OrdinalIgnoreCase)))
            throw new UserException($"a deck of this folder is already named \"{deck.Name}\"");
        Errors.WithContext($"moving {path}", () => File.Move(path, target));
        return deck with { Path = target, Folder = folder };
    }

    /// <summary>The content of a deck file, in the simulator's format.</summary>
    public static string Format(string name, string date, string sleeve, IEnumerable<string> codes, IEnumerable<string>? tags = null)
    {
        // The simulator writes Sleeve: with nothing after the colon when the deck has none.
        sleeve = sleeve.Trim();
        var sleevePart = sleeve.Length == 0 ? "" : " " + sleeve;
        var output = new StringBuilder($"{Header}\nName: {name}\nDate: {date}\nSleeve:{sleevePart}\n");
        foreach (var tag in CleanTags(tags ?? []))
            output.Append("Tag: ").Append(tag).Append('\n');
        output.Append("\nCards:\n");
        foreach (var code in codes)
            output.Append(code).Append('\n');
        return output.ToString();
    }

    /// <summary>A tag as it can be saved: one line, trimmed; empty when nothing is left.</summary>
    public static string CleanTag(string tag) =>
        new string(tag.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();

    /// <summary>The non-empty tags, without duplicates (ignoring case), at most <see cref="MaxTags"/>.</summary>
    public static List<string> CleanTags(IEnumerable<string> tags) =>
        tags.Select(CleanTag).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxTags).ToList();

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
    /// Writes a new deck file in <paramref name="dir"/> (the Decks folder or one of its subfolders),
    /// never overwriting one: a name taken in that folder gets a " (2)", " (3)"… suffix, which is
    /// also used as the deck name. Returns the saved deck.
    /// </summary>
    public static DeckFile SaveNew(string dir, string name, string date, string sleeve, IReadOnlyList<string> codes,
        IReadOnlyList<string>? tags = null)
    {
        Errors.WithContext($"creating {dir}", () => Directory.CreateDirectory(dir));
        var stem = FileStemFor(name);
        bool Taken(string n)
        {
            try
            {
                return ListIn(dir).Any(d => d.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
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
        Errors.WithContext($"writing {path}", () => File.WriteAllText(path, Format(finalName, date, sleeve, codes, tags)));
        return new DeckFile { Name = finalName, Date = date, Sleeve = sleeve, Tags = CleanTags(tags ?? []), Codes = codes.ToList(), Path = path };
    }

    /// <summary>
    /// Rewrites an existing deck file. When the name changed, the file is renamed to match it if
    /// that file name is free. Fails when another deck of its folder already uses the new name.
    /// </summary>
    public static DeckFile Update(string path, string name, string date, string sleeve, IReadOnlyList<string> codes,
        IReadOnlyList<string>? tags = null)
    {
        name = name.Trim();
        var dir = System.IO.Path.GetDirectoryName(path) is { Length: > 0 } d ? d : ".";
        if (ListIn(dir).Any(deck => deck.Path != path && deck.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new UserException($"another deck is already named \"{name}\"");
        var renamed = System.IO.Path.Combine(dir, $"{FileStemFor(name)}.txt");
        var target = path;
        if (renamed != path && !File.Exists(renamed) && !Directory.Exists(renamed))
        {
            Errors.WithContext($"renaming {path}", () => File.Move(path, renamed));
            target = renamed;
        }
        Errors.WithContext($"writing {target}", () => File.WriteAllText(target, Format(name, date, sleeve, codes, tags)));
        return new DeckFile { Name = name, Date = date, Sleeve = sleeve, Tags = CleanTags(tags ?? []), Codes = codes.ToList(), Path = target };
    }

    /// <summary>Current time in the simulator's deck date format, HH:MM  MM/DD/YYYY.</summary>
    public static string NowDate() => DateTime.Now.ToString(DateFormat, CultureInfo.InvariantCulture);
}
