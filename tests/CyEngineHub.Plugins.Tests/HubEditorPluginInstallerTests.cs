using System.Text.Json;
using System.Text.RegularExpressions;
using CyEngineHub.Core.Processes;
using CyEngineHub.Plugins.Installation;

namespace CyEngineHub.Plugins.Tests;

public sealed class HubEditorPluginInstallerTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "CyHubEditorInstallerTest-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(Root, "SourcePlugin");
        public string Engine => Path.Combine(Root, "EngineInstall");
        public string Work => Path.Combine(Root, "Builds");
        public string Destination => Path.Combine(Engine, "Engine/Plugins/Marketplace/CyEngineHub");
        public Fixture(int major = 5, int minor = 5)
        {
            Directory.CreateDirectory(Path.Combine(Source, "Source/CyEngineHub"));
            File.WriteAllText(Path.Combine(Source, "CyEngineHub.uplugin"), "{\"EnabledByDefault\":true,\"Modules\":[{\"Name\":\"CyEngineHub\",\"Type\":\"Editor\"}]}");
            File.WriteAllText(Path.Combine(Source, "Source/CyEngineHub/CyEngineHub.Build.cs"), "fixture source");
            Directory.CreateDirectory(Path.Combine(Engine, "Engine/Build"));
            File.WriteAllText(Path.Combine(Engine, "Engine/Build/Build.version"), JsonSerializer.Serialize(new { MajorVersion = major, MinorVersion = minor }));
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class Runner(bool success = true, bool binaries = true) : IProcessRunner
    {
        public int Calls { get; private set; }
        public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            var project = Regex.Match(request.RawArguments!, "-Project=([^\"]+)").Groups[1].Value;
            var root = Path.Combine(Path.GetDirectoryName(project)!, "Plugins/CyEngineHub/Binaries/Win64");
            if (binaries)
            {
                Directory.CreateDirectory(root);
                var editor = request.RawArguments!.Contains(" UE4Editor ", StringComparison.Ordinal) ? "UE4Editor" : "UnrealEditor";
                File.WriteAllText(Path.Combine(root, editor + "-CyEngineHub.dll"), "fixture binary");
                File.WriteAllText(Path.Combine(root, editor + ".modules"), "{}");
            }
            return Task.FromResult(new ProcessResult(1, success ? 0 : 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []));
        }
    }
    [Theory]
    [InlineData(4, 27)]
    [InlineData(5, 0)]
    [InlineData(5, 8)]
    public async Task PublishesOnlyPluginAndCleansItsBuild(int major, int minor)
    {
        using var fixture = new Fixture(major, minor); var runner = new Runner();
        var destination = await new HubEditorPluginInstaller(runner).InstallAsync(fixture.Source, fixture.Engine, fixture.Work, null, CancellationToken.None);
        Assert.Equal(fixture.Destination, destination);
        Assert.True(File.Exists(Path.Combine(destination, "CyEngineHub.uplugin")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Work));
        Assert.False(Directory.Exists(Path.Combine(destination, "Intermediate")));
        Assert.Equal(1, runner.Calls);
    }
    [Fact] public async Task ExistingPluginNeverReplaced()
    {
        using var fixture = new Fixture(); var runner = new Runner();
        Directory.CreateDirectory(fixture.Destination);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HubEditorPluginInstaller(runner).InstallAsync(fixture.Source, fixture.Engine, fixture.Work, null, CancellationToken.None));
        Assert.Equal(0, runner.Calls);
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task FailureOrMissingBinaryLeavesEngineUntouchedAndCleansBuild(bool success, bool binaries)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HubEditorPluginInstaller(new Runner(success, binaries)).InstallAsync(fixture.Source, fixture.Engine, fixture.Work, null, CancellationToken.None));
        Assert.False(Directory.Exists(fixture.Destination));
        Assert.Empty(Directory.EnumerateFileSystemEntries(fixture.Work));
    }
    [Theory]
    [InlineData(4, 26)]
    [InlineData(5, 9)]
    [InlineData(6, 0)]
    public async Task UnsupportedEnginesRejectedBeforeBuild(int major, int minor)
    {
        using var fixture = new Fixture(major, minor); var runner = new Runner();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HubEditorPluginInstaller(runner).InstallAsync(fixture.Source, fixture.Engine, fixture.Work, null, CancellationToken.None));
        Assert.Equal(0, runner.Calls);
    }
}
