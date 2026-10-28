using WeissSchwarzSimulatorTools.DamageSim;
using Sim = WeissSchwarzSimulatorTools.DamageSim.Simulator;

namespace WeissSchwarzSimulatorTools.Tests;

public class DamageSimTests
{
    private static DeckState Deck(int nonClimax, int climaxes)
    {
        var d = new DeckState();
        d.Add(new TriggerKind(false, 0, false), nonClimax);
        d.Add(new TriggerKind(true, 0, false), climaxes);
        return d;
    }

    /// <summary>The three characters attack, front, in order.</summary>
    private static Turn ThreeAttacks(uint[] souls, DeckState own) => new()
    {
        Souls = souls,
        Sources = Enumerable.Range(0, Rules.Characters).Select(Source.AttackBy).ToList(),
        Deck = own,
    };

    /// <summary>Only the first character attacks, then <paramref name="then"/>.</summary>
    private static Turn OneAttack(uint soul, DeckState own, SourceKind then) => new()
    {
        Souls = [soul, 0, 0],
        Sources = [Source.AttackBy(0), new Source(then, 1)],
        Deck = own,
    };

    private static Opponent Opp(uint cards, uint climaxes) => new() { DeckCards = cards, DeckClimaxes = climaxes };

    [Fact]
    public void NoClimaxMeansEveryDamageSticks()
    {
        var output = Sim.Simulate(ThreeAttacks([2, 2, 3], Deck(20, 0)), Opp(30, 0), 1000, 1);
        Assert.Equal(1000u, output.Histogram[7]);
        Assert.Equal(7.0, output.Mean());
        Assert.Equal(3000ul, output.Sources[2].Damage);
    }

    [Fact]
    public void OnlyClimaxesCancelEverything()
    {
        var output = Sim.Simulate(ThreeAttacks([2, 2, 2], Deck(20, 0)), Opp(10, 10), 500, 2);
        Assert.Equal([500u], output.Histogram);
        Assert.Equal(500u, output.Sources[0].Cancels);
    }

    [Fact]
    public void OneDamageCancelRateMatchesTheClimaxRatio()
    {
        var t = ThreeAttacks([1, 1, 1], Deck(20, 0));
        t.Sources.RemoveRange(1, 2);
        var output = Sim.Simulate(t, Opp(40, 10), 100_000, 3);
        var rate = output.Sources[0].Cancels / 100_000.0;
        Assert.True(Math.Abs(rate - 0.25) < 0.01, rate.ToString());
    }

    [Fact]
    public void AttackKindsChangeSoul()
    {
        var t = ThreeAttacks([2, 2, 2], Deck(20, 0));
        t.Sources[0] = t.Sources[0] with { Attack = AttackKind.Direct };
        t.Sources[1] = t.Sources[1] with { Attack = AttackKind.Side, DefenderLevel = 3 };
        t.Sources.RemoveAt(2);
        Assert.Equal(10u, Sim.Simulate(t, Opp(30, 0), 10, 4).Histogram[3]);
    }

    [Fact]
    public void SoulTriggersAddDamage()
    {
        var own = new DeckState();
        own.Add(new TriggerKind(false, 2, false), 10);
        Assert.Equal(10u, Sim.Simulate(ThreeAttacks([1, 1, 1], own), Opp(30, 0), 10, 5).Histogram[9]);
    }

    [Fact]
    public void ARestandedCharacterKeepsItsTriggerSoul()
    {
        // 1 soul, +1 trigger: 2 damage; again with the first +1 and a new +1: 3 damage.
        var own = new DeckState();
        own.Add(new TriggerKind(false, 1, false), 10);
        var t = new Turn { Souls = [1, 1, 1], Sources = [Source.AttackBy(0), Source.AttackBy(0)], Deck = own };
        var output = Sim.Simulate(t, Opp(30, 0), 10, 6);
        Assert.Equal(10u, output.Histogram[5]);
        Assert.Equal(30ul, output.Sources[1].Damage);
    }

    [Fact]
    public void BurnsAndMills()
    {
        var t = new Turn
        {
            Sources = [new Source(SourceKind.Burn, 3), new Source(SourceKind.Mill, 5), new Source(SourceKind.Burn, 2)],
        };
        var output = Sim.Simulate(t, Opp(30, 0), 10, 7);
        Assert.Equal(10u, output.Histogram[5]);
        Assert.Equal(0ul, output.Sources[1].Damage);
    }

    [Fact]
    public void CancelledAttacksTriggerCancelBurnsAndShots()
    {
        var own = new DeckState();
        own.Add(new TriggerKind(true, 1, true), 5);
        var t = OneAttack(0, own, SourceKind.CancelBurn);
        // The deck is a climax then nothing but non-climaxes: the attack is cancelled, the shot
        // and the effect stick.
        var opp = Opp(1, 1);
        opp.WaitingRoomCards = 10;
        var output = Sim.Simulate(t, opp, 10, 8);
        Assert.Equal(10u, output.Sources[0].Cancels);
        Assert.Equal(10u, output.Sources[1].Happened);
        // 1 refresh penalty + shot + cancel burn.
        Assert.Equal(10u, output.Histogram[3]);
    }

