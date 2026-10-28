// Printable PDFs of a deck: proxies to cut out and slip in sleeves, and a translation sheet
// (each card with its English text).
//
// This file lays out the two printouts; Document.cs (pages, lines, wrapped text) and
// Artwork.cs (card pictures) do the drawing.

using PdfSharp.Drawing;

namespace SimuDeckParser.Pdf;

public enum Paper
{
    A4,
    Letter,
}

public static class PaperExtensions
{
    public static readonly Paper[] All = [Paper.A4, Paper.Letter];

    public static string Label(this Paper paper) => paper == Paper.A4 ? "A4" : "US Letter";

    /// <summary>Width and height in points.</summary>
    internal static (double Width, double Height) Size(this Paper paper) =>
        paper == Paper.A4 ? (Printouts.Mm(210), Printouts.Mm(297)) : (612, 792);
}

public static class Printouts
{
    /// <summary>Artwork resolution in the PDFs.</summary>
    private const double ProxyDpi = 300;
    private const double SheetDpi = 200;
    private static readonly XColor Black = XColor.FromArgb(26, 26, 26);
    private static readonly XColor Grey = XColor.FromArgb(97, 97, 102);
    internal static readonly XColor Red = XColor.FromArgb(199, 26, 26);
    private static readonly XColor LightGrey = XColor.FromArgb(204, 204, 204);

    /// <summary>Millimetres to points, the PDF unit (1/72 inch).</summary>
    internal static double Mm(double v) => v * 72 / 25.4;

    /// <summary>Weiss Schwarz card size, in millimetres.</summary>
    private const double CardWMm = 59, CardHMm = 86;

    /// <summary>Proxies per row and per column on a page, leaving room for the crop marks.</summary>
    public static (int Cols, int Rows) ProxyGrid(Paper paper)
    {
        var (width, height) = paper.Size();
        var minMargin = Mm(5);
        int Fit(double page, double card) => Math.Max(1, (int)Math.Floor((page - 2 * minMargin) / Mm(card)));
        return (Fit(width, CardWMm), Fit(height, CardHMm));
    }

    /// <summary>
    /// Every card of the deck at Weiss Schwarz size, as many per page as fit, with crop marks
    /// between them. Cards without artwork are printed as text.
    /// </summary>
    public static byte[] Proxies(Deck deck, Paper paper)
    {
        var cards = deck.Cards.ToList();
        CardSorting.SortForDeck(cards);
        var art = Jpeg.Prepare(Unique(cards), CardWMm, CardHMm, ProxyDpi, Fit.Fill);

        using var doc = new Doc(paper);
        var (cw, ch) = (Mm(CardWMm), Mm(CardHMm));
        var (cols, rows) = ProxyGrid(paper);
        var x0 = (doc.Width - cols * cw) / 2;
        var y0 = (doc.Height - rows * ch) / 2;

        foreach (var page in cards.Chunk(cols * rows))
        {
            doc.NewPage();
            for (var i = 0; i < page.Length; i++)
            {
                var card = page[i];
                var x = x0 + i % cols * cw;
                var y = y0 + i / cols * ch;
                if (art.TryGetValue(card.Key, out var image)) doc.Image(card.Key, image, x, y, cw, ch);
                else TextCard(doc, card, x, y, cw, ch);
            }
            var usedRows = (page.Length + cols - 1) / cols;
            var usedCols = Math.Min(page.Length, cols);
            CropMarks(doc, x0, y0, cw, ch, usedCols, usedRows);
        }
        return doc.Finish($"{deck.Name} (proxies)");
    }

