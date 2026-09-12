using CyEngineHub.Desktop.Views;

namespace CyEngineHub.Core.Tests;

public sealed class MarketplaceDownloadStorageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cyhub-browser-download-tests-").FullName;

    [Fact]
    public void UsesConfiguredDirectoryInsteadOfBrowserSuggestedDirectory()
    {
        var target = MarketplaceDownloadStorage.PrepareDestination(_root, @"C:\Users\Public\Downloads\SamplePack.zip");
        Assert.Equal("SamplePack.zip", Path.GetFileName(target));
        Assert.Equal(_root, Path.GetDirectoryName(Path.GetDirectoryName(target)));
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void RepeatedNamesNeverOverwriteAnExistingDownload()
    {
        var first = MarketplaceDownloadStorage.PrepareDestination(_root, "pack.zip");
        File.WriteAllText(first, "original archive");
        var second = MarketplaceDownloadStorage.PrepareDestination(_root, "pack.zip");
        Assert.NotEqual(first, second);
        Assert.Equal("original archive", File.ReadAllText(first));
    }

    [Theory]
    [InlineData("../../outside.zip", "outside.zip")]
    [InlineData("file.zip:stream", "file.zip_stream")]
    [InlineData("CON.zip", "_CON.zip")]
    [InlineData("LPT1.zip", "_LPT1.zip")]
    [InlineData("..", "download.bin")]
    [InlineData("", "download.bin")]
    public void UntrustedNamesAreContainedAndSafe(string suggested, string expected)
    {
        var target = MarketplaceDownloadStorage.PrepareDestination(_root, suggested);
        Assert.Equal(expected, Path.GetFileName(target));
        Assert.Equal(_root, Path.GetDirectoryName(Path.GetDirectoryName(target)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/folder")]
    public void RejectsUnsetOrRelativeDestination(string destination) =>
        Assert.Throws<InvalidOperationException>(() => MarketplaceDownloadStorage.PrepareDestination(destination, "pack.zip"));

    [Fact]
    public void UnavailableDestinationFailsInsteadOfFallingBack()
    {
        var file = Path.Combine(_root, "not-a-folder");
        File.WriteAllText(file, "keep me");
        Assert.Throws<IOException>(() => MarketplaceDownloadStorage.PrepareDestination(file, "pack.zip"));
        Assert.Equal("keep me", File.ReadAllText(file));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
