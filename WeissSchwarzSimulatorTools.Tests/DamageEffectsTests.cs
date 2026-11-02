using WeissSchwarzSimulatorTools.DamageSim;
using WeissSchwarzSimulatorTools.Simulator;
using Sim = WeissSchwarzSimulatorTools.DamageSim.Simulator;

namespace WeissSchwarzSimulatorTools.Tests;

public class DamageEffectsTests
{
    private static Card Char(string code, string name = "Hero") => new()
    {
        Key = "T/W01-001",
        Name = name,
        Type = CardType.Character,
        Code = code.Replace("\r", ""),
    };

    private static Card Climax(string name) => new() { Key = "T/W01-100", Name = name, Type = CardType.Climax };

    private static DamageEffect Single(Card card, Card? climax = null) => Assert.Single(DamageEffects.Read(card, climax));

    [Fact]
    public void BurnWhenAttacking()
    {
        var e = Single(Char("""
            Auto: OnAttack
            {
            	Burn 2
            }
            """));
        Assert.Equal(EffectTiming.Attack, e.Timing);
        Assert.Equal([new DamageAction(SourceKind.Burn, 2)], e.Actions);
        Assert.Empty(e.Conditions);
    }

    [Fact]
    public void ClimaxComboNeedsItsClimax()
    {
        var card = Char("""
            Auto: OnAttack
            {
            	If Climax Promise From Before && StageContains 2 Other
            	{
            		Burn 1
            	}
            }
            """);
        Assert.Empty(DamageEffects.Read(card, null));
        Assert.Empty(DamageEffects.Read(card, Climax("Another One")));
        var e = Single(card, Climax("Promise From Before"));
        Assert.Equal(["StageContains 2 Other"], e.Conditions);
    }

    [Fact]
    public void ElseIsTakenWhenTheClimaxIsMissing()
    {
        var e = Single(Char("""
            Auto: OnAttack
            {
            	If Climax Big
            	{
            		Burn 3
            	}
            	Else
            	{
            		Burn 1
            	}
            }
            """));
        Assert.Equal(1u, e.Actions[0].Amount);
    }

    [Fact]
    public void QuestionsCostsAndCommonEffectsAreTaken()
    {
        // *OptionalBurn(1) as CardFile expands it, inside a cost.
        var e = Single(Char("""
            Auto: OnDamageCancel
            {
            	PayCost 1
            	{
            		If Compare 1 1 OrMore
            		{
            			Question_Obvious Deal 1 damage to your opponent?
            			If Yes
            			{
            				Burn 1
            			}
            			If No
            			{
            				Burn 5
            			}
            		}
            	}
            }
            TimesPerTurn 1
            """));
        Assert.Equal(EffectTiming.Cancelled, e.Timing);
        Assert.Equal([new DamageAction(SourceKind.Burn, 1)], e.Actions);
    }

    [Fact]
    public void ClockKickTopClockAndSwap()
    {
        var kick = Single(Char("""
            Auto: OnReverseOpponent
            {
            	Question_Obvious Put battle opponent into clock?
            	If Yes
            	{
            		TargetOther
            		MoveCard OpponentClock
            	}
            }
            """));
        Assert.Equal(EffectTiming.Battle, kick.Timing);
        Assert.Equal([new DamageAction(SourceKind.ClockKick, 1)], kick.Actions);

        var top = Single(Char("""
            Auto: OnAttack
            {
            	TargetTop OpponentDeck 2
            	MoveCard OpponentClock
            }
            """));
        Assert.Equal([new DamageAction(SourceKind.TopClock, 2)], top.Actions);

        var swap = Single(Char("""
            Auto: OnReverseOpponent
            {
            	TargetLocation OpponentClock
            	If HasTarget
            	{
            		TargetTop OpponentClock 1
            		MoveCard OpponentWaitingRoom
            		TargetBattleOpponent
            		MoveCard OpponentClock
            	}
            }
            """));
        Assert.Equal([new DamageAction(SourceKind.ClockSwap, 1)], swap.Actions);
    }

    [Fact]
    public void MusashiFromTheTopOfTheDeck()
    {
        var e = Single(Char("""
            Auto: OnAttack
            {
            	Mill 1
            	SetXValue TargetCardsLevel
            	AddToXValue 1
            	Burn X
            }
            """));
        Assert.Equal([new DamageAction(SourceKind.Musashi, 1)], e.Actions);
    }

