using System.Text;
using SimuDeckParser.Simulator;
using SkiaSharp;

namespace SimuDeckParser.Tests;

public class FolderTests
{
    [Fact]
    public void FindsRootFromSubfolders()
    {
        using var dir = new TempDir();
        var root = dir.Join("SimuWeiss");
        Directory.CreateDirectory(Path.Combine(root, "CardContent", "Cards"));
        dir.Write("SimuWeiss/Weiss Schwarz_Data/StreamingAssets/CommonEffects_USER_REFERENCE.txt", "");

        var folder = SimulatorFolder.At(root)!;
        Assert.Equal(folder, SimulatorFolder.Find(Path.Combine(root, "CardContent", "Cards")));
        Assert.Equal(folder, SimulatorFolder.Find(Path.Combine(root, "Weiss Schwarz_Data")));
        Assert.Equal(folder, SimulatorFolder.Find(Path.Combine(root, "Decks")));
        Assert.Null(SimulatorFolder.Find(dir.Path));
        Assert.EndsWith("CommonEffects_USER_REFERENCE.txt", folder.CommonEffects());
        Assert.Equal(Path.Combine(root, "Decks"), folder.Decks);
    }

    [Fact]
    public void SearchesGameFolders()
    {
        using var dir = new TempDir();
        var wanted = dir.Join("Games", "SimuWeiss");
        Directory.CreateDirectory(Path.Combine(wanted, "CardContent", "Cards"));
        // Too deep inside a folder whose name says nothing about games.
        Directory.CreateDirectory(dir.Join("Documents", "a", "b", "Simu", "CardContent", "Cards"));
        var found = new List<string>();
        SimulatorFolder.Search(dir.Path, 0, found);
        Assert.Equal([wanted], found);
    }
}

public class SeriesFileTests
{
    [Fact]
    public void LoadsSeriesAndSets()
    {
        using var dir = new TempDir();
        var cards = dir.Join("CardContent", "Cards");
        var alternate = dir.Join("AlternateArtwork");
        Directory.CreateDirectory(Path.Combine(cards, "Konosuba", "W49"));
        Directory.CreateDirectory(Path.Combine(cards, ".hidden"));
        Directory.CreateDirectory(Path.Combine(cards, "Key", "W50"));
        dir.Write("CardContent/Cards/Konosuba/SingleSetData.txt",
            "\uFEFFSeries: KonoSuba!\r\nFolder: Konosuba/W49\nPrefix: KS/W49\nSetName: Booster\n\n" +
            "Folder: Key/W50\nPrefix: K/W50\nPrefix: K2/W50\nFileName: Premium\n\n" +
            "Folder: Konosuba/Missing\nSetName: Not shipped\n");
        dir.Write("CardContent/Cards/Konosuba/W49/CardData.txt", "Character: KS/W49-E001\nName Aqua\nEndCard\n");
        dir.Write("CardContent/Cards/Key/W50/Premium.txt",
            [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("Character: K/W50-E001\r\nName Kud\r\nEndCard\r\n")]);
        dir.Write("AlternateArtwork/ENW49/KS_W49_E001.jpg", "");

        var series = SeriesFile.LoadSeries(cards, alternate, new CommonEffects());
        // Key holds card folders only, so it is not a serie.
        var s = Assert.Single(series);
        Assert.Equal("KonoSuba!", s.Name);
        Assert.Equal(2, s.Sets.Count);
        Assert.Equal(("KS/W49", "Booster"), (s.Sets[0].Key, s.Sets[0].Name));
        var card = s.Sets[0].Cards["KS/W49-E001"];
        Assert.Equal("Aqua", card.Name);
        Assert.EndsWith(Path.Combine("ENW49", "KS_W49_E001.jpg"), card.Image);
        // No SetName: named after its first prefix, cards read from the FileName file.
        Assert.Equal("K/W50", s.Sets[1].Name);
        Assert.Equal("Kud", s.Sets[1].Cards["K/W50-E001"].Name);
    }
}

