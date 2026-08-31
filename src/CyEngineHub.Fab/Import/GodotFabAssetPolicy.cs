namespace CyEngineHub.Fab.Import;

public static class GodotFabAssetPolicy
{
    private static readonly HashSet<string> SupportedExtensions = new([
        ".fbx", ".obj", ".gltf", ".glb", ".usd", ".usda", ".usdc", ".abc",
        ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".exr", ".hdr", ".dds", ".tif", ".tiff", ".psd",
        ".wav", ".ogg", ".mp3", ".flac", ".json", ".csv", ".txt", ".md", ".xml", ".yaml", ".yml",
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> BlockedExtensions = new([
        ".gd", ".cs", ".gdshader", ".gdshaderinc", ".gdextension",
        ".dll", ".exe", ".com", ".msi", ".so", ".dylib", ".bundle", ".aar", ".jar",
        ".js", ".shader", ".compute", ".hlsl", ".cginc", ".glsl", ".metal",
        ".bat", ".cmd", ".ps1", ".sh", ".py", ".rb", ".pl", ".lua",
    ], StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string extension) => SupportedExtensions.Contains(extension);

    public static bool IsBlocked(string extension) => BlockedExtensions.Contains(extension);
}

