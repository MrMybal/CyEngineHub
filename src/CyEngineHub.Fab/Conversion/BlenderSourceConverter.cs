using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Fab.Conversion;

public sealed class BlenderSourceConverter(IProcessRunner runner, string scriptPath) : IAssetSourceConverter
{
    public const string ConverterId = "blender.interchange";
    public AssetSourceConverterDescriptor Descriptor { get; } = new(ConverterId, "Blender · Sources → GLB / FBX · Alpha",
        "Convertit les scènes Blender, USD et formats d’échange dans une copie. Préserve les originaux ; les pertes non représentables demandent un accord explicite.",
        [".blend", ".usd", ".usda", ".usdc", ".usdz", ".fbx", ".obj", ".gltf", ".glb"],
        [
            new("blenderExecutable", "Exécutable Blender", "Blender installé localement. Vide : détection des installations standard. Aucun téléchargement d’outil.", FabInstallationOptionKind.Text, "", []),
            new("outputFormat", "Format de sortie", "GLB conserve les matériaux PBR et embarque les textures. FBX sert aux importeurs plus anciens, avec un sous-ensemble de matériaux.", FabInstallationOptionKind.Choice, "GLB", ["GLB", "FBX"]),
            new("inputSelection", "Fichiers du pack", "Tous conserve chaque fichier source détecté. Les autres choix excluent explicitement certains formats pour éviter des exports doublons.", FabInstallationOptionKind.Choice, "all", ["all", "blendOnly", "interchangeOnly"]),
            new("animations", "Exporter les animations", "Inclut armatures, animations et morphs pris en charge par l’exporteur installé.", FabInstallationOptionKind.Boolean, "true", []),
            new("allowLossy", "Autoriser les pertes signalées", "Autorise les limites signalées (shaders procéduraux, matériaux FBX, contraintes). Un rapport est conservé ; aucune équivalence visuelle universelle.", FabInstallationOptionKind.Boolean, "false", []),
        ]);

    public async Task<AssetSourceConversionResult> ConvertAsync(AssetSourceConversionRequest request,
        IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.SourceDirectory));
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        var script = Path.GetFullPath(scriptPath);
        if (!File.Exists(script)) throw new FileNotFoundException("Plugin de conversion Blender absent.", script);
        var executable = ResolveExecutable(request.Options.GetValueOrDefault("blenderExecutable", ""));
        var format = request.Options.GetValueOrDefault("outputFormat", "GLB");
        if (format is not ("GLB" or "FBX")) throw new ArgumentException("Format de sortie invalide.");
        var work = Path.Combine(Path.GetFullPath(request.WorkRoot), Guid.NewGuid().ToString("N"));
        if (work.Equals(source, StringComparison.OrdinalIgnoreCase) || work.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le travail de conversion doit être séparé des sources.");
        Directory.CreateDirectory(work);
        var content = Path.Combine(work, "Content"); Directory.CreateDirectory(content);
        var report = Path.Combine(content, "conversion-report.json");
        var config = Path.Combine(work, "request.json");
        var completed = false;
        try
        {
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new
            {
                source, destination = content, report, format,
                inputSelection = request.Options.GetValueOrDefault("inputSelection", "all"),
                animations = ReadBoolean(request.Options, "animations", true),
                allowLossy = ReadBoolean(request.Options, "allowLossy", false)
            }), cancellationToken).ConfigureAwait(false);
            var result = await runner.RunAsync(new ProcessRequest
            {
                FileName = executable,
                Arguments = ["--background", "--factory-startup", "--disable-autoexec", "--python-exit-code", "17", "--python", script, "--", config],
                WorkingDirectory = work, MaxCapturedLines = 2_000
            }, new OutputProgress(progress), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                var diagnostic = File.Exists(report) ? await File.ReadAllTextAsync(report, cancellationToken).ConfigureAwait(false) : string.Join(" · ", result.CapturedOutput.TakeLast(8).Select(line => line.Text));
                throw new InvalidOperationException("Conversion Blender échouée ; originaux conservés. " + diagnostic[..Math.Min(diagnostic.Length, 4_000)]);
            }
            if (!File.Exists(report) || new FileInfo(report).Length > 1024 * 1024) throw new InvalidDataException("Rapport de conversion absent/invalide.");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report, cancellationToken).ConfigureAwait(false));
            if (!json.RootElement.GetProperty("success").GetBoolean() || json.RootElement.GetProperty("outputs").GetArrayLength() == 0)
                throw new InvalidDataException("Aucun modèle converti validé.");
            foreach (var output in json.RootElement.GetProperty("outputs").EnumerateArray())
            {
                var path = Path.GetFullPath(Path.Combine(content, output.GetString()!));
                if (!path.StartsWith(content + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new InvalidDataException("Sortie de conversion invalide.");
            }
            completed = true;
            return new(ConverterId, content, report, LibraryPackageContentKind.RawAsset);
        }
        finally { if (!completed && Directory.Exists(work)) Directory.Delete(work, recursive: true); }
    }

    public static string ResolveExecutable(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var path = Path.GetFullPath(configured.Trim().Trim('"'));
            if (!File.Exists(path)) throw new FileNotFoundException("Exécutable Blender absent.", path);
            return path;
        }
        var roots = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Blender Foundation"), "/Applications", "/usr/bin" };
        foreach (var root in roots.Where(Directory.Exists))
        {
            var candidates = OperatingSystem.IsWindows()
                ? Directory.EnumerateDirectories(root, "Blender*").OrderByDescending(value => value, StringComparer.OrdinalIgnoreCase).Select(path => Path.Combine(path, "blender.exe"))
                : new[] { "/usr/bin/blender", "/Applications/Blender.app/Contents/MacOS/Blender" };
            var found = candidates.FirstOrDefault(File.Exists); if (found is not null) return found;
        }
        throw new FileNotFoundException("Installez Blender ou renseignez son exécutable dans les options du convertisseur.");
    }
    private static bool ReadBoolean(IReadOnlyDictionary<string, string> options, string key, bool fallback) =>
        !options.TryGetValue(key, out var value) ? fallback : bool.TryParse(value, out var parsed) ? parsed : throw new ArgumentException("Option booléenne invalide : " + key);
    private sealed class OutputProgress(IProgress<FabInstallationScriptProgress>? progress) : IProgress<ProcessOutputLine>
    { public void Report(ProcessOutputLine value) => progress?.Report(new(null, value.Text)); }
}
