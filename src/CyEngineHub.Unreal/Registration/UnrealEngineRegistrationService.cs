namespace CyEngineHub.Unreal.Registration;

public sealed class UnrealEngineRegistrationService : IUnrealEngineRegistrationService
{
    private readonly IUnrealEngineRegistrationStore _store;
    private readonly StringComparer _pathComparer;

    public UnrealEngineRegistrationService(IUnrealEngineRegistrationStore store)
    {
        _store = store;
        _pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    public UnrealEngineRegistration? FindByRoot(string rootPath)
    {
        var normalizedRoot = NormalizeRoot(rootPath, requireValidEngine: false);
        return _store.ReadAll().FirstOrDefault(registration =>
            PathsEqual(registration.RootPath, normalizedRoot));
    }

    public UnrealEngineRegistration Register(string rootPath)
    {
        var normalizedRoot = NormalizeRoot(rootPath, requireValidEngine: true);
        var existing = _store.ReadAll().FirstOrDefault(registration =>
            PathsEqual(registration.RootPath, normalizedRoot));
        if (existing is not null)
        {
            return existing;
        }

        var registration = new UnrealEngineRegistration(
            Guid.NewGuid().ToString("B").ToUpperInvariant(),
            normalizedRoot);
        _store.Write(registration);
        return registration;
    }

    public bool Unregister(string rootPath)
    {
        var normalizedRoot = NormalizeRoot(rootPath, requireValidEngine: false);
        var matches = _store.ReadAll()
            .Where(registration => PathsEqual(registration.RootPath, normalizedRoot))
            .ToArray();
        var deleted = false;
        foreach (var registration in matches)
        {
            deleted |= _store.Delete(registration.Identifier);
        }

        return deleted;
    }

    private bool PathsEqual(string left, string right)
    {
        try
        {
            return _pathComparer.Equals(
                NormalizeRoot(left, requireValidEngine: false),
                right);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizeRoot(string rootPath, bool requireValidEngine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var normalized = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(rootPath.Trim().Trim('"')));
        if (requireValidEngine &&
            (!Directory.Exists(Path.Combine(normalized, "Engine", "Binaries")) ||
             !Directory.Exists(Path.Combine(normalized, "Engine", "Build"))))
        {
            throw new UnrealEngineRegistrationException(
                "Le dossier ne contient pas Engine\\Binaries et Engine\\Build ; Unreal ne le considère pas comme un moteur enregistrable.");
        }

        return normalized;
    }
}
