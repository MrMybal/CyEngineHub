namespace CyEngineHub.Unreal.Registration;

public sealed record UnrealEngineRegistration(string Identifier, string RootPath);

public interface IUnrealEngineRegistrationStore
{
    IReadOnlyList<UnrealEngineRegistration> ReadAll();

    void Write(UnrealEngineRegistration registration);

    bool Delete(string identifier);
}

public interface IUnrealEngineRegistrationService
{
    UnrealEngineRegistration? FindByRoot(string rootPath);

    UnrealEngineRegistration Register(string rootPath);

    UnrealEngineRegistration SetAssociation(string rootPath, string identifier) =>
        throw new NotSupportedException("La modification des associations n’est pas disponible.");

    bool Unregister(string rootPath);
}

public sealed class UnrealEngineRegistrationException : Exception
{
    public UnrealEngineRegistrationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
