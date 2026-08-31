using CyEngineHub.Epic.Models;

namespace CyEngineHub.Epic.Authentication;

public interface IEpicAuthenticationService
{
    Uri SignInUri { get; }

    Uri AccountUri { get; }

    Uri DeveloperPortalUri { get; }

    Task<EpicAuthenticationStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);
}

public sealed class OfficialEpicWebAuthenticationService : IEpicAuthenticationService
{
    public Uri SignInUri { get; } = new("https://www.epicgames.com/id/login");

    public Uri AccountUri { get; } = new("https://www.epicgames.com/account/personal");

    public Uri DeveloperPortalUri { get; } = new("https://dev.epicgames.com/portal");

    public Task<EpicAuthenticationStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new EpicAuthenticationStatus(
            EpicAuthenticationState.ExternalBrowserRequired,
            "Epic Account · navigateur officiel",
            "La session reste dans le navigateur; CyEngineHub ne lit ni cookies, ni mot de passe, ni token EGL.",
            false,
            false));
    }
}
