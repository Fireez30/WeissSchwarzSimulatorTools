// The data the whole program works with: cards, the series and sets they belong to, and decks.
//
// A simulator install holds many Series (one per franchise, e.g. "KonoSuba!"). Each serie has
// one or more Sets (booster boxes, trial decks…), and each set has its Cards, keyed by card
// code (KS/W49-E001).

namespace WeissSchwarzSimulatorTools;

public static class CardImage
{
    /// <summary>Height / width of a card picture (the simulator's artwork is 224 × 313 pixels).</summary>
    public const double Ratio = 313.0 / 224.0;
}

/// <summary>The order of the values matters: it is the order of the card list's colour sort.</summary>
public enum CardColor
{
    Yellow,
    Blue,
    Red,
    Green,
    Purple,
    All,
    None,
}

/// <summary>The order of the values matters: characters sort before events, events before climaxes.</summary>
public enum CardType
{
    Character,
    Event,
    Climax,
}

/// <summary>The icon in a card's top-right corner, which takes effect when the card is revealed.</summary>
public enum Trigger
{
    Soul,
    Choice,
    Burn,
    Wind,
    Standby,
    Salvage,
    Book,
    Pant,
    Bar,
    Bag,
}

public static class ModelLabels
{
    public static string Label(this CardColor color) => color.ToString();

    public static string Label(this CardType type) => type.ToString();

    /// <summary>Section title for a group of cards of this type.</summary>
    public static string PluralLabel(this CardType type) => type switch
    {
        CardType.Character => "Characters",
        CardType.Event => "Events",
        _ => "Climaxes",
    };

    public static string Label(this Trigger trigger) => trigger switch
    {
        Trigger.Bag => "Goldbag",
        Trigger.Bar => "Goldbar",
        _ => trigger.ToString(),
    };
}

public sealed class Card
{
    /// <summary>The card code, e.g. <c>KS/W49-E001</c>.</summary>
    public required string Key { get; init; }
    public CardType Type { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Null when no artwork was found; the UI draws a placeholder instead.</summary>
    public string? Image { get; set; }
    public CardColor Color { get; init; }
    public uint Level { get; init; }
    public uint Cost { get; init; }
    public int Power { get; init; }
    public IReadOnlyList<Trigger> Triggers { get; init; } = [];
    public uint SoulCount { get; init; }
    public IReadOnlyList<string> Traits { get; init; } = [];
    /// <summary>The simulator's script for the card (Auto: / Cont: / Act: blocks). Not shown.</summary>
    public string Code { get; init; } = "";
    /// <summary>The effect text shown to players, one effect per line.</summary>
    public string Text { get; init; } = "";

    public override string ToString() => Key;
}

public sealed class CardSet
{
    /// <summary>Code prefix shared by the set's cards, e.g. <c>KS/W49</c>.</summary>
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>The set's cards by card code, sorted by code.</summary>
    public SortedDictionary<string, Card> Cards { get; set; } = new(StringComparer.Ordinal);
}

public sealed class Serie
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<CardSet> Sets { get; set; } = [];

    public IEnumerable<Card> AllCards => Sets.SelectMany(s => s.Cards.Values);
}

/// <summary>
/// A deck whose card codes have been matched to loaded cards (see <see cref="CardIndex.ResolveDeck"/>).
/// For the deck file as saved in the simulator, see <see cref="Simulator.DeckFile"/>.
/// </summary>
public sealed class Deck
{
    public string Name { get; set; } = "";
    public string Date { get; set; } = "";
    /// <summary>One entry per copy, in the deck file's order.</summary>
    public List<Card> Cards { get; set; } = [];
    /// <summary>Codes of the deck that match no loaded card (another simulator version, a typo…).</summary>
    public List<string> Unmatched { get; set; } = [];
}
