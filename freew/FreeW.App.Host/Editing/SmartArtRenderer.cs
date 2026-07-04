using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Pure-static WPF geometry renderer for SmartArt diagrams. Converts a list of
/// <see cref="SmartArtNode"/> objects into a <see cref="FrameworkElement"/> by dispatching on the
/// resolved layout id. Color-scheme and style presets are applied per-node. Called exclusively by
/// <c>DocumentView.BuildSmartArtRun</c>.
/// </summary>
internal static class SmartArtRenderer
{
    /// <summary>
    /// Build the content element for a SmartArt diagram given its nodes, resolved layout/color/style ids.
    /// Returns a <see cref="FrameworkElement"/> sized to fill the outer Border (which carries the Tag).
    /// </summary>
    public static FrameworkElement Build(
        SmartArt smartArt,
        SmartArtVisualPlan plan,
        double strokeThickness) =>
        Build(smartArt.Nodes, plan, strokeThickness);

    private static FrameworkElement Build(
        IReadOnlyList<SmartArtNode> nodes,
        SmartArtVisualPlan plan,
        double strokeThickness)
    {
        return plan.LayoutId switch
        {
            // ── List layouts ────────────────────────────────────────────────────────────────────────
            "list1" or "vertbullet1" => BuildVerticalList(plan.Nodes, strokeThickness),
            "horizbullet1"           => BuildHorizontalList(plan.Nodes, strokeThickness),

            // ── Process layouts ─────────────────────────────────────────────────────────────────────
            "process1"               => BuildProcess(plan.Nodes, strokeThickness),
            "stepup1"                => BuildStepProcess(plan.Nodes, strokeThickness, ascending: true),
            "stepdown1"              => BuildStepProcess(plan.Nodes, strokeThickness, ascending: false),

            // ── Cycle ────────────────────────────────────────────────────────────────────────────────
            "cycle1"                 => BuildCycle(plan.Nodes, strokeThickness),

            // ── Hierarchy ───────────────────────────────────────────────────────────────────────────
            "hierarchy1" or "orgchart1" => BuildHierarchy(nodes, plan.Nodes, strokeThickness),

            // ── Radial ──────────────────────────────────────────────────────────────────────────────
            "radial1"                => BuildRadial(plan.Nodes, strokeThickness),

            // ── Matrix ──────────────────────────────────────────────────────────────────────────────
            "matrix1"                => BuildMatrix(plan.Nodes, strokeThickness),

            // ── Fallback (unknown layout) ────────────────────────────────────────────────────────────
            _                        => BuildVerticalList(plan.Nodes, strokeThickness)
        };
    }

    // ── Shared node-box factory ──────────────────────────────────────────────────────────────────────

