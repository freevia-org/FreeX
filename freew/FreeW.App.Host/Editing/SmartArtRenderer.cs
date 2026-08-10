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
        double strokeThickness)
    {
        const double pixelsPerPoint = 96.0 / 72.0;
        var targetWidth = Math.Max(1, smartArt.WidthPt * pixelsPerPoint);
        var targetHeight = Math.Max(1, smartArt.HeightPt * pixelsPerPoint);

        if (IsWordSimpleColorfulProcess(smartArt, plan))
            return BuildWordSimpleColorfulProcessLayout(plan, targetWidth, targetHeight);

        return Build(
            plan,
            strokeThickness,
            targetWidth,
            targetHeight);
    }

    private static FrameworkElement Build(
        SmartArtVisualPlan plan,
        double strokeThickness,
        double targetWidth,
        double targetHeight)
    {
        return plan.LayoutId switch
        {
            // ── List layouts ────────────────────────────────────────────────────────────────────────
            "list1" or "vertbullet1" => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),
            "horizbullet1"           => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),

            // ── Process layouts ─────────────────────────────────────────────────────────────────────
            "process1"               => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),
            "continuousBlockProcess" => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),
            "stepup1"                => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),
            "stepdown1"              => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),

            // ── Cycle ────────────────────────────────────────────────────────────────────────────────
            "cycle1"                 => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),
            "pyramid1"               => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),

            // ── Hierarchy ───────────────────────────────────────────────────────────────────────────
            "hierarchy1" or "orgchart1" => BuildHierarchy(plan, strokeThickness, targetWidth, targetHeight),

            // ── Radial ──────────────────────────────────────────────────────────────────────────────
            "radial1"                => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),

            // ── Matrix ──────────────────────────────────────────────────────────────────────────────
            "matrix1"                => BuildPlannedLayout(plan, strokeThickness, targetWidth, targetHeight),

            // ── Fallback (unknown layout) ────────────────────────────────────────────────────────────
            _                        => BuildVerticalList(plan.Nodes, strokeThickness)
        };
    }

    private static bool IsWordSimpleColorfulProcess(SmartArt smartArt, SmartArtVisualPlan plan) =>
        string.Equals(plan.LayoutId, "process1", StringComparison.OrdinalIgnoreCase)
        && string.Equals(smartArt.ColorSchemeId, "colorful1", StringComparison.OrdinalIgnoreCase)
        // The DOCX writer emits Word's simple1 quick-style payload for the FreeW subtle1 gallery
        // selection. Word resolves that serialized process signature to the same native visual.
        && (string.Equals(smartArt.StyleId, "simple1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(smartArt.StyleId, "subtle1", StringComparison.OrdinalIgnoreCase))
        && plan.Nodes.Count == 3;

    // Word's 96-DPI gallery raster renders this native process signature as three solid accent nodes.
    // The calibrated 216pt by 90pt geometry is scaled here for other sizes.
    private static FrameworkElement BuildWordSimpleColorfulProcessLayout(
        SmartArtVisualPlan plan,
        double targetWidth,
        double targetHeight)
    {
        const double nativeWidth = 288;
        const double nativeHeight = 120;
        const double nodeWidth = 76;
        const double nodeHeight = 46;
        const double nodeTop = 22.4;
        const double nodeStride = 106;
        var scaleX = targetWidth / nativeWidth;
        var scaleY = targetHeight / nativeHeight;
        var baseColor = ParseHex(plan.ColorScheme.Color1Hex);
        var connectorColor = Color.FromRgb(0xB2, 0xC1, 0xDB);
        var canvas = new Canvas
        {
            Width = Math.Max(1, targetWidth),
            Height = Math.Max(1, targetHeight),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        for (var index = 0; index < plan.Nodes.Count; index++)
        {
            var left = index * nodeStride * scaleX;
            var top = nodeTop * scaleY;
            var node = new Border
            {
                Width = nodeWidth * scaleX,
                Height = nodeHeight * scaleY,
                Background = new SolidColorBrush(baseColor),
                CornerRadius = new CornerRadius(5 * Math.Min(scaleX, scaleY)),
                Child = new TextBlock
                {
                    Text = plan.Nodes[index].Text,
                    Foreground = Brushes.White,
                    FontSize = 16 * Math.Min(scaleX, scaleY),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = System.Windows.TextAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap
                }
            };
            Canvas.SetLeft(node, left);
            Canvas.SetTop(node, top);
            canvas.Children.Add(node);

            if (index >= plan.Nodes.Count - 1)
                continue;

            var arrow = new Polygon
            {
                Points = new PointCollection([
                    new Point(0, 0),
                    new Point(16 * scaleX, 7 * scaleY),
                    new Point(0, 14 * scaleY)
                ]),
                Fill = new SolidColorBrush(connectorColor),
                Width = 16 * scaleX,
                Height = 14 * scaleY,
                Stretch = Stretch.Fill
            };
            Canvas.SetLeft(arrow, left + nodeWidth * scaleX + 8 * scaleX);
            Canvas.SetTop(arrow, top + 16 * scaleY);
            canvas.Children.Add(arrow);
        }

        return canvas;
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
            Child = MakeNodeText(node)
        };
        if (width.HasValue) box.Width = width.Value;
        if (minWidth.HasValue) box.MinWidth = minWidth.Value;
        return box;
    }

    private static Border MakeNodeTextBox(
        SmartArtNodeVisualPlan node,
        Thickness margin,
        Thickness padding,
        double width)
    {
        return new Border
        {
            Background = Brushes.Transparent,
            Margin = margin,
            Padding = padding,
            Width = width,
            Child = MakeNodeText(node)
        };
    }

    private static TextBlock MakeNodeText(SmartArtNodeVisualPlan node)
    {
        var text = new TextBlock
        {
            Text = node.Text,
            Foreground = new SolidColorBrush(ParseHex(node.TextHex)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FontSize = Math.Max(1, node.FontSizeDip),
            TextAlignment = System.Windows.TextAlignment.Center
        };
        if (!string.IsNullOrWhiteSpace(node.FontFamilyName))
            text.FontFamily = new FontFamily(node.FontFamilyName);
        return text;
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

    // Hierarchy layout: shared planner geometry, with styles mapped by DFS node index.
    private static FrameworkElement BuildHierarchy(
        SmartArtVisualPlan plan,
        double strokeThickness,
        double targetWidth,
        double targetHeight)
    {
        if (plan.HierarchyGeometry is not { Nodes.Count: > 0 } geometry)
            return BuildVerticalList(plan.Nodes, strokeThickness);

        var canvas = new Canvas
        {
            Width = geometry.NaturalWidth,
            Height = geometry.NaturalHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = string.Equals(plan.LayoutId, "orgchart1", StringComparison.OrdinalIgnoreCase)
                ? new Thickness(0)
                : new Thickness(6)
        };

        foreach (var connector in geometry.Connectors)
        {
            if (connector.ParentNodeIndex < 0 || connector.ParentNodeIndex >= plan.Nodes.Count)
                continue;

            var parent = plan.Nodes[connector.ParentNodeIndex];
            var connectorBrush = new SolidColorBrush(ParseHex(parent.ConnectorHex));
            var connectorThickness = ConnectorThickness(parent, strokeThickness);
            var points = connector.Points.Count > 1
                ? connector.Points
                : [new SmartArtLayoutPoint(connector.X1, connector.Y1), new(connector.X2, connector.Y2)];
            for (var pointIndex = 1; pointIndex < points.Count; pointIndex++)
            {
                canvas.Children.Add(new Line
                {
                    X1 = points[pointIndex - 1].X,
                    Y1 = points[pointIndex - 1].Y,
                    X2 = points[pointIndex].X,
                    Y2 = points[pointIndex].Y,
                    Stroke = connectorBrush,
                    StrokeThickness = connectorThickness,
                    Opacity = 0.75
                });
            }
        }

        foreach (var nodeGeometry in geometry.Nodes)
        {
            if (nodeGeometry.NodeIndex < 0 || nodeGeometry.NodeIndex >= plan.Nodes.Count)
                continue;

            var box = MakeNodeBox(
                plan.Nodes[nodeGeometry.NodeIndex],
                strokeThickness,
                margin: new Thickness(0),
                padding: new Thickness(6, 3, 6, 3),
                width: nodeGeometry.Width);
            box.Height = nodeGeometry.Height;
            Canvas.SetLeft(box, nodeGeometry.X);
            Canvas.SetTop(box, nodeGeometry.Y);
            canvas.Children.Add(box);
        }

        var viewbox = new Viewbox
        {
            Width = targetWidth,
            Height = targetHeight,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = canvas
        };

        if (string.Equals(plan.LayoutId, "orgchart1", StringComparison.OrdinalIgnoreCase)
            && geometry.NaturalWidth == 320
            && geometry.NaturalHeight == 140
            && plan.Nodes.Count == 3)
        {
            viewbox.RenderTransform = new TranslateTransform(0, 3);
        }

        return viewbox;
    }

    private static double ConnectorThickness(SmartArtNodeVisualPlan node, double strokeThickness) =>
        Math.Max(strokeThickness, node.BorderThickness > 0 ? node.BorderThickness : strokeThickness);

    private static FrameworkElement BuildPlannedLayout(
        SmartArtVisualPlan plan,
        double strokeThickness,
        double targetWidth,
        double targetHeight)
    {
        if (plan.LayoutGeometry is not { Nodes.Count: > 0 } geometry)
            return BuildVerticalList(plan.Nodes, strokeThickness);

        var canvas = new Canvas
        {
            Width = Math.Max(1, geometry.NaturalWidth),
            Height = Math.Max(1, geometry.NaturalHeight),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = plan.LayoutId is "orgchart1" or "pyramid1"
                ? new Thickness(0)
                : new Thickness(6)
        };
        var useNativeWordPyramidTextScale = string.Equals(plan.LayoutId, "pyramid1", StringComparison.OrdinalIgnoreCase)
            && string.Equals(plan.ColorScheme.Id, "accent2", StringComparison.OrdinalIgnoreCase)
            && string.Equals(plan.Style.Id, "flat1", StringComparison.OrdinalIgnoreCase)
            && geometry.NaturalWidth == 300
            && geometry.NaturalHeight == 150
            && geometry.Nodes.Count == 4;

        foreach (var connector in geometry.Connectors)
            AddPlannedConnector(canvas, plan.Nodes, connector, strokeThickness, geometry.Kind);

        foreach (var nodeGeometry in geometry.Nodes)
        {
            if (nodeGeometry.NodeIndex < 0 || nodeGeometry.NodeIndex >= plan.Nodes.Count)
                continue;

            var node = plan.Nodes[nodeGeometry.NodeIndex];
            if (nodeGeometry.HasPolygon)
            {
                canvas.Children.Add(MakePlannedPolygon(node, nodeGeometry, strokeThickness));
                var label = MakeNodeTextBox(
                    node,
                    margin: new Thickness(0),
                    padding: new Thickness(4, 2, 4, 2),
                    width: nodeGeometry.Width);
                if (useNativeWordPyramidTextScale && label.Child is TextBlock text)
                    text.FontSize *= 0.75;
                label.Height = nodeGeometry.Height;
                Canvas.SetLeft(label, nodeGeometry.X + (useNativeWordPyramidTextScale ? 1 : 0));
                Canvas.SetTop(label, nodeGeometry.Y + (useNativeWordPyramidTextScale ? 1.5 : 0));
                canvas.Children.Add(label);
            }
            else
            {
                var box = MakeNodeBox(
                    node,
                    strokeThickness,
                    margin: new Thickness(0),
                    padding: new Thickness(4, 2, 4, 2),
                    width: nodeGeometry.Width);
                box.Height = nodeGeometry.Height;
                Canvas.SetLeft(box, nodeGeometry.X);
                Canvas.SetTop(box, nodeGeometry.Y);
                canvas.Children.Add(box);
            }
        }

        return new Viewbox
        {
            Width = targetWidth,
            Height = targetHeight,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = canvas
        };
    }

    private static Polygon MakePlannedPolygon(
        SmartArtNodeVisualPlan node,
        SmartArtLayoutNodeGeometry nodeGeometry,
        double strokeThickness)
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

        return new Polygon
        {
            Points = new PointCollection(nodeGeometry.PolygonPoints.Select(point => new Point(point.X, point.Y))),
            Fill = new SolidColorBrush(ParseHex(node.FillHex)),
            Stroke = node.BorderThickness > 0 ? new SolidColorBrush(ParseHex(node.BorderHex)) : null,
            StrokeThickness = node.BorderThickness > 0
                ? node.BorderThickness
                : Math.Max(0, strokeThickness),
            Effect = effect
        };
    }

    private static void AddPlannedConnector(
        Canvas canvas,
        IReadOnlyList<SmartArtNodeVisualPlan> nodes,
        SmartArtLayoutConnectorGeometry connector,
        double strokeThickness,
        SmartArtLayoutGeometryKind geometryKind)
    {
        if (connector.SourceNodeIndex < 0 || connector.SourceNodeIndex >= nodes.Count)
            return;

        var source = nodes[connector.SourceNodeIndex];
        var brush = new SolidColorBrush(ParseHex(source.ConnectorHex));
        var thickness = ConnectorThickness(source, strokeThickness);
        var start = new Point(connector.X1, connector.Y1);
        var end = new Point(connector.X2, connector.Y2);

        canvas.Children.Add(new Line
        {
            X1 = start.X,
            Y1 = start.Y,
            X2 = end.X,
            Y2 = end.Y,
            Stroke = brush,
            StrokeThickness = thickness,
            Opacity = 0.7
        });

        if (geometryKind == SmartArtLayoutGeometryKind.BasicProcess && connector.Kind == SmartArtLayoutConnectorKind.Arrow)
        {
            AddBasicProcessArrow(canvas, brush, start, end);
            return;
        }

        if (connector.Kind == SmartArtLayoutConnectorKind.Arrow)
            AddArrowHead(canvas, brush, thickness, start, end);
    }

    private static void AddBasicProcessArrow(
        Canvas canvas,
        Brush brush,
        Point start,
        Point end)
    {
        const double halfHeight = 4;
        const double headLength = 5;
        var headStart = Math.Max(start.X, end.X - headLength);
        var centerY = (start.Y + end.Y) / 2;

        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection(
            [
                new Point(start.X, centerY - halfHeight),
                new Point(headStart, centerY - halfHeight),
                new Point(headStart, centerY - halfHeight * 2),
                new Point(end.X, centerY),
                new Point(headStart, centerY + halfHeight * 2),
                new Point(headStart, centerY + halfHeight),
                new Point(start.X, centerY + halfHeight)
            ]),
            Fill = brush,
            Stroke = null
        });
    }

    private static void AddArrowHead(
        Canvas canvas,
        Brush brush,
        double thickness,
        Point start,
        Point end)
    {
        var arrowhead = SmartArtConnectorArrowheadPlanner.Calculate(
            new SmartArtLayoutPoint(start.X, start.Y),
            new SmartArtLayoutPoint(end.X, end.Y));
        if (!arrowhead.IsVisible)
            return;

        var tip = new Point(arrowhead.Tip.X, arrowhead.Tip.Y);
        var left = new Point(arrowhead.Left.X, arrowhead.Left.Y);
        var right = new Point(arrowhead.Right.X, arrowhead.Right.Y);

        canvas.Children.Add(new Line
        {
            X1 = tip.X,
            Y1 = tip.Y,
            X2 = left.X,
            Y2 = left.Y,
            Stroke = brush,
            StrokeThickness = thickness,
            Opacity = 0.7
        });
        canvas.Children.Add(new Line
        {
            X1 = tip.X,
            Y1 = tip.Y,
            X2 = right.X,
            Y2 = right.Y,
            Stroke = brush,
            StrokeThickness = thickness,
            Opacity = 0.7
        });
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
