// The deck editor: the deck being built or edited, in a panel next to the card browser.
//
// The editor is the browser's picker (IPicker), so clicking a card in the browser adds it here.
// Saving is done by the Decks screen, which owns the deck files.

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeissSchwarzSimulatorTools.Simulator;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>A distinct card of the deck being built, with its copies.</summary>
public sealed partial class EditorCardViewModel(Card card, int copies, DeckEditorViewModel editor)
{
    public Card Card { get; } = card;
    public int Copies { get; } = copies;

    public string Stats => Card.Type == CardType.Climax ? Card.Key : $"{Card.Key} · {Card.Type.Label()} · C{Card.Cost}";

    [RelayCommand]
    private void Add() => editor.Add(Card);

    [RelayCommand]
    private void Remove() => editor.Remove(Card);
}

public sealed record EditorGroupViewModel(string Title, int Count, IReadOnlyList<EditorCardViewModel> Cards);

public sealed partial class DeckEditorViewModel : ObservableObject, IPicker
{
    private readonly DeckList _list;
    /// <summary>Names of the other decks of a folder, to warn about duplicates.</summary>
    private readonly Func<string, IReadOnlyList<string>> _takenNames;
    /// <summary>Tags used before, offered as suggestions.</summary>
    private readonly IReadOnlyList<string> _knownTags;
    private readonly Action<DeckEditorViewModel> _save;
    private readonly Action<DeckEditorViewModel> _cancel;

    /// <summary>Deck file being edited; null for a new deck.</summary>
    public string? Target { get; }
    public CardBrowserViewModel Browser { get; }
    public SleevePickerViewModel SleevePicker { get; }

    /// <param name="folder">The deck's folder (see <see cref="DeckFile.Folder"/>), one of <paramref name="folders"/>.</param>
    public DeckEditorViewModel(string name, string? target, string folder, IReadOnlyList<Option<string>> folders, string sleeve,
        IReadOnlyList<string> tags, IReadOnlyList<string> knownTags, Func<string> sleevesDir, DeckList list,
        Func<string, IReadOnlyList<string>> takenNames, IReadOnlyList<Serie> series,
        Action<DeckEditorViewModel> save, Action<DeckEditorViewModel> cancel)
    {
        _name = name;
        Target = target;
        Folders = folders;
        _folder = folders.FirstOrDefault(f => f.Value == folder) ?? folders.FirstOrDefault() ?? new Option<string>("", FolderLabels.Root);
        _list = list;
        _takenNames = takenNames;
        _knownTags = knownTags;
        Tags = [.. DeckFiles.CleanTags(tags)];
        _save = save;
        _cancel = cancel;
        SleevePicker = new SleevePickerViewModel(sleevesDir, picked =>
        {
            SleevePicker!.Current = picked;
            Dirty = true;
        }) { Current = sleeve };
        Browser = new CardBrowserViewModel(Title, this);
        // Straight to the deck's serie: opening a serie builds its whole grid.
        var serie = list.Entries().FirstOrDefault() is { Card: { } first } ? CardBrowserViewModel.SerieNameOf(series, first.Key) : null;
        Browser.SetSeries(series, serie);
        Refresh();
    }

    public event Action? Changed;

