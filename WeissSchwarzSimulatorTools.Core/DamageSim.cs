// Damage calculator: how much damage a turn deals, estimated by playing the turn many times with
// shuffled decks (a Monte Carlo simulation).
//
// A Turn describes the player's side: the soul of the three characters on stage (the climax's
// bonus included, as the user enters it), the Sources of damage in the order they happen, and
// what is left in the player's deck (for trigger checks). Opponent describes the defending
// player's deck, waiting room, stock, level and clock. Simulate plays the turn `trials` times and
// returns an Outcome: how often each total damage happened, and how often the opponent died.
//
// The sources, one after the other:
// - an attack: the top card of the player's deck is the trigger check, its soul icons go to the
//   attacking character for the rest of the turn (a character attacking again keeps them); then
//   cards are revealed from the opponent's deck one by one, a climax cancels the damage (the
//   cards go to the waiting room), otherwise they go to the clock; a shot trigger deals 1 more
//   damage when the damage is cancelled;
// - a burn, maybe only when the last attack was cancelled, not cancelled, or its trigger check
//   revealed a climax;
// - a mill: the top cards of the opponent's deck go to their waiting room;
// - a Musashi: reveals a card of the player's deck and deals its level + 1 damage. The card is
//   drawn from the deck as it was at the start of the turn (Turn.Musashi), apart from the
//   trigger checks.
//
// The opponent refreshes when their deck runs out (waiting room shuffled back, plus 1
// uncancellable damage), and levels up at 7 clock. Card power, battles and counters are not
// simulated.
//
// Decks are not stored card by card: a deck is a count of each kind of card, and drawing picks
// one at random. That is the same as drawing from a shuffled deck, and much faster.

namespace WeissSchwarzSimulatorTools.DamageSim;

public static class Rules
{
    /// <summary>Cards in the clock that make the player level up.</summary>
    internal const uint ClockSize = 7;
    /// <summary>The level at which a player loses.</summary>
    internal const uint LethalLevel = 4;
    /// <summary>Deck building rules: a deck has at most 50 cards, and at most 8 of them are climaxes.</summary>
    public const uint DeckSize = 50;
    public const uint DeckClimaxes = 8;
    /// <summary>Characters on the stage that can attack.</summary>
    public const int Characters = 3;
    /// <summary>Card levels a deck holds: 0 to 3. Climaxes are level 0.</summary>
    public const int LevelCount = 4;
}

/// <summary>What a source of damage is.</summary>
public enum SourceKind
{
    /// <summary>A character attacks, with a trigger check.</summary>
    Attack,
    /// <summary>Deal damage (when attacking, after the attack, when the climax is placed…).</summary>
    Burn,
    /// <summary>Deal damage if the last attack's damage was cancelled.</summary>
    CancelBurn,
    /// <summary>Deal damage if the last attack's damage was not cancelled.</summary>
    NotCancelledBurn,
    /// <summary>Deal damage if the last attack's trigger check revealed a climax.</summary>
    ClimaxTriggerBurn,
    /// <summary>Put the top cards of the opponent's deck in their waiting room.</summary>
    Mill,
    /// <summary>Reveal a card of the player's deck and deal its level + 1 damage.</summary>
    Musashi,
    /// <summary>The same, if the last attack's damage was cancelled.</summary>
    CancelMusashi,
}

public static class SourceKinds
{
    public static readonly SourceKind[] All = Enum.GetValues<SourceKind>();

    public static string Label(this SourceKind kind) => kind switch
    {
        SourceKind.Attack => "Attack",
        SourceKind.Burn => "Burn",
        SourceKind.CancelBurn => "Burn if cancelled",
        SourceKind.NotCancelledBurn => "Burn if not cancelled",
        SourceKind.ClimaxTriggerBurn => "Burn on climax trigger",
        SourceKind.Mill => "Mill opponent",
        SourceKind.Musashi => "Musashi",
        _ => "Musashi if cancelled",
    };

