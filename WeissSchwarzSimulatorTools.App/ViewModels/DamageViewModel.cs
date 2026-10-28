// The Damage screen: how much damage a turn deals, estimated by playing it many times
// (WeissSchwarzSimulatorTools.DamageSim).
//
// The decks saved in the simulator are on the left. With one picked, the page in the middle sets
// up the turn: the three characters on stage with their soul (the climax's bonus included), the
// climax (only shown, and out of the deck), the sources of damage in order (attacks, burns,
// mills, Musashi), the trigger cards left in the player's deck, and the opponent's deck. "Run"
// plays the turn in the background and shows the damage dealt: average, chance to kill, and the
// chance of each total.

using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WeissSchwarzSimulatorTools.DamageSim;
using WeissSchwarzSimulatorTools.Simulator;
using Sim = WeissSchwarzSimulatorTools.DamageSim.Simulator;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>A card choice of a stage or climax slot: a card, or none (Card null).</summary>
public sealed record CardChoice(Card? Card, string Label)
{
    public override string ToString() => Label;
}

public sealed record Option<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A character on stage: the card (optional: the soul can be typed in without one) and its soul, every bonus included.</summary>
public sealed partial class SlotViewModel : ObservableObject
{
    private readonly DamageViewModel _page;

    public SlotViewModel(DamageViewModel page, string title, IReadOnlyList<CardChoice> choices)
    {
        _page = page;
        Title = title;
        Choices = choices;
        _choice = choices[0];
    }

    public string Title { get; }
    public IReadOnlyList<CardChoice> Choices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Card), nameof(Image))]
    private CardChoice _choice;

    public Card? Card => Choice.Card;
    public string? Image => Card?.Image;

    [ObservableProperty]
    private decimal? _soul = 1;

    public uint SoulValue => (uint)Math.Clamp(Soul ?? 0, 0, 20);

    private CardChoice? _previous;

    partial void OnChoiceChanging(CardChoice value) => _previous = Choice;

    partial void OnChoiceChanged(CardChoice value) => _page.Placed(this, _previous?.Card, value.Card);

    partial void OnSoulChanged(decimal? value) => _page.Changed();
}

/// <summary>One source of damage of the turn, as its row of the list edits it.</summary>
public sealed partial class SourceViewModel : ObservableObject
{
    private readonly DamageViewModel _page;

    public SourceViewModel(DamageViewModel page, Source source)
    {
        _page = page;
        _kind = SourceKinds.All.Select(k => new Option<SourceKind>(k, k.Label())).First(c => c.Value == source.Kind);
        _amount = source.Amount;
        _character = page.CharacterChoices[Math.Clamp(source.Character, 0, Rules.Characters - 1)];
        _attack = AttackChoices.First(a => a.Value == source.Attack);
        _defenderLevel = source.DefenderLevel;
    }

    public static IReadOnlyList<Option<SourceKind>> KindChoices { get; } = SourceKinds.All.Select(k => new Option<SourceKind>(k, k.Label())).ToList();
    public static IReadOnlyList<Option<AttackKind>> AttackChoices { get; } = Enum.GetValues<AttackKind>().Select(k => new Option<AttackKind>(k, k.ToString())).ToList();
    public IReadOnlyList<Option<int>> CharacterChoices => _page.CharacterChoices;

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAttack), nameof(HasAmount), nameof(IsMusashi), nameof(Description), nameof(Unit))]
    private Option<SourceKind> _kind;

    [ObservableProperty]
    private decimal? _amount;

    [ObservableProperty]
    private Option<int> _character;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSide))]
    private Option<AttackKind> _attack;

    [ObservableProperty]
    private decimal? _defenderLevel;

    [ObservableProperty]
    private bool _isFirst;

    [ObservableProperty]
    private bool _isLast;

    public bool IsAttack => Kind.Value == SourceKind.Attack;
    public bool IsSide => Attack.Value == AttackKind.Side;
    public bool IsMusashi => Kind.Value is SourceKind.Musashi or SourceKind.CancelMusashi;
    public bool HasAmount => Kind.Value.HasAmount();
    public string Description => Kind.Value.Description();
    public string Unit => Kind.Value == SourceKind.Mill ? "cards" : "damage";
    public string MusashiText => _page.MusashiText;

    public Source Source => new(Kind.Value, (uint)Math.Clamp(Amount ?? 0, 0, 20), Character.Value, Attack.Value, (uint)Math.Clamp(DefenderLevel ?? 0, 0, 3));

    partial void OnKindChanged(Option<SourceKind> value) => _page.Changed();
    partial void OnAmountChanged(decimal? value) => _page.Changed();
    partial void OnCharacterChanged(Option<int> value) => _page.Changed();
    partial void OnAttackChanged(Option<AttackKind> value) => _page.Changed();
    partial void OnDefenderLevelChanged(decimal? value) => _page.Changed();

    public void RefreshMusashi() => OnPropertyChanged(nameof(MusashiText));

    [RelayCommand]
    private void MoveUp() => _page.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _page.Move(this, 1);

    [RelayCommand]
    private void Remove() => _page.RemoveSource(this);
}

