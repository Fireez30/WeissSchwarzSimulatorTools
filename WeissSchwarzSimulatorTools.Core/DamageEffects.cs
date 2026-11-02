// Damage effects read from the cards' simulator scripts (Card.Code), to set up the damage
// calculator's turn by itself.
//
// A script is a tree of lines and { } blocks:
//
//     Auto: OnAttack                   <- when the effect happens
//     {
//         If Climax Promise From Before   <- a condition, with its block
//         {
//             Burn 2                   <- what it does
//         }
//     }
//
// DamageEffects.Read walks the tree of a card and keeps what changes the damage dealt to the
// opponent:
// - Burn N: N damage, which a climax can cancel;
// - "put the top card of your deck into your waiting room, deal X damage, X is its level (+1)":
//   a Musashi;
// - MoveCard OpponentClock: a clock kick when the moved card is one of the opponent's characters
//   (or any card that is not in their deck), a top-deck clock when it is the top of their deck,
//   and a clock swap when the top card of their clock went to the waiting room first (net no
//   damage);
// - MillOpponent N (or the top cards of their deck put into their waiting room): a mill;
// - what changes the opponent's deck (see SourceKind): cards of their waiting room put on top
//   (assumed not climaxes) or shuffled in, climaxes shuffled back, characters put on top or at
//   the bottom, their stock shuffled in, the top cards looked at (Peek): the climaxes are sent
//   away (to the waiting room, the bottom, or under the others, as the script allows), the
//   other cards stay on top.
//
// When an effect lets the player choose between effects ("Choose 1 of the following effects"),
// each option is read, and the effect keeps them all (DamageEffect.Options): the one that does
// the most damage is used, unless the player picks another. Other questions (top or bottom?
// hand or stock?) are answered the way that helps the damage most.
//
// What the turn cannot know is assumed to go the player's way: costs are paid, questions are
// answered yes, conditions other than the climax placed are met (and listed in the effect's
// conditions), the battles are won. Amounts that depend on the game (X = the number of
// characters…) are guessed as 1 and marked as guesses.
//
// TurnBuilder then lays the effects of the stage and the climax around the attacks, in the
// order of the turn.

using System.Globalization;
using System.Text.RegularExpressions;

namespace WeissSchwarzSimulatorTools.DamageSim;

/// <summary>When an effect happens during the player's turn, in the order of the turn.</summary>
public enum EffectTiming
{
    /// <summary>Main phase: when the card is placed from hand, an act, an event.</summary>
    Main,
    /// <summary>When the climax is placed.</summary>
    ClimaxPlaced,
    /// <summary>At the beginning of the attack phase.</summary>
    AttackPhase,
    /// <summary>When the card attacks, before the trigger check.</summary>
    Attack,
    /// <summary>When another character attacks.</summary>
    OtherAttacks,
    /// <summary>When the card's damage is cancelled.</summary>
    Cancelled,
    /// <summary>When the card's damage is not cancelled.</summary>
    NotCancelled,
    /// <summary>When the card's trigger check reveals a climax.</summary>
    ClimaxTrigger,
    /// <summary>When the card's battle opponent is reversed (the battle is assumed won).</summary>
    Battle,
    /// <summary>At the end of the card's attack.</summary>
    EndOfAttack,
    /// <summary>In the encore step, after the attacks.</summary>
    Encore,
}

/// <summary>Which characters an effect belongs to.</summary>
public enum EffectOwner
{
    /// <summary>The card itself.</summary>
    Self,
    /// <summary>All the player's characters (an ability given to all of them).</summary>
    All,
    /// <summary>A character the player chooses (an ability given to 1 of them).</summary>
    Chosen,
}

/// <summary>One thing an effect does to the opponent, as a source of the damage calculator.</summary>
/// <param name="Kind">Burn, Musashi, ClockKick, TopClock, ClockSwap or Mill (never a conditional kind: the timing says that).</param>
/// <param name="Amount">Damage or cards; for a Musashi, what is added to the card's level.</param>
/// <param name="Guess">Why the amount is a guess, when it is one.</param>
/// <param name="ClimaxTo">For a look at the top cards: where the climaxes go.</param>
/// <param name="Shuffle">For a look, cards put on top: the deck is shuffled after.</param>
public sealed record DamageAction(SourceKind Kind, uint Amount, string? Guess = null, LookClimaxes ClimaxTo = LookClimaxes.WaitingRoom, bool Shuffle = false);

/// <summary>One option of an effect that lets the player choose what it does.</summary>
public sealed record EffectOption(string Label, IReadOnlyList<DamageAction> Actions, IReadOnlyList<string> Conditions)
{
    public string Summary => Actions.Count == 0 ? "no damage" : string.Join(", ", Actions.Select(a => a.Summary()));

    public override string ToString() => Label;
}

/// <summary>Soul a climax gives to the characters until the end of the turn.</summary>
/// <param name="Soul">How much each character gets.</param>
/// <param name="Choose">How many characters the player chooses: at most this many; 0 for all of them.</param>
/// <param name="Only">Which characters can get it (the script's condition, e.g. "IsColor Red"); null for any.</param>
public sealed record SoulBonus(uint Soul, int Choose, string? Only)
{
    public string Summary
    {
        get
        {
            var who = Choose switch
            {
                0 => "All your characters",
                1 => "1 character you choose",
                _ => $"Up to {Choose} characters you choose",
            };
            return $"{who}{(Only is null ? "" : $" ({Only})")}: +{Soul} soul";
        }
    }
}

