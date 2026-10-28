// The Decks screen: the decks saved in the simulator on the left, the open deck in the middle
// (its stats and cards), the hovered card on the right.
//
// From here the user can also build or edit a deck (the screen then shows the deck editor
// instead), import from Encore Decks, upload to it, and write PDFs.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimuDeckParser.EncoreDecks;
using SimuDeckParser.Simulator;

namespace SimuDeckParser.App.ViewModels;

/// <summary>The order of the deck list.</summary>
public enum DeckSort
{
    Name,
    /// <summary>Most recently saved first; decks without a date last.</summary>
    Date,
}

/// <summary>A deck file of the list on the left.</summary>
public sealed record DeckItem(DeckFile File)
{
    public string Name => File.Ai && File.Name.StartsWith("AI_", StringComparison.Ordinal) ? File.Name[3..] : File.Name;

    public string Supporting => File.Ai || File.Date.Trim().Length == 0
        ? $"{File.Codes.Count} cards"
        : $"{File.Codes.Count} cards · {File.Date.Trim()}";
}

/// <summary>The deck open in the middle of the screen: its file, and its cards matched to the loaded simulator.</summary>
public sealed partial class PickedDeckViewModel(DeckFile file, Deck deck, SleevePickerViewModel sleeves) : ObservableObject
{
    public DeckFile File { get; } = file;
    public Deck Deck { get; } = deck;
    public DeckPreviewViewModel Preview { get; } = new(deck);
    public SleevePickerViewModel Sleeves { get; } = sleeves;
    public string Name => Deck.Name;
    public bool IsAi => File.Ai;
    public bool IsOwn => !File.Ai;
    public bool HasDate => Deck.Date.Trim().Length > 0;
    public string SavedText => $"Saved {Deck.Date.Trim()}";
    public string AiSleeve => File.Sleeve.Trim().Length == 0 ? "None" : Sleeve.DisplayName(File.Sleeve.Trim());
}

