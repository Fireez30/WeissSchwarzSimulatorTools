// <set folder>/CardData.txt parser.
//
// The file is a sequence of blocks such as:
//
//     Character: KS/W49-E073
//     Name Megumin
//     Color R
//     Level 3
//     Trait1 Magic
//     Auto: ...simulator script...
//     Text [A] When this card attacks, ...
//     EndCard

using System.Text;

namespace SimuDeckParser.Simulator;

public static class CardFile
{
    private const int MaxTraits = 3;

    /// <summary>The card being read: filled line by line, turned into a Card at EndCard.</summary>
    private sealed class Draft
    {
        public string Key = "";
        public CardType Type = CardType.Character;
        public string Name = "";
        public CardColor Color = CardColor.None;
        public uint Level;
        public uint Cost;
        public int Power = 1;
        public Trigger?[] Triggers = [null, null];
        public uint SoulCount = 1;
        public StringBuilder Code = new();
        public StringBuilder Text = new();
        public List<string> Traits = [];
        /// <summary>Image line: artwork file stem when it doesn't follow the card code.</summary>
        public string? Image;
        public bool ParsingCode;
        public bool ParsingText;

        public void AddTrait(string t)
        {
            if (t.Length > 0 && Traits.Count < MaxTraits)
                Traits.Add(t);
        }

        /// <summary>Clone &lt;code&gt;: starts from every field of another card (foils, reprints).</summary>
        public void CloneFrom(Card c)
        {
            Type = c.Type;
            Name = c.Name;
            Color = c.Color;
            Level = c.Level;
            Cost = c.Cost;
            Power = c.Power;
            Triggers = [c.Triggers.Count > 0 ? c.Triggers[0] : null, c.Triggers.Count > 1 ? c.Triggers[1] : null];
            SoulCount = c.SoulCount;
            Code.Clear().Append(c.Code);
            Text.Clear().Append(c.Text);
            foreach (var t in c.Traits)
                AddTrait(t);
        }

        public Card Finish(ImageDir setImages, string alternateArtwork) => new()
        {
            Image = CardImages.Resolve(setImages, Key, Image, alternateArtwork),
            SoulCount = Type == CardType.Climax ? 0 : SoulCount,
            Key = Key,
            Type = Type,
            Name = Name,
            Color = Color,
            Level = Level,
            Cost = Cost,
            Power = Power,
            Triggers = Triggers.Where(t => t.HasValue).Select(t => t!.Value).ToList(),
            Traits = Traits,
            Code = Code.ToString(),
            Text = Text.ToString(),
        };
    }

    /// <summary>*XxxClimax marker -> (triggers, color). 2SoulClimax keeps the color already set.</summary>
    private static (Trigger?[] Triggers, CardColor? Color)? ClimaxMarker(string marker)
    {
        Trigger?[] soul2 = [Trigger.Soul, Trigger.Soul];
        return marker switch
        {
            "DoorClimax" => ([Trigger.Salvage, null], CardColor.Red),
            "StandbyClimax" => ([Trigger.Standby, Trigger.Soul], CardColor.Red),
            "BookClimax" => ([Trigger.Book, null], CardColor.Blue),
            "GateClimax" => ([Trigger.Pant, Trigger.Soul], CardColor.Blue),
            "BarClimax" => ([Trigger.Bar, null], CardColor.Green),
            "BagClimax" => ([Trigger.Bag, null], CardColor.Green),
            "WindClimax" => ([Trigger.Wind, null], CardColor.Yellow),
            "ChoiceClimax" => ([Trigger.Choice, null], CardColor.Yellow),
            "ShotClimax" => ([Trigger.Burn, Trigger.Soul], CardColor.Yellow),
            "BlueStockSoul" or "2K2Climax(Blue)" or "3K1Climax(Blue)" => (soul2, CardColor.Blue),
            "YellowStockSoul" or "2K2Climax(Yellow)" or "3K1Climax(Yellow)" => (soul2, CardColor.Yellow),
            "RedStockSoul" or "2K2Climax(Red)" or "3K1Climax(Red)" => (soul2, CardColor.Red),
            "GreenStockSoul" or "2K2Climax(Green)" or "3K1Climax(Green)" => (soul2, CardColor.Green),
            "2SoulClimax" => (soul2, null),
            _ => null,
        };
    }

    /// <summary>Everything from the first '(' (or the whole string when there is none) is the parameter list.</summary>
    private static (string Prefix, List<string>? Params) SplitParams(string s)
    {
        var i = s.IndexOf('(');
        if (i < 0) return (s, null);
        var parameters = s[i..].TrimStart('(').TrimEnd(')');
        return (s[..i], parameters.Split(',').Select(p => p.Trim()).ToList());
    }

