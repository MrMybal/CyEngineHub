using CyEngineHub.Desktop.Services;

namespace CyEngineHub.Core.Tests;

public sealed class SafeFolderSelectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2208", Justification = "Reproduce the native Avalonia crash with its original parameter name.")]
    public async Task NativePickerExceptionIsReportedWithoutChangingTheTarget(bool asynchronous)
    {
        var target = "original";
        string? error = null;
        Task<string?> Pick()
        {
            var exception = new ArgumentException("Directory must exist", "fileSystemInfo");
            if (asynchronous) return Task.FromException<string?>(exception);
            throw exception;
        }
        await SafeFolderSelection.SelectAsync(Pick, path => target = path, message => error = message);
        Assert.Equal("original", target);
        Assert.Contains("ArgumentException", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("empty")]
    [InlineData("missing")]
    [InlineData("removed")]
    [InlineData("valid")]
    [InlineData("apply-error")]
    public async Task HandlesCancellationMissingAndValidFolders(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "CyHubPickerQa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var target = "original";
        string? error = null;
        try
        {
            Task<string?> Pick()
            {
                if (scenario == "cancel") return Task.FromCanceled<string?>(new CancellationToken(true));
                if (scenario == "empty") return Task.FromResult<string?>(null);
                if (scenario == "removed") Directory.Delete(root);
                return Task.FromResult<string?>(scenario == "missing" ? Path.Combine(root, "missing") : root);
            }
            await SafeFolderSelection.SelectAsync(Pick, path =>
            {
                if (scenario == "apply-error") throw new IOException("fixture");
                target = path;
            }, message => error = message);
            Assert.Equal(scenario == "valid" ? root : "original", target);
            Assert.Equal(scenario is "missing" or "removed" or "apply-error", error is not null);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root); }
    }
}
