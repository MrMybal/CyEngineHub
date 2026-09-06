using System.Text;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class EpicAuthorizationResponseTests
{
    private const string Callback = "https://www.epicgames.com/id/api/redirect?clientId=test-client&responseType=code";
    private const string Code = "test-authorization-code-123";
    private static EpicAuthorizationResponse CreateReader() => new(new Uri(
        "https://www.epicgames.com/id/login?redirectUrl=" + Uri.EscapeDataString(Callback)));

    [Fact]
    public async Task AcceptsOnlyAuthorizationCodeFromExpectedEpicResponse()
    {
        using var body = Body("{\"authorizationCode\":\"" + Code + "\",\"redirectUrl\":null}");
        Assert.Equal(Code, await CreateReader().ReadCodeAsync(new Uri(Callback), 200, body));
    }

    [Theory]
    [InlineData("http://www.epicgames.com/id/api/redirect?clientId=test-client&responseType=code")]
    [InlineData("https://www.epicgames.com.evil.test/id/api/redirect?clientId=test-client&responseType=code")]
    [InlineData("https://evil.test/id/api/redirect?clientId=test-client&responseType=code")]
    [InlineData("https://www.epicgames.com/id/login?clientId=test-client&responseType=code")]
    [InlineData("https://www.epicgames.com/id/api/redirect?clientId=another-client&responseType=code")]
    [InlineData("https://www.epicgames.com/id/api/redirect?clientId=test-client&clientId=another&responseType=code")]
    [InlineData("https://www.epicgames.com/id/api/redirect?clientId=test-client&responseType=token")]
    [InlineData("https://name@www.epicgames.com/id/api/redirect?clientId=test-client&responseType=code")]
    [InlineData("https://www.epicgames.com:8443/id/api/redirect?clientId=test-client&responseType=code")]
    [InlineData("https://www.epicgames.com/id/api/redirect?clientId=test-client&responseType=code#fragment")]
    public async Task RejectsWrongOriginsCallbacksAndClientsWithoutReadingBody(string uri)
    {
        using var body = Body("{\"authorizationCode\":\"" + Code + "\"}");
        Assert.Null(await CreateReader().ReadCodeAsync(new Uri(uri), 200, body));
        Assert.Equal(0, body.Position);
    }

    [Theory]
    [InlineData("<html>not JSON</html>")]
    [InlineData("[]")]
    [InlineData("{\"access_token\":\"not-an-authorization-code\"}")]
    [InlineData("{\"authorizationCode\":12}")]
    [InlineData("{\"authorizationCode\":\"too-short\"}")]
    [InlineData("{\"authorizationCode\":\"an invalid code with spaces\"}")]
    [InlineData("{\"authorizationCode\":\"test-authorization-code-123\",\"authorizationCode\":\"another-code-456789\"}")]
    public async Task RejectsInvalidOrAmbiguousResponses(string json)
    {
        using var body = Body(json);
        Assert.Null(await CreateReader().ReadCodeAsync(new Uri(Callback), 200, body));
    }

    [Fact]
    public async Task RejectsOversizedResponsesAndHttpErrors()
    {
        using var large = Body(new string(' ', EpicAuthorizationResponse.MaximumResponseBytes + 1));
        Assert.Null(await CreateReader().ReadCodeAsync(new Uri(Callback), 200, large));
        using var error = Body("{\"authorizationCode\":\"" + Code + "\"}");
        Assert.Null(await CreateReader().ReadCodeAsync(new Uri(Callback), 403, error));
        Assert.Equal(0, error.Position);
    }

    [Fact]
    public async Task HonorsCancellation()
    {
        using var body = Body("{}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateReader().ReadCodeAsync(new Uri(Callback), 200, body, cancellation.Token));
    }

    [Fact]
    public void RejectsUntrustedLoginConfiguration() => Assert.Throws<ArgumentException>(() =>
        new EpicAuthorizationResponse(new Uri("https://evil.test/login?redirectUrl=" + Uri.EscapeDataString(Callback))));

    [Fact]
    public async Task ReadsPartialComStyleChunksWithoutNonzeroBufferOffsets()
    {
        using var body = new OffsetZeroReadStream(Encoding.UTF8.GetBytes(
            "{\"padding\":\"" + new string('x', 9000) + "\",\"authorizationCode\":\"" + Code + "\"}"));
        Assert.Equal(Code, await CreateReader().ReadCodeAsync(new Uri(Callback), 200, body));
    }

    // WebView2's COMStreamWrapper throws NotImplementedException for offset != 0.
    private sealed class OffsetZeroReadStream(byte[] data) : Stream
    {
        private int _position;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (offset != 0) throw new NotImplementedException("Buffer offsets are unsupported.");
            var length = Math.Min(127, Math.Min(count, data.Length - _position));
            data.AsSpan(_position, length).CopyTo(buffer);
            _position += length;
            return length;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static MemoryStream Body(string text) => new(Encoding.UTF8.GetBytes(text));
}
