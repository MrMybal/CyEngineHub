using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CyEngineHub.Engines;

namespace CyEngineHub.Projects.Creation;

public sealed partial class UnrealCreatePluginPostCreationScript : IProjectPostCreationScript
{
    public const string ScriptId = "cyenginehub.unreal.create-plugin";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> SupportedModuleTypes =
        new(["Runtime", "Editor", "DeveloperTool"], StringComparer.OrdinalIgnoreCase);

    public ProjectPostCreationScriptDescriptor Descriptor { get; } = new(
        ScriptId,
        "Unreal · Créer un plugin",
        "Crée un plugin dans le projet et l’active dans le fichier .uproject.",
        "CyEngineHub",
        [EngineFamily.Unreal],
        [
            new("pluginName", "Nom technique", "Identifiant C++ du plugin.", ProjectPostCreationOptionKind.Text, "MyPlugin", [], true),
            new("friendlyName", "Nom affiché", "Nom visible dans Unreal Editor.", ProjectPostCreationOptionKind.Text, "My Plugin", []),
            new("createdBy", "Créateur", "Auteur ou studio du plugin.", ProjectPostCreationOptionKind.Text, "", []),
            new("description", "Description", "Description enregistrée dans le .uplugin.", ProjectPostCreationOptionKind.Text, "Plugin créé par CyEngineHub.", []),
            new("template", "Modèle de plugin", "Modèle natif fourni par le moteur sélectionné (Blank, Content Only, Blueprint Library, Editor Mode, fenêtre ou barre d’outils…).", ProjectPostCreationOptionKind.Choice, "Blank", UnrealPluginTemplates.Names, true),
            new("moduleType", "Type du module", "Runtime / Editor / DeveloperTool pour les modèles génériques ; les modèles éditeur imposent Editor. ContentOnly force un plugin sans code.", ProjectPostCreationOptionKind.Choice, "Runtime", ["Runtime", "Editor", "DeveloperTool", "ContentOnly"], true),
            new("canContainContent", "Peut contenir du contenu", "Crée aussi le dossier Content du plugin.", ProjectPostCreationOptionKind.Boolean, "true", []),
        ],
        IsBuiltIn: true);

    public async Task<ProjectPostCreationScriptResult> ExecuteAsync(
        ProjectPostCreationContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Engine != EngineFamily.Unreal)
        {
            throw new InvalidOperationException("Ce script est réservé aux projets Unreal.");
        }

        var pluginName = Get(context, "pluginName", "MyPlugin").Trim();
        if (!PluginNameRegex().IsMatch(pluginName))
        {
            throw new InvalidOperationException(
                "Le nom technique du plugin doit commencer par une lettre et contenir uniquement lettres, chiffres ou underscore.");
        }

        var friendlyName = Get(context, "friendlyName", pluginName).Trim();
        var createdBy = Get(context, "createdBy", string.Empty).Trim();
        var description = Get(context, "description", "Plugin créé par CyEngineHub.").Trim();
        var moduleType = Get(context, "moduleType", "Runtime").Trim();
        var template = Get(context, "template", "Blank").Trim();
        var templateInfo = UnrealPluginTemplates.Find(template);
        var contentOnly = templateInfo.Directory == "ContentOnly" || moduleType.Equals("ContentOnly", StringComparison.OrdinalIgnoreCase);
        if (templateInfo.IsEditor) moduleType = "Editor";
        if (!contentOnly && !SupportedModuleTypes.Contains(moduleType))
        {
            throw new InvalidOperationException("Le type du plugin n’est pas pris en charge.");
        }

        var canContainContent = contentOnly || (bool.TryParse(Get(context, "canContainContent", "true"), out var parsed) && parsed);
        var pluginRoot = Path.Combine(context.ProjectRoot, "Plugins", pluginName);
        if (Directory.Exists(pluginRoot) || File.Exists(pluginRoot))
        {
            throw new IOException($"Le plugin {pluginName} existe déjà dans le projet.");
        }

        Directory.CreateDirectory(pluginRoot);
        if (canContainContent) Directory.CreateDirectory(Path.Combine(pluginRoot, "Content"));
        if (!string.IsNullOrWhiteSpace(context.EngineRoot))
            await UnrealPluginTemplates.CopyAsync(context.EngineRoot, contentOnly ? "Content Only" : template,
                pluginRoot, pluginName, cancellationToken).ConfigureAwait(false);
        else if (templateInfo.Directory is not ("Blank" or "ContentOnly"))
            throw new InvalidOperationException("Sélectionnez un moteur local pour utiliser ses modèles de plugin.");
        else if (!contentOnly)
        {
            await CreateModuleAsync(pluginRoot, pluginName, moduleType, cancellationToken)
                .ConfigureAwait(false);
        }

