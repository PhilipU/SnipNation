using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace SnipNation.Editor;

[MarkupExtensionReturnType(typeof(Geometry))]
public sealed class SvgIconExtension(string name) : MarkupExtension
{
    private static readonly IReadOnlyDictionary<string, Geometry> Icons = LoadIcons();

    public override object ProvideValue(IServiceProvider serviceProvider) => GetIcon(name);

    internal static Geometry GetIcon(string name) => Icons[name];

    private static Dictionary<string, Geometry> LoadIcons()
    {
        var uri = new Uri("/SnipNation;component/Assets/EditorIcons.svg", UriKind.Relative);
        using var stream = Application.GetResourceStream(uri).Stream;
        XNamespace svg = "http://www.w3.org/2000/svg";
        var icons = new Dictionary<string, Geometry>(StringComparer.Ordinal);
        foreach (var symbol in XDocument.Load(stream).Root!.Elements(svg + "symbol"))
        {
            var geometry = Geometry.Parse(symbol.Element(svg + "path")!.Attribute("d")!.Value);
            geometry.Freeze();
            icons.Add(symbol.Attribute("id")!.Value, geometry);
        }
        return icons;
    }
}
