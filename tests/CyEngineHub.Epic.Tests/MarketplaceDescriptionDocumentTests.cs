using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class MarketplaceDescriptionDocumentTests
{
    [Fact]
    public void ImagesAreAltTextOnlyAndMarkdownAutolinksAndCodeLabelsKeepTheirDestinations()
    {
        var image = MarketplaceDescriptionDocument.Parse("<img src='https://example.invalid/image.png' alt='Preview'>");
        Assert.Equal("Preview", Assert.Single(Assert.Single(image.Blocks).Runs).Text);
        var runs = MarketplaceDescriptionDocument.Parse("<https://example.invalid/docs> and [`API`](https://example.invalid/api)").Blocks.SelectMany(block => block.Runs).ToArray();
        Assert.Contains(runs, run => run.Text == "https://example.invalid/docs" && run.Link?.AbsoluteUri == "https://example.invalid/docs");
        Assert.Contains(runs, run => run.Text == "API" && run.Code && run.Link?.AbsoluteUri == "https://example.invalid/api");
    }
    [Fact]
    public void HtmlKeepsHeadingsInlineStylesAndNamedLinksWithoutPrintingDestinations()
    {
        var document = MarketplaceDescriptionDocument.Parse("<h2>Features</h2><p><strong>Bold</strong> and <em>italic</em>. <a href='https://example.invalid/docs'><strong>Documentation</strong></a></p><ul><li>Meshes</li><li>Materials</li></ul>");
        Assert.Equal(2, document.Blocks[0].Heading);
        var runs = document.Blocks.SelectMany(block => block.Runs).ToArray();
        Assert.Contains(runs, run => run.Text == "Bold" && run.Bold);
        Assert.Contains(runs, run => run.Text == "italic" && run.Italic);
        var link = Assert.Single(runs, run => run.Link is not null);
        Assert.Equal("Documentation", link.Text); Assert.True(link.Bold);
        Assert.Equal("https://example.invalid/docs", link.Link!.AbsoluteUri);
        Assert.Equal(2, document.Blocks.Count(block => block.Marker == "•"));
        Assert.DoesNotContain(runs, run => run.Text.Contains("https://", StringComparison.Ordinal));
    }
    [Fact]
    public void MarkdownSupportsHeadingsEmphasisListsQuotesRulesAndCodeWithoutFetchingImages()
    {
        var document = MarketplaceDescriptionDocument.Parse("# Heading\n\n**Bold** and *italic*, ***both***, `source_code`.\n\n1. First\n2. Second\n\n> Quote\n\n---\n\n![Alt text](https://example.invalid/image.png)\n\n```cpp\nhttps://example.invalid/not-a-link\n```");
        Assert.Equal(1, document.Blocks[0].Heading);
        var runs = document.Blocks.SelectMany(block => block.Runs).ToArray();
        Assert.Contains(runs, run => run.Text == "both" && run.Bold && run.Italic);
        Assert.Contains(runs, run => run.Text == "source_code" && run.Code && run.Link is null);
        Assert.Contains(document.Blocks, block => block.Marker == "2.");
        Assert.Contains(document.Blocks, block => block.Quote);
        Assert.Contains(document.Blocks, block => block.Separator);
        Assert.Contains(document.Blocks, block => block.Code && block.Runs.All(run => run.Link is null));
        Assert.Contains(runs, run => run.Text == "Alt text" && run.Link is null);
    }
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/private")]
    [InlineData("data:text/html,hello")]
    [InlineData("shell:AppsFolder")]
    [InlineData("ftp://example.invalid/file")]
    [InlineData("https://user:password@example.invalid/docs")]
    public void UntrustedDescriptionsCannotCreateExecutableOrCredentialBearingLinks(string target)
    {
        foreach (var source in new[] { $"<a href='{target}'>Label</a>", $"[Label]({target})" })
        {
            var runs = MarketplaceDescriptionDocument.Parse(source).Blocks.SelectMany(block => block.Runs).ToArray();
            Assert.Contains(runs, run => run.Text == "Label");
            Assert.All(runs, run => Assert.Null(run.Link));
        }
    }
    [Fact]
    public void PlainLegacyCacheUrlsAndBalancedMarkdownUrlParenthesesRemainClickable()
    {
        foreach (var source in new[] { "Docs (https://example.invalid/Function_(x)).", "[Docs](https://example.invalid/Function_(x))" })
        {
            var link = Assert.Single(MarketplaceDescriptionDocument.Parse(source).Blocks.SelectMany(block => block.Runs), run => run.Link is not null);
            Assert.Equal("https://example.invalid/Function_(x)", link.Link!.AbsoluteUri);
        }
    }
    [Fact]
    public void EntityEncodedCacheMarkupIsFormattedAndHiddenContentIsNeverRendered()
    {
        var document = MarketplaceDescriptionDocument.Parse("&lt;p&gt;&lt;strong&gt;Éclairage &amp;amp; matériaux&lt;/strong&gt;&lt;/p&gt;<script>invisible()</script>");
        Assert.Contains(document.Blocks.SelectMany(block => block.Runs), run => run.Bold && run.Text == "Éclairage & matériaux");
        Assert.DoesNotContain(document.Blocks.SelectMany(block => block.Runs), run => run.Text.Contains("invisible", StringComparison.Ordinal));
    }
    [Fact]
    public void NewCacheRecordsPreserveOriginalFormattingWhileOldRecordsRemainReadable()
    {
        const string html = "<p><strong>Saved heading</strong> <a href='https://example.invalid/docs'>Docs</a></p>";
        var asset = new RemoteFabAsset("fixture", "fab", "Fixture", MarketplaceRichText.ToPlainText(html), "Fab", "assetPack", null, null, [], []) { RichDescription = html };
        var restored = JsonSerializer.Deserialize<RemoteFabAsset>(JsonSerializer.Serialize(asset))!;
        Assert.Equal(html, restored.RichDescription);
        Assert.Contains(MarketplaceDescriptionDocument.Parse(restored.RichDescription).Blocks.SelectMany(block => block.Runs), run => run.Bold);
        Assert.Contains(MarketplaceDescriptionDocument.Parse(asset.Description).Blocks.SelectMany(block => block.Runs), run => run.Link is not null);
    }
}
