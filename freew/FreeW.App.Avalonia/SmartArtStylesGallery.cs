using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Native SmartArt color and style thumbnail strip backed by shared preview commands.</summary>
internal static class SmartArtStylesGallery
{
    public static Control BuildLayouts(IRibbonCommandRegistry registry)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 2, 0) };
        foreach (var layout in SmartArtLayoutPreset.Catalog) host.Children.Add(LayoutButton(layout, registry));
        return host;
    }

    public static Control Build(IRibbonCommandRegistry registry)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 1, 2, 0) };
        foreach (var scheme in SmartArtColorScheme.Catalog) host.Children.Add(ColorButton(scheme, registry));
        host.Children.Add(new Border { Width = 1, Height = 48, Background = Brush("#D0D0D0"), Margin = new Thickness(2, 0) });
        foreach (var style in SmartArtStyle.Catalog) host.Children.Add(StyleButton(style, registry));
        return host;
    }

    private static Button ColorButton(SmartArtColorScheme scheme, IRibbonCommandRegistry registry)
    {
        var colors = new StackPanel { Orientation = Orientation.Horizontal, Width = 36, Height = 18, Margin = new Thickness(2, 5, 2, 0) };
        foreach (var hex in new[] { scheme.Color1Hex, scheme.Color2Hex, scheme.Color3Hex, scheme.Color4Hex }) colors.Children.Add(new Border { Width = 9, Height = 18, Background = Brush(hex), BorderBrush = Brushes.White, BorderThickness = new Thickness(.5) });
        return Button(new StackPanel { Width = 40, Children = { colors, Label(scheme.Name) } }, scheme.Name + " colors", new RibbonCommandId($"freew.smartart-colors-{scheme.Id}"), registry);
    }

    private static Button StyleButton(SmartArtStyle style, IRibbonCommandRegistry registry)
    {
        var fill = Adjust(Color.FromRgb(0x4E, 0x81, 0xBD), style.BrightnessAdjust);
        var node = new Border { Width = 36, Height = 28, Margin = new Thickness(2, 2, 2, 0), Background = new SolidColorBrush(fill), BorderBrush = Brush("#3B628F"), BorderThickness = new Thickness(Math.Max(style.BorderThickness, 1)), CornerRadius = new CornerRadius(style.CornerRadius), Child = new TextBlock { Text = "A", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        return Button(new StackPanel { Width = 42, Children = { node, Label(style.Name) } }, style.Name + " style", SmartArtCommandPlanner.StyleCommandId(style), registry);
    }

    private static Button LayoutButton(SmartArtLayoutPreset layout, IRibbonCommandRegistry registry)
    {
        var preview = new Border { Width = 46, Height = 30, Margin = new Thickness(2, 2, 2, 0), Background = Brushes.White, BorderBrush = Brush("#C0C0C0"), BorderThickness = new Thickness(1), Child = LayoutMiniature(layout.Id) };
        return Button(new StackPanel { Width = 50, Children = { preview, Label(layout.Name) } }, layout.Name, new RibbonCommandId($"freew.smartart-layout-{layout.Id}"), registry);
    }

    private static Control LayoutMiniature(string id)
    {
        Border Node(string color, double width = 10, double height = 8) => new() { Width = width, Height = height, Margin = new Thickness(1), Background = Brush(color) };
        if (id is "hierarchy1" or "orgchart1") return new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { Node("#4E81BD", 24), new StackPanel { Orientation = Orientation.Horizontal, Children = { Node("#C0504D"), Node("#9BBB59") } } } };
        if (id == "cycle1" || id == "radial1") return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { Node("#4E81BD"), Node("#C0504D"), Node("#9BBB59") } };
        if (id == "matrix1")
        {
            var matrix = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Star) }, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            var colors = new[] { "#4E81BD", "#C0504D", "#9BBB59", "#8064A2" };
            for (var index = 0; index < colors.Length; index++)
            {
                var node = Node(colors[index]);
                Grid.SetRow(node, index / 2); Grid.SetColumn(node, index % 2); matrix.Children.Add(node);
            }
            return matrix;
        }
        return new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Children = { Node("#4E81BD"), Node("#C0504D"), Node("#9BBB59") } };
    }

    private static TextBlock Label(string value) => new() { Text = value, FontSize = 8, TextAlignment = global::Avalonia.Media.TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private static Button Button(Control content, string name, RibbonCommandId id, IRibbonCommandRegistry registry)
    {
        var button = new Button { Content = content, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), Padding = new Thickness(1) };
        ToolTip.SetTip(button, name); AutomationProperties.SetName(button, name);
        button.PointerEntered += (_, _) => { button.Background = Brush("#EAF1FB"); button.BorderBrush = Brush("#2B579A"); Preview(id, registry, command => command.BeginPreview(RibbonCommandContext.Empty)); };
        button.PointerExited += (_, _) => { button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; Preview(id, registry, command => command.CancelPreview()); };
        button.Click += (_, _) => { if (registry.TryGet(id, out var command) && command is not null) command.Execute(RibbonCommandContext.Empty); };
        return button;
    }
    private static void Preview(RibbonCommandId id, IRibbonCommandRegistry registry, Action<IRibbonPreviewCommand> action) { if (registry.TryGet(id, out var command) && command is IRibbonPreviewCommand preview) action(preview); }
    private static Color Adjust(Color color, double amount) { var delta = amount * 255; return Color.FromRgb(Clamp(color.R + delta), Clamp(color.G + delta), Clamp(color.B + delta)); }
    private static byte Clamp(double value) => (byte)Math.Clamp(value, 0, 255);
    private static IBrush Brush(string hex) => Color.TryParse(hex.StartsWith('#') ? hex : "#" + hex, out var color) ? new SolidColorBrush(color) : Brushes.Black;
}
