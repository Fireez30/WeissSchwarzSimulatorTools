// Text helpers: decoding the simulator's files, reading the markup of card text, and finding
// the card names quoted in it.

using System.Text;

namespace SimuDeckParser;

/// <summary>How a run of card text is styled by the simulator's rich-text tags.</summary>
public readonly record struct TextStyle(bool Bold = false, bool Italic = false, bool Highlight = false, bool Strike = false);

/// <summary>A piece of card text, after reading the simulator's rich-text tags.</summary>
public abstract record Rich
{
    public sealed record Text(string Value, TextStyle Style) : Rich;

    /// <summary><c>&lt;sprite name="1cost"/&gt;</c>: an inline icon.</summary>
    public sealed record Sprite(string Name) : Rich;
}

/// <summary>A piece of text from <see cref="TextHelpers.SplitNames"/>: <c>Name</c> is set when the span is a quoted card name.</summary>
public readonly record struct NameSpan(string Span, string? Name);

public static class TextHelpers
{
    /// <summary>Strips trailing ASCII letters (<c>ABC/W1-001SP</c> -> <c>ABC/W1-001</c>).</summary>
    public static string RemoveTrailingAlphas(string input)
    {
        var end = input.Length;
        while (end > 0 && char.IsAsciiLetter(input[end - 1]))
            end--;
        return input[..end];
    }

    /// <summary>Case-insensitive substring search; an empty needle always matches.</summary>
    public static bool ContainsIgnoreCase(string needle, string haystack) =>
        needle.Length == 0 || haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Card code -> image file stem used by the simulator (<c>ABC/W1-001</c> -> <c>ABC_W1_001</c>).</summary>
    public static string ImageStem(string code) => code.Trim().Replace('/', '_').Replace('-', '_');

