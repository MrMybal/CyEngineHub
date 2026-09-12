namespace CyEngineHub.Fab.Import;

public static class UnityFabAssetPolicy
{
    private static readonly HashSet<string> SupportedExtensions = new([
        ".fbx", ".obj", ".mtl", ".gltf", ".glb", ".bin", ".usd", ".usda", ".usdc", ".abc",
        ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".exr", ".hdr", ".dds", ".tif", ".tiff", ".psd",
        ".wav", ".ogg", ".mp3", ".flac", ".json", ".csv", ".txt", ".md", ".xml", ".yaml", ".yml",
        ".asset", ".prefab", ".mat", ".anim", ".controller", ".overridecontroller", ".unity",
        ".physicmaterial", ".physicsmaterial2d", ".lighting", ".cubemap", ".rendertexture",
        ".spriteatlas", ".mask", ".guiskin", ".fontsettings", ".playable", ".signal", ".meta",
    ], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> BlockedExtensions = new([
        ".cs", ".dll", ".exe", ".com", ".msi", ".so", ".dylib", ".bundle", ".aar", ".jar",
        ".js", ".boo", ".asmdef", ".asmref", ".rsp", ".shader", ".compute", ".hlsl", ".cginc",
        ".glsl", ".metal", ".bat", ".cmd", ".ps1", ".sh", ".py", ".rb", ".pl", ".lua",
    ], StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string extension) => SupportedExtensions.Contains(extension);

    public static bool IsBlocked(string extension) => BlockedExtensions.Contains(extension);
}