/// <summary>A number of the "Your deck" or "Opponent" cards, with its label and bounds.</summary>
public sealed partial class CountViewModel(string label, string tip, Func<uint> get, Action<uint> set, Func<uint> max) : ObservableObject
{
    public string Label { get; } = label;
    public string Tip { get; } = tip;
    public decimal Max => max();

    public decimal? Value
    {
        get => get();
        set
        {
            var v = (uint)Math.Clamp(value ?? 0, 0, Math.Max(max(), get()));
            if (v == get()) return;
            set(v);
            OnPropertyChanged();
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Max));
    }
}

public sealed record ResultTile(string Label, string Value, string Supporting);

/// <summary>A bar of the damage chart: the chance of exactly <c>Damage</c>.</summary>
public sealed record HistogramBar(int Damage, double Height, string Label, string Tip, bool Kills);

public sealed record SourceResult(string Name, string Damage, string Happened, string Cancelled);

public sealed partial class DamageViewModel : ObservableObject
{
    /// <summary>The number of trials the user can pick from.</summary>
    private static readonly uint[] TrialChoices = [1_000, 10_000, 100_000];
    private const double ChartHeight = 160;

    private readonly MainViewModel _main;
    private List<DeckFile> _decks = [];
    private Deck? _deck;
    private bool _building;

    public DamageViewModel(MainViewModel main)
    {
        _main = main;
        CharacterChoices = Enumerable.Range(0, Rules.Characters).Select(i => new Option<int>(i, $"Character {i + 1}")).ToList();
        Opponent = Opponent.Default();
        BuildCounts();
    }

    /// <summary>The cards were (re)loaded: the picked deck was matched to the old ones.</summary>
    public void CardsChanged() => Picked = null;

    public ObservableCollection<DeckItem> OwnDecks { get; } = [];
    public ObservableCollection<DeckItem> AiDecks { get; } = [];
    public bool HasAiDecks => AiDecks.Count > 0;
    public bool NoDecks => _decks.Count == 0;
    public bool ShowSearch => _decks.Count > 8;
    public string EmptyText => $"No decks yet.\nDecks saved in the simulator appear here.\n\n{_main.DecksPath}";

    [ObservableProperty]
    private string _query = "";

    partial void OnQueryChanged(string value) => RefreshList();

    [ObservableProperty]
    private DeckItem? _selectedOwn;

    [ObservableProperty]
    private DeckItem? _selectedAi;

    private bool _selecting;

    partial void OnSelectedOwnChanged(DeckItem? value)
    {
        if (value is not null && !_selecting) Pick(value.File);
    }

    partial void OnSelectedAiChanged(DeckItem? value)
    {
        if (value is not null && !_selecting) Pick(value.File);
    }

    /// <summary>The deck the turn is set up from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPicked), nameof(NothingPicked), nameof(DeckName))]
    private DeckFile? _picked;

    public bool HasPicked => Picked is not null;
    public bool NothingPicked => Picked is null;
    public string DeckName => _deck?.Name ?? "";

    public DetailPaneViewModel Pane { get; } = new();

