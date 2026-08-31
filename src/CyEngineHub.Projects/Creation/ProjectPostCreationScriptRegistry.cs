using System.Text.Json;
using System.Globalization;
using CyEngineHub.Core.Processes;
using CyEngineHub.Engines;

namespace CyEngineHub.Projects.Creation;

public sealed class ProjectPostCreationScriptRegistry(
    IProcessRunner processRunner,
    IEnumerable<IProjectPostCreationScript>? builtInScripts = null)
    : IProjectPostCreationScriptRegistry
{
    private readonly IProjectPostCreationScript[] _builtIn =
        (builtInScripts ?? [new UnrealCreatePluginPostCreationScript()]).ToArray();
    private readonly List<IProjectPostCreationScript> _external = [];

    public IReadOnlyList<IProjectPostCreationScript> Scripts => _builtIn
        .Concat(_external)
        .OrderByDescending(script => script.Descriptor.IsBuiltIn)
        .ThenBy(script => script.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IProjectPostCreationScript? Find(string id) => Scripts.FirstOrDefault(script =>
        string.Equals(script.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase));

    public async Task<int> RefreshExternalAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(root);
        await EnsureDocumentationAsync(root, cancellationToken).ConfigureAwait(false);
        _external.Clear();
        var ids = _builtIn.Select(script => script.Descriptor.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(root).Take(200))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsReparsePoint(directory)) continue;
            var manifestPath = Path.Combine(directory, "project-post-script.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath, cancellationToken)
                    .ConfigureAwait(false);
                var manifest = JsonSerializer.Deserialize<ExternalProjectPostCreationScriptManifest>(
                    json,
                    JsonOptions) ?? throw new InvalidDataException("Manifest vide.");
                var script = ExternalProjectPostCreationScript.Create(
                    directory,
                    manifest,
                    processRunner);
                if (ids.Add(script.Descriptor.Id)) _external.Add(script);
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Un script utilisateur invalide ne doit pas empêcher la création de projets.
            }
        }
        return _external.Count;
    }

    private static async Task EnsureDocumentationAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var readme = Path.Combine(root, "README.txt");
        if (!File.Exists(readme))
        {
            await File.WriteAllTextAsync(
                readme,
                """
                SCRIPTS POST-CRÉATION CYENGINEHUB

                Créez un sous-dossier par script avec un fichier project-post-script.json.
                L’exécutable ou le script PowerShell doit rester dans ce dossier.

                Variables disponibles :
                {ProjectRoot} {ProjectFile} {ProjectName} {EngineAssociation} {EngineRoot}
                {Config:identifiantOption}

                CyEngineHub exécute ces scripts après la création de la structure du projet et
                avant sa publication définitive. Un échec annule la création sans laisser un
                projet partiellement construit.
                """,
                cancellationToken).ConfigureAwait(false);
        }

        var example = Path.Combine(root, "project-post-script.example.json");
        if (!File.Exists(example))
        {
            await File.WriteAllTextAsync(
                example,
                """
                {
                  "schemaVersion": 1,
                  "id": "monstudio.unreal.post-create",
                  "displayName": "Initialiser mon projet",
                  "description": "Applique les conventions du studio.",
                  "publisher": "Mon studio",
                  "executable": "tools/setup.ps1",
                  "arguments": [
                    "-Project", "{ProjectFile}",
                    "-Profile", "{Config:profile}"
                  ],
                  "supportedEngines": ["Unreal"],
                  "options": [
                    {
                      "id": "profile",
                      "displayName": "Profil",
                      "description": "Convention à appliquer.",
                      "kind": "Choice",
                      "defaultValue": "standard",
                      "choices": ["standard", "vr"],
                      "isRequired": true
                    }
                  ]
                }
                """,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}

public sealed record ExternalProjectPostCreationScriptManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Publisher { get; init; } = "Utilisateur";
    public string Executable { get; init; } = string.Empty;
    public IReadOnlyList<string> Arguments { get; init; } = [];
    public IReadOnlyList<string> SupportedEngines { get; init; } = [];
    public IReadOnlyList<ExternalProjectPostCreationOptionManifest> Options { get; init; } = [];
}

public sealed record ExternalProjectPostCreationOptionManifest
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Kind { get; init; } = "Text";
    public string DefaultValue { get; init; } = string.Empty;
    public IReadOnlyList<string> Choices { get; init; } = [];
    public bool IsRequired { get; init; }
}