public class CommonEffectsTests
{
    [Fact]
    public void ParsesDefinitions()
    {
        var fx = CommonEffects.Parse(
            "// comment\nDefine: Brainstorm($X)\nQuick\nAuto: something\nText [A] BRAINSTORM put the top $X cards\n" +
            "into your waiting room.\nTag Brainstorm\n\nDefine: Encore\n" +
            "Text [A] ENCORE [Put 1 character from your hand into your waiting room]\n");
        Assert.Equal(2, fx.Text.Count);
        Assert.Equal("[A] BRAINSTORM put the top $X cardsinto your waiting room.", fx.Text["Brainstorm($X)"]);
        Assert.Equal("[A] ENCORE [Put 1 character from your hand into your waiting room]", fx.Text["Encore"]);
        Assert.Equal("Auto: something\n", fx.Script["Brainstorm($X)"]);
        Assert.False(fx.Script.ContainsKey("Encore"));
    }
}

public class CardImagesTests
{
    [Fact]
    public void ResolveImages()
    {
        using var dir = new TempDir();
        var set = dir.Join("Cards", "W49");
        var alt = dir.Join("AlternateArtwork");
        foreach (var file in new[] { "KS_W49_E001.jpg", "ks_w49_e002.jpg", "W49_E004.jpg", "ks_w49_005.png", "035.jpg", "007.jpg", "notes.txt" })
            dir.Write($"Cards/W49/{file}", "");
        dir.Write("AlternateArtwork/ENW49/KS_W49_E003SP.jpg", "");
        var images = ImageDir.Read(set);
        string? Resolve(string key, string? hint = null) => CardImages.Resolve(images, key, hint, alt);

        Assert.Equal(Path.Combine(set, "KS_W49_E001.jpg"), Resolve("KS/W49-001"));
        // -E rewrite + lowercase.
        Assert.Equal(Path.Combine(set, "ks_w49_e002.jpg"), Resolve("KS/W49-002"));
        Assert.Equal(Path.Combine(alt, "ENW49", "KS_W49_E003SP.jpg"), Resolve("KS/W49-003SP"));
        // Without the series prefix, png, and the card's Image line.
        Assert.Equal(Path.Combine(set, "W49_E004.jpg"), Resolve("KS/W49-004"));
        Assert.Equal(Path.Combine(set, "ks_w49_005.png"), Resolve("KS/W49-005"));
        Assert.Equal(Path.Combine(set, "035.jpg"), Resolve("KS/W49-006", "035"));
        Assert.Equal(Path.Combine(set, "007.jpg"), Resolve("KS/W49-007"));
        Assert.Null(Resolve("KS/W49-999"));
    }
}

public class CardFileTests
{
    private const string Data = """
        Character: KS/W49-E001
        Image KS/W49-E001
        Name Megumin
        Color R
        Level 3
        Cost 2
        Power 10000
        Soul 2
        Trigger Soul
        Trait1 Magic
        Trait2 Crimson Demon
        Auto: script line 1
        script line 2
        Text [A] When this card attacks,
        deal 1 damage.
        *Brainstorm(2)
        *Encore()
        *UnknownEffect(1)
        EndCard

        Climax: KS/W49-E099
        Name Explosion!
        *BookClimax
        Text [C] All your characters get +1000 power.
        EndCard

        Combo KS/W49-E001
        Character: KS/W49-E001SP
        Clone KS/W49-E001
        Trait3 Foil
        EndCard

        """;

    private static SortedDictionary<string, Card> Parse()
    {
        var effects = CommonEffects.Parse("Define: Brainstorm($X)\nText BRAINSTORM top $X cards|or not\nDefine: Encore\nText ENCORE pay 3\n");
        return CardFile.Parse(Data, "/nonexistent", "/nonexistent", effects);
    }

    [Fact]
    public void Character()
    {
        var c = Parse()["KS/W49-E001"];
        Assert.Equal(CardType.Character, c.Type);
        Assert.Equal("Megumin", c.Name);
        Assert.Equal(CardColor.Red, c.Color);
        Assert.Equal((3u, 2u, 10000, 2u), (c.Level, c.Cost, c.Power, c.SoulCount));
        Assert.Equal([Trigger.Soul], c.Triggers);
        Assert.Equal(["Magic", "Crimson Demon"], c.Traits);
        Assert.Equal("Auto: script line 1\nscript line 2\n", c.Code);
        Assert.Equal("[A] When this card attacks,\ndeal 1 damage.\nBRAINSTORM top 2 cards or or not\nENCORE pay 3\nUnknownEffect(1)\n", c.Text);
        Assert.Null(c.Image);
    }

