using CyEngineHub.Fab.Conversion;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Fab.Installation;

/// <summary>A conversion-only preparation step; downstream steps receive its real output kind.</summary>
public interface IFabPreparationScript : IFabInstallationScript
{
    LibraryPackageContentKind OutputContentKind { get; }
}

public sealed class BlenderConversionInstallationScript(IAssetSourceConverter converter) : IFabPreparationScript
{
    public const string ScriptId = "convert.blender-interchange";
    public LibraryPackageContentKind OutputContentKind => LibraryPackageContentKind.RawAsset;
    public FabInstallationScriptDescriptor Descriptor { get; } = new(ScriptId, converter.Descriptor.DisplayName,
        converter.Descriptor.Description, "CyEngineHub", [LibraryPackageContentKind.Unknown, LibraryPackageContentKind.RawAsset],
        [FabInstallationTargetKind.Project], converter.Descriptor.Options, true);
    public async Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = await converter.ConvertAsync(new(context.SourceDirectory, context.SourceDirectory + ".converted", context.Configuration), progress, cancellationToken).ConfigureAwait(false);
        return new(Descriptor.Id, "Sources converties ; projet inchangé.", new Dictionary<string, string>
        { ["variant"] = result.Directory, ["report"] = result.ReportPath, ["contentKind"] = result.ContentKind.ToString(), ["sourceConverter"] = result.ConverterId });
    }
}
