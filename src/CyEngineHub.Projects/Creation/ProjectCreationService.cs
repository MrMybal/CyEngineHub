using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyEngineHub.Engines;

namespace CyEngineHub.Projects.Creation;

public sealed record ProjectCreationPreset(
    string Id,
    string DisplayName,
    EngineFamily Engine,
    string Description,
    bool IsCodeProject,
    bool UsesLegacyRenderingProfile,
    IReadOnlyList<string> DefaultPlugins);

public sealed record ProjectCreationRequest(
    ProjectCreationPreset Preset,
    string ProjectName,
    string ParentDirectory,
    string? EngineAssociation,
    IReadOnlyList<string> Plugins,
    ProjectConfigurationProfile? ConfigurationProfile = null,
    IReadOnlyList<string>? DisabledPlugins = null,
    IReadOnlyList<string>? StarterCollections = null,
    IReadOnlyList<string>? StarterAssetIds = null,
    IReadOnlyList<ProjectPostCreationScriptSelection>? PostCreationScripts = null,
    string? EngineRoot = null,
    IReadOnlyList<ProjectFeaturePack>? FeaturePacks = null,
    IReadOnlyDictionary<string, string>? IniFileOverrides = null);

public sealed record ProjectCreationResult(
    string ProjectRoot,
    string ProjectMarker,
    ProjectCreationPreset Preset,
    string ReceiptPath = "");

public interface IProjectCreationService
{
    IReadOnlyList<ProjectCreationPreset> Presets { get; }

    IReadOnlyList<ProjectConfigurationProfile> ConfigurationProfiles { get; }

    IReadOnlyList<ProjectPostCreationScriptDescriptor> PostCreationScripts { get; }

