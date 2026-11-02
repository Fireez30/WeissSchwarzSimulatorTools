// The Damage screen: how much damage a turn deals, estimated by playing it many times
// (WeissSchwarzSimulatorTools.DamageSim).
//
// The decks saved in the simulator are on the left, as the tree of their folders (as on the Decks
// screen, without managing them; the open folders are remembered apart). With one picked, the page in the middle sets
// up the turn: the three characters on stage with their soul (the climax's bonus included), the
// climax (out of the deck), the sources of damage in order (attacks, burns, mills, Musashi, clock
// kicks…), the trigger cards left in the player's deck, and the opponent's deck. "Run" plays the
// turn in the background and shows the damage dealt: average, chance to kill, and the chance of
// each total.
//
// The climax's soul bonus is read from its script and added to the soul of the characters: all
// of them, or the ones ticked on the stage when the climax lets the player choose.
//
// The sources are read from the cards: when a character or the climax changes, the list is
// rebuilt from the attacks and the damage effects of the stage and the climax (TurnBuilder).
// The other damage effects of the deck (events, acts, cards not on the stage) are listed, to add
// by hand. The effects of the turn that let the player choose what they do ("choose 1 of the
// following effects") are listed with their options: picking one rebuilds the list.

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

    /// <summary>Its soul without the climax's bonus (the card's, plus the backrow's… as typed in).</summary>
    [ObservableProperty]
    private decimal? _soul = 1;

    public uint SoulValue => (uint)Math.Clamp(Soul ?? 0, 0, 20);

    /// <summary>The soul the climax gives it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasClimaxSoul), nameof(ClimaxSoulText))]
    private uint _climaxSoul;

    public bool HasClimaxSoul => ClimaxSoul > 0;
    public string ClimaxSoulText => $"{TotalSoul} soul with the climax";

    /// <summary>Its soul when it attacks.</summary>
    public uint TotalSoul => SoulValue + ClimaxSoul;

    /// <summary>One box per climax bonus the player gives to characters of their choice.</summary>
    public ObservableCollection<ClimaxPickViewModel> Picks { get; } = [];

    private CardChoice? _previous;

    partial void OnChoiceChanging(CardChoice value) => _previous = Choice;

    partial void OnChoiceChanged(CardChoice value) => _page.Placed(this, _previous?.Card, value.Card);

    partial void OnSoulChanged(decimal? value)
    {
        OnPropertyChanged(nameof(ClimaxSoulText));
        _page.Changed();
    }
}

/// <summary>Whether a character gets a climax bonus the player chooses who gets.</summary>
public sealed partial class ClimaxPickViewModel(DamageViewModel page, int bonus, string label, bool picked) : ObservableObject
{
    /// <summary>Which bonus of the climax (DamageViewModel.ClimaxBonuses).</summary>
    public int Bonus { get; } = bonus;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isChecked = picked;

    /// <summary>False when enough characters have it already, or this one cannot get it.</summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    partial void OnIsCheckedChanged(bool value) => page.RefreshClimaxSouls();
}

/// <summary>One source of damage of the turn, as its row of the list edits it.</summary>
public sealed partial class SourceViewModel : ObservableObject
{
    private readonly DamageViewModel _page;

    public SourceViewModel(DamageViewModel page, Source source)
    {
        _page = page;
        Origin = source.Origin;
        Note = source.Note;
        _kind = SourceKinds.All.Select(k => new Option<SourceKind>(k, k.Label())).First(c => c.Value == source.Kind);
        _amount = source.Amount;
        _character = page.CharacterChoices[Math.Clamp(source.Character, 0, Rules.Characters - 1)];
        _attack = AttackChoices.First(a => a.Value == source.Attack);
        _defenderLevel = source.DefenderLevel;
        _climaxTo = ClimaxToChoices.First(c => c.Value == source.ClimaxTo);
        _shuffle = source.Shuffle;
    }

    public static IReadOnlyList<Option<LookClimaxes>> ClimaxToChoices { get; } =
        Enum.GetValues<LookClimaxes>().Select(k => new Option<LookClimaxes>(k, k.Label())).ToList();

    /// <summary>For a look at the top cards: where the climaxes go.</summary>
    [ObservableProperty]
    private Option<LookClimaxes> _climaxTo;

    /// <summary>For a look, cards put on top: the deck is shuffled after.</summary>
    [ObservableProperty]
    private bool _shuffle;