    /// <summary>Expands *Effect(args) using the common effects file; falls back to the raw reference.</summary>
    private static string ExpandEffect(string reference, CommonEffects effects) =>
        FindDefinition(reference, effects.Text) switch
        {
            ({ } text, false) => text,
            ({ } text, true) => text.Replace("$", "").Replace("|", " or ").Trim(),
            _ => reference.Trim(),
        };

    /// <summary>
    /// The script of a *Effect(args) script line, with nested references expanded too; the line
    /// itself when the common effects file does not define it.
    /// </summary>
    private static string ExpandScriptLine(string line, CommonEffects effects, int depth)
    {
        if (!line.StartsWith('*')) return line + "\n";
        // A depth limit, in case a definition refers to itself.
        if (FindDefinition(line[1..], effects.Script) is ({ } script, _) && depth < 4)
            return string.Concat(TextHelpers.Lines(script).Select(l => ExpandScriptLine(l.Trim(), effects, depth + 1)));
        return line + "\n";
    }

    /// <summary>
    /// The definition <paramref name="reference"/> (Name(args)) points to, with the arguments in
    /// place of the parameters, and whether arguments were put in.
    /// </summary>
    private static (string? Text, bool Substituted) FindDefinition(string reference, SortedDictionary<string, string> definitions)
    {
        var parsed = reference.Trim().Replace("()", "");
        if (definitions.TryGetValue(parsed, out var exact))
            return (exact.Trim(), false);

        var (refPrefix, refParams) = SplitParams(parsed);
        if (refParams is null) return (null, false);
        foreach (var (key, definition) in definitions)
        {
            var (keyPrefix, keyParams) = SplitParams(key.Trim());
            // The original gave up (and dropped the effect) on a parameter count mismatch;
            // keep looking for an overload with the right arity instead.
            if (keyParams is null || keyPrefix != refPrefix || keyParams.Count != refParams.Count)
                continue;
            var text = definition.Trim();
            // Longer names first: NAME must not replace the start of NAME1. (A stable sort.)
            var parameters = keyParams.Zip(refParams).OrderByDescending(p => p.First.Length);
            foreach (var (name, value) in parameters)
                if (name.Length > 0)
                    text = text.Replace(name, value);
            return (text, true);
        }
        return (null, false);
    }

    /// <summary>
    /// Reads the content of a card file. Artwork is looked up in <paramref name="setPath"/>, then
    /// in <paramref name="alternateArtwork"/>.
    /// </summary>
    public static SortedDictionary<string, Card> Parse(string content, string setPath, string alternateArtwork, CommonEffects effects)
    {
        var setImages = ImageDir.Read(setPath);
        var (cards, forwardClones) = ParsePass(content, setImages, alternateArtwork, effects, null);
        if (!forwardClones) return cards;
        // Some Clone pointed at a card defined further down: parse again, knowing every card.
        return ParsePass(content, setImages, alternateArtwork, effects, cards).Cards;
    }

