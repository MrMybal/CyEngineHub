namespace CyEngineHub.Desktop.Services;

public static class SafeFolderSelection
{
    // Native pickers can fail before returning (e.g. a folder disappears in the dialog).
    // Keep exceptions inside the awaited boundary, rather than escaping an async-void UI event.
    public static async Task SelectAsync(Func<Task<string?>> pick, Action<string> apply, Action<string> report)
    {
        try
        {
            var path = await pick().ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!Directory.Exists(path))
            {
                report("Le dossier sélectionné n’existe plus ou n’est pas accessible. Choisissez un dossier parent existant, puis réessayez.");
                return;
            }
            apply(Path.GetFullPath(path));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            report($"Sélection du dossier impossible ({exception.GetType().Name}). Choisissez un dossier parent existant, puis réessayez.");
        }
    }
}
