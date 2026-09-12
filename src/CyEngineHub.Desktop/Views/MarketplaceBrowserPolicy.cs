namespace CyEngineHub.Desktop.Views;

internal static class MarketplaceBrowserPolicy
{
    public static bool CanNavigate(string value, bool allowBlank = false) =>
        allowBlank && value == "about:blank" ||
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    public static string DisplayOrigin(string value) =>
        CanNavigate(value) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Authority)
            : "Navigateur intégré";
}
