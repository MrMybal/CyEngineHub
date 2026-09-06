using System.Text.Json;

namespace CyEngineHub.Fab.Remote;

/// <summary>Accepts only the JSON response of the exact Epic callback opened for this login.</summary>
public sealed class EpicAuthorizationResponse
{
    public const int MaximumResponseBytes = 64 * 1024;
    private readonly Uri _callback;
    private readonly string _clientId;

    public EpicAuthorizationResponse(Uri authorizationUri)
    {
        ArgumentNullException.ThrowIfNull(authorizationUri);
        if (!IsEpicHttps(authorizationUri) || authorizationUri.AbsolutePath != "/id/login" ||
            !TryReadUniqueQuery(authorizationUri, "redirectUrl", out var redirect) ||
            !Uri.TryCreate(redirect, UriKind.Absolute, out var callback) || !IsEpicHttps(callback) ||
            callback.AbsolutePath != "/id/api/redirect" ||
            !TryReadUniqueQuery(callback, "clientId", out var clientId) || string.IsNullOrEmpty(clientId) ||
            !TryReadUniqueQuery(callback, "responseType", out var responseType) || responseType != "code")
        {
            throw new ArgumentException("Adresse d’autorisation Epic non reconnue.", nameof(authorizationUri));
        }

        _callback = callback;
        _clientId = clientId;
    }

    public bool IsExpectedCallback(Uri uri) =>
        IsEpicHttps(uri) && uri.AbsolutePath == _callback.AbsolutePath &&
        TryReadUniqueQuery(uri, "clientId", out var clientId) && clientId == _clientId &&
        TryReadUniqueQuery(uri, "responseType", out var responseType) && responseType == "code";

    public async Task<string?> ReadCodeAsync(Uri responseUri, int statusCode, Stream body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(responseUri);
        ArgumentNullException.ThrowIfNull(body);
        if (statusCode != 200 || !IsExpectedCallback(responseUri)) return null;
        // Bound memory and do not interpret HTML, scripts, cookie stores or arbitrary JSON endpoints.
        var buffer = new byte[MaximumResponseBytes + 1];
        var chunk = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            // WebView2's COM stream adapter only supports reads at buffer offset zero.
            var read = await body.ReadAsync(chunk.AsMemory(0, Math.Min(chunk.Length, buffer.Length - length)), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            chunk.AsSpan(0, read).CopyTo(buffer.AsSpan(length));
            length += read;
        }
        if (length == 0 || length > MaximumResponseBytes) return null;
        return ParseCode(buffer.AsMemory(0, length));
    }

    private static string? ParseCode(ReadOnlyMemory<byte> json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var properties = document.RootElement.EnumerateObject()
                .Where(property => property.Name is "authorizationCode" or "code").ToArray();
            if (properties.Length != 1 || properties[0].Value.ValueKind != JsonValueKind.String) return null;
            var code = properties[0].Value.GetString();
            return code is { Length: >= 16 and <= 4096 } &&
                code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                ? code : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool IsEpicHttps(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps &&
        uri.IdnHost.Equals("www.epicgames.com", StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    private static bool TryReadUniqueQuery(Uri uri, string key, out string? value)
    {
        value = null;
        foreach (var item in uri.Query.TrimStart('?').Split('&'))
        {
            var parts = item.Split('=', 2);
            if (parts.Length != 2 || Uri.UnescapeDataString(parts[0]) != key) continue;
            if (value is not null) return false;
            value = Uri.UnescapeDataString(parts[1]);
        }
        return value is not null;
    }
}
