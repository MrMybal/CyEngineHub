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

    public UnrealEngineRegistration SetAssociation(string rootPath, string identifier)
    {
        var normalizedRoot = NormalizeRoot(rootPath, requireValidEngine: true);
        ArgumentNullException.ThrowIfNull(identifier);
        identifier = identifier.Trim();
        if (identifier.Length is 0 or > 128 || identifier.Any(c => char.IsControl(c) || c is '\\' or '/' or '"') ||
            Version.TryParse(identifier, out _))
            throw new UnrealEngineRegistrationException("Utilisez un identifiant personnalisé ou un GUID (128 caractères maximum), pas un numéro de version Epic.");
        var all = _store.ReadAll();
        if (all.Any(item => item.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase) && !PathsEqual(item.RootPath, normalizedRoot)))
            throw new UnrealEngineRegistrationException("Cet identifiant est déjà associé à un autre moteur ; il ne sera pas écrasé.");
        var previous = all.Where(item => PathsEqual(item.RootPath, normalizedRoot)).ToArray();
        var updated = new UnrealEngineRegistration(identifier, normalizedRoot);
        // Write and verify before removing any previous value. Unrelated engines are never changed.
        try
        {
            _store.Write(updated);
            if (!_store.ReadAll().Any(item => item.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase) && PathsEqual(item.RootPath, normalizedRoot)))
                throw new UnrealEngineRegistrationException("La nouvelle association n’a pas pu être relue ; anciennes valeurs conservées.");
            foreach (var item in previous.Where(item => !item.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase)))
                _store.Delete(item.Identifier);
        }
        catch
        {
            foreach (var item in previous) _store.Write(item);
            if (!previous.Any(item => item.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase))) _store.Delete(identifier);
            throw;
        }
        return updated;
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
