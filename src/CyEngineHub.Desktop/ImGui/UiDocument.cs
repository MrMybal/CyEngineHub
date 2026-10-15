using System.Globalization;
using System.Xml.Linq;

namespace CyEngineHub.Desktop.ImGui;

internal sealed class UiNode
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public required IReadOnlyDictionary<string, string> Attributes { get; init; }
    public required UiNode[] Children { get; init; }
    public string? this[string name] => Attributes.GetValueOrDefault(name);
    public bool IsModal => this["Panel.ZIndex"] is not null && this["IsVisible"] is not null;
    public IEnumerable<UiNode> Descendants() => Children.SelectMany(child => new[] { child }.Concat(child.Descendants()));
    public float Number(string name, float fallback) => float.TryParse(this[name], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

internal sealed class UiDocument
{
    public UiNode Root { get; }
    public UiNode[] Modals { get; }
    public UiNode[] Pages { get; }

    public UiDocument(Stream stream)
    {
        var root = XElement.Load(stream);
        var serial = 0;
        var prefix = root.Attribute("Name")?.Value ?? "ui";
        static IReadOnlyDictionary<string, string> Attributes(XElement element)
        {
            var attributes = element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration)
                .GroupBy(attribute => attribute.Name.LocalName).ToDictionary(group => group.Key, group => group.First().Value);
            if (element.Elements().Where(child => child.Name.LocalName.EndsWith(".ItemsPanel", StringComparison.Ordinal))
                .Any(panel => panel.Descendants().Any(child => child.Name.LocalName == "WrapPanel"))) attributes["Flow"] = "Wrap";
            return attributes;
        }
        UiNode Read(XElement element) => new()
        {
            Id = prefix + "/" + (serial++).ToString(CultureInfo.InvariantCulture),
            Kind = element.Name.LocalName,
            Attributes = Attributes(element),
            Children = element.Elements().Where(child => !child.Name.LocalName.EndsWith(".Styles", StringComparison.Ordinal)
                && !child.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)
                && !child.Name.LocalName.EndsWith(".Effect", StringComparison.Ordinal)
                && !child.Name.LocalName.EndsWith(".ItemsPanel", StringComparison.Ordinal)
                && !child.Name.LocalName.EndsWith(".RenderTransform", StringComparison.Ordinal)).Select(Read).ToArray()
        };
        Root = Read(root);
        Modals = Root.Descendants().Where(node => node.IsModal).OrderBy(node => node.Number("Panel.ZIndex", 0)).ToArray();
        Pages = Root.Descendants().Where(node => node["Grid.Row"] == "1" && node.Children.Count(child => child["IsVisible"] is not null) >= 12)
            .SelectMany(node => node.Children).ToArray();
    }

    public static UiDocument Load(string name)
    {
        using var stream = typeof(UiDocument).Assembly.GetManifestResourceStream($"CyEngineHub.Desktop.ImGui.Layouts.{name}.xml")
            ?? throw new InvalidOperationException($"Interface manquante : {name}");
        return new UiDocument(stream);
    }
}
