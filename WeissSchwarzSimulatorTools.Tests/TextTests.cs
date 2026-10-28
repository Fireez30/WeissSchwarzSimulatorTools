using System.Text;

namespace WeissSchwarzSimulatorTools.Tests;

public class TextTests
{
    [Fact]
    public void TrailingAlphas()
    {
        Assert.Equal("KS/W49-E073", TextHelpers.RemoveTrailingAlphas("KS/W49-E073SP"));
        Assert.Equal("KS/W49-E073", TextHelpers.RemoveTrailingAlphas("KS/W49-E073"));
        Assert.Equal("", TextHelpers.RemoveTrailingAlphas("abc"));
    }

    [Fact]
    public void IgnoreCase()
    {
        Assert.True(TextHelpers.ContainsIgnoreCase("", "anything"));
        Assert.True(TextHelpers.ContainsIgnoreCase("miku", "Hatsune MIKU"));
        Assert.False(TextHelpers.ContainsIgnoreCase("rin", "Miku"));
    }

    [Fact]
    public void Decoding()
    {
        var le = Encoding.Unicode.GetBytes("Name é");
        var be = Encoding.BigEndianUnicode.GetBytes("Name é");
        Assert.Equal("Name é", TextHelpers.DecodeText([0xFF, 0xFE, .. le]));
        Assert.Equal("Name é", TextHelpers.DecodeText([0xFE, 0xFF, .. be]));
        Assert.Equal("Name é", TextHelpers.DecodeText(le));
        Assert.Equal("Name é", TextHelpers.DecodeText(Encoding.UTF8.GetBytes("﻿Name é")));
        Assert.Equal("Name é", TextHelpers.DecodeText(Encoding.UTF8.GetBytes("Name é")));
    }

    [Fact]
    public void Lines()
    {
        Assert.Equal(["a", "b", "", "c"], TextHelpers.Lines("a\r\nb\n\nc\n"));
        Assert.Empty(TextHelpers.Lines(""));
    }

    [Fact]
    public void RichText()
    {
        static Rich Plain(string text) => new Rich.Text(text, default);
        var bold = new TextStyle(Bold: true);
        var red = new TextStyle(Highlight: true);
        Assert.Equal(
            [Plain("Auto: ["), new Rich.Sprite("1cost"), Plain("] When <Magic> is "), new Rich.Text("here", bold)],
            TextHelpers.ParseRich("Auto: [<sprite name=\"1cost\"/>] When <Magic> is <b>here</b>"));
        Assert.Equal(
            [new Rich.Text("x", red), Plain(" y <hololive")],
            TextHelpers.ParseRich("<color=red>x</color> y <hololive"));
        Assert.Equal(
            [new Rich.Text("a", bold), new Rich.Text("b", bold), new Rich.Text("c", bold), Plain("d")],
            TextHelpers.ParseRich("<b>a<b>b</b>c</b>d"));
        Assert.Equal([new Rich.Sprite("reverse")], TextHelpers.ParseRich("<sprite name=reverse>"));
        Assert.Empty(TextHelpers.ParseRich(""));
    }

    [Fact]
    public void QuotedNames()
    {
        string[] names = ["Aqua", "Megumin", "\"Significance of Killing Snow Sprites?\" Aqua"];
        List<NameSpan> Split(string text) => TextHelpers.SplitNames(text, names.Contains);
        Assert.Equal(
            [
                new("your ", null),
                new("\"Aqua\"", "Aqua"),
                new(" and ", null),
                new("「Megumin」", "Megumin"),
                new("'s \"Darkness\" get ", null),
            ],
            Split("your \"Aqua\" and 「Megumin」's \"Darkness\" get "));
        Assert.Equal(
            [
                new("If ", null),
                new("'\"Significance of Killing Snow Sprites?\" Aqua'", names[2]),
                new(" is in", null),
            ],
            Split("If '\"Significance of Killing Snow Sprites?\" Aqua' is in"));
        Assert.Equal([new NameSpan("“Aqua”", "Aqua")], Split("“Aqua”"));
        Assert.Equal([new NameSpan("a \"\" b \"open", null)], Split("a \"\" b \"open"));
        Assert.Empty(Split(""));
    }

