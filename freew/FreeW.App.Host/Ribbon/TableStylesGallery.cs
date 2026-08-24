using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation;
using FreeW.App.Presentation.ContextMenus;
using FreeW.Core.Model;
using Free.Shared.Ribbon;

namespace FreeW.App.Host;

/// <summary>
/// Word-style Table Styles gallery for the Table Design contextual tab. It exposes a compact strip of
/// direct preview tiles and a <see cref="ContextMenu"/> with the complete catalog. Native pointer/menu events invoke
/// Presentation-owned <see cref="IRibbonPreviewCommand"/> instances for preview, cancellation, and commit.
/// Reuses the same hover/preview/commit idiom as <see cref="ThemeGallery"/> and
/// <see cref="StylesGallery"/>. Hosted as app-side custom content — no shared RibbonGallery render needed.
/// </summary>
internal static class TableStylesGallery
{
    /// <summary>
    /// Build the Table Styles gallery widget for the Table Design contextual tab's "Table Styles" group.
    /// The first three catalog styles remain directly available, with all styles under More.
    /// </summary>
    public static FrameworkElement Build(DocumentView editor)
        => Build(editor, registry: null);

    /// <summary>Builds the native WPF gallery over Presentation-owned preview commands.</summary>
    public static FrameworkElement Build(DocumentView editor, IRibbonCommandRegistry? registry)
    {
        var root = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 1, 2, 0),
        };
        AutomationProperties.SetName(root, FreeWUiTextCatalog.TableStyles);
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var index in Enumerable.Range(0, Math.Min(3, DocumentTableStyle.Catalog.Count)))
            swatches.Children.Add(BuildStyleButton(DocumentTableStyle.Catalog[index], index, editor, registry));
        root.Children.Add(new Border
        {
            Height = 52,
            Width = 162,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)),
            BorderThickness = new Thickness(1),
            Child = swatches,
        });

        var button = new Button { Content = "▼", Width = 20, Height = 52, Margin = new Thickness(2, 0, 0, 0) };
        button.ToolTip = "More Table Styles";
        AutomationProperties.SetName(button, "More Table Styles");
        var menu = BuildMenu(editor, registry, out var cancelActivePreview);
        menu.Closed += (_, _) =>
        {
            cancelActivePreview();
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        };
        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        };
        root.Children.Add(button);
        return root;
    }

    private static Button BuildStyleButton(
        DocumentTableStyle style,
        int index,
        DocumentView editor,
        IRibbonCommandRegistry? registry)
    {
        var commandId = new RibbonCommandId(FreeWContextMenuPlanner.TableStylesPrefix + index);
        var button = new Button
        {
            Content = BuildThumb(style, 46, 30),
            Width = 52,
            Height = 50,
            Padding = new Thickness(2),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            ToolTip = style.Name,
        };
        AutomationProperties.SetName(button, FreeWUiTextCatalog.TableStyleAutomationName(style.Name));
        button.MouseEnter += (_, _) =>
        {
            button.Background = new SolidColorBrush(Color.FromRgb(0xEA, 0xF1, 0xFB));
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0x57, 0x9A));
            if (registry?.TryGet(commandId, out var command) == true && command is IRibbonPreviewCommand preview)
                preview.BeginPreview(RibbonCommandContext.Empty);
            else
                editor.PreviewTableStyle(style);
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            if (registry?.TryGet(commandId, out var command) == true && command is IRibbonPreviewCommand preview)
                preview.CancelPreview();
            else
                editor.EndTableStylePreview();
        };
        button.Click += (_, _) =>
        {
            if (registry?.TryGet(commandId, out var command) == true && command is not null)
            {
                command.Execute(RibbonCommandContext.Empty);
                return;
            }

            editor.EndTableStylePreview();
            editor.ApplyTableStyle(style);
        };
        return button;
    }

    private static ContextMenu BuildMenu(
        DocumentView editor,
        IRibbonCommandRegistry? registry,
        out Action cancelActivePreview)
    {
        var menu = new ContextMenu();
        IRibbonPreviewCommand? activePreview = null;
        cancelActivePreview = () =>
        {
            activePreview?.CancelPreview();
            activePreview = null;
        };
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
            IRibbonCommand? command = null;
            if (registry is not null)
                registry.TryGet(commandId, out command);
            var preview = command as IRibbonPreviewCommand;
            item.MouseEnter += (_, _) =>
            {
                if (preview is null)
                {
                    editor.PreviewTableStyle(style);
                    return;
                }

                if (!ReferenceEquals(activePreview, preview))
                    activePreview?.CancelPreview();
                activePreview = preview;
                preview.BeginPreview(RibbonCommandContext.Empty);
            };
            item.MouseLeave += (_, _) =>
            {
                if (preview is null)
                {
                    editor.EndTableStylePreview();
                    return;
                }

                if (ReferenceEquals(activePreview, preview))
                {
                    preview.CancelPreview();
                    activePreview = null;
                }
            };
            item.Click += (_, _) =>
            {
                if (command is null)
                {
                    editor.EndTableStylePreview();
                    editor.ApplyTableStyle(style);
                    return;
                }

                command.Execute(RibbonCommandContext.Empty);
                activePreview = null;
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
    private static FrameworkElement BuildThumb(DocumentTableStyle style, double width = 42, double height = 22)
    {
        var grid = new Grid { Width = width, Height = height };
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

    private static Brush BrushFor(string hex)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.Gray; }
    }
}