    /// <summary>Re-reads the deck files. The picked deck stays picked if its file still exists.</summary>
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
        if (Picked is not null && _decks.All(d => d.Path != Picked.Path)) Picked = null;
        RefreshList();
    }

    private void RefreshList()
    {
        var query = Query.Trim();
        _selecting = true;
        OwnDecks.Clear();
        AiDecks.Clear();
        foreach (var deck in _decks.Where(d => query.Length == 0 || d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)))
            (deck.Ai ? AiDecks : OwnDecks).Add(new DeckItem(deck with { Date = "" }));
        SelectedOwn = OwnDecks.FirstOrDefault(d => d.File.Path == Picked?.Path);
        SelectedAi = AiDecks.FirstOrDefault(d => d.File.Path == Picked?.Path);
        _selecting = false;
        foreach (var name in new[] { nameof(HasAiDecks), nameof(NoDecks), nameof(ShowSearch), nameof(EmptyText) })
            OnPropertyChanged(name);
    }

    // The setup.

    public IReadOnlyList<Option<int>> CharacterChoices { get; }
    public ObservableCollection<SlotViewModel> Slots { get; } = [];
    public IReadOnlyList<CardChoice> ClimaxChoices { get; private set; } = [new(null, "No climax")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClimaxImage))]
    private CardChoice _climax = new(null, "No climax");

    public string? ClimaxImage => Climax.Card?.Image;

    private CardChoice? _previousClimax;

    partial void OnClimaxChanging(CardChoice value) => _previousClimax = Climax;

    partial void OnClimaxChanged(CardChoice value)
    {
        if (_building) return;
        Move(_previousClimax?.Card, value.Card, null);
    }

    /// <summary>In the order they happen.</summary>
    public ObservableCollection<SourceViewModel> Sources { get; } = [];

    /// <summary>What is left in the player's deck, for the trigger checks.</summary>
    public DeckState Deck { get; private set; } = new();

    public Opponent Opponent { get; }

    public IReadOnlyList<CountViewModel> DeckCounts { get; private set; } = [];
    public IReadOnlyList<CountViewModel> OpponentCounts { get; private set; } = [];

    /// <summary>Opens <paramref name="file"/>, and starts a new setup from it. The opponent is kept: it does not depend on the player's deck.</summary>
    private void Pick(DeckFile file)
    {
        _building = true;
        _deck = _main.CardIndex.ResolveDeck(file.Name, file.Date, file.Codes);
        var unique = _deck.Cards.ToList();
        CardSorting.SortForDeck(unique);
        unique = unique.DistinctBy(c => c.Key).Where(c => c.Type != CardType.Event).ToList();
        var characters = unique.Where(c => c.Type != CardType.Climax)
            .Select(c => new CardChoice(c, $"{c.Name}  ·  L{c.Level} · {c.SoulCount} soul  ·  {c.Key}"));
        var climaxes = unique.Where(c => c.Type == CardType.Climax).Select(c => new CardChoice(c, $"{c.Name}  ·  {c.Key}"));
        Slots.Clear();
        for (var i = 0; i < Rules.Characters; i++)
            Slots.Add(new SlotViewModel(this, $"Character {i + 1}", [new CardChoice(null, "Any character"), .. characters]));
        ClimaxChoices = [new CardChoice(null, "No climax"), .. climaxes];
        OnPropertyChanged(nameof(ClimaxChoices));
        Climax = ClimaxChoices[0];
        Sources.Clear();
        for (var i = 0; i < Rules.Characters; i++) Sources.Add(new SourceViewModel(this, Source.AttackBy(i)));
        Outcome = null;
        Pane.Clear();
        Picked = file;
        _building = false;
        ResetDeck();
    }

    /// <summary>The cards on the stage and in the climax area: out of the deck.</summary>
    private IEnumerable<Card> InPlay() => Slots.Select(s => s.Card).Append(Climax.Card).OfType<Card>();

    /// <summary>The cards of the deck that are not on the stage or in the climax area.</summary>
    private List<Card> OffBoard()
    {
        if (_deck is null) return [];
        var onBoard = InPlay().Select(c => c.Key).ToList();
        var off = new List<Card>();
        foreach (var card in _deck.Cards)
        {
            var i = onBoard.IndexOf(card.Key);
            if (i >= 0) onBoard.RemoveAt(i);
            else off.Add(card);
        }
        return off;
    }

    /// <summary>The player's deck: the whole deck, without the cards on the stage and the climax.</summary>
    [RelayCommand]
    private void ResetDeck()
    {
        Deck = DeckState.Of(OffBoard());
        BuildCounts();
        Changed();
    }

    /// <summary>A card was put on the stage (in place of <paramref name="old"/>): the old card goes back to the deck, the new one comes out of it.</summary>
    public void Placed(SlotViewModel slot, Card? old, Card? card)
    {
        if (_building) return;
        Move(old, card, slot);
    }

    private void Move(Card? old, Card? card, SlotViewModel? slot)
    {
        if (old is not null) Deck.Add(TriggerKind.Of(old), 1);
        if (card is not null)
        {
            Deck.Add(TriggerKind.Of(card), -1);
            if (slot is not null) slot.Soul = card.SoulCount;
        }
        Changed();
    }

    /// <summary>How many cards and climaxes "Your deck" may hold: 50 cards and 8 climaxes, minus the ones in play.</summary>
    private (uint Cards, uint Climaxes) DeckLimits()
    {
        var cards = (uint)InPlay().Count();
        var climaxes = Climax.Card is null ? 0u : 1u;
        return (Rules.DeckSize - Math.Min(Rules.DeckSize, cards), Rules.DeckClimaxes - Math.Min(Rules.DeckClimaxes, climaxes));
    }

    private void BuildCounts()
    {
        uint CardRoom()
        {
            var (maxCards, _) = DeckLimits();
            return maxCards - Math.Min(maxCards, Deck.Total);
        }
        uint ClimaxRoom()
        {
            var (_, maxClimaxes) = DeckLimits();
            return Math.Min(CardRoom(), maxClimaxes - Math.Min(maxClimaxes, Deck.Climaxes));
        }
        DeckCounts = TriggerKind.All.Select((kind, i) => new CountViewModel(
                $"{(kind.Climax ? "Climax" : "Character")}, {kind.Label.ToLowerInvariant()}", "",
                () => Deck.Counts[i],
                v =>
                {
                    Deck.Counts[i] = v;
                    Changed();
                },
                () => Deck.Counts[i] + (kind.Climax ? ClimaxRoom() : CardRoom())))
            .ToList();

        var o = Opponent;
        uint OppCardRoom() => Rules.DeckSize - Math.Min(Rules.DeckSize, o.Cards);
        uint OppClimaxRoom() => Rules.DeckClimaxes - Math.Min(Rules.DeckClimaxes, o.Climaxes);
        // Stock, level and clock come from the deck: changing them takes cards out of it.
        void Held(Action change)
        {
            var before = o.Stock + o.Level + o.Clock;
            change();
            var after = o.Stock + o.Level + o.Clock;
            if (after != before)
            {
                o.DeckCards = (uint)Math.Max(0, (long)o.DeckCards + before - after);
                o.DeckClimaxes = Math.Min(o.DeckClimaxes, o.DeckCards);
            }
            Changed();
        }
        OpponentCounts =
        [
            new("Cards in deck", "Cards left in the opponent's deck", () => o.DeckCards, v => Held(() => o.DeckCards = v), () => Math.Min(Rules.DeckSize, o.DeckCards + OppCardRoom())),
            new("Climaxes in deck", "Climaxes among them", () => o.DeckClimaxes, v => Held(() => o.DeckClimaxes = v), () => Math.Min(Rules.DeckClimaxes, o.DeckClimaxes + OppClimaxRoom())),
            new("Waiting room", "Cards in the waiting room: shuffled back into the deck when it runs out", () => o.WaitingRoomCards, v => Held(() => o.WaitingRoomCards = v), () => Math.Min(Rules.DeckSize, o.WaitingRoomCards + OppCardRoom())),
            new("Climaxes in waiting room", "Climaxes among them", () => o.WaitingRoomClimaxes, v => Held(() => o.WaitingRoomClimaxes = v), () => Math.Min(Rules.DeckClimaxes, o.WaitingRoomClimaxes + OppClimaxRoom())),
            new("Stock", "Cards in the opponent's stock, none of them a climax", () => o.Stock, v => Held(() => o.Stock = v), () => Rules.DeckSize),
            new("Level", "The opponent's level", () => o.Level, v => Held(() => o.Level = v), () => 3),
            new("Clock", "Cards in the opponent's clock", () => o.Clock, v => Held(() => o.Clock = v), () => 6),
        ];
        OnPropertyChanged(nameof(DeckCounts));
        OnPropertyChanged(nameof(OpponentCounts));
    }

    /// <summary>Every 8 climaxes in the deck, every card not in the stock, level or clock.</summary>
    [RelayCommand]
    private void RefreshOpponent()
    {
        Opponent.Refresh();
        Changed();
    }

    [RelayCommand]
    private void AddAttack()
    {
        var attacks = Sources.Count(s => s.IsAttack);
        Sources.Add(new SourceViewModel(this, Source.AttackBy(attacks % Rules.Characters)));
        Changed();
    }

    [RelayCommand]
    private void AddBurn()
    {
        Sources.Add(new SourceViewModel(this, new Source(SourceKind.Burn, 1)));
        Changed();
    }

    public void Move(SourceViewModel source, int by)
    {
        var i = Sources.IndexOf(source);
        var j = i + by;
        if (i < 0 || j < 0 || j >= Sources.Count) return;
        Sources.Move(i, j);
        Changed();
    }

    public void RemoveSource(SourceViewModel source)
    {
        Sources.Remove(source);
        Changed();
    }

    /// <summary>Something of the setup changed: refreshes what depends on it.</summary>
    public void Changed()
    {
        if (_building) return;
        for (var i = 0; i < Sources.Count; i++)
        {
            Sources[i].Number = i + 1;
            Sources[i].IsFirst = i == 0;
            Sources[i].IsLast = i == Sources.Count - 1;
            Sources[i].RefreshMusashi();
        }
        foreach (var count in DeckCounts) count.Refresh();
        foreach (var count in OpponentCounts) count.Refresh();
        foreach (var name in new[] { nameof(Problem), nameof(HasProblem), nameof(CanRun), nameof(DeckTotalText), nameof(OpponentTotalText), nameof(DamageToKillText), nameof(IsStale) })
            OnPropertyChanged(name);
    }

    public string MusashiText => Levels.Of(OffBoard()).MusashiDamage() is { } d
        ? $"{d.ToString("0.00", CultureInfo.InvariantCulture)} damage on average"
        : "No damage: your deck is empty";

    public string DeckTotalText
    {
        get
        {
            var (cards, climaxes) = DeckLimits();
            return $"{Deck.Total} / {cards} cards, {Deck.Climaxes} / {climaxes} climaxes";
        }
    }

    public string OpponentTotalText => $"{Opponent.Cards} / 50 cards, {Opponent.Climaxes} / 8 climaxes";
    public string DamageToKillText => $"{Opponent.DamageToKill} damage to kill";

    /// <summary>The turn as set up on the page.</summary>
    private Turn Turn() => new()
    {
        Souls = Slots.Select(s => s.SoulValue).ToArray(),
        Sources = Sources.Select(s => s.Source).ToList(),
        Deck = Deck.Clone(),
        Musashi = Levels.Of(OffBoard()),
    };

    /// <summary>Why the turn cannot be simulated, if it cannot.</summary>
    public string? Problem
    {
        get
        {
            if (Sources.Count == 0) return "Add a source of damage.";
            var (maxCards, maxClimaxes) = DeckLimits();
            if (Deck.Total > maxCards) return "Your deck has more than 50 cards, counting the stage and the climax.";
            if (Deck.Climaxes > maxClimaxes) return "Your deck has more than 8 climaxes, counting the one placed.";
            return Opponent.Problem();
        }
    }

    public bool HasProblem => Problem is not null;
    public bool CanRun => Problem is null && !Running;

    // Running.

    [ObservableProperty]
    private uint _trials = TrialChoices[1];

    public bool Is1000 => Trials == 1_000;
    public bool Is10000 => Trials == 10_000;
    public bool Is100000 => Trials == 100_000;

    partial void OnTrialsChanged(uint value)
    {
        OnPropertyChanged(nameof(Is1000));
        OnPropertyChanged(nameof(Is10000));
        OnPropertyChanged(nameof(Is100000));
        OnPropertyChanged(nameof(IsStale));
    }

    [RelayCommand]
    private void SetTrials(string n) => Trials = uint.Parse(n, CultureInfo.InvariantCulture);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun), nameof(IsStale))]
    private bool _running;

    /// <summary>The setup of the last run, to tell when the results no longer match the page.</summary>
    private string? _ranWith;

    private string Fingerprint() =>
        string.Join("|", Slots.Select(s => s.SoulValue)) + ";" + string.Join("|", Sources.Select(s => s.Source)) + ";"
        + string.Join(",", Deck.Counts) + ";" + Opponent + ";" + Trials + ";" + string.Join(",", Levels.Of(OffBoard()).Counts);

    public bool IsStale => !Running && Outcome is not null && _ranWith != Fingerprint();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults), nameof(IsStale))]
    private Outcome? _outcome;

    public bool HasResults => Outcome is not null;
    public string TrialsText { get; private set; } = "";
    public IReadOnlyList<ResultTile> Tiles { get; private set; } = [];
    public IReadOnlyList<HistogramBar> Bars { get; private set; } = [];
    public IReadOnlyList<SourceResult> BySource { get; private set; } = [];

    /// <summary>Raised once new results are shown: the view scrolls down to them.</summary>
    public event Action? ResultsShown;

    [RelayCommand]
    private async Task Run()
    {
        if (!CanRun) return;
        var (turn, opponent, trials) = (Turn(), Opponent with { }, Trials);
        var fingerprint = Fingerprint();
        Running = true;
        try
        {
            var outcome = await Task.Run(() => Sim.Simulate(turn, opponent, trials, Sim.RandomSeed()));
            _ranWith = fingerprint;
            ShowResults(outcome, turn.Sources);
            ResultsShown?.Invoke();
        }
        catch (Exception e)
        {
            _main.Notify(Notice.FromException("The simulation failed", e));
        }
        finally
        {
            Running = false;
        }
    }

    private static string Thousands(uint n) => n.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

    private static string Percent(double p) => (p * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private void ShowResults(Outcome outcome, IReadOnlyList<Source> sources)
    {
        TrialsText = $"{Thousands(outcome.Trials)} turns played";
        var max = Math.Max(0, outcome.Histogram.Count - 1);
        var likeliest = Enumerable.Range(0, max + 1).MaxBy(outcome.ChanceOf);
        var lowest = outcome.Histogram.FindIndex(n => n > 0);
        Tiles =
        [
            new("Average damage", outcome.Mean().ToString("0.00", CultureInfo.InvariantCulture), $"Median {outcome.Median()}"),
            new("Kill chance", Percent(outcome.KillChance()), $"{outcome.DamageToKill} damage to kill"),
            new("Most likely", $"{likeliest} damage", $"{Percent(outcome.ChanceOf(likeliest))} of the turns"),
            new("Range", $"{Math.Max(0, lowest)} – {max}", "Lowest to highest"),
        ];
        var highest = Math.Max(double.Epsilon, Enumerable.Range(0, max + 1).Max(outcome.ChanceOf));
        Bars = Enumerable.Range(0, max + 1)
            .Select(n => new HistogramBar(n, ChartHeight * outcome.ChanceOf(n) / highest,
                outcome.ChanceOf(n) >= 0.005 ? Percent(outcome.ChanceOf(n)) : "",
                $"{n} damage: {Percent(outcome.ChanceOf(n))}\n{n} or more: {Percent(outcome.ChanceAtLeast(n))}",
                n >= outcome.DamageToKill))
            .ToList();
        var trials = Math.Max(1.0, outcome.Trials);
        BySource = sources.Zip(outcome.Sources).Select((p, i) =>
        {
            var (source, stats) = p;
            var name = source.Kind switch
            {
                SourceKind.Attack => $"{i + 1}. {source.Attack} attack, character {source.Character + 1}",
                var kind when kind.HasAmount() => $"{i + 1}. {kind.Label()} {source.Amount}",
                var kind => $"{i + 1}. {kind.Label()}",
            };
            return new SourceResult(name, (stats.Damage / trials).ToString("0.00", CultureInfo.InvariantCulture),
                Percent(stats.Happened / trials),
                source.Kind == SourceKind.Attack ? Percent((double)stats.Cancels / Math.Max(1u, stats.Happened)) : "–");
        }).ToList();
        foreach (var name in new[] { nameof(TrialsText), nameof(Tiles), nameof(Bars), nameof(BySource) })
            OnPropertyChanged(name);
        Outcome = outcome;
    }
}
