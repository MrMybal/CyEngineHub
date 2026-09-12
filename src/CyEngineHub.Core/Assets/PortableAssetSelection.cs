using System.Text.Json;

namespace CyEngineHub.Core.Assets;

public static class PortableAssetSelection
{
    // A single model file cannot represent its external buffers/materials/textures.
    // Ask for the containing pack instead of silently producing a broken import.
    public static void ValidateSingleFile(string path)
    {
        var extension = Path.GetExtension(path);
        if (!extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".obj", StringComparison.OrdinalIgnoreCase)) return;
        if (new FileInfo(path).Length > 16 * 1024 * 1024)
            throw new InvalidOperationException("Pour ce modèle, sélectionnez le dossier du pack avec ses dépendances.");
        var external = false;
        if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
            external = File.ReadLines(path).Any(line => line.TrimStart().StartsWith("mtllib ", StringComparison.OrdinalIgnoreCase) || line.TrimStart().StartsWith("mtllib\t", StringComparison.OrdinalIgnoreCase));
        else
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var name in new[] { "buffers", "images" })
                    if (json.RootElement.TryGetProperty(name, out var items) && items.ValueKind == JsonValueKind.Array)
                        foreach (var item in items.EnumerateArray())
                            if (item.TryGetProperty("uri", out var uri) && uri.ValueKind == JsonValueKind.String &&
                                uri.GetString() is { } value && !value.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) external = true;
            }
            catch (JsonException error) { throw new InvalidOperationException("Le fichier glTF est invalide.", error); }
        }
        if (external) throw new InvalidOperationException("Ce modèle a des fichiers annexes : sélectionnez le dossier du pack avec ses dépendances, pas le modèle seul.");
    }
}