    [Fact]
    public void Climax()
    {
        var c = Parse()["KS/W49-E099"];
        Assert.Equal(CardType.Climax, c.Type);
        Assert.Equal([Trigger.Book], c.Triggers);
        Assert.Equal(CardColor.Blue, c.Color);
        Assert.Equal(0u, c.SoulCount);
        Assert.Equal(1, c.Power);
    }

    [Fact]
    public void CloneCopiesAndExtends()
    {
        var cards = Parse();
        var c = cards["KS/W49-E001SP"];
        Assert.Equal("Megumin", c.Name);
        Assert.Equal(3u, c.Level);
        Assert.Equal(["Magic", "Crimson Demon", "Foil"], c.Traits);
        Assert.Equal(cards["KS/W49-E001"].Text, c.Text);
    }

    [Fact]
    public void CommonEffectsAreExpandedInTheScript()
    {
        var effects = CommonEffects.Parse(
            "Define: OptionalBurn(AMOUNT)\n\tIf Yes\n\t{\n\t\tBurn AMOUNT\n\t}\n" +
            "Define: OnCancelBurn1\n\tAuto: OnDamageCancel\n\t{\n\t\tBurn 1\n\t}\n" +
            "\tText Auto: When damage dealt by this card is canceled, deal 1 damage.\n");
        var cards = CardFile.Parse(
            "Character: A-001\nName Burner\nAuto: OnAttack\n{\n\t*OptionalBurn(2)\n}\n" +
            "Text Auto: When this card attacks, you may deal 2 damage.\n*OnCancelBurn1\nEndCard\n",
            "/nowhere", "/nowhere", effects);
        var c = cards["A-001"];
        Assert.Equal("Auto: OnAttack\n{\nIf Yes\n{\nBurn 2\n}\n}\nAuto: OnDamageCancel\n{\nBurn 1\n}\n", c.Code);
        Assert.EndsWith("When damage dealt by this card is canceled, deal 1 damage.\n", c.Text);
    }

    [Fact]
    public void NewFormatEventsAndTypos()
    {
        var cards = CardFile.Parse(
            "Event: AYT/W110-027\nName Memory\nJPName 思い出\nRequirement StageContains 1\n" +
            "Text If you do not have a <Omiko City> character, this card cannot be played.\n" +
            "Effect:\n{\n    *Check4Add1(Omiko City)\n    Bounce\n}\n" +
            "Text Look at up to 4 cards.\\nYou may return it.\nEndCard\nEndCard\n" +
            "Event:: SMP/W60-096\nName Typo\nEndCard\n",
            "/nowhere", "/nowhere", new CommonEffects());
        Assert.Equal(2, cards.Count); // A stray EndCard adds no card.
        var e = cards["AYT/W110-027"];
        Assert.Equal(
            "If you do not have a <Omiko City> character, this card cannot be played.\nLook at up to 4 cards.\nYou may return it.\n",
            e.Text);
        Assert.Contains("Bounce", e.Code);
        Assert.Equal("Typo", cards["SMP/W60-096"].Name);

        // A clone of a card defined further down the file.
        cards = CardFile.Parse(
            "Character: A-002\nClone A-001\nTrait1 Foil\nEndCard\nCharacter: A-001\nName Source\nLevel 2\nTrait1 Magic\nEndCard\n",
            "/nowhere", "/nowhere", new CommonEffects());
        Assert.Equal("Source", cards["A-002"].Name);
        Assert.Equal(2u, cards["A-002"].Level);
        Assert.Equal(["Magic", "Foil"], cards["A-002"].Traits);
    }
}

public class DeckFilesTests
{
    [Fact]
    public void Parse()
    {
        var deck = DeckFiles.Parse(
            "\uFEFF# WeissSim Deck\nName: My deck\nDate: 12:00  06/15/2010\nSleeve: RZ Emilia 1.jpg\n\n" +
            "Cards:\nBD/W95-017\r\nBD/W95-017\n\nBD/W95-001\n",
            "/d/AI_file.txt");
        Assert.Equal("My deck", deck.Name);
        Assert.Equal("12:00  06/15/2010", deck.Date);
        Assert.Equal(new DateTime(2010, 6, 15, 12, 0, 0), deck.SavedAt);
        Assert.Equal("RZ Emilia 1.jpg", deck.Sleeve);
        Assert.Equal(["BD/W95-017", "BD/W95-017", "BD/W95-001"], deck.Codes);
        Assert.True(deck.Ai);
        Assert.Equal("Fallback", DeckFiles.Parse("Cards:\nA\n", "x/Fallback.txt").Name);
        Assert.Equal("", DeckFiles.Parse("# WeissSim Deck\nName: X\nCards:\nA\n", "x/None.txt").Sleeve);
    }