    public static string Description(this SourceKind kind) => kind switch
    {
        SourceKind.Attack =>
            "A character attacks: trigger check, then damage equal to its soul. Its trigger soul stays for its next attacks",
        SourceKind.Burn =>
            "Deal this much damage: when attacking, after the attack, when the climax is placed… Its place in the list says when",
        SourceKind.CancelBurn => "If the last attack's damage was cancelled, deal this much",
        SourceKind.NotCancelledBurn => "If the last attack's damage was not cancelled, deal this much",
        SourceKind.ClimaxTriggerBurn => "If the last attack's trigger check revealed a climax, deal this much",
        SourceKind.Mill => "Put this many cards from the top of the opponent's deck into their waiting room",
        SourceKind.Musashi => "Reveal a card of your deck (minus the stage and the climax) and deal its level + 1 damage",
        _ => "If the last attack's damage was cancelled: reveal a card of your deck and deal its level + 1 damage",
    };

    /// <summary>Whether the amount matters: damage or cards milled.</summary>
    public static bool HasAmount(this SourceKind kind) =>
        kind is not (SourceKind.Attack or SourceKind.Musashi or SourceKind.CancelMusashi);
}

/// <summary>How an attack is made.</summary>
public enum AttackKind
{
    Front,
    /// <summary>Side attack: loses as much soul as the level of the character in front of it.</summary>
    Side,
    /// <summary>Direct attack (no character in front): +1 soul.</summary>
    Direct,
}

/// <summary>One source of damage of the turn.</summary>
/// <param name="Amount">Damage, or cards milled. Unused by attacks and Musashi.</param>
/// <param name="Character">For an attack: which character attacks (0 to 2).</param>
/// <param name="Attack">For an attack: how.</param>
/// <param name="DefenderLevel">For a side attack: the level of the opponent's character in front of it.</param>
public sealed record Source(SourceKind Kind, uint Amount, int Character = 0, AttackKind Attack = AttackKind.Front, uint DefenderLevel = 0)
{
    /// <summary>A front attack by <paramref name="character"/>.</summary>
    public static Source AttackBy(int character) => new(SourceKind.Attack, 0, character);
}

/// <summary>What a card revealed by a trigger check does to the attack.</summary>
/// <param name="Soul">Soul icons (0, 1 or 2).</param>
/// <param name="Shot">Shot trigger: 1 damage when the attack's damage is cancelled.</param>
public readonly record struct TriggerKind(bool Climax, uint Soul, bool Shot)
{
    /// <summary>The kinds the deck state is entered with, in display order.</summary>
    public static readonly TriggerKind[] All =
    [
        new(false, 0, false),
        new(false, 1, false),
        new(false, 2, false),
        new(true, 0, false),
        new(true, 1, false),
        new(true, 2, false),
        new(true, 1, true),
    ];

    /// <summary>
    /// The kind of a card, from its trigger icons. A non-climax shot card counts as a climax shot
    /// trigger: shots are on climaxes.
    /// </summary>
    public static TriggerKind Of(Card card)
    {
        var soul = (uint)card.Triggers.Count(t => t == Trigger.Soul);
        if (card.Triggers.Contains(Trigger.Burn)) return new(true, 1, true);
        return new(card.Type == CardType.Climax, Math.Min(soul, 2), false);
    }

    public string Label => (Shot, Soul) switch
    {
        (true, _) => "Shot (+1 soul)",
        (false, 0) => "No soul",
        (false, 1) => "+1 soul",
        _ => "+2 soul",
    };
}

/// <summary>The player's deck for the trigger checks: how many cards of each <see cref="TriggerKind.All"/> kind are left in it.</summary>
public sealed class DeckState : IEquatable<DeckState>
{
    public uint[] Counts { get; } = new uint[TriggerKind.All.Length];

    /// <summary>The state of a deck holding <paramref name="cards"/>.</summary>
    public static DeckState Of(IEnumerable<Card> cards)
    {
        var state = new DeckState();
        foreach (var card in cards) state.Add(TriggerKind.Of(card), 1);
        return state;
    }

    public DeckState Clone()
    {
        var copy = new DeckState();
        Counts.CopyTo(copy.Counts, 0);
        return copy;
    }

