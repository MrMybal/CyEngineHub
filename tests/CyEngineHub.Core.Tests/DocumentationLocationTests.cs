using CyEngineHub.Desktop.Documentation;
using Xunit;

namespace CyEngineHub.Core.Tests;

public sealed class DocumentationLocationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyDoc routes é & spaces " + Guid.NewGuid().ToString("N"));
    public DocumentationLocationTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root,"DocData"));
        File.WriteAllText(Path.Combine(_root,"Documentation.html"),"<!doctype html>");
        File.WriteAllText(Path.Combine(_root,"DocData","manifest.json"),"""
            {"aliases":{"overview":"welcome"},"pages":[
            {"id":"welcome","locale":"en","headings":[{"id":"start"}],"html":""},
            {"id":"engines","locale":"en","headings":[{"id":"install"}],"html":"<figure data-cydoc-id=\"engines\">"},
            {"id":"engines","locale":"fr","headings":[{"id":"install"}],"html":"<figure data-cydoc-id=\"engines\">"}]}
            """);
    }
    [Fact] public void RoutesEncodeWindowsPathsAndKeepLegacyIdentifiers()
    {
        var location = new DocumentationLocation(_root);
        var uri = location.CreateUri("help:engines#install","fr",media:"engines");
        Assert.True(uri.IsFile); Assert.Equal("#/fr/engines/install?media=engines",uri.Fragment);
        Assert.Equal(Path.Combine(_root,"Documentation.html"),uri.LocalPath);
        Assert.EndsWith("#/en/welcome",location.CreateUri("overview").AbsoluteUri,StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("https://example.invalid/image.png")]
    [InlineData("file://server/share/index.html")]
    [InlineData("file:///C:/Windows/system.ini")]
    [InlineData("data:text/html,test")]
    public void NetworkAndUnrelatedResourcesAreDenied(string url) => Assert.False(new DocumentationLocation(_root).AllowsResource(new(url)));
    [Fact] public void ResourcePolicyUsesDirectoryBoundariesAndNormalizedPaths()
    {
        var location = new DocumentationLocation(_root);
        Assert.True(location.AllowsResource(location.CreateUri("welcome")));
        Assert.False(location.AllowsResource(new Uri(new Uri(Path.Combine(_root,"index.html")),"../secret.txt")));
        Assert.False(location.AllowsResource(new Uri(Path.Combine(_root+"-other","index.html"))));
        Assert.False(location.AllowsResource(new Uri(new Uri(Path.Combine(_root,"index.html")).AbsoluteUri+"?remote=1")));
    }
    [Theory]
    [InlineData("../secret",null,null)]
    [InlineData("missing",null,null)]
    [InlineData("engines","missing",null)]
    [InlineData("engines",null,"missing")]
    public void InvalidPagesAnchorsAndMediaAreRejected(string id,string? anchor,string? media) =>
        Assert.Throws<ArgumentException>(()=>new DocumentationLocation(_root).CreateUri(id,"en",anchor,media));
    [Fact] public void EnglishFallbackDoesNotBreakFrenchRoutes() =>
        Assert.EndsWith("#/fr/welcome/start",new DocumentationLocation(_root).CreateUri("welcome","fr","start").AbsoluteUri,StringComparison.Ordinal);
    public void Dispose() => Directory.Delete(_root,true);
}