    Task<ProjectCreationResult> CreateAsync(
        ProjectCreationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed partial class ProjectCreationService : IProjectCreationService
{
    private const int MaximumPlugins = 2048;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IProjectPostCreationScriptRegistry _postCreationScripts;

    public ProjectCreationService(IProjectPostCreationScriptRegistry? postCreationScripts = null)
    {
        _postCreationScripts = postCreationScripts ?? new BuiltInProjectPostCreationScriptRegistry();
    }

    public IReadOnlyList<ProjectCreationPreset> Presets { get; } =
    [
        new(
            "unreal-cpp",
            "Unreal Engine · C++",
            EngineFamily.Unreal,
            "Projet UE C++ minimal avec cibles Editor/Game et configuration de plugins.",
            IsCodeProject: true,
            UsesLegacyRenderingProfile: false,
            DefaultPlugins: []),
        new(
            "unreal-cpp-ue4-profile",
            "Unreal Engine · C++ · profil UE4",
            EngineFamily.Unreal,
            "Projet UE5 C++ avec Lumen, Nanite et Virtual Shadow Maps désactivés par défaut.",
            IsCodeProject: true,
            UsesLegacyRenderingProfile: true,
            DefaultPlugins: []),
        new(
            "unreal-blueprint",
            "Unreal Engine · Blueprint",
            EngineFamily.Unreal,
            "Projet Blueprint minimal avec configuration de plugins.",
            IsCodeProject: false,
            UsesLegacyRenderingProfile: false,
            DefaultPlugins: []),
        new(
            "unity-empty",
            "Unity · vide",
            EngineFamily.Unity,
            "Structure Unity minimale à ouvrir avec la version configurée.",
            IsCodeProject: false,
            UsesLegacyRenderingProfile: false,
            DefaultPlugins: []),
        new(
            "godot-empty",
            "Godot · vide",
            EngineFamily.Godot,
            "Projet Godot 4 minimal avec scène principale.",
            IsCodeProject: false,
            UsesLegacyRenderingProfile: false,
            DefaultPlugins: []),
        new(
            "cryengine-empty",
            "CryEngine · vide",
            EngineFamily.CryEngine,
            "Descripteur .cryproject minimal sans binaire tiers.",
            IsCodeProject: false,
            UsesLegacyRenderingProfile: false,
            DefaultPlugins: []),
    ];

    public IReadOnlyList<ProjectConfigurationProfile> ConfigurationProfiles { get; } =
    [
        new(
            "unreal-standard",
            "Unreal · Standard",
            "Configuration Unreal par défaut, sans forcer le pipeline de rendu.",
            EngineFamily.Unreal,
            new Dictionary<string, string>(),
            ["EnhancedInput"],
            []),
        new(
            "unreal-no-lumen",
            "Unreal · Sans Lumen",
            "Désactive Lumen GI et les réflexions Lumen, tout en laissant Nanite disponible.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.DynamicGlobalIlluminationMethod"] = "0",
                ["r.ReflectionMethod"] = "0",
                ["r.Lumen.HardwareRayTracing"] = "False",
                ["r.GenerateMeshDistanceFields"] = "False",
            },
            ["EnhancedInput"],
            []),
        new(
            "unreal-no-nanite",
            "Unreal · Sans Nanite",
            "Désactive Nanite sans modifier Lumen ni le reste du pipeline UE5.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.Nanite.ProjectEnabled"] = "False",
            },
            ["EnhancedInput"],
            []),
        new(
            "unreal-no-lumen-nanite",
            "Unreal · Sans Lumen ni Nanite",
            "Pipeline UE5 traditionnel sans Lumen ni Nanite, avec ombres non virtuelles.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.DynamicGlobalIlluminationMethod"] = "0",
                ["r.ReflectionMethod"] = "0",
                ["r.Shadow.Virtual.Enable"] = "0",
                ["r.Nanite.ProjectEnabled"] = "False",
                ["r.GenerateMeshDistanceFields"] = "False",
                ["r.Lumen.HardwareRayTracing"] = "False",
            },
            ["EnhancedInput"],
            []),
        new(
            "unreal-raytracing",
            "Unreal · Ray Tracing",
            "Active le ray tracing matériel et le support du Compute Skin Cache.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.RayTracing"] = "True",
                ["r.SupportComputeSkinCache"] = "True",
                ["r.Lumen.HardwareRayTracing"] = "True",
                ["r.Lumen.HardwareRayTracing.LightingMode"] = "1",
            },
            ["EnhancedInput"],
            []),
        new(
            "unreal-ue5-optimized",
            "Unreal · UE5 Optimisé",
            "Garde Lumen, Nanite, VSM et TSR avec des réglages équilibrés pour une production temps réel.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.DynamicGlobalIlluminationMethod"] = "1",
                ["r.ReflectionMethod"] = "1",
                ["r.Shadow.Virtual.Enable"] = "1",
                ["r.Nanite.ProjectEnabled"] = "True",
                ["r.AntiAliasingMethod"] = "4",
                ["r.Lumen.HardwareRayTracing"] = "False",
                ["r.GenerateMeshDistanceFields"] = "True",
            },
            ["EnhancedInput"],
            []),
        new(
            "unreal-ue5-high-end",
            "Unreal · UE5 High End",
            "Qualité maximale : Lumen matériel, Nanite, VSM, ray tracing et réserve de streaming élevée.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.DynamicGlobalIlluminationMethod"] = "1",
                ["r.ReflectionMethod"] = "1",
                ["r.Shadow.Virtual.Enable"] = "1",
                ["r.Nanite.ProjectEnabled"] = "True",
                ["r.RayTracing"] = "True",
                ["r.SupportComputeSkinCache"] = "True",
                ["r.Lumen.HardwareRayTracing"] = "True",
                ["r.Lumen.HardwareRayTracing.LightingMode"] = "1",
                ["r.Streaming.PoolSize"] = "4096",
            },
            ["EnhancedInput"],
            []),
        new(
            "unreal-forward-vr",
            "Unreal · Forward VR",
            "Forward Shading, MSAA, Instanced Stereo et OpenXR pour un projet VR performant.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.ForwardShading"] = "True",
                ["r.AntiAliasingMethod"] = "3",
                ["vr.InstancedStereo"] = "True",
                ["r.MobileMultiView"] = "True",
                ["r.GenerateMeshDistanceFields"] = "False",
            },
            ["OpenXR", "EnhancedInput"],
            []),
        new(
            "unreal-mobile-vr",
            "Unreal · Mobile / Standalone VR",
            "Forward mobile, HDR coupé, MSAA, Instanced Stereo et Mobile Multi-View.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.ForwardShading"] = "True",
                ["r.Mobile.ShadingPath"] = "0",
                ["r.MobileHDR"] = "False",
                ["r.AntiAliasingMethod"] = "3",
                ["vr.InstancedStereo"] = "True",
                ["r.MobileMultiView"] = "True",
                ["r.Nanite.ProjectEnabled"] = "False",
                ["r.DynamicGlobalIlluminationMethod"] = "0",
            },
            ["OpenXR", "EnhancedInput"],
            []),
        new(
            "unreal-ue5-lite",
            "Unreal · UE5 Lite",
            "Base minimale : plugins moteur désactivés par défaut, outils essentiels uniquement. Ajoutez les fonctionnalités par packs.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.DynamicGlobalIlluminationMethod"] = "0",
                ["r.ReflectionMethod"] = "0",
                ["r.Shadow.Virtual.Enable"] = "0",
                ["r.Nanite.ProjectEnabled"] = "False",
                ["r.GenerateMeshDistanceFields"] = "False",
                ["r.Lumen.HardwareRayTracing"] = "False",
                ["r.RayTracing"] = "False",
            },
            ["EngineAssetDefinitions", "ContentBrowserAssetDataSource", "ContentBrowserFileDataSource", "PluginBrowser", "EnhancedInput"],
            [])
        {
            DisableEnginePluginsByDefault = true,
            IniSettings = NativeAnimationCompressionSettings(),
        },
        new(
            "unreal-ue4-style",
            "Unreal · UE5 mode UE4",
            "UE5 avec rendu proche UE4 : Deferred, TAA, SSR, éclairage statique, sans Lumen, Nanite, VSM ni ray tracing. Ce mode ne convertit pas le moteur en UE4.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.ForwardShading"] = "False",
                ["r.AntiAliasingMethod"] = "2",
                ["r.TemporalAA.Upsampling"] = "False",
                ["r.DynamicGlobalIlluminationMethod"] = "0",
                ["r.ReflectionMethod"] = "2",
                ["r.Shadow.Virtual.Enable"] = "0",
                ["r.Nanite.ProjectEnabled"] = "False",
                ["r.RayTracing"] = "False",
                ["r.Lumen.HardwareRayTracing"] = "False",
                ["r.AllowStaticLighting"] = "True",
                ["r.GenerateMeshDistanceFields"] = "False",
            },
            ["EngineAssetDefinitions", "ContentBrowserAssetDataSource", "ContentBrowserFileDataSource", "PluginBrowser"],
            [])
        {
            DisableEnginePluginsByDefault = true,
            IniSettings = NativeAnimationCompressionSettings(),
        },
    ];

    // UE5 may default to ACL assets. Minimal profiles must not reference a disabled plugin.
    private static ProjectIniSetting[] NativeAnimationCompressionSettings() =>
    [
        new("DefaultEngine.ini", "Animation.DefaultObjectSettings", "BoneCompressionSettings",
            "\"/Engine/Animation/DefaultAnimBoneCompressionSettings\""),
        new("DefaultEngine.ini", "Animation.DefaultObjectSettings", "CurveCompressionSettings",
            "\"/Engine/Animation/DefaultAnimCurveCompressionSettings\""),
    ];

    public IReadOnlyList<ProjectPostCreationScriptDescriptor> PostCreationScripts =>
        _postCreationScripts.Scripts.Select(script => script.Descriptor).ToArray();

    public async Task<ProjectCreationResult> CreateAsync(
        ProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ProjectNameRegex().IsMatch(request.ProjectName))
        {
            throw new ArgumentException(
                "Le nom doit commencer par une lettre et contenir uniquement lettres, chiffres ou underscore.",
                nameof(request));
        }

        if (!Presets.Any(preset => string.Equals(preset.Id, request.Preset.Id, StringComparison.Ordinal)))
        {
            throw new ArgumentException("Le preset de création n’est pas reconnu.", nameof(request));
        }

        var profile = request.ConfigurationProfile ?? ConfigurationProfiles.FirstOrDefault(item =>
            item.Engine == request.Preset.Engine &&
            item.Id.EndsWith("-standard", StringComparison.OrdinalIgnoreCase));
        if (profile is not null &&
            (profile.Engine != request.Preset.Engine ||
             !ProfileIdRegex().IsMatch(profile.Id)))
        {
            throw new ArgumentException("Le profil de configuration n’est pas reconnu pour ce moteur.", nameof(request));
        }

        var manualPlugins = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in request.Plugins.Where(value => !string.IsNullOrWhiteSpace(value)))
            manualPlugins[plugin.Trim()] = true;
        foreach (var plugin in (request.DisabledPlugins ?? []).Where(value => !string.IsNullOrWhiteSpace(value)))
            manualPlugins[plugin.Trim()] = false;
        if (profile is not null)
            profile = ProjectConfigurationComposer.Compose(profile, request.FeaturePacks ?? [], manualPlugins);
        var disabledPlugins = profile?.DisabledPlugins.ToArray() ??
            manualPlugins.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray();
        var plugins = profile?.EnabledPlugins.ToArray() ??
            manualPlugins.Where(pair => pair.Value).Select(pair => pair.Key).ToArray();
        foreach (var file in request.IniFileOverrides ?? new Dictionary<string, string>())
            _ = UnrealProjectIni.Parse(file.Key, file.Value);
        if (plugins.Length + disabledPlugins.Length > MaximumPlugins ||
            plugins.Concat(disabledPlugins).Any(plugin => !PluginNameRegex().IsMatch(plugin)))
        {
            throw new ArgumentException("La liste de plugins contient un identifiant invalide.", nameof(request));
        }

        var parent = Path.GetFullPath(request.ParentDirectory.Trim().Trim('"'));
        if (!Directory.Exists(parent) || IsReparsePoint(parent))
        {
            throw new DirectoryNotFoundException(
                "Le dossier parent doit exister et ne peut pas être un lien symbolique.");
        }

        var projectRoot = CombineContained(parent, request.ProjectName);
        if (Directory.Exists(projectRoot) || File.Exists(projectRoot))
        {
            throw new IOException("Un fichier ou projet du même nom existe déjà.");
        }

        var stagingName = $".cyenginehub-project-{Guid.NewGuid():N}";
        var staging = CombineContained(parent, stagingName);
        Directory.CreateDirectory(staging);
        try
        {
            var marker = request.Preset.Engine switch
            {
                EngineFamily.Unreal => await CreateUnrealAsync(
                    staging,
                    request,
                    plugins,
                    disabledPlugins,
                    profile,
                    cancellationToken).ConfigureAwait(false),
                EngineFamily.Unity => await CreateUnityAsync(
                    staging,
                    request,
                    cancellationToken).ConfigureAwait(false),
                EngineFamily.Godot => await CreateGodotAsync(
                    staging,
                    request,
                    cancellationToken).ConfigureAwait(false),
                EngineFamily.CryEngine => await CreateCryEngineAsync(
                    staging,
                    request,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException("Ce moteur ne fournit pas encore de preset de création."),
            };

            var postCreationResults = await ExecutePostCreationScriptsAsync(
                    staging,
                    marker,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            var metadataDirectory = Directory.CreateDirectory(
                Path.Combine(staging, ".cyenginehub")).FullName;
            var receiptPath = Path.Combine(metadataDirectory, "project-creation.json");
            var receipt = new ProjectCreationReceipt(
                SchemaVersion: 1,
                CreatedAt: DateTimeOffset.UtcNow,
                request.ProjectName,
                request.Preset.Engine.ToString(),
                request.EngineAssociation?.Trim() ?? string.Empty,
                request.Preset.IsCodeProject ? "C++" : "Blueprint",
                request.Preset.Id,
                profile?.Id ?? string.Empty,
                plugins,
                disabledPlugins,
                request.StarterCollections ?? [],
                request.StarterAssetIds ?? [],
                postCreationResults);
            await WriteAsync(
                    receiptPath,
                    JsonSerializer.Serialize(receipt, JsonOptions),
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, projectRoot);
            return new ProjectCreationResult(
                projectRoot,
                Path.Combine(projectRoot, Path.GetRelativePath(staging, marker)),
                request.Preset,
                Path.Combine(projectRoot, Path.GetRelativePath(staging, receiptPath)));
        }
        catch
        {
            if (Directory.Exists(staging) && !IsReparsePoint(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            throw;
        }
    }

    private static async Task<string> CreateUnrealAsync(
        string root,
        ProjectCreationRequest request,
        IReadOnlyList<string> plugins,
        IReadOnlyList<string> disabledPlugins,
        ProjectConfigurationProfile? profile,
        CancellationToken cancellationToken)
    {
        var marker = Path.Combine(root, $"{request.ProjectName}.uproject");
        var descriptor = new
        {
            FileVersion = 3,
            EngineAssociation = request.EngineAssociation?.Trim() ?? string.Empty,
            Category = string.Empty,
            Description = $"Projet créé par CyEngineHub avec le preset {request.Preset.DisplayName}",
            DisableEnginePluginsByDefault = profile?.DisableEnginePluginsByDefault ?? false,
            Modules = request.Preset.IsCodeProject
                ? [new { Name = request.ProjectName, Type = "Runtime", LoadingPhase = "Default" }]
                : Array.Empty<object>(),
            Plugins = plugins.Select(plugin => new { Name = plugin, Enabled = true })
                .Concat(disabledPlugins.Select(plugin => new { Name = plugin, Enabled = false }))
                .ToArray(),
        };
        await WriteAsync(
            marker,
            JsonSerializer.Serialize(descriptor, JsonOptions),
            cancellationToken).ConfigureAwait(false);

        var config = Directory.CreateDirectory(Path.Combine(root, "Config")).FullName;
        var files = new Dictionary<string, string>(UnrealProjectIni.Generate(
            profile, request.ProjectName, request.Preset.UsesLegacyRenderingProfile), StringComparer.OrdinalIgnoreCase);
        foreach (var file in request.IniFileOverrides ?? new Dictionary<string, string>())
        {
            files[UnrealProjectIni.NormalizeFileName(file.Key)] = file.Value;
        }
        foreach (var file in files)
            await WriteAsync(Path.Combine(config, file.Key), file.Value, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(Path.Combine(root, "Content"));
        if (request.Preset.IsCodeProject)
        {
            await CreateUnrealSourceAsync(root, request.ProjectName, cancellationToken).ConfigureAwait(false);
        }

        return marker;
    }

    private static async Task CreateUnrealSourceAsync(
        string root,
        string projectName,
        CancellationToken cancellationToken)
    {
        var source = Directory.CreateDirectory(Path.Combine(root, "Source")).FullName;
        var module = Directory.CreateDirectory(Path.Combine(source, projectName)).FullName;
        await WriteAsync(
            Path.Combine(module, $"{projectName}.Build.cs"),
            $"using UnrealBuildTool;{Environment.NewLine}{Environment.NewLine}public class {projectName} : ModuleRules{Environment.NewLine}{{{Environment.NewLine}    public {projectName}(ReadOnlyTargetRules Target) : base(Target){Environment.NewLine}    {{{Environment.NewLine}        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;{Environment.NewLine}        PublicDependencyModuleNames.AddRange(new[] {{ \"Core\", \"CoreUObject\", \"Engine\", \"InputCore\" }});{Environment.NewLine}    }}{Environment.NewLine}}}{Environment.NewLine}",
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            Path.Combine(source, $"{projectName}.Target.cs"),
            CreateTarget(projectName, editor: false),
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            Path.Combine(source, $"{projectName}Editor.Target.cs"),
            CreateTarget(projectName, editor: true),
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            Path.Combine(module, $"{projectName}.h"),
            $"#pragma once{Environment.NewLine}{Environment.NewLine}#include \"CoreMinimal.h\"{Environment.NewLine}",
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            Path.Combine(module, $"{projectName}.cpp"),
            $"#include \"{projectName}.h\"{Environment.NewLine}#include \"Modules/ModuleManager.h\"{Environment.NewLine}{Environment.NewLine}IMPLEMENT_PRIMARY_GAME_MODULE(FDefaultGameModuleImpl, {projectName}, \"{projectName}\");{Environment.NewLine}",
            cancellationToken).ConfigureAwait(false);
    }

    private static string CreateTarget(string projectName, bool editor)
    {
        var suffix = editor ? "Editor" : string.Empty;
        var targetType = editor ? "Editor" : "Game";
        return
            $"using UnrealBuildTool;{Environment.NewLine}using System.Collections.Generic;{Environment.NewLine}{Environment.NewLine}" +
            $"public class {projectName}{suffix}Target : TargetRules{Environment.NewLine}{{{Environment.NewLine}" +
            $"    public {projectName}{suffix}Target(TargetInfo Target) : base(Target){Environment.NewLine}    {{{Environment.NewLine}" +
            $"        Type = TargetType.{targetType};{Environment.NewLine}        DefaultBuildSettings = BuildSettingsVersion.V5;{Environment.NewLine}" +
            $"        IncludeOrderVersion = EngineIncludeOrderVersion.Latest;{Environment.NewLine}        ExtraModuleNames.Add(\"{projectName}\");{Environment.NewLine}    }}{Environment.NewLine}}}{Environment.NewLine}";
    }

    private static async Task<string> CreateUnityAsync(
        string root,
        ProjectCreationRequest request,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        var settings = Directory.CreateDirectory(Path.Combine(root, "ProjectSettings")).FullName;
        var marker = Path.Combine(settings, "ProjectVersion.txt");
        var version = string.IsNullOrWhiteSpace(request.EngineAssociation)
            ? "6000.0.0f1"
            : request.EngineAssociation.Trim();
        await WriteAsync(marker, $"m_EditorVersion: {version}{Environment.NewLine}", cancellationToken)
            .ConfigureAwait(false);
        return marker;
    }

    private static async Task<string> CreateGodotAsync(
        string root,
        ProjectCreationRequest request,
        CancellationToken cancellationToken)
    {
        var marker = Path.Combine(root, "project.godot");
        await WriteAsync(
            marker,
            $"[application]{Environment.NewLine}config/name=\"{request.ProjectName}\"{Environment.NewLine}run/main_scene=\"res://main.tscn\"{Environment.NewLine}{Environment.NewLine}[display]{Environment.NewLine}window/size/viewport_width=1280{Environment.NewLine}window/size/viewport_height=720{Environment.NewLine}{Environment.NewLine}[rendering]{Environment.NewLine}renderer/rendering_method=\"gl_compatibility\"{Environment.NewLine}",
            cancellationToken).ConfigureAwait(false);
        await WriteAsync(
            Path.Combine(root, "main.tscn"),
            "[gd_scene format=3]" + Environment.NewLine + Environment.NewLine + "[node name=\"Main\" type=\"Node\"]" + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
        return marker;
    }

    private static async Task<string> CreateCryEngineAsync(
        string root,
        ProjectCreationRequest request,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        var marker = Path.Combine(root, $"{request.ProjectName}.cryproject");
        var descriptor = new
        {
            type = "CryProject",
            name = request.ProjectName,
            content = new { assets = new[] { "Assets" } },
        };
        await WriteAsync(
            marker,
            JsonSerializer.Serialize(descriptor, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        return marker;
    }

    private async Task<IReadOnlyList<ProjectPostCreationScriptResult>> ExecutePostCreationScriptsAsync(
        string projectRoot,
        string projectMarker,
        ProjectCreationRequest request,
        CancellationToken cancellationToken)
    {
        var results = new List<ProjectPostCreationScriptResult>();
        foreach (var selection in request.PostCreationScripts ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            var script = _postCreationScripts.Find(selection.ScriptId)
                ?? throw new InvalidOperationException(
                    $"Le script post-création {selection.ScriptId} n’est plus disponible.");
            if (!script.Descriptor.SupportedEngines.Contains(request.Preset.Engine))
            {
                throw new InvalidOperationException(
                    $"Le script {script.Descriptor.DisplayName} ne prend pas en charge {request.Preset.Engine}.");
            }
            ValidatePostCreationConfiguration(script.Descriptor, selection.Configuration);
            results.Add(await script.ExecuteAsync(
                    new ProjectPostCreationContext(
                        projectRoot,
                        projectMarker,
                        request.ProjectName,
                        request.Preset.Engine,
                        request.EngineAssociation?.Trim() ?? string.Empty,
                        request.EngineRoot,
                        selection.Configuration),
                    cancellationToken)
                .ConfigureAwait(false));
        }
        return results;
    }

    private static void ValidatePostCreationConfiguration(
        ProjectPostCreationScriptDescriptor descriptor,
        IReadOnlyDictionary<string, string> configuration)
    {
        foreach (var option in descriptor.Options)
        {
            var value = configuration.GetValueOrDefault(option.Id, option.DefaultValue);
            if (option.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Le paramètre {option.DisplayName} du script {descriptor.DisplayName} est obligatoire.");
            }
            if (option.Kind == ProjectPostCreationOptionKind.Choice &&
                option.Choices.Count > 0 &&
                !option.Choices.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"La valeur de {option.DisplayName} n’est pas autorisée.");
            }
        }
    }

    private static async Task WriteAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            path,
            content,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
    }

    private static string CombineContained(string parent, string name)
    {
        var root = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(root, name));
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Le chemin du projet sort du dossier parent.");
        }

        return combined;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex ProjectNameRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,63}$")]
    private static partial Regex PluginNameRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_.-]{0,127}$")]
    private static partial Regex ProfileIdRegex();
}

internal sealed class BuiltInProjectPostCreationScriptRegistry : IProjectPostCreationScriptRegistry
{
    public IReadOnlyList<IProjectPostCreationScript> Scripts { get; } =
        [new UnrealCreatePluginPostCreationScript()];

    public IProjectPostCreationScript? Find(string id) => Scripts.FirstOrDefault(script =>
        string.Equals(script.Descriptor.Id, id, StringComparison.OrdinalIgnoreCase));

    public Task<int> RefreshExternalAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default) => Task.FromResult(0);
}
