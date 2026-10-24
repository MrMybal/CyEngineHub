using System.Text;

namespace CyEngineHub.Projects.Creation;

/// <summary>Copies the selected engine's own templates, never embedded copies from another UE version.</summary>
public static class UnrealPluginTemplates
{
    private static readonly UnrealPluginTemplate[] Templates =
    [
        new("Blank", "Blank", false),
        new("Content Only", "ContentOnly", false),
        new("Blueprint Library", "BlueprintLibrary", false),
        new("Editor Mode", "EditorMode", true),
        new("Editor Standalone Window", "Advanced", true),
        new("Editor Toolbar Button", "Basic", true),
        new("Third Party Library", "ThirdPartyLibrary", false),
    ];
    public static IReadOnlyList<string> Names { get; } = Templates.Select(item => item.Name).ToArray();
    public static UnrealPluginTemplate Find(string name) => Templates.FirstOrDefault(item =>
        item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("Modèle de plugin inconnu : " + name);
    public static IReadOnlyList<string> Available(string? engineRoot) => string.IsNullOrWhiteSpace(engineRoot)
        ? ["Blank", "Content Only"]
        : Templates.Where(item => Directory.Exists(Path.Combine(TemplateRoot(engineRoot), item.Directory))).Select(item => item.Name).ToArray();

    internal static async Task CopyAsync(string engineRoot, string template, string destination, string pluginName, CancellationToken cancellationToken)
    {
        var root = Path.Combine(TemplateRoot(engineRoot), Find(template).Directory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Modèle absent du moteur sélectionné : " + template);
        for (var ancestor = new DirectoryInfo(root); ancestor is not null; ancestor = ancestor.Parent)
            RejectLink(ancestor.FullName);
        var pending = new Stack<string>(); pending.Push(root);
        var count = 0; long total = 0;
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLink(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested(); RejectLink(entry);
                if (Directory.Exists(entry)) { pending.Push(entry); continue; }
                var size = new FileInfo(entry).Length; total += size;
                if (++count > 2_000 || size > 32 * 1024 * 1024 || total > 128 * 1024 * 1024)
                    throw new InvalidDataException("Le modèle de plugin dépasse la limite de copie sécurisée.");
                var relative = Path.GetRelativePath(root, entry).Replace("PLUGIN_NAME", pluginName, StringComparison.Ordinal);
                var target = Path.GetFullPath(Path.Combine(destination, relative));
                if (!target.StartsWith(Path.GetFullPath(destination) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Chemin de modèle hors du plugin.");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (Path.GetExtension(entry).ToLowerInvariant() is ".cs" or ".h" or ".cpp" or ".svg" or ".vcxproj" or ".filters" or ".sln" or ".tps" or ".pbxproj" or ".xcworkspacedata")
                {
                    var text = await File.ReadAllTextAsync(entry, cancellationToken).ConfigureAwait(false);
                    text = text.Replace("PLUGIN_NAME_API", pluginName.ToUpperInvariant() + "_API", StringComparison.Ordinal)
                        .Replace("PLUGIN_NAME", pluginName, StringComparison.Ordinal);
                    await File.WriteAllTextAsync(target, text, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
                }
                else File.Copy(entry, target, overwrite: false);
            }
        }
    }
    private static string TemplateRoot(string engineRoot) => Path.Combine(engineRoot, "Engine", "Plugins", "Editor", "PluginBrowser", "Templates");
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Un modèle de plugin ne peut pas contenir de lien symbolique : " + path);
    }
}

public sealed record UnrealPluginTemplate(string Name, string Directory, bool IsEditor);
