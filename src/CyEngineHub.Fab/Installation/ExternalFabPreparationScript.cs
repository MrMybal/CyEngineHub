using System.Text.Json;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Fab.Installation;

/// <summary>Opt-in conversion SDK for user-installed tools. Never trusts an arbitrary reported output path.</summary>
internal sealed class ExternalFabPreparationScript(IFabInstallationScript inner, LibraryPackageContentKind outputKind) : IFabPreparationScript
{
    public FabInstallationScriptDescriptor Descriptor => inner.Descriptor;
    public LibraryPackageContentKind OutputContentKind => outputKind;
    public async Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var workspace = Path.Combine(context.SourceDirectory + ".external", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(workspace, "Content");
        var resultFile = Path.Combine(workspace, "result.json");
        Directory.CreateDirectory(output);
        var completed = false;
        try
        {
            var result = await inner.InstallAsync(context with { Configuration = new Dictionary<string, string>(context.Configuration)
                { ["__cyhub_output"] = output, ["__cyhub_result"] = resultFile } }, progress, cancellationToken).ConfigureAwait(false);
            if (!File.Exists(resultFile) || new FileInfo(resultFile).Length > 1024 * 1024 || IsLink(resultFile))
                throw new InvalidDataException("Le convertisseur externe doit écrire le fichier résultat demandé (1 Mio maximum).");
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(resultFile, cancellationToken).ConfigureAwait(false));
            var variant = Path.GetFullPath(Path.Combine(workspace, json.RootElement.GetProperty("variant").GetString() ?? ""));
            if (!variant.Equals(output, StringComparison.OrdinalIgnoreCase) && !variant.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La sortie doit rester dans OutputDirectory, jamais dans le projet ou les originaux.");
            if (!Directory.Exists(variant) || IsLink(variant)) throw new InvalidDataException("Sortie du convertisseur absente/invalide.");
            var files = 0;
            var queue = new Queue<string>(); queue.Enqueue(output);
            while (queue.TryDequeue(out var directory))
            {
                if (IsLink(directory)) throw new InvalidDataException("Lien interdit dans la sortie de conversion.");
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (IsLink(path)) throw new InvalidDataException("Lien interdit dans la sortie de conversion.");
                    if (Directory.Exists(path)) queue.Enqueue(path);
                    else if (++files > 100_000) throw new InvalidDataException("Sortie trop volumineuse.");
                }
            }
            if (files == 0) throw new InvalidDataException("Le convertisseur n’a produit aucun fichier.");
            completed = true;
            return result with { Outputs = new Dictionary<string, string>(result.Outputs)
                { ["variant"] = variant, ["contentKind"] = outputKind.ToString(), ["report"] = resultFile } };
        }
        finally { if (!completed && Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true); }
    }
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
