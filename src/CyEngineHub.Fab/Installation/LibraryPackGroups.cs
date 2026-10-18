using System.Text.Json;
using CyEngineHub.Core.Settings;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Fab.Installation;

public sealed record LibraryPackGroup(string Id, string Name, IReadOnlyList<LibraryPackGroupItem> Items);
public sealed record LibraryPackGroupItem(string AssetId, string ArtifactId, string Name, string Source,
    string Path, LibraryPackageContentKind ContentKind, IReadOnlyList<string> EngineVersions,
    IReadOnlyList<FabInstallationStep> Steps);

public sealed class LibraryPackGroupStore(ISettingsStore settings)
{
    public const string Key = "library.pack-groups.v1";
    public async Task<IReadOnlyList<LibraryPackGroup>> LoadAsync()
    {
        var json = await settings.GetAsync(Key).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return [];
        var groups = JsonSerializer.Deserialize<LibraryPackGroup[]>(json) ?? throw new InvalidDataException("Groupes illisibles ; aucune donnée remplacée.");
        Validate(groups); return groups;
    }
    public Task SaveAsync(IReadOnlyList<LibraryPackGroup> groups)
    { Validate(groups); return settings.SetAsync(Key, JsonSerializer.Serialize(groups)); }
    private static void Validate(IReadOnlyList<LibraryPackGroup> groups)
    {
        if (groups.Count > 200 || groups.Select(g => g.Id).Distinct().Count() != groups.Count)
            throw new InvalidDataException("Identifiants de groupes invalides.");
        foreach (var group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.Id) || string.IsNullOrWhiteSpace(group.Name) || group.Name.Length > 120 || group.Items is null || group.Items.Count > 500)
                throw new InvalidDataException("Groupe invalide (500 packs maximum).");
            foreach (var item in group.Items)
                if (item is null || !System.IO.Path.IsPathFullyQualified(item.Path) || string.IsNullOrWhiteSpace(item.ArtifactId) || item.EngineVersions is null || item.Steps is null || item.Steps.Count is 0 or > 32 || item.Steps.Any(s => s is null || string.IsNullOrWhiteSpace(s.ScriptId) || s.Configuration is null))
                    throw new InvalidDataException("Pack ou chaîne de scripts invalide.");
        }
    }
}