public sealed partial class DecksViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private IReadOnlyList<Serie> _series = [];
    /// <summary>Deck files of the Decks folder; cards are only matched when a deck is opened.</summary>
    private List<DeckFile> _decks = [];

    public DecksViewModel(MainViewModel main) => _main = main;

    public void SetSeries(IReadOnlyList<Serie> series)
    {
        _series = series;
        Editor = null;
        Picked = null;
    }

    public ObservableCollection<DeckItem> OwnDecks { get; } = [];
    public ObservableCollection<DeckItem> AiDecks { get; } = [];
    public bool HasAiDecks => AiDecks.Count > 0;
    public bool HasDecks => _decks.Count > 0;
    public bool NoDecks => _decks.Count == 0;
    public bool ShowSearch => _decks.Count > 8;
    public string OwnCount => _decks.Count(d => !d.Ai).ToString();
    public string EmptyText => $"No decks yet.\nDecks saved in the simulator appear here.\n\n{_main.DecksPath}";

    /// <summary>The deck list's search field.</summary>
    [ObservableProperty]
    private string _query = "";

    partial void OnQueryChanged(string value) => RefreshList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel), nameof(SortTip))]
    private DeckSort _sort;

    public string SortLabel => Sort.ToString();
    public string SortTip => $"Sort by {(Sort == DeckSort.Name ? "date" : "name")}";

    partial void OnSortChanged(DeckSort value) => RefreshList();

    [RelayCommand]
    private void ToggleSort()
    {
        Sort = Sort == DeckSort.Name ? DeckSort.Date : DeckSort.Name;
        _main.SaveSetting(Settings.DeckSort, Sort.ToString().ToLowerInvariant());
    }

    /// <summary>The selected item of the lists (bound both ways: a click opens the deck).</summary>
    [ObservableProperty]
    private DeckItem? _selectedOwn;

    [ObservableProperty]
    private DeckItem? _selectedAi;

    private bool _selecting;

    partial void OnSelectedOwnChanged(DeckItem? value)
    {
        if (value is null || _selecting) return;
        Pick(value.File.Path);
    }

    partial void OnSelectedAiChanged(DeckItem? value)
    {
        if (value is null || _selecting) return;
        Pick(value.File.Path);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicked), nameof(NothingPicked))]
    private PickedDeckViewModel? _picked;

    public bool HasPicked => Picked is not null;
    public bool NothingPicked => Picked is null;

    /// <summary>The deck being built or edited. While there is one, the screen shows the editor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing), nameof(IsBrowsing))]
    private DeckEditorViewModel? _editor;

    public bool IsEditing => Editor is not null;
    public bool IsBrowsing => Editor is null;

    /// <summary>Whether a deck is being built, with changes not saved yet.</summary>
    public bool HasUnsavedDeck => Editor is { Dirty: true };

    /// <summary>Re-reads the deck files. The open deck stays open if its file still exists.</summary>
    public void Reload()
    {
        try
        {
            _decks = DeckFiles.List(_main.DecksPath);
        }
        catch (Exception e)
        {
            _main.Notify(Notice.FromException("Could not read the simulator decks", e));
            return;
        }
        RefreshList();
        if (Picked is not null && _decks.All(d => d.Path != Picked.File.Path)) Picked = null;
    }

    /// <summary>Sorts the files and fills the two lists, keeping the open deck selected.</summary>
    private void RefreshList()
    {
        var sorted = _decks.OrderBy(d => d.Ai).ThenBy(d => d.Name.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        if (Sort == DeckSort.Date)
            // Stable: decks saved at the same time stay sorted by name; no date last.
            sorted = sorted.OrderBy(d => d.Ai).ThenByDescending(d => d.SavedAt ?? DateTime.MinValue).ToList();
        var query = Query.Trim();
        _selecting = true;
        OwnDecks.Clear();
        AiDecks.Clear();
        foreach (var deck in sorted.Where(d => query.Length == 0 || d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            (deck.Ai ? AiDecks : OwnDecks).Add(new DeckItem(deck));
        SelectedOwn = OwnDecks.FirstOrDefault(d => d.File.Path == Picked?.File.Path);
        SelectedAi = AiDecks.FirstOrDefault(d => d.File.Path == Picked?.File.Path);
        _selecting = false;
        foreach (var name in new[] { nameof(HasAiDecks), nameof(HasDecks), nameof(NoDecks), nameof(ShowSearch), nameof(OwnCount), nameof(EmptyText) })
            OnPropertyChanged(name);
    }

    /// <summary>Opens the deck file at <paramref name="path"/>, matching its codes to the loaded cards.</summary>
    private void Pick(string path)
    {
        var file = _decks.FirstOrDefault(d => d.Path == path);
        if (file is null) return;
        var deck = new CardIndex(_series).ResolveDeck(file.Name, file.Date, file.Codes);
        Open(file, deck);
    }

    private void Open(DeckFile file, Deck deck)
    {
        var sleeves = new SleevePickerViewModel(() => _main.SleevesPath, ApplySleeve) { Current = file.Sleeve };
        Picked = new PickedDeckViewModel(file, deck, sleeves);
        _selecting = true;
        SelectedOwn = OwnDecks.FirstOrDefault(d => d.File.Path == file.Path);
        SelectedAi = AiDecks.FirstOrDefault(d => d.File.Path == file.Path);
        _selecting = false;
    }

    /// <summary>Re-reads the deck files and opens the one at <paramref name="path"/>.</summary>
    private void ReloadAndPick(string path)
    {
        Reload();
        Pick(path);
    }

    /// <summary>Writes the open deck's file with a new sleeve, keeping its name, date and cards.</summary>
    private void ApplySleeve(string sleeve)
    {
        if (Picked is not { File: var file }) return;
        try
        {
            var saved = DeckFiles.Update(file.Path, file.Name, file.Date, sleeve, file.Codes);
            ReloadAndPick(saved.Path);
            var what = sleeve.Length == 0 ? "now has no sleeve" : $"uses the sleeve {sleeve}";
            _main.Notify(new Notice.Info($"\"{saved.Name}\" {what}."));
        }
        catch (Exception e)
        {
            _main.Notify(Notice.FromException("Could not set the sleeve", e));
        }
    }

    // The deck editor.

    [RelayCommand]
    private void NewDeck() => OpenEditor("", null, new DeckList(), "");

    [RelayCommand]
    private void Edit()
    {
        if (Picked is { } p) OpenEditor(p.Deck.Name, p.File.Path, new DeckList(p.Deck.Cards, [.. p.Deck.Unmatched]), p.File.Sleeve);
    }

    /// <summary>Starts a new deck from a copy of the open deck.</summary>
    [RelayCommand]
    private void Duplicate()
    {
        if (Picked is { } p) OpenEditor($"{p.Deck.Name} (copy)", null, new DeckList(p.Deck.Cards, [.. p.Deck.Unmatched]), p.File.Sleeve);
    }

    private void OpenEditor(string name, string? target, DeckList list, string sleeve)
    {
        // Names of the other decks, so the editor can warn about duplicates.
        var taken = _decks.Where(d => d.Path != target).Select(d => d.Name).ToList();
        Editor = new DeckEditorViewModel(name, target, sleeve, () => _main.SleevesPath, list, taken, _series, SaveEditor, CancelEditor);
    }

    /// <summary>Writes the edited deck: a new file, or the file being edited.</summary>
    private void SaveEditor(DeckEditorViewModel editor)
    {
        var codes = editor.Codes;
        var date = DeckFiles.NowDate();
        var sleeve = editor.Sleeve.Trim();
        try
        {
            var saved = editor.Target is { } path
                ? DeckFiles.Update(path, editor.Name, date, sleeve, codes)
                : DeckFiles.SaveNew(_main.DecksPath, editor.Name.Trim(), date, sleeve, codes);
            Editor = null;
            ReloadAndPick(saved.Path);
            _main.Notify(new Notice.Info($"Saved \"{saved.Name}\" ({codes.Count} cards) to the simulator decks."));
        }
        catch (Exception e)
        {
            editor.Error = $"Could not save: {e.Message}";
        }
    }

    /// <summary>Cancel in the editor: asks first when there are unsaved changes.</summary>
    private void CancelEditor(DeckEditorViewModel editor)
    {
        if (!editor.Dirty)
        {
            Editor = null;
            return;
        }
        var what = editor.Name.Trim().Length == 0 ? "This deck" : $"\"{editor.Name.Trim()}\"";
        _main.ShowDialog(new ConfirmDialogViewModel("Discard changes?", $"{what} has changes that are not saved.", "Discard",
            () => Editor = null));
    }

    // Dialogs.

    [RelayCommand]
    private void Import() => _main.ShowDialog(new ImportDialogViewModel(ImportLink));

    /// <summary>Imports the deck at <paramref name="link"/>, then opens it and tells the user.</summary>
    private async Task ImportLink(string link)
    {
        var (series, dir) = (_series, _main.DecksPath);
        Deck deck;
        try
        {
            deck = await Task.Run(() => Decks.ImportAsync(link, series, dir));
        }
        catch (Exception e)
        {
            _main.Notify(Notice.FromException("Import failed", e));
            return;
        }
        Reload();
        if (_decks.FirstOrDefault(d => !d.Ai && d.Name == deck.Name) is { } file) Open(file, deck);
        _main.Notify(new Notice.Info(ImportDialogViewModel.Message(deck)));
    }

    [RelayCommand]
    private void Upload()
    {
        if (Picked is { } p) _main.ShowDialog(new UploadDialogViewModel(_main, p.Deck.Name, p.File.Codes));
    }

    [RelayCommand]
    private void PrintProxies() => Print(Printout.Proxies);

    [RelayCommand]
    private void PrintTranslation() => Print(Printout.Translation);

    private void Print(Printout printout)
    {
        if (Picked is { } p) _main.ShowDialog(new ExportDialogViewModel(_main, printout, p.Deck, ExportDialogViewModel.LastPaper));
    }
}
