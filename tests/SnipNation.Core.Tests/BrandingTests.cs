using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace SnipNation.Core.Tests;

public sealed class BrandingTests
{
    [Fact]
    public void AppPaletteMatchesTheBrandKit()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "branding");
        using var kit = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "palette.json")));
        var appPalette = XDocument.Load(Path.Combine(folder, "Palette.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var colors = appPalette.Root!.Elements(wpf + "Color").ToDictionary(
            element => (string)element.Attribute(xaml + "Key")!,
            element => element.Value);

        var expected = new Dictionary<string, string>
        {
            ["background"] = "BrandBackground",
            ["accent"] = "BrandAccent",
            ["foreground"] = "BrandForeground",
            ["muted"] = "BrandMuted",
            ["lightBackground"] = "BrandLightBackground",
            ["lightAccent"] = "BrandLightAccent",
            ["lightForeground"] = "BrandBackground",
            ["lightMuted"] = "BrandLightMuted"
        };

        Assert.Equal(expected.Values.Distinct().Order(), colors.Keys.Order());
        foreach (var (kitRole, resource) in expected)
            Assert.Equal(kit.RootElement.GetProperty(kitRole).GetString(), colors[resource], ignoreCase: true);
    }
}