    /// <summary>
    /// The lines of a text, like Rust's <c>str::lines</c>: split on <c>\n</c>, a trailing <c>\r</c>
    /// removed, and no empty last line after a final newline.
    /// </summary>
    public static IEnumerable<string> Lines(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            var next = end < 0 ? text.Length : end + 1;
            if (end < 0) end = text.Length;
            var line = text[start..end];
            yield return line.EndsWith('\r') ? line[..^1] : line;
            start = next;
        }
    }

    /// <summary>
    /// Decodes a simulator text file: UTF-8 (BOM optional) or UTF-16 (with a BOM, or detected by
    /// its NUL bytes, as some card files are saved by Windows editors).
    /// </summary>
    public static string DecodeText(ReadOnlySpan<byte> bytes)
    {
        static string Utf16(ReadOnlySpan<byte> b, bool little)
        {
            b = b[..(b.Length & ~1)];
            return (little ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(b);
        }

        if (bytes is [0xFF, 0xFE, ..]) return Utf16(bytes[2..], true);
        if (bytes is [0xFE, 0xFF, ..]) return Utf16(bytes[2..], false);
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return Encoding.UTF8.GetString(bytes[3..]);

        var sample = bytes[..Math.Min(bytes.Length, 512)];
        int oddNuls = 0, evenNuls = 0;
        for (var i = 0; i < sample.Length; i++)
        {
            if (sample[i] != 0) continue;
            if (i % 2 == 1) oddNuls++;
            else evenNuls++;
        }
        var half = sample.Length / 4;
        if (sample.Length >= 8 && oddNuls > half) return Utf16(bytes, true);
        if (sample.Length >= 8 && evenNuls > half) return Utf16(bytes, false);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// How many &lt;b&gt;, &lt;i&gt;… tags are open at a point of the text. Tags can nest
    /// (<c>&lt;b&gt;a&lt;b&gt;b&lt;/b&gt;c&lt;/b&gt;</c>), so they are counted rather than switched on and off.
    /// </summary>
    private struct OpenTags
    {
        public int Bold, Italic, Highlight, Strike;

        public readonly TextStyle Style => new(Bold > 0, Italic > 0, Highlight > 0, Strike > 0);

        /// <summary>Applies a style tag (b, /b, color=red…). Returns false when <paramref name="tag"/> is not one.</summary>
        public bool Apply(string tag)
        {
            switch (tag)
            {
                case "b": Bold++; return true;
                case "/b": Bold = Math.Max(0, Bold - 1); return true;
                case "i": Italic++; return true;
                case "/i": Italic = Math.Max(0, Italic - 1); return true;
                case "s": Strike++; return true;
                case "/s": Strike = Math.Max(0, Strike - 1); return true;
                case "/color": Highlight = Math.Max(0, Highlight - 1); return true;
            }
            if (tag.StartsWith("color", StringComparison.Ordinal))
            {
                Highlight++;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Splits a line of card text on its Unity rich-text tags (&lt;b&gt;, &lt;i&gt;, &lt;s&gt;,
    /// &lt;color=…&gt;, &lt;sprite name="…"/&gt;). Anything else in angle brackets, such as the
    /// trait &lt;Magic&gt;, is text.
    /// </summary>
    public static List<Rich> ParseRich(string line)
    {
        var open = new OpenTags();
        var output = new List<Rich>();
        // Start of the text not yet added to the output, and where to look for the next '<'.
        var textStart = 0;
        var searchFrom = 0;
        while (true)
        {
            var start = line.IndexOf('<', searchFrom);
            if (start < 0) break;
            var close = line.IndexOf('>', start);
            if (close < 0) break;
            var end = close + 1;
            var tag = line[(start + 1)..close];
            var lower = tag.Trim().ToLowerInvariant();
            var isSprite = lower.StartsWith("sprite", StringComparison.Ordinal)
                && lower.Length > 6 && (lower[6] == ' ' || lower[6] == '=');

            // The text before the tag keeps the style that was active before it.
            var styleBefore = open.Style;
            var isStyle = !isSprite && open.Apply(lower);
            if (!isSprite && !isStyle)
            {
                // Not a tag: <Magic> stays in the text.
                searchFrom = start + 1;
                continue;
            }
            if (start > textStart)
                output.Add(new Rich.Text(line[textStart..start], styleBefore));
            if (isSprite)
                output.Add(new Rich.Sprite(SpriteName(tag)));
            textStart = end;
            searchFrom = end;
        }
        if (textStart < line.Length)
            output.Add(new Rich.Text(line[textStart..], open.Style));
        return output;
    }

    /// <summary><c>sprite name="1cost"/</c> -> <c>1cost</c>.</summary>
    private static string SpriteName(string tag)
    {
        var eq = tag.IndexOf('=');
        var value = eq < 0 ? "" : tag[(eq + 1)..];
        return value.Trim().TrimEnd('/').Trim().Trim('"').Trim();
    }

    /// <summary>Keywords of card text, spelled out in bold (ALARM -> Alarm).</summary>
    public static readonly (string Upper, string Word)[] Keywords =
    [
        ("ALARM", "Alarm"),
        ("ACCELERATE", "Accelerate"),
        ("ASSIST", "Assist"),
        ("BACKUP", "Backup"),
        ("BOND", "Bond"),
        ("BRAINSTORM", "Brainstorm"),
        ("CHANGE", "Change"),
        ("ENCORE", "Encore"),
        ("EXPERIENCE", "Experience"),
        ("GREAT PERFORMANCE", "Great Performance"),
        ("MEMORY", "Memory"),
        ("SHIFT", "Shift"),
    ];

    /// <summary>Trigger drawn by a <c>&lt;sprite name="mini_…"/&gt;</c> of card text.</summary>
    public static Trigger? SpriteTrigger(string name) => name switch
    {
        "mini_soul" => Trigger.Soul,
        "mini_choice" => Trigger.Choice,
        "mini_shot" => Trigger.Burn,
        "mini_bounce" => Trigger.Wind,
        "mini_standby" => Trigger.Standby,
        "mini_door" => Trigger.Salvage,
        "mini_gate" => Trigger.Pant,
        "mini_book" => Trigger.Book,
        "mini_bar" => Trigger.Bar,
        "mini_bag" => Trigger.Bag,
        _ => null,
    };

    /// <summary>Stock paid by a <c>&lt;sprite name="2cost"/&gt;</c>.</summary>
    public static uint? SpriteCost(string name) =>
        name.EndsWith("cost", StringComparison.Ordinal) && uint.TryParse(name[..^4], out var n) ? n : null;

    /// <summary>Word for the other sprites: card states, turn counts, keywords (reverse -> Reverse).</summary>
    public static string SpriteWord(string name)
    {
        // The counter (hand) icon of events that can be played during battle.
        if (name == "backup") return "Counter";
        if (name.EndsWith("turn", StringComparison.Ordinal) && uint.TryParse(name[..^4], out _))
            return $"{name[..^4]} turn";
        var word = name.StartsWith("mini_", StringComparison.Ordinal) ? name[5..] : name;
        return word.Length == 0 ? "" : char.ToUpperInvariant(word[0]) + word[1..];
    }

    private const string Quotes = "\"'“”‘’「」";

    /// <summary>
    /// Splits text on quoted card names: spans where quoted ones keep their quote marks and
    /// <c>Name</c> is what is between them. Any quote marks pair up ("…", '…', “…”, 「…」), since
    /// names hold quotes themselves ('"Significance of…" Aqua'); <paramref name="isName"/> picks
    /// the pairs that enclose a name, the longest one first.
    /// </summary>
    public static List<NameSpan> SplitNames(string text, Func<string, bool> isName)
    {
        var quotes = new List<int>();
        for (var i = 0; i < text.Length; i++)
            if (Quotes.Contains(text[i]))
                quotes.Add(i);
        var output = new List<NameSpan>();
        var done = 0;
        var x = 0;
        while (x < quotes.Count)
        {
            var start = quotes[x];
            var inner = start + 1;
            var k = -1;
            for (var j = quotes.Count - 1; j > x; j--)
            {
                if (quotes[j] > inner && isName(text[inner..quotes[j]]))
                {
                    k = j;
                    break;
                }
            }
            if (k < 0)
            {
                x++;
                continue;
            }
            var nameEnd = quotes[k];
            var end = nameEnd + 1;
            if (start > done)
                output.Add(new NameSpan(text[done..start], null));
            output.Add(new NameSpan(text[start..end], text[inner..nameEnd]));
            done = end;
            x = k + 1;
        }
        if (done < text.Length)
            output.Add(new NameSpan(text[done..], null));
        return output;
    }

    /// <summary>
    /// %20 and the like decoded (My%20Deck -> My Deck); invalid escapes are kept as they are.
    /// Works byte by byte so that multi-byte UTF-8 characters (%C3%A9 -> é) survive.
    /// </summary>
    public static string PercentDecode(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var output = new List<byte>(bytes.Length);
        var i = 0;
        while (i < bytes.Length)
        {
            if (bytes[i] == (byte)'%' && i + 2 < bytes.Length &&Uri.IsHexDigit((char)bytes[i + 1]) && Uri.IsHexDigit((char)bytes[i + 2]))
            {
                output.Add(Convert.ToByte(Encoding.ASCII.GetString(bytes, i + 1, 2), 16));
                i += 3;
            }
            else
            {
                output.Add(bytes[i]);
                i++;
            }
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
