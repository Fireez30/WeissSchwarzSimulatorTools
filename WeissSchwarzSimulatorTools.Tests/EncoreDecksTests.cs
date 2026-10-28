using System.Text.Json.Nodes;
using WeissSchwarzSimulatorTools.EncoreDecks;

namespace WeissSchwarzSimulatorTools.Tests;

public class EncoreDecksTests
{
    private static JsonNode Json(string text) => JsonNode.Parse(text)!;

    [Fact]
    public void LinkParsing()
    {
        Assert.Equal("PPyvcLuvt", Decks.DeckIdFromLink(" https://www.encoredecks.com/deck/PPyvcLuvt "));
        Assert.Equal("PPyvcLuvt", Decks.DeckIdFromLink("https://www.encoredecks.com/deck/PPyvcLuvt/?x=1"));
        Assert.Null(Decks.DeckIdFromLink("PPyvcLuvt"));
        Assert.Null(Decks.DeckIdFromLink("https://www.encoredecks.com/"));
    }

    [Fact]
    public void DateFormat()
    {
        Assert.Equal("22:20  09/30/2025", Decks.SimulatorDate("2025-09-30T22:20:36.123Z"));
        Assert.Null(Decks.SimulatorDate("garbage"));
    }

    [Fact]
    public void JsonParsing()
    {
        var deck = Decks.ParseDeckJson(Json("""
            {"name": " My deck ", "datemodified": "2025-01-02T03:04:05Z",
             "cards": [{"cardcode": "KS/W49-E001 "}, {"cardcode": "KS/W49-E002"}, {"x": 1}]}
            """));
        Assert.Equal("My deck", deck.Name);
        Assert.Equal("03:04  01/02/2025", deck.Date);
        Assert.Equal(["KS/W49-E001", "KS/W49-E002"], deck.Codes);
        Assert.Throws<UserException>(() => Decks.ParseDeckJson(Json("""{"cards": []}""")));
        var nullCreated = Decks.ParseDeckJson(Json("""
            {"name": "x", "datecreated": null, "datemodified": "2025-10-01T03:35:02.799Z", "cards": [{"cardcode": "A/B-001"}]}
            """));
        Assert.Equal("03:35  10/01/2025", nullCreated.Date);
    }

    [Fact]
    public void SearchPageParsing()
    {
        Assert.Equal("https://www.encoredecks.com/deck/PPyvcLuvt", Decks.DeckLink(" PPyvcLuvt "));
        // Shape of /api/search/deck (fields the select leaves out are absent).
        var page = Decks.ParseSearchJson(Json("""
            {"decks": [
                {"name": " My deck ", "deckid": "PPyvcLuvt", "datemodified": "2025-01-02T03:04:05Z", "description": " fun "},
                {"name": "", "deckid": "  ", "description": null},
                {"name": "Old deck", "deckid": "abc", "datecreated": "2024-12-31T23:59:59.999Z", "description": ""}
             ],
             "totalDecks": 25, "limit": 24, "totalPages": 2, "page": 1, "hasNextPage": true}
            """));
        Assert.Equal(
            [new AccountDeck("My deck", "PPyvcLuvt", "03:04  01/02/2025", "fun"), new AccountDeck("Old deck", "abc", "23:59  12/31/2024", "")],
            page.Decks);
        Assert.Equal((1, 2, 25), (page.Page, page.TotalPages, page.TotalDecks));
        Assert.Throws<UserException>(() => Decks.ParseSearchJson(Json("""{"message": "Something went wrong"}""")));
        var empty = Decks.ParseSearchJson(Json("""{"decks": []}"""));
        Assert.Equal((0, 1), (empty.Decks.Count, empty.TotalPages));
    }

    [Fact]
    public void LoginBodyIsJson()
    {
        var body = AccountApi.LoginBody(" me@x.y ", "p\"a\\ss\u0001é");
        var json = JsonNode.Parse(body)!;
        Assert.Equal("me@x.y", (string)json["username"]!);
        Assert.Equal("p\"a\\ss\u0001é", (string)json["password"]!);
    }

