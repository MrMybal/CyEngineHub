namespace CyEngineHub.Projects.Creation;

/// <summary>Profile, ordered feature packs, then explicit per-project choices. Last layer wins.</summary>
public static class ProjectConfigurationComposer
{
    public static ProjectConfigurationProfile Compose(
        ProjectConfigurationProfile profile,
        IEnumerable<ProjectFeaturePack> packs,
        IReadOnlyDictionary<string, bool>? manualPlugins = null)
    {
        var plugins = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        ApplyPlugins(profile.EnabledPlugins, profile.DisabledPlugins);
        var settings = profile.EngineSettings.Select(pair => new ProjectIniSetting(
            "DefaultEngine.ini", "/Script/Engine.RendererSettings", pair.Key, pair.Value))
            .Concat(profile.IniSettings).ToList();
        foreach (var pack in packs)
        {
            ApplyPlugins(pack.EnabledPlugins, pack.DisabledPlugins);
            settings.AddRange(pack.IniSettings);
        }
        foreach (var pair in manualPlugins ?? new Dictionary<string, bool>())
            plugins[pair.Key] = pair.Value;
        return profile with
        {
            EngineSettings = new Dictionary<string, string>(),
            IniSettings = MergeIniSettings(settings),
            EnabledPlugins = plugins.Where(pair => pair.Value).Select(pair => pair.Key).ToArray(),
            DisabledPlugins = plugins.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray(),
        };

        void ApplyPlugins(IEnumerable<string> enabled, IEnumerable<string> disabled)
        {
            foreach (var name in enabled.Where(name => !string.IsNullOrWhiteSpace(name))) plugins[name.Trim()] = true;
            foreach (var name in disabled.Where(name => !string.IsNullOrWhiteSpace(name))) plugins[name.Trim()] = false;
        }
    }

    public static IReadOnlyList<ProjectIniSetting> MergeIniSettings(IEnumerable<ProjectIniSetting> settings)
    {
        var result = new List<ProjectIniSetting>();
        foreach (var original in settings)
        {
            var setting = original with { FileName = UnrealProjectIni.NormalizeFileName(original.FileName) };
            UnrealProjectIni.ValidateSetting(setting);
            // Unreal array operators are ordered commands, not scalar keys.
            if (setting.Key[0] is not ('+' or '-' or '.' or '!'))
                result.RemoveAll(previous =>
                    previous.FileName.Equals(setting.FileName, StringComparison.OrdinalIgnoreCase) &&
                    previous.Section.Equals(setting.Section, StringComparison.OrdinalIgnoreCase) &&
                    previous.Key.Equals(setting.Key, StringComparison.OrdinalIgnoreCase));
            result.Add(setting);
        }
        return result;
    }
}