/// <summary>An effect of a card that deals damage.</summary>
/// <param name="Conditions">Conditions of the script that were not checked (assumed met).</param>
/// <param name="Act">An activated ability: it is not used unless the player adds it.</param>
/// <param name="Id">Which effect of which card: the same from one reading to the next.</param>
/// <param name="Options">For an effect that lets the player choose: every option (Actions and Conditions are the chosen one's).</param>
/// <param name="Chosen">For an effect with options: the one used.</param>
public sealed record DamageEffect(
    Card Card,
    EffectTiming Timing,
    EffectOwner Owner,
    IReadOnlyList<DamageAction> Actions,
    IReadOnlyList<string> Conditions,
    bool Act = false,
    string Id = "",
    IReadOnlyList<EffectOption>? Options = null,
    int Chosen = 0)
{
    public string TimingLabel => Timing.Label();

    public bool HasOptions => Options is { Count: > 1 };

    /// <summary>The effect with option <paramref name="option"/> chosen (unchanged when there is no such option).</summary>
    public DamageEffect Choose(int option) => Options is { } o && option >= 0 && option < o.Count
        ? this with { Actions = o[option].Actions, Conditions = o[option].Conditions, Chosen = option }
        : this;

    /// <summary>What it does, e.g. "Burn 1, clock kick".</summary>
    public string Summary => string.Join(", ", Actions.Select(a => a.Summary()));
}

public static class DamageEffectLabels
{
    public static string Label(this EffectTiming timing) => timing switch
    {
        EffectTiming.Main => "Main phase",
        EffectTiming.ClimaxPlaced => "When the climax is placed",
        EffectTiming.AttackPhase => "Start of the attack phase",
        EffectTiming.Attack => "When it attacks",
        EffectTiming.OtherAttacks => "When another character attacks",
        EffectTiming.Cancelled => "When its damage is cancelled",
        EffectTiming.NotCancelled => "When its damage is not cancelled",
        EffectTiming.ClimaxTrigger => "When its trigger is a climax",
        EffectTiming.Battle => "When it wins its battle",
        EffectTiming.EndOfAttack => "At the end of its attack",
        _ => "Encore step",
    };

    public static string Summary(this DamageAction action) => action.Kind switch
    {
        SourceKind.Musashi => action.Amount == 0 ? "damage = a card's level" : $"damage = a card's level + {action.Amount}",
        SourceKind.ClockKick => action.Amount == 1 ? "clock kick" : $"clock kick {action.Amount}",
        SourceKind.TopClock => $"top {action.Amount} into clock",
        SourceKind.ClockSwap => "clock swap",
        SourceKind.Mill => $"mill {action.Amount}",
        SourceKind.WaitingRoomToDeck => $"{action.Amount} from waiting room to {(action.Shuffle ? "deck" : "top")}",
        SourceKind.ClimaxToDeck => $"{action.Amount} climax back to deck",
        SourceKind.CharacterToTop => action.Shuffle ? "character into deck" : "character to top",
        SourceKind.CharacterToBottom => "character to bottom",
        SourceKind.LookTop => $"look at top {action.Amount}, {action.ClimaxTo.Label().ToLowerInvariant()}{(action.Shuffle ? ", shuffle" : "")}",
        SourceKind.StockToDeck => "stock into deck",
        _ => $"burn {action.Amount}",
    } + (action.Guess is null ? "" : " (guess)");
}

public static class DamageEffects
{
    /// <summary>A line of a script, with the block that follows it.</summary>
    private sealed class Node(string text)
    {
        public string Text { get; } = text;
        public List<Node> Children { get; } = [];

        public Node With(List<Node> children)
        {
            var copy = new Node(Text);
            copy.Children.AddRange(children);
            return copy;
        }
    }

    /// <summary>The script as a tree: the top-level lines, each with its block.</summary>
    private static List<Node> Tree(string code)
    {
        var root = new Node("");
        var path = new Stack<Node>();
        path.Push(root);
        Node? previous = null;
        foreach (var raw in TextHelpers.Lines(code))
        {
            var line = raw.Trim();
            // "Auto: OnAttack {" opens its block on the same line.
            var opens = line.EndsWith('{');
            if (opens) line = line[..^1].Trim();
            if (line == "}")
            {
                if (path.Count > 1) previous = path.Pop();
                continue;
            }
            if (line.Length > 0)
            {
                previous = new Node(line);
                path.Peek().Children.Add(previous);
            }
            if (opens)
            {
                // A "{" alone opens the block of the line before it.
                var owner = line.Length > 0 ? previous : previous ?? path.Peek();
                if (owner is not null && owner != path.Peek()) path.Push(owner);
            }
        }
        return root.Children;
    }

    /// <summary>
    /// The damage effects of <paramref name="card"/>, with <paramref name="climax"/> in the climax
    /// area. With <paramref name="anyClimax"/>, every climax condition is met instead (and listed
    /// in the conditions): all the effects the card can have.
    /// </summary>
    public static List<DamageEffect> Read(Card card, Card? climax, bool anyClimax = false)
    {
        var effects = new List<DamageEffect>();
        try
        {
            ReadEffects(Tree(card.Code), card, climax, anyClimax, EffectOwner.Self, [], effects, 0, $"{card.Key}#");
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        {
            // A script this reader does not understand: no effects, rather than no turn.
        }
        return effects;
    }

    /// <summary>The effects among top-level nodes: the Auto:/Act:/Effect:/Cont: headers and their blocks.</summary>
    private static void ReadEffects(List<Node> nodes, Card card, Card? climax, bool anyClimax, EffectOwner owner,
        IReadOnlyList<string> conditions, List<DamageEffect> effects, int depth, string path)
    {
        if (depth > 4) return;
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var id = path + index;
            var (kind, rest) = Header(node.Text);
            if (kind is null) continue;
            EffectTiming? timing;
            var act = false;
            switch (kind)
            {
                case "Auto":
                    timing = AutoTiming(rest);
                    break;
                case "Act":
                    timing = EffectTiming.Main;
                    act = true;
                    break;
                case "Effect":
                    timing = EffectTiming.Main;
                    break;
                default:
                    // Cont: what it does all the time; only the abilities it gives matter.
                    timing = null;
                    break;
            }
            // Cont: GiveAll gives its abilities to all the characters it targets.
            var start = kind == "Cont" && rest.StartsWith("GiveAll", StringComparison.Ordinal) ? EffectOwner.All : EffectOwner.Self;
            Walk Walked(List<Node> block)
            {
                var w = new Walk(climax, anyClimax, conditions, start);
                w.Run(block);
                w.Finish();
                return w;
            }
            var walk = Walked(node.Children);
            // A choice of effects: each option read on its own. Kept when they differ.
            List<EffectOption>? options = null;
            if (ChoiceVariants(node.Children) is { } variants)
            {
                var walks = variants.Select(v => (v.Label, Walk: Walked(v.Nodes))).ToList();
                if (walks.Select(w => string.Join(";", w.Walk.Actions)).Distinct().Count() > 1)
                {
                    options = walks.Select(w => new EffectOption(w.Label, w.Walk.Actions, w.Walk.Conditions)).ToList();
                    walk = walks.MaxBy(w => Walk.Weight(w.Walk)).Walk;
                }
            }
            if (timing is { } t)
            {
                // The conditions on the last damage ("If LastDamageCanceled") make the effect a
                // cancel effect.
                var at = (t, walk.Requires) switch
                {
                    (EffectTiming.Attack or EffectTiming.EndOfAttack or EffectTiming.Battle, true) => EffectTiming.Cancelled,
                    (EffectTiming.Attack or EffectTiming.EndOfAttack or EffectTiming.Battle, false) => EffectTiming.NotCancelled,
                    _ => t,
                };
                if (walk.Actions.Count > 0 || options is not null)
                {
                    var effect = new DamageEffect(card, at, owner, walk.Actions, walk.Conditions, act, id, options);
                    effects.Add(options is null ? effect : effect with { Chosen = options.FindIndex(o => o.Actions.SequenceEqual(walk.Actions)) });
                }
            }
            // Abilities given until end of turn, or for as long as the Cont: lasts.
            for (var g = 0; g < walk.Granted.Count; g++)
            {
                var (grantOwner, block, grantConditions) = walk.Granted[g];
                var o = owner == EffectOwner.Self ? grantOwner : owner;
                ReadEffects(block, card, climax, anyClimax, o, grantConditions, effects, depth + 1, $"{id}/{g}.");
            }
        }
    }

