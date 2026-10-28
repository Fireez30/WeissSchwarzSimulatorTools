using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeissSchwarzSimulatorTools.App.Controls;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>A filter chip: a value of a filter group, picked or not.</summary>
public sealed partial class ChipViewModel(string label, IBrush? dot, Func<bool> picked, Action click) : ObservableObject
{
    public string Label { get; } = label;
    public IBrush? Dot { get; } = dot;
    public bool HasDot => Dot is not null;
    public bool IsPicked => picked();

    [ObservableProperty]
    private bool _isVisible = true;

    [RelayCommand]
    private void Click() => click();

    public void Refresh() => OnPropertyChanged(nameof(IsPicked));
}

/// <summary>A group of filter chips (Type, Color…).</summary>
public sealed class FilterGroupViewModel(string title, List<ChipViewModel> chips)
{
    public string Title { get; } = title;
    public List<ChipViewModel> Chips { get; } = chips;
    public string Count => Chips.Count.ToString();
}

/// <summary>A card shown in the grid or the list: with its copies in the deck being built.</summary>
public sealed partial class CardTileViewModel(Card card, CardBrowserViewModel browser) : ObservableObject
{
    public Card Card { get; } = card;
    public int Copies => browser.Picker?.Copies(Card) ?? 0;
    public bool HasCopies => Copies > 0;
    public bool Selected => Copies > 0 || (browser.Picker is null && browser.Pane.PinnedKey == Card.Key);
    public bool IsPicking => browser.Picker is not null;
    public double Width => browser.ThumbWidth;
    public double Height => browser.ThumbWidth * CardImage.Ratio;
    public int DecodeWidth => (int)Math.Ceiling(browser.ThumbWidth * 1.5 / 40) * 40;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Copies));
        OnPropertyChanged(nameof(HasCopies));
        OnPropertyChanged(nameof(Selected));
    }

    public void Resize()
    {
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(DecodeWidth));
    }

    [RelayCommand]
    private void Add() => browser.Picker?.Add(Card);

    [RelayCommand]
    private void Remove() => browser.Picker?.Remove(Card);
}

/// <summary>A row of the grid view: the grid is a virtualised list of rows.</summary>
public sealed record CardRow(IReadOnlyList<CardTileViewModel> Tiles);

/// <summary>A choice of the set picker: one set, or every set of the serie (Index null).</summary>
public sealed record SetChoice(string Name, int? Index)
{
    public override string ToString() => Name;
}

/// <summary>
/// The card browser: a serie (or one of its sets) as a grid or a list, with a search field,
/// filter chips and a pane showing the hovered card. It is the Cards screen, and the deck editor
/// reuses it with a picker: clicks then add cards to the deck.
/// </summary>
public sealed partial class CardBrowserViewModel : ObservableObject
{
    private const double GridGap = 10;
    /// <summary>Trait groups longer than this get their own filter field.</summary>
    private const int TraitSearchThreshold = 12;

    private IReadOnlyList<Serie> _series = [];
    private List<Card> _cards = [];
    private Filters _filters = new();
    private List<CardTileViewModel> _tiles = [];
    private double _viewportWidth = 800;

    public IPicker? Picker { get; }
    public DetailPaneViewModel Pane { get; }
    public string Title { get; }

