// The Decks screen: the decks saved in the simulator on the left, as a tree of their folders, the
// open deck in the middle (its stats and cards), the hovered card on the right.
//
// The tree is where folders are managed: create, rename, move and delete them, and move decks
// between them. New decks and imports go to the current folder (the last one clicked).
//
// From here the user can also build or edit a deck (the screen then shows the deck editor
// instead), import from Encore Decks, upload to it, and write PDFs.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeissSchwarzSimulatorTools.EncoreDecks;
using WeissSchwarzSimulatorTools.Simulator;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>The order of the deck list.</summary>
public enum DeckSort
{
    Name,
    /// <summary>Most recently saved first; decks without a date last.</summary>
    Date,
}

/// <summary>A deck file of the list on the left.</summary>
/// <param name="ShowFolder">Show the deck's folder under its name (a flat list: search results, the Damage screen).</param>
public sealed record DeckItem(DeckFile File, bool ShowFolder = false)
{
    public string Name => File.Ai && File.Name.StartsWith("AI_", StringComparison.Ordinal) ? File.Name[3..] : File.Name;

    public string Supporting => File.Ai || File.Date.Trim().Length == 0
        ? $"{File.Codes.Count} cards"
        : $"{File.Codes.Count} cards · {File.Date.Trim()}";

    public bool HasTags => File.Tags.Count > 0;
    public string TagsText => string.Join(" · ", File.Tags);
    public bool HasFolder => ShowFolder && File.Folder.Length > 0;
    public string FolderText => FolderLabels.Path(File.Folder);
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
    public string FolderText => FolderLabels.Path(File.Folder);
    public IReadOnlyList<string> Tags => File.Tags;
    public bool HasTags => File.Tags.Count > 0;
    public string AiSleeve => File.Sleeve.Trim().Length == 0 ? "None" : Sleeve.DisplayName(File.Sleeve.Trim());
}

