using CyEngineHub.Epic.Local;
using CyEngineHub.Epic.Models;

namespace CyEngineHub.Epic.Tests;

public sealed class EpicManifestParserTests
{
    [Fact]
    public async Task ParseAsyncKeepsOnlyAllowlistedInventoryFields()
    {
        using var temporary = new TemporaryDirectory();
        var installation = Directory.CreateDirectory(Path.Combine(temporary.Path, "UE_5.5"));
        var manifest = Path.Combine(temporary.Path, "engine.item");
        await File.WriteAllTextAsync(manifest, $$"""
            {
              "ArtifactId": "engine-artifact",
              "AppName": "UE_5.5",
              "DisplayName": "Unreal Engine 5.5",
              "AppVersionString": "5.5.4",
              "InstallLocation": "{{installation.FullName.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
              "CatalogItemId": "catalog-item",
              "CatalogNamespace": "ue",
              "AccessToken": "must-never-be-imported",
              "Password": "must-never-be-imported"
            }
            """);

        var artifact = await new EpicManifestParser().ParseAsync(manifest);

        Assert.NotNull(artifact);
        Assert.Equal("UE_5.5", artifact.AppName);
        Assert.Equal("Unreal Engine 5.5", artifact.DisplayName);
        Assert.Equal("5.5.4", artifact.Version);
        Assert.Equal(EpicArtifactKind.UnrealEngine, artifact.Kind);
        Assert.True(artifact.IsInstalled);
        var serialized = System.Text.Json.JsonSerializer.Serialize(artifact);
        Assert.DoesNotContain("must-never-be-imported", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParseAsyncRejectsOversizedManifest()
    {
        using var temporary = new TemporaryDirectory();
        var manifest = Path.Combine(temporary.Path, "oversized.item");
        await using (var stream = File.Create(manifest))
        {
            stream.SetLength((2 * 1024 * 1024) + 1);
        }

        var exception = await Assert.ThrowsAsync<EpicOperationException>(
            () => new EpicManifestParser().ParseAsync(manifest));

        Assert.Equal("ParseManifest", exception.Operation);
        Assert.Equal(manifest, exception.Path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CyEngineHub-EpicTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