    private static (string Command, string Arg) SplitLine(string line)
    {
        var space = line.IndexOf(' ');
        return space < 0 ? (line, "") : (line[..space], line[(space + 1)..].Trim());
    }

    private static readonly Regex OptionLine = new(@"^\s*(\d+)\s*[.:)]\s*(.+)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// For a block with a choice between effects (a question about "effects", answered by If
    /// Answer blocks): the block once per option, the option's block in place of the choice;
    /// null when there is no such choice. Only the first choice counts (the others are answered
    /// automatically); abilities given to characters are not looked into.
    /// </summary>
    private static List<(string Label, List<Node> Nodes)>? ChoiceVariants(List<Node> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var (command, arg) = SplitLine(node.Text);
            if (command == "If" && arg.StartsWith("Answer", StringComparison.Ordinal))
            {
                var question = nodes.Take(i).LastOrDefault(n => n.Text.Contains('|')
                    && SplitLine(n.Text).Command is "MultipleChoiceQuestion" or "Question" or "OpponentMultipleChoiceQuestion")?.Text;
                var j = i;
                while (j < nodes.Count && SplitLine(nodes[j].Text) is ("If", var a) && a.StartsWith("Answer", StringComparison.Ordinal)) j++;
                if (question is null || !question.Contains("effect", StringComparison.OrdinalIgnoreCase)
                    || question.Contains("first", StringComparison.OrdinalIgnoreCase))
                {
                    i = j - 1;
                    continue;
                }
                var parts = question[(question.IndexOf(' ') + 1)..].Split('|');
                var described = parts[0].Split("\\n")
                    .Select(l => OptionLine.Match(l))
                    .Where(m => m.Success)
                    .GroupBy(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
                    .ToDictionary(g => g.Key, g => g.First().Groups[2].Value.Trim());
                var variants = new List<(string, List<Node>)>();
                for (var b = i; b < j; b++)
                {
                    var answer = int.TryParse(SplitLine(nodes[b].Text).Arg.Split(' ').ElementAtOrDefault(1), CultureInfo.InvariantCulture, out var n) ? n : b - i;
                    var label = described.GetValueOrDefault(answer + 1)
                        ?? (answer + 1 < parts.Length ? parts[answer + 1].Trim() : $"Effect {answer + 1}");
                    if (label.Length > 110) label = label[..107] + "…";
                    List<Node> variant = [.. nodes.Take(i), new Node("Choice").With(nodes[b].Children), .. nodes.Skip(j)];
                    variants.Add(($"{answer + 1}. {label}", variant));
                }
                return variants;
            }
            if (node.Children.Count > 0 && command is not ("GainEffect" or "GainEffectUntil")
                && ChoiceVariants(node.Children) is { } inner)
                return inner.Select(v => (v.Label, (List<Node>)[.. nodes.Take(i), node.With(v.Nodes), .. nodes.Skip(i + 1)])).ToList();
        }
        return null;
    }

    /// <summary>
    /// The soul a climax gives when it is placed (Cont: GiveAll, or Auto: OnPlay): to all the
    /// characters, or to characters the player chooses.
    /// </summary>
    public static List<SoulBonus> ClimaxSouls(Card climax)
    {
        var bonuses = new List<SoulBonus>();
        try
        {
            foreach (var node in Tree(climax.Code))
            {
                var (kind, arg) = Header(node.Text);
                var trigger = arg.Split(' ')[0];
                if (kind == "Cont" && arg.StartsWith("GiveAll", StringComparison.Ordinal)
                    || kind == "Auto" && trigger is "OnPlay" or "OnClimaxPlay" or "OnSpecificClimaxPlay")
                    SoulWalk(node.Children, new SoulTarget(0, null), bonuses);
            }
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        {
        }
        return bonuses;
    }

    /// <summary>Who the next GainSoul goes to: Choose characters (0: all of them), maybe only some (Only).</summary>
    private sealed record SoulTarget(int Choose, string? Only);

    private static void SoulWalk(List<Node> nodes, SoulTarget target, List<SoulBonus> bonuses)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var space = node.Text.IndexOf(' ');
            var (command, arg) = space < 0 ? (node.Text, "") : (node.Text[..space], node.Text[(space + 1)..].Trim());
            var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (command)
            {
                case "If" when arg is "No" or "OpponentsTurn":
                    continue;
                case "Else" or "GainEffect" or "GainEffectUntil":
                    // Taken: the If before it. Given abilities: not soul for the climax.
                    continue;
                case "TargetLocation" when words.FirstOrDefault() == "Stage":
                    target = new SoulTarget(0, null);
                    break;
                case "NarrowTargets":
                    target = target with { Only = Restriction(arg) ?? target.Only };
                    break;
                case "ChooseCharacterFromTargets" or "ChooseCharacter":
                    target = target with { Choose = 1 };
                    break;
                case "ChooseCharactersFromTargets" or "ChooseCharacters" or "ChooseMultipleCharacters":
                {
                    var max = words.Length > 1 && int.TryParse(words[1], CultureInfo.InvariantCulture, out var m) ? m : 1;
                    // ChooseMultipleCharacters 0 2 InLocation Stage && IsColor Red: the rest says which.
                    var only = command == "ChooseMultipleCharacters" ? Restriction(string.Join(' ', words.Skip(2))) : null;
                    target = new SoulTarget(Math.Clamp(max, 1, Rules.Characters), only ?? target.Only);
                    break;
                }
                case "GainSoul" when uint.TryParse(words.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out var soul) && soul > 0:
                    bonuses.Add(new SoulBonus(soul, target.Choose, target.Only));
                    break;
            }
            if (node.Children.Count > 0) SoulWalk(node.Children, target, bonuses);
        }
    }

