// A PDF being drawn: pages, lines, pictures, and paragraphs of wrapped text.
//
// Positions are in points (1/72 inch) from the top-left corner of the page, as PDFsharp's
// XGraphics counts them. PDFsharp embeds the fonts, subset to the characters used.

using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace WeissSchwarzSimulatorTools.Pdf;

/// <summary>A piece of text in one style.</summary>
internal sealed record Run(string Text, bool Bold, XColor Color);

/// <summary>A paragraph: runs of text wrapped together at one size.</summary>
internal sealed record Block(List<Run> Runs, double Size)
{
    public double SpaceBefore { get; init; }
}

/// <summary>A piece of a wrapped line, at its x offset.</summary>
internal sealed record Piece(double X, string Text, bool Bold, XColor Color);

/// <summary>Gives PDFsharp the embedded Noto Sans fonts: regular, and medium for bold.</summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    public const string Family = "Noto Sans";
    private static readonly object Lock = new();

    /// <summary>Installs the resolver once for the whole program (PDFsharp keeps it globally).</summary>
    public static void Install()
    {
        lock (Lock)
        {
            if (GlobalFontSettings.FontResolver is not EmbeddedFontResolver)
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "NotoSans-Medium" : "NotoSans-Regular");

    public byte[]? GetFont(string faceName) =>
        faceName == "NotoSans-Medium" ? Fonts.NotoSansMedium : Fonts.NotoSansRegular;
}

internal sealed class Doc : IDisposable
{
    private readonly PdfDocument _pdf = new();
    private readonly Dictionary<(bool, double), XFont> _fonts = [];
    /// <summary>Pictures by card key: each is embedded once, however many times it is drawn.</summary>
    private readonly Dictionary<string, XImage> _images = new(StringComparer.Ordinal);
    private readonly List<MemoryStream> _imageStreams = [];
    private XGraphics _gfx;

    public double Width { get; }
    public double Height { get; }

    public Doc(Paper paper)
    {
        EmbeddedFontResolver.Install();
        (Width, Height) = paper.Size();
        // Text is measured before the first page exists.
        _gfx = XGraphics.CreateMeasureContext(new XSize(Width, Height), XGraphicsUnit.Point, XPageDirection.Downwards);
    }

    public void NewPage()
    {
        _gfx.Dispose();
        var page = _pdf.AddPage();
        page.Width = XUnit.FromPoint(Width);
        page.Height = XUnit.FromPoint(Height);
        _gfx = XGraphics.FromPdfPage(page);
    }

    public int PageCount => _pdf.PageCount;

    public XFont Font(bool bold, double size)
    {
        if (!_fonts.TryGetValue((bold, size), out var font))
        {
            font = new XFont(EmbeddedFontResolver.Family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
            _fonts[(bold, size)] = font;
        }
        return font;
    }

    public double TextWidth(string text, bool bold, double size) => _gfx.MeasureString(text, Font(bold, size)).Width;

    /// <summary>Draws the picture of the card <paramref name="key"/>, embedded once however many times it is drawn.</summary>
    public void Image(string key, Jpeg jpeg, double x, double y, double w, double h)
    {
        if (!_images.TryGetValue(key, out var image))
        {
            var stream = new MemoryStream(jpeg.Data);
            _imageStreams.Add(stream);
            image = XImage.FromStream(stream);
            _images[key] = image;
        }
        _gfx.DrawImage(image, x, y, w, h);
    }

    public void Line(double x1, double y1, double x2, double y2, double width, XColor color) =>
        _gfx.DrawLine(new XPen(color, width), x1, y1, x2, y2);

    public void Rect(double x, double y, double w, double h, double width, XColor color) =>
        _gfx.DrawRectangle(new XPen(color, width), x, y, w, h);

    /// <summary>Words and runs of whitespace.</summary>
    private static IEnumerable<(string Word, bool IsSpace)> Words(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var space = char.IsWhiteSpace(text[start]);
            var end = start;
            while (end < text.Length && char.IsWhiteSpace(text[end]) == space) end++;
            yield return (text[start..end], space);
            start = end;
        }
    }

    /// <summary>Greedy word wrap at <paramref name="width"/>.</summary>
    public List<List<Piece>> Wrap(Block block, double width)
    {
        var size = block.Size;
        var lines = new List<List<Piece>>();
        var line = new List<Piece>();
        var x = 0.0;
        Run? space = null;

        void Push(double at, string text, Run style)
        {
            if (line.Count > 0 && line[^1] is var last && last.Bold == style.Bold && last.Color == style.Color)
                line[^1] = last with { Text = last.Text + text };
            else
                line.Add(new Piece(at, text, style.Bold, style.Color));
        }

        foreach (var run in block.Runs)
        {
            foreach (var (word, isSpace) in Words(run.Text))
            {
                if (isSpace)
                {
                    if (line.Count > 0) space = run;
                    continue;
                }
                var w = TextWidth(word, run.Bold, size);
                var sw = space is null ? 0 : TextWidth(" ", space.Bold, size);
                if (line.Count > 0 && x + sw + w > width)
                {
                    lines.Add(line);
                    line = [];
                    x = 0;
                    space = null;
                    sw = 0;
                }
                if (space is not null)
                {
                    Push(x, " ", space);
                    x += sw;
                    space = null;
                }
                Push(x, word, run);
                x += w;
            }
        }
        if (line.Count > 0) lines.Add(line);
        return lines;
    }

    private static double Leading(double size) => size * 1.3;

    public double ParagraphHeight(Block block, double width) =>
        block.SpaceBefore + Wrap(block, width).Count * Leading(block.Size);

    /// <summary>Draws the block from <paramref name="top"/> down; lines past <paramref name="bottom"/> are dropped. Returns the next top.</summary>
    public double Paragraph(Block block, double x, double top, double width, double bottom)
    {
        var size = block.Size;
        var leading = Leading(size);
        top += block.SpaceBefore;
        foreach (var line in Wrap(block, width))
        {
            if (top + leading > bottom) break;
            // Baseline: ascent is about 1.07 em for Noto Sans, centred in the leading.
            var baseline = top + (leading - size) / 2 + size * 0.95;
            foreach (var piece in line)
            {
                var text = new string(piece.Text.Where(c => !char.IsControl(c)).ToArray());
                _gfx.DrawString(text, Font(piece.Bold, size), new XSolidBrush(piece.Color), x + piece.X, baseline, XStringFormats.BaseLineLeft);
            }
            top += leading;
        }
        return top;
    }

    public byte[] Finish(string title)
    {
        _gfx.Dispose();
        _pdf.Info.Title = title;
        _pdf.Info.Creator = "WeissSchwarz Simulator Tools";
        using var output = new MemoryStream();
        _pdf.Save(output, false);
        return output.ToArray();
    }

    public void Dispose()
    {
        _gfx.Dispose();
        _pdf.Dispose();
        foreach (var image in _images.Values) image.Dispose();
        foreach (var stream in _imageStreams) stream.Dispose();
    }
}
