// Deck folders: the rows of the deck tree of the Decks screen, and the dialogs that name a folder
// or pick where a deck or folder goes.
//
// The simulator lets decks live in folders and subfolders of Decks; the tree shows them the same
// way. Folders are named by their path relative to Decks ("ReZero/Test"; "" is Decks itself).

using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeissSchwarzSimulatorTools.Simulator;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

public static class FolderLabels
{
    /// <summary>The name the Decks folder itself is shown with.</summary>
    public const string Root = "Decks";

    /// <summary>An error as a sentence for a dialog: "a folder cannot…" -> "A folder cannot….".</summary>
    public static string Sentence(Exception e)
    {
        var text = e is UserException ? e.Message.Trim() : $"{e.Message} ({e.GetType().Name})";
        if (text.Length == 0) return "Something went wrong.";
        text = char.ToUpperInvariant(text[0]) + text[1..];
        return text.EndsWith('.') ? text : text + ".";
    }

    /// <summary>"ReZero/Test" -> "Decks › ReZero › Test".</summary>
    public static string Path(string folder) =>
        folder.Length == 0 ? Root : $"{Root} › {folder.Replace("/", " › ")}";

    /// <summary>"ReZero/Test" -> "Test"; "" -> "Decks".</summary>
    public static string Name(string folder) => folder.Length == 0 ? Root : DeckFiles.FolderName(folder);

    /// <summary>
    /// The folders a deck can be saved to, for a folder field: Decks then its subfolders (as
    /// <paramref name="folders"/> lists them, or as read from <paramref name="root"/>).
    /// </summary>
    public static List<Option<string>> Choices(string root, IEnumerable<string>? folders = null)
    {
        IEnumerable<string> found;
        try
        {
            found = folders ?? DeckFiles.Folders(root);
        }
        catch (Exception)
        {
            found = [];
        }
        return found.Prepend("").Select(f => new Option<string>(f, Path(f))).ToList();
    }

    /// <summary>The choice of <paramref name="folder"/>, or Decks itself when it is not there (any more).</summary>
    public static Option<string> Pick(IReadOnlyList<Option<string>> choices, string? folder) =>
        choices.FirstOrDefault(c => c.Value == folder) ?? choices.FirstOrDefault() ?? new Option<string>("", Root);

    /// <summary>"Decks", "ReZero", "ReZero/Test" for "ReZero/Test": the folder and every folder above it.</summary>
    public static IEnumerable<string> Chain(string folder)
    {
        yield return "";
        if (folder.Length == 0) yield break;
        var parts = folder.Split('/');
        for (var i = 1; i <= parts.Length; i++) yield return string.Join('/', parts[..i]);
    }
}

/// <summary>A row of the deck tree: a folder, a deck, or the note that a folder is empty.</summary>
public abstract record DeckTreeRow(int Depth)
{
    /// <summary>Room left of the row's content, by depth.</summary>
    public double IndentWidth => Depth * 16;
}

/// <summary>A folder of the deck tree; a click opens or closes it.</summary>
public sealed record FolderRow(string Folder, int Depth, int Count, bool Expanded, bool IsCurrent) : DeckTreeRow(Depth)
{
    public string Name => FolderLabels.Name(Folder);
    public string CountText => Count.ToString();
    public bool Collapsed => !Expanded;
    public required ICommand Toggle { get; init; }
    public required ICommand NewFolder { get; init; }
    public required ICommand Rename { get; init; }
    public required ICommand Move { get; init; }
    public required ICommand Delete { get; init; }
}

/// <summary>A deck of the deck tree; a click opens it.</summary>
public sealed record DeckRow(DeckItem Item, int Depth, bool IsSelected) : DeckTreeRow(Depth)
{
    public required ICommand Open { get; init; }
    public required ICommand Move { get; init; }
}

/// <summary>Under an open folder with nothing in it.</summary>
public sealed record EmptyFolderRow(int Depth) : DeckTreeRow(Depth);

/// <summary>A segment of the path of the current folder, above the tree: a click goes to that folder.</summary>
public sealed record FolderCrumb(string Folder, bool IsLast, ICommand Go)
{
    public string Name => FolderLabels.Name(Folder);
    public bool HasSeparator => !IsLast;
}

/// <summary>
/// Asks for a folder name (new folder, rename). <paramref name="confirmed"/> does the work; when
/// it fails, the dialog stays open with the error.
/// </summary>
public sealed partial class FolderNameDialogViewModel(
    string title, string message, string confirmLabel, string name, Func<string, string?> problem, Action<string> confirmed)
    : DialogViewModel
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public string ConfirmLabel { get; } = confirmLabel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Problem), nameof(HasProblem), nameof(CanConfirm))]
    private string _name = name;

    partial void OnNameChanged(string value) => Error = null;

    /// <summary>Why the name cannot be used; nothing is said while the field is empty.</summary>
    public string? Problem => Name.Length == 0 ? null : problem(Name);
    public bool HasProblem => Problem is not null;
    public bool CanConfirm => Name.Length > 0 && Problem is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    [RelayCommand]
    private void Confirm()
    {
        if (!CanConfirm) return;
        try
        {
            confirmed(Name);
            Close();
        }
        catch (Exception e)
        {
            Error = FolderLabels.Sentence(e);
        }
    }
}

/// <summary>A folder the Move dialog offers.</summary>
public sealed record FolderChoice(string Folder, int Depth, bool IsEnabled, string? Why)
{
    public string Name => FolderLabels.Name(Folder);
    public double IndentWidth => Depth * 16;
}

/// <summary>Picks the folder a deck or a folder moves to. Folders it cannot go to are shown greyed out.</summary>
public sealed partial class MoveDialogViewModel : DialogViewModel
{
    private readonly Action<string> _confirmed;

    public MoveDialogViewModel(string title, string message, IReadOnlyList<FolderChoice> choices, Action<string> confirmed)
    {
        Title = title;
        Message = message;
        Choices = choices;
        _confirmed = confirmed;
    }

    public string Title { get; }
    public string Message { get; }
    public IReadOnlyList<FolderChoice> Choices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(Destination))]
    private FolderChoice? _selected;

    partial void OnSelectedChanged(FolderChoice? value)
    {
        Error = null;
        // Folders it cannot go to cannot be picked.
        if (value is { IsEnabled: false }) Selected = null;
    }

    public bool CanConfirm => Selected is { IsEnabled: true };
    public string Destination => Selected is { } s ? $"To {FolderLabels.Path(s.Folder)}" : "Pick a folder.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool HasError => Error is not null;

    [RelayCommand]
    private void Confirm()
    {
        if (Selected is not { IsEnabled: true } target) return;
        try
        {
            _confirmed(target.Folder);
            Close();
        }
        catch (Exception e)
        {
            Error = FolderLabels.Sentence(e);
        }
    }
}
