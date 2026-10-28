namespace SimuDeckParser.Tests;

public class CardIndexTests
{
    private static Serie SerieOf(params Card[] cards) => new()
    {
        Sets = [new CardSet { Cards = new SortedDictionary<string, Card>(cards.ToDictionary(c => c.Key), StringComparer.Ordinal) }],
    };

    [Fact]
    public void Names()
    {
        Card C(string key, string name, string text = "") => Cards.Make(key, CardType.Character, CardColor.Yellow, name: name, text: text);

        var index = new NameIndex([SerieOf(C("AA/W1-001", "Aqua")), SerieOf(C("KS/W49-001", "Aqua"), C("KS/W49-002", "Megumin"))]);
        var from = C("KS/W49-003", "Kazuma");
        Assert.Equal("KS/W49-001", index.Get("Aqua ", from)!.Key);
        Assert.Equal("AA/W1-001", index.Get("Aqua", C("XX/Y1-001", ""))!.Key);
        // Never the card itself.
        Assert.Null(index.Get("Megumin", C("KS/W49-002", "Megumin")));
        Assert.Null(index.Get("Darkness", from));

        var kazuma = C("KS/W49-003", "Kazuma", "If \"Aqua\" and 'Megumin' are on stage");
        var other = C("AA/W1-002", "Other", "\"Aqua\"");
        var megumin = C("KS/W49-004", "Megumin", "Your \"Kazuma\" gets +1000");
        index = new NameIndex([SerieOf(C("AA/W1-001", "Aqua"), other), SerieOf(C("KS/W49-001", "Aqua"), kazuma, megumin)]);
        var related = index.Related(kazuma);
        Assert.Equal(["KS/W49-001", "KS/W49-004"], Cards.Keys(related.Named));
        Assert.Equal(["KS/W49-004"], Cards.Keys(related.Naming));
        // The other series' "Aqua" is named by its own series only.
        var aqua = index.Get("Aqua", kazuma)!;
        Assert.Equal(["KS/W49-003"], Cards.Keys(index.Related(aqua).Naming));
    }
}

public class SettingsTests
{
    [Fact]
    public void DefaultPathIsInOsConfigDir()
    {
        var path = Settings.OpenDefault().Path;
        Assert.EndsWith(Path.Combine("weiss_simulator_parser", "settings.conf"), path);
        Assert.Equal(Path.Combine(Settings.ConfigDir(), "weiss_simulator_parser"), Path.GetDirectoryName(path));
    }

    [Fact]
    public void RoundTrip()
    {
        using var dir = new TempDir();
        var s = new Settings(dir.Join("sub", "settings.conf"));
        Assert.Null(s.Get(Settings.SimulatorPath));
        s.Delete("missing");

        s.Set(Settings.SimulatorPath, "/games/ws:1/Linux_Data/");
        s.Set("other", "x");
        Assert.Equal("/games/ws:1/Linux_Data/", s.Get(Settings.SimulatorPath));
        Assert.Equal("other:x\nsimulator_data_path:/games/ws:1/Linux_Data/\n", File.ReadAllText(s.Path));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(s.Path));

        s.Delete(Settings.SimulatorPath);
        Assert.Null(s.Get(Settings.SimulatorPath));
        Assert.Equal("x", s.Get("other"));
    }
}

public class FiltersTests
{
    private static Card C(string key, CardType t, CardColor color, uint level, uint cost, int power) =>
        Cards.Make(key, t, color, level, cost, power, triggers: [Trigger.Soul]);

    [Fact]
    public void DeckSort()
    {
        var cards = new List<Card>
        {
            C("cx", CardType.Climax, CardColor.Red, 0, 0, 0),
            C("l3", CardType.Character, CardColor.Red, 3, 2, 10000),
            C("l0b", CardType.Character, CardColor.Blue, 0, 0, 3000),
            C("l0a", CardType.Character, CardColor.Yellow, 0, 0, 3000),
            C("ev", CardType.Event, CardColor.Red, 1, 1, 0),
            C("l0c", CardType.Character, CardColor.Red, 0, 0, 1500),
        };
        CardSorting.SortForDeck(cards);
        Assert.Equal(["l0c", "l0a", "l0b", "l3", "ev", "cx"], Cards.Keys(cards));
        CardSorting.SortForCardList(cards);
        Assert.Equal(["l0a", "l0b", "l0c", "l3", "ev", "cx"], Cards.Keys(cards));
    }

    [Fact]
    public void CardListSort()
    {
        var cards = new List<Card>
        {
            C("b-cx", CardType.Climax, CardColor.Blue, 0, 0, 0),
            C("r-ev", CardType.Event, CardColor.Red, 1, 0, 0),
            C("r-c1-9k", CardType.Character, CardColor.Red, 2, 1, 9000),
            C("r-c1-6k", CardType.Character, CardColor.Red, 2, 1, 6000),
            C("r-c0", CardType.Character, CardColor.Red, 3, 0, 10000),
            C("y-c2", CardType.Character, CardColor.Yellow, 3, 2, 10000),
            C("b-c0", CardType.Character, CardColor.Blue, 0, 0, 3000),
        };
        CardSorting.SortForCardList(cards);
        Assert.Equal(["y-c2", "b-c0", "b-cx", "r-c0", "r-c1-6k", "r-c1-9k", "r-ev"], Cards.Keys(cards));
    }