    public string Title => Target is not null ? "Edit deck" : "New deck";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHint), nameof(HasNameHint), nameof(NameTaken), nameof(CanSave))]
    private string _name;

    /// <summary>The folders the deck can be saved to.</summary>
    public IReadOnlyList<Option<string>> Folders { get; }
    public bool HasFolders => Folders.Count > 1;

    /// <summary>The folder the deck is saved to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameHint), nameof(HasNameHint), nameof(NameTaken))]
    private Option<string> _folder;

    partial void OnFolderChanged(Option<string> value)
    {
        Dirty = true;
        Error = null;
    }

    partial void OnNameChanged(string value)
    {
        Dirty = true;
        Error = null;
    }

    /// <summary>Changes not saved yet.</summary>
    [ObservableProperty]
    private bool _dirty;

    /// <summary>Why the last card could not be added.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefusal))]
    private string? _refusal;

    /// <summary>The save failed; shown in the panel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    [ObservableProperty]
    private bool _isGrid;

    /// <summary>The deck's tags, at most <see cref="DeckFiles.MaxTags"/>.</summary>
    public ObservableCollection<string> Tags { get; }

    /// <summary>The tag field: Enter adds what it holds, and it filters the suggestions.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TagSuggestions), nameof(HasTagSuggestions))]
    private string _tagInput = "";

    public bool HasTags => Tags.Count > 0;
    public bool CanAddTag => Tags.Count < DeckFiles.MaxTags;
    public string TagCountText => $"{Tags.Count}/{DeckFiles.MaxTags}";
    public IReadOnlyList<string> TagSuggestions => !CanAddTag ? [] : _knownTags
        .Where(t => !Tags.Contains(t, StringComparer.OrdinalIgnoreCase) && t.Contains(TagInput.Trim(), StringComparison.OrdinalIgnoreCase))
        .Take(12)
        .ToList();
    public bool HasTagSuggestions => TagSuggestions.Count > 0;

    public bool HasRefusal => Refusal is not null;
    public bool HasError => Error is not null;
    public string Sleeve => SleevePicker.Current;
    public IReadOnlyList<string> Codes => _list.Codes();

    public bool NameTaken => _takenNames(Folder.Value).Any(n => n.Equals(Name.Trim(), StringComparison.OrdinalIgnoreCase));
    public string? NameHint => Name.Trim().Length == 0 ? "Give the deck a name to save it."
        : !NameTaken ? null
        : Target is not null ? "Another deck of this folder already has this name."
        : $"A deck of this folder has this name: it will be saved as \"{Name.Trim()} (2)\".";
    public bool HasNameHint => NameHint is not null;

    public int Total => _list.Total;
    public string TotalText => $"/ {DeckList.DeckSize}";
    public double Progress => Math.Min(1.0, (double)Total / DeckList.DeckSize);
    public bool IsComplete => Total == DeckList.DeckSize;
    public string TypeCounts => $"Char {_list.Count(CardType.Character)} · Event {_list.Count(CardType.Event)} ·";
    public string ClimaxCount => $"CX {_list.Count(CardType.Climax)}/{DeckList.MaxClimaxes}";
    public bool TooManyClimaxes => _list.Count(CardType.Climax) > DeckList.MaxClimaxes;
    public string DistinctText => $"{_list.Entries().Count} different cards";
    public bool IsEmpty => _list.IsEmpty;
    public bool HasUnmatched => _list.Unmatched.Count > 0;
    public string UnmatchedText => $"{_list.Unmatched.Count} code(s) match no loaded card and are kept as they are: {string.Join(", ", _list.Unmatched)}";
    public bool CanSave => Name.Trim().Length > 0 && !_list.IsEmpty;
    public string? SaveTip => CanSave && Total != DeckList.DeckSize
        ? $"The deck has {Total} cards; the simulator plays {DeckList.DeckSize}-card decks."
        : null;

    public IReadOnlyList<EditorGroupViewModel> Groups { get; private set; } = [];

    private void Refresh()
    {
        Groups = _list.ByLevel()
            .Select(g => new EditorGroupViewModel(g.Group.Label, g.Cards.Sum(e => e.Copies),
                g.Cards.Select(e => new EditorCardViewModel(e.Card, e.Copies, this)).ToList()))
            .ToList();
        foreach (var name in new[] { nameof(Groups), nameof(Total), nameof(Progress), nameof(IsComplete), nameof(TypeCounts), nameof(ClimaxCount),
                     nameof(TooManyClimaxes), nameof(DistinctText), nameof(IsEmpty), nameof(HasUnmatched), nameof(UnmatchedText), nameof(CanSave), nameof(SaveTip) })
            OnPropertyChanged(name);
        Changed?.Invoke();
    }

    public int Copies(Card card) => _list.Copies(card.Key);

    public void Add(Card card)
    {
        try
        {
            _list.Add(card);
            Dirty = true;
            Refusal = null;
            Refresh();
        }
        catch (AddCardException e)
        {
            Refusal = e.Message;
        }
    }

    public void Remove(Card card)
    {
        if (!_list.Remove(card.Key)) return;
        Dirty = true;
        Refusal = null;
        Refresh();
    }

    /// <summary>A card under the pointer in the deck panel: the detail pane shows it.</summary>
    [RelayCommand]
    private void Hover(Card card) => Browser.Pane.Hover(card);

    [RelayCommand]
    private void AddCard(Card card) => Add(card);

    [RelayCommand]
    private void RemoveCard(Card card) => Remove(card);

    [RelayCommand]
    private void ClearCards()
    {
        if (_list.IsEmpty) return;
        _list.Clear();
        Dirty = true;
        Refresh();
    }

    /// <summary>Adds <paramref name="tag"/>, or the tag field's text when it is null.</summary>
    [RelayCommand]
    private void AddTag(string? tag)
    {
        var cleaned = DeckFiles.CleanTag(tag ?? TagInput);
        if (cleaned.Length == 0 || !CanAddTag) return;
        TagInput = "";
        if (Tags.Contains(cleaned, StringComparer.OrdinalIgnoreCase)) return;
        Tags.Add(cleaned);
        TagsChanged();
    }

    [RelayCommand]
    private void RemoveTag(string tag)
    {
        if (Tags.Remove(tag)) TagsChanged();
    }

    private void TagsChanged()
    {
        Dirty = true;
        foreach (var name in new[] { nameof(HasTags), nameof(CanAddTag), nameof(TagCountText), nameof(TagSuggestions), nameof(HasTagSuggestions) })
            OnPropertyChanged(name);
    }

    [RelayCommand]
    private void ShowGrid() => IsGrid = true;

    [RelayCommand]
    private void ShowList() => IsGrid = false;

    [RelayCommand]
    private void Save() => _save(this);

    [RelayCommand]
    private void Cancel() => _cancel(this);
}