    [Fact]
    public void UnknownXIsAGuess()
    {
        var e = Single(Char("""
            Auto: OnAttack
            {
            	TargetLocation Stage
            	SetXValue TargetCount
            	Burn X
            }
            """));
        Assert.Equal(SourceKind.Burn, e.Actions[0].Kind);
        Assert.Equal("TargetCount", e.Actions[0].Guess);
    }

    [Fact]
    public void RepeatAndChoices()
    {
        var e = Single(Char("""
            Auto: OnAttack
            {
            	MultipleChoiceQuestion Choose|Effect 1|Effect 2
            	If Answer 0
            	{
            		Burn 1
            	}
            	If Answer 1
            	{
            		Repeat 2
            		{
            			Burn 2
            		}
            	}
            }
            """));
        Assert.Equal([new DamageAction(SourceKind.Burn, 2), new DamageAction(SourceKind.Burn, 2)], e.Actions);
    }

    [Fact]
    public void GivenAbilities()
    {
        var effects = DamageEffects.Read(Char("""
            Auto: OnSpecificClimaxPlay
            {
            	If Climax Night
            	{
            		TargetLocation Stage
            		ChooseCharacterFromTargets Choose a character.
            		GainPower 2000
            		GainEffect
            		{
            			Auto: OnOpponentReverse
            			{
            				Burn 5
            			}
            		}
            	}
            }
            Cont: GiveAll
            {
            	TargetLocation Stage
            	GainEffect
            	{
            		Auto: OnDamageNotCanceled
            		{
            			Burn 1
            		}
            	}
            }
            """), Climax("Night"));
        Assert.Equal(2, effects.Count);
        Assert.Equal((EffectTiming.Battle, EffectOwner.Chosen), (effects[0].Timing, effects[0].Owner));
        Assert.Equal((EffectTiming.NotCancelled, EffectOwner.All), (effects[1].Timing, effects[1].Owner));
    }

    [Fact]
    public void LastDamageConditionMakesACancelEffect()
    {
        var e = Single(Char("""
            Auto: OnEndOfAttack
            {
            	If LastDamageCanceled
            	{
            		Burn 1
            	}
            }
            """));
        Assert.Equal(EffectTiming.Cancelled, e.Timing);
    }

    [Fact]
    public void OpponentTurnEffectsAreLeftOut()
    {
        Assert.Empty(DamageEffects.Read(Char("""
            Auto: OnFrontalAttacked
            {
            	Burn 1
            }
            Auto: OnAttack
            {
            	If OpponentsTurn
            	{
            		Burn 1
            	}
            }
            """), null));
    }

    [Fact]
    public void TurnPutsTheEffectsAroundTheAttacks()
    {
        var first = Char("""
            Auto: OnAttack
            {
            	Burn 1
            }
            Auto: OnDamageCancel
            {
            	Burn 2
            }
            """, "First");
        var third = Char("""
            Auto: OnReverseOpponent
            {
            	TargetBattleOpponent
            	MoveCard OpponentClock
            }
            Auto: StartMyEncoreStep
            {
            	Burn 3
            }
            """, "Third");
        var sources = TurnBuilder.Build([first, null, third], null);
        Assert.Equal(
            [
                (SourceKind.Burn, 1u, 0), (SourceKind.Attack, 0u, 0), (SourceKind.CancelBurn, 2u, 0),
                (SourceKind.Attack, 0u, 1),
                (SourceKind.Attack, 0u, 2), (SourceKind.ClockKick, 1u, 0),
                (SourceKind.Burn, 3u, 0),
            ],
            sources.Select(s => (s.Kind, s.Amount, s.Character)));
        Assert.Equal("First · When it attacks", sources[0].Origin);
    }

    [Fact]
    public void BuildKeepsTheAttacksSetUp()
    {
        var attacks = new List<Source> { new(SourceKind.Attack, 0, 2, AttackKind.Direct), new(SourceKind.Attack, 0, 0, AttackKind.Side, 2) };
        Assert.Equal(attacks, TurnBuilder.Build([null, null, null], null, attacks));
    }

    private static Card ClimaxWith(string code) => new() { Key = "T/W01-100", Name = "Cx", Type = CardType.Climax, Code = code.Replace("\r", "") };