    public bool IsLook => Kind.Value == SourceKind.LookTop;
    public bool CanShuffle => Kind.Value.CanShuffle();

    partial void OnClimaxToChanged(Option<LookClimaxes> value) => _page.Changed();
    partial void OnShuffleChanged(bool value) => _page.Changed();

    public static IReadOnlyList<Option<SourceKind>> KindChoices { get; } = SourceKinds.All.Select(k => new Option<SourceKind>(k, k.Label())).ToList();
    public static IReadOnlyList<Option<AttackKind>> AttackChoices { get; } = Enum.GetValues<AttackKind>().Select(k => new Option<AttackKind>(k, k.ToString())).ToList();
    public IReadOnlyList<Option<int>> CharacterChoices => _page.CharacterChoices;

    [ObservableProperty]
    private int _number;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAttack), nameof(HasAmount), nameof(IsMusashi), nameof(Description), nameof(Unit), nameof(IsLook), nameof(CanShuffle))]
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

    /// <summary>For a source read from a card: the card and when its effect happens.</summary>
    public string? Origin { get; }
    /// <summary>For a source read from a card: what was assumed.</summary>
    public string? Note { get; }
    public bool HasOrigin => Origin is not null;
    public string OriginText => Note is null ? Origin ?? "" : $"{Origin}  ·  {Note}";

    public bool IsAttack => Kind.Value == SourceKind.Attack;
    public bool IsSide => Attack.Value == AttackKind.Side;
    public bool IsMusashi => Kind.Value.IsMusashi();
    public bool HasAmount => Kind.Value.HasAmount();
    public string Description => Kind.Value.Description();
    public string Unit => Kind.Value.Unit();
    public string MusashiText => _page.MusashiText(AmountValue);

    private uint AmountValue => (uint)Math.Clamp(Amount ?? 0, 0, 20);

    public Source Source => new(Kind.Value, AmountValue, Character.Value, Attack.Value, (uint)Math.Clamp(DefenderLevel ?? 0, 0, 3), Origin, Note,
        ClimaxTo.Value, Shuffle && Kind.Value.CanShuffle());

    partial void OnKindChanged(Option<SourceKind> value)
    {
        // A Musashi adds 1 to the level, unless told otherwise.
        if (value.Value.IsMusashi() && Amount is null or 0 && !_page.Building) Amount = 1;
        _page.Changed();
    }

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

/// <summary>An effect of the turn that lets the player choose what it does: its options, the one picked.</summary>
public sealed partial class ChoiceViewModel : ObservableObject
{
    private readonly DamageViewModel _page;
    private readonly string _id;

    public ChoiceViewModel(DamageViewModel page, TurnChoice choice, DamageEffect effect)
    {
        _page = page;
        _id = choice.Key;
        HasCharacter = choice.Character >= 0;
        CharacterText = HasCharacter ? $"Character {choice.Character + 1}" : "";
        Title = $"{effect.Card.Name} · {effect.TimingLabel}";
        Tip = effect.Card.Text;
        Options = effect.Options!.Select((o, i) => new Option<int>(i, $"{o.Summary}  ·  {o.Label}")).ToList();
        _selected = Options[Math.Clamp(effect.Chosen, 0, Options.Count - 1)];
    }

    /// <summary>Chosen for one attacking character (Character 1…), or once for the turn.</summary>
    public bool HasCharacter { get; }
    public string CharacterText { get; }
    public string Title { get; }
    public string Tip { get; }
    public IReadOnlyList<Option<int>> Options { get; }

    [ObservableProperty]
    private Option<int> _selected;

    partial void OnSelectedChanged(Option<int> value) => _page.Choose(_id, value.Value);
}

/// <summary>A damage effect of a card of the deck, which the user can add to the turn.</summary>
public sealed partial class DeckEffectViewModel(DamageViewModel page, DamageEffect effect)
{
    public DamageEffect Effect { get; } = effect;
    public string Card => Effect.Card.Name;
    public string Kind => Effect.Act ? "Act" : Effect.Card.Type.Label();
    public string What => $"{Effect.TimingLabel}: {Effect.Summary}";
    public string Tip => Effect.Conditions.Count == 0 ? Effect.Card.Text : $"If {string.Join("; ", Effect.Conditions)}\n\n{Effect.Card.Text}";

    [RelayCommand]
    private void Add() => page.AddEffect(Effect);
}

