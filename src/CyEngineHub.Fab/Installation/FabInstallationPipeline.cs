namespace CyEngineHub.Fab.Installation;

public sealed record FabInstallationStep(string ScriptId, IReadOnlyDictionary<string, string> Configuration);

/// <summary>Validated ordered steps. Preparation never installs into the project before the final step.</summary>
public sealed class FabInstallationPipeline(IFabInstallationScriptRegistry registry)
{
    public void Validate(FabInstallationScriptContext context, IReadOnlyList<FabInstallationStep> steps)
    {
        if (steps.Count is 0 or > 32) throw new InvalidOperationException("Choisissez entre 1 et 32 étapes.");
        var projectedKind = context.ContentKind;
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            var script = registry.Find(step.ScriptId) ?? throw new InvalidOperationException($"Script absent : {step.ScriptId}.");
            if (!script.Descriptor.SupportedContentKinds.Contains(projectedKind) ||
                !script.Descriptor.SupportedTargetKinds.Contains(context.Target.Kind))
                throw new InvalidOperationException($"Script incompatible : {script.Descriptor.DisplayName}.");
            foreach (var option in script.Descriptor.Options)
            {
                var value = step.Configuration.GetValueOrDefault(option.Id, option.DefaultValue);
                if ((option.IsRequired && string.IsNullOrWhiteSpace(value)) ||
                    (option.Kind == FabInstallationOptionKind.Choice && option.Choices.Count > 0 && !option.Choices.Contains(value)))
                    throw new InvalidOperationException($"Option invalide : {option.DisplayName}.");
            }
            if (step.Configuration.GetValueOrDefault("analyzeOnly") == "true")
                throw new InvalidOperationException("L’analyse seule ne peut pas servir d’étape d’installation.");
            if (index == steps.Count - 1 && step.Configuration.GetValueOrDefault("prepareOnly") == "true")
                throw new InvalidOperationException("La dernière étape doit installer le contenu.");
            if (step.ScriptId == UnrealMegascansInstallationScript.ScriptId && step.Configuration.GetValueOrDefault("importMode") == "raw")
                projectedKind = Packaging.LibraryPackageContentKind.ContentPack;
        }
    }

    public async Task<IReadOnlyList<FabInstallationScriptResult>> ExecuteAsync(FabInstallationScriptContext context,
        IReadOnlyList<FabInstallationStep> steps, IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(context, steps); // No step runs if any descriptor/configuration is invalid.
        List<FabInstallationScriptResult> results = [];
        for (var index = 0; index < steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = steps[index];
            var script = registry.Find(step.ScriptId)!;
            var configuration = new Dictionary<string, string>(step.Configuration);
            var prepares = index < steps.Count - 1 && step.ScriptId is
                UnrealVersionBridgeInstallationScript.ScriptId or UnrealRelocatedContentInstallationScript.ScriptId or UnrealMegascansInstallationScript.ScriptId;
            configuration["prepareOnly"] = prepares ? "true" : "false";
            progress?.Report(new(null, $"Étape {index + 1}/{steps.Count} · {script.Descriptor.DisplayName}"));
            var result = await script.InstallAsync(context with { Configuration = configuration }, progress, cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (prepares)
            {
                if (!result.Outputs.TryGetValue("variant", out var variant)) throw new InvalidDataException("Une étape de préparation n’a pas fourni sa sortie.");
                if (!Directory.Exists(variant)) throw new DirectoryNotFoundException("La sortie de préparation est absente.");
                context = context with { SourceDirectory = variant };
                if (result.Outputs.GetValueOrDefault("contentKind") == "ContentPack") context = context with { ContentKind = Packaging.LibraryPackageContentKind.ContentPack };
                if (result.Outputs.TryGetValue("sourceVersion", out var version))
                    context = context with { Version = context.Version with { EngineVersions = [version] } };
            }
        }
        return results;
    }
}
