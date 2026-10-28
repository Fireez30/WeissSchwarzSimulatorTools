// The card list's filters (type, colour, level… and the search field) and the two sort orders
// cards are shown in. Independent from the UI, so it is unit-tested.

namespace SimuDeckParser;

public static class CardSorting
{
    /// <summary>Sorts cards as the card list shows them: by colour, then type, cost, power and code.</summary>
    public static void SortForCardList(List<Card> cards)
    {
        var sorted = cards
            .OrderBy(c => c.Color).ThenBy(c => c.Type).ThenBy(c => c.Cost).ThenBy(c => c.Power)
            .ThenBy(c => c.Key, StringComparer.Ordinal).ThenBy(c => c.Level)
            .ToList();
        cards.Clear();
        cards.AddRange(sorted);
    }

    /// <summary>
    /// Sorts cards as decks show them: characters, then events, then climaxes; inside each type
    /// by level, cost, power and colour.
    /// </summary>
    public static void SortForDeck(List<Card> cards)
    {
        // A stable sort, like Rust's sort_by: equal cards keep their order.
        var sorted = cards
            .OrderBy(c => c.Type).ThenBy(c => c.Level).ThenBy(c => c.Cost)
            .ThenBy(c => c.Power).ThenBy(c => c.Color)
            .ToList();
        cards.Clear();
        cards.AddRange(sorted);
    }
}

/// <summary>One group of filter chips: every value present in the loaded cards, and the selected ones.</summary>
public sealed class Choice<T> where T : notnull
{
    public List<T> Available { get; private set; } = [];
    public SortedSet<T> Selected { get; private set; }

    public Choice(IComparer<T>? comparer = null) => Selected = new SortedSet<T>(comparer ?? Comparer<T>.Default);

    internal static Choice<T> FromValues(IEnumerable<T> values, IComparer<T>? comparer = null)
    {
        var set = new SortedSet<T>(values, comparer ?? Comparer<T>.Default);
        return new Choice<T>(comparer) { Available = set.ToList(), Selected = set };
    }

    public Choice<T> Clone() => new(Selected.Comparer) { Available = [.. Available], Selected = new SortedSet<T>(Selected, Selected.Comparer) };

    public bool Contains(T v) => Selected.Contains(v);

    public void Toggle(T v, bool on)
    {
        if (on) Selected.Add(v);
        else Selected.Remove(v);
    }

    /// <summary>Every available value is selected, i.e. this group filters nothing out.</summary>
    public bool IsAll => Available.All(Selected.Contains);

    public void SelectAll() => Selected = new SortedSet<T>(Available, Selected.Comparer);

    /// <summary>Whether <paramref name="v"/> shows as picked in a chip UI: nothing is picked while the group is unfiltered.</summary>
    public bool IsPicked(T v) => !IsAll && Contains(v);

    /// <summary>
    /// Chip click: the first pick narrows the group to <paramref name="v"/>, later picks add or
    /// remove values, and removing the last pick clears the filter.
    /// </summary>
    public void Click(T v)
    {
        if (IsAll) Selected = new SortedSet<T>([v], Selected.Comparer);
        else if (!Selected.Remove(v)) Selected.Add(v);
        if (Selected.Count == 0) SelectAll();
    }
}

public sealed class Filters
{
    public Choice<uint> Levels { get; private set; } = new();
    public Choice<uint> Costs { get; private set; } = new();
    public Choice<CardColor> Colors { get; private set; } = new();
    public Choice<CardType> Types { get; private set; } = new();
    public Choice<string> Traits { get; private set; } = new(StringComparer.Ordinal);
    /// <summary>Some loaded card has the climax combo keyword, so the filter is offered.</summary>
    public bool HasCxCombo { get; private set; }
    /// <summary>Only show cards with the climax combo keyword.</summary>
    public bool CxCombo { get; set; }
    /// <summary>Name / code / text search, applied on top of the other filters.</summary>
    public string Search { get; set; } = "";

    /// <summary>No checkbox group filters anything out (the search is not considered).</summary>
    public bool IsUnfiltered =>
        Levels.IsAll && Costs.IsAll && Colors.IsAll && Types.IsAll && Traits.IsAll && !CxCombo;

    public void Clear()
    {
        Levels.SelectAll();
        Costs.SelectAll();
        Colors.SelectAll();
        Types.SelectAll();
        Traits.SelectAll();
        CxCombo = false;
    }

    /// <summary>Offers every value present in <paramref name="cards"/>, all selected (nothing filtered out yet).</summary>
    public static Filters FromCards(IReadOnlyCollection<Card> cards) => new()
    {
        Levels = Choice<uint>.FromValues(cards.Select(c => c.Level)),
        Costs = Choice<uint>.FromValues(cards.Select(c => c.Cost)),
        Colors = Choice<CardColor>.FromValues(cards.Select(c => c.Color)),
        Types = Choice<CardType>.FromValues(cards.Select(c => c.Type)),
        Traits = Choice<string>.FromValues(cards.SelectMany(c => c.Traits), StringComparer.Ordinal),
        HasCxCombo = cards.Any(IsCxCombo),
    };

    public Filters Clone() => new()
    {
        Levels = Levels.Clone(),
        Costs = Costs.Clone(),
        Colors = Colors.Clone(),
        Types = Types.Clone(),
        Traits = Traits.Clone(),
        HasCxCombo = HasCxCombo,
        CxCombo = CxCombo,
        Search = Search,
    };

    public bool Matches(Card c)
    {
        var search = Search.Trim();
        var found = search.Length == 0
            || TextHelpers.ContainsIgnoreCase(search, c.Name)
            || TextHelpers.ContainsIgnoreCase(search, c.Key)
            || TextHelpers.ContainsIgnoreCase(search, c.Text);
        return found
            && Levels.Contains(c.Level)
            && Costs.Contains(c.Cost)
            && Colors.Contains(c.Color)
            && Types.Contains(c.Type)
            // Traitless cards (climaxes, most events) only drop out once traits are narrowed.
            && (Traits.IsAll || c.Traits.Any(Traits.Contains))
            && (!CxCombo || IsCxCombo(c));
    }

    /// <summary>The cards that pass every filter and the search.</summary>
    public List<Card> Apply(IEnumerable<Card> cards) => cards.Where(Matches).ToList();

    /// <summary>The card text has the climax combo keyword.</summary>
    public static bool IsCxCombo(Card c) => c.Text.Contains("CxCombo", StringComparison.Ordinal);
}
