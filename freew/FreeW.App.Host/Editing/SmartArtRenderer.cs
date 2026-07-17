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
        && string.Equals(smartArt.StyleId, "simple1", StringComparison.OrdinalIgnoreCase)
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
        double? minWidth = null,
        TextWrapping textWrapping = TextWrapping.Wrap)
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
                TextWrapping = textWrapping,
                FontSize = Math.Max(1, node.FontSizeDip),
                TextAlignment = System.Windows.TextAlignment.Center
            }
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
            Child = new TextBlock
            {
                Text = node.Text,
                Foreground = new SolidColorBrush(ParseHex(node.TextHex)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = Math.Max(1, node.FontSizeDip),
                TextAlignment = System.Windows.TextAlignment.Center
            }
        };
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
        if (plan.UsesWordLayeredGalleryStyle
            && string.Equals(plan.LayoutId, "hierarchy1", StringComparison.OrdinalIgnoreCase))
        {
            return BuildWordLayeredHierarchyLayout(plan, targetWidth, targetHeight);
        }

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
                width: nodeGeometry.Width,
                textWrapping: string.Equals(plan.LayoutId, "orgchart1", StringComparison.OrdinalIgnoreCase)
                    ? TextWrapping.NoWrap
                    : TextWrapping.Wrap);
            box.Height = nodeGeometry.Height;
            Canvas.SetLeft(box, nodeGeometry.X);
            Canvas.SetTop(box, nodeGeometry.Y);
            canvas.Children.Add(box);
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

    private static double ConnectorThickness(SmartArtNodeVisualPlan node, double strokeThickness) =>
        Math.Max(strokeThickness, node.BorderThickness > 0 ? node.BorderThickness : strokeThickness);

    private static FrameworkElement BuildPlannedLayout(
        SmartArtVisualPlan plan,
        double strokeThickness,
        double targetWidth,
        double targetHeight)
    {
        if (IsNativeWordPyramidLayout(plan))
            return BuildNativeWordPyramidLayout(plan, targetWidth, targetHeight);

        if (plan.UsesWordLayeredGalleryStyle
            && plan.LayoutId is "list1" or "process1" or "cycle1" or "radial1")
        {
            return BuildWordLayeredGalleryLayout(plan, targetWidth, targetHeight);
        }

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
                label.Height = nodeGeometry.Height;
                Canvas.SetLeft(label, nodeGeometry.X);
                Canvas.SetTop(label, nodeGeometry.Y);
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

    private static bool IsNativeWordPyramidLayout(SmartArtVisualPlan plan) =>
        string.Equals(plan.LayoutId, "pyramid1", StringComparison.OrdinalIgnoreCase)
        && string.Equals(plan.ColorScheme.Id, "accent2", StringComparison.OrdinalIgnoreCase)
        && (string.Equals(plan.Style.Id, "flat1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(plan.Style.Id, "simple1", StringComparison.OrdinalIgnoreCase));

    private static FrameworkElement BuildNativeWordPyramidLayout(
        SmartArtVisualPlan plan,
        double targetWidth,
        double targetHeight)
    {
        var width = Math.Max(1, targetWidth);
        var height = Math.Max(1, targetHeight);
        var canvas = new Canvas
        {
            Width = width,
            Height = height,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        if (plan.Nodes.Count == 0)
            return canvas;

        // Word's native Basic Pyramid gallery is one solid triangle. Its logical nodes control
        // label placement only; Word does not draw separate trapezoid bands between them.
        canvas.Children.Add(new Polygon
        {
            Points = new PointCollection(
            [
                new Point(width / 2, 0),
                new Point(width, height),
                new Point(0, height)
            ]),
            Fill = new SolidColorBrush(ParseHex(plan.Nodes[0].FillHex))
        });

        for (var index = 0; index < plan.Nodes.Count; index++)
        {
            var node = plan.Nodes[index];
            var label = new TextBlock
            {
                Text = node.Text,
                Foreground = new SolidColorBrush(ParseHex(node.TextHex)),
                FontSize = Math.Max(1, node.FontSizeDip),
                TextWrapping = TextWrapping.NoWrap,
                TextAlignment = System.Windows.TextAlignment.Center
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, (width - label.DesiredSize.Width) / 2);
            Canvas.SetTop(label, height * (index + 0.5) / plan.Nodes.Count - label.DesiredSize.Height / 2);
            canvas.Children.Add(label);
        }

        return canvas;
    }

    // Word's stock SmartArt galleries materialise a dark backing shape behind a smaller, light
    // foreground shape. The diagram frame itself is transparent; only the two-layer node treatment
    // is visible on the page. This path is used for authored Word gallery ids, while newly-created
    // FreeW diagrams retain their explicit model style until a gallery is selected.
    private static FrameworkElement BuildWordLayeredGalleryLayout(
        SmartArtVisualPlan plan,
        double targetWidth,
        double targetHeight)
    {
        var canvas = new Canvas
        {
            Width = Math.Max(1, targetWidth),
            Height = Math.Max(1, targetHeight),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var baseColor = ParseHex(plan.ColorScheme.Color1Hex);
        var frontColor = BlendWithWhite(baseColor, 0.10);
        var nodeCount = plan.Nodes.Count;
        var isList = string.Equals(plan.LayoutId, "list1", StringComparison.OrdinalIgnoreCase);
        var isCycle = string.Equals(plan.LayoutId, "cycle1", StringComparison.OrdinalIgnoreCase);
        var isRadial = string.Equals(plan.LayoutId, "radial1", StringComparison.OrdinalIgnoreCase);
        if (isCycle || isRadial)
            return BuildWordLayeredVerticalGalleryLayout(plan, targetWidth, targetHeight, baseColor, isRadial);

        var frontWidth = isList ? 60d : 120d;
        var frontHeight = isList ? 35d : 72d;
        var offsetX = isList ? 8d : 16d;
        var offsetY = isList ? 8d : 14d;
        var gap = isList ? 24d : 40d;
        var fontSize = isList ? 6d * 96d / 72d : 18d * 96d / 72d;

        if (isList)
        {
            var frontX = targetWidth * 0.455;
            for (var i = 0; i < nodeCount; i++)
            {
                var frontY = 6 + i * (frontHeight + gap);
                if (i > 0)
                {
                    canvas.Children.Add(new Line
                    {
                        X1 = frontX - offsetX / 2,
                        Y1 = frontY - gap,
                        X2 = frontX - offsetX / 2,
                        Y2 = frontY,
                        Stroke = new SolidColorBrush(baseColor),
                        StrokeThickness = 1
                    });
                }

                AddWordLayeredNode(
                    canvas,
                    plan.Nodes[i],
                    frontX,
                    frontY,
                    frontWidth,
                    frontHeight,
                    offsetX,
                    offsetY,
                    baseColor,
                    frontColor,
                    fontSize,
                    TextWrapping.Wrap);
            }
        }
        else
        {
            var frontX = 16d;
            var frontY = 84d;
            for (var i = 0; i < nodeCount; i++)
            {
                AddWordLayeredNode(
                    canvas,
                    plan.Nodes[i],
                    frontX + i * (frontWidth + gap),
                    frontY,
                    frontWidth,
                    frontHeight,
                    offsetX,
                    offsetY,
                    baseColor,
                    frontColor,
                    fontSize,
                    TextWrapping.NoWrap);
            }
        }

        return canvas;
    }

    private static FrameworkElement BuildWordLayeredHierarchyLayout(
        SmartArtVisualPlan plan,
        double targetWidth,
        double targetHeight)
    {
        var canvas = new Canvas
        {
            Width = Math.Max(1, targetWidth),
            Height = Math.Max(1, targetHeight),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        if (plan.Nodes.Count == 0)
            return canvas;

        var baseColor = ParseHex(plan.ColorScheme.Color1Hex);
        var frontColor = BlendWithWhite(baseColor, 0.10);
        const double frontWidth = 160;
        const double frontHeight = 101;
        const double offsetX = 18;
        const double offsetY = 18;
        var rootX = (targetWidth - frontWidth) / 2;
        var childY = Math.Max(0, targetHeight - frontHeight);
        var childGap = Math.Max(0, (targetWidth - frontWidth * 3) / 4);
        var childXs = new[]
        {
            childGap,
            (targetWidth - frontWidth) / 2,
            targetWidth - childGap - frontWidth
        };
        var childCount = Math.Min(childXs.Length, Math.Max(0, plan.Nodes.Count - 1));

        var rootCenterX = rootX + frontWidth / 2;
        var childCenterY = childY - 31;
        var childCenters = childXs.Take(childCount).Select(x => x + frontWidth / 2).ToArray();
        var connectorBrush = new SolidColorBrush(baseColor);
        if (childCount > 0)
        {
            canvas.Children.Add(new Line
            {
                X1 = rootCenterX,
                Y1 = frontHeight - offsetY,
                X2 = rootCenterX,
                Y2 = childCenterY,
                Stroke = connectorBrush,
                StrokeThickness = 1
            });
            canvas.Children.Add(new Line
            {
                X1 = childCenters[0],
                Y1 = childCenterY,
                X2 = childCenters[^1],
                Y2 = childCenterY,
                Stroke = connectorBrush,
                StrokeThickness = 1
            });
        }
        foreach (var childCenter in childCenters)
        {
            canvas.Children.Add(new Line
            {
                X1 = childCenter,
                Y1 = childCenterY,
                X2 = childCenter,
                Y2 = childY,
                Stroke = connectorBrush,
                StrokeThickness = 1
            });
        }

        AddWordLayeredNode(
            canvas,
            plan.Nodes[0],
            rootX,
            offsetY,
            frontWidth,
            frontHeight,
            offsetX,
            offsetY,
            baseColor,
            frontColor,
            36d * 96d / 72d,
            TextWrapping.NoWrap);
        for (var i = 1; i <= childCount; i++)
        {
            AddWordLayeredNode(
                canvas,
                plan.Nodes[i],
                childXs[i - 1],
                childY,
                frontWidth,
                frontHeight,
                offsetX,
                offsetY,
                baseColor,
                frontColor,
                36d * 96d / 72d,
                TextWrapping.NoWrap);
        }

        return canvas;
    }

    private static FrameworkElement BuildWordLayeredVerticalGalleryLayout(
        SmartArtVisualPlan plan,
        double targetWidth,
        double targetHeight,
        Color baseColor,
        bool isRadial)
    {
        var canvas = new Canvas
        {
            Width = Math.Max(1, targetWidth),
            Height = Math.Max(1, targetHeight),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        var frontColor = BlendWithWhite(baseColor, 0.10);
        var frontWidth = isRadial ? 66d : 76d;
        var frontHeight = isRadial ? 43d : 49d;
        var offsetX = isRadial ? 8d : 9d;
        var offsetY = isRadial ? 7d : 8d;
        var gap = isRadial ? 17d : 21d;
        var frontX = (targetWidth - frontWidth) / 2;
        var frontY = isRadial ? 7d : 9d;
        var fontSize = isRadial ? 7d * 96d / 72d : 16d * 96d / 72d;

        for (var i = 1; i < plan.Nodes.Count; i++)
        {
            var lineY = frontY + i * (frontHeight + gap) - gap;
            canvas.Children.Add(new Line
            {
                X1 = frontX - offsetX / 2,
                Y1 = lineY,
                X2 = frontX - offsetX / 2,
                Y2 = lineY + gap,
                Stroke = new SolidColorBrush(baseColor),
                StrokeThickness = 1
            });
        }

        for (var i = 0; i < plan.Nodes.Count; i++)
        {
            AddWordLayeredNode(
                canvas,
                plan.Nodes[i],
                frontX,
                frontY + i * (frontHeight + gap),
                frontWidth,
                frontHeight,
                offsetX,
                offsetY,
                baseColor,
                frontColor,
                fontSize,
                TextWrapping.NoWrap);
        }

        return canvas;
    }

    private static Border AddWordLayeredNode(
        Canvas canvas,
        SmartArtNodeVisualPlan node,
        double frontX,
        double frontY,
        double width,
        double height,
        double offsetX,
        double offsetY,
        Color baseColor,
        Color frontColor,
        double fontSize,
        TextWrapping textWrapping)
    {
        var backing = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(baseColor),
            CornerRadius = new CornerRadius(5)
        };
        Canvas.SetLeft(backing, frontX - offsetX);
        Canvas.SetTop(backing, frontY - offsetY);
        canvas.Children.Add(backing);

        var front = new Border
        {
            Width = width,
            Height = height,
            Background = new SolidColorBrush(frontColor),
            BorderBrush = new SolidColorBrush(baseColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = new TextBlock
            {
                Text = node.Text,
                Foreground = new SolidColorBrush(Colors.Black),
                FontSize = fontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = System.Windows.TextAlignment.Center,
                TextWrapping = textWrapping,
                Padding = new Thickness(3, 1, 3, 1)
            }
        };
        Canvas.SetLeft(front, frontX);
        Canvas.SetTop(front, frontY);
        canvas.Children.Add(front);
        return front;
    }

    private static Color BlendWithWhite(Color color, double baseWeight)
    {
        var weight = Math.Clamp(baseWeight, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(color.R * weight + 255 * (1 - weight), MidpointRounding.AwayFromZero),
            (byte)Math.Round(color.G * weight + 255 * (1 - weight), MidpointRounding.AwayFromZero),
            (byte)Math.Round(color.B * weight + 255 * (1 - weight), MidpointRounding.AwayFromZero));
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
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= 0.001)
            return;

        var ux = dx / length;
        var uy = dy / length;
        var px = -uy;
        var py = ux;
        const double arrowLength = 6;
        const double arrowWidth = 4;

        var p1 = new Point(
            end.X - ux * arrowLength + px * arrowWidth,
            end.Y - uy * arrowLength + py * arrowWidth);
        var p2 = new Point(
            end.X - ux * arrowLength - px * arrowWidth,
            end.Y - uy * arrowLength - py * arrowWidth);

        canvas.Children.Add(new Line
        {
            X1 = end.X,
            Y1 = end.Y,
            X2 = p1.X,
            Y2 = p1.Y,
            Stroke = brush,
            StrokeThickness = thickness,
            Opacity = 0.7
        });
        canvas.Children.Add(new Line
        {
            X1 = end.X,
            Y1 = end.Y,
            X2 = p2.X,
            Y2 = p2.Y,
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
