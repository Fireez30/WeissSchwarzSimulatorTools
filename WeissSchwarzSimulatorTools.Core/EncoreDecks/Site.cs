// encoredecks.com, the deck-sharing site: importing its decks into the simulator, publishing
// simulator decks on it, and signing in to an account.
//
// - Decks: reading decks (one by its link, or a page of the linked account's decks), and
//   importing them into the simulator's Decks folder.
// - Upload: publishing a simulator deck.
// - AccountApi: signing in, and checking that a saved session is still valid.
// - Credentials: keeping the session in the operating system's credential store.
//
// Security: the password is only ever sent to the login endpoint, over HTTPS, and is never
// stored. Requests carrying the session cookie never follow redirects (Http.NoRedirect), so the
// cookie can only reach encoredecks.com.

namespace WeissSchwarzSimulatorTools.EncoreDecks;

internal static class Site
{
    public const string Root = "https://www.encoredecks.com";
    /// <summary>{DeckApi}{id}: one deck, as JSON.</summary>
    public const string DeckApi = "https://www.encoredecks.com/api/deck/";
    /// <summary>?cardcode=…: one card, as JSON.</summary>
    public const string CardApi = "https://www.encoredecks.com/api/card";
    /// <summary>POST a deck here to publish it.</summary>
    public const string SaveApi = "https://www.encoredecks.com/api/deck";
    /// <summary>{DeckPage}{id}: a deck's page, the link users share.</summary>
    public const string DeckPage = "https://www.encoredecks.com/deck/";
    /// <summary>Deck search: serves 24 decks per page, with page, username and invalid parameters.</summary>
    public const string SearchApi = "https://www.encoredecks.com/api/search/deck";
    public const string LoginApi = "https://www.encoredecks.com/api/login";
    public const string LogoutApi = "https://www.encoredecks.com/api/logout";
    /// <summary>Redirects to /user/&lt;name&gt; when logged in, to /login otherwise.</summary>
    public const string UserPage = "https://www.encoredecks.com/user";

    /// <summary>Sends <paramref name="request"/>, wrapping network errors with <paramref name="context"/>.</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, string context)
    {
        try
        {
            return await client.SendAsync(request);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw Errors.Context(context, e);
        }
    }
}
