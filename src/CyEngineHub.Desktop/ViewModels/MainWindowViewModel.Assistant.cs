using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string AssistantSettingsKey = "ai.assistant.preferences.v1";
    private nint _assistant;
    private bool _assistantStopping;
    private string _assistantProvider = "ollama";
    private string _assistantModel = "";
    private string _assistantEndpoint = "";
    private string _assistantRemoteUrl = "";
    private string _assistantRemoteTools = "";
    private string _assistantStatus = "";
    private int _assistantContextKiB;
    private int _assistantRecentContextTurns = 8;
    private sealed record AssistantPreferences(string Provider, string Model, string Endpoint, string RemoteUrl, string RemoteTools,
        int ContextKiB = 0, int RecentContextTurns = 8);

    public string AssistantProvider { get => _assistantProvider; set => SetProperty(ref _assistantProvider, value); }
    public string AssistantModel { get => _assistantModel; set => SetProperty(ref _assistantModel, value); }
    public string AssistantEndpoint { get => _assistantEndpoint; set => SetProperty(ref _assistantEndpoint, value); }
    public string AssistantRemoteUrl { get => _assistantRemoteUrl; set => SetProperty(ref _assistantRemoteUrl, value); }
    public string AssistantRemoteTools { get => _assistantRemoteTools; set => SetProperty(ref _assistantRemoteTools, value); }
    public int AssistantContextKiB { get => _assistantContextKiB; set => SetProperty(ref _assistantContextKiB, value); }
    public int AssistantRecentContextTurns { get => _assistantRecentContextTurns; set => SetProperty(ref _assistantRecentContextTurns, value); }
    public string AssistantStatus { get => _assistantStatus; private set => SetProperty(ref _assistantStatus, value); }
    public ICommand OpenAssistantCommand => _openAssistant ??= new AsyncRelayCommand(() => OpenAssistantAsync(false), onError: ShowError);
    public ICommand RestoreAssistantCommand => _restoreAssistant ??= new AsyncRelayCommand(() => OpenAssistantAsync(true), onError: ShowError);
    public ICommand AttachAssistantFilesCommand => _attachAssistantFiles ??= new AsyncRelayCommand(AttachAssistantFilesAsync, onError: ShowError);
    public ICommand DetectAssistantProvidersCommand => _detectAssistantProviders ??= new AsyncRelayCommand(DetectAssistantProvidersAsync, onError: ShowError);
    private ICommand? _openAssistant, _restoreAssistant, _attachAssistantFiles, _detectAssistantProviders;

    private async Task DetectAssistantProvidersAsync()
    {
        AssistantStatus = "Détection des fournisseurs…";
        var encoded = await Task.Run(() => Marshal.PtrToStringUTF8(AssistantNative.Detect())).ConfigureAwait(true);
        using var result = JsonDocument.Parse(encoded ?? "[]");
        AssistantStatus = result.RootElement.ValueKind == JsonValueKind.Array
            ? string.Join(" · ", result.RootElement.EnumerateArray().Select(item =>
                item.GetProperty("provider").GetString() + " " + (item.GetProperty("state").GetString() switch
                {
                    "available" => "disponible",
                    "not-running" => "arrêté",
                    _ => "à configurer",
                })))
            : result.RootElement.GetProperty("error").GetString() ?? "Détection indisponible.";
    }

    private async Task LoadAssistantSettingsAsync()
    {
        var saved = await _settings.GetAsync(AssistantSettingsKey).ConfigureAwait(true);
        if (string.IsNullOrEmpty(saved)) return;
        try
        {
            if (JsonSerializer.Deserialize<AssistantPreferences>(saved) is not { } preferences) return;
            AssistantProvider = preferences.Provider;
            AssistantModel = preferences.Model;
            AssistantEndpoint = preferences.Endpoint;
            AssistantRemoteUrl = preferences.RemoteUrl;
            AssistantRemoteTools = preferences.RemoteTools;
            AssistantContextKiB = Math.Clamp(preferences.ContextKiB, 0, 4096);
            AssistantRecentContextTurns = Math.Clamp(preferences.RecentContextTurns, 1, 128);
        }
        catch (JsonException) { AssistantStatus = "Préférences de l’assistant illisibles."; }
    }

    private async Task OpenAssistantAsync(bool restore)
    {
        var provider = AssistantProvider.Trim().ToLowerInvariant();
        if (provider is not ("ollama" or "openai" or "anthropic" or "openrouter"))
            throw new ArgumentException("Fournisseurs disponibles : ollama, openai, anthropic ou openrouter.");
        var endpoint = AssistantEndpoint.Trim();
        var remoteUrl = AssistantRemoteUrl.Trim();
        ValidateAssistantEndpoint(endpoint);
        ValidateAssistantEndpoint(remoteUrl);
        var remoteTools = AssistantRemoteTools.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (remoteTools.Length > 16 || remoteTools.Any(name =>
                name.Length > 56 || !name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.')))
            throw new ArgumentException("Autorisez jusqu’à 16 noms d’outils MCP séparés par des virgules.");
        if (remoteTools.Length > 0 && remoteUrl.Length == 0)
            throw new ArgumentException("Indiquez l’adresse du serveur MCP externe.");
        if (AssistantContextKiB is < 0 or > 4096 || AssistantRecentContextTurns is < 1 or > 128)
            throw new ArgumentException("Contexte de 0 à 4 096 Kio et de 1 à 128 tours récents.");
        var config = JsonSerializer.Serialize(new
        {
            provider,
            model = AssistantModel.Trim(),
            baseUrl = endpoint,
            historyDirectory = Path.Combine(DataDirectory, "Assistant"),
            restore,
            remoteUrl,
            remoteTools,
            contextBytes = AssistantContextKiB * 1024,
            recentContextTurns = AssistantRecentContextTurns,
        });
        var assistant = GetAssistant();
        if (assistant == 0) return;
        AssistantStatus = Marshal.PtrToStringUTF8(AssistantNative.Open(assistant, config)) ?? "";
        if (AssistantStatus.Length == 0)
            await _settings.SetAsync(AssistantSettingsKey, JsonSerializer.Serialize(
                new AssistantPreferences(provider, AssistantModel, endpoint, remoteUrl, AssistantRemoteTools,
                    AssistantContextKiB, AssistantRecentContextTurns))).ConfigureAwait(true);
    }

    private static void ValidateAssistantEndpoint(string endpoint)
    {
        if (endpoint.Length == 0) return;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("Utilisez une adresse HTTPS ou HTTP locale, sans identifiant, paramètre ni fragment.");
    }

    private async Task AttachAssistantFilesAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }) return;
        var files = await window.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Joindre des fichiers à l’assistant",
            AllowMultiple = true,
        }).ConfigureAwait(true);
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
        if (paths.Length > 0 && GetAssistant() is var assistant && assistant != 0)
            AssistantStatus = Marshal.PtrToStringUTF8(AssistantNative.Attach(assistant, JsonSerializer.Serialize(paths))) ?? "";
    }

    private nint GetAssistant()
    {
        if (_assistantStopping) return 0;
        if (_assistant == 0) _assistant = AssistantNative.Create(SdkRegistry.Handle);
        return _assistant;
    }

    internal void DrawAssistant(float height)
    {
        if (GetAssistant() is var assistant && assistant != 0)
            AssistantNative.Draw(assistant, height);
    }

    internal async Task StopAssistantAsync()
    {
        _assistantStopping = true;
        var assistant = _assistant;
        _assistant = 0;
        if (assistant != 0)
        {
            AssistantNative.Cancel(assistant);
            await Task.Run(() => AssistantNative.Destroy(assistant)).ConfigureAwait(false);
        }
        if (_mcpServer is { } server)
            await server.ConfigureAwait(false);
        _sdkMcpRegistry?.Dispose();
    }

    private static class AssistantNative
    {
        private const string Library = "cimgui";
        [DllImport(Library, EntryPoint = "cyhub_chat_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Create(nint registry);
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA2101", Justification = "The SDK ABI accepts UTF-8, explicitly marshalled here.")]
        [DllImport(Library, EntryPoint = "cyhub_chat_open", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Open(nint chat, [MarshalAs(UnmanagedType.LPUTF8Str)] string config);
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA2101", Justification = "The SDK ABI accepts UTF-8, explicitly marshalled here.")]
        [DllImport(Library, EntryPoint = "cyhub_chat_attach", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Attach(nint chat, [MarshalAs(UnmanagedType.LPUTF8Str)] string paths);
        [DllImport(Library, EntryPoint = "cyhub_chat_draw", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Draw(nint chat, float height);
        [DllImport(Library, EntryPoint = "cyhub_chat_cancel", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Cancel(nint chat);
        [DllImport(Library, EntryPoint = "cyhub_chat_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(nint chat);
        [DllImport(Library, EntryPoint = "cyhub_chat_detect", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint Detect();
    }
}
