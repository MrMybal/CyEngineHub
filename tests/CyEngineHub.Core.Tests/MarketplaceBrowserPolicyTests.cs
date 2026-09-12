using CyEngineHub.Desktop.Views;

namespace CyEngineHub.Core.Tests;

public sealed class MarketplaceBrowserPolicyTests
{
    [Theory]
    [InlineData("https://cosmos.leartesstudios.com/inventory", true)]
    [InlineData("https://accounts.google.com/signin?state=value", true)]
    [InlineData("http://example.com", false)]
    [InlineData("file:///C:/secret.txt", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,hello", false)]
    [InlineData("https://user:password@example.com", false)]
    [InlineData("ms-settings:privacy", false)]
    [InlineData("about:blank", false)]
    public void NavigationIsRestrictedToSecureWebPages(string url, bool expected) =>
        Assert.Equal(expected, MarketplaceBrowserPolicy.CanNavigate(url));

    [Fact]
    public void ChildWindowMayStartBlankButNotOpenBrowserSettings()
    {
        Assert.True(MarketplaceBrowserPolicy.CanNavigate("about:blank", allowBlank: true));
        Assert.False(MarketplaceBrowserPolicy.CanNavigate("about:settings", allowBlank: true));
    }

    [Fact]
    public void DisplayDoesNotExposeAuthenticationParametersOrPaths() =>
        Assert.Equal("https://api.cosmos.leartesstudios.com",
            MarketplaceBrowserPolicy.DisplayOrigin("https://api.cosmos.leartesstudios.com/auth/secret?code=secret#token"));
}
