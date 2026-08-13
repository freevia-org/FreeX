using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation;
using FreeW.App.Presentation.ContextMenus;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// Word-style Table Styles gallery for the Table Design contextual tab. Builds a button that opens a
/// <see cref="ContextMenu"/> dropdown of catalog table-style entries; hovering a menu item live-previews
/// the style on the caret's table via <see cref="DocumentView.PreviewTableStyle"/>; leaving reverts via
/// <see cref="DocumentView.EndTableStylePreview"/>; clicking commits via <see cref="DocumentView.ApplyTableStyle"/>.
/// Reuses the same hover/preview/commit idiom as <see cref="ThemeGallery"/> and
/// <see cref="StylesGallery"/>. Hosted as app-side custom content — no shared RibbonGallery render needed.
/// </summary>
internal static class TableStylesGallery
{
    /// <summary>
    /// Build the Table Styles gallery widget for the Table Design contextual tab's "Table Styles" group.
    /// Returns a <see cref="Button"/> that opens a context menu of style thumbnails on click.
    /// </summary>
    public static FrameworkElement Build(DocumentView editor)
    {
        var button = new Button
        {
            Margin = new Thickness(4, 2, 4, 2),
            Padding = new Thickness(6, 3, 6, 3),
            MinWidth = 100,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = FreeWUiTextCatalog.TableStyles
        };
        AutomationProperties.SetName(button, FreeWUiTextCatalog.TableStyles);

        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(BuildMiniTableIcon());
        stack.Children.Add(new TextBlock
        {
            Text = FreeWUiTextCatalog.TableStylesCompact,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        });
        button.Content = stack;

        var hover = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
        button.MouseEnter += (_, _) =>
        {
            button.Background = hover;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.ContextMenu is null || !button.ContextMenu.IsOpen)
            {
                button.Background = Brushes.Transparent;
                button.BorderBrush = Brushes.Transparent;
            }
        };

        var menu = BuildMenu(editor, button);
        menu.Closed += (_, _) =>
        {
            editor.EndTableStylePreview();
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        };

        return button;
    }

    private static ContextMenu BuildMenu(DocumentView editor, FrameworkElement anchor)
    {
        var menu = new ContextMenu();
        foreach (var planned in FreeWContextMenuPlanner.BuildTableStyles().Items)
        {
            if (planned.CommandId is not { } commandId
                || !FreeWContextMenuPlanner.TryParseIndex(commandId, FreeWContextMenuPlanner.TableStylesPrefix, out var index)
                || index >= DocumentTableStyle.Catalog.Count)
                continue;
            var style = DocumentTableStyle.Catalog[index];
            var item = new MenuItem
            {
                Header = BuildStyleMenuItem(style),
                Tag = style,
                IsEnabled = planned.IsEnabled,
            };
            AutomationProperties.SetName(item, FreeWUiTextCatalog.TableStyleAutomationName(style.Name));
            item.MouseEnter += (_, _) => editor.PreviewTableStyle(style);
            item.MouseLeave += (_, _) => editor.EndTableStylePreview();
            item.Click += (_, _) =>
            {
                editor.EndTableStylePreview();
                editor.ApplyTableStyle(style);
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    // Build the visual content for one menu item: a mini table thumbnail (3x2 grid of colored cells)
    // over the style name, mirroring the ThemeGallery swatch approach.
    private static FrameworkElement BuildStyleMenuItem(DocumentTableStyle style)
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 8, 1) };

        // Mini table thumbnail: 3 columns x 2 rows showing the style's header + body row fills.
        var thumb = BuildThumb(style);
        host.Children.Add(thumb);
        host.Children.Add(new TextBlock
        {
            Text = style.Name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        });

        return host;
    }

    // 3-column x 2-row mini table: top row uses the header fill; body row uses the odd-band fill.
    private static FrameworkElement BuildThumb(DocumentTableStyle style)
    {
        var grid = new Grid { Width = 42, Height = 22 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var headerFill = style.HeaderBand?.FillHex;
        var bodyFill = style.BandedRowOdd?.FillHex;
        var borderColor = style.Borders
            ? (style.BorderColorHex is { Length: > 0 } hex ? BrushFor("#" + hex) : new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)))
            : Brushes.LightGray;

        for (var col = 0; col < 3; col++)
        {
            for (var row = 0; row < 2; row++)
            {
                var fill = row == 0 ? headerFill : bodyFill;
                var cell = new Border
                {
                    Background = fill is { Length: > 0 } ? BrushFor("#" + fill) : Brushes.White,
                    BorderBrush = borderColor,
                    BorderThickness = new Thickness(0.5),
                    SnapsToDevicePixels = true
                };
                Grid.SetColumn(cell, col);
                Grid.SetRow(cell, row);
                grid.Children.Add(cell);
            }
        }

        return new Border
        {
            Child = grid,
            BorderBrush = borderColor,
            BorderThickness = new Thickness(0.5),
            SnapsToDevicePixels = true
        };
    }

    // Small table icon for the gallery button: 2x2 grid of grey cells.
    private static FrameworkElement BuildMiniTableIcon()
    {
        var grid = new Grid { Width = 20, Height = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        var borderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        for (var c = 0; c < 2; c++)
            for (var r = 0; r < 2; r++)
            {
                var cell = new Border
                {
                    Background = r == 0 ? new SolidColorBrush(Color.FromRgb(0xD9, 0xE2, 0xF3)) : Brushes.White,
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(0.5)
                };
                Grid.SetColumn(cell, c);
                Grid.SetRow(cell, r);
                grid.Children.Add(cell);
            }
        return grid;
    }

    private static Brush BrushFor(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Gray; }
    }
}