    /// <summary>Marks outside the grid on every cut line: a paper cutter lines up on them.</summary>
    private static void CropMarks(Doc doc, double x0, double y0, double cw, double ch, int cols, int rows)
    {
        var gap = Mm(1);
        var (x1, y1) = (x0 + cols * cw, y0 + rows * ch);
        var lenX = Math.Min(x0 - gap - Mm(1), Mm(6));
        var lenY = Math.Min(y0 - gap - Mm(1), Mm(6));
        for (var i = 0; i <= cols; i++)
        {
            var x = x0 + i * cw;
            doc.Line(x, y0 - gap, x, y0 - gap - lenY, 0.3, Black);
            doc.Line(x, y1 + gap, x, y1 + gap + lenY, 0.3, Black);
        }
        for (var j = 0; j <= rows; j++)
        {
            var y = y0 + j * ch;
            doc.Line(x0 - gap, y, x0 - gap - lenX, y, 0.3, Black);
            doc.Line(x1 + gap, y, x1 + gap + lenX, y, 0.3, Black);
        }
    }

    /// <summary>A proxy without artwork: name, stats and effect text in a frame.</summary>
    private static void TextCard(Doc doc, Card card, double x, double y, double w, double h)
    {
        doc.Rect(x, y, w, h, 0.5, Grey);
        var pad = Mm(3);
        var width = w - 2 * pad;
        var top = y + pad;
        foreach (var block in CardBlocks(card, 9, 6.5))
            top = doc.Paragraph(block, x + pad, top, width, y + h - pad);
    }

    /// <summary>Each different card once, with its copies, picture, stats and effect text.</summary>
    public static byte[] Translation(Deck deck, Paper paper)
    {
        var cards = deck.Cards.ToList();
        CardSorting.SortForDeck(cards);
        var counts = new List<(Card Card, int Copies)>();
        foreach (var card in cards)
        {
            var i = counts.FindIndex(c => c.Card.Key == card.Key);
            if (i >= 0) counts[i] = (counts[i].Card, counts[i].Copies + 1);
            else counts.Add((card, 1));
        }
        const double imgWMm = 26;
        var art = Jpeg.Prepare(Unique(cards), imgWMm, imgWMm * CardImage.Ratio, SheetDpi, Fit.Contain);

        using var doc = new Doc(paper);
        var margin = Mm(14);
        var bottom = doc.Height - margin - Mm(4);
        var (imgW, imgH) = (Mm(imgWMm), Mm(imgWMm * CardImage.Ratio));
        var textX = margin + imgW + Mm(5);
        var textW = doc.Width - margin - textX;

        doc.NewPage();
        var y = margin;
        y = doc.Paragraph(new Block([new Run(deck.Name, true, Black)], 16), margin, y, doc.Width - 2 * margin, bottom);
        var subtitle = $"{cards.Count} cards, {counts.Count} different. Translation sheet made with SimuDeckParser.";
        y = doc.Paragraph(new Block([new Run(subtitle, false, Grey)], 9), margin, y + Mm(1), textW, bottom) + Mm(4);

        CardType? kind = null;
        foreach (var (card, copies) in counts)
        {
            var blocks = new List<Block>
            {
                new([new Run($"{copies}× ", true, Red), new Run(card.Name, true, Black)], 10.5),
            };
            blocks.AddRange(CardBlocks(card, 0, 8.5));
            var textH = blocks.Sum(b => doc.ParagraphHeight(b, textW));
            var rowH = Math.Max(imgH, textH);
            string? header = kind != card.Type
                ? $"{card.Type.PluralLabel()} ({cards.Count(c => c.Type == card.Type)})"
                : null;
            var headerH = header is not null ? Mm(10) : 0;
            if (y + headerH + rowH > bottom && y > margin + Mm(20))
            {
                doc.NewPage();
                y = margin;
            }
            if (header is not null)
            {
                kind = card.Type;
                y = doc.Paragraph(new Block([new Run(header, true, Black)], 12), margin, y + Mm(2), textW, bottom);
                doc.Line(margin, y + Mm(1), doc.Width - margin, y + Mm(1), 0.6, Grey);
                y += Mm(4);
            }
            if (art.TryGetValue(card.Key, out var image))
            {
                // Whole picture, centred at the top of its box (climaxes are landscape).
                var scale = Math.Min(imgW / image.Width, imgH / image.Height);
                var (w, h) = (image.Width * scale, image.Height * scale);
                doc.Image(card.Key, image, margin + (imgW - w) / 2, y, w, h);
            }
            else
            {
                doc.Rect(margin, y, imgW, imgH, 0.5, Grey);
            }
            var top = y;
            foreach (var b in blocks)
                top = doc.Paragraph(b, textX, top, textW, double.MaxValue);
            y += rowH + Mm(3);
            doc.Line(textX, y - Mm(1.5), doc.Width - margin, y - Mm(1.5), 0.25, LightGrey);
        }
        return doc.Finish($"{deck.Name} (translation)");
    }