    [Fact]
    public void SaveAndList()
    {
        using var dir = new TempDir();
        var decks = dir.Join("Decks");
        Assert.Empty(DeckFiles.List(decks));

        string[] codes = ["KS/W49-E001", "KS/W49-E002"];
        var a = DeckFiles.SaveNew(decks, "Aqua / Eris?", "10:00  01/01/2025", "", codes);
        Assert.EndsWith("Aqua - Eris-.txt", a.Path);
        Assert.Equal("Aqua / Eris?", a.Name);
        Assert.Equal("", a.Sleeve);
        var b = DeckFiles.SaveNew(decks, "Aqua / Eris?", "10:00  01/01/2025", "RZ Emilia 1.jpg", codes);
        Assert.Equal("Aqua / Eris? (2)", b.Name);
        Assert.EndsWith("Aqua - Eris- (2).txt", b.Path);
        File.WriteAllText(Path.Combine(decks, "AI_Bot.txt"), DeckFiles.Format("AI_Bot", "", "KS/W49-E001", codes));
        File.WriteAllText(Path.Combine(decks, "notes.md"), "");

        var listed = DeckFiles.List(decks);
        Assert.Equal(["Aqua / Eris?", "Aqua / Eris? (2)", "AI_Bot"], listed.Select(d => d.Name));
        Assert.Equal(a, listed[0]);
        Assert.Equal("RZ Emilia 1.jpg", listed[1].Sleeve);
    }

    [Fact]
    public void FormatRoundTrip()
    {
        string[] codes = ["BD/W95-017"];
        // The simulator writes Sleeve: with nothing after the colon when there is none.
        var none = DeckFiles.Format("My deck", "12:00  06/15/2010", "", codes);
        Assert.Contains("Sleeve:\n", none);
        Assert.DoesNotContain("Sleeve: \n", none);
        Assert.Equal(
            new DeckFile { Name = "My deck", Date = "12:00  06/15/2010", Codes = codes, Path = "d/My deck.txt" },
            DeckFiles.Parse(none, "d/My deck.txt"));
        var withSleeve = DeckFiles.Format("My deck", "12:00  06/15/2010", " RZ Emilia 1.jpg ", codes);
        Assert.Contains("Sleeve: RZ Emilia 1.jpg\n", withSleeve);
        Assert.Equal("RZ Emilia 1.jpg", DeckFiles.Parse(withSleeve, "d/My deck.txt").Sleeve);
    }

    [Fact]
    public void UpdateAndRename()
    {
        using var dir = new TempDir();
        string[] codes = ["A-001"];
        var a = DeckFiles.SaveNew(dir.Path, "Alpha", "d", "", codes);
        DeckFiles.SaveNew(dir.Path, "Beta", "d", "", codes);

        string[] more = ["A-001", "A-002"];
        var same = DeckFiles.Update(a.Path, "Alpha", "e", "RZ Rem 1.jpg", more);
        Assert.Equal(a.Path, same.Path);
        Assert.Equal("RZ Rem 1.jpg", same.Sleeve);
        var listed = DeckFiles.List(dir.Path);
        Assert.Equal(more, listed[0].Codes);
        Assert.Equal("RZ Rem 1.jpg", listed[0].Sleeve);

        var renamed = DeckFiles.Update(same.Path, "Gamma", "e", "", more);
        Assert.EndsWith("Gamma.txt", renamed.Path);
        Assert.False(File.Exists(a.Path));
        Assert.Equal([("Beta", ""), ("Gamma", "")], DeckFiles.List(dir.Path).Select(d => (d.Name, d.Sleeve)));

        Assert.Throws<UserException>(() => DeckFiles.Update(renamed.Path, "beta", "e", "", more));
        Assert.True(File.Exists(renamed.Path));
    }
}

