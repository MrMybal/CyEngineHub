namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    internal async Task OpenDescriptionLinkAsync(Uri destination)
    {
        try
        {
            if (!destination.IsAbsoluteUri || destination.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(destination.UserInfo))
                throw new InvalidOperationException("Seuls les liens web HTTP/HTTPS sont autorisés dans une description.");
            await _platformLauncher.OpenUriAsync(destination).ConfigureAwait(true);
            StatusMessage = "Lien ouvert : " + destination.Host;
        }
        catch (Exception exception) { ShowError(exception); }
    }
}