    /// <summary>Text of a card: name (skipped when <paramref name="nameSize"/> is 0), stats line, traits, effects.</summary>
    private static List<Block> CardBlocks(Card card, double nameSize, double size)
    {
        var blocks = new List<Block>();
        if (nameSize > 0)
            blocks.Add(new Block([new Run(card.Name, true, Black)], nameSize));
        var stats = new List<string> { card.Key, card.Color.Label(), card.Type.Label() };
        if (card.Type != CardType.Climax)
        {
            stats.Add($"Level {card.Level}");
            stats.Add($"Cost {card.Cost}");
        }
        if (card.Type == CardType.Character) stats.Add($"Power {card.Power}");
        if (card.SoulCount > 0) stats.Add($"Soul {card.SoulCount}");
        if (card.Triggers.Count > 0) stats.Add($"Trigger {string.Join(" + ", card.Triggers.Select(t => t.Label()))}");
        var small = size - 1;
        blocks.Add(new Block([new Run(string.Join(" · ", stats), false, Grey)], small));
        if (card.Traits.Count > 0)
            blocks.Add(new Block([new Run($"Traits: {string.Join(", ", card.Traits)}", false, Grey)], small));
        foreach (var line in TextHelpers.Lines(card.Text).Where(l => l.Trim().Length > 0))
            blocks.Add(new Block(EffectRuns(line), size) { SpaceBefore = size * 0.35 });
        return blocks;
    }

    private static readonly (string Pattern, string Word)[] Icons =
    [
        ("Auto:", "AUTO"),
        ("Cont:", "CONT"),
        ("Act:", "ACT"),
        ("CxCombo", "CX COMBO"),
    ];

    /// <summary>A line of effect text: rich-text tags, sprites and keywords as plain or bold text.</summary>
    internal static List<Run> EffectRuns(string line)
    {
        var runs = new List<Run>();
        foreach (var rich in TextHelpers.ParseRich(line))
        {
            switch (rich)
            {
                case Rich.Sprite { Name: var name }:
                    string word;
                    if (TextHelpers.SpriteCost(name) is { } cost) word = $"({cost})";
                    else if (TextHelpers.SpriteTrigger(name) is { } t) word = $"[{t.Label()}]";
                    // Always followed by the ALARM keyword itself.
                    else if (name == "alarm") continue;
                    else word = $"[{TextHelpers.SpriteWord(name)}]";
                    runs.Add(new Run(word, true, Black));
                    break;
                case Rich.Text { Value: var text, Style: var style }:
                    var color = style.Highlight ? Red : Black;
                    while (text.Length > 0)
                    {
                        var next = Icons.Concat(TextHelpers.Keywords)
                            .Select(k => (Index: text.IndexOf(k.Item1, StringComparison.Ordinal), Pattern: k.Item1, Word: k.Item2))
                            .Where(k => k.Index >= 0)
                            .OrderBy(k => k.Index).ThenByDescending(k => k.Pattern.Length)
                            .FirstOrDefault();
                        if (next.Pattern is null)
                        {
                            runs.Add(new Run(text, style.Bold, color));
                            break;
                        }
                        if (next.Index > 0) runs.Add(new Run(text[..next.Index], style.Bold, color));
                        runs.Add(new Run(next.Word, true, color));
                        text = text[(next.Index + next.Pattern.Length)..];
                    }
                    break;
            }
        }
        return runs;
    }

    /// <summary>Each different card once, in order.</summary>
    private static List<Card> Unique(IEnumerable<Card> cards) => cards.DistinctBy(c => c.Key).ToList();
}
