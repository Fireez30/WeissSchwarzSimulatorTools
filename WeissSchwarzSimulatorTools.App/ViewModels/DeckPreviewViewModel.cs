// The cards of a deck as an open deck shows them: composition tiles, then the card groups, with
// a detail pane that follows hover and clicks. Shared by the simulator decks and the Encore
// Decks page.

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeissSchwarzSimulatorTools.App.Controls;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>A distinct card of an open deck, with its copies.</summary>
public sealed partial class DeckCardViewModel(Card card, int copies, DeckPreviewViewModel preview) : ObservableObject
{
    public Card Card { get; } = card;
    public int Copies { get; } = copies;
    /// <summary>The copies badge shows from 2 copies.</summary>
    public int Badge => Copies > 1 ? Copies : 0;
    public bool Selected => preview.Pane.PinnedKey == Card.Key;

    public void Refresh() => OnPropertyChanged(nameof(Selected));
}

public sealed record DeckGroupViewModel(string Title, int Count, IReadOnlyList<DeckCardViewModel> Cards);

public sealed record LevelBar(string Label, int Count, double Height, string Tip);

public sealed record ColorShare(string Label, IBrush Brush, double Width);

/// <summary>Deck cards grouped by type, in deck order, with composition counts.</summary>
public sealed partial class DeckPreviewViewModel : ObservableObject
{
    private readonly List<DeckCardViewModel> _cards = [];

    public DetailPaneViewModel Pane { get; } = new();
    public IReadOnlyList<DeckGroupViewModel> Groups { get; }
    public IReadOnlyList<string> Unmatched { get; }
    public bool HasUnmatched => Unmatched.Count > 0;
    public string UnmatchedText => $"{Unmatched.Count} code(s) matched no card in the loaded simulator: {string.Join(", ", Unmatched)}";

    public int Total { get; }
    public string TotalText => $"{Total} / {DeckList.DeckSize}";
    public bool IsLegal { get; }
    public string LegalText { get; }
    public int Characters { get; }
    public int Events { get; }
    public int Climaxes { get; }
    public int Triggers { get; }
    public int NonTriggers => Total - Triggers;
    public string TriggerBreakdown { get; }
    public IReadOnlyList<LevelBar> LevelBars { get; }
    public IReadOnlyList<ColorShare> Colors { get; }

    public DeckPreviewViewModel(Deck deck)
    {
        Unmatched = deck.Unmatched;
        var sorted = deck.Cards.ToList();
        CardSorting.SortForDeck(sorted);
        Total = sorted.Count;
        var byType = sorted.GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.Count());
        Characters = byType.GetValueOrDefault(CardType.Character);
        Events = byType.GetValueOrDefault(CardType.Event);
        Climaxes = byType.GetValueOrDefault(CardType.Climax);
        IsLegal = Total == DeckList.DeckSize && Climaxes <= DeckList.MaxClimaxes;
        LegalText = IsLegal ? "Legal size"
            : Climaxes > DeckList.MaxClimaxes ? $"{Climaxes} climaxes (max {DeckList.MaxClimaxes})"
            : $"Needs exactly {DeckList.DeckSize}";
        Triggers = sorted.Count(c => c.Triggers.Count > 0);
        TriggerBreakdown = string.Join("\n", sorted.SelectMany(c => c.Triggers).GroupBy(t => t).OrderBy(g => g.Key)
            .Select(g => $"{g.Key.Label()}: {g.Count()}"));

        var byLevel = new int[4];
        foreach (var card in sorted.Where(c => c.Type != CardType.Climax)) byLevel[Math.Min((int)card.Level, 3)]++;
        var max = Math.Max(1, byLevel.Max());
        LevelBars = byLevel.Select((n, level) => new LevelBar($"L{level}", n, 32.0 * n / max, $"Level {level}: {n} cards")).ToList();
        Colors = sorted.GroupBy(c => c.Color).OrderBy(g => g.Key)
            .Select(g => new ColorShare($"{g.Key.Label()} {g.Count()}", CardColors.Brush(g.Key), 200.0 * g.Count() / Math.Max(1, Total)))
            .ToList();

        var groups = new List<DeckGroupViewModel>();
        foreach (var byKind in sorted.GroupBy(c => c.Type))
        {
            var cards = byKind.GroupBy(c => c.Key).Select(g => new DeckCardViewModel(g.First(), g.Count(), this)).ToList();
            _cards.AddRange(cards);
            groups.Add(new DeckGroupViewModel(byKind.Key.PluralLabel(), byKind.Count(), cards));
        }
        Groups = groups;
        Pane.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DetailPaneViewModel.Pinned) || e.PropertyName == nameof(DetailPaneViewModel.Card))
                foreach (var card in _cards) card.Refresh();
        };
    }

    [RelayCommand]
    private void Hover(Card card) => Pane.Hover(card);

    [RelayCommand]
    private void Click(Card card) => Pane.Click(card);
}