    public CardBrowserViewModel(string title, IPicker? picker = null)
    {
        Title = title;
        Picker = picker;
        Pane = new DetailPaneViewModel(picker);
        Pane.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DetailPaneViewModel.Pinned) && Picker is null)
                foreach (var tile in _tiles) tile.Refresh();
        };
        if (picker is not null)
            picker.Changed += () =>
            {
                foreach (var tile in _tiles) tile.Refresh();
            };
    }

    public IReadOnlyList<Serie> Series => _series;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSerie), nameof(SerieName), nameof(Heading))]
    private Serie? _serie;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    private SetChoice? _set;

    public ObservableCollection<SetChoice> Sets { get; } = [];
    public bool HasSeveralSets => Sets.Count > 2;
    public bool HasSerie => Serie is not null;
    public string? SerieName => Serie?.Name;

    public string Heading => Set?.Index is { } i && Serie is not null ? $"{Serie.Name} · {Serie.Sets[i].Name}" : Serie?.Name ?? "";

    /// <summary>The serie picker's search.</summary>
    [ObservableProperty]
    private string _serieQuery = "";

    public ObservableCollection<Serie> SerieMatches { get; } = [];

    partial void OnSerieQueryChanged(string value) => RefreshSerieMatches();

    private void RefreshSerieMatches()
    {
        SerieMatches.Clear();
        var query = SerieQuery.Trim();
        foreach (var s in _series.Where(s => query.Length == 0 || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            SerieMatches.Add(s);
    }

    [ObservableProperty]
    private string _search = "";

    partial void OnSearchChanged(string value)
    {
        _filters.Search = value;
        Refilter();
    }

    [ObservableProperty]
    private bool _showFilters = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsList), nameof(ShowPane))]
    private bool _isGrid = true;

    public bool IsList => !IsGrid;

    /// <summary>The detail pane is shown with the grid, and always while building a deck.</summary>
    public bool ShowPane => IsGrid || Picker is not null;

    [RelayCommand]
    private void ShowGrid() => IsGrid = true;

    [RelayCommand]
    private void ShowList() => IsGrid = false;

    [ObservableProperty]
    private double _thumbWidth = 150;

    partial void OnThumbWidthChanged(double value)
    {
        foreach (var tile in _tiles) tile.Resize();
        Regroup();
    }

    public ObservableCollection<FilterGroupViewModel> FilterGroups { get; } = [];
    public FilterGroupViewModel? Traits { get; private set; }
    public bool HasTraits => Traits is not null;
    public bool HasTraitSearch => Traits?.Chips.Count > TraitSearchThreshold;

    [ObservableProperty]
    private string _traitQuery = "";

    partial void OnTraitQueryChanged(string value)
    {
        if (Traits is null) return;
        var query = value.Trim();
        foreach (var chip in Traits.Chips)
            chip.IsVisible = query.Length == 0 || chip.Label.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsFiltered => !_filters.IsUnfiltered;

    public List<CardTileViewModel> Shown { get; private set; } = [];
    public ObservableCollection<CardRow> Rows { get; } = [];
    public bool HasCards => Shown.Count > 0;
    public bool NoMatch => HasSerie && Shown.Count == 0;
    public string NoMatchText => IsFiltered ? "The search only looks at cards that pass the filters." : "Try another search.";

    public string CountText => Shown.Count == _cards.Count ? $"{_cards.Count} cards" : $"{Shown.Count} of {_cards.Count} cards";

    public string? PickHint => Picker is not null ? "· click a card to add it, right-click to remove it" : null;

    /// <summary>The series to browse (after loading or reloading the simulator).</summary>
    public void SetSeries(IReadOnlyList<Serie> series, string? openName)
    {
        _series = series;
        OnPropertyChanged(nameof(Series));
        RefreshSerieMatches();
        OpenSerie(openName);
    }

    /// <summary>Opens the serie named <paramref name="name"/> (falls back to the first one).</summary>
    public void OpenSerie(string? name)
    {
        var serie = _series.FirstOrDefault(s => s.Name == name) ?? _series.FirstOrDefault();
        if (serie is not null) Load(serie, null);
    }

    /// <summary>Opens the serie holding the card <paramref name="key"/>, if any.</summary>
    public void OpenSerieOf(string key)
    {
        var serie = _series.FirstOrDefault(s => s.Sets.Any(set => set.Cards.ContainsKey(key)));
        if (serie is not null) OpenSerie(serie.Name);
    }

    [RelayCommand]
    private void PickSerie(Serie serie)
    {
        SerieQuery = "";
        Load(serie, null);
    }

    partial void OnSetChanged(SetChoice? value)
    {
        if (value is not null && Serie is not null && !_loading) Load(Serie, value.Index);
    }

    private bool _loading;

    private void Load(Serie serie, int? set)
    {
        _loading = true;
        Serie = serie;
        if (set is { } i && i >= serie.Sets.Count) set = null;
        Sets.Clear();
        Sets.Add(new SetChoice($"All sets ({serie.Sets.Count})", null));
        for (var k = 0; k < serie.Sets.Count; k++) Sets.Add(new SetChoice(serie.Sets[k].Name, k));
        Set = Sets[set is { } index ? index + 1 : 0];
        OnPropertyChanged(nameof(HasSeveralSets));
        _loading = false;

        _cards = set is { } s ? serie.Sets[s].Cards.Values.ToList() : serie.AllCards.ToList();
        _filters = Filters.FromCards(_cards);
        _filters.Search = Search;
        BuildFilterGroups();
        TraitQuery = "";
        Pane.Clear();
        Refilter();
    }

    private void BuildFilterGroups()
    {
        FilterGroups.Clear();
        var chips = new List<ChipViewModel>();

        void Group<T>(string title, Choice<T> choice, Func<T, string> label, Func<T, IBrush?>? dot = null) where T : notnull
        {
            if (choice.Available.Count < 2) return;
            var group = choice.Available
                .Select(v => new ChipViewModel(label(v), dot?.Invoke(v), () => choice.IsPicked(v), () =>
                {
                    choice.Click(v);
                    FiltersChanged();
                }))
                .ToList();
            chips.AddRange(group);
            FilterGroups.Add(new FilterGroupViewModel(title, group));
        }

        Group("Type", _filters.Types, t => t.Label());
        Group("Color", _filters.Colors, c => c.Label(), c => CardColors.Brush(c));
        Group("Level", _filters.Levels, v => v.ToString());
        Group("Cost", _filters.Costs, v => v.ToString());
        if (_filters.HasCxCombo)
        {
            var cx = new ChipViewModel("Climax combo", null, () => _filters.CxCombo, () =>
            {
                _filters.CxCombo = !_filters.CxCombo;
                FiltersChanged();
            });
            chips.Add(cx);
            FilterGroups.Add(new FilterGroupViewModel("Keywords", [cx]));
        }
        Traits = null;
        if (_filters.Traits.Available.Count > 0)
        {
            var traits = _filters.Traits.Available
                .Select(t => new ChipViewModel(t, null, () => _filters.Traits.IsPicked(t), () =>
                {
                    _filters.Traits.Click(t);
                    FiltersChanged();
                }))
                .ToList();
            chips.AddRange(traits);
            Traits = new FilterGroupViewModel("Traits", traits);
        }
        _chips = chips;
        OnPropertyChanged(nameof(Traits));
        OnPropertyChanged(nameof(HasTraits));
        OnPropertyChanged(nameof(HasTraitSearch));
    }

    private List<ChipViewModel> _chips = [];

    private void FiltersChanged()
    {
        foreach (var chip in _chips) chip.Refresh();
        Refilter();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _filters.Clear();
        FiltersChanged();
    }

    private void Refilter()
    {
        var shown = _filters.Apply(_cards);
        CardSorting.SortForCardList(shown);
        Shown = shown.Select(c => new CardTileViewModel(c, this)).ToList();
        _tiles = Shown;
        OnPropertyChanged(nameof(Shown));
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(NoMatch));
        OnPropertyChanged(nameof(NoMatchText));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsFiltered));
        Regroup();
    }

    /// <summary>The width the grid has, from the view: it sets the number of columns.</summary>
    public void SetViewportWidth(double width)
    {
        if (Math.Abs(width - _viewportWidth) < 1) return;
        _viewportWidth = width;
        Regroup(force: false);
    }

    private int _columns;

    private void Regroup(bool force = true)
    {
        var columns = Math.Max(1, (int)Math.Floor((_viewportWidth - 12 + GridGap) / (ThumbWidth + GridGap)));
        if (!force && columns == _columns) return;
        _columns = columns;
        Rows.Clear();
        foreach (var chunk in Shown.Chunk(columns)) Rows.Add(new CardRow(chunk));
    }

    // Pointer actions on a card of the grid.

    [RelayCommand]
    private void Hover(Card card) => Pane.Hover(card);

    [RelayCommand]
    private void Click(Card card)
    {
        if (Picker is not null) Picker.Add(card);
        else Pane.Click(card);
    }

    [RelayCommand]
    private void SecondaryClick(Card card) => Picker?.Remove(card);
}
