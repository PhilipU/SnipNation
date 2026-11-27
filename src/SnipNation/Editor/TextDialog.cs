using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using SnipNation.Branding;

namespace SnipNation.Editor;

internal static class TextDialog
{
    internal static string? Edit(Window owner, string text)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = "Text",
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Height,
            Width = 380,
            Icon = owner.Icon,
            Background = owner.Background,
            Foreground = owner.Foreground,
            FontFamily = owner.FontFamily,
            FontSize = owner.FontSize
        };
        var input = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            MinLines = 4,
            MaxLines = 6,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AutomationProperties.SetName(input, "Text");

        var style = (Style)owner.FindResource("EditorIconButton");
        var accept = new Button
        {
            Content = SvgIconExtension.GetIcon("apply"), ToolTip = "Apply text", Style = style, IsDefault = true
        };
        accept.Click += (_, _) => dialog.DialogResult = true;
        var cancel = new Button
        {
            Content = SvgIconExtension.GetIcon("cancel"), ToolTip = "Cancel", Style = style, IsCancel = true
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(input);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.SourceInitialized += (_, _) => Brand.ApplyCaption(dialog);
        dialog.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return dialog.ShowDialog() == true ? input.Text.TrimEnd() : null;
    }
}
