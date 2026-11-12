using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Shell.Wpf;
using FreeW.App.Presentation.Backstage;

namespace FreeW.App.Host.Backstage;

/// <summary>
/// WPF composition for FreeW's File &gt; New template gallery. All decisions -- which templates,
/// their names, the miniature page marks, what a click does -- come from the shared
/// <see cref="FreeWTemplateGallerySpec"/>; this only draws tiles.
/// </summary>
internal static class BackstageTemplateGalleryRenderer
{
    public static UIElement Build(
        BackstageVisualKit kit,
        FreeWTemplateGallerySpec spec,
        double tileWidth,
        double tileHeight)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(spec);

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(kit.HeadingText(spec.Heading));

        foreach (var section in spec.Sections)
        {
            panel.Children.Add(kit.SubHeading(section.Heading));

            if (section.Tiles.Count > 0)
            {
                var tiles = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var tile in section.Tiles)
                    tiles.Children.Add(BuildTile(kit, tile, tileWidth, tileHeight));
                panel.Children.Add(tiles);
            }

            AddNote(panel, kit, section.EmptyText);
            AddNote(panel, kit, section.FooterText);
        }

        return kit.Scroll(panel);
    }

    private static void AddNote(Panel panel, BackstageVisualKit kit, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = kit.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 10),
        });
    }

    private static Button BuildTile(BackstageVisualKit kit, FreeWTemplateTile tile, double width, double height)
    {
        var page = new Border
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            BorderBrush = kit.Muted,
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true,
            Child = BuildMiniature(kit, tile.Thumbnail, width, height),
        };

        var content = new StackPanel { Width = width };
        content.Children.Add(page);
        content.Children.Add(new TextBlock
        {
            Text = tile.Caption,
            Foreground = kit.Heading,
            FontSize = 13,
            Margin = new Thickness(0, 8, 0, 0),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });

        var button = new Button
        {
            Content = content,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4),
            Margin = new Thickness(0, 0, 14, 14),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = tile.Description,
        };
        AutomationProperties.SetAutomationId(button, tile.AutomationId);
        AutomationProperties.SetName(button, tile.Caption);
        AutomationProperties.SetHelpText(button, tile.Description);
        button.Click += (_, _) =>
        {
            try
            {
                tile.Invoke();
            }
            catch (Exception ex)
            {
                // Same containment the shared Backstage rows use: a failed host callback is reported,
                // not allowed to take the process down from a UI event handler.
                Free.Shared.Ribbon.RibbonCommandFaultReporter.Report(ex, tile.AutomationId);
            }
        };
        return button;
    }

    private static Canvas BuildMiniature(
        BackstageVisualKit kit,
        IReadOnlyList<TemplateThumbnailMark> marks,
        double width,
        double height)
    {
        var canvas = new Canvas { Width = width, Height = height, ClipToBounds = true, IsHitTestVisible = false };
        foreach (var mark in marks)
        {
            var x = mark.X * width;
            var y = mark.Y * height;
            var markWidth = Math.Max(1, mark.Width * width);
            var markHeight = Math.Max(1, mark.Height * height);

            if (mark.Kind == TemplateThumbnailMarkKind.Table)
            {
                AddTable(canvas, kit, mark, x, y, markWidth, markHeight);
                continue;
            }

            var shape = new Rectangle
            {
                Width = markWidth,
                Height = markHeight,
                Fill = mark.Kind is TemplateThumbnailMarkKind.Heading or TemplateThumbnailMarkKind.Rule
                    ? kit.Link
                    : mark.Kind == TemplateThumbnailMarkKind.Title ? kit.Heading : kit.Muted,
                Opacity = mark.Kind switch
                {
                    TemplateThumbnailMarkKind.Text => 0.45,
                    TemplateThumbnailMarkKind.Title => 0.8,
                    _ => 0.85,
                },
            };
            Canvas.SetLeft(shape, x);
            Canvas.SetTop(shape, y);
            canvas.Children.Add(shape);
        }

        return canvas;
    }

    private static void AddTable(
        Canvas canvas,
        BackstageVisualKit kit,
        TemplateThumbnailMark mark,
        double x,
        double y,
        double width,
        double height)
    {
        var outline = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = kit.Muted,
            StrokeThickness = 0.75,
            Opacity = 0.7,
        };
        Canvas.SetLeft(outline, x);
        Canvas.SetTop(outline, y);
        canvas.Children.Add(outline);

        for (var row = 1; row < mark.Rows; row++)
            AddLine(canvas, kit, x, y + height * row / mark.Rows, x + width, y + height * row / mark.Rows);
        for (var column = 1; column < mark.Columns; column++)
            AddLine(canvas, kit, x + width * column / mark.Columns, y, x + width * column / mark.Columns, y + height);
    }

    private static void AddLine(Canvas canvas, BackstageVisualKit kit, double x1, double y1, double x2, double y2) =>
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = kit.Muted,
            StrokeThickness = 0.5,
            Opacity = 0.7,
        });
}
