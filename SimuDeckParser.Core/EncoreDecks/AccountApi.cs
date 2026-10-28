// Signing in to encoredecks.com, and checking that a saved session is still valid.
//
// Signing in sends the email and password once and gets back a session cookie, valid for a
// year. Only the session is kept (see Credentials); the password is not stored.

using System.Net;
using System.Text;
using System.Text.Json;

namespace SimuDeckParser.EncoreDecks;

/// <summary>An Encore Decks account linked to the tool: its name and login session.</summary>
public sealed record Account(string Name, string Session)
{
    /// <summary>Printing an account (in logs, in test failures) never shows the session.</summary>
    public override string ToString() => $"Account {{ Name = {Name}, Session = <redacted> }}";
}

/// <summary>
/// The error thrown when the linked account's session is no longer valid: the user must link the
/// account again.
/// </summary>
public sealed class SessionExpiredException()
    : UserException("the Encore Decks session has expired; link your account again in Settings");

public static class AccountApi
{
    private const string SessionCookie = "encoresesssionid";

    /// <summary>The Cookie header value that carries <paramref name="session"/>.</summary>
    internal static string Cookie(string session) => $"{SessionCookie}={session}";

    /// <summary><c>encoresesssionid=s%3Aabc.def; Path=/; HttpOnly</c> -> <c>s%3Aabc.def</c>.</summary>
    public static string? SessionFromSetCookie(string header)
    {
        var first = header.Split(';')[0];
        var eq = first.IndexOf('=');
        if (eq < 0) return null;
        var value = first[(eq + 1)..].Trim();
        return first[..eq].Trim() == SessionCookie && value.Length > 0 ? value : null;
    }

    /// <summary><c>/user/Some%20Name</c> -> <c>Some Name</c>; <c>/login</c> (not logged in) -> null.</summary>
    public static string? NameFromUserRedirect(string location)
    {
        var path = location.Trim();
        while (path.StartsWith(Site.Root, StringComparison.Ordinal))
            path = path[Site.Root.Length..];
        if (!path.StartsWith("/user/", StringComparison.Ordinal)) return null;
        var encoded = path[6..].Split('/', '?', '#')[0];
        return encoded.Length == 0 ? null : TextHelpers.PercentDecode(encoded);
    }

    /// <summary>The account name of a session, or null when the session is not logged in.</summary>
    public static async Task<string?> AccountNameAsync(string session)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Site.UserPage);
        request.Headers.Add("Cookie", Cookie(session));
        using var response = await Site.SendAsync(Http.NoRedirect, request, "checking the Encore Decks session");
        return response.Headers.Location is { } location ? NameFromUserRedirect(location.OriginalString) : null;
    }

    /// <summary>{"username":…,"password":…}, as UTF-8 bytes the caller wipes after sending.</summary>
    internal static byte[] LoginBody(string email, string password)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("username", email.Trim());
            writer.WriteString("password", password);
            writer.WriteEndObject();
        }
        var body = buffer.ToArray();
        // The stream's internal buffer held the password too.
        Array.Clear(buffer.GetBuffer());
        return body;
    }

    /// <summary>
    /// Logs in with the account's email and password, over HTTPS only, and returns the session.
    /// The password is sent once and not kept.
    /// </summary>
    public static async Task<Account> LoginAsync(string email, string password)
    {
        var body = LoginBody(email, password);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Site.LoginApi)
            {
                Content = new ByteArrayContent(body) { Headers = { { "Content-Type", "application/json" } } },
            };
            // Never let a redirect carry the credentials anywhere else.
            using var response = await Site.SendAsync(Http.NoRedirect, request, "signing in to encoredecks");
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    break;
                case HttpStatusCode.Unauthorized:
                    throw new UserException("wrong email or password");
                default:
                    throw new UserException($"encoredecks answered {(int)response.StatusCode} to the sign-in");
            }
            var session = (response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [])
                .Select(SessionFromSetCookie)
                .FirstOrDefault(s => s is not null)
                ?? throw new UserException("encoredecks did not return a session");
            var name = await AccountNameAsync(session)
                ?? throw new UserException("encoredecks did not keep the session open");
            return new Account(name, session);
        }
        finally
        {
            Array.Clear(body);
        }
    }

    /// <summary>Ends the session on encoredecks.com. Best effort: the tool forgets it anyway.</summary>
    public static async Task LogoutAsync(string session)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Site.LogoutApi);
            request.Headers.Add("Cookie", Cookie(session));
            using var response = await Http.NoRedirect.SendAsync(request);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
        }
    }
}
