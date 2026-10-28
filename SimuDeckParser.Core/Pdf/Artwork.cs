// Card pictures for the PDFs: read, cropped or fitted, scaled down and re-encoded as JPEG, which
// PDFs embed as is.

using SkiaSharp;

namespace SimuDeckParser.Pdf;

/// <summary>How artwork goes in its box.</summary>
internal enum Fit
{
    /// <summary>Fills the box, cropping the edges; landscape pictures (climaxes) are turned upright.</summary>
    Fill,
    /// <summary>Whole picture, as large as fits.</summary>
    Contain,
}

internal sealed record Jpeg(byte[] Data, int Width, int Height)
{
    /// <summary>
    /// Artwork of each card, fitted to <paramref name="wMm"/> × <paramref name="hMm"/> and scaled
    /// down to <paramref name="dpi"/>, by card key. Cards without a readable picture are left out.
    /// </summary>
    public static Dictionary<string, Jpeg> Prepare(IEnumerable<Card> cards, double wMm, double hMm, double dpi, Fit fit) =>
        cards
            .Where(c => c.Image is not null)
            .AsParallel()
            .Select(c => (c.Key, Jpeg: Card(c.Image!, wMm, hMm, dpi, fit)))
            .Where(r => r.Jpeg is not null)
            .ToDictionary(r => r.Key, r => r.Jpeg!, StringComparer.Ordinal);

    /// <summary>The picture at <paramref name="path"/>, fitted; null when it cannot be read.</summary>
    public static Jpeg? Card(string path, double wMm, double hMm, double dpi, Fit fit)
    {
        using var decoded = SKBitmap.Decode(path);
        if (decoded is null) return null;
        var image = Flatten(decoded);
        int Px(double mm) => (int)Math.Round(mm / 25.4 * dpi);
        var (maxW, maxH) = (Px(wMm), Px(hMm));
        try
        {
            if (fit == Fit.Fill)
            {
                if (image.Width > image.Height)
                    Replace(ref image, Rotate90(image));
                // Crop to the printed shape, keeping the middle.
                var target = wMm / hMm;
                var (w, h) = (image.Width, image.Height);
                var (cw, ch) = (double)w / h > target
                    ? ((int)Math.Round(h * target), h)
                    : (w, (int)Math.Round(w / target));
                Replace(ref image, Crop(image, (w - cw) / 2, (h - ch) / 2, cw, ch));
                if (image.Width > maxW)
                    Replace(ref image, Resize(image, maxW, maxH));
            }
            else
            {
                var scale = Math.Min((double)maxW / image.Width, (double)maxH / image.Height);
                if (scale < 1.0)
                {
                    var w = (int)Math.Max(1, Math.Round(image.Width * scale));
                    var h = (int)Math.Max(1, Math.Round(image.Height * scale));
                    Replace(ref image, Resize(image, w, h));
                }
            }
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
            return new Jpeg(data.ToArray(), image.Width, image.Height);
        }
        finally
        {
            image.Dispose();
        }
    }

    private static void Replace(ref SKBitmap image, SKBitmap next)
    {
        image.Dispose();
        image = next;
    }

    /// <summary>RGB on a white background.</summary>
    private static SKBitmap Flatten(SKBitmap source)
    {
        var output = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, 0, 0);
        return output;
    }

    /// <summary>Turned a quarter clockwise.</summary>
    private static SKBitmap Rotate90(SKBitmap source)
    {
        var output = new SKBitmap(new SKImageInfo(source.Height, source.Width, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(output);
        canvas.Translate(source.Height, 0);
        canvas.RotateDegrees(90);
        canvas.DrawBitmap(source, 0, 0);
        return output;
    }

    private static SKBitmap Crop(SKBitmap source, int x, int y, int w, int h)
    {
        using var subset = new SKBitmap();
        source.ExtractSubset(subset, new SKRectI(x, y, x + w, y + h));
        return subset.Copy();
    }

    private static SKBitmap Resize(SKBitmap source, int w, int h) =>
        source.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque), SKFilterQuality.High);
}