    [Fact]
    public void ClimaxSoulForAll()
    {
        Assert.Equal([new SoulBonus(1, 0, null)], DamageEffects.ClimaxSouls(ClimaxWith("""
            Cont: GiveAll
            {
            	GainPower 1000
            	GainSoul 1
            }
            """)));
        Assert.Equal([new SoulBonus(1, 0, null)], DamageEffects.ClimaxSouls(ClimaxWith("""
            Auto: OnPlay
            {
            	TargetTop Deck 1
            	MoveCard Stock
            	TargetLocation Stage
            	GainSoul 1
            }
            """)));
    }

    [Fact]
    public void ClimaxSoulToChoose()
    {
        Assert.Equal([new SoulBonus(3, 1, null)], DamageEffects.ClimaxSouls(ClimaxWith("""
            Auto: OnPlay
            {
            	DrawCard
            	TargetLocation Stage
            	ChooseCharacterFromTargets Choose a character to give +3 soul.
            	GainSoul 3
            }
            """)));
        Assert.Equal([new SoulBonus(2, 2, "IsColor Red")], DamageEffects.ClimaxSouls(ClimaxWith("""
            Auto: OnPlay
            {
            	ChooseMultipleCharacters 0 2 InLocation Stage && IsColor Red
            	GainPower 2000
            	GainSoul 2
            }
            """)));
        Assert.Equal([new SoulBonus(1, 2, null)], DamageEffects.ClimaxSouls(ClimaxWith("""
            Auto: OnPlay
            {
            	Question Draw a card?
            	If Yes
            	{
            		DrawCard
            	}
            	TargetLocation Stage
            	ChooseCharactersFromTargets 0 2 choose up to 2 of your characters, and they gain +1 soul
            	GainSoul 1
            }
            """)));
    }

    [Fact]
    public void ClimaxSoulOnlyForSomeCharacters()
    {
        var red = new Card { Key = "a", Color = CardColor.Red, Traits = ["Magic"] };
        Assert.True(DamageEffects.Allows("IsColor Red", red));
        Assert.False(DamageEffects.Allows("ColorIs Blue", red));
        Assert.True(DamageEffects.Allows("HasTrait Idol|Magic", red));
        Assert.Null(DamageEffects.Allows("Compare ThatCardsLevel 1 OrLess", red));
        Assert.Null(DamageEffects.Allows("IsColor Red", null));
        Assert.Equal([new SoulBonus(1, 0, "HasTrait Magic")], DamageEffects.ClimaxSouls(ClimaxWith("""
            Cont: GiveAll
            {
            	NarrowTargets HasTrait Magic
            	GainSoul 1
            }
            """)));
    }

    private const string ChoiceScript = """
        Auto: OnAttack
        {
        	If Climax Night
        	{
        		MultipleChoiceQuestion Choose an effect :\n1: Deal 1 damage to your opponent.\n2: Look at up to 2 cards from the top of your opponent's deck.|1|2
        		If Answer 0
        		{
        			Burn 1
        		}
        		If Answer 1
        		{
        			Peek OpponentDeck 2
        			ChooseCardsFromTargets 0 2 Any
        			MoveCard OpponentWaitingRoom
        			EndPeek
        		}
        	}
        }
        """;

    [Fact]
    public void ChoiceOfEffectsKeepsEveryOption()
    {
        var e = Single(Char(ChoiceScript), Climax("Night"));
        Assert.True(e.HasOptions);
        Assert.Equal(["1. Deal 1 damage to your opponent.", "2. Look at up to 2 cards from the top of your opponent's deck."], e.Options!.Select(o => o.Label));
        // The burn deals the most damage: chosen by default.
        Assert.Equal(0, e.Chosen);
        Assert.Equal([new DamageAction(SourceKind.Burn, 1)], e.Actions);
        Assert.Equal([new DamageAction(SourceKind.LookTop, 2, ClimaxTo: LookClimaxes.WaitingRoom)], e.Choose(1).Actions);

        var card = Char(ChoiceScript);
        var choice = Assert.Single(TurnBuilder.Choices([card, null, null], Climax("Night")));
        Assert.Equal(0, choice.Character);
        var picked = TurnBuilder.Build([card, null, null], Climax("Night"), choices: new Dictionary<string, int> { [choice.Key] = 1 });
        Assert.Equal(SourceKind.LookTop, picked[0].Kind);
    }