    [Fact]
    public void Filtering()
    {
        var a = Cards.Make("KS/W49-001", color: CardColor.Red, power: 1, traits: ["Magic"]);
        var b = Cards.Make("KS/W49-002", color: CardColor.Blue, level: 1, power: 1, traits: ["Water"], text: "Brainstorm");
        Card[] cards = [a, b];

        var f = Filters.FromCards(cards);
        Assert.Equal([0u, 1u], f.Levels.Available);
        Assert.Equal(["Magic", "Water"], f.Traits.Available);
        Assert.Equal(2, f.Apply(cards).Count);

        f.Traits.Toggle("Magic", false);
        Assert.Equal(["KS/W49-002"], Cards.Keys(f.Apply(cards)));

        f.Colors.Toggle(CardColor.Blue, false);
        Assert.Empty(f.Apply(cards));

        // The search applies on top of the filters.
        f.Search = "brainSTORM";
        Assert.Empty(f.Apply(cards));
        f.Clear();
        Assert.Equal(["KS/W49-002"], Cards.Keys(f.Apply(cards)));
        f.Search = "w49-001";
        Assert.Equal(["KS/W49-001"], Cards.Keys(f.Apply(cards)));
        f.Colors.Toggle(CardColor.Red, false);
        Assert.Empty(f.Apply(cards));
    }

    [Fact]
    public void TraitlessCardsOnlyHiddenWhenTraitsAreNarrowed()
    {
        Card[] cards = [Cards.Make("a", traits: ["Magic"]), Cards.Make("b", traits: ["Water"]), Cards.Make("cx", CardType.Climax)];
        var f = Filters.FromCards(cards);
        Assert.Equal(3, f.Apply(cards).Count);
        f.Traits.Click("Magic");
        Assert.Equal(["a"], Cards.Keys(f.Apply(cards)));
    }

    [Fact]
    public void CxComboFilter()
    {
        Card[] cards = [Cards.Make("a", text: "Auto: CxCombo When a climax is placed"), Cards.Make("b")];
        var f = Filters.FromCards(cards);
        Assert.True(f.HasCxCombo && f.IsUnfiltered);
        f.CxCombo = true;
        Assert.False(f.IsUnfiltered);
        Assert.Equal(["a"], Cards.Keys(f.Apply(cards)));
        f.Clear();
        Assert.Equal(2, f.Apply(cards).Count);
        Assert.False(Filters.FromCards(cards[1..]).HasCxCombo);
    }

    [Fact]
    public void ChipClicksNarrowThenWiden()
    {
        var c = Choice<int>.FromValues([1, 2, 3]);
        Assert.True(c.IsAll && !c.IsPicked(1));
        c.Click(2);
        Assert.Equal([2], c.Selected);
        Assert.True(c.IsPicked(2) && !c.IsPicked(1));
        c.Click(3);
        Assert.Equal([2, 3], c.Selected);
        c.Click(2);
        c.Click(3);
        Assert.True(c.IsAll);
    }
}

public class DeckBuilderTests
{
    private static Card C(string key, string name, CardType type, uint level) => Cards.Make(key, type, level: level, name: name);

    [Fact]
    public void Rules()
    {
        var aqua = C("KS/W49-001", "Aqua", CardType.Character, 1);
        var aquaFoil = C("KS/W49-001SP", "Aqua", CardType.Character, 1);
        var cx = C("KS/W49-099", "Splash", CardType.Climax, 0);
        var deck = new DeckList([], ["XX/Y-001"]);

        for (var i = 0; i < 3; i++) deck.Add(aqua);
        deck.Add(aquaFoil);
        // The foil shares the name, so a fifth "Aqua" is refused.
        var error = Assert.Throws<AddCardException>(() => deck.Add(aqua));
        Assert.Equal("A deck can hold only 4 cards named \"Aqua\".", error.Message);
        Assert.Equal(3, deck.Copies("KS/W49-001"));
        Assert.Equal(4, deck.CopiesNamed("Aqua"));
        deck.Add(cx);
        Assert.Equal(6, deck.Total);
        Assert.Equal(1, deck.Count(CardType.Climax));
        Assert.Equal(["KS/W49-001", "KS/W49-001", "KS/W49-001", "KS/W49-001SP", "KS/W49-099", "XX/Y-001"], deck.Codes());

        Assert.True(deck.Remove("KS/W49-001SP"));
        Assert.False(deck.Remove("KS/W49-001SP"));
        Assert.Equal(3, deck.CopiesNamed("Aqua"));

        var ev = C("KS/W49-050", "Event", CardType.Event, 1);
        var l1 = C("KS/W49-010", "Megumin", CardType.Character, 1);
        var l0 = C("KS/W49-020", "Kazuma", CardType.Character, 0);
        var groups = new DeckList([cx, ev, l1, l0, l0], []).ByLevel()
            .Select(g => $"{g.Group.Label}: {string.Join(", ", g.Cards.Select(e => e.Card.Key))}");
        Assert.Equal(["Level 0: KS/W49-020", "Level 1: KS/W49-010, KS/W49-050", "Climaxes: KS/W49-099"], groups);

        var full = new DeckList(Enumerable.Repeat(cx, DeckList.DeckSize), []);
        Assert.Equal("The deck already has 50 cards.", Assert.Throws<AddCardException>(() => full.Add(aqua)).Message);
        full.Clear();
        Assert.True(full.IsEmpty);
    }
}