    private static Border MakeNodeBox(
        SmartArtNodeVisualPlan node,
        double strokeThickness,
        Thickness margin,
        Thickness padding,
        double? width = null,
        double? minWidth = null)
    {
        Effect? effect = null;
        if (node.ShadowOpacity > 0)
        {
            effect = new DropShadowEffect
            {
                BlurRadius = node.ShadowBlur,
                ShadowDepth = node.ShadowDepth,
                Opacity = node.ShadowOpacity,
                Color = Colors.Black
            };
        }

        var box = new Border
        {
            Background = new SolidColorBrush(ParseHex(node.FillHex)),
            CornerRadius = new CornerRadius(node.CornerRadius),
            Margin = margin,
            Padding = padding,
            BorderBrush = node.BorderThickness > 0 ? new SolidColorBrush(ParseHex(node.BorderHex)) : null,
            BorderThickness = new Thickness(node.BorderThickness > 0 ? node.BorderThickness : 0),
            Effect = effect,
            Child = new TextBlock
            {
                Text = node.Text,
                Foreground = new SolidColorBrush(ParseHex(node.TextHex)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                TextAlignment = System.Windows.TextAlignment.Center
            }
        };
        if (width.HasValue) box.Width = width.Value;
        if (minWidth.HasValue) box.MinWidth = minWidth.Value;
        return box;
    }

    // ── Layout renderers ─────────────────────────────────────────────────────────────────────────────

    // List layouts: vertical stack of labelled boxes.
    private static FrameworkElement BuildVerticalList(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes,
        double strokeThickness)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6)
        };
        for (var i = 0; i < nodes.Count; i++)
            panel.Children.Add(MakeNodeBox(nodes[i], strokeThickness,
                margin: new Thickness(2),
                padding: new Thickness(8, 4, 8, 4)));
        return panel;
    }

    // Horizontal list (bullet list horizontal).
    private static FrameworkElement BuildHorizontalList(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes,
        double strokeThickness)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6)
        };
        for (var i = 0; i < nodes.Count; i++)
            panel.Children.Add(MakeNodeBox(nodes[i], strokeThickness,
                margin: new Thickness(3, 2, 3, 2),
                padding: new Thickness(8, 4, 8, 4)));
        return panel;
    }

    // Process layout: horizontal boxes connected by chevron arrows.
    private static FrameworkElement BuildProcess(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes,
        double strokeThickness)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6)
        };
        for (var i = 0; i < nodes.Count; i++)
        {
            panel.Children.Add(MakeNodeBox(nodes[i], strokeThickness,
                margin: new Thickness(2),
                padding: new Thickness(8, 5, 8, 5),
                minWidth: 50));

            // Arrow connector between nodes: use a darker/neutral shade so it is visible against any box fill.
            // We darken the current node's fill color rather than repeating it verbatim.
            if (i < nodes.Count - 1)
            {
                panel.Children.Add(MakeArrow(ParseHex(nodes[i].ConnectorHex)));
            }
        }
        return panel;
    }

    private static FrameworkElement MakeArrow(Color fill)
    {
        var arrow = new Polygon
        {
            Points = new PointCollection([new Point(0, 5), new Point(8, 10), new Point(0, 15)]),
            Fill = new SolidColorBrush(fill),
            Stretch = Stretch.Uniform,
            Width = 12,
            Height = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 0)
        };
        return arrow;
    }

    // Step-Up / Step-Down process: staircase ascending or descending.
    private static FrameworkElement BuildStepProcess(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes,
        double strokeThickness,
        bool ascending)
    {
        var canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var n = nodes.Count;
        if (n == 0) return canvas;

        const double boxW = 70;
        const double boxH = 30;
        const double stepX = 60;
        const double stepY = 28;

        for (var i = 0; i < n; i++)
        {
            var x = i * stepX;
            var y = ascending ? (n - 1 - i) * stepY : i * stepY;
            var box = MakeNodeBox(nodes[i], strokeThickness,
                margin: new Thickness(0),
                padding: new Thickness(4, 2, 4, 2),
                width: boxW);
            box.Height = boxH;
            Canvas.SetLeft(box, x);
            Canvas.SetTop(box, y);
            canvas.Children.Add(box);
        }
        return canvas;
    }

    // Cycle layout: nodes arranged in a circle.
    private static FrameworkElement BuildCycle(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes,
        double strokeThickness)
    {
        var canvas = new Canvas
        {
            Width = 200,
            Height = 160,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var n = nodes.Count;
        if (n == 0) return canvas;

        const double rx = 72;
        const double ry = 56;
        const double cx = 100;
        const double cy = 80;
        const double boxW = 52;
        const double boxH = 26;

        for (var i = 0; i < n; i++)
        {
            var angle = 2 * Math.PI * i / n - Math.PI / 2;
            var x = cx + rx * Math.Cos(angle) - boxW / 2;
            var y = cy + ry * Math.Sin(angle) - boxH / 2;
            var box = MakeNodeBox(nodes[i], strokeThickness,
                margin: new Thickness(0),
                padding: new Thickness(3, 2, 3, 2),
                width: boxW);
            box.Height = boxH;
            Canvas.SetLeft(box, x);
            Canvas.SetTop(box, y);
            canvas.Children.Add(box);
        }
        return canvas;
    }

    // Hierarchy layout: tree of parent + indented children.
    private static FrameworkElement BuildHierarchy(
        IReadOnlyList<SmartArtNode> nodes,
        IReadOnlyList<SmartArtNodeVisualPlan> plannedNodes,
        double strokeThickness)
    {
        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6)
        };
        var planIndex = 0;
        foreach (var node in nodes)
        {
            root.Children.Add(MakeNodeBox(plannedNodes[planIndex++], strokeThickness,
                margin: new Thickness(2),
                padding: new Thickness(8, 4, 8, 4)));

            // Render children indented beneath parent (hierarchy-children rendered, not dropped).
            if (node.Children.Count > 0)
            {
                var childPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(20, 0, 2, 4)
                };
                foreach (var child in node.Children)
                {
                    childPanel.Children.Add(MakeNodeBox(plannedNodes[planIndex++], strokeThickness,
                        margin: new Thickness(2),
                        padding: new Thickness(6, 3, 6, 3)));
                }
                root.Children.Add(childPanel);
            }
        }
        return root;
    }

    // Radial layout: central hub + satellite nodes arranged around it.
    private static FrameworkElement BuildRadial(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes,
        double strokeThickness)
    {
        var canvas = new Canvas
        {
            Width = 220,
            Height = 180,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (nodes.Count == 0) return canvas;

        // Central node (first entry).
        const double cx = 110;
        const double cy = 90;
        const double centW = 56;
        const double centH = 36;
        var center = MakeNodeBox(nodes[0], strokeThickness,
            margin: new Thickness(0),
            padding: new Thickness(4, 3, 4, 3),
            width: centW);
        center.Height = centH;
        Canvas.SetLeft(center, cx - centW / 2);
        Canvas.SetTop(center, cy - centH / 2);
        canvas.Children.Add(center);

        // Satellite nodes arranged in a ring.
        var n = nodes.Count - 1;
        if (n <= 0) return canvas;
        const double rx = 76;
        const double ry = 58;
        const double satW = 48;
        const double satH = 24;
        for (var i = 0; i < n; i++)
        {
            var angle = 2 * Math.PI * i / n - Math.PI / 2;
            var x = cx + rx * Math.Cos(angle) - satW / 2;
            var y = cy + ry * Math.Sin(angle) - satH / 2;

            // Connector line to center.
            var line = new Line
            {
                X1 = cx,
                Y1 = cy,
                X2 = x + satW / 2,
                Y2 = y + satH / 2,
                Stroke = new SolidColorBrush(ParseHex(nodes[i + 1].ConnectorHex)),
                StrokeThickness = strokeThickness,
                Opacity = 0.6
            };
            canvas.Children.Add(line);

            var sat = MakeNodeBox(nodes[i + 1], strokeThickness,
                margin: new Thickness(0),
                padding: new Thickness(3, 2, 3, 2),
                width: satW);
            sat.Height = satH;
            Canvas.SetLeft(sat, x);
            Canvas.SetTop(sat, y);
            canvas.Children.Add(sat);
        }
        return canvas;
    }

    // Matrix layout: 2×2 (or n-cell) grid.
    private static FrameworkElement BuildMatrix(
        IReadOnlyList<SmartArtNodeVisualPlan> nodes,
        double strokeThickness)
    {
        var cols = nodes.Count <= 4 ? 2 : (int)Math.Ceiling(Math.Sqrt(nodes.Count));
        var grid = new UniformGrid
        {
            Columns = cols,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6)
        };
        for (var i = 0; i < nodes.Count; i++)
            grid.Children.Add(MakeNodeBox(nodes[i], strokeThickness,
                margin: new Thickness(3),
                padding: new Thickness(6, 4, 6, 4)));
        return grid;
    }

    // ── Color helpers ────────────────────────────────────────────────────────────────────────────────

    private static Color ParseHex(string hex)
    {
        try
        {
            return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return Color.FromRgb(0x4E, 0x81, 0xBD);
        }
    }
}
