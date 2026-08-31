using CyEngineHub.Core.Processes;
using CyEngineHub.Unreal.Acquisition;

namespace CyEngineHub.Unreal.Tests;

public sealed class UnrealSourceDownloadServiceTests
{
    [Fact]
    public void PrepareUsesStructuredNonInteractiveGitClone()
    {
        using var temporary = new TemporaryDirectory();
        var target = Path.Combine(temporary.Path, "UE-Source");
        var service = new UnrealSourceDownloadService(new FakeProcessRunner(succeeds: true));

        var plan = service.Prepare(new UnrealSourceDownloadRequest(
            new Uri("https://github.com/EpicGames/UnrealEngine.git"),
            "release",
            target,
            true));

        Assert.Equal("git", plan.Process.FileName);
        Assert.Contains("clone", plan.Process.Arguments);
        Assert.Contains("--depth", plan.Process.Arguments);
        Assert.Contains("release", plan.Process.Arguments);
        Assert.Contains(target, plan.Process.Arguments);
        Assert.Equal("0", plan.Process.EnvironmentVariables["GIT_TERMINAL_PROMPT"]);
        Assert.Null(plan.Process.RawArguments);
        Assert.Equal("dev.epicgames.com", service.AccessGuideUri.Host);
        Assert.Equal("github.com", service.OfficialRepositoryUri.Host);
    }

    [Theory]
    [InlineData("http://github.com/EpicGames/UnrealEngine.git")]
    [InlineData("https://token@github.com/EpicGames/UnrealEngine.git")]
    [InlineData("https://example.com/EpicGames/UnrealEngine.git")]
    [InlineData("https://github.com/EpicGames/OtherRepository.git")]
    public void PrepareRejectsUnsupportedOrCredentialBearingRepository(string repository)
    {
        using var temporary = new TemporaryDirectory();
        var service = new UnrealSourceDownloadService(new FakeProcessRunner(succeeds: true));

        Assert.Throws<ArgumentException>(() => service.Prepare(new UnrealSourceDownloadRequest(
            new Uri(repository),
            "release",
            Path.Combine(temporary.Path, "UE-Source"),
            false)));
    }

    [Fact]
    public async Task ExecuteValidatesDownloadedEngineAndRetainsFailedClone()
    {
        using var successfulTemporary = new TemporaryDirectory();
        var successfulTarget = Path.Combine(successfulTemporary.Path, "UE-Source");
        var successfulService = new UnrealSourceDownloadService(
            new FakeProcessRunner(succeeds: true));
        var successfulPlan = successfulService.Prepare(new UnrealSourceDownloadRequest(
            new Uri("https://github.com/EpicGames/UnrealEngine.git"),
            "release",
            successfulTarget,
            false));

        var result = await successfulService.ExecuteAsync(successfulPlan);

        Assert.Equal(successfulTarget, result.EngineRoot);
        Assert.True(File.Exists(Path.Combine(successfulTarget, "Setup.bat")));

        using var failedTemporary = new TemporaryDirectory();
        var failedTarget = Path.Combine(failedTemporary.Path, "UE-Source");
        var failedService = new UnrealSourceDownloadService(new FakeProcessRunner(succeeds: false));
        var failedPlan = failedService.Prepare(new UnrealSourceDownloadRequest(
            new Uri("https://github.com/EpicGames/UnrealEngine.git"),
            "release",
            failedTarget,
            true));

        var exception = await Assert.ThrowsAsync<UnrealSourceDownloadException>(
            () => failedService.ExecuteAsync(failedPlan));

        Assert.Equal(128, exception.ExitCode);
        Assert.True(Directory.Exists(failedTarget));
    }

    private sealed class FakeProcessRunner(bool succeeds) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? output = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = request.Arguments[^1];
            Directory.CreateDirectory(target);
            if (succeeds)
            {
                Directory.CreateDirectory(Path.Combine(target, "Engine"));
                File.WriteAllText(Path.Combine(target, "Setup.bat"), "@echo off");
            }

            var started = DateTimeOffset.UtcNow;
            var line = new ProcessOutputLine(
                started,
                succeeds ? ProcessOutputChannel.StandardOutput : ProcessOutputChannel.StandardError,
                succeeds ? "Receiving objects: 100%" : "fatal: authentication failed");
            output?.Report(line);
            return Task.FromResult(new ProcessResult(
                42,
                succeeds ? 0 : 128,
                started,
                started.AddSeconds(1),
                [line]));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CyEngineHub-UnrealSourceTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