    public uint Total => (uint)Counts.Sum(n => (long)n);

    public uint Climaxes => (uint)TriggerKind.All.Zip(Counts).Where(p => p.First.Climax).Sum(p => (long)p.Second);

    /// <summary>Adds (or with a negative <paramref name="n"/>, removes) cards of a kind; counts stay at 0 or more.</summary>
    public void Add(TriggerKind kind, int n)
    {
        var i = Array.IndexOf(TriggerKind.All, kind);
        if (i >= 0) Counts[i] = (uint)Math.Clamp((long)Counts[i] + n, 0, uint.MaxValue);
    }

    /// <summary>Takes a random card out; null when the deck is empty.</summary>
    internal TriggerKind? Draw(Rng rng)
    {
        var pick = rng.Below(Total);
        for (var i = 0; i < Counts.Length; i++)
        {
            if (pick < Counts[i])
            {
                Counts[i]--;
                return TriggerKind.All[i];
            }
            pick -= Counts[i];
        }
        return null;
    }

    public bool Equals(DeckState? other) => other is not null && Counts.SequenceEqual(other.Counts);

    public override bool Equals(object? obj) => Equals(obj as DeckState);

    public override int GetHashCode() => Counts.Aggregate(17, (h, n) => h * 31 + (int)n);
}

/// <summary>How many cards of each level a deck holds, for Musashi effects.</summary>
public sealed class Levels : IEquatable<Levels>
{
    public uint[] Counts { get; }

    public Levels() => Counts = new uint[Rules.LevelCount];

    public Levels(params uint[] counts) => Counts = counts;

    public static Levels Of(IEnumerable<Card> cards)
    {
        var levels = new Levels();
        foreach (var card in cards) levels.Counts[Math.Min((int)card.Level, Rules.LevelCount - 1)]++;
        return levels;
    }

    public uint Total => (uint)Counts.Sum(n => (long)n);

    /// <summary>The average damage of a Musashi (the level + 1 of a random card); null when there are no cards.</summary>
    public double? MusashiDamage()
    {
        var total = Total;
        if (total == 0) return null;
        var sum = Counts.Select((n, level) => (long)(level + 1) * n).Sum();
        return (double)sum / total;
    }

    /// <summary>The level + 1 of a random card, which stays in the deck; null when there are none.</summary>
    internal uint? Musashi(Rng rng)
    {
        var pick = rng.Below(Total);
        for (var i = 0; i < Counts.Length; i++)
        {
            if (pick < Counts[i]) return (uint)(i + 1);
            pick -= Counts[i];
        }
        return null;
    }

    public bool Equals(Levels? other) => other is not null && Counts.SequenceEqual(other.Counts);

    public override bool Equals(object? obj) => Equals(obj as Levels);

    public override int GetHashCode() => Counts.Aggregate(17, (h, n) => h * 31 + (int)n);
}

/// <summary>The player's side of the turn.</summary>
public sealed class Turn
{
    /// <summary>The soul of each character on stage, every bonus included (the climax's…).</summary>
    public uint[] Souls { get; init; } = new uint[Rules.Characters];
    /// <summary>In the order they happen.</summary>
    public List<Source> Sources { get; init; } = [];
    /// <summary>What is left in the player's deck, for the trigger checks.</summary>
    public DeckState Deck { get; init; } = new();
    /// <summary>The player's deck at the start of the turn, for Musashi effects.</summary>
    public Levels Musashi { get; set; } = new();
}

/// <summary>The defending player.</summary>
public sealed record Opponent
{
    public uint DeckCards { get; set; }
    public uint DeckClimaxes { get; set; }
    public uint WaitingRoomCards { get; set; }
    public uint WaitingRoomClimaxes { get; set; }
    /// <summary>Cards in the stock: out of the deck, none of them a climax.</summary>
    public uint Stock { get; set; }
    /// <summary>0 to 3.</summary>
    public uint Level { get; set; }
    /// <summary>Cards in the clock, 0 to 6.</summary>
    public uint Clock { get; set; }

