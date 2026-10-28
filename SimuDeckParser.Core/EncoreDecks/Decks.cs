// Reading decks from encoredecks.com, and importing them into the simulator.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SimuDeckParser.Simulator;

namespace SimuDeckParser.EncoreDecks;

/// <summary>A deck as encoredecks.com serves it: card codes only.</summary>
/// <param name="Date">Already formatted the way the simulator stores it: HH:MM  MM/DD/YYYY.</param>
public sealed record EncoreDeck(string Name, string Date, IReadOnlyList<string> Codes);

/// <summary>One of the linked account's decks, as the deck search lists it (no cards).</summary>
/// <param name="Id">Encore Decks id: the last part of the deck's link.</param>
/// <param name="Date">Already formatted the way the simulator stores it: HH:MM  MM/DD/YYYY.</param>
public sealed record AccountDeck(string Name, string Id, string Date, string Description);

/// <summary>One page of the account's decks; the search API serves 24 decks per page.</summary>
/// <param name="Page">1-based page number.</param>
public sealed record DeckPage(IReadOnlyList<AccountDeck> Decks, int Page, int TotalPages, int TotalDecks);

public static class Decks
{
    /// <summary><c>https://www.encoredecks.com/deck/PPyvcLuvt</c> -> <c>PPyvcLuvt</c>.</summary>
    public static string? DeckIdFromLink(string link)
    {
        link = link.Trim().Split('?', '#')[0].TrimEnd('/');
        // https:, "", www.encoredecks.com, deck, then the id.
        var parts = link.Split('/');
        if (parts.Length <= 4) return null;
        return parts[^1].Length == 0 ? null : parts[^1];
    }

    /// <summary><c>PPyvcLuvt</c> -> <c>https://www.encoredecks.com/deck/PPyvcLuvt</c>: a link <see cref="ImportAsync"/> accepts.</summary>
    public static string DeckLink(string id) => Site.DeckPage + id.Trim();

    /// <summary><c>2025-09-30T22:20:36.123Z</c> -> <c>22:20  09/30/2025</c>.</summary>
    public static string? SimulatorDate(string iso)
    {
        iso = iso.Trim();
        if (iso.Length < 19) return null;
        return DateTime.TryParseExact(iso[..19], "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToString(DeckFiles.DateFormat, CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>The text field <paramref name="key"/> of a JSON object, trimmed; null when it is absent or not text.</summary>
    private static string? TextField(JsonNode? json, string key) =>
        json is JsonObject obj && obj[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String
            ? v.GetValue<string>().Trim()
            : null;

    /// <summary>The first of the date fields <paramref name="keys"/> that is set, in the simulator's date format.</summary>
    private static string DateField(JsonNode json, params string[] keys)
    {
        var raw = keys.Select(k => json is JsonObject o && o[k] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null)
            .FirstOrDefault(v => v is not null);
        return raw is null ? "" : SimulatorDate(raw) ?? "";
    }

    public static EncoreDeck ParseDeckJson(JsonNode json)
    {
        var name = TextField(json, "name") is { Length: > 0 } n ? n : throw new UserException("the deck has no name");
        var date = DateField(json, "datecreated", "datemodified");
        var cards = (json as JsonObject)?["cards"] as JsonArray ?? throw new UserException("the deck has no card list");
        var codes = cards.Select(c => TextField(c, "cardcode")).OfType<string>().ToList();
        if (codes.Count == 0) throw new UserException("the deck is empty");
        return new EncoreDeck(name, date, codes);
    }

    /// <summary>Downloads the deck <paramref name="id"/> (the last part of its link).</summary>
    public static async Task<EncoreDeck> FetchDeckAsync(string id)
    {
        var url = Site.DeckApi + id.Trim();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await Site.SendAsync(Http.Client, request, $"requesting {url}");
        try
        {
            Http.EnsureSuccess(response);
        }
        catch (UserException e)
        {
            throw Errors.Context($"requesting {url}", e);
        }
        return ParseDeckJson(await Http.ReadJsonAsync(response));
    }

    /// <summary>
    /// Downloads a deck and saves it as a new file in <paramref name="decksDir"/>, matched to the
    /// loaded cards. Returns the deck as saved: its name gets a " (2)" suffix when taken.
    /// </summary>
    public static async Task<Deck> ImportAsync(string link, IReadOnlyList<Serie> series, string decksDir)
    {
        var id = DeckIdFromLink(link) ?? throw new UserException($"not an encoredecks deck link: \"{link}\"");
        var fetched = await FetchDeckAsync(id);

        var index = new CardIndex(series);
        var codes = CardCodes.TransformToExistingKeys(index.Codes(), fetched.Codes);
        var saved = DeckFiles.SaveNew(decksDir, fetched.Name, fetched.Date, "", codes);
        return index.ResolveDeck(saved.Name, saved.Date, codes);
    }

    /// <summary>Parses one page of the deck search response.</summary>
    public static DeckPage ParseSearchJson(JsonNode json)
    {
        var decks = (json as JsonObject)?["decks"] as JsonArray ?? throw new UserException("encoredecks returned no deck list");
        var parsed = decks
            .Where(d => d is not null)
            .Select(d => new AccountDeck(
                TextField(d, "name") is { Length: > 0 } n ? n : "(unnamed deck)",
                TextField(d, "deckid") ?? "",
                DateField(d!, "datemodified", "datecreated"),
                TextField(d, "description") ?? ""))
            .Where(d => d.Id.Length > 0)
            .ToList();
        long Number(string key, long fallback) =>
            json[key] is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue<long>(out var n) && n >= 0 ? n : fallback;
        return new DeckPage(parsed, (int)Number("page", 1), (int)Math.Max(1, Number("totalPages", 1)), (int)Number("totalDecks", 0));
    }

    /// <summary>One page (1-based, 24 decks) of the linked account's decks, private and unfinished ones included.</summary>
    public static async Task<DeckPage> FetchAccountDecksAsync(string session, int page)
    {
        // Logged out, the search would silently list public decks instead.
        if (await AccountApi.AccountNameAsync(session) is null)
            throw new SessionExpiredException();
        var url = $"{Site.SearchApi}?page={Math.Max(1, page)}&username=true&invalid=1";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", AccountApi.Cookie(session));
        using var response = await Site.SendAsync(Http.NoRedirect, request, $"requesting {url}");
        try
        {
            Http.EnsureSuccess(response);
        }
        catch (UserException e)
        {
            throw Errors.Context($"requesting {url}", e);
        }
        return ParseSearchJson(await Http.ReadJsonAsync(response));
    }
}