    /// <summary>The part of a target condition that says which characters: null when it says nothing (IsCharacter, InLocation Stage…).</summary>
    private static string? Restriction(string condition)
    {
        var parts = condition.Split("&&", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim('&', ' '))
            .Where(p => p.Length > 0 && p is not ("IsCharacter" or "Any" or "CanBeChosen") && !p.StartsWith("InLocation", StringComparison.Ordinal))
            .ToList();
        return parts.Count == 0 ? null : string.Join(" && ", parts);
    }

    /// <summary>
    /// Whether <paramref name="card"/> is one of the characters a restriction (SoulBonus.Only)
    /// allows: null when it cannot tell (no card, or a condition other than a colour or a trait).
    /// </summary>
    public static bool? Allows(string? only, Card? card)
    {
        if (only is null) return true;
        if (card is null) return null;
        bool? result = true;
        foreach (var part in only.Split("&&", StringSplitOptions.TrimEntries))
        {
            var space = part.IndexOf(' ');
            var (what, value) = space < 0 ? (part, "") : (part[..space], part[(space + 1)..].Trim());
            var values = value.Split('|', StringSplitOptions.TrimEntries);
            bool? met = what switch
            {
                "IsColor" or "ColorIs" => values.Any(v => ColorOf(v) == card.Color),
                "HasTrait" => values.Any(v => card.Traits.Contains(v, StringComparer.OrdinalIgnoreCase)),
                _ => null,
            };
            if (met == false) return false;
            if (met is null) result = null;
        }
        return result;
    }

    private static CardColor? ColorOf(string name) => name.ToLowerInvariant() switch
    {
        "red" or "r" => CardColor.Red,
        "blue" or "b" => CardColor.Blue,
        "yellow" or "y" => CardColor.Yellow,
        "green" or "g" => CardColor.Green,
        "purple" or "p" => CardColor.Purple,
        _ => null,
    };

    /// <summary>"Auto: OnAttack" -> ("Auto", "OnAttack"); not a header -> (null, "").</summary>
    private static (string? Kind, string Arg) Header(string line)
    {
        foreach (var kind in new[] { "Auto", "Act", "Effect", "Cont" })
            if (line.StartsWith(kind + ":", StringComparison.Ordinal))
                return (kind, line[(kind.Length + 1)..].Trim());
        return (null, "");
    }

    /// <summary>The timing of an Auto: effect; null when it does not happen in the player's turn.</summary>
    private static EffectTiming? AutoTiming(string triggers)
    {
        foreach (var trigger in triggers.Split('|', StringSplitOptions.TrimEntries))
        {
            EffectTiming? timing = trigger.Split(' ')[0] switch
            {
                "OnPlay" or "PlacedBy" or "OnPlayedNormally" or "OnPlacedOnStageFromDeck" or "OnPlacedOnStageFromMemory" => EffectTiming.Main,
                "OnClimaxPlay" or "OnSpecificClimaxPlay" or "StartClimaxPhase" => EffectTiming.ClimaxPlaced,
                "StartAttackPhase" => EffectTiming.AttackPhase,
                "OnAttack" or "OnFrontalAttack" or "OnSideAttack" or "OnDirectAttack" => EffectTiming.Attack,
                "OnOtherAttacks" => EffectTiming.OtherAttacks,
                "OnDamageCancel" or "OnDamageCanceled" or "OnCancel" => EffectTiming.Cancelled,
                "OnDamageNotCanceled" => EffectTiming.NotCancelled,
                "OnTriggerCheckClimax" => EffectTiming.ClimaxTrigger,
                "OnReverseOpponent" or "OnOpponentReverse" or "OnReversed" => EffectTiming.Battle,
                "OnEndOfAttack" => EffectTiming.EndOfAttack,
                "StartMyEncoreStep" or "StartEncoreStep" or "StartMyEncorePhase" or "EndOfMyTurn" => EffectTiming.Encore,
                _ => null,
            };
            if (timing is not null) return timing;
        }
        return null;
    }

    /// <summary>What the last command targeted, for the commands that act on "the targets".</summary>
    private enum Target
    {
        None,
        /// <summary>The top card(s) of the player's deck.</summary>
        OwnDeckTop,
        /// <summary>The top card(s) of the opponent's deck.</summary>
        OpponentDeckTop,
        /// <summary>Card(s) of the opponent's clock.</summary>
        OpponentClock,
        /// <summary>The top card(s) of the opponent's deck, looked at (Peek): they stay in the deck.</summary>
        OpponentDeckLook,
        /// <summary>Card(s) of the opponent's waiting room.</summary>
        OpponentWaitingRoom,
        /// <summary>The opponent's stock.</summary>
        OpponentStock,
        /// <summary>Anything else: the opponent's characters, the player's cards…</summary>
        Other,
    }

