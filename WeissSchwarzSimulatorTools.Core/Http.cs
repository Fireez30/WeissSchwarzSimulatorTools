// The HTTP clients the program uses: one that follows redirects (public pages, downloads), and
// one that never does, for requests that carry the Encore Decks session cookie, so the cookie
// can only reach encoredecks.com.

using System.Net;
using System.Text.Json.Nodes;

namespace WeissSchwarzSimulatorTools;

public static class Http
{
    private static readonly string UserAgent = $"WeissSchwarzSimulatorTools/{typeof(Http).Assembly.GetName().Version?.ToString(3)}";

    /// <summary>Follows redirects; never sends or keeps cookies.</summary>
    public static readonly HttpClient Client = Create(allowRedirects: true);

    /// <summary>Never follows redirects: a 3xx answer is returned as it is.</summary>
    public static readonly HttpClient NoRedirect = Create(allowRedirects: false);

    private static HttpClient Create(bool allowRedirects)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = allowRedirects,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    /// <summary>Throws a <see cref="UserException"/> when the server did not answer with a success status.</summary>
    public static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new UserException($"the server answered {(int)response.StatusCode} {response.ReasonPhrase}");
    }

    /// <summary>Reads the body of a response as JSON.</summary>
    public static async Task<JsonNode> ReadJsonAsync(HttpResponseMessage response)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync();
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            throw Errors.Context("reading the encoredecks response", e);
        }
        try
        {
            return JsonNode.Parse(body) ?? throw new UserException("encoredecks returned invalid JSON");
        }
        catch (System.Text.Json.JsonException e)
        {
            throw Errors.Context("encoredecks returned invalid JSON", e);
        }
    }

    /// <summary>Downloads <paramref name="url"/>, refusing bodies larger than <paramref name="limit"/> bytes.</summary>
    public static async Task<byte[]> GetBytesAsync(string url, long limit)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        EnsureSuccess(response);
        if (response.Content.Headers.ContentLength > limit)
            throw new UserException($"the file is larger than {limit / 1024 / 1024} MB");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > limit)
                throw new UserException($"the file is larger than {limit / 1024 / 1024} MB");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }
}