internal sealed class ExternalProjectPostCreationScript(
    string scriptRoot,
    string executablePath,
    IReadOnlyList<string> argumentTemplates,
    ProjectPostCreationScriptDescriptor descriptor,
    IProcessRunner processRunner) : IProjectPostCreationScript
{
    public ProjectPostCreationScriptDescriptor Descriptor { get; } = descriptor;

    public static ExternalProjectPostCreationScript Create(
        string scriptRoot,
        ExternalProjectPostCreationScriptManifest manifest,
        IProcessRunner processRunner)
    {
        if (manifest.SchemaVersion != 1 ||
            string.IsNullOrWhiteSpace(manifest.Id) ||
            manifest.Id.Length > 128 ||
            string.IsNullOrWhiteSpace(manifest.DisplayName))
        {
            throw new ArgumentException("Le manifeste du script post-création est invalide.");
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(scriptRoot));
        var executable = Path.GetFullPath(Path.Combine(root, manifest.Executable));
        if (!IsUnderRoot(root, executable) || !File.Exists(executable))
        {
            throw new ArgumentException("L’exécutable doit rester dans le dossier du script.");
        }
        var engines = manifest.SupportedEngines
            .Select(value => Enum.TryParse<EngineFamily>(value, true, out var parsed)
                ? parsed
                : (EngineFamily?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .Distinct()
            .ToArray();
        if (engines.Length == 0)
        {
            throw new ArgumentException("Le script ne déclare aucun moteur valide.");
        }
        var options = manifest.Options
            .Where(option => !string.IsNullOrWhiteSpace(option.Id) && option.Id.Length <= 64)
            .Select(option => new ProjectPostCreationOptionDefinition(
                option.Id.Trim(),
                string.IsNullOrWhiteSpace(option.DisplayName) ? option.Id.Trim() : option.DisplayName.Trim(),
                option.Description.Trim(),
                Enum.TryParse<ProjectPostCreationOptionKind>(option.Kind, true, out var kind)
                    ? kind
                    : ProjectPostCreationOptionKind.Text,
                option.DefaultValue,
                option.Choices,
                option.IsRequired))
            .GroupBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        return new ExternalProjectPostCreationScript(
            root,
            executable,
            manifest.Arguments.Take(128).ToArray(),
            new ProjectPostCreationScriptDescriptor(
                manifest.Id.Trim(),
                manifest.DisplayName.Trim(),
                manifest.Description.Trim(),
                manifest.Publisher.Trim(),
                engines,
                options,
                IsBuiltIn: false),
            processRunner);
    }

    public async Task<ProjectPostCreationScriptResult> ExecuteAsync(
        ProjectPostCreationContext context,
        CancellationToken cancellationToken = default)
    {
        ValidateConfiguration(context.Configuration);
        var executable = executablePath;
        var arguments = argumentTemplates.Select(template => Expand(template, context)).ToList();
        if (Path.GetExtension(executablePath).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            executable = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
            arguments.InsertRange(0, ["-NoProfile", "-NonInteractive", "-File", executablePath]);
        }
        var result = await processRunner.RunAsync(
            new ProcessRequest
            {
                FileName = executable,
                Arguments = arguments,
                WorkingDirectory = scriptRoot,
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Le script {Descriptor.DisplayName} a retourné le code {result.ExitCode}.");
        }
        return new ProjectPostCreationScriptResult(
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
            if (option.Kind == ProjectPostCreationOptionKind.Choice &&
                option.Choices.Count > 0 &&
                !option.Choices.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"La valeur de {option.DisplayName} n’est pas autorisée.");
            }
        }
    }

    private static string Expand(string template, ProjectPostCreationContext context)
    {
        var expanded = template
            .Replace("{ProjectRoot}", context.ProjectRoot, StringComparison.Ordinal)
            .Replace("{ProjectFile}", context.ProjectMarker, StringComparison.Ordinal)
            .Replace("{ProjectName}", context.ProjectName, StringComparison.Ordinal)
            .Replace("{EngineAssociation}", context.EngineAssociation, StringComparison.Ordinal)
            .Replace("{EngineRoot}", context.EngineRoot ?? string.Empty, StringComparison.Ordinal);
        foreach (var pair in context.Configuration)
        {
            expanded = expanded.Replace($"{{Config:{pair.Key}}}", pair.Value, StringComparison.Ordinal);
        }
        return expanded;
    }

    private static bool IsUnderRoot(string root, string candidate) => candidate.StartsWith(
        root + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
