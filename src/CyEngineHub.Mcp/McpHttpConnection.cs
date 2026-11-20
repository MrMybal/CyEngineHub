using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Text;

internal sealed class McpHttpConnection(Uri endpoint, string? token) : IAsyncDisposable
{
    private const string Protocol = "2025-06-18"; // Matches the CLI initialize request.
    private const string SessionHeader = "Mcp-Session-Id";
    private readonly HttpClient _client = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
    }) { Timeout = Timeout.InfiniteTimeSpan };
    private string? _session;

    public static bool TryGetEndpoint(string address, [NotNullWhen(true)] out Uri? url) =>
        Uri.TryCreate(address, UriKind.Absolute, out url) && url.Scheme == Uri.UriSchemeHttp &&
        url.Host == "127.0.0.1" && url.AbsolutePath == "/mcp" &&
        url.UserInfo.Length == 0 && url.Query.Length == 0 && url.Fragment.Length == 0;

    private HttpRequestMessage Request(HttpMethod method)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (_session is not null)
        {
            request.Headers.Add(SessionHeader, _session);
            request.Headers.Add("MCP-Protocol-Version", Protocol);
        }
        return request;
    }

    public async Task<string?> ExchangeAsync(string line, CancellationToken cancellationToken)
    {
        try
        {
            using var request = Request(HttpMethod.Post);
            request.Content = new StringContent(line, Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (_session is null && response.Headers.TryGetValues(SessionHeader, out var sessions))
                _session = sessions.FirstOrDefault();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new IOException(exception.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "MCP HTTP requires a valid Bearer token."
                : "MCP HTTP unavailable. " + exception.Message, exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_session is null) return;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var request = Request(HttpMethod.Delete);
            using var response = await _client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException) { }
        finally { _client.Dispose(); }
    }
}
