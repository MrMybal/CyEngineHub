namespace CyEngineHub.Desktop.ViewModels;

public sealed class FabWebAuthorizationResult
{
    public string? Code { get; init; }
    public bool UseManualMode { get; init; }
}

public sealed class FabWebAuthorizationRequestEventArgs(Uri authorizationUri, string profileDirectory) : EventArgs
{
    private readonly TaskCompletionSource<FabWebAuthorizationResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Uri AuthorizationUri { get; } = authorizationUri;
    public string ProfileDirectory { get; } = profileDirectory;
    public Task<FabWebAuthorizationResult> Completion => _completion.Task;
    public void Complete(FabWebAuthorizationResult result) => _completion.TrySetResult(result);
}
