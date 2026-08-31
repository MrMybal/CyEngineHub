using System.Text;

namespace CyEngineHub.Projects.Creation;

/// <summary>The preview and the project writer use the same final INI generator.</summary>
public static class UnrealProjectIni
{
    public static IReadOnlyDictionary<string, string> Generate(
        ProjectConfigurationProfile? profile, string projectName, bool legacyRendering = false)
    {
        var settings = (profile?.EngineSettings ?? new Dictionary<string, string>())
            .Select(pair => new ProjectIniSetting("DefaultEngine.ini", "/Script/Engine.RendererSettings", pair.Key, pair.Value))
            .Concat(profile?.IniSettings ?? []).ToList();
        if (legacyRendering)
        {
            foreach (var key in new[] { "r.DynamicGlobalIlluminationMethod", "r.ReflectionMethod", "r.Shadow.Virtual.Enable", "r.Nanite.ProjectEnabled" })
                settings.Add(new("DefaultEngine.ini", "/Script/Engine.RendererSettings", key,
                    key == "r.Nanite.ProjectEnabled" ? "False" : "0"));
        }
        settings.Add(new("DefaultGame.ini", "/Script/EngineSettings.GeneralProjectSettings", "ProjectName", projectName));
        return Render(ProjectConfigurationComposer.MergeIniSettings(settings));
    }

    public static IReadOnlyDictionary<string, string> Render(IEnumerable<ProjectIniSetting> settings) =>
        settings.GroupBy(setting => NormalizeFileName(setting.FileName), StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(file => file.Key, file =>
            {
                var text = new StringBuilder();
                foreach (var section in file.GroupBy(setting => setting.Section, StringComparer.OrdinalIgnoreCase))
                {
                    text.Append('[').Append(section.Key).AppendLine("]");
                    foreach (var setting in section) text.Append(setting.Key).Append('=').AppendLine(setting.Value);
                    text.AppendLine();
                }
                return text.ToString();
            }, StringComparer.OrdinalIgnoreCase);

    public static string NormalizeFileName(string value)
    {
        var name = value.Trim();
        if (name.Length is 0 or > 80 || name.IndexOfAny(['/', '\\', ':']) >= 0 ||
            name.Any(character => char.IsControl(character) || "<>\"|?*".Contains(character)) || name is "." or "..")
            throw new ArgumentException("Le fichier INI doit être un nom simple dans Config, sans chemin.");
        if (!name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) name += ".ini";
        return name.StartsWith("Default", StringComparison.OrdinalIgnoreCase) ? name : "Default" + name;
    }

    public static void ValidateSetting(ProjectIniSetting setting)
    {
        _ = NormalizeFileName(setting.FileName);
        if (string.IsNullOrWhiteSpace(setting.Section) || setting.Section.Length > 256 ||
            setting.Section.Any(character => char.IsControl(character) || character is '[' or ']') ||
            string.IsNullOrWhiteSpace(setting.Key) || setting.Key.Length > 256 ||
            setting.Key.Any(character => char.IsControl(character) || character == '=') ||
            setting.Value.Any(character => character is '\r' or '\n'))
            throw new ArgumentException("Réglage INI invalide : vérifiez le fichier, la section, la clé et la valeur.");
    }

    public static IReadOnlyList<ProjectIniSetting> Parse(string fileName, string content)
    {
        var name = NormalizeFileName(fileName);
        if (content.Length > 4 * 1024 * 1024) throw new ArgumentException("Le fichier INI dépasse 4 Mo.");
        var settings = new List<ProjectIniSetting>();
        var section = string.Empty;
        var lineNumber = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            var separator = line.IndexOf('=');
            if (section.Length == 0 || separator <= 0)
                throw new ArgumentException($"{name}, ligne {lineNumber} : utilisez [Section] puis Clé=Valeur.");
            var setting = new ProjectIniSetting(name, section, line[..separator].Trim(), line[(separator + 1)..]);
            ValidateSetting(setting);
            settings.Add(setting);
        }
        return settings;
    }
}
