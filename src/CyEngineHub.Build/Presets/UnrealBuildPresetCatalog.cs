using System.Text.Json;
using System.Text.Json.Serialization;
using CyEngineHub.Core.Settings;

namespace CyEngineHub.Build.Presets;

public sealed class UnrealBuildPresetCatalog
{
    public const string SettingsKey = "build.presets.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ISettingsStore _settings;

    public UnrealBuildPresetCatalog(ISettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<UnrealBuildPreset>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var custom = await ReadCustomAsync(cancellationToken).ConfigureAwait(false);
        return UnrealBuildPresetDefaults.All.Concat(custom).ToArray();
    }

    public async Task<UnrealBuildPreset> SaveCustomAsync(
        UnrealBuildPreset preset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (UnrealBuildPresetDefaults.IsBuiltIn(preset.Id))
        {
            throw new InvalidOperationException(
                "Un preset intégré doit être copié avant modification.");
        }

        var normalized = UnrealBuildPresetValidator.ValidateAndNormalize(preset);
        var custom = (await ReadCustomAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var index = custom.FindIndex(item =>
            string.Equals(item.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            custom[index] = normalized;
        }
        else
        {
            custom.Add(normalized);
        }

        await WriteCustomAsync(custom, cancellationToken).ConfigureAwait(false);
        return normalized;
    }

    public async Task<bool> DeleteCustomAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (UnrealBuildPresetDefaults.IsBuiltIn(id))
        {
            return false;
        }

        var custom = (await ReadCustomAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var removed = custom.RemoveAll(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            await WriteCustomAsync(custom, cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    private async Task<IReadOnlyList<UnrealBuildPreset>> ReadCustomAsync(
        CancellationToken cancellationToken)
    {
        var json = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<UnrealBuildPreset>>(json, SerializerOptions) ?? [];
            var results = new List<UnrealBuildPreset>();
            foreach (var value in values)
            {
                if (UnrealBuildPresetDefaults.IsBuiltIn(value.Id))
                {
                    continue;
                }

                try
                {
                    results.Add(UnrealBuildPresetValidator.ValidateAndNormalize(value));
                }
                catch (ArgumentException)
                {
                }
            }

            return results
                .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private Task WriteCustomAsync(
        IReadOnlyList<UnrealBuildPreset> presets,
        CancellationToken cancellationToken) =>
        _settings.SetAsync(
            SettingsKey,
            JsonSerializer.Serialize(presets, SerializerOptions),
            cancellationToken);
}

public static class UnrealBuildPresetValidator
{
    public static UnrealBuildPreset ValidateAndNormalize(UnrealBuildPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        var id = Require(preset.Id, nameof(preset.Id), 120);
        var name = Require(preset.Name, nameof(preset.Name), 100);
        var platform = ValidateToken(preset.Platform, nameof(preset.Platform));
        var configuration = ValidateToken(preset.Configuration, nameof(preset.Configuration));
        var target = preset.Target.Trim();
        if (preset.Kind == UnrealBuildPresetKind.Target && string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("Un target UBT est requis.", nameof(preset));
        }

        if (preset.MaximumParallelActions is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preset),
                "Le parallélisme doit être positif.");
        }

        var cookFlavor = string.IsNullOrWhiteSpace(preset.CookFlavor)
            ? null
            : ValidateToken(preset.CookFlavor, nameof(preset.CookFlavor));
        return preset with
        {
            Id = id,
            Name = name,
            Target = target,
            Platform = platform,
            Configuration = configuration,
            CookFlavor = cookFlavor,
        };
    }

    private static string Require(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("La valeur est obligatoire.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException(
                $"La valeur dépasse {maximumLength} caractères.",
                parameterName);
        }

        return normalized;
    }

    private static string ValidateToken(string value, string parameterName)
    {
        var normalized = Require(value, parameterName, 64);
        if (normalized.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '_' and not '-'))
        {
            throw new ArgumentException(
                "Utilisez uniquement lettres, nombres, tirets et underscores.",
                parameterName);
        }

        return normalized;
    }
}
