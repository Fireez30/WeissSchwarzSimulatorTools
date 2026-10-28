// Finding loaded cards: by code (CardIndex), and by the names quoted in effect text (NameIndex).

namespace WeissSchwarzSimulatorTools;

/// <summary>
/// Lookup of every loaded card by code, across all series. Building it reads every card: build it
/// once per load and share it (it is only read afterwards, so any thread may use it).
/// </summary>
public sealed class CardIndex
{
    private readonly Dictionary<string, Card> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Card> _lowercase = new(StringComparer.Ordinal);
    private readonly HashSet<string> _codes;

    public CardIndex(IEnumerable<Serie> series)
    {
        foreach (var card in series.SelectMany(s => s.AllCards))
        {
            // First occurrence wins, like the original series-then-set scan.
            _exact.TryAdd(card.Key, card);
            _lowercase.TryAdd(card.Key.ToLowerInvariant(), card);
        }
        _codes = new HashSet<string>(_exact.Keys, StringComparer.Ordinal);
    }

    public IReadOnlySet<string> Codes() => _codes;

    /// <summary>Exact, then without rarity suffix, then case-insensitively for both.</summary>
    public Card? Get(string code)
    {
        code = code.Trim();
        var stripped = TextHelpers.RemoveTrailingAlphas(code);
        return _exact.GetValueOrDefault(code)
            ?? _exact.GetValueOrDefault(stripped)
            ?? _lowercase.GetValueOrDefault(code.ToLowerInvariant())
            ?? _lowercase.GetValueOrDefault(stripped.ToLowerInvariant());
    }

    /// <summary>
    /// Matches the codes of a deck to loaded cards, trying the other spellings of each code
    /// (JP/EN, foil or not). Codes that match nothing end up in <see cref="Deck.Unmatched"/>.
    /// </summary>
    public Deck ResolveDeck(string name, string date, IEnumerable<string> codes)
    {
        var deck = new Deck { Name = name, Date = date };
        foreach (var code in CardCodes.TransformToExistingKeys(Codes(), codes))
        {
            if (string.IsNullOrWhiteSpace(code)) continue;
            if (Get(code) is { } card) deck.Cards.Add(card);
            else deck.Unmatched.Add(code);
        }
        return deck;
    }
}

/// <summary>See <see cref="NameIndex.Related"/>.</summary>
/// <param name="Named">Cards named in the target's text.</param>
/// <param name="Naming">Cards whose text names the target.</param>
public sealed record Related(List<Card> Named, List<Card> Naming);

/// <summary>
/// Cards by name, to resolve the names quoted in effect text ("Aqua" gets +1000 power).
/// Names are matched exactly: effect text spells them as the cards do.
/// </summary>
public sealed class NameIndex
{
    private readonly Dictionary<string, List<Card>> _byName = new(StringComparer.Ordinal);

    public NameIndex() { }

    public NameIndex(IEnumerable<Serie> series)
    {
        foreach (var card in series.SelectMany(s => s.AllCards).Where(c => !string.IsNullOrWhiteSpace(c.Name)))
        {
            var name = card.Name.Trim();
            if (!_byName.TryGetValue(name, out var list))
                _byName[name] = list = [];
            list.Add(card);
        }
    }

    /// <summary>
    /// The card named <paramref name="name"/>, preferring one of the same series as
    /// <paramref name="from"/> (same code prefix before the '/'), as names repeat across series.
    /// </summary>
    public Card? Get(string name, Card from)
    {
        if (!_byName.TryGetValue(name.Trim(), out var cards)) return null;
        var own = SeriesPrefix(from);
        return cards.FirstOrDefault(c => c.Key != from.Key && SeriesPrefix(c) == own)
            ?? cards.FirstOrDefault(c => c.Key != from.Key);
    }

    /// <summary>The cards named in the effect text of <paramref name="card"/>.</summary>
    public List<Card> NamedBy(Card card)
    {
        var output = new List<Card>();
        foreach (var line in TextHelpers.Lines(card.Text))
        {
            foreach (var span in TextHelpers.SplitNames(line, n => Get(n, card) is not null))
            {
                if (span.Name is { } n && Get(n, card) is { } named && !output.Any(c => c.Key == named.Key))
                    output.Add(named);
            }
        }
        return output;
    }

    /// <summary>
    /// Cards that react with <paramref name="target"/>: the ones its text names, and the ones of
    /// its series whose text names it (or a card of the same name), by code.
    /// </summary>
    public Related Related(Card target)
    {
        var own = SeriesPrefix(target);
        var naming = _byName.Values
            .SelectMany(cards => cards)
            .Where(c => c.Key != target.Key && c.Text.Contains(target.Name, StringComparison.Ordinal))
            .Where(c => NamedBy(c).Any(n => n.Name == target.Name && SeriesPrefix(n) == own))
            .OrderBy(c => c.Key, StringComparer.Ordinal)
            .ToList();
        // Remove consecutive duplicates (the same code in several sets).
        for (var i = naming.Count - 1; i > 0; i--)
            if (naming[i].Key == naming[i - 1].Key)
                naming.RemoveAt(i);
        return new Related(NamedBy(target), naming);
    }

    /// <summary>KS for KS/W49-E001: names repeat across series, not within one.</summary>
    private static string SeriesPrefix(Card card)
    {
        var slash = card.Key.IndexOf('/');
        return slash < 0 ? card.Key : card.Key[..slash];
    }
}