public sealed partial class DamageViewModel : ObservableObject
{
    /// <summary>The number of trials the user can pick from.</summary>
    private static readonly uint[] TrialChoices = [1_000, 10_000, 100_000];
    private const double ChartHeight = 160;

    private readonly MainViewModel _main;
    private List<DeckFile> _decks = [];
    private Deck? _deck;
    private bool _building;

    /// <summary>The setup is being built: the changes are not reacted to.</summary>
    public bool Building => _building;

    public DamageViewModel(MainViewModel main)
    {
        _main = main;
        CharacterChoices = Enumerable.Range(0, Rules.Characters).Select(i => new Option<int>(i, $"Character {i + 1}")).ToList();
        Opponent = Opponent.Default();
        BuildCounts();
    }

    /// <summary>The cards were (re)loaded: the picked deck was matched to the old ones.</summary>
    public void CardsChanged() => Picked = null;

    /// <summary>The folders and the player's decks, as rows of a tree; flat search results while searching.</summary>
    public ObservableCollection<DeckTreeRow> Rows { get; } = [];
    public ObservableCollection<DeckItem> AiDecks { get; } = [];
    public bool HasAiDecks => AiDecks.Count > 0;
    public bool NoDecks => _decks.Count == 0 && _folders.Count == 0;
    public bool NoMatch => Query.Trim().Length > 0 && Rows.Count == 0 && AiDecks.Count == 0;
    public bool ShowSearch => _decks.Count > 8;

    /// <summary>Every subfolder of the Decks folder, parents before their subfolders.</summary>
    private List<string> _folders = [];
    /// <summary>The open folders of the tree.</summary>
    private HashSet<string>? _expanded;

    private HashSet<string> Expanded => _expanded ??= (_main.Settings.Get(Settings.DamageFoldersOpen) ?? "")
        .Split(Settings.TagSeparator).Where(f => f.Length > 0).ToHashSet(StringComparer.Ordinal);

    private void ToggleFolder(string folder)
    {
        if (!Expanded.Remove(folder)) Expanded.Add(folder);
        var open = Expanded.Where(f => !f.Contains(Settings.TagSeparator)).Order(StringComparer.Ordinal);
        _main.SaveSetting(Settings.DamageFoldersOpen, string.Join(Settings.TagSeparator, open));
        RefreshList();
    }
    public string EmptyText => $"No decks yet.\nDecks saved in the simulator appear here.\n\n{_main.DecksPath}";

    [ObservableProperty]
    private string _query = "";

    partial void OnQueryChanged(string value) => RefreshList();

    [ObservableProperty]
    private DeckItem? _selectedAi;

