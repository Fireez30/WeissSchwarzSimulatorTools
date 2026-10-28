using PdfSharp.Pdf.IO;
using SimuDeckParser.Pdf;
using SimuDeckParser.Simulator;
using SkiaSharp;

namespace SimuDeckParser.Tests;

public class PdfTests
{
    private static string SaveImage(string path, int w, int h, SKColor color, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(w, h);
        bitmap.Erase(color);
        using var data = bitmap.Encode(format, 90);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    private static Card PdfCard(string key, CardType type, string? image) => Cards.Make(key, type, CardColor.Blue, 1, 1, 5000,
        name: $"\"Card\" {key}", traits: ["Magic"], triggers: [Trigger.Soul], image: image,
        text: "Auto: [<sprite name=\"1cost\"/>] When this card becomes <sprite name=\"reverse\"/>, <b>ENCORE</b> <color=red>Once per turn.</color>\nCont: ASSIST");

    [Fact]
    public void ProxiesAndTranslationSheet()
    {
        using var dir = new TempDir();
        var portrait = SaveImage(dir.Join("a.png"), 224, 313, new SKColor(200, 30, 30, 128), SKEncodedImageFormat.Png);
        var landscape = SaveImage(dir.Join("b.jpg"), 313, 224, new SKColor(30, 30, 200), SKEncodedImageFormat.Jpeg);
        var cards = Enumerable.Range(0, 20).Select(i => (i % 7) switch
        {
            0 => PdfCard($"KS/W49-E{i % 7:000}", CardType.Climax, landscape),
            1 => PdfCard($"KS/W49-E{i % 7:000}", CardType.Character, null),
            _ => PdfCard($"KS/W49-E{i % 7:000}", CardType.Character, portrait),
        }).ToList();
        var deck = new Deck { Name = "Test", Cards = cards };

        Assert.Equal((3, 3), Printouts.ProxyGrid(Paper.A4));
        Assert.Equal((3, 3), Printouts.ProxyGrid(Paper.Letter));
        var proxies = Printouts.Proxies(deck, Paper.A4);
        Assert.Equal("%PDF-"u8.ToArray(), proxies[..5]);
        using (var read = PdfReader.Open(new MemoryStream(proxies), PdfDocumentOpenMode.Import))
            Assert.Equal(3, read.PageCount);
        // Each picture is embedded once, however many copies are printed: PDFsharp even shares
        // identical pictures between cards, so the 6 cards with artwork need 2 images.
        var text = System.Text.Encoding.Latin1.GetString(proxies);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(text, @"/Subtype\s*/Image"));

        var sheet = Printouts.Translation(deck, Paper.Letter);
        Assert.Equal("%PDF-"u8.ToArray(), sheet[..5]);
        using (var read = PdfReader.Open(new MemoryStream(sheet), PdfDocumentOpenMode.Import))
            Assert.True(read.PageCount >= 1);
        Assert.Contains("/FontFile2", System.Text.Encoding.Latin1.GetString(sheet));
    }

    [Fact]
    public void EffectText()
    {
        var runs = Pdf.Printouts.EffectRuns("Auto: [<sprite name=\"1cost\"/>] <sprite name=\"reverse\"/> <color=red>ALARM</color>");
        Assert.Equal(
            [("AUTO", true), (" [", false), ("(1)", true), ("] ", false), ("[Reverse]", true), (" ", false), ("Alarm", true)],
            runs.Select(r => (r.Text, r.Bold)));
        Assert.Equal(Pdf.Printouts.Red, runs[^1].Color);
    }

    [Fact]
    public void Wrapping()
    {
        using var doc = new Doc(Paper.A4);
        var block = new Block([new Run("aaa bbb ccc", false, PdfSharp.Drawing.XColors.Black)], 10);
        var width = doc.TextWidth("aaa bbb", false, 10) + 1;
        var lines = doc.Wrap(block, width).Select(l => l.Select(p => p.Text).ToList()).ToList();
        Assert.Equal([["aaa bbb"], ["ccc"]], lines);
    }
}