    /// <summary>Level 3 with 8 stock, just refreshed.</summary>
    public static Opponent Default()
    {
        var o = new Opponent { Stock = 8, Level = 3 };
        o.Refresh();
        return o;
    }

    /// <summary>A deck just refreshed: every card not in the stock, level or clock is in the deck, with all 8 climaxes.</summary>
    public void Refresh()
    {
        DeckCards = Rules.DeckSize - Math.Min(Rules.DeckSize, Stock + Level + Clock);
        DeckClimaxes = Math.Min(Rules.DeckClimaxes, DeckCards);
        WaitingRoomCards = 0;
        WaitingRoomClimaxes = 0;
    }

    /// <summary>Damage that kills the opponent: what is left before level 4.</summary>
    public uint DamageToKill =>
        (Rules.LethalLevel - 1 - Math.Min(Rules.LethalLevel - 1, Level)) * Rules.ClockSize
        + (Rules.ClockSize - Math.Min(Rules.ClockSize, Clock));

    /// <summary>Cards of the opponent's deck the page knows of: deck, waiting room, stock, clock and level.</summary>
    public uint Cards => DeckCards + WaitingRoomCards + Stock + Clock + Level;

    public uint Climaxes => DeckClimaxes + WaitingRoomClimaxes;

    /// <summary>Why this state cannot be simulated, if it cannot.</summary>
    public string? Problem()
    {
        if (Cards > Rules.DeckSize)
            return "The opponent has more than 50 cards in their deck, waiting room, stock, clock and level.";
        if (Climaxes > Rules.DeckClimaxes)
            return "The opponent has more than 8 climaxes in their deck and waiting room.";
        if (DeckClimaxes > DeckCards)
            return "The opponent's deck has more climaxes than cards.";
        if (WaitingRoomClimaxes > WaitingRoomCards)
            return "The opponent's waiting room has more climaxes than cards.";
        if (DeckCards + WaitingRoomCards == 0)
            return "The opponent has no cards in their deck or waiting room.";
        return null;
    }
}

/// <summary>What one source did, summed over all trials.</summary>
public sealed class SourceStats
{
    /// <summary>Trials where it happened (its condition was met).</summary>
    public uint Happened { get; set; }
    /// <summary>Damage it dealt, with its trigger's shot for an attack.</summary>
    public ulong Damage { get; set; }
    /// <summary>For an attack: trials where its damage was cancelled.</summary>
    public uint Cancels { get; set; }
}

public sealed class Outcome
{
    public uint Trials { get; init; }
    /// <summary>Histogram[n]: trials where the opponent took n damage in total.</summary>
    public List<uint> Histogram { get; init; } = [];
    /// <summary>Trials where the opponent reached level 4 (or could not refresh).</summary>
    public uint Kills { get; set; }
    public uint DamageToKill { get; init; }
    /// <summary>One per source of the turn, in the same order.</summary>
    public List<SourceStats> Sources { get; init; } = [];

    private double PerTrial(double n) => n / Math.Max(1u, Trials);

    public double Mean() => PerTrial(Histogram.Select((t, n) => (double)n * t).Sum());

    /// <summary>The chance of exactly <paramref name="n"/> damage.</summary>
    public double ChanceOf(int n) => PerTrial(n < Histogram.Count ? Histogram[n] : 0);

    /// <summary>The chance of <paramref name="n"/> damage or more.</summary>
    public double ChanceAtLeast(int n) => PerTrial(Histogram.Skip(n).Sum(t => (double)t));

    public double KillChance() => PerTrial(Kills);

    /// <summary>The damage reached or exceeded in half of the trials.</summary>
    public int Median()
    {
        var half = (Trials + 1) / 2;
        uint seen = 0;
        for (var n = 0; n < Histogram.Count; n++)
        {
            seen += Histogram[n];
            if (seen >= half) return n;
        }
        return 0;
    }
}

public static class Simulator
{
    /// <summary>What the last attack did, for the sources that depend on it.</summary>
    private record struct LastAttack(bool Cancelled, bool ClimaxTrigger);