public sealed partial class DecksViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private IReadOnlyList<Serie> _series = [];
    /// <summary>Deck files of the Decks folder and its subfolders; cards are only matched when a deck is opened.</summary>
    private List<DeckFile> _decks = [];
    /// <summary>Every subfolder of the Decks folder, parents before their subfolders.</summary>
    private List<string> _folders = [];
    /// <summary>The open folders of the tree.</summary>
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    public DecksViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var folder in (main.Settings.Get(Settings.DeckFoldersOpen) ?? "").Split(Settings.TagSeparator))
            if (folder.Length > 0) _expanded.Add(folder);
    }

    public void SetSeries(IReadOnlyList<Serie> series)
    {
        _series = series;
        Editor = null;
        Picked = null;
    }

    private string Root => _main.DecksPath;

    /// <summary>The folders and decks of the tree, as rows; flat search results while searching.</summary>
    public ObservableCollection<DeckTreeRow> Rows { get; } = [];
    public ObservableCollection<DeckItem> AiDecks { get; } = [];
    public bool HasAiDecks => AiDecks.Count > 0;
    public bool HasDecks => _decks.Count > 0 || _folders.Count > 0;
    public bool NoDecks => !HasDecks;
    public bool ShowSearch => _decks.Count > 8;
    public bool NoMatch => Query.Trim().Length > 0 && Rows.Count == 0 && AiDecks.Count == 0;
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

    /// <summary>
    /// The folder new decks, imports and new folders go to: the last folder clicked, or the
    /// folder of the deck opened last. "" is the Decks folder itself.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentFolderText), nameof(Crumbs), nameof(NewFolderTip), nameof(ImportTip))]
    private string _currentFolder = "";

    partial void OnCurrentFolderChanged(string value) => RefreshList();

    public string CurrentFolderText => FolderLabels.Path(CurrentFolder);
    public string NewFolderTip => $"New folder in {CurrentFolderText}";
    public string ImportTip => "Import a deck from Encore Decks into a folder of the simulator's decks";

    /// <summary>The current folder's path, a segment per folder.</summary>
    public IReadOnlyList<FolderCrumb> Crumbs
    {
        get
        {
            var chain = FolderLabels.Chain(CurrentFolder).ToList();
            return chain.Select((f, i) => new FolderCrumb(f, i == chain.Count - 1, new RelayCommand(() => CurrentFolder = f))).ToList();
        }
    }

    /// <summary>The selected item of the AI list (bound both ways: a click opens the deck).</summary>
    [ObservableProperty]
    private DeckItem? _selectedAi;

    private bool _selecting;

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

    /// <summary>Re-reads the deck files and folders. The open deck stays open if its file still exists.</summary>
    public void Reload()
    {
        try
        {
            _decks = DeckFiles.List(Root);
            _folders = DeckFiles.Folders(Root);
        }
        catch (Exception e)
        {
            _main.Notify(Notice.FromException("Could not read the simulator decks", e));
            return;
        }
        if (Picked is not null && _decks.All(d => d.Path != Picked.File.Path)) Picked = null;
        if (CurrentFolder.Length > 0 && !_folders.Contains(CurrentFolder)) CurrentFolder = "";
        RefreshList();
    }

    /// <summary>The decks in the order of the list.</summary>
    private List<DeckFile> Sorted()
    {
        var sorted = _decks.OrderBy(d => d.Ai).ThenBy(d => d.Name.ToLowerInvariant(), StringComparer.Ordinal).ToList();
        if (Sort == DeckSort.Date)
            // Stable: decks saved at the same time stay sorted by name; no date last.
            sorted = sorted.OrderBy(d => d.Ai).ThenByDescending(d => d.SavedAt ?? DateTime.MinValue).ToList();
        return sorted;
    }

    /// <summary>Sorts the files and fills the tree and the AI list, keeping the open deck selected.</summary>
    private void RefreshList()
    {
        var sorted = Sorted();
        var query = Query.Trim();
        _selecting = true;
        Rows.Clear();
        AiDecks.Clear();
        bool Matches(DeckFile d) => query.Length == 0 || d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || d.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
        var own = sorted.Where(d => !d.Ai).ToList();
        if (query.Length > 0)
        {
            // Search results: a flat list, each deck with its folder.
            foreach (var deck in own.Where(Matches))
                Rows.Add(MakeDeckRow(deck, 0, showFolder: true));
        }
        else
            AddFolderRows("", 0, own);
        foreach (var deck in sorted.Where(d => d.Ai && Matches(d)))
            AiDecks.Add(new DeckItem(deck, ShowFolder: true));
        SelectedAi = AiDecks.FirstOrDefault(d => d.File.Path == Picked?.File.Path);
        _selecting = false;
        foreach (var name in new[] { nameof(HasAiDecks), nameof(HasDecks), nameof(NoDecks), nameof(ShowSearch), nameof(NoMatch), nameof(OwnCount), nameof(EmptyText) })
            OnPropertyChanged(name);
    }

    /// <summary>The rows inside <paramref name="parent"/>: its subfolders first (with what is in the open ones), then its decks.</summary>
    private void AddFolderRows(string parent, int depth, List<DeckFile> own)
    {
        var subfolders = _folders.Where(f => DeckFiles.ParentFolder(f) == parent).ToList();
        var decks = own.Where(d => d.Folder == parent).ToList();
        if (depth > 0 && subfolders.Count == 0 && decks.Count == 0)
        {
            Rows.Add(new EmptyFolderRow(depth));
            return;
        }
        foreach (var folder in subfolders)
        {
            var expanded = _expanded.Contains(folder);
            Rows.Add(new FolderRow(folder, depth, own.Count(d => DeckFiles.IsInFolder(d.Folder, folder)), expanded, folder == CurrentFolder)
            {
                Toggle = new RelayCommand(() => ToggleFolder(folder)),
                NewFolder = new RelayCommand(() => NewFolderIn(folder)),
                Rename = new RelayCommand(() => RenameFolder(folder)),
                Move = new RelayCommand(() => MoveFolder(folder)),
                Delete = new RelayCommand(() => DeleteFolder(folder)),
            });
            if (expanded) AddFolderRows(folder, depth + 1, own);
        }
        foreach (var deck in decks)
            Rows.Add(MakeDeckRow(deck, depth, showFolder: false));
    }

    private DeckRow MakeDeckRow(DeckFile deck, int depth, bool showFolder) =>
        new(new DeckItem(deck, showFolder), depth, deck.Path == Picked?.File.Path)
        {
            Open = new RelayCommand(() => Pick(deck.Path)),
            Move = new RelayCommand(() => MoveDeck(deck)),
        };

    /// <summary>A click on a folder: opens or closes it, and makes it the current folder.</summary>
    private void ToggleFolder(string folder)
    {
        if (!_expanded.Remove(folder)) _expanded.Add(folder);
        SaveExpanded();
        if (CurrentFolder == folder) RefreshList();
        else CurrentFolder = folder;
    }

    /// <summary>Opens <paramref name="folder"/> and every folder above it.</summary>
    private void Reveal(string folder)
    {
        foreach (var f in FolderLabels.Chain(folder).Where(f => f.Length > 0)) _expanded.Add(f);
        SaveExpanded();
    }

    private void SaveExpanded()
    {
        // Folders holding the separator are not remembered.
        var open = _expanded.Where(f => !f.Contains(Settings.TagSeparator)).Order(StringComparer.Ordinal);
        _main.SaveSetting(Settings.DeckFoldersOpen, string.Join(Settings.TagSeparator, open));
    }

    /// <summary>Opens the deck file at <paramref name="path"/>, matching its codes to the loaded cards.</summary>
    private void Pick(string path)
    {
        var file = _decks.FirstOrDefault(d => d.Path == path);
        if (file is null) return;
        var deck = _main.CardIndex.ResolveDeck(file.Name, file.Date, file.Codes);
        Open(file, deck);
    }

    private void Open(DeckFile file, Deck deck)
    {
        var sleeves = new SleevePickerViewModel(() => _main.SleevesPath, ApplySleeve) { Current = file.Sleeve };
        Picked = new PickedDeckViewModel(file, deck, sleeves);
        if (!file.Ai)
        {
            Reveal(file.Folder);
            if (CurrentFolder != file.Folder)
            {
                CurrentFolder = file.Folder;
                return;
            }
        }
        RefreshList();
    }

    /// <summary>Opens the first deck of the tree (for the screenshots).</summary>
    internal void OpenFirst()
    {
        if (Rows.OfType<DeckRow>().FirstOrDefault() is { } row) Pick(row.Item.File.Path);
        else if (_decks.FirstOrDefault(d => !d.Ai) is { } deck) Pick(deck.Path);
    }

    /// <summary>Re-reads the deck files and opens the one at <paramref name="path"/>.</summary>
    private void ReloadAndPick(string path)
    {
        Reload();
        Pick(path);
    }

    /// <summary>Writes the open deck's file with a new sleeve, keeping its name, date, tags and cards.</summary>
    private void ApplySleeve(string sleeve)
    {
        if (Picked is not { File: var file }) return;
        try
        {
            var saved = DeckFiles.Update(file.Path, file.Name, file.Date, sleeve, file.Codes, file.Tags);
            ReloadAndPick(saved.Path);
            var what = sleeve.Length == 0 ? "now has no sleeve" : $"uses the sleeve {sleeve}";
            _main.Notify(new Notice.Info($"\"{saved.Name}\" {what}."));
        }
        catch (Exception e)
        {
            _main.Notify(Notice.FromException("Could not set the sleeve", e));
        }
    }

    // Folders.

    /// <summary>Names of the decks directly in <paramref name="folder"/>.</summary>
    private List<string> NamesIn(string folder, string? except = null) =>
        _decks.Where(d => d.Folder == folder && d.Path != except).Select(d => d.Name).ToList();

    /// <summary>Why <paramref name="name"/> cannot name a new folder of <paramref name="parent"/>.</summary>
    private string? FolderProblem(string parent, string name, string? except = null)
    {
        if (DeckFiles.FolderNameProblem(name) is { } problem) return problem;
        var taken = _folders.Any(f => f != except && DeckFiles.ParentFolder(f) == parent
            && DeckFiles.FolderName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        return taken ? $"{FolderLabels.Path(parent)} already has a folder named \"{name}\"." : null;
    }

    [RelayCommand]
    private void NewFolder() => NewFolderIn(CurrentFolder);

    private void NewFolderIn(string parent) =>
        _main.ShowDialog(new FolderNameDialogViewModel("New folder", $"In {FolderLabels.Path(parent)}.", "Create", "",
            name => FolderProblem(parent, name),
            name =>
            {
                var created = DeckFiles.CreateFolder(Root, parent, name);
                Reveal(parent);
                Reload();
                CurrentFolder = created;
                _main.Notify(new Notice.Info($"Created the folder {FolderLabels.Path(created)}."));
            }));

    private void RenameFolder(string folder) =>
        _main.ShowDialog(new FolderNameDialogViewModel("Rename folder", $"{FolderLabels.Path(folder)}, with the decks in it.", "Rename",
            DeckFiles.FolderName(folder),
            name => FolderProblem(DeckFiles.ParentFolder(folder), name, except: folder),
            name =>
            {
                var renamed = DeckFiles.RenameFolder(Root, folder, name);
                FolderMoved(folder, renamed);
                _main.Notify(new Notice.Info($"Renamed the folder to {FolderLabels.Path(renamed)}."));
            }));

    /// <summary>The Move dialog's folders: Decks then the tree, with those <paramref name="why"/> refuses greyed out.</summary>
    private List<FolderChoice> Choices(Func<string, string?> why) =>
        _folders.Prepend("")
            .Select(f => (Folder: f, Why: why(f)))
            .Select(c => new FolderChoice(c.Folder, c.Folder.Length == 0 ? 0 : c.Folder.Count(ch => ch == '/') + 1, c.Why is null, c.Why))
            .ToList();

    private void MoveFolder(string folder)
    {
        var name = DeckFiles.FolderName(folder);
        var choices = Choices(target =>
            DeckFiles.IsInFolder(target, folder) ? "The folder itself, or inside it"
            : DeckFiles.ParentFolder(folder) == target ? "Already there"
            : _folders.Any(f => DeckFiles.ParentFolder(f) == target && DeckFiles.FolderName(f).Equals(name, StringComparison.OrdinalIgnoreCase))
                ? $"Has a folder named \"{name}\""
                : null);
        _main.ShowDialog(new MoveDialogViewModel($"Move \"{name}\"", $"{FolderLabels.Path(folder)} moves with every deck and folder in it.", choices,
            target =>
            {
                var moved = DeckFiles.MoveFolder(Root, folder, target);
                Reveal(target);
                FolderMoved(folder, moved);
                _main.Notify(new Notice.Info($"Moved the folder to {FolderLabels.Path(moved)}."));
            }));
    }

    /// <summary>
    /// After <paramref name="from"/> became <paramref name="to"/>: the open folders, the current
    /// folder and the open deck follow it.
    /// </summary>
    private void FolderMoved(string from, string to)
    {
        string Follow(string f) => DeckFiles.IsInFolder(f, from) ? to + f[from.Length..] : f;
        var open = _expanded.Select(Follow).ToList();
        _expanded.Clear();
        _expanded.UnionWith(open);
        SaveExpanded();
        var picked = Picked is { File: var file } && DeckFiles.IsInFolder(file.Folder, from)
            ? Path.Combine(DeckFiles.FolderPath(Root, Follow(file.Folder)), Path.GetFileName(file.Path))
            : Picked?.File.Path;
        var current = Follow(CurrentFolder);
        Reload();
        CurrentFolder = _folders.Contains(current) ? current : "";
        if (picked is not null) Pick(picked);
    }

    private void DeleteFolder(string folder)
    {
        var name = DeckFiles.FolderName(folder);
        bool empty;
        try
        {
            empty = DeckFiles.IsEmptyFolder(Root, folder);
        }
        catch (Exception e)
        {
            _main.Notify(Notice.FromException("Could not delete the folder", e));
            return;
        }
        if (!empty)
        {
            var decks = _decks.Count(d => DeckFiles.IsInFolder(d.Folder, folder));
            var what = decks > 0 ? $"{decks} deck(s)" : "files";
            _main.Notify(new Notice.Error("Could not delete the folder",
                $"\"{name}\" holds {what}. Decks are never deleted with their folder: move them to another folder first."));
            return;
        }
        _main.ShowDialog(new ConfirmDialogViewModel($"Delete \"{name}\"?", $"{FolderLabels.Path(folder)} is empty; it is removed from the simulator's Decks folder.",
            "Delete", () =>
            {
                try
                {
                    DeckFiles.DeleteFolder(Root, folder);
                    _expanded.RemoveWhere(f => DeckFiles.IsInFolder(f, folder));
                    SaveExpanded();
                    if (DeckFiles.IsInFolder(CurrentFolder, folder)) CurrentFolder = DeckFiles.ParentFolder(folder);
                    Reload();
                    _main.Notify(new Notice.Info($"Deleted the folder {FolderLabels.Path(folder)}."));
                }
                catch (Exception e)
                {
                    _main.Notify(Notice.FromException("Could not delete the folder", e));
                }
            }));
    }

    /// <summary>Moves the open deck to another folder.</summary>
    [RelayCommand]
    private void Move()
    {
        if (Picked is { IsOwn: true, File: var file }) MoveDeck(file);
    }

    private void MoveDeck(DeckFile deck)
    {
        var fileName = Path.GetFileName(deck.Path);
        var choices = Choices(target =>
            target == deck.Folder ? "Already there"
            : _decks.Any(d => d.Folder == target && d.Name.Equals(deck.Name, StringComparison.OrdinalIgnoreCase)) ? "Has a deck of this name"
            : _decks.Any(d => d.Folder == target && Path.GetFileName(d.Path).Equals(fileName, StringComparison.OrdinalIgnoreCase)) ? "Has a deck file of this name"
            : null);
        _main.ShowDialog(new MoveDialogViewModel($"Move \"{deck.Name}\"", $"The deck is in {FolderLabels.Path(deck.Folder)}.", choices,
            target =>
            {
                var moved = DeckFiles.MoveDeck(Root, deck.Path, target);
                Reveal(target);
                Reload();
                if (Picked?.File.Path == deck.Path || Picked is null) Pick(moved.Path);
                _main.Notify(new Notice.Info($"Moved \"{moved.Name}\" to {FolderLabels.Path(target)}."));
            }));
    }

    // The deck editor.

    [RelayCommand]
    private void NewDeck() => OpenEditor("", null, CurrentFolder, new DeckList(), "", []);

    [RelayCommand]
    private void Edit()
    {
        if (Picked is { } p)
            OpenEditor(p.Deck.Name, p.File.Path, p.File.Folder, new DeckList(p.Deck.Cards, [.. p.Deck.Unmatched]), p.File.Sleeve, p.File.Tags);
    }

    /// <summary>Starts a new deck from a copy of the open deck, in the same folder (an AI deck's copy goes to the current folder).</summary>
    [RelayCommand]
    private void Duplicate()
    {
        if (Picked is { } p)
            OpenEditor($"{p.Deck.Name} (copy)", null, p.IsAi ? CurrentFolder : p.File.Folder, new DeckList(p.Deck.Cards, [.. p.Deck.Unmatched]),
                p.File.Sleeve, p.File.Tags);
    }

    private void OpenEditor(string name, string? target, string folder, DeckList list, string sleeve, IReadOnlyList<string> tags)
    {
        var folders = _folders.Prepend("").Select(f => new Option<string>(f, FolderLabels.Path(f))).ToList();
        // Names of the other decks of a folder, so the editor can warn about duplicates.
        Editor = new DeckEditorViewModel(name, target, folder, folders, sleeve, tags, KnownTags(), () => _main.SleevesPath, list,
            f => NamesIn(f, except: target), _series, SaveEditor, CancelEditor);
    }

    /// <summary>Tags remembered in the settings, then those of the deck files, without duplicates (ignoring case).</summary>
    private List<string> KnownTags()
    {
        var remembered = (_main.Settings.Get(Settings.DeckTags) ?? "").Split(Settings.TagSeparator);
        return remembered.Concat(_decks.Where(d => !d.Ai).SelectMany(d => d.Tags))
            .Select(DeckFiles.CleanTag)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Remembers <paramref name="tags"/> in the settings, so the editor offers them again.</summary>
    private void RememberTags(IEnumerable<string> tags)
    {
        // Tags holding the separator are not remembered; they are still found in their deck files.
        var all = KnownTags().Concat(tags)
            .Where(t => !t.Contains(Settings.TagSeparator))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase);
        _main.SaveSetting(Settings.DeckTags, string.Join(Settings.TagSeparator, all));
    }

    /// <summary>Writes the edited deck: a new file, or the file being edited (moved when its folder changed).</summary>
    private void SaveEditor(DeckEditorViewModel editor)
    {
        var codes = editor.Codes;
        var date = DeckFiles.NowDate();
        var sleeve = editor.Sleeve.Trim();
        var tags = editor.Tags.ToList();
        var folder = editor.Folder.Value;
        try
        {
            DeckFile saved;
            if (editor.Target is { } path)
            {
                var moving = _decks.FirstOrDefault(d => d.Path == path) is { } old && old.Folder != folder;
                if (moving && editor.NameTaken) throw new UserException($"{FolderLabels.Path(folder)} already has a deck named \"{editor.Name.Trim()}\"");
                saved = DeckFiles.Update(path, editor.Name, date, sleeve, codes, tags);
                if (moving) saved = DeckFiles.MoveDeck(Root, saved.Path, folder);
            }
            else
                saved = DeckFiles.SaveNew(DeckFiles.FolderPath(Root, folder), editor.Name.Trim(), date, sleeve, codes, tags);
            RememberTags(tags);
            Editor = null;
            ReloadAndPick(saved.Path);
            _main.Notify(new Notice.Info($"Saved \"{saved.Name}\" ({codes.Count} cards) to {FolderLabels.Path(folder)}."));
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
    private void Import() =>
        _main.ShowDialog(new ImportDialogViewModel(ImportLink, FolderLabels.Choices(Root, _folders), CurrentFolder));

    /// <summary>Imports the deck at <paramref name="link"/> into <paramref name="folder"/>, then opens it and tells the user.</summary>
    private async Task ImportLink(string link, string folder)
    {
        var index = _main.CardIndex;
        Deck deck;
        try
        {
            var dir = DeckFiles.FolderPath(Root, folder);
            deck = await Task.Run(() => Decks.ImportAsync(link, index, dir));
        }
        catch (Exception e)
        {
            _main.Notify(Notice.FromException("Import failed", e));
            return;
        }
        Reload();
        if (_decks.FirstOrDefault(d => !d.Ai && d.Folder == folder && d.Name == deck.Name) is { } file) Open(file, deck);
        _main.Notify(new Notice.Info(ImportDialogViewModel.Message(deck, folder)));
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
