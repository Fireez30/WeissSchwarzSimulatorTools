// Publishing a simulator deck on encoredecks.com.
//
// Encore Decks stores decks as lists of its own card ids, so each card code is first looked up
// on the site (trying its other spellings). Nothing is sent unless every card was found: an
// uploaded deck is public, so it must be exactly the user's deck.

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WeissSchwarzSimulatorTools.EncoreDecks;

public static class Upload
{
    /// <summary>
    /// Publishes a deck and returns its link: in the linked account when there is a session, as
    /// an anonymous deck otherwise.
    ///
    /// Nothing is sent unless the deck has exactly <see cref="DeckList.DeckSize"/> cards, every
    /// one of them exists on Encore Decks, and the session (if any) is still logged in: an expired
    /// session would silently publish an anonymous deck instead, so
    /// <see cref="SessionExpiredException"/> is thrown.
    /// </summary>
    public static async Task<string> UploadAsync(string name, IReadOnlyList<string> codes, string? session)
    {
        if (name.Trim().Length == 0) throw new UserException("the deck has no name");
        CheckDeckSize(codes);
        var (ids, missing) = await ResolveCardIdsAsync(codes, FetchCardIdAsync);
        CheckAllFound(ids, missing);
        using var request = new HttpRequestMessage(HttpMethod.Post, Site.SaveApi)
        {
            Content = new StringContent(SaveRequest(name, ids).ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (session is not null)
        {
            if (await AccountApi.AccountNameAsync(session) is null)
                throw new SessionExpiredException();
            request.Headers.Add("Cookie", AccountApi.Cookie(session));
        }
        // The session cookie must not follow a redirect to another host.
        using var response = await Site.SendAsync(Http.NoRedirect, request, "sending the deck to encoredecks");
        try
        {
            Http.EnsureSuccess(response);
        }
        catch (UserException e)
        {
            throw Errors.Context("sending the deck to encoredecks", e);
        }
        return DeckUrlFromResponse(await Http.ReadJsonAsync(response));
    }

    /// <summary>Checked before anything is looked up: only full decks are published.</summary>
    public static void CheckDeckSize(IEnumerable<string> codes)
    {
        var count = codes.Count(c => c.Trim().Length > 0);
        if (count != DeckList.DeckSize)
            throw new UserException($"the deck has {count} cards; only {DeckList.DeckSize}-card decks can be uploaded");
    }

    /// <summary>Encore Decks' id of a card, or null when it has no card with this exact code.</summary>
    private static async Task<string?> FetchCardIdAsync(string code)
    {
        var url = $"{Site.CardApi}?cardcode={Uri.EscapeDataString(code)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await Site.SendAsync(Http.Client, request, $"looking up {code} on encoredecks");
        try
        {
            Http.EnsureSuccess(response);
        }
        catch (UserException e)
        {
            throw Errors.Context($"looking up {code} on encoredecks", e);
        }
        var json = await Http.ReadJsonAsync(response);
        return json is JsonObject obj && obj["_id"] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
    }

    /// <summary>
    /// Maps deck codes to Encore Decks card ids (one per copy, in deck order), trying the other
    /// spellings of each code (JP/EN, foil or not). Returns the ids and the codes not found.
    ///
    /// <paramref name="lookup"/> returns the id of an exact code; it is a parameter so tests can
    /// replace the site with a fake.
    /// </summary>
    public static async Task<(List<string> Ids, List<string> Missing)> ResolveCardIdsAsync(
        IEnumerable<string> codes, Func<string, Task<string?>> lookup)
    {
        // Each code is looked up once, however many copies the deck has.
        var known = new Dictionary<string, string?>(StringComparer.Ordinal);
        var ids = new List<string>();
        var missing = new List<string>();
        foreach (var code in codes.Select(c => c.Trim()).Where(c => c.Length > 0))
        {
            if (!known.TryGetValue(code, out var found))
            {
                foreach (var spelling in CardCodes.EncoreSpellings(code))
                {
                    found = await lookup(spelling);
                    if (found is not null) break;
                }
                if (found is null) missing.Add(code);
                known[code] = found;
            }
            if (found is not null) ids.Add(found);
        }
        return (ids, missing);
    }

    /// <summary>
    /// Checked before sending: an uploaded deck is public and searchable, so it must be the exact
    /// deck, never one with cards left out.
    /// </summary>
    public static void CheckAllFound(IReadOnlyList<string> ids, IReadOnlyList<string> missing)
    {
        if (missing.Count > 0)
            throw new UserException($"{missing.Count} code(s) are not on Encore Decks: {string.Join(", ", missing)}. Nothing was uploaded.");
        if (ids.Count != DeckList.DeckSize)
            throw new UserException($"only {ids.Count} of {DeckList.DeckSize} cards were found on Encore Decks. Nothing was uploaded.");
    }

    /// <summary>The body Encore Decks' deck builder posts to save a new deck.</summary>
    public static JsonObject SaveRequest(string name, IEnumerable<string> cardIds) => new()
    {
        ["name"] = name.Trim(),
        ["description"] = "",
        ["private"] = false,
        ["attribute-group"] = new JsonArray(),
        ["cards"] = new JsonArray(cardIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray()),
    };

    /// <summary><c>{"deck": {"deckid": "abc", ...}}</c> -> <c>https://www.encoredecks.com/deck/abc</c>.</summary>
    public static string DeckUrlFromResponse(JsonNode json)
    {
        if (json is JsonObject { } obj && obj["deck"] is JsonObject deck && deck["deckid"] is JsonValue v
            && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() is { Length: > 0 } id)
            return Site.DeckPage + id;
        var message = json is JsonObject o && o["message"] is JsonValue m && m.GetValueKind() == JsonValueKind.String ? m.GetValue<string>() : "";
        throw new UserException($"encoredecks did not return the new deck {message}");
    }
}