    /// <summary>X: a number, a card's level plus a number, or unknown.</summary>
    private readonly record struct Value(uint N, bool Level = false, string? Unknown = null)
    {
        public static Value Guess(string why) => new(1, false, why);
    }

    private static readonly Regex ClimaxCondition = new(@"(?:^|&&&?)\s*(Not\s+)?Climax\s+(.+?)\s*(?=&&|$)", RegexOptions.CultureInvariant);

    /// <summary>A walk through an effect's block: what it does, under which conditions.</summary>
    private sealed class Walk(Card? climax, bool anyClimax, IReadOnlyList<string> conditions, EffectOwner owner)
    {
        public List<DamageAction> Actions { get; private set; } = [];
        public List<string> Conditions { get; private set; } = [.. conditions];
        /// <summary>Abilities the effect gives: to whom, their script, the conditions to get there.</summary>
        public List<(EffectOwner Owner, List<Node> Block, List<string> Conditions)> Granted { get; private set; } = [];
        /// <summary>Whether the effect needs the last damage cancelled (true) or not cancelled (false).</summary>
        public bool? Requires { get; private set; }

        private Value _x = Value.Guess("X");
        private Target _target;
        private uint _targetCount;
        private EffectOwner _targetOwner = owner;
        /// <summary>A card of the opponent's clock was put into their waiting room: the next clock is a swap.</summary>
        private bool _healed;
        /// <summary>The targets are climaxes (IsClimax).</summary>
        private bool _climaxes;
        /// <summary>The top cards of the opponent's deck being looked at: how many, where the climaxes can go, whether anything was done with them.</summary>
        private (uint N, LookClimaxes To, bool Moved)? _look;
        /// <summary>Where the actions since the opponent's deck was last shuffled start: a shuffle applies to them.</summary>
        private int _sinceShuffle;

        private Walk Fork()
        {
            var w = new Walk(climax, anyClimax, [], _targetOwner)
            {
                Actions = [],
                Conditions = [.. Conditions],
                Granted = [],
                Requires = Requires,
                _x = _x,
                _target = _target,
                _targetCount = _targetCount,
                _targetOwner = _targetOwner,
                _healed = _healed,
                _climaxes = _climaxes,
                _look = _look,
            };
            return w;
        }

        /// <summary>The end of the effect: a look at the top cards still open is done.</summary>
        public void Finish() => EndLook();

        private void EndLook()
        {
            if (_look is { Moved: true } look && !(look.To == LookClimaxes.Under && look.N <= 1))
                Actions.Add(new DamageAction(SourceKind.LookTop, Math.Clamp(look.N, 1, 20), ClimaxTo: look.To));
            _look = null;
        }

        public void Run(List<Node> nodes)
        {
            for (var i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var (command, arg) = Split(node.Text);
                switch (command)
                {
                    case "If" when Arg(arg).StartsWith("Answer", StringComparison.Ordinal):
                    {
                        // A choice between effects: the one that does the most damage.
                        var branches = new List<Node>();
                        while (i < nodes.Count && Split(nodes[i].Text) is ("If", var a) && a.StartsWith("Answer", StringComparison.Ordinal))
                            branches.Add(nodes[i++]);
                        i--;
                        Walk? best = null;
                        foreach (var branch in branches)
                        {
                            var fork = Fork();
                            fork.Run(branch.Children);
                            if (best is null || Weight(fork, pending: true) > Weight(best, pending: true)) best = fork;
                        }
                        if (best is not null) Merge(best);
                        break;
                    }
                    case "If":
                    {
                        var met = Evaluate(arg);
                        var hasElse = i + 1 < nodes.Count && Split(nodes[i + 1].Text).Command == "Else";
                        if (met != false) Run(node.Children);
                        else if (hasElse) Run(nodes[i + 1].Children);
                        if (hasElse) i++;
                        break;
                    }
                    case "IfTarget" or "IfOther" or "IfThatCard" or "IfOwner":
                    {
                        Run(node.Children);
                        if (i + 1 < nodes.Count && Split(nodes[i + 1].Text).Command == "Else") i++;
                        break;
                    }
                    case "Else":
                        // An Else whose If was not a condition this walk knows: skipped.
                        break;
                    case "Repeat":
                    {
                        var times = Number(arg, out var guess);
                        if (guess is not null) Conditions.Add($"repeated X times, counted once");
                        for (var n = 0u; n < Math.Clamp(times, 1, 10); n++) Run(node.Children);
                        break;
                    }
                    case "GainEffect" or "GainEffectUntil":
                        Granted.Add((_targetOwner, node.Children, [.. Conditions]));
                        break;
                    default:
                        Command(command, arg);
                        // PayCost, AskToUse, ScheduleEffect, Effect:… : what is inside happens.
                        if (node.Children.Count > 0) Run(node.Children);
                        break;
                }
            }
        }

        /// <summary>How much an effect helps the damage, to choose between options: what is dealt, then what changes the deck.</summary>
        public static double Weight(Walk w, bool pending = false) => w.Actions.Sum(a => a.Kind switch
        {
            SourceKind.Mill => 0.1 * a.Amount,
            SourceKind.ClockSwap => 0.1,
            SourceKind.Musashi => 2 + a.Amount,
            SourceKind.WaitingRoomToDeck or SourceKind.CharacterToTop => 0.3 * a.Amount,
            SourceKind.CharacterToBottom or SourceKind.StockToDeck => 0.05,
            SourceKind.ClimaxToDeck => -0.5,
            SourceKind.LookTop => 0.2 * a.Amount * (a.ClimaxTo == LookClimaxes.Under ? 0.5 : 1),
            _ => a.Amount,
        }) + (pending && w._look is { Moved: true } look ? 0.2 * look.N * (look.To == LookClimaxes.Under ? 0.5 : 1) : 0);