public class SleevesTests
{
    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(2, 3);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void SleevesOfAFolder()
    {
        using var dir = new TempDir();
        Assert.Empty(Sleeves.List(dir.Path));
        dir.Write("Sleeves/B sleeve.jpg", "");
        dir.Write("Sleeves/a sleeve.png", "");
        dir.Write("Sleeves/ignored.txt", "");
        var listed = Sleeves.List(dir.Join("Sleeves"));
        Assert.Equal(["a sleeve.png", "B sleeve.jpg"], listed.Select(s => s.Name));
        Assert.EndsWith("a sleeve.png", listed[0].Path);
    }

    [Fact]
    public void DisplayNames()
    {
        Assert.Equal("RZ Emilia 1", Sleeve.DisplayName("RZ Emilia 1.jpg"));
        Assert.Equal("a", Sleeve.DisplayName("a.png"));
        Assert.Equal("no extension", Sleeve.DisplayName("no extension"));
    }

    [Fact]
    public void Sources()
    {
        Assert.Null(SleeveSource.Parse("  "));
        Assert.Equal(new SleeveSource.Url("https://x.org/a.png"), SleeveSource.Parse(" https://x.org/a.png "));
        Assert.Equal(new SleeveSource.File("/home/me/my sleeve.png"), SleeveSource.Parse("\"/home/me/my sleeve.png\""));
        Assert.Equal(new SleeveSource.File("/home/me/my sleeve.png"), SleeveSource.Parse("file:///home/me/my%20sleeve.png"));
        Assert.Equal(new SleeveSource.File("C:/Games/x.png"), SleeveSource.Parse("file:///C:/Games/x.png"));
    }

    [Fact]
    public void Stems()
    {
        string Url(string u) => new SleeveSource.Url(u).Stem();
        Assert.Equal("RZ Rem", Url("https://x.org/img/RZ%20Rem.jpg?w=200#top"));
        Assert.Equal("", Url("https://x.org"));
        Assert.Equal("", Url("https://x.org/"));
        Assert.Equal("Emilia", new SleeveSource.File("/a/b/Emilia.png").Stem());
    }

    [Fact]
    public async Task AddsASleeveFromAFile()
    {
        using var dir = new TempDir();
        var source = new SleeveSource.File(dir.Write("Rem.png", Png()));
        var sleeves = dir.Join("Sleeves");

        var first = await Sleeves.AddAsync(sleeves, source, "");
        Assert.Equal("Rem.png", first.Name);
        Assert.Equal(Png(), File.ReadAllBytes(first.Path));

        // Never overwrites, whatever the case of the taken name.
        File.WriteAllText(Path.Combine(sleeves, "rem (2).JPG"), "");
        Assert.Equal("Rem (3).png", (await Sleeves.AddAsync(sleeves, source, "")).Name);
        Assert.Equal("Re-Zero - Rem.png", (await Sleeves.AddAsync(sleeves, source, "Re:Zero / Rem")).Name);
        Assert.Equal("Sleeve.png", (await Sleeves.AddAsync(sleeves, source, " . ")).Name);
    }

    [Fact]
    public async Task RefusesWhatIsNotAUsableImage()
    {
        using var dir = new TempDir();
        var sleeves = dir.Join("Sleeves");

        await Assert.ThrowsAsync<UserException>(() => Sleeves.AddAsync(sleeves, new SleeveSource.File(dir.Join("nope.png")), ""));

        var html = dir.Write("page.png", "<html></html>");
        var error = await Assert.ThrowsAsync<UserException>(() => Sleeves.AddAsync(sleeves, new SleeveSource.File(html), ""));
        Assert.Contains("not an image", error.Message);

        var gif = dir.Write("a.gif", "GIF89a\x01\x00\x01\x00\x00\x00\x00;"u8.ToArray());
        error = await Assert.ThrowsAsync<UserException>(() => Sleeves.AddAsync(sleeves, new SleeveSource.File(gif), ""));
        Assert.Contains("GIF", error.Message);

        var truncated = dir.Write("cut.png", Png()[..20]);
        await Assert.ThrowsAnyAsync<Exception>(() => Sleeves.AddAsync(sleeves, new SleeveSource.File(truncated), ""));

        // Nothing was written.
        Assert.Empty(Sleeves.List(sleeves));
    }
}
