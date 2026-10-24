namespace CyEngineHub.Desktop.ImGui;

internal enum OperationProgressTone { Active, Completed, Failed, Canceled }

internal static class OperationProgressPolicy
{
    internal static OperationProgressTone Resolve(string? status, float fraction, bool indeterminate = false)
    {
        if (status is "Échec" or "Failed" || status?.StartsWith("Échec", StringComparison.Ordinal) == true) return OperationProgressTone.Failed;
        if (status is "Annulé" or "Canceled" or "Interrupted") return OperationProgressTone.Canceled;
        if (status is "Terminé" or "Completed" or "Installé") return OperationProgressTone.Completed;
        // A reported 100% is not necessarily the end: validation may still be running.
        if (status is "En cours" or "Running" or "En attente" or "Queued" or "Annulation…" or "Téléchargement" || indeterminate) return OperationProgressTone.Active;
        return fraction >= 1 ? OperationProgressTone.Completed : OperationProgressTone.Active;
    }
}