        private void Merge(Walk other)
        {
            _look = other._look;
            Actions.AddRange(other.Actions);
            Granted.AddRange(other.Granted);
            foreach (var c in other.Conditions.Where(c => !Conditions.Contains(c))) Conditions.Add(c);
            Requires ??= other.Requires;
        }

        private static (string Command, string Arg) Split(string line)
        {
            var space = line.IndexOf(' ');
            return space < 0 ? (line, "") : (line[..space], line[(space + 1)..].Trim());
        }

        private static string Arg(string arg) => arg.Trim();

        /// <summary>Whether a condition is met: null when the turn cannot tell (assumed met, and listed).</summary>
        private bool? Evaluate(string condition)
        {
            var c = condition.Trim();
            switch (c)
            {
                case "Yes" or "MyTurn" or "Not OpponentsTurn":
                    return true;
                case "No" or "OpponentsTurn" or "Not MyTurn":
                    return false;
                case "LastDamageCanceled":
                    Requires = true;
                    return true;
                case "Not LastDamageCanceled":
                    Requires = false;
                    return true;
            }
            var known = false;
            foreach (Match m in ClimaxCondition.Matches(c))
            {
                known = true;
                if (anyClimax)
                {
                    Conditions.Add($"{m.Groups[1].Value}\"{m.Groups[2].Value}\" in the climax area");
                    continue;
                }
                var names = m.Groups[2].Value.Split('|', StringSplitOptions.TrimEntries).Select(Normalize);
                var placed = climax is not null && names.Contains(Normalize(climax.Name));
                if (placed == (m.Groups[1].Success)) return false;
            }
            // The rest of the condition (beyond the climax) is assumed met.
            var rest = ClimaxCondition.Replace(c, "").Trim().Trim('&').Trim();
            var unchecked_ = rest.Length > 0 ? rest : c;
            if ((rest.Length > 0 || !known) && !Obvious.Contains(unchecked_) && !Conditions.Contains(unchecked_)) Conditions.Add(unchecked_);
            return known && rest.Length == 0 ? true : null;
        }

        /// <summary>Conditions that hold in the turn the effect happens in: not worth listing.</summary>
        private static readonly HashSet<string> Obvious = ["HasTarget", "InBattle", "OnStage", "CanMove", "Any"];

        /// <summary>Whether a target condition asks for climaxes.</summary>
        private static bool IsClimaxes(string condition) =>
            Regex.IsMatch(condition, @"(?<![!\w])IsClimax\b") && !Regex.IsMatch(condition, @"Not\s+IsClimax");

        private static string Normalize(string name) => name.Trim().Trim('"', '“', '”').Trim().ToLowerInvariant();