    [Fact]
    public void EachCharacterPicksItsOwnOption()
    {
        // The same card on two characters, and an ability given to all: one choice per attacker.
        var card = Char(ChoiceScript);
        var night = Climax("Night");
        var choices = TurnBuilder.Choices([card, card, null], night);
        Assert.Equal([0, 1], choices.Select(c => c.Character));
        var sources = TurnBuilder.Build([card, card, null], night, choices: new Dictionary<string, int> { [choices[1].Key] = 1 });
        Assert.Equal(
            [SourceKind.Burn, SourceKind.Attack, SourceKind.LookTop, SourceKind.Attack, SourceKind.Attack],
            sources.Select(s => s.Kind));

        var giver = Char("Cont: GiveAll\n{\nTargetLocation Stage\nGainEffect\n{\n" + ChoiceScript + "\n}\n}");
        Assert.Equal([0, 1, 2], TurnBuilder.Choices([giver, null, null], night).Select(c => c.Character));
    }

    [Fact]
    public void PlacementQuestionsAreAnsweredForTheDamage()
    {
        // "Top or bottom?" is not a choice of effects: the climax goes to the bottom.
        var e = Single(Char("""
            Auto: OnAttack
            {
            	PeekTop OpponentDeck
            	MultipleChoiceQuestion Put this card on the top or bottom of your opponent's deck?\n1. Top \n2. Bottom|1|2
            	If Answer 0
            	{
            		MoveCard OpponentDeck
            	}
            	If Answer 1
            	{
            		MoveCardToBottom OpponentDeck
            	}
            	EndPeek OpponentDeck
            }
            """));
        Assert.False(e.HasOptions);
        Assert.Equal([new DamageAction(SourceKind.LookTop, 1, ClimaxTo: LookClimaxes.Bottom)], e.Actions);
    }

    [Fact]
    public void OpponentDeckEffects()
    {
        DamageAction One(string body) => Assert.Single(Single(Char("Auto: OnAttack\n{\n" + body + "\n}")).Actions);

        Assert.Equal(new DamageAction(SourceKind.WaitingRoomToDeck, 1), One("SearchLocationForTargets OpponentWaitingRoom 1 1 Any\nMoveCard OpponentDeck"));
        Assert.Equal(new DamageAction(SourceKind.WaitingRoomToDeck, 2, Shuffle: true),
            One("SearchLocationForTargets OpponentWaitingRoom 0 2 Any\nMoveCard OpponentDeck\nShuffleLocation OpponentDeck"));
        Assert.Equal(new DamageAction(SourceKind.ClimaxToDeck, 1),
            One("OpponentSearchLocationForTargets WaitingRoom 1 1 IsClimax\nMoveCard OpponentDeck\nShuffleLocation OpponentDeck"));
        Assert.Equal(new DamageAction(SourceKind.CharacterToTop, 1), One("TargetBattleOpponent\nMoveCard OpponentDeck"));
        Assert.Equal(new DamageAction(SourceKind.CharacterToBottom, 1), One("TargetBattleOpponent\nMoveCardToBottom OpponentDeck"));
        Assert.Equal(new DamageAction(SourceKind.StockToDeck, 0),
            One("TargetLocation OpponentStock\nMoveCard OpponentDeck\nShuffleLocation OpponentDeck\nTargetTop OpponentDeck X\nMoveCard OpponentStock"));
        Assert.Equal(new DamageAction(SourceKind.LookTop, 4, ClimaxTo: LookClimaxes.WaitingRoom, Shuffle: true),
            One("Peek OpponentDeck 4\nChooseCardsFromTargets 0 4 Any\nMoveCard OpponentWaitingRoom\nEndPeek\nShuffleLocation OpponentDeck"));
        Assert.Equal(new DamageAction(SourceKind.LookTop, 2, ClimaxTo: LookClimaxes.Under), One("Peek OpponentDeck 2\nReorder\nEndPeek"));
        Assert.Equal(new DamageAction(SourceKind.Mill, 2), One("TargetTop OpponentDeck 2\nMoveCard OpponentWaitingRoom"));
        // Looking and putting back in the same order changes nothing.
        Assert.Empty(DamageEffects.Read(Char("Auto: OnAttack\n{\nPeek OpponentDeck 2\nEndPeek\n}"), null));
    }