    [Fact]
    public void SessionParsing()
    {
        Assert.Equal("s%3Aab.c%2Fd",
            AccountApi.SessionFromSetCookie("encoresesssionid=s%3Aab.c%2Fd; Path=/; Expires=Wed, 29 Sep 2027; HttpOnly"));
        Assert.Null(AccountApi.SessionFromSetCookie("other=x; Path=/"));
        Assert.Null(AccountApi.SessionFromSetCookie("encoresesssionid=; Path=/"));

        Assert.Equal("Benji", AccountApi.NameFromUserRedirect("/user/Benji"));
        Assert.Equal("A Bé", AccountApi.NameFromUserRedirect("https://www.encoredecks.com/user/A%20B%C3%A9"));
        Assert.Null(AccountApi.NameFromUserRedirect("/login"));
        Assert.Null(AccountApi.NameFromUserRedirect("/user/"));

        var printed = new Account("Benji", "s%3Asecret").ToString();
        Assert.True(printed.Contains("Benji") && !printed.Contains("secret"));
    }

    [Fact]
    public async Task CardIdResolution()
    {
        string[] codes = ["KS/W49-001", "KS/W49-001", " ", "X/Y-1", "KS/W49-002SP"];
        var calls = new List<string>();
        var (ids, missing) = await Upload.ResolveCardIdsAsync(codes, c =>
        {
            calls.Add(c);
            return Task.FromResult(c switch
            {
                "KS/W49-E001" => "id1",
                "KS/W49-E002SP" => "id2",
                _ => (string?)null,
            });
        });
        Assert.Equal(["id1", "id1", "id2"], ids);
        Assert.Equal(["X/Y-1"], missing);
        // A code is looked up once, however many copies the deck has.
        Assert.Single(calls, c => c == "KS/W49-001");
    }

    [Fact]
    public void UploadFailSafe()
    {
        var full = Enumerable.Repeat("A/B-001", DeckList.DeckSize).ToArray();
        Upload.CheckDeckSize(full);
        Assert.Throws<UserException>(() => Upload.CheckDeckSize(full[1..]));
        var blank = full.ToArray();
        blank[0] = " ";
        Assert.Throws<UserException>(() => Upload.CheckDeckSize(blank));

        var ids = Enumerable.Repeat("id", DeckList.DeckSize).ToArray();
        Upload.CheckAllFound(ids, []);
        Assert.Contains("X/Y-1", Assert.Throws<UserException>(() => Upload.CheckAllFound(ids[1..], ["X/Y-1"])).Message);
        Assert.Throws<UserException>(() => Upload.CheckAllFound(ids[1..], []));
    }

    [Fact]
    public void SaveBodyAndResponse()
    {
        var body = Upload.SaveRequest(" Deck ", ["a", "a"]);
        Assert.Equal("Deck", (string)body["name"]!);
        Assert.Equal("""["a","a"]""", body["cards"]!.ToJsonString());
        Assert.Equal("[]", body["attribute-group"]!.ToJsonString());
        Assert.Equal("https://www.encoredecks.com/deck/abc", Upload.DeckUrlFromResponse(Json("""{"deck": {"deckid": "abc"}}""")));
        Assert.Throws<UserException>(() => Upload.DeckUrlFromResponse(Json("""{"message": "something went wrong"}""")));
    }

    /// <summary>Hits the network (read-only): WSST_LIVE_TESTS=1 dotnet test --filter Category=Live.</summary>
    [LiveFact]
    [Trait("Category", "Live")]
    public async Task LiveEncoreDecks()
    {
        var deck = await Decks.FetchDeckAsync("PPyvcLuvt");
        Assert.NotEmpty(deck.Name);
        Assert.Equal(50, deck.Codes.Count);
        Assert.Null(await AccountApi.AccountNameAsync("s%3Anot-a-session"));
        await Assert.ThrowsAsync<SessionExpiredException>(() => Decks.FetchAccountDecksAsync("s%3Anot-a-session", 1));
        var error = await Assert.ThrowsAsync<UserException>(() => AccountApi.LoginAsync("nobody@example.invalid", "wrong"));
        Assert.Contains("wrong email or password", error.Message);
    }

    /// <summary>Uses the real credential store: WSST_LIVE_TESTS=1 dotnet test --filter Category=Live.</summary>
    [LiveFact]
    [Trait("Category", "Live")]
    public void LiveCredentialStore()
    {
        const string account = "weiss_schwarz_simulator_tools test account";
        Credentials.SaveSession(account, "s%3Atest");
        Assert.Equal("s%3Atest", Credentials.LoadSession(account));
        Credentials.DeleteSession(account);
        Assert.Null(Credentials.LoadSession(account));
        Credentials.DeleteSession(account);
    }
}
