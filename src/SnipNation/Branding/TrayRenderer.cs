using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnipNation.Branding;

internal sealed class TrayRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Background = ToDrawingColor("Background");
    private static readonly Color Accent = ToDrawingColor("Accent");
    private static readonly Color Foreground = ToDrawingColor("Foreground");
    private static readonly Color Muted = ToDrawingColor("Muted");

    internal TrayRenderer() : base(new TrayColors())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = TextColor(e.Item);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = TextColor(e.Item);
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var bounds = e.ImageRectangle;
        using var pen = new Pen(Accent, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawLines(pen, new Point[]
        {
            new(bounds.Left + 3, bounds.Top + 7),
            new(bounds.Left + 6, bounds.Top + 10),
            new(bounds.Left + 12, bounds.Top + 4)
        });
    }

    private static Color TextColor(ToolStripItem? item) =>
        item?.Enabled != true ? Muted : item.Selected || item.Pressed ? Background : Foreground;

    private static Color ToDrawingColor(string role) => Color.FromArgb(unchecked((int)Brand.Argb(role)));

    private sealed class TrayColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Muted;
        public override Color MenuItemBorder => Accent;
        public override Color MenuItemSelected => Accent;
        public override Color MenuItemSelectedGradientBegin => Accent;
        public override Color MenuItemSelectedGradientEnd => Accent;
        public override Color MenuItemPressedGradientBegin => Accent;
        public override Color MenuItemPressedGradientMiddle => Accent;
        public override Color MenuItemPressedGradientEnd => Accent;
        public override Color SeparatorDark => Muted;
        public override Color SeparatorLight => Background;
    }
}