        /// <summary>A number argument: a literal, X, or something of the game (guessed as 1).</summary>
        private uint Number(string arg, out string? guess)
        {
            var first = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            guess = null;
            if (uint.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return n;
            if (first == "X" && _x is { Level: false, Unknown: null }) return _x.N;
            guess = first == "X" ? _x.Unknown ?? "X" : first;
            return 1;
        }

        private void Command(string command, string arg)
        {
            var words = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var where = words.FirstOrDefault() ?? "";
            switch (command)
            {
                // What the next commands act on.
                case "Peek" or "PeekTop" when where == "OpponentDeck":
                    // Looking at the top of their deck: the cards stay there unless moved.
                    EndLook();
                    _target = Target.OpponentDeckLook;
                    _targetCount = words.Length > 1 ? Number(string.Join(' ', words.Skip(1)), out _) : 1;
                    _look = (_targetCount, LookClimaxes.Under, false);
                    break;
                case "EndPeek":
                    EndLook();
                    break;
                case "Reorder" when _look is { } reordered:
                    _look = reordered with { Moved = true };
                    break;
                case "TargetTop" or "PeekTop" or "RevealExactly" or "Peek" or "RevealUpTo":
                    _target = where switch
                    {
                        "Deck" => Target.OwnDeckTop,
                        "OpponentDeck" => Target.OpponentDeckTop,
                        "OpponentClock" => Target.OpponentClock,
                        _ => Target.Other,
                    };
                    _targetCount = Number(string.Join(' ', words.Skip(1)), out _);
                    _climaxes = false;
                    break;
                case "Reveal":
                    break;
                case "Mill" or "MillUpTo":
                    _target = Target.OwnDeckTop;
                    _targetCount = Number(arg, out _);
                    break;
                case "TargetSelf":
                    _target = Target.Other;
                    _targetOwner = EffectOwner.Self;
                    break;
                case "TargetLocation" or "SearchLocationForTargets" or "TargetBottom" or "TargetNthCardFromTop" or "OpponentSearchLocationForTargets":
                {
                    // OpponentSearchLocationForTargets names the opponent's zones from their side.
                    var zone = command == "OpponentSearchLocationForTargets" ? "Opponent" + where : where;
                    _target = zone switch
                    {
                        "OpponentClock" => Target.OpponentClock,
                        "OpponentWaitingRoom" => Target.OpponentWaitingRoom,
                        "OpponentStock" => Target.OpponentStock,
                        _ => Target.Other,
                    };
                    // All the characters of the stage, until one is chosen among them.
                    _targetOwner = command == "TargetLocation" && where == "Stage" ? EffectOwner.All : _targetOwner;
                    // SearchLocationForTargets <zone> <min> <max> <condition>.
                    _targetCount = command.Contains("Search", StringComparison.Ordinal) && words.Length > 2
                        && uint.TryParse(words[2], NumberStyles.None, CultureInfo.InvariantCulture, out var max) ? Math.Max(1, max) : 1;
                    _climaxes = IsClimaxes(arg);
                    break;
                }
                case "NarrowTargets":
                    _climaxes |= IsClimaxes(arg);
                    break;
                case "ChooseCharacterFromTargets" or "ChooseCharactersFromTargets" or "ChooseCardsFromTargets" or "ChooseCardFromTargets"
                    or "ChooseMultipleCharacters" or "ChooseCharacter" or "ChooseCharacters":
                    if (_targetOwner == EffectOwner.All) _targetOwner = EffectOwner.Chosen;
                    if (_target == Target.OpponentDeckTop) _target = Target.Other;
                    // Choosing among the cards looked at, or among the waiting room: as many as allowed.
                    if (_target is not (Target.OpponentDeckLook or Target.OpponentWaitingRoom)) _targetCount = 1;
                    else if (words.Length > 1 && uint.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var chosen) && chosen > 0 && _target == Target.OpponentWaitingRoom)
                        _targetCount = chosen;
                    break;
                case "AddSelfToTargets":
                    break;
                case var t when t.StartsWith("Target", StringComparison.Ordinal):
                    // TargetBattleOpponent, TargetFacing, TargetOther…: a character.
                    _target = Target.Other;
                    _targetCount = 1;
                    _climaxes = false;
                    break;

                // X.
                case "SetXValue" or "SetXvalue":
                    _x = where switch
                    {
                        var v when uint.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) => new Value(n),
                        "TargetCardsLevel" when _target == Target.OwnDeckTop && _targetCount == 1 => new Value(0, Level: true),
                        _ => Value.Guess(where),
                    };
                    break;
                case "SetXToNumberOfCharacters":
                    _x = Value.Guess("number of characters");
                    break;
                case "AddToXValue":
                    if (uint.TryParse(where, NumberStyles.None, CultureInfo.InvariantCulture, out var add) && _x.Unknown is null)
                        _x = _x with { N = _x.N + add };
                    break;
                case "MultiplyXValue" or "MultiplyXvalue":
                    if (uint.TryParse(where, NumberStyles.None, CultureInfo.InvariantCulture, out var times) && _x is { Unknown: null, Level: false })
                        _x = _x with { N = _x.N * times };
                    else _x = Value.Guess("X");
                    break;

                // Damage.
                case "Burn":
                    if (where == "X" && _x.Level)
                        Actions.Add(new DamageAction(SourceKind.Musashi, _x.N));
                    else if (where == "TargetCardsLevel" && _target == Target.OwnDeckTop && _targetCount == 1)
                        Actions.Add(new DamageAction(SourceKind.Musashi, 0));
                    else
                    {
                        var n = Number(arg, out var guess);
                        if (n > 0) Actions.Add(new DamageAction(SourceKind.Burn, Math.Min(n, 20), guess));
                    }
                    break;
                case "MillOpponent" or "MillOpponentBottom" or "OpponentMill":
                {
                    var n = Number(arg, out var guess);
                    if (n > 0) Actions.Add(new DamageAction(SourceKind.Mill, Math.Min(n, 20), guess));
                    break;
                }
                case "MoveCard" or "MoveCards" or "Movecards" when where == "OpponentWaitingRoom" && _target == Target.OpponentClock:
                    _healed = true;
                    break;
                case "MoveCard" or "MoveCards" or "Movecards" when where == "OpponentWaitingRoom" && _target == Target.OpponentDeckLook && _look is { } look:
                    // Some of the cards looked at go to the waiting room: the climaxes.
                    _look = look with { To = LookClimaxes.WaitingRoom, Moved = true };
                    break;
                case "MoveCard" or "MoveCards" or "Movecards" when where == "OpponentWaitingRoom" && _target == Target.OpponentDeckTop:
                    Actions.Add(new DamageAction(SourceKind.Mill, Math.Clamp(_targetCount, 1, 20)));
                    break;
                case "MoveCard" or "MoveCards" or "Movecards" when where == "OpponentClock":
                    if (_healed)
                    {
                        Actions.Add(new DamageAction(SourceKind.ClockSwap, 1));
                        _healed = false;
                    }
                    else if (_target is Target.OpponentDeckTop or Target.OpponentDeckLook)
                        Actions.Add(new DamageAction(SourceKind.TopClock, Math.Clamp(_targetCount, 1, 20)));
                    else if (_target != Target.OpponentClock)
                        Actions.Add(new DamageAction(SourceKind.ClockKick, 1));
                    break;
                case "MoveCard" or "MoveCards" or "Movecards" when where == "OpponentDeck":
                    switch (_target)
                    {
                        case Target.OpponentDeckLook when _look is { } back:
                            // Put back on top, in any order.
                            _look = back with { Moved = true };
                            break;
                        case Target.OpponentWaitingRoom:
                            Actions.Add(new DamageAction(_climaxes ? SourceKind.ClimaxToDeck : SourceKind.WaitingRoomToDeck, Math.Clamp(_targetCount, 1, 20)));
                            break;
                        case Target.OpponentStock:
                            Actions.Add(new DamageAction(SourceKind.StockToDeck, 0));
                            break;
                        case Target.Other:
                            Actions.Add(new DamageAction(SourceKind.CharacterToTop, 1));
                            break;
                    }
                    break;
                case "MoveCardToBottom" or "MoveCardtoBottom" when where == "OpponentDeck":
                case "MoveCard" or "MoveCards" when where == "BottomOfOpponentDeck":
                    if (_target == Target.OpponentDeckLook && _look is { } under)
                        _look = under with { To = under.To == LookClimaxes.WaitingRoom ? LookClimaxes.WaitingRoom : LookClimaxes.Bottom, Moved = true };
                    else if (_target is Target.Other or Target.OpponentWaitingRoom)
                        Actions.Add(new DamageAction(SourceKind.CharacterToBottom, 1));
                    break;
                case "ShuffleLocation" when where == "OpponentDeck":
                    // What was put on top since the last shuffle is shuffled in.
                    EndLook();
                    for (var i = _sinceShuffle; i < Actions.Count; i++)
                        if (Actions[i].Kind.CanShuffle())
                            Actions[i] = Actions[i] with { Shuffle = true };
                    _sinceShuffle = Actions.Count;
                    break;
            }
        }
    }
}

/// <summary>An effect of the turn that lets the player choose what it does.</summary>
/// <param name="Key">What the option picked is stored under (TurnBuilder.Build's choices).</param>
/// <param name="Character">The attacking character it is chosen for (0 to 2), -1 when it happens once in the turn.</param>
public sealed record TurnChoice(string Key, int Character, DamageEffect Effect);

