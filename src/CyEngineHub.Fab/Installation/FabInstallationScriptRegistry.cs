using System.Text.Json;
using System.Globalization;
using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Fab.Installation;

public interface IFabInstallationScriptRegistry
{
    IReadOnlyList<IFabInstallationScript> Scripts { get; }

    IReadOnlyList<IFabInstallationScript> GetCompatible(
        LibraryPackageContentKind contentKind,
        FabInstallationTargetKind targetKind);

    IFabInstallationScript? Find(string id);

    void Register(IFabInstallationScript script);

    Task<int> RefreshExternalAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);
}

public sealed class FabInstallationScriptRegistry(
    IEnumerable<IFabInstallationScript> builtInScripts,
    IProcessRunner processRunner) : IFabInstallationScriptRegistry
{
    private readonly List<IFabInstallationScript> _builtIn = [.. builtInScripts];
    private readonly List<IFabInstallationScript> _registered = [];
    private readonly List<IFabInstallationScript> _external = [];

    public IReadOnlyList<IFabInstallationScript> Scripts => _builtIn
        .Concat(_registered)
        .Concat(_external)
        .OrderByDescending(script => script.Descriptor.IsBuiltIn)
        .ThenBy(script => script.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<IFabInstallationScript> GetCompatible(
        LibraryPackageContentKind contentKind,
        FabInstallationTargetKind targetKind) => Scripts
        .Where(script =>
            script.Descriptor.SupportedContentKinds.Contains(contentKind) &&
            script.Descriptor.SupportedTargetKinds.Contains(targetKind))
        .ToArray();

    public IFabInstallationScript? Find(string id) => Scripts.FirstOrDefault(script =>
        string.Equals(script.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase));

    public void Register(IFabInstallationScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (Scripts.Any(existing => string.Equals(
                existing.Descriptor.Id,
                script.Descriptor.Id,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Un script d’installation utilise déjà l’identifiant {script.Descriptor.Id}.");
        }
        _registered.Add(script);
    }

    public async Task<int> RefreshExternalAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(root);
        await EnsureDocumentationAsync(root, cancellationToken).ConfigureAwait(false);
        _external.Clear();
        var reservedIds = _builtIn.Concat(_registered)
            .Select(script => script.Descriptor.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(root)
                     .Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(directory)) continue;
            var manifestPath = Path.Combine(directory, "installation-script.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath, cancellationToken)
                    .ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<ExternalFabInstallationScriptManifest>(
                    json,
                    JsonOptions) ?? throw new InvalidDataException("Manifest vide.");
                var script = ExternalProcessFabInstallationScript.Create(
                    directory,
                    manifest,
                    processRunner);
                if (reservedIds.Add(script.Descriptor.Id))
                {
                    _external.Add(script);
                }
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Un script externe invalide ne doit pas rendre le Hub inutilisable.
            }
        }
        return _external.Count;
    }

    private static async Task EnsureDocumentationAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var readmePath = Path.Combine(root, "README.txt");
        if (!File.Exists(readmePath))
        {
            await File.WriteAllTextAsync(
                    readmePath,
                    """
                    SCRIPTS D’INSTALLATION CYENGINEHUB

                    Créez un sous-dossier par script et placez-y un fichier installation-script.json.
                    L’exécutable ou le fichier .ps1 doit rester dans ce sous-dossier. CyEngineHub ne passe
                    pas les arguments par un shell : chaque entrée de Arguments devient un argument distinct.

                    Variables disponibles :
                    {SourceDirectory} {TargetRoot} {ProjectFile} {EngineRoot} {EngineVersion}
                    {AssetId} {ArtifactId} {Config:identifiantOption}

                    Copiez installation-script.example.json dans un sous-dossier, renommez-le
                    installation-script.json, ajoutez votre outil puis cliquez sur « Actualiser les scripts ».
                    """,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var examplePath = Path.Combine(root, "installation-script.example.json");
        if (!File.Exists(examplePath))
        {
            await File.WriteAllTextAsync(
                    examplePath,
                    """
                    {
                      "schemaVersion": 1,
                      "id": "monstudio.unreal.post-import",
                      "displayName": "Mon post-traitement Unreal",
                      "description": "Exécute un traitement personnalisé après téléchargement.",
                      "publisher": "Mon studio",
                      "executable": "tools/install.ps1",
                      "arguments": [
                        "-Source", "{SourceDirectory}",
                        "-Project", "{ProjectFile}",
                        "-Preset", "{Config:preset}"
                      ],
                      "supportedContentKinds": ["ContentPack", "RawAsset"],
                      "supportedTargetKinds": ["Project"],
                      "options": [
                        {
                          "id": "preset",
                          "displayName": "Preset d’import",
                          "description": "Preset transmis au script.",
                          "kind": "Choice",
                          "defaultValue": "standard",
                          "choices": ["standard", "nanite", "mobile"],
                          "isRequired": true
                        }
                      ]
                    }
                    """,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}

public sealed record ExternalFabInstallationScriptManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Publisher { get; init; } = "Utilisateur";
    public string Executable { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IReadOnlyList<string> SupportedContentKinds { get; init; } = [];
    public IReadOnlyList<string> SupportedTargetKinds { get; init; } = [];
    public IReadOnlyList<ExternalFabInstallationOptionManifest> Options { get; init; } = [];
}

public sealed record ExternalFabInstallationOptionManifest
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Kind { get; init; } = "Text";
    public string DefaultValue { get; init; } = string.Empty;
    public IReadOnlyList<string> Choices { get; init; } = [];
    public bool IsRequired { get; init; }
}

internal sealed class ExternalProcessFabInstallationScript : IFabInstallationScript
{
    private readonly string _scriptRoot;
    private readonly string _executablePath;
    private readonly IReadOnlyList<string> _argumentTemplates;
    private readonly IProcessRunner _processRunner;

    private ExternalProcessFabInstallationScript(
        string scriptRoot,
        string executablePath,
        IReadOnlyList<string> argumentTemplates,
        FabInstallationScriptDescriptor descriptor,
        IProcessRunner processRunner)
    {
        _scriptRoot = scriptRoot;
        _executablePath = executablePath;
        _argumentTemplates = argumentTemplates;
        Descriptor = descriptor;
        _processRunner = processRunner;
    }

    public FabInstallationScriptDescriptor Descriptor { get; }

    public static ExternalProcessFabInstallationScript Create(
        string scriptRoot,
        ExternalFabInstallationScriptManifest manifest,
        IProcessRunner processRunner)
    {
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.Id) ||
            manifest.Id.Length > 128 || manifest.Id.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            throw new ArgumentException("Le manifeste du script d’installation est invalide.");
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scriptRoot));
        var executable = Path.GetFullPath(Path.Combine(root, manifest.Executable));
        if (!IsUnderRoot(root, executable) || !File.Exists(executable))
        {
            throw new ArgumentException("L’exécutable du script doit être un fichier du dossier du script.");
        }

        var contentKinds = manifest.SupportedContentKinds
            .Select(value => Enum.TryParse<LibraryPackageContentKind>(value, true, out var parsed)
                ? parsed
                : (LibraryPackageContentKind?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        var targetKinds = manifest.SupportedTargetKinds
            .Select(value => Enum.TryParse<FabInstallationTargetKind>(value, true, out var parsed)
                ? parsed
                : (FabInstallationTargetKind?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (contentKinds.Length == 0 || targetKinds.Length == 0)
        {
            throw new ArgumentException("Le script ne déclare aucun type de contenu ou de cible valide.");
        }

        var options = manifest.Options.Select(option => new FabInstallationOptionDefinition(
                option.Id.Trim(),
                string.IsNullOrWhiteSpace(option.DisplayName) ? option.Id.Trim() : option.DisplayName.Trim(),
                option.Description.Trim(),
                Enum.TryParse<FabInstallationOptionKind>(option.Kind, true, out var kind)
                    ? kind
                    : FabInstallationOptionKind.Text,
                option.DefaultValue,
                option.Choices,
                option.IsRequired))
            .Where(option => option.Id.Length > 0 && option.Id.Length <= 64 && !option.Id.Any(char.IsControl))
            .GroupBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return new ExternalProcessFabInstallationScript(
            root,
            executable,
            manifest.Arguments.Take(128).ToArray(),
            new FabInstallationScriptDescriptor(
                manifest.Id.Trim(),
                manifest.DisplayName.Trim(),
                manifest.Description.Trim(),
                manifest.Publisher.Trim(),
                contentKinds,
                targetKinds,
                options,
                IsBuiltIn: false),
            processRunner);
    }

    public async Task<FabInstallationScriptResult> InstallAsync(
        FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(context.Configuration);
        var arguments = _argumentTemplates
            .Select(template => Expand(template, context))
            .ToList();
        var executable = _executablePath;
        if (Path.GetExtension(_executablePath).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            executable = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
            arguments.Insert(0, _executablePath);
            arguments.Insert(0, "-File");
            arguments.Insert(0, "-NonInteractive");
            arguments.Insert(0, "-NoProfile");
        }
        var output = new Progress<ProcessOutputLine>(line =>
            progress?.Report(new FabInstallationScriptProgress(null, line.Text)));
        var result = await _processRunner.RunAsync(
                new ProcessRequest
                {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = _scriptRoot,
                },
                output,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Le script {Descriptor.DisplayName} a retourné le code {result.ExitCode}.");
        }
        return new FabInstallationScriptResult(
            Descriptor.Id,
            $"{Descriptor.DisplayName} terminé en {result.Duration.TotalSeconds:0.0} s.",
            new Dictionary<string, string>
            {
                ["exitCode"] = result.ExitCode.ToString(CultureInfo.InvariantCulture),
            });
    }

    private void ValidateConfiguration(IReadOnlyDictionary<string, string> configuration)
    {
        foreach (var option in Descriptor.Options)
        {
            var value = configuration.GetValueOrDefault(option.Id, option.DefaultValue);
            if (option.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Le paramètre {option.DisplayName} est obligatoire.");
            }
            if (option.Kind == FabInstallationOptionKind.Choice &&
                option.Choices.Count > 0 &&
                !option.Choices.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"La valeur de {option.DisplayName} n’est pas autorisée.");
            }
        }
    }

    private static string Expand(string template, FabInstallationScriptContext context)
    {
        var expanded = template
            .Replace("{SourceDirectory}", context.SourceDirectory, StringComparison.Ordinal)
            .Replace("{TargetRoot}", context.Target.RootPath, StringComparison.Ordinal)
            .Replace("{ProjectFile}", context.Target.Project?.FilePath ?? string.Empty, StringComparison.Ordinal)
            .Replace("{EngineRoot}", context.Target.Engine?.RootPath ?? string.Empty, StringComparison.Ordinal)
            .Replace("{EngineVersion}", context.Target.EngineVersion, StringComparison.Ordinal)
            .Replace("{AssetId}", context.Asset.AssetId, StringComparison.Ordinal)
            .Replace("{ArtifactId}", context.Version.ArtifactId, StringComparison.Ordinal);
        foreach (var pair in context.Configuration)
        {
            expanded = expanded.Replace($"{{Config:{pair.Key}}}", pair.Value, StringComparison.Ordinal);
        }
        return expanded;
    }

    private static bool IsUnderRoot(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}