    /// <summary>
    /// One pass over a card file. <paramref name="known"/> holds the cards of a previous pass, to
    /// resolve clones of cards defined further down the file. Also returns whether some clone
    /// source was missing.
    /// </summary>
    private static (SortedDictionary<string, Card> Cards, bool MissingClone) ParsePass(
        string content, ImageDir setImages, string alternateArtwork, CommonEffects effects,
        SortedDictionary<string, Card>? known)
    {
        var missingClone = false;
        var cards = new SortedDictionary<string, Card>(StringComparer.Ordinal);
        var d = new Draft();

        foreach (var line in TextHelpers.Lines(content))
        {
            if (line.Length == 0 || line.StartsWith("Combo", StringComparison.Ordinal))
                continue;

            if (After(line, "Character:") is { } character)
            {
                d.Key = CardKey(character);
                d.Type = CardType.Character;
            }
            else if (After(line, "Event:") is { } ev)
            {
                d.Key = CardKey(ev);
                d.Type = CardType.Event;
            }
            else if (After(line, "Climax:") is { } climax)
            {
                d.Key = CardKey(climax);
                d.Type = CardType.Climax;
            }
            else if (After(line, "Image ") is { } image)
            {
                // Artwork is resolved at EndCard, from this or from the card key.
                d.Image = image.Trim() is { Length: > 0 } i ? i : null;
            }
            else if (After(line, "Name ") is { } name)
            {
                d.Name = name.Trim();
            }
            else if (After(line, "Color ") is { } color)
            {
                d.Color = color.Trim() switch
                {
                    "Y" => CardColor.Yellow,
                    "B" => CardColor.Blue,
                    "G" => CardColor.Green,
                    "R" => CardColor.Red,
                    _ => CardColor.None,
                };
            }
            else if (After(line, "Level ") is { } level)
            {
                ParseInto(ref d.Level, level, "level", d.Key);
            }
            else if (After(line, "Clone ") is { } clone)
            {
                var source = clone.Trim();
                var found = cards.GetValueOrDefault(source) ?? known?.GetValueOrDefault(source);
                if (found is not null) d.CloneFrom(found);
                else missingClone = true;
            }
            else if (After(line, "Cost ") is { } cost)
            {
                ParseInto(ref d.Cost, cost, "cost", d.Key);
            }
            else if (After(line, "Power ") is { } power)
            {
                ParseInto(ref d.Power, power, "power", d.Key);
            }
            else if (After(line, "Soul ") is { } soul)
            {
                ParseInto(ref d.SoulCount, soul, "soul", d.Key);
            }
            else if (After(line, "Trigger ") is { } trigger)
            {
                if (trigger.Trim() == "Soul")
                    d.Triggers = [Trigger.Soul, null];
            }
            else if (After(line, "Trait") is { } trait)
            {
                // Trait1 Magic: skip the trait number.
                d.AddTrait(trait.Length > 0 ? trait[1..].Trim() : "");
            }
            else if (After(line, "*") is { } reference)
            {
                if (d.Type == CardType.Climax)
                {
                    if (ClimaxMarker(reference.Trim()) is var (triggers, markerColor))
                    {
                        d.Triggers = triggers;
                        if (markerColor is { } c)
                            d.Color = c;
                    }
                }
                else
                {
                    d.Text.Append(ExpandEffect(reference, effects)).Append('\n');
                    // The damage calculator reads the script: add the effect's, when it has one.
                    if (FindDefinition(reference, effects.Script).Text is not null)
                        d.Code.Append(ExpandScriptLine(line.Trim(), effects, 0));
                }
            }
            else if (line.StartsWith("EndCard", StringComparison.Ordinal))
            {
                var finished = d;
                d = new Draft();
                // A stray EndCard (no card header before it) closes nothing.
                if (finished.Key.Length > 0)
                {
                    var card = finished.Finish(setImages, alternateArtwork);
                    cards[card.Key] = card;
                }
            }
            else if (!line.StartsWith("Text ", StringComparison.Ordinal)
                && (d.ParsingCode || new[] { "Cont:", "Auto:", "Act:", "Effect:" }.Any(p => line.StartsWith(p, StringComparison.Ordinal))))
            {
                // Simulator script: everything until the next Text line.
                d.ParsingText = false;
                d.ParsingCode = true;
                d.Code.Append(ExpandScriptLine(line.Trim(), effects, 0));
            }
            else if (After(line, "Text ") is { } text)
            {
                d.ParsingCode = false;
                d.ParsingText = true;
                // Texts may hold literal \n line breaks.
                d.Text.Append(text.Trim().Replace("\\n", "\n")).Append('\n');
            }
            else if (d.ParsingText)
            {
                d.Text.Append(line.Trim()).Append('\n');
            }
        }
        return (cards, missingClone);
    }

    /// <summary>What follows <paramref name="prefix"/> in <paramref name="line"/>, or null when the line does not start with it.</summary>
    private static string? After(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal) ? line[prefix.Length..] : null;

    /// <summary>Event:: SMP/W60-096 (typo in the data) -> SMP/W60-096.</summary>
    private static string CardKey(string rest) => rest.TrimStart(':').Trim();

    /// <summary>Parses a number into <paramref name="field"/>; on a typo in the data, reports it and keeps the default.</summary>
    private static void ParseInto<T>(ref T field, string raw, string what, string key) where T : IParsable<T>
    {
        if (T.TryParse(raw.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var v))
            field = v;
        else
            Console.Error.WriteLine($"{key}: could not parse {what} from \"{raw}\"");
    }

    /// <summary>
    /// Parses a set's card file: &lt;file&gt;.txt when the set names one, else CardData.txt (file
    /// names are matched case-insensitively). A missing file yields an empty set.
    /// </summary>
    public static SortedDictionary<string, Card> Load(string setPath, string? file, string alternateArtwork, CommonEffects effects)
    {
        var wanted = $"{file ?? "CardData"}.txt".ToLowerInvariant();
        try
        {
            var path = Directory.EnumerateFiles(setPath)
                .FirstOrDefault(p => Path.GetFileName(p).ToLowerInvariant() == wanted);
            if (path is not null)
                return Parse(TextHelpers.DecodeText(File.ReadAllBytes(path)), setPath, alternateArtwork, effects);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return new SortedDictionary<string, Card>(StringComparer.Ordinal);
    }
}