    [Fact]
    public void Sprites()
    {
        Assert.Equal(2u, TextHelpers.SpriteCost("2cost"));
        Assert.Null(TextHelpers.SpriteCost("xcost"));
        Assert.Equal(Trigger.Salvage, TextHelpers.SpriteTrigger("mini_door"));
        Assert.Equal("Counter", TextHelpers.SpriteWord("backup"));
        Assert.Equal("2 turn", TextHelpers.SpriteWord("2turn"));
        Assert.Equal("Clock", TextHelpers.SpriteWord("mini_clock"));
    }

    [Fact]
    public void PercentDecoding()
    {
        Assert.Equal("A Bé", TextHelpers.PercentDecode("A%20B%C3%A9"));
        Assert.Equal("100%", TextHelpers.PercentDecode("100%"));
        Assert.Equal("%zz", TextHelpers.PercentDecode("%zz"));
    }

    [Fact]
    public void Stem() => Assert.Equal("KS_W49_E073", TextHelpers.ImageStem(" KS/W49-E073 "));
}

public class CardCodesTests
{
    [Fact]
    public void EncoreSpellingsOrder()
    {
        var spellings = CardCodes.EncoreSpellings("ks/w49-001sp");
        Assert.Equal("ks/w49-001sp", spellings[0]);
        Assert.Equal("ks/w49-E001sp", spellings[1]);
        Assert.Contains("KS/W49-E001", spellings);
        Assert.Equal(spellings.Count, spellings.Distinct().Count());
        // No 'a' after a letter: 001SPa is never a card.
        Assert.DoesNotContain(spellings, s => s.EndsWith("spa") || s.EndsWith("SPa"));

        // Several arts: the first art comes before the plain code, for every rewrite.
        Assert.Equal(["SY/WE09-19a", "SY/WE09-19", "SY/WE09-E19a", "SY/WE09-E19"], CardCodes.EncoreSpellings("SY/WE09-19")[..4]);
    }

    private static string Transform(string[] existing, string key) =>
        CardCodes.TransformToExistingKeys(existing.ToHashSet(), [key])[0];

    [Fact]
    public void KeyVariantOrderMatchesOriginal()
    {
        var v = CardCodes.KeyVariants("AB/W1-T01S");
        Assert.Equal(28, v.Count);
        Assert.Equal(new CardCodes.Variant("AB/W1-T01S", false), v[0]);
        Assert.Equal(new CardCodes.Variant("AB/W1-T01S", true), v[1]);
        Assert.Equal(new CardCodes.Variant("AB/W1-ET01S", false), v[2]);
        Assert.Equal(new CardCodes.Variant("AB/W1-TE01S", false), v[6]);
        Assert.Equal(new CardCodes.Variant("AB/W1-T01", false), v[14]);
        Assert.Equal(new CardCodes.Variant("AB/W1-T01", false), v[16]); // -P rewrite is a no-op here
        Assert.Equal(new CardCodes.Variant("AB/W1-ET01", false), v[20]);
        Assert.Equal(new CardCodes.Variant("AB/W1-TE01", false), v[24]);
    }

    [Fact]
    public void TransformCases()
    {
        Assert.Equal("KS/W49-E073", Transform(["KS/W49-E073"], "KS/W49-E073"));
        Assert.Equal("KS/W49-E073", Transform(["KS/W49-E073"], "KS/W49-073"));
        Assert.Equal("KS/W49-073", Transform(["KS/W49-073"], "KS/W49-E073"));
        Assert.Equal("KS/W49-TE01", Transform(["KS/W49-TE01"], "KS/W49-T01"));
        Assert.Equal("KS/W49-PE01", Transform(["KS/W49-PE01"], "KS/W49-P01"));
        Assert.Equal("KS/W49-073", Transform(["KS/W49-073"], "KS/W49-073SP"));
        Assert.Equal("KS/W49-E073", Transform(["KS/W49-E073"], "KS/W49-073SP"));
        // A lowercase match keeps the original case.
        Assert.Equal("KS/W49-E073", Transform(["ks/w49-e073"], "KS/W49-E073"));
        // Unknown codes pass through.
        Assert.Equal("XX/Y1-001", Transform([], "XX/Y1-001"));
    }
}
