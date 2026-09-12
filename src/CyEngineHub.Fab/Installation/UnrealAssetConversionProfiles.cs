using System.Text.Json;

namespace CyEngineHub.Fab.Installation;

public sealed record UnrealAssetConversionProfile(
    string Id, int Revision, string AssetType, string SourceVersion, string TargetVersion,
    string NativeAdapter, string PortableAdapter, string[] Checks, string[] Losses, string Notes);

public sealed record UnrealAssetConversionProfileSet(int SchemaVersion, UnrealAssetConversionProfile[] Profiles);

public sealed record UnrealAssetConversionInventoryItem(
    string Path, string AssetType, string[] Dependencies, string[] Features, string[] InspectionErrors);

public sealed record UnrealAssetConversionDecision(
    string AssetPath, string AssetType, string? ProfileId, int? ProfileRevision,
    string Status, string Adapter, string Reason, string[] Checks, string[] Losses, string[] Dependencies);

public sealed record UnrealAssetConversionPlan(
    int SchemaVersion, string SourceVersion, string TargetVersion, string Mode,
    bool CanConvert, UnrealAssetConversionDecision[] Assets);

/// <summary>Declarative policy only: a JSON file cannot enable an unimplemented adapter.</summary>
public static class UnrealAssetConversionProfiles
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly Dictionary<string, string> PortableAdapters = new(StringComparer.Ordinal)
    {
        ["StaticMesh"] = "portable-static-mesh-v1",
        ["Texture2D"] = "portable-texture-v1",
        ["SoundWave"] = "portable-sound-v1",
    };

    public static UnrealAssetConversionProfileSet Load(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
            throw new InvalidOperationException("Le fichier de profils est absent ou dépasse 1 Mio.");
        var set = JsonSerializer.Deserialize<UnrealAssetConversionProfileSet>(File.ReadAllText(path), JsonOptions);
        Validate(set);
        return set!;
    }

    public static void Validate(UnrealAssetConversionProfileSet? set)
    {
        if (set is null || set.SchemaVersion != 1 || set.Profiles is null || set.Profiles.Length is 0 or > 256)
            throw new InvalidOperationException("Catalogue de profils de conversion invalide (schéma 1 requis).");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in set.Profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Id) || profile.Revision < 1 ||
                string.IsNullOrWhiteSpace(profile.AssetType) || !IsVersion(profile.SourceVersion) || !IsVersion(profile.TargetVersion) ||
                profile.NativeAdapter is null || profile.PortableAdapter is null || profile.Notes is null ||
                !ValidStrings(profile.Checks) || !ValidStrings(profile.Losses) ||
                !ids.Add(profile.Id) || !keys.Add($"{profile.SourceVersion}/{profile.TargetVersion}/{profile.AssetType}"))
                throw new InvalidOperationException("Profil invalide ou doublon de type/couple de versions.");
        }
    }

    public static UnrealAssetConversionPlan Analyze(UnrealAssetConversionProfileSet profiles,
        IReadOnlyList<UnrealAssetConversionInventoryItem> inventory, string sourceVersion, string targetVersion,
        string mode, bool allowLossyConversion)
    {
        Validate(profiles);
        if (mode is not ("native" or "portable")) throw new InvalidOperationException("Mode de conversion inconnu.");
        if (inventory.Count == 0 || inventory.Any(a => a is null || string.IsNullOrWhiteSpace(a.Path) ||
            string.IsNullOrWhiteSpace(a.AssetType) || a.Dependencies is null || a.Features is null || a.InspectionErrors is null) ||
            inventory.Select(a => a.Path).Distinct(StringComparer.Ordinal).Count() != inventory.Count)
            throw new InvalidOperationException("Inventaire d'assets vide, incomplet ou dupliqué.");
        var decisions = inventory.Select(asset => Decide(asset)).ToArray();
        // A dependency with no working adapter blocks the entire pack. No partial publication.
        return new(1, sourceVersion, targetVersion, mode,
            decisions.All(IsExecutable), decisions);

        UnrealAssetConversionDecision Decide(UnrealAssetConversionInventoryItem asset)
        {
            var profile = profiles.Profiles.SingleOrDefault(p => p.AssetType == asset.AssetType &&
                p.SourceVersion == sourceVersion && p.TargetVersion == targetVersion);
            UnrealAssetConversionDecision Result(string status, string adapter, string reason) =>
                new(asset.Path, asset.AssetType, profile?.Id, profile?.Revision, status, adapter, reason,
                    profile?.Checks ?? [], mode == "portable" ? profile?.Losses ?? [] : [], asset.Dependencies);
            if (asset.InspectionErrors.Length > 0)
                return Result("blocked", "", "Inspection incomplète : " + string.Join(" ; ", asset.InspectionErrors));
            if (profile is null)
                return Result("unsupported", "", "Aucun profil pour ce type et ce couple de versions.");
            if (mode == "native")
            {
                if (asset.AssetType != "Texture2D" || profile.NativeAdapter != "native-texture-v1" || sourceVersion != "5.6" || targetVersion != "5.5")
                    return Result("unsupported", "", "Adaptateur natif non implémenté pour ce profil. " + profile.Notes);
                if (!asset.Features.Contains("native-texture-candidate", StringComparer.Ordinal) || asset.Features.Contains("virtual-texture", StringComparer.Ordinal) ||
                    asset.Dependencies.Any(p => !p.StartsWith("/Script/", StringComparison.Ordinal)))
                    return Result("blocked", "", "Texture native : inspection requise, virtual texturing ou dépendances non gérés.");
                return Result("adaptation-required", "native-texture-v1", "Pixels source BGRA8/G8 et propriétés prises en charge : contrôle C++ obligatoire, sauvegarde et réouverture cible comparées.");
            }
            if (!PortableAdapters.TryGetValue(asset.AssetType, out var adapter) || profile.PortableAdapter != adapter)
                return Result("unsupported", "", "Adaptateur portable absent ou non validé pour ce type. " + profile.Notes);
            // This implementation does not preserve project dependencies, Nanite, virtual textures or LOD chains.
            if (asset.Dependencies.Any(p => p.StartsWith("/Game/", StringComparison.Ordinal) ||
                (!p.StartsWith("/Engine/", StringComparison.Ordinal) && !p.StartsWith("/Script/", StringComparison.Ordinal))))
                return Result("blocked", "", "Dépendances projet/plugin : reconstruction et liaison non implémentées.");
            if (asset.Features.Any(f => f is "nanite" or "virtual-texture" or "multiple-lods"))
                return Result("blocked", "", "Fonctionnalité sans adaptation validée : " + string.Join(", ", asset.Features));
            // Loss acknowledgement cannot be bypassed by removing descriptions in a custom JSON profile.
            return Result(allowLossyConversion ? "lossy-confirmed" : "loss-confirmation-required", adapter,
                "Export/import expérimental, pas une sérialisation native. Propriétés et données dérivées peuvent être perdues.");
        }
    }

    public static bool IsExecutable(UnrealAssetConversionDecision decision) => decision.Status is "lossy-confirmed" or "adaptation-required";

    private static bool ValidStrings(string[]? values) => values is not null && values.Length <= 128 && values.All(v => !string.IsNullOrWhiteSpace(v));
    private static bool IsVersion(string? value) => value is not null && value.Split('.') is [var major, var minor] &&
        int.TryParse(major, out var a) && a > 0 && int.TryParse(minor, out var b) && b >= 0;
}
