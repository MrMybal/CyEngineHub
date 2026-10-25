using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CyEngineHub.Core.Ai;
using CyEngineHub.Core.Editor;

namespace CyEngineHub.Core.Tests;

public sealed class HubEditorTests
{
    private sealed class Backend : IHubEditorBackend
    {
        public int Installs { get; private set; }
        public bool FailCatalog { get; init; }
        public Task<HubEditorProject> ResolveProjectAsync(string projectFile, CancellationToken token) =>
            projectFile == "known.uproject" ? Task.FromResult(new HubEditorProject("project:one", "One", "5.5.4")) : throw new InvalidOperationException("Register the project first.");
        public Task<IReadOnlyList<HubAiAsset>> GetAssetsAsync(CancellationToken token) => FailCatalog ? throw new IOException("private authenticated URL") : Task.FromResult<IReadOnlyList<HubAiAsset>>([
            new("trees", "Fab", "Trees", "", ["Nature"], [new("ue55", ["5.5"], ["Windows"]), new("ue57", ["5.7"], ["Windows"])], ["private/path"]),
            ..Enumerable.Range(0, 45).Select(i => new HubAiAsset("asset" + i, "Fab", "Asset " + i, "", [], [], []))]);
        public Task<object> InstallAsync(HubEditorProject project, string assetId, string source, string variantId, string requestId, CancellationToken token)
        { Installs++; return Task.FromResult<object>(new { status = "queued", taskId = Guid.NewGuid() }); }
        public Task<object> GetTasksAsync(HubEditorProject project, CancellationToken token) => Task.FromResult<object>(Array.Empty<object>());
    }
    private static Task<string> Send(HubEditorSession session, string method, string project = "known.uproject", bool confirmed = true, string variant = "ue55", string source = "Fab", string? requestId = null) =>
        session.HandleAsync(JsonSerializer.Serialize(new { protocol = 1, engineVersion = "5.5", projectFile = project, method, confirmed, assetId = "trees", source, variantId = variant, requestId = requestId ?? Guid.NewGuid().ToString("N") }), CancellationToken.None);

    [Fact] public async Task SearchIsBoundedAndDoesNotLeakLocalPaths()
    {
        using var response = JsonDocument.Parse(await Send(new(new Backend()), "search"));
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(46, result.GetProperty("total").GetInt32());
        Assert.Equal(40, result.GetProperty("results").GetArrayLength());
        Assert.Single(result.GetProperty("results")[0].GetProperty("variants").EnumerateArray());
        Assert.DoesNotContain("private/path", response.RootElement.GetRawText());
    }
    [Theory]
    [InlineData("other.uproject", true, "ue55", "Fab")]
    [InlineData("known.uproject", false, "ue55", "Fab")]
    [InlineData("known.uproject", true, "ue57", "Fab")]
    [InlineData("known.uproject", true, "ue55", "Unknown")]
    public async Task InstallRejectsUnknownProjectUnconfirmedWrongVersionOrSource(string project, bool confirm, string variant, string source)
    {
        var backend = new Backend();
        Assert.Contains("\"ok\":false", await Send(new(backend), "install", project, confirm, variant, source));
        Assert.Equal(0, backend.Installs);
    }
    [Fact] public async Task ExplicitCompatibleInstallReachesBackend()
    {
        var backend = new Backend(); Assert.Contains("queued", await Send(new(backend), "install")); Assert.Equal(1, backend.Installs);
    }
    [Fact] public async Task InvalidReplayIdRejected()
    {
        var backend = new Backend(); Assert.Contains("\"ok\":false", await Send(new(backend), "install", requestId: "bad")); Assert.Equal(0, backend.Installs);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"protocol\":2}")]
    public async Task MalformedInputReturnsError(string input) => Assert.Contains("\"ok\":false", await new HubEditorSession(new Backend()).HandleAsync(input, CancellationToken.None));
    [Fact] public async Task ProviderErrorDoesNotLeakAuthenticatedUrl() => Assert.DoesNotContain("private authenticated URL", await Send(new(new Backend { FailCatalog = true }), "search"));
    [Theory]
    [InlineData("4.27", "4.27.2", "Win64", true)]
    [InlineData("5.8", "5.8.4", "Windows", true)]
    [InlineData("5.7", "5.5.4", "Windows", false)]
    [InlineData("5.5", "5.5.4", "Linux", false)]
    public void CompatibilityUsesExactBranchAndPlatform(string from, string to, string platform, bool expected) => Assert.Equal(expected, HubEditorSession.Compatible(new("v", [from], [platform]), to));

    [Fact] public async Task RealPipeRoundTripAndShutdown()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var stop = new CancellationTokenSource();
        var name = "CyEngineHub.Editor.Test." + Guid.NewGuid().ToString("N");
        var server = HubEditorTransport.ServeAsync(new Backend(), stop.Token, name);
        try
        {
            await using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(5000);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync("{\"protocol\":1,\"engineVersion\":\"5.5\",\"method\":\"connect\",\"projectFile\":\"known.uproject\"}");
            Assert.Contains("project:one", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally { stop.Cancel(); try { await server; } catch (OperationCanceledException) { } }
    }
}