/// <summary>The sources of a turn, from the cards on the stage and the climax.</summary>
public static class TurnBuilder
{
    /// <summary>
    /// The turn's sources: the attacks of <paramref name="attacks"/> (by default each character
    /// attacks once, in order), with the damage effects of <paramref name="stage"/> (the cards of
    /// characters 1 to 3, null for a slot without one) and of <paramref name="climax"/> around
    /// them. An ability given to a chosen character goes to the card that gives it, or to
    /// character 1 when the climax gives it. <paramref name="choices"/> holds the option picked
    /// for the effects that let the player choose (by DamageEffect.Id); the others use the option
    /// that does the most damage.
    /// </summary>
    public static List<Source> Build(IReadOnlyList<Card?> stage, Card? climax, IReadOnlyList<Source>? attacks = null,
        IReadOnlyDictionary<string, int>? choices = null) =>
        Layout(stage, climax, attacks)
            .SelectMany(step => step.Effect is { } effect
                ? SourcesOf(choices is not null && choices.TryGetValue(step.Key, out var option) ? effect.Choose(option) : effect)
                : [step.Attack!])
            .ToList();

    /// <summary>
    /// The effects of the turn that let the player choose what they do. An effect that happens on
    /// each attack is chosen for each attacking character; the others once.
    /// </summary>
    public static List<TurnChoice> Choices(IReadOnlyList<Card?> stage, Card? climax, IReadOnlyList<Source>? attacks = null) =>
        Layout(stage, climax, attacks)
            .Where(step => step.Effect is { HasOptions: true })
            .DistinctBy(step => step.Key)
            .Select(step => new TurnChoice(step.Key, step.Character, step.Effect!))
            .ToList();

    /// <summary>
    /// One step of the turn: an attack (Attack), or an effect (Effect) with the key of its choice
    /// and the character it is chosen for (-1: once for the turn).
    /// </summary>
    private sealed record Step(Source? Attack, DamageEffect? Effect, string Key, int Character);

    /// <summary>The turn in order: the attacks, with the effects of the stage and the climax around them.</summary>
    private static List<Step> Layout(IReadOnlyList<Card?> stage, Card? climax, IReadOnlyList<Source>? attacks)
    {
        attacks ??= Enumerable.Range(0, Rules.Characters).Select(Source.AttackBy).ToList();
        var effects = Effects(stage, climax);
        var steps = new List<Step>();
        // Once for the turn, whoever has them: chosen once (for that character's card).
        void Once(EffectTiming timing)
        {
            foreach (var (character, effect) in effects)
                if (effect.Timing == timing)
                    steps.Add(new Step(null, effect, $"{character}|{effect.Id}", -1));
        }
        // On an attack: chosen for the attacking character.
        void OnAttack(EffectTiming timing, int attacker, Func<int, bool> who)
        {
            foreach (var (character, effect) in effects)
                if (effect.Timing == timing && who(character))
                    steps.Add(new Step(null, effect, $"{attacker}|{character}|{effect.Id}", attacker));
        }

        Once(EffectTiming.Main);
        Once(EffectTiming.ClimaxPlaced);
        Once(EffectTiming.AttackPhase);
        foreach (var attack in attacks)
        {
            if (attack.Kind != SourceKind.Attack)
            {
                steps.Add(new Step(attack, null, "", -1));
                continue;
            }
            var c = attack.Character;
            bool Mine(int character) => character == c || character == -1;
            OnAttack(EffectTiming.Attack, c, Mine);
            OnAttack(EffectTiming.OtherAttacks, c, character => character != c && character != -1);
            steps.Add(new Step(attack, null, "", -1));
            foreach (var timing in new[] { EffectTiming.Cancelled, EffectTiming.NotCancelled, EffectTiming.ClimaxTrigger, EffectTiming.Battle, EffectTiming.EndOfAttack })
                OnAttack(timing, c, Mine);
        }
        Once(EffectTiming.Encore);
        return steps;
    }

    /// <summary>The effects of the turn (not the acts), each with its character: -1 for every character.</summary>
    private static List<(int Character, DamageEffect Effect)> Effects(IReadOnlyList<Card?> stage, Card? climax)
    {
        var effects = new List<(int Character, DamageEffect Effect)>();
        for (var i = 0; i < stage.Count && i < Rules.Characters; i++)
            if (stage[i] is { } card)
                foreach (var e in DamageEffects.Read(card, climax).Where(e => !e.Act))
                    effects.Add((e.Owner == EffectOwner.All ? -1 : i, e));
        if (climax is not null)
            foreach (var e in DamageEffects.Read(climax, climax).Where(e => !e.Act))
                effects.Add((e.Owner == EffectOwner.All ? -1 : 0, e));
        return effects;
    }

    /// <summary>The sources of one effect: its actions, with the condition of its timing.</summary>
    public static IEnumerable<Source> SourcesOf(DamageEffect effect)
    {
        var notes = effect.Conditions.Select(c => "if " + c)
            .Concat(effect.Actions.Where(a => a.Guess is not null).Select(a => $"amount guessed ({a.Guess})"))
            .ToList();
        if (effect.Owner == EffectOwner.Chosen) notes.Add("given to a character you choose");
        var origin = $"{effect.Card.Name} · {effect.TimingLabel}" + (effect.HasOptions ? $" · option {effect.Chosen + 1}" : "");
        var note = notes.Count == 0 ? null : string.Join("; ", notes);
        foreach (var action in effect.Actions)
        {
            var kind = (action.Kind, effect.Timing) switch
            {
                (SourceKind.Burn, EffectTiming.Cancelled) => SourceKind.CancelBurn,
                (SourceKind.Burn, EffectTiming.NotCancelled) => SourceKind.NotCancelledBurn,
                (SourceKind.Burn, EffectTiming.ClimaxTrigger) => SourceKind.ClimaxTriggerBurn,
                (SourceKind.Musashi, EffectTiming.Cancelled) => SourceKind.CancelMusashi,
                var (k, _) => k,
            };
            yield return new Source(kind, action.Amount, Origin: origin, Note: note, ClimaxTo: action.ClimaxTo, Shuffle: action.Shuffle);
        }
    }
}