    /// <summary>
    /// Plays the turn <paramref name="trials"/> times; <paramref name="seed"/> picks the random
    /// sequence (the same seed gives the same outcome).
    /// </summary>
    public static Outcome Simulate(Turn turn, Opponent opponent, uint trials, ulong seed)
    {
        var rng = new Rng(seed);
        var outcome = new Outcome
        {
            Trials = trials,
            DamageToKill = opponent.DamageToKill,
            Sources = turn.Sources.Select(_ => new SourceStats()).ToList(),
        };
        for (var trial = 0u; trial < trials; trial++)
        {
            var opp = new Defender(opponent);
            var deck = turn.Deck.Clone();
            // Trigger soul stays on the character for the turn.
            var souls = (uint[])turn.Souls.Clone();
            // Before any attack, nothing was cancelled.
            var last = new LastAttack();
            for (var s = 0; s < turn.Sources.Count; s++)
            {
                var source = turn.Sources[s];
                var stats = outcome.Sources[s];
                var before = opp.Taken;
                bool happened;
                switch (source.Kind)
                {
                    case SourceKind.Attack:
                        var character = Math.Clamp(source.Character, 0, Rules.Characters - 1);
                        var soul = source.Attack switch
                        {
                            AttackKind.Front => souls[character],
                            AttackKind.Side => souls[character] - Math.Min(souls[character], source.DefenderLevel),
                            _ => souls[character] + 1,
                        };
                        var trigger = deck.Draw(rng);
                        var triggerSoul = trigger?.Soul ?? 0;
                        souls[character] += triggerSoul;
                        var cancelled = opp.Damage(soul + triggerSoul, rng);
                        if (cancelled)
                        {
                            stats.Cancels++;
                            if (trigger is { Shot: true }) opp.Damage(1, rng);
                        }
                        last = new LastAttack(cancelled, trigger is { Climax: true });
                        happened = true;
                        break;
                    case SourceKind.CancelBurn or SourceKind.CancelMusashi:
                        happened = last.Cancelled;
                        break;
                    case SourceKind.NotCancelledBurn:
                        happened = !last.Cancelled;
                        break;
                    case SourceKind.ClimaxTriggerBurn:
                        happened = last.ClimaxTrigger;
                        break;
                    case SourceKind.Mill:
                        opp.Mill(source.Amount, rng);
                        happened = true;
                        break;
                    default:
                        happened = true;
                        break;
                }
                if (!happened) continue;
                switch (source.Kind)
                {
                    case SourceKind.Burn or SourceKind.CancelBurn or SourceKind.NotCancelledBurn or SourceKind.ClimaxTriggerBurn:
                        opp.Damage(source.Amount, rng);
                        break;
                    case SourceKind.Musashi or SourceKind.CancelMusashi:
                        if (turn.Musashi.Musashi(rng) is { } n) opp.Damage(n, rng);
                        break;
                }
                stats.Happened++;
                stats.Damage += opp.Taken - before;
            }
            var taken = (int)opp.Taken;
            while (outcome.Histogram.Count <= taken) outcome.Histogram.Add(0);
            outcome.Histogram[taken]++;
            if (opp.Dead) outcome.Kills++;
        }
        return outcome;
    }

    /// <summary>A random seed, different on each run.</summary>
    public static ulong RandomSeed() => (ulong)Random.Shared.NextInt64();
}

/// <summary>A pile of cards, of which some are climaxes.</summary>
internal struct Pile
{
    public uint Cards;
    public uint Climaxes;

    /// <summary>Takes a random card; returns whether it is a climax. The pile must not be empty.</summary>
    public bool Draw(Rng rng)
    {
        var climax = rng.Below(Cards) < Climaxes;
        Cards--;
        if (climax) Climaxes--;
        return climax;
    }

    public void Put(bool climax)
    {
        Cards++;
        if (climax) Climaxes++;
    }

    public void PutAll(Pile other)
    {
        Cards += other.Cards;
        Climaxes += other.Climaxes;
    }
}

/// <summary>The opponent during one trial.</summary>
internal sealed class Defender
{
    public Pile Deck;
    public Pile WaitingRoom;
    public Pile Clock;
    public uint Level;
    /// <summary>Damage taken this turn.</summary>
    public uint Taken;
    public bool Dead;