/// <summary>End-to-end on a fake simulator folder: parse cards, then save and load decks.</summary>
public class EndToEndTests
{
    private static void FakeSimulator(TempDir dir)
    {
        dir.Write("Weiss Schwarz_Data/StreamingAssets/CommonEffects_USER_REFERENCE.txt",
            "﻿//comment\nDefine: Encore\n\tTag Encore\n\tText [A] ENCORE [(3)]\n");
        dir.Write("CardContent/Cards/Konosuba/SingleSetData.txt",
            "Series: KonoSuba\nFolder: Konosuba/W49\nPrefix: KS/W49\nSetName: Konosuba\n");
        dir.Write("CardContent/Cards/Konosuba/W49/CardData.txt",
            "Character: KS/W49-E001\nName Aqua\nColor B\nLevel 1\nPower 5000\nTrait1 Goddess\n*Encore\nEndCard\n" +
            "Climax: KS/W49-E099\nName Splash\n*GateClimax\nEndCard\n" +
            "Character: KS/W49-E002\nName Megumin\nColor R\nLevel 0\nPower 2500\nEndCard\n");
        dir.Write("CardContent/Cards/Konosuba/W49/KS_W49_E001.jpg", "");
    }

    [Fact]
    public void LoadAndImportRoundTrip()
    {
        using var dir = new TempDir();
        FakeSimulator(dir);
        var folder = SimulatorFolder.Find(dir.Join("CardContent"))!;

        var series = folder.LoadSeries();
        var cards = series.Single(s => s.Name == "KonoSuba").Sets[0].Cards;
        Assert.Equal(3, cards.Count);
        Assert.Equal("[A] ENCORE [(3)]\n", cards["KS/W49-E001"].Text);
        Assert.NotNull(cards["KS/W49-E001"].Image);
        Assert.Equal([Trigger.Pant, Trigger.Soul], cards["KS/W49-E099"].Triggers);
        Assert.Null(cards["KS/W49-E002"].Image);

        // Encore Decks gives JP-style codes; they resolve to the simulator's EN keys.
        var index = new CardIndex(series);
        var codes = CardCodes.TransformToExistingKeys(index.Codes(), ["KS/W49-001", "KS/W49-099", "KS/W49-002SP", "XX/Y1-001"]);
        Assert.Equal(["KS/W49-E001", "KS/W49-E099", "KS/W49-E002", "XX/Y1-001"], codes);

        DeckFiles.SaveNew(folder.Decks, "Test", "10:00  01/01/2025", "", codes);
        var stored = Assert.Single(DeckFiles.List(folder.Decks));
        Assert.Equal(codes, stored.Codes);

        var deck = index.ResolveDeck("Test", "", stored.Codes);
        Assert.Equal(["XX/Y1-001"], deck.Unmatched);
        CardSorting.SortForDeck(deck.Cards);
        Assert.Equal(
            [("Megumin", CardType.Character), ("Aqua", CardType.Character), ("Splash", CardType.Climax)],
            deck.Cards.Select(c => (c.Name, c.Type)));
    }

    /// <summary>Loads a real install: SIMULATOR_ROOT=~/Games/SimuWeiss dotnet test --filter Category=RealInstall.</summary>
    [RealInstallFact]
    [Trait("Category", "RealInstall")]
    public void RealInstall()
    {
        var root = Environment.GetEnvironmentVariable("SIMULATOR_ROOT") ?? throw new InvalidOperationException("set SIMULATOR_ROOT");
        var folder = SimulatorFolder.Find(root) ?? throw new InvalidOperationException("not a simulator folder");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var series = folder.LoadSeries();
        var all = series.SelectMany(s => s.AllCards).ToList();
        Console.WriteLine($"{series.Count} series, {series.Sum(s => s.Sets.Count)} sets, {all.Count} cards in {watch.Elapsed}; " +
            $"{all.Count(c => c.Image is null)} without image");
        Assert.NotEmpty(series);
        var index = new CardIndex(series);
        foreach (var deck in DeckFiles.List(folder.Decks))
        {
            var built = index.ResolveDeck(deck.Name, deck.Date, deck.Codes);
            Console.WriteLine($"deck \"{deck.Name}\"{(deck.Ai ? " (AI)" : "")}: {built.Cards.Count} cards, missing {string.Join(", ", built.Unmatched)}");
        }
    }
}
