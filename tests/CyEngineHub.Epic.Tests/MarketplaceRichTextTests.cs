using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class MarketplaceRichTextTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    [InlineData("<p><strong>IMPORTANT: AVE II IS NOW AVAILABLE!</strong></p><p>New&nbsp;framework &amp; input.</p>", "IMPORTANT: AVE II IS NOW AVAILABLE!\n\nNew framework & input.")]
    [InlineData("&lt;p&gt;Déjà &amp;amp; bientôt&lt;/p&gt;", "Déjà & bientôt")]
    [InlineData("&amp;lt;p&amp;gt;Été&amp;lt;/p&amp;gt;", "Été")]
    [InlineData("<h2>Features</h2><ul><li>Meshes</li><li>Textures<br />Materials</li></ul>", "Features\n\n• Meshes\n• Textures\nMaterials")]
    [InlineData("<ol><li>Install</li><li>Open</li></ol>", "1. Install\n2. Open")]
    [InlineData("<a target='_blank' href='https://example.invalid/docs?a=1&amp;b=2' rel='noopener'>Documentation</a>", "Documentation (https://example.invalid/docs?a=1&b=2)")]
    [InlineData("<a title='A > B' href='https://example.invalid/docs'><strong>Read more</strong></a>", "Read more (https://example.invalid/docs)")]
    [InlineData("<a href='https://example.invalid/docs'>https://example.invalid/docs</a>", "https://example.invalid/docs")]
    [InlineData("<a href='/listings/asset'>Pack</a>", "Pack (https://www.fab.com/listings/asset)")]
    [InlineData("<a href='javascript:alert(1)'>Safe label</a>", "Safe label")]
    [InlineData("<a href='file:///C:/private'>Label</a>", "Label")]
    [InlineData("<a href='https://user:password@example.invalid/a'>Label</a>", "Label")]
    [InlineData("<p>Hello</p><script>doBadThings()</script><style>body{color:red}</style><!-- ignore --><p>World</p>", "Hello\n\nWorld")]
    [InlineData("<p>First</p><iframe src='https://example.invalid'>hidden</iframe><p>Second</p>", "First\n\nSecond")]
    [InlineData("C++ TArray<FVector> : 2 < 3 et 4 > 1\nC:\\new\\test", "C++ TArray<FVector> : 2 < 3 et 4 > 1\nC:\\new\\test")]
    [InlineData("<table><tr><td>Texture</td><td>4K</td></tr><tr><td>LOD</td><td>3</td></tr></table>", "Texture 4K\nLOD 3")]
    [InlineData("<P class='note'>Cr&#233;ation &#x1F680;</P><BR/>Test", "Création 🚀\n\nTest")]
    [InlineData("<p><img src='https://example.invalid/image' alt='Preview'></p>", "Preview")]
    [InlineData("   <a href='https://example.invalid/docs'><p></p></a>", "(https://example.invalid/docs)")]
    public void ConvertsMarketplaceMarkupWithoutExecutingOrLosingUsefulContent(string? input, string expected)
    {
        var actual = MarketplaceRichText.ToPlainText(input);
        Assert.Equal(expected, actual);
        Assert.Equal(actual, MarketplaceRichText.ToPlainText(actual));
    }
}
