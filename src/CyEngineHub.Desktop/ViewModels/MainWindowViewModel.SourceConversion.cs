using System.Text.Json;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Fab.Conversion;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;
using LocalAsset = CyEngineHub.Core.Marketplaces.MarketplaceLocalAsset;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private AssetSourceConverterRegistry? _sourceConverters;
    public AssetSourceConverterRegistry SourceConverters => _sourceConverters ??= new([
        new BlenderSourceConverter(_processRunner, Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.source-converters", "scripts", "blender-convert.py")),
        new UnrealRawSourceConverter(_processRunner)]);
    public LocalAsset? SourceConversionAsset { get; private set; }
    public bool IsSourceConversionVisible { get; set; }
    public bool IsSourceConversionBusy { get; private set; }
    public string SourceConversionStatus { get; private set; } = "";
    public string SourceConversionConverterId { get; private set; } = BlenderSourceConverter.ConverterId;
    public Dictionary<string, string> SourceConversionOptions { get; } = [];
    public bool SourceConversionToUnreal { get; set; }
    public string? SourceConversionEngineId { get; set; }

    public void SelectSourceConverter(string id)
    {
        var converter = SourceConverters.Find(id) ?? throw new InvalidOperationException("Convertisseur absent.");
        SourceConversionConverterId = id; SourceConversionOptions.Clear();
        foreach (var option in converter.Descriptor.Options) SourceConversionOptions[option.Id] = option.DefaultValue;
    }

    private async Task RequestSourceConversionAsync(LocalAsset asset)
    {
        if (IsSourceConversionBusy) return;
        SourceConversionAsset = asset;
        var extensions = await Task.Run(() => AssetSourceConversionPlanner.ReadPackageExtensions(asset.FilePath)).ConfigureAwait(true);
        var suggested = AssetSourceConversionPlanner.Suggest(extensions);
        SelectSourceConverter(suggested.Contains(BlenderConversionInstallationScript.ScriptId) ? BlenderSourceConverter.ConverterId : UnrealRawSourceConverter.ConverterId);
        SourceConversionToUnreal = false;
        SourceConversionEngineId = Engines.FirstOrDefault()?.Engine.Id;
        SourceConversionStatus = "Formats détectés : " + string.Join(", ", extensions.Order()) + ". Aucun projet modifié. Les limites/pertes doivent être acceptées explicitement.";
        var saved = await _settings.GetAsync("source-conversion." + asset.Id).ConfigureAwait(true);
        if (saved is not null)
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(saved);
            foreach (var pair in values ?? []) if (SourceConversionOptions.ContainsKey(pair.Key)) SourceConversionOptions[pair.Key] = pair.Value;
        }
        IsSourceConversionVisible = true;
    }

    public async Task ConvertLocalSourceAsync()
    {
        if (IsSourceConversionBusy || SourceConversionAsset is not { } source) return;
        IsSourceConversionBusy = true;
        try
        {
            var connector = MarketplaceConnectors.FirstOrDefault(item => item.Id == source.MarketplaceId && item.IsEnabled)
                ?? throw new InvalidOperationException("Activez la marketplace avant une conversion.");
            var converter = SourceConverters.Find(SourceConversionConverterId) ?? throw new InvalidOperationException("Convertisseur absent.");
            var toUnreal = SourceConversionToUnreal || converter.Descriptor.RequiresTargetEngine;
            var engine = toUnreal ? Engines.FirstOrDefault(item => item.Engine.Id == SourceConversionEngineId)?.Engine
                ?? throw new InvalidOperationException("Choisissez le moteur Unreal cible.") : null;
            var options = new Dictionary<string, string>(SourceConversionOptions);
            await _settings.SetAsync("source-conversion." + source.Id, JsonSerializer.Serialize(options)).ConfigureAwait(true);
            await connector.SaveAsync().ConfigureAwait(true);
            var storage = connector.Snapshot();
            var profile = new MarketplaceStorageProfile(connector.Id, Path.Combine(DataDirectory, "Cache", connector.Id), storage.DownloadDirectory,
                storage.Libraries!.Select(item => new MarketplaceLibraryLocation(item.Path, item.MaximumBytes, item.Enabled)).ToArray());
            var mode = Enum.Parse<LibraryPackageStorageMode>(storage.Compression);
            string? published = null;
            var task = _taskScheduler.Enqueue(new HubTaskDefinition("Conversion · " + connector.Name, "Convertir " + source.Name,
                async (operation, token) =>
                {
                    var scratch = Path.Combine(DataDirectory, "SourceConversionInputs", Guid.NewGuid().ToString("N"));
                    var packaging = Path.Combine(DataDirectory, "SourceConversionPackages", Guid.NewGuid().ToString("N"));
                    try
                    {
                        if (File.Exists(source.FilePath)) await ConnectorLibraryPackageService.MaterializeFileAsync(source.FilePath, scratch, token).ConfigureAwait(false);
                        else await DerivedLibraryPackageService.MaterializeAsync(source.FilePath, scratch, cancellationToken: token).ConfigureAwait(false);
                        var progress = new InlineProgress<FabInstallationScriptProgress>(value => operation.Report(value.Fraction, value.Message));
                        var converted = await converter.ConvertAsync(new(scratch, scratch + ".converted", options, engine), progress, token).ConfigureAwait(false);
                        if (toUnreal && !converter.Descriptor.RequiresTargetEngine)
                            converted = await SourceConverters.Find(UnrealRawSourceConverter.ConverterId)!.ConvertAsync(new(converted.Directory,
                                Path.Combine(scratch + ".converted", "Unreal"), options, engine), progress, token).ConfigureAwait(false);
                        var version = new RemoteFabProjectVersion(source.Id, [], engine is null ? [] : [engine.Version.ToString()], []);
                        var name = toUnreal ? "Unreal " + engine!.Version : options.GetValueOrDefault("outputFormat", "GLB");
                        var result = await DerivedLibraryPackageService.CreateAsync(new(
                            new(source.Id, "local", source.Name, "", source.MarketplaceId, "ASSET_PACK", null, null, [], []), version,
                            converted.Directory, packaging, name, "Conversion " + converter.Descriptor.Id + " · original " + source.Id,
                            converted.ContentKind, mode), cancellationToken: token).ConfigureAwait(false);
                        var placed = await MarketplaceStorageRouter.PlaceAsync(result, profile, cancellationToken: token).ConfigureAwait(false);
                        var entry = await ConnectorDownloadCatalog.RegisterAsync(source.MarketplaceId, placed.DestinationDirectory).ConfigureAwait(false);
                        await ConnectorDownloadCatalog.RenameAsync(entry.Id, source.Name + " · " + name, "Dérivé de " + source.Id + " · " + converter.Descriptor.Id).ConfigureAwait(false);
                        published = placed.DestinationDirectory;
                        operation.Report(1, "Dérivé local créé et indexé ; original conservé.");
                    }
                    finally
                    {
                        DeleteOwnedDerivedDirectory(scratch, "SourceConversionInputs");
                        DeleteOwnedDerivedDirectory(scratch + ".converted", "SourceConversionInputs");
                        DeleteOwnedDerivedDirectory(packaging, "SourceConversionPackages");
                    }
                }));
            SourceConversionStatus = "Conversion en cours ; suivi et annulation dans Actions.";
            var result = await task.Completion.ConfigureAwait(true);
            if (result.Status != HubTaskStatus.Completed || published is null) throw new InvalidOperationException("Conversion non publiée : " + result.Error);
            SourceConversionStatus = "Dérivé créé : " + published;
            await ShowConnectorLibraryAsync(source.MarketplaceId, published).ConfigureAwait(true);
            IsSourceConversionVisible = false;
        }
        catch (Exception error) { SourceConversionStatus = error.Message; ShowError(error); }
        finally { IsSourceConversionBusy = false; }
    }
}