    public Defender(Opponent o)
    {
        Deck = new Pile { Cards = o.DeckCards, Climaxes = o.DeckClimaxes };
        WaitingRoom = new Pile { Cards = o.WaitingRoomCards, Climaxes = o.WaitingRoomClimaxes };
        // The cards already in the clock are not climaxes: a climax cancels damage.
        Clock = new Pile { Cards = Math.Min(o.Clock, Rules.ClockSize - 1) };
        Level = o.Level;
        Dead = o.Level >= Rules.LethalLevel;
    }

    /// <summary>
    /// Refreshes when the deck is empty: the waiting room becomes the deck. The rules do it at
    /// once, even in the middle of a damage.
    /// </summary>
    private void RefreshIfEmpty(ref uint refreshes)
    {
        if (Deck.Cards == 0 && WaitingRoom.Cards > 0)
        {
            Deck = WaitingRoom;
            WaitingRoom = default;
            refreshes++;
        }
    }

    /// <summary>
    /// Takes the top card of the deck; returns whether it is a climax. Null when there is nothing
    /// left, even to refresh with: the opponent loses.
    /// </summary>
    private bool? Reveal(ref uint refreshes, Rng rng)
    {
        RefreshIfEmpty(ref refreshes);
        if (Deck.Cards == 0)
        {
            Dead = true;
            return null;
        }
        var climax = Deck.Draw(rng);
        RefreshIfEmpty(ref refreshes);
        return climax;
    }

    /// <summary>Deals <paramref name="n"/> damage; returns whether it was cancelled.</summary>
    public bool Damage(uint n, Rng rng)
    {
        if (n == 0) return false;
        uint refreshes = 0;
        var revealed = new Pile();
        var cancelled = false;
        for (var i = 0u; i < n; i++)
        {
            if (Reveal(ref refreshes, rng) is not { } climax) break;
            revealed.Put(climax);
            if (climax)
            {
                cancelled = true;
                break;
            }
        }
        if (cancelled) WaitingRoom.PutAll(revealed);
        else
            for (var i = 0u; i < revealed.Cards; i++)
                PutInClock(false);
        PayRefreshes(refreshes, rng);
        return cancelled;
    }

    /// <summary>The refresh penalty: the top card of the deck goes to the clock, cancelled by nothing.</summary>
    private void PayRefreshes(uint refreshes, Rng rng)
    {
        while (refreshes > 0)
        {
            refreshes--;
            if (Reveal(ref refreshes, rng) is { } climax) PutInClock(climax);
        }
    }

    private void PutInClock(bool climax)
    {
        Clock.Put(climax);
        Taken++;
        if (Clock.Cards < Rules.ClockSize) return;
        // Level up: 1 card to the level zone (a non-climax when possible), the rest to the
        // waiting room.
        var toLevelIsClimax = Clock.Climaxes == Clock.Cards;
        var rest = Clock;
        Clock = default;
        rest.Cards--;
        if (toLevelIsClimax) rest.Climaxes--;
        WaitingRoom.PutAll(rest);
        Level++;
        if (Level >= Rules.LethalLevel) Dead = true;
    }

    public void Mill(uint n, Rng rng)
    {
        uint refreshes = 0;
        for (var i = 0u; i < n; i++)
        {
            if (Reveal(ref refreshes, rng) is not { } climax) break;
            WaitingRoom.Put(climax);
        }
        PayRefreshes(refreshes, rng);
    }
}

/// <summary>A small, fast random number generator (SplitMix64): plenty for shuffling cards.</summary>
internal sealed class Rng(ulong seed)
{
    private ulong _state = seed;

    public ulong Next()
    {
        unchecked
        {
            _state += 0x9E37_79B9_7F4A_7C15;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58_476D_1CE4_E5B9;
            z = (z ^ (z >> 27)) * 0x94D0_49BB_1331_11EB;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A number from 0 to n - 1.</summary>
    public uint Below(uint n) => (uint)(((Next() >> 32) * n) >> 32);
}
