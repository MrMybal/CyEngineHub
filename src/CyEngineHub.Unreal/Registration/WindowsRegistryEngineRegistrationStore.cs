using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CyEngineHub.Unreal.Registration;

public sealed class WindowsRegistryEngineRegistrationStore : IUnrealEngineRegistrationStore
{
    private const string RegistryPath = @"SOFTWARE\Epic Games\Unreal Engine\Builds";

    public IReadOnlyList<UnrealEngineRegistration> ReadAll()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw CreatePlatformException();
        }

        return ReadAllWindows();
    }

    public void Write(UnrealEngineRegistration registration)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw CreatePlatformException();
        }

        WriteWindows(registration);
    }

    public bool Delete(string identifier)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw CreatePlatformException();
        }

        return DeleteWindows(identifier);
    }

    [SupportedOSPlatform("windows")]
    private static UnrealEngineRegistration[] ReadAllWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key is null)
            {
                return [];
            }

            return key.GetValueNames()
                .Select(identifier => new
                {
                    Identifier = identifier,
                    RootPath = key.GetValue(identifier) as string,
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.RootPath))
                .Select(item => new UnrealEngineRegistration(item.Identifier, item.RootPath!))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new UnrealEngineRegistrationException(
                "Impossible de lire les associations Unreal du profil utilisateur.",
                exception);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void WriteWindows(UnrealEngineRegistration registration)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
                ?? throw new UnrealEngineRegistrationException(
                    "Impossible de créer la clé d’associations Unreal.");
            key.SetValue(registration.Identifier, registration.RootPath, RegistryValueKind.String);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new UnrealEngineRegistrationException(
                "Impossible d’enregistrer ce moteur dans le profil utilisateur.",
                exception);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool DeleteWindows(string identifier)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true);
            if (key is null || !key.GetValueNames().Contains(identifier, StringComparer.Ordinal))
            {
                return false;
            }

            key.DeleteValue(identifier, throwOnMissingValue: false);
            return true;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new UnrealEngineRegistrationException(
                "Impossible de désenregistrer ce moteur du profil utilisateur.",
                exception);
        }
    }

    private static PlatformNotSupportedException CreatePlatformException() => new(
        "Les associations Unreal du registre sont disponibles uniquement sous Windows.");
}