        var descriptor = new
        {
            FileVersion = 3,
            Version = 1,
            VersionName = "1.0.0",
            FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? pluginName : friendlyName,
            Description = description,
            Category = "Project",
            CreatedBy = createdBy,
            CanContainContent = canContainContent,
            IsBetaVersion = false,
            Installed = false,
            Modules = contentOnly
                ? Array.Empty<object>()
                : [new { Name = pluginName, Type = moduleType, LoadingPhase = "Default" }],
        };
        var pluginMarker = Path.Combine(pluginRoot, $"{pluginName}.uplugin");
        await WriteAsync(
                pluginMarker,
                JsonSerializer.Serialize(descriptor, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
        await EnablePluginInProjectAsync(context.ProjectMarker, pluginName, cancellationToken)
            .ConfigureAwait(false);

        return new ProjectPostCreationScriptResult(
            ScriptId,
            $"Plugin {pluginName} créé et activé.",
            new Dictionary<string, string>
            {
                ["plugin"] = Path.Combine("Plugins", pluginName, $"{pluginName}.uplugin"),
                ["moduleType"] = contentOnly ? "ContentOnly" : moduleType,
                ["template"] = contentOnly ? "Content Only" : template,
            });
    }

    private static async Task CreateModuleAsync(
        string pluginRoot,
        string pluginName,
        string moduleType,
        CancellationToken cancellationToken)
    {
        var moduleRoot = Directory.CreateDirectory(Path.Combine(pluginRoot, "Source", pluginName)).FullName;
        var dependencyModules = moduleType.Equals("Editor", StringComparison.OrdinalIgnoreCase)
            ? "\"Core\", \"CoreUObject\", \"Engine\", \"UnrealEd\""
            : "\"Core\", \"CoreUObject\", \"Engine\"";
        await WriteAsync(
                Path.Combine(moduleRoot, $"{pluginName}.Build.cs"),
                $"using UnrealBuildTool;{Environment.NewLine}{Environment.NewLine}public class {pluginName} : ModuleRules{Environment.NewLine}{{{Environment.NewLine}    public {pluginName}(ReadOnlyTargetRules Target) : base(Target){Environment.NewLine}    {{{Environment.NewLine}        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;{Environment.NewLine}        PublicDependencyModuleNames.AddRange(new[] {{ {dependencyModules} }});{Environment.NewLine}    }}{Environment.NewLine}}}{Environment.NewLine}",
                cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(
                Path.Combine(moduleRoot, $"{pluginName}.h"),
                $"#pragma once{Environment.NewLine}{Environment.NewLine}#include \"Modules/ModuleManager.h\"{Environment.NewLine}{Environment.NewLine}class F{pluginName}Module final : public IModuleInterface{Environment.NewLine}{{{Environment.NewLine}public:{Environment.NewLine}    virtual void StartupModule() override;{Environment.NewLine}    virtual void ShutdownModule() override;{Environment.NewLine}}};{Environment.NewLine}",
                cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(
                Path.Combine(moduleRoot, $"{pluginName}.cpp"),
                $"#include \"{pluginName}.h\"{Environment.NewLine}{Environment.NewLine}void F{pluginName}Module::StartupModule() {{ }}{Environment.NewLine}void F{pluginName}Module::ShutdownModule() {{ }}{Environment.NewLine}{Environment.NewLine}IMPLEMENT_MODULE(F{pluginName}Module, {pluginName}){Environment.NewLine}",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnablePluginInProjectAsync(
        string marker,
        string pluginName,
        CancellationToken cancellationToken)
    {
        var root = JsonNode.Parse(await File.ReadAllTextAsync(marker, cancellationToken).ConfigureAwait(false))
                   as JsonObject ?? throw new InvalidDataException("Le fichier .uproject est invalide.");
        var plugins = root["Plugins"] as JsonArray ?? [];
        root["Plugins"] = plugins;
        var existing = plugins.OfType<JsonObject>().FirstOrDefault(item =>
            string.Equals(item["Name"]?.GetValue<string>(), pluginName, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            plugins.Add(new JsonObject { ["Name"] = pluginName, ["Enabled"] = true });
        }
        else
        {
            existing["Enabled"] = true;
        }
        await WriteAsync(marker, root.ToJsonString(JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private static string Get(ProjectPostCreationContext context, string id, string fallback) =>
        context.Configuration.TryGetValue(id, out var value) ? value : fallback;

    private static Task WriteAsync(string path, string content, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex PluginNameRegex();
}