    [Fact]
    public void KnownTopCardsComeFirst()
    {
        // Only climaxes in the deck, but a character put on top: the first damage sticks.
        var opp = new Opponent { DeckCards = 10, DeckClimaxes = 10 };
        var turn = new Turn { Sources = [new Source(SourceKind.CharacterToTop, 1), new Source(SourceKind.Burn, 1)] };
        Assert.Equal(100u, Sim.Simulate(turn, opp, 100, 1).Histogram[1]);
        // Shuffled in: 1 chance in 11 to stick.
        turn.Sources[0] = new Source(SourceKind.CharacterToTop, 1, Shuffle: true);
        Assert.InRange(Sim.Simulate(turn, opp, 10_000, 1).ChanceOf(1), 0.07, 0.12);
        // At the bottom: never seen.
        turn.Sources[0] = new Source(SourceKind.CharacterToBottom, 1);
        Assert.Equal(10_000u, Sim.Simulate(turn, opp, 10_000, 1).Histogram[0]);
        // 2 non-climaxes of the waiting room on top: a 2 damage sticks.
        opp.WaitingRoomCards = 3;
        turn.Sources[0] = new Source(SourceKind.WaitingRoomToDeck, 2);
        turn.Sources[1] = new Source(SourceKind.Burn, 2);
        Assert.Equal(100u, Sim.Simulate(turn, opp, 100, 1).Histogram[2]);
    }

    [Fact]
    public void LookingAtTheTopSendsTheClimaxesAway()
    {
        // 8 cards, 2 climaxes, all looked at: 6 others on top.
        var opp = new Opponent { DeckCards = 8, DeckClimaxes = 2, WaitingRoomCards = 10 };
        Outcome Run(LookClimaxes to, uint burn) => Sim.Simulate(
            new Turn { Sources = [new Source(SourceKind.LookTop, 8, ClimaxTo: to), new Source(SourceKind.Burn, burn)] }, opp, 100, 1);
        Assert.Equal(100u, Run(LookClimaxes.Bottom, 6).Histogram[6]);
        Assert.Equal(100u, Run(LookClimaxes.Under, 6).Histogram[6]);
        // The 7th card is a climax, unless they went to the waiting room.
        Assert.Equal(100u, Run(LookClimaxes.Bottom, 7).Histogram[0]);
        // The deck is then empty: the refresh adds 1.
        Assert.Equal(100u, Run(LookClimaxes.WaitingRoom, 6).Histogram[7]);
        // Without looking, the climaxes cancel it most of the time.
        var blind = Sim.Simulate(new Turn { Sources = [new Source(SourceKind.Burn, 6)] }, opp, 10_000, 1);
        Assert.True(blind.ChanceOf(0) > 0.9);
    }

    [Fact]
    public void ClimaxBackToDeckAndStock()
    {
        // 3 cards and the climax of the waiting room: a 1 damage is cancelled 1 time in 4.
        var opp = new Opponent { DeckCards = 3, WaitingRoomCards = 5, WaitingRoomClimaxes = 1 };
        var turn = new Turn { Sources = [new Source(SourceKind.ClimaxToDeck, 1), new Source(SourceKind.Burn, 1)] };
        Assert.InRange(Sim.Simulate(turn, opp, 10_000, 1).ChanceOf(0), 0.2, 0.3);

        // Stock of 10 shuffled into a deck of 10 climaxes, 10 cards back to the stock: the top
        // card is then a climax half of the time.
        var withStock = new Opponent { DeckCards = 10, DeckClimaxes = 10, Stock = 10 };
        turn = new Turn { Sources = [new Source(SourceKind.StockToDeck, 0), new Source(SourceKind.Burn, 1)] };
        Assert.InRange(Sim.Simulate(turn, withStock, 10_000, 1).ChanceOf(1), 0.4, 0.6);
    }

    [Fact]
    public void KickAndTopClockAreNotCancelled()
    {
        var opp = new Opponent { DeckCards = 10, DeckClimaxes = 10 };
        var turn = new Turn { Sources = [new Source(SourceKind.ClockKick, 2), new Source(SourceKind.TopClock, 3)] };
        Assert.Equal(100u, Sim.Simulate(turn, opp, 100, 1).Histogram[5]);
    }

