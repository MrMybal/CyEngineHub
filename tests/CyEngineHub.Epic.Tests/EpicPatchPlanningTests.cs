using System.Security.Cryptography;
using System.Text;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

#pragma warning disable CA5350 // Epic distribution integrity protocol.

public sealed class EpicPatchPlanningTests
{
    [Fact]
    public void ExplicitTagsDoNotTurnCoreHelpersIntoEntireOptionalPlatforms()
    {
        var root = Temp();
        try
        {
            var files = new[]
            {
                File("Engine/Source/Core/Public/AndroidHelper.h", "core"),
                File("Engine/Binaries/DotNET/Linux.Automation.dll", "core"),
                File("Engine/Content/Old.uasset", "base"),
                File("Engine/Source/Optional.cpp", "source", "engine_source"),
                File("Engine/Platforms/Android/SDK.bin", "sdk", "platform_Android"),
                File("Engine/Platforms/Linux/SDK.bin", "sdk", "platform_Linux"),
                File("Engine/Binaries/Win64/Editor.pdb", "symbols", "editor_symbols"),
                File("Templates/New/New.uproject", "template", "templates"),
                File("Templates/Old/Old.uproject", "template", "templates"),
                File("Engine/Content/New.uasset", "new base")
            };
            foreach (var file in files.Take(3).Append(files[8])) Write(root, file.RelativePath, Encoding.UTF8.GetBytes("exists"));
            var manifest = new EpicDownloadManifest("UE", "5.8.4", 13, new Dictionary<string, EpicManifestChunk>(), files);
            var selected = OfficialEpicGamesLauncherEngineBackend.SelectPatchFiles(root, manifest, manifest);
            Assert.DoesNotContain(selected, file => file.InstallTags.Contains("platform_Android") || file.InstallTags.Contains("platform_Linux") || file.InstallTags.Contains("engine_source") || file.InstallTags.Contains("editor_symbols"));
            Assert.Contains(selected, file => file.RelativePath == "Templates/New/New.uproject");
            Assert.Contains(selected, file => file.RelativePath == "Engine/Content/New.uasset");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SharedTagsDoNotEnableAnUninstalledSecondComponent()
    {
        var root = Temp();
        try
        {
            var files = new[] { File("Engine/Source/Old.cpp", "old", "engine_source"),
                File("Engine/Source/Android/Shared.cpp", "shared", "engine_source", "platform_Android"),
                File("Engine/Platforms/Android/Only.bin", "android", "platform_Android") };
            Write(root, files[0].RelativePath, [1]); Write(root, files[1].RelativePath, [1]);
            var manifest = new EpicDownloadManifest("UE", "5.8.4", 13, new Dictionary<string, EpicManifestChunk>(), files);
            Assert.DoesNotContain(OfficialEpicGamesLauncherEngineBackend.SelectPatchFiles(root, manifest, manifest), file => file.RelativePath == files[2].RelativePath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task OldManifestMapsVerifiedInstalledRangesIntoNewFiles(bool corrupt, bool uncovered, bool reusable)
    {
        var root = Temp();
        try
        {
            var oldBytes = Encoding.UTF8.GetBytes("original-block");
            var oldFile = File("Engine/Binaries/Old.dll", Encoding.UTF8.GetString(oldBytes)) with { Parts = [new("id", 4, (uint)oldBytes.Length, 0)] };
            Write(root, oldFile.RelativePath, corrupt ? Encoding.UTF8.GetBytes("corrupted-data") : oldBytes);
            var chunk = new EpicManifestChunk("id", 1, 0, 100, new("https://example.invalid/id.chunk"));
            var chunks = new Dictionary<string, EpicManifestChunk> { ["id"] = chunk };
            var old = new EpicDownloadManifest("UE", "5.8.3", 13, chunks, [oldFile]);
            var nextFile = File("Engine/Binaries/New.dll", "block") with { Parts = [new("id", uncovered ? 0U : 13U, 5, 0)], Size = 5 };
            var next = new EpicDownloadManifest("UE", "5.8.4", 13, chunks, [nextFile]);
            var local = await EpicPatchLocalContent.CreateAsync(root, next, old, [nextFile], new HashSet<string>(), null, CancellationToken.None);
            Assert.Equal(reusable, local.Contains("id"));
            if (reusable)
            {
                var bytes = await local.ReadAsync("id", CancellationToken.None);
                Assert.Equal("block", Encoding.UTF8.GetString(bytes!, 13, 5));
                Assert.Equal(oldBytes, await System.IO.File.ReadAllBytesAsync(Path.Combine(root, oldFile.RelativePath)));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void PatchSpaceUsesRemainingFilesAndBoundedCacheNotTheWholeDownload()
    {
        const long mib = 1L << 20;
        Assert.Equal(0, EpicEnginePatchPlan.RequiredSpace(0, 0, 0));
        var first = EpicEnginePatchPlan.RequiredSpace(300 * mib, 64 * mib, 0);
        var resumed = EpicEnginePatchPlan.RequiredSpace(100 * mib, 64 * mib, 64 * mib);
        Assert.InRange(first, 364 * mib, 381 * mib);
        Assert.InRange(resumed, 100 * mib, 117 * mib);
        Assert.Contains("variation finale", new EpicEnginePatchPlan(10, 50 * mib, 250 * mib, first, -20 * mib, 64 * mib).Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void MaliciousLocalManifestPathsCannotEscapeTheEngineRoot()
    {
        var root = Temp();
        try
        {
            var file = File("../../outside", "unsafe", "platform_Android");
            var manifest = new EpicDownloadManifest("UE", "5.8.4", 13, new Dictionary<string, EpicManifestChunk>(), [file]);
            Assert.Throws<InvalidOperationException>(() => OfficialEpicGamesLauncherEngineBackend.SelectPatchFiles(root, manifest, manifest));
        }
        finally { Directory.Delete(root, true); }
    }

    private static EpicManifestFile File(string path, string text, params string[] tags) => new(path, SHA1.HashData(Encoding.UTF8.GetBytes(text)), [], Encoding.UTF8.GetByteCount(text), tags);
    private static string Temp() { var root = Path.Combine(Path.GetTempPath(), "CyHubPatchPlan-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    private static void Write(string root, string path, byte[] bytes) { var full = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); System.IO.File.WriteAllBytes(full, bytes); }
}
