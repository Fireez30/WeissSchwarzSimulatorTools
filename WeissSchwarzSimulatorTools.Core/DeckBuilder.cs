// A deck being built in the deck editor: its cards with their number of copies, and the deck
// construction rules.

namespace WeissSchwarzSimulatorTools;

/// <summary>Section of a deck list while building: one per level, climaxes last.</summary>
public readonly record struct DeckGroup(bool IsClimax, uint Level) : IComparable<DeckGroup>
{
    public static DeckGroup Of(Card card) =>
        card.Type == CardType.Climax ? new DeckGroup(true, 0) : new DeckGroup(false, card.Level);

    public string Label => IsClimax ? "Climaxes" : $"Level {Level}";

    public int CompareTo(DeckGroup other) =>
        IsClimax != other.IsClimax ? IsClimax.CompareTo(other.IsClimax) : Level.CompareTo(other.Level);
}

/// <summary>Why a card could not be added. The message is what users see.</summary>
public sealed class AddCardException(string message) : Exception(message)
{
    public static AddCardException Full() => new($"The deck already has {DeckList.DeckSize} cards.");

    public static AddCardException TooManyCopies(string name) =>
        new($"A deck can hold only {DeckList.MaxCopies} cards named \"{name}\".");
}

/// <summary>A distinct card of a deck, with its number of copies.</summary>
public readonly record struct DeckEntry(Card Card, int Copies);

public sealed class DeckList
{
    /// <summary>Cards in a deck the simulator plays.</summary>
    public const int DeckSize = 50;
    /// <summary>Copies allowed of cards sharing a name.</summary>
    public const int MaxCopies = 4;
    public const int MaxClimaxes = 8;

    /// <summary>Each different card once, with its number of copies.</summary>
    private readonly List<(Card Card, int Copies)> _entries = [];

    /// <summary>Codes of an edited deck that match no loaded card; kept as they are when saving.</summary>
    public List<string> Unmatched { get; }

    public DeckList() : this([], []) { }

    public DeckList(IEnumerable<Card> cards, List<string> unmatched)
    {
        Unmatched = unmatched;
        foreach (var card in cards)
            Push(card);
    }

    private void Push(Card card)
    {
        var i = _entries.FindIndex(e => e.Card.Key == card.Key);
        if (i >= 0) _entries[i] = (_entries[i].Card, _entries[i].Copies + 1);
        else _entries.Add((card, 1));
    }

    public int Total => _entries.Sum(e => e.Copies) + Unmatched.Count;

    public bool IsEmpty => Total == 0;

    public int Copies(string key) => _entries.FirstOrDefault(e => e.Card.Key == key).Copies;

    /// <summary>Copies of every card named <paramref name="name"/> (foils and reprints share the limit).</summary>
    public int CopiesNamed(string name) => _entries.Where(e => e.Card.Name == name).Sum(e => e.Copies);

    public int Count(CardType type) => _entries.Where(e => e.Card.Type == type).Sum(e => e.Copies);

    /// <summary>Adds one copy, or throws an <see cref="AddCardException"/> saying why it cannot.</summary>
    public void Add(Card card)
    {
        if (Total >= DeckSize) throw AddCardException.Full();
        if (card.Name.Length > 0 && CopiesNamed(card.Name) >= MaxCopies) throw AddCardException.TooManyCopies(card.Name);
        Push(card);
    }

    /// <summary>Removes one copy. Returns false when the card is not in the deck.</summary>
    public bool Remove(string key)
    {
        var i = _entries.FindIndex(e => e.Card.Key == key);
        if (i < 0) return false;
        if (_entries[i].Copies == 1) _entries.RemoveAt(i);
        else _entries[i] = (_entries[i].Card, _entries[i].Copies - 1);
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        Unmatched.Clear();
    }

    /// <summary>Distinct cards in deck order, with their copies.</summary>
    public List<DeckEntry> Entries()
    {
        var cards = _entries.Select(e => e.Card).ToList();
        CardSorting.SortForDeck(cards);
        return cards.Select(c => new DeckEntry(c, Copies(c.Key))).ToList();
    }

    /// <summary>
    /// Distinct cards grouped by level (characters and events together), climaxes last. Inside a
    /// group, cards keep the deck order (characters first, then by cost, power…).
    /// </summary>
    public List<(DeckGroup Group, List<DeckEntry> Cards)> ByLevel()
    {
        var groups = new List<(DeckGroup Group, List<DeckEntry> Cards)>();
        // OrderBy is stable: keeps the deck order inside each group.
        foreach (var entry in Entries().OrderBy(e => DeckGroup.Of(e.Card)))
        {
            var group = DeckGroup.Of(entry.Card);
            if (groups.Count > 0 && groups[^1].Group == group) groups[^1].Cards.Add(entry);
            else groups.Add((group, [entry]));
        }
        return groups;
    }

    /// <summary>Every card code, one per copy, in deck order (unmatched codes last).</summary>
    public List<string> Codes() =>
        Entries().SelectMany(e => Enumerable.Repeat(e.Card.Key, e.Copies)).Concat(Unmatched).ToList();
}