    [Fact]
    public void SwapDealsNoDamage()
    {
        var opp = new Opponent { DeckCards = 10, DeckClimaxes = 0, Clock = 6, Level = 3 };
        var turn = new Turn { Sources = [new Source(SourceKind.ClockSwap, 1)] };
        var outcome = Sim.Simulate(turn, opp, 100, 1);
        Assert.Equal(100u, outcome.Histogram[0]);
        Assert.Equal(0u, outcome.Kills);
        // The kick alone kills.
        turn.Sources[0] = new Source(SourceKind.ClockKick, 1);
        Assert.Equal(100u, Sim.Simulate(turn, opp, 100, 1).Kills);
    }

    /// <summary>
    /// Reads every card of a real install: no failure, and how many effects of each kind are
    /// found. SIMULATOR_ROOT=~/Games/SimuWeiss dotnet test --filter Category=RealInstall.
    /// </summary>
    [RealInstallFact]
    [Trait("Category", "RealInstall")]
    public void ReadsARealInstall()
    {
        var root = Environment.GetEnvironmentVariable("SIMULATOR_ROOT") ?? throw new InvalidOperationException("set SIMULATOR_ROOT");
        var folder = SimulatorFolder.Find(root) ?? throw new InvalidOperationException("not a simulator folder");
        var cards = folder.LoadSeries().SelectMany(s => s.AllCards).ToList();
        var effects = cards.SelectMany(c => DamageEffects.Read(c, null, anyClimax: true)).ToList();
        var actions = effects.SelectMany(e => e.Actions).ToList();
        Console.WriteLine($"{cards.Count} cards, {effects.Select(e => e.Card).Distinct().Count()} with damage effects, {effects.Count} effects");
        foreach (var g in actions.GroupBy(a => a.Kind).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key}: {g.Count()} ({g.Count(a => a.Guess is not null)} guessed)");
        Console.WriteLine($"  effects with a choice of options: {effects.Count(e => e.HasOptions)}");
        foreach (var e in effects.Where(e => e.HasOptions).Take(5))
            Console.WriteLine($"    {e.Card.Key}: {string.Join(" / ", e.Options!.Select(o => $"{o.Label[..Math.Min(40, o.Label.Length)]} = {o.Summary}"))}");
        foreach (var g in effects.GroupBy(e => e.Timing).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key}: {g.Count()}");
        // Cards whose script burns, clocks or mills the opponent but where nothing was found.
        var missed = cards.Where(c => c.Type != CardType.Climax
                && System.Text.RegularExpressions.Regex.IsMatch(c.Code, @"^\s*(Burn |MillOpponent|MoveCards? OpponentClock)", System.Text.RegularExpressions.RegexOptions.Multiline)
                && DamageEffects.Read(c, null, anyClimax: true).Count == 0)
            .ToList();
        Console.WriteLine($"  scripts with a damage command but no effect found: {missed.Count}");
        foreach (var c in missed.Take(40)) Console.WriteLine($"    {c.Key} {c.Name}");
        Assert.NotEmpty(effects);
        var climaxes = cards.Where(c => c.Type == CardType.Climax).ToList();
        var souls = climaxes.Select(c => (Card: c, Bonuses: DamageEffects.ClimaxSouls(c))).ToList();
        Console.WriteLine($"{climaxes.Count} climaxes: {souls.Count(s => s.Bonuses.Count > 0 && s.Bonuses.All(b => b.Choose == 0))} soul for all, " +
            $"{souls.Count(s => s.Bonuses.Any(b => b.Choose > 0))} soul to choose, {souls.Count(s => s.Bonuses.Count == 0)} without soul");
        foreach (var g in souls.SelectMany(s => s.Bonuses).GroupBy(b => b.Summary).OrderByDescending(g => g.Count()).Take(15))
            Console.WriteLine($"  {g.Count()} × {g.Key}");
        foreach (var (c, _) in souls.Where(s => s.Bonuses.Count == 0 && s.Card.Code.Contains("GainSoul")).Take(10))
            Console.WriteLine($"  soul in the script but none read: {c.Key} {c.Name}");
        foreach (var (c, _) in souls.Where(s => s.Bonuses.Count == 0 && !s.Card.Code.Contains("GainSoul") && !s.Card.Code.Contains("Standby")).Take(10))
            Console.WriteLine($"  no soul: {c.Key} {c.Name}: {c.Code.Replace("\n", " ")[..Math.Min(100, c.Code.Length)]}");
    }
}