    private bool _selecting;

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
            _decks = DeckFiles.List(_main.DecksPath)
                .OrderBy(d => d.Name.ToLowerInvariant(), StringComparer.Ordinal).ToList();
            _folders = DeckFiles.Folders(_main.DecksPath);
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
        Rows.Clear();
        AiDecks.Clear();
        bool Matches(DeckFile d) => query.Length == 0 || d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || d.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
        DeckRow DeckRow(DeckFile deck, int depth, bool showFolder) =>
            new(new DeckItem(deck with { Date = "" }, showFolder), depth, deck.Path == Picked?.Path) { Open = new RelayCommand(() => Pick(deck)) };
        var own = _decks.Where(d => !d.Ai).ToList();
        if (query.Length > 0)
        {
            // Search results: a flat list, each deck with its folder.
            foreach (var deck in own.Where(Matches)) Rows.Add(DeckRow(deck, 0, showFolder: true));
        }
        else
            DeckTree.Add(Rows, _folders, own, Expanded,
                (folder, depth, count, open) => new FolderRow(folder, depth, count, open, false) { Toggle = new RelayCommand(() => ToggleFolder(folder)) },
                (deck, depth) => DeckRow(deck, depth, showFolder: false));
        foreach (var deck in _decks.Where(d => d.Ai && Matches(d)))
            AiDecks.Add(new DeckItem(deck with { Date = "" }, ShowFolder: true));
        SelectedAi = AiDecks.FirstOrDefault(d => d.File.Path == Picked?.Path);
        _selecting = false;
        foreach (var name in new[] { nameof(HasAiDecks), nameof(NoDecks), nameof(NoMatch), nameof(ShowSearch), nameof(EmptyText) })
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
        ApplyClimax();
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
        _choices.Clear();
        Outcome = null;
        Pane.Clear();
        Picked = file;
        _building = false;
        ApplyClimax();
        BuildSources(fresh: true);
        ResetDeck();
        // The picked deck shows as selected in the tree.
        RefreshList();
    }

    /// <summary>The soul bonuses of the climax placed.</summary>
    public IReadOnlyList<SoulBonus> ClimaxBonuses { get; private set; } = [];

    public string ClimaxBonusText => Climax.Card is null
        ? "Out of your deck."
        : ClimaxBonuses.Count == 0
            ? "Out of your deck. No soul bonus read from it: add it to the soul of the characters it gives it to."
            : string.Join("\n", ClimaxBonuses.Select(b => b.Summary))
              + (ClimaxBonuses.Any(b => b.Choose > 0) ? "\nTick who gets it on the stage." : "");

    /// <summary>Reads the climax's soul bonuses, and picks who gets the ones to choose: the first characters that can.</summary>
    private void ApplyClimax()
    {
        ClimaxBonuses = Climax.Card is { } climax ? DamageEffects.ClimaxSouls(climax) : [];
        var taken = new int[ClimaxBonuses.Count];
        foreach (var slot in Slots)
        {
            slot.Picks.Clear();
            for (var b = 0; b < ClimaxBonuses.Count; b++)
            {
                var bonus = ClimaxBonuses[b];
                if (bonus.Choose == 0) continue;
                var picked = taken[b] < bonus.Choose && DamageEffects.Allows(bonus.Only, slot.Card) != false;
                if (picked) taken[b]++;
                slot.Picks.Add(new ClimaxPickViewModel(this, b, $"+{bonus.Soul} soul from the climax", picked));
            }
        }
        OnPropertyChanged(nameof(ClimaxBonuses));
        OnPropertyChanged(nameof(ClimaxBonusText));
        RefreshClimaxSouls();
    }

    private bool _refreshingSouls;

    /// <summary>The climax's soul on each character: the bonuses for all that it can get, the ones ticked.</summary>
    public void RefreshClimaxSouls()
    {
        if (_refreshingSouls) return;
        _refreshingSouls = true;
        for (var b = 0; b < ClimaxBonuses.Count; b++)
        {
            var bonus = ClimaxBonuses[b];
            if (bonus.Choose == 0) continue;
            var picks = Slots.Select(s => (s.Card, Pick: s.Picks.FirstOrDefault(p => p.Bonus == b))).Where(p => p.Pick is not null).ToList();
            foreach (var (card, pick) in picks)
                if (DamageEffects.Allows(bonus.Only, card) == false) pick!.IsChecked = false;
            var full = picks.Count(p => p.Pick!.IsChecked) >= bonus.Choose;
            foreach (var (card, pick) in picks)
                pick!.IsEnabled = pick.IsChecked || (!full && DamageEffects.Allows(bonus.Only, card) != false);
        }
        foreach (var slot in Slots)
            slot.ClimaxSoul = (uint)ClimaxBonuses.Select((bonus, b) => bonus.Choose == 0
                    ? DamageEffects.Allows(bonus.Only, slot.Card) != false ? bonus.Soul : 0
                    : slot.Picks.Any(p => p.Bonus == b && p.IsChecked) ? bonus.Soul : 0)
                .Sum(n => (long)n);
        _refreshingSouls = false;
        Changed();
    }

    /// <summary>Whether the sources are rebuilt from the cards when the stage or the climax changes.</summary>
    [ObservableProperty]
    private bool _readEffects = true;

    partial void OnReadEffectsChanged(bool value)
    {
        if (value) BuildSources();
    }

    /// <summary>
    /// The sources from the cards: the attacks (as set up, or one per character when
    /// <paramref name="fresh"/>), with the damage effects of the stage and the climax around them.
    /// </summary>
    private void BuildSources(bool fresh = false)
    {
        var attacks = fresh ? null : Sources.Where(s => s.IsAttack).Select(s => s.Source).ToList();
        var stage = Slots.Select(s => s.Card).ToList();
        var sources = ReadEffects
            ? TurnBuilder.Build(stage, Climax.Card, attacks is { Count: > 0 } ? attacks : null, _choices)
            : attacks is { Count: > 0 } ? attacks : Enumerable.Range(0, Rules.Characters).Select(Source.AttackBy).ToList();
        Choices = ReadEffects
            ? TurnBuilder.Choices(stage, Climax.Card, attacks is { Count: > 0 } ? attacks : null)
                .Select(c => new ChoiceViewModel(this, c, _choices.TryGetValue(c.Key, out var option) ? c.Effect.Choose(option) : c.Effect))
                .ToList()
            : [];
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(HasChoices));
        _building = true;
        Sources.Clear();
        foreach (var source in sources) Sources.Add(new SourceViewModel(this, source));
        _building = false;
        BuildDeckEffects();
        Changed();
    }

    /// <summary>The option picked for the effects that let the player choose, by TurnChoice.Key (the others: the most damage).</summary>
    private readonly Dictionary<string, int> _choices = [];

    /// <summary>The effects of the turn that let the player choose what they do.</summary>
    public IReadOnlyList<ChoiceViewModel> Choices { get; private set; } = [];
    public bool HasChoices => Choices.Count > 0;

    /// <summary>An option was picked for an effect: the sources are rebuilt with it.</summary>
    public void Choose(string id, int option)
    {
        if (_choices.TryGetValue(id, out var old) && old == option) return;
        _choices[id] = option;
        BuildSources();
    }

    /// <summary>Rebuilds the sources from the cards, even when they are not read by themselves.</summary>
    [RelayCommand]
    private void ReadCards() => BuildSources();

    /// <summary>The damage effects of the deck's cards, whatever the climax (with its name in the conditions).</summary>
    public IReadOnlyList<DeckEffectViewModel> DeckEffects { get; private set; } = [];
    public bool HasDeckEffects => DeckEffects.Count > 0;
    public string DeckEffectsTitle => $"Damage effects in this deck ({DeckEffects.Count})";

    private void BuildDeckEffects()
    {
        DeckEffects = (_deck?.Cards ?? []).DistinctBy(c => c.Key)
            .SelectMany(c => DamageEffects.Read(c, null, anyClimax: true))
            .Select(e => new DeckEffectViewModel(this, e))
            .ToList();
        OnPropertyChanged(nameof(DeckEffects));
        OnPropertyChanged(nameof(HasDeckEffects));
        OnPropertyChanged(nameof(DeckEffectsTitle));
    }

    /// <summary>Adds an effect of the deck: main phase effects before the first attack, the others at the end.</summary>
    public void AddEffect(DamageEffect effect)
    {
        var at = effect.Timing is EffectTiming.Main or EffectTiming.ClimaxPlaced or EffectTiming.AttackPhase
            ? Math.Max(0, Sources.ToList().FindIndex(s => s.IsAttack))
            : Sources.Count;
        foreach (var source in TurnBuilder.SourcesOf(effect)) Sources.Insert(at++, new SourceViewModel(this, source));
        Changed();
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
        // A bonus only for some characters (a colour, a trait…).
        RefreshClimaxSouls();
    }

    private void Move(Card? old, Card? card, SlotViewModel? slot)
    {
        if (old is not null) Deck.Add(TriggerKind.Of(old), 1);
        if (card is not null)
        {
            Deck.Add(TriggerKind.Of(card), -1);
            if (slot is not null) slot.Soul = card.SoulCount;
        }
        if (ReadEffects) BuildSources();
        else Changed();
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

    /// <summary>The average damage of a Musashi adding <paramref name="bonus"/> to the level.</summary>
    public string MusashiText(uint bonus) => Levels.Of(OffBoard()).MusashiDamage(bonus) is { } d
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
        Souls = Slots.Select(s => s.TotalSoul).ToArray(),
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
        string.Join("|", Slots.Select(s => s.TotalSoul)) + ";" + string.Join("|", Sources.Select(s => s.Source)) + ";"
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
                var kind when kind.IsMusashi() => $"{i + 1}. {kind.Label()} (level + {source.Amount})",
                var kind when kind.HasAmount() => $"{i + 1}. {kind.Label()} {source.Amount}",
                var kind => $"{i + 1}. {kind.Label()}",
            } + (source.Origin is { } origin ? $"  ·  {origin}" : "");
            return new SourceResult(name, (stats.Damage / trials).ToString("0.00", CultureInfo.InvariantCulture),
                Percent(stats.Happened / trials),
                source.Kind == SourceKind.Attack ? Percent((double)stats.Cancels / Math.Max(1u, stats.Happened)) : "–");
        }).ToList();
        foreach (var name in new[] { nameof(TrialsText), nameof(Tiles), nameof(Bars), nameof(BySource) })
            OnPropertyChanged(name);
        Outcome = outcome;
    }
}
