using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.ContextMenus;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia;

/// <summary>Native thumbnail picker for Table Design's table-style catalog.</summary>
internal static class TableStylesGallery
{
    public static Control Build(IRibbonCommandRegistry registry)
    {
        var button = new Button { Padding = new Thickness(4, 2), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        ToolTip.SetTip(button, "Table Styles");
        AutomationProperties.SetName(button, "Table Styles");
        button.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { Thumb(DocumentTableStyle.Catalog[0]), new TextBlock { Text = "Table\nStyles", FontSize = 10, Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } },
        };
        var flyout = new MenuFlyout();
        for (var index = 0; index < DocumentTableStyle.Catalog.Count; index++)
            flyout.Items.Add(Item(DocumentTableStyle.Catalog[index], index, registry));
        button.Click += (_, _) => flyout.ShowAt(button);
        return button;
    }

    private static MenuItem Item(DocumentTableStyle style, int index, IRibbonCommandRegistry registry)
    {
        var id = new RibbonCommandId(FreeWContextMenuPlanner.TableStylesPrefix + index);
        var item = new MenuItem { Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { Thumb(style), new TextBlock { Text = style.Name, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center } } } };
        AutomationProperties.SetName(item, style.Name);
        item.PointerEntered += (_, _) => Preview(id, registry, command => command.BeginPreview(RibbonCommandContext.Empty));
        item.PointerExited += (_, _) => Preview(id, registry, command => command.CancelPreview());
        item.Click += (_, _) => { if (registry.TryGet(id, out var command) && command is not null) command.Execute(RibbonCommandContext.Empty); };
        return item;
    }

    private static void Preview(RibbonCommandId id, IRibbonCommandRegistry registry, Action<IRibbonPreviewCommand> action)
    {
        if (registry.TryGet(id, out var command) && command is IRibbonPreviewCommand preview) action(preview);
    }

    private static Control Thumb(DocumentTableStyle style)
    {
        var grid = new Grid { Width = 42, Height = 22 };
        for (var i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Star)); grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        var border = Brush(style.BorderColorHex);
        for (var column = 0; column < 3; column++) for (var row = 0; row < 2; row++)
        {
            var cell = new Border { Background = Brush(row == 0 ? style.HeaderBand?.FillHex : style.BandedRowOdd?.FillHex, Brushes.White), BorderBrush = border, BorderThickness = new Thickness(.5) };
            Grid.SetColumn(cell, column); Grid.SetRow(cell, row); grid.Children.Add(cell);
        }
        return grid;
    }

    private static IBrush Brush(string? hex, IBrush? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex.StartsWith('#') ? hex : "#" + hex, out var color)) return new SolidColorBrush(color);
        return fallback ?? new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
    }
}
