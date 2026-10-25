using System.Text.Json;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Plugins.Installation;

/// <summary>Builds against the selected engine; publishes only this editor plugin, never an engine binary.</summary>
public sealed class HubEditorPluginInstaller(IProcessRunner runner)
{
    public static bool Supports(int major, int minor) => major == 4 && minor == 27 || major == 5 && minor is >= 0 and <= 8;

    public async Task<string> InstallAsync(string source, string engineRoot, string buildParent,
        IProgress<ProcessOutputLine>? output, CancellationToken token)
    {
        engineRoot = Path.GetFullPath(engineRoot);
        source = Path.GetFullPath(source);
        buildParent = Path.GetFullPath(buildParent);
        EnsurePlainPath(engineRoot); EnsurePlainPath(source); EnsurePlainPath(buildParent);
        using var version = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(engineRoot, "Engine/Build/Build.version"), token).ConfigureAwait(false));
        var major = version.RootElement.GetProperty("MajorVersion").GetInt32();
        var minor = version.RootElement.GetProperty("MinorVersion").GetInt32();
        if (!Supports(major, minor)) throw new InvalidOperationException("Plugin éditeur : Unreal 4.27 à 5.8 uniquement.");
        var destination = Path.Combine(engineRoot, "Engine/Plugins/Marketplace/CyEngineHub");
        EnsurePlainPath(destination);
        if (Directory.Exists(destination)) throw new InvalidOperationException("CyEngineHub est déjà installé dans ce moteur. Fermez Unreal avant toute modification du plugin.");
        var build = Path.Combine(buildParent, Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(Path.GetDirectoryName(destination)!, ".cyenginehub-" + Guid.NewGuid().ToString("N"));
        try
        {
            var plugin = Path.Combine(build, "Plugins/CyEngineHub");
            Directory.CreateDirectory(plugin);
            CopySources(source, plugin, token);
            var project = Path.Combine(build, "CyHubEditorHost.uproject");
            await File.WriteAllTextAsync(project, "{\"FileVersion\":3,\"Plugins\":[{\"Name\":\"CyEngineHub\",\"Enabled\":true}]}", token).ConfigureAwait(false);
            var batch = Path.Combine(engineRoot, "Engine/Build/BatchFiles/Build.bat");
            var target = major == 4 ? "UE4Editor" : "UnrealEditor";
            var result = await runner.RunAsync(new ProcessRequest {
                FileName = "cmd.exe", WorkingDirectory = engineRoot,
                RawArguments = $"/d /v:off /s /c \"\"{batch}\" {target} Win64 Development \"-Project={project}\" \"-plugin={plugin}/CyEngineHub.uplugin\" -NoPCH -NoSharedPCH -OverrideBuildEnvironment -NoUBA -MaxParallelActions=4 -WaitMutex -NoHotReload\"",
                MaxCapturedLines = 500,
            }, output, token).ConfigureAwait(false);
            if (!result.Succeeded) throw new InvalidOperationException("Compilation du plugin échouée. Consultez le journal ; moteur inchangé.");
            var dll = Path.Combine(plugin, "Binaries/Win64", target + "-CyEngineHub.dll");
            var modules = Path.Combine(plugin, "Binaries/Win64", target + ".modules");
            if (!File.Exists(dll) || !File.Exists(modules)) throw new InvalidOperationException("Compilation sans module éditeur : installation refusée.");
            EnsurePlainPath(destination);
            Directory.CreateDirectory(staging);
            CopySources(source, staging, token);
            Directory.CreateDirectory(Path.Combine(staging, "Binaries/Win64"));
            File.Copy(dll, Path.Combine(staging, "Binaries/Win64", Path.GetFileName(dll)));
            File.Copy(modules, Path.Combine(staging, "Binaries/Win64", Path.GetFileName(modules)));
            token.ThrowIfCancellationRequested();
            // Move fails if another installation appeared; never replace an existing plugin.
            Directory.Move(staging, destination);
            return destination;
        }
        finally { DeleteOwned(build, buildParent); DeleteOwned(staging, Path.GetDirectoryName(destination)!); }
    }

    private static void CopySources(string source, string destination, CancellationToken token)
    {
        var files = PlainTree(source).Where(File.Exists).Take(101).ToArray();
        if (files.Length > 100) throw new InvalidOperationException("Plugin source trop volumineux.");
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested(); EnsurePlainPath(file);
            var relative = Path.GetRelativePath(source, file);
            if (relative != "CyEngineHub.uplugin" && !relative.StartsWith("Source" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (new FileInfo(file).Length > 1_048_576) throw new InvalidOperationException("Fichier source trop volumineux.");
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        if (!File.Exists(Path.Combine(destination, "CyEngineHub.uplugin"))) throw new InvalidOperationException("Sources du plugin éditeur absentes.");
    }
    private static void EnsurePlainPath(string path)
    {
        if (path.IndexOfAny(['\"', '%', '!', '&', '|', '<', '>', '^', '\r', '\n']) >= 0) throw new ArgumentException("Chemin non compatible avec les batchs Unreal.");
        for (string? current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Lien de dossier refusé.");
    }
    private static void DeleteOwned(string path, string parent)
    {
        if (!Directory.Exists(path)) return;
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Nettoyage hors du dossier de travail refusé.");
        EnsurePlainPath(path);
        foreach (var _ in PlainTree(path)) { }
        Directory.Delete(path, true);
    }
    private static IEnumerable<string> PlainTree(string root)
    {
        var directories = new Stack<string>(); directories.Push(root); var count = 0;
        while (directories.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++count > 100_000) throw new InvalidOperationException("Arborescence temporaire trop volumineuse.");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Lien dans le temporaire ou les sources refusé.");
                yield return entry;
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(entry);
            }
    }
}