    [Fact]
    public void ConditionsFollowTheLastAttack()
    {
        var t = OneAttack(1, Deck(20, 0), SourceKind.NotCancelledBurn);
        Assert.Equal(10u, Sim.Simulate(t, Opp(30, 0), 10, 9).Histogram[2]);
        Assert.Equal([10u], Sim.Simulate(t, Opp(10, 10), 10, 9).Histogram);
        t = OneAttack(1, Deck(20, 0), SourceKind.CancelBurn);
        var output = Sim.Simulate(t, Opp(30, 0), 10, 9);
        Assert.Equal((10u, 0u), (output.Histogram[1], output.Sources[1].Happened));
        t = OneAttack(1, Deck(0, 10), SourceKind.ClimaxTriggerBurn);
        Assert.Equal(10u, Sim.Simulate(t, Opp(30, 0), 10, 9).Histogram[2]);
    }

    [Fact]
    public void RefreshAddsOneDamage()
    {
        var t = ThreeAttacks([3, 0, 0], Deck(20, 0));
        t.Sources.RemoveRange(1, 2);
        var opp = Opp(2, 0);
        opp.WaitingRoomCards = 10;
        Assert.Equal(10u, Sim.Simulate(t, opp, 10, 9).Histogram[4]);
    }

    [Fact]
    public void AnEmptyDeckAndWaitingRoomIsAWin()
    {
        var t = ThreeAttacks([5, 0, 0], Deck(20, 0));
        t.Sources.RemoveRange(1, 2);
        Assert.Equal(10u, Sim.Simulate(t, Opp(2, 0), 10, 10).Kills);
    }

    [Fact]
    public void KillsCountReachingLevelFour()
    {
        var t = ThreeAttacks([2, 2, 3], Deck(20, 0));
        var opp = Opp(30, 0);
        opp.Level = 3;
        Assert.Equal(7u, opp.DamageToKill);
        Assert.Equal(10u, Sim.Simulate(t, opp, 10, 11).Kills);
        opp.Level = 2;
        Assert.Equal(0u, Sim.Simulate(t, opp, 10, 11).Kills);
    }

    [Fact]
    public void LevelUpSendsTheClockToTheWaitingRoom()
    {
        var d = new Defender(Opp(10, 0));
        d.Damage(7, new Rng(1));
        Assert.Equal((1u, 0u, 6u), (d.Level, d.Clock.Cards, d.WaitingRoom.Cards));
    }

    [Fact]
    public void TheDefaultOpponentJustRefreshed()
    {
        var o = Opponent.Default();
        Assert.Equal((39u, 8u, 8u, 3u), (o.DeckCards, o.DeckClimaxes, o.Stock, o.Level));
        Assert.Equal(50u, o.Cards);
        Assert.Null(o.Problem());
    }

    [Fact]
    public void Statistics()
    {
        var output = new Outcome { Trials = 4, Histogram = [1, 0, 2, 1] };
        Assert.Equal(1.75, output.Mean());
        Assert.Equal(2, output.Median());
        Assert.Equal(0.75, output.ChanceAtLeast(2));
        Assert.Equal(0.25, output.ChanceOf(0));
    }

    [Fact]
    public void DeckStateCountsTriggerKinds()
    {
        Card C(CardType type, uint level, params Trigger[] triggers) => Cards.Make("XX/Y1-001", type, CardColor.Yellow, level, triggers: triggers);
        Card[] cards =
        [
            C(CardType.Character, 1, Trigger.Soul),
            C(CardType.Character, 2, Trigger.Soul, Trigger.Soul),
            C(CardType.Character, 0),
            C(CardType.Climax, 0, Trigger.Choice),
            C(CardType.Climax, 0, Trigger.Burn, Trigger.Soul),
        ];
        var state = DeckState.Of(cards);
        Assert.Equal([1u, 1, 1, 1, 0, 0, 1], state.Counts);
        Assert.Equal((5u, 2u), (state.Total, state.Climaxes));
        Assert.Equal(new Levels(3, 1, 1, 0), Levels.Of(cards));
    }

    [Fact]
    public void MusashiDealsTheLevelPlusOne()
    {
        var t = new Turn
        {
            Sources = [new Source(SourceKind.Musashi, 0), new Source(SourceKind.Musashi, 0)],
            Musashi = new Levels(0, 0, 10, 0),
        };
        Assert.Equal(100u, Sim.Simulate(t, Opp(30, 0), 100, 6).Histogram[6]);
    }

    [Fact]
    public void MusashiDamageIsTheAverageLevelPlusOne()
    {
        Assert.Null(new Levels().MusashiDamage());
        Assert.Equal(1.75, new Levels(3, 0, 0, 1).MusashiDamage());
    }

    [Fact]
    public void CancelMusashiOnlyOnCancel()
    {
        var t = OneAttack(1, Deck(10, 0), SourceKind.CancelMusashi);
        t.Musashi = new Levels(0, 10, 0, 0);
        Assert.Equal(100u, Sim.Simulate(t, Opp(30, 0), 100, 8).Histogram[1]);
    }
}
