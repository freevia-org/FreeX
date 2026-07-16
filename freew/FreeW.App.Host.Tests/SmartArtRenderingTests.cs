using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Rendering tests for SmartArt diagrams (<c>DocumentView.BuildSmartArtRun</c> →
/// <c>SmartArtRenderer.Build</c>). Covers the bugs fixed on 2026-06-25:
/// <list type="bullet">
///   <item><description>Bug #4: SmartArt node color cycling — each node in a multi-node diagram must get
///   a different fill color (colorful1 default: #4E81BD blue, #C0504D red, #9BBB59 green, #8064A2 purple).</description></item>
///   <item><description>Bug #5: Process arrow fill contrasts with adjacent box fill — arrow must NOT
///   share the same color as the box that precedes it.</description></item>
/// </list>
/// Runs on STA because it builds the real WPF editing surface.
/// </summary>
public sealed class SmartArtRenderingTests
{
    private static List<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var result = new List<T>();
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d)
            {
                if (d is T t)
                    result.Add(t);
                result.AddRange(LogicalDescendants<T>(d));
            }
        return result;
    }

    private static DocumentView ViewWithSmartArt(SmartArt smartArt)
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        view.InsertSmartArt(smartArt);
        return view;
    }

    private static Border NodeBorder(DocumentView view, string text) =>
        LogicalDescendants<Border>(view.Document)
            .Single(b => b.Child is TextBlock { Text: var nodeText } && nodeText == text);

    // ── Bug #4: node color cycling ───────────────────────────────────────────────────────────────

    /// <summary>
    /// A 4-node vertical list with the default colorful1 scheme must render 4 borders with 4
    /// distinct fill colors (#4E81BD, #C0504D, #9BBB59, #8064A2). If cycling is broken all
    /// borders would share the same Color1 (#4E81BD).
    /// </summary>
    [StaFact]
    public void VerticalList_FourNodes_HasFourDistinctFillColors()
    {
        var sa = SmartArt.Create(SmartArtKind.List, ["Alpha", "Beta", "Gamma", "Delta"]);
        var view = ViewWithSmartArt(sa);

        var borders = LogicalDescendants<Border>(view.Document)
            .Where(b => b.Background is SolidColorBrush)
            .ToList();

        // At least 4 borders with distinct backgrounds (one per node).
        var distinctColors = borders
            .Select(b => ((SolidColorBrush)b.Background!).Color)
            .Distinct()
            .ToList();
        Assert.True(distinctColors.Count >= 4,
            $"expected ≥4 distinct node fill colors (cycling); got {distinctColors.Count}: [{string.Join(", ", distinctColors)}]");
    }

    /// <summary>
    /// Default scheme colorful1 Color1 is #4E81BD (blue). Color2 is #C0504D (red).
    /// The first node must be blue and the second node must be red — confirming index=0 and index=1
    /// are applied to the first two nodes respectively.
    /// </summary>
    [StaFact]
    public void VerticalList_Colorful1_FirstNodeBlueSecondNodeRed()
    {
        // colorful1 default: Color1=#4E81BD (blue), Color2=#C0504D (red)
        var sa = SmartArt.Create(SmartArtKind.List, ["Node1", "Node2"]);
        // ColorSchemeId null → uses Default (colorful1)
        var view = ViewWithSmartArt(sa);

        var borders = LogicalDescendants<Border>(view.Document)
            .Where(b => b.Background is SolidColorBrush)
            .Select(b => ((SolidColorBrush)b.Background!).Color)
            .ToList();

        // Outer border of the SmartArt has a white/transparent background — skip it.
        // Node borders use the scheme colors. Find those that are not white/transparent.
        var nodeColors = borders
            .Where(c => c.R != 0xFF || c.G != 0xFF || c.B != 0xFF) // exclude white
            .Where(c => c.A > 0)                                     // exclude transparent
            .ToList();

        Assert.True(nodeColors.Count >= 2, $"expected ≥2 node fill colors, got {nodeColors.Count}");

        // Color1 = #4E81BD → R=0x4E, G=0x81, B=0xBD
        Assert.Equal(0x4E, nodeColors[0].R);
        Assert.Equal(0x81, nodeColors[0].G);
        Assert.Equal(0xBD, nodeColors[0].B);

        // Color2 = #C0504D → R=0xC0, G=0x50, B=0x4D
        Assert.Equal(0xC0, nodeColors[1].R);
        Assert.Equal(0x50, nodeColors[1].G);
        Assert.Equal(0x4D, nodeColors[1].B);
    }

    /// <summary>
    /// Using a named scheme (accent1 = dark monochromatic blue) verifies that
    /// <c>SmartArtColorScheme.FindById</c> returns the correct non-default scheme.
    /// Node fills must NOT match colorful1's #4E81BD; they must match accent1's Color1 (#1F3864).
    /// </summary>
    [StaFact]
    public void VerticalList_Accent1Scheme_FirstNodeDarkBlueNotColorful1Blue()
    {
        var sa = SmartArt.Create(SmartArtKind.List, ["A", "B", "C"]);
        sa.ColorSchemeId = "accent1"; // accent1 Color1 = #1F3864 (very dark blue)
        var view = ViewWithSmartArt(sa);

        var nodeColors = LogicalDescendants<Border>(view.Document)
            .Where(b => b.Background is SolidColorBrush)
            .Select(b => ((SolidColorBrush)b.Background!).Color)
            .Where(c => c.A > 0 && (c.R != 0xFF || c.G != 0xFF || c.B != 0xFF))
            .ToList();

        Assert.True(nodeColors.Count >= 1, "expected at least one node fill color");

        // accent1 Color1 = #1F3864 → R=0x1F, G=0x38, B=0x64
        // If colorful1 default is used instead, node 0 = #4E81BD (R=0x4E)
        Assert.Equal(0x1F, nodeColors[0].R);
    }

    [StaFact]
    public void VerticalList_IntenseStyle_UsesPlannedFillBorderCornerAndShadowValues()
    {
        var sa = SmartArt.Create(SmartArtKind.List, ["Styled"]);
        sa.ColorSchemeId = "accent1";
        sa.StyleId = "intense1";
        var view = ViewWithSmartArt(sa);

        var nodeBorder = LogicalDescendants<Border>(view.Document)
            .Single(b => b.Child is TextBlock { Text: "Styled" });

        var fill = Assert.IsType<SolidColorBrush>(nodeBorder.Background).Color;
        Assert.Equal(0x38, fill.R);
        Assert.Equal(0x51, fill.G);
        Assert.Equal(0x7D, fill.B);

        var border = Assert.IsType<SolidColorBrush>(nodeBorder.BorderBrush).Color;
        Assert.Equal(0x0A, border.R);
        Assert.Equal(0x23, border.G);
        Assert.Equal(0x4F, border.B);
        Assert.Equal(1.5, nodeBorder.BorderThickness.Left);
        Assert.Equal(0, nodeBorder.CornerRadius.TopLeft);

        var shadow = Assert.IsType<DropShadowEffect>(nodeBorder.Effect);
        Assert.InRange(shadow.Opacity, 0.29, 0.31);
        Assert.InRange(shadow.BlurRadius, 6.39, 6.41);
        Assert.InRange(shadow.ShadowDepth, 2.09, 2.11);
    }

    [StaFact]
    public void HierarchyWithGrandchild_DoesNotShiftPlanValuesOntoNextRoot()
    {
        var root = new SmartArtNode("Root");
        var child = root.AddChild("Child");
        child.AddChild("Grandchild");

        var sa = new SmartArt { Kind = SmartArtKind.Hierarchy };
        sa.ColorSchemeId = "accent1";
        sa.StyleId = "intense1";
        sa.Nodes.Add(root);
        sa.Nodes.Add(new SmartArtNode("SecondRoot"));

        var view = ViewWithSmartArt(sa);

        var renderedNodeTexts = LogicalDescendants<Border>(view.Document)
            .Select(b => b.Child as TextBlock)
            .Where(tb => tb is not null && tb.Text is "Root" or "Child" or "Grandchild" or "SecondRoot")
            .Select(tb => tb!.Text)
            .ToList();

        Assert.Equal(new[] { "Root", "Child", "Grandchild", "SecondRoot" }, renderedNodeTexts);

        var connectors = LogicalDescendants<Line>(view.Document);
        Assert.True(connectors.Count >= 2,
            $"expected hierarchy connectors for Root->Child and Child->Grandchild; got {connectors.Count}");

        var secondRootBorder = LogicalDescendants<Border>(view.Document)
            .Single(b => b.Child is TextBlock { Text: "SecondRoot" });
        var secondRootFill = Assert.IsType<SolidColorBrush>(secondRootBorder.Background).Color;
        Assert.Equal(0xB6, secondRootFill.R);
        Assert.Equal(0xDC, secondRootFill.G);
        Assert.Equal(0xFF, secondRootFill.B);

        var secondRootShadow = Assert.IsType<DropShadowEffect>(secondRootBorder.Effect);
        Assert.InRange(secondRootShadow.Opacity, 0.29, 0.31);
    }

    [StaFact]
    public void NativeOrgChart_UsesWordGeometryStyleAndNoOuterFrame()
    {
        var root = new SmartArtNode("Plan");
        var build = root.AddChild("Build");
        build.AddChild("Verify");
        var sa = new SmartArt { Kind = SmartArtKind.Hierarchy };
        sa.LayoutId = "orgchart1";
        sa.ColorSchemeId = "accent1";
        sa.StyleId = "intense1";
        sa.WidthPt = 320;
        sa.HeightPt = 140;
        sa.Nodes.Add(root);

        var view = ViewWithSmartArt(sa);
        var outer = LogicalDescendants<Border>(view.Document)
            .Single(border => ReferenceEquals(border.Tag, sa));

        Assert.Equal(0, outer.BorderThickness.Left);
        Assert.Null(outer.BorderBrush);

        foreach (var text in new[] { "Plan", "Build", "Verify" })
        {
            var node = NodeBorder(view, text);
            var fill = Assert.IsType<SolidColorBrush>(node.Background).Color;
            Assert.Equal(Color.FromRgb(0x1F, 0x38, 0x64), fill);
            Assert.Equal(0, node.Effect is DropShadowEffect shadow ? shadow.Opacity : 0);
            Assert.InRange(Assert.IsType<TextBlock>(node.Child).FontSize, 29.331, 29.335);
            Assert.Equal(TextWrapping.NoWrap, Assert.IsType<TextBlock>(node.Child).TextWrapping);
        }

        var canvas = LogicalDescendants<Canvas>(outer).Single();
        var plan = NodeBorder(view, "Plan");
        var buildBox = NodeBorder(view, "Build");
        var verify = NodeBorder(view, "Verify");
        Assert.InRange(Canvas.GetLeft(plan), 169.28, 169.30);
        Assert.InRange(Canvas.GetTop(plan), 0.05, 0.08);
        Assert.InRange(Canvas.GetLeft(buildBox), 77.85, 77.87);
        Assert.InRange(Canvas.GetTop(buildBox), 51.77, 51.80);
        Assert.InRange(Canvas.GetLeft(verify), 125.20, 125.23);
        Assert.InRange(Canvas.GetTop(verify), 103.50, 103.53);
        Assert.Equal(4, LogicalDescendants<Line>(canvas).Count);
    }

    // ── Bug #5: process arrow fill contrasts with box fill ───────────────────────────────────────

    /// <summary>
    /// A process diagram with 3 nodes must have at least 2 arrow Polygons, and each arrow's Fill must
    /// differ from the fill of the node box that precedes it. Before the fix the arrow had the same
    /// color as the box (invisible), making the topology indistinguishable from a list.
    /// </summary>
    [StaFact]
    public void CycleLayout_RendersSharedGeometryConnectorsAndNodePositions()
    {
        var sa = SmartArt.Create(SmartArtKind.List, ["North", "East", "South", "West"]);
        sa.LayoutId = "cycle1";
        var view = ViewWithSmartArt(sa);

        var north = NodeBorder(view, "North");
        Assert.InRange(Canvas.GetLeft(north), 73, 75);
        Assert.InRange(Canvas.GetTop(north), 10, 12);

        var connectorLines = LogicalDescendants<Line>(view.Document);
        Assert.True(connectorLines.Count >= 4,
            $"expected shared cycle geometry to render connector lines; got {connectorLines.Count}");
    }

    [StaFact]
    public void MatrixLayout_RendersSharedTwoByTwoNodeGrid()
    {
        var sa = SmartArt.Create(SmartArtKind.List, ["A", "B", "C", "D"]);
        sa.LayoutId = "matrix1";
        var view = ViewWithSmartArt(sa);

        var a = NodeBorder(view, "A");
        var b = NodeBorder(view, "B");
        var c = NodeBorder(view, "C");
        var d = NodeBorder(view, "D");

        Assert.True(Canvas.GetLeft(b) > Canvas.GetLeft(a), "B should be in the second matrix column");
        Assert.True(Canvas.GetTop(c) > Canvas.GetTop(a), "C should be in the second matrix row");
        Assert.True(Canvas.GetLeft(d) > Canvas.GetLeft(c), "D should be in the second matrix column");
        Assert.True(Canvas.GetTop(d) > Canvas.GetTop(b), "D should be in the second matrix row");
    }

    [StaFact]
    public void BasicPyramidLayout_RendersSharedPolygonBands()
    {
        var sa = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        sa.LayoutId = "pyramid1";
        var view = ViewWithSmartArt(sa);

        var top = NodeBorder(view, "Top");
        var middle = NodeBorder(view, "Middle");
        var lower = NodeBorder(view, "Lower");
        var bottom = NodeBorder(view, "Base");

        Assert.True(Canvas.GetTop(middle) > Canvas.GetTop(top), "second shared pyramid band should be below the first");
        Assert.True(Canvas.GetTop(lower) > Canvas.GetTop(middle), "third shared pyramid band should be below the second");
        Assert.True(Canvas.GetTop(bottom) > Canvas.GetTop(lower), "base shared pyramid band should be last");
        Assert.True(top.Width < middle.Width, "top text bounds should be narrower than the second band");
        Assert.True(middle.Width < lower.Width, "middle text bounds should be narrower than the third band");
        Assert.True(lower.Width < bottom.Width, "lower text bounds should be narrower than the base");
        Assert.True(Canvas.GetLeft(top) > Canvas.GetLeft(bottom), "top text bounds should be centered inside the base width");

        var polygons = LogicalDescendants<Polygon>(view.Document)
            .Where(p => p.Points.Count >= 4)
            .ToList();

        Assert.True(polygons.Count >= 4,
            $"expected WPF to render shared polygon bands for Basic Pyramid; got {polygons.Count}");
        Assert.InRange(polygons[0].Points[0].X, 136.4, 136.6);
        Assert.InRange(polygons[0].Points[2].X, 185.9, 186.1);
        Assert.True(polygons[0].Points[3].X < polygons[0].Points[0].X
            && polygons[0].Points[2].X > polygons[0].Points[1].X,
            "top shared pyramid polygon should widen from top edge to bottom edge");
    }

    [StaFact]
    public void NativePyramid_UsesWordGeometryStyleAndNoOuterFrame()
    {
        var sa = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        sa.LayoutId = "pyramid1";
        sa.ColorSchemeId = "accent2";
        sa.StyleId = "flat1";
        sa.WidthPt = 300;
        sa.HeightPt = 150;

        var view = ViewWithSmartArt(sa);
        var outer = LogicalDescendants<Border>(view.Document)
            .Single(border => ReferenceEquals(border.Tag, sa));
        Assert.Equal(0, outer.BorderThickness.Left);
        Assert.Null(outer.BorderBrush);
        Assert.Equal(new Thickness(2, 4, 0, 6), outer.Margin);

        foreach (var text in new[] { "Top", "Middle", "Lower", "Base" })
        {
            var node = NodeBorder(view, text);
            var textBlock = Assert.IsType<TextBlock>(node.Child);
            var foreground = Assert.IsType<SolidColorBrush>(textBlock.Foreground).Color;
            Assert.Equal(Colors.Black, foreground);
            Assert.InRange(textBlock.FontSize, 18.665, 18.668);
        }

        var canvas = LogicalDescendants<Canvas>(outer).Single();
        var top = NodeBorder(view, "Top");
        var baseNode = NodeBorder(view, "Base");
        Assert.InRange(Canvas.GetLeft(top), 113.99, 114.01);
        Assert.InRange(Canvas.GetTop(top), 5.99, 6.01);
        Assert.InRange(Canvas.GetLeft(baseNode), 5.99, 6.01);
        Assert.InRange(Canvas.GetTop(baseNode), 110.99, 111.01);
        var polygons = LogicalDescendants<Polygon>(canvas);
        Assert.Equal(4, polygons.Count);
        foreach (var polygon in polygons)
            Assert.Equal(Color.FromRgb(0x7F, 0x00, 0x00), Assert.IsType<SolidColorBrush>(polygon.Fill).Color);
    }

    [StaTheory]
    [InlineData("list1")]
    [InlineData("vertbullet1")]
    public void BasicVerticalListLayouts_RenderSharedGeometryNodePositions(string layoutId)
    {
        var sa = SmartArt.Create(SmartArtKind.List, ["One", "Two", "Three"]);
        sa.LayoutId = layoutId;
        var view = ViewWithSmartArt(sa);

        var one = NodeBorder(view, "One");
        var two = NodeBorder(view, "Two");
        var three = NodeBorder(view, "Three");

        Assert.InRange(Canvas.GetLeft(one), 7, 9);
        Assert.InRange(Canvas.GetTop(one), 7, 9);
        Assert.Equal(Canvas.GetLeft(one), Canvas.GetLeft(two));
        Assert.Equal(Canvas.GetLeft(two), Canvas.GetLeft(three));
        Assert.True(Canvas.GetTop(two) > Canvas.GetTop(one), "second node should be below the first node");
        Assert.True(Canvas.GetTop(three) > Canvas.GetTop(two), "third node should be below the second node");
    }

    [StaFact]
    public void BasicProcessLayout_RendersSharedGeometryNodePositionsAndConnectors()
    {
        var sa = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        sa.LayoutId = "process1";
        var view = ViewWithSmartArt(sa);

        var plan = NodeBorder(view, "Plan");
        var build = NodeBorder(view, "Build");
        var verify = NodeBorder(view, "Verify");

        Assert.InRange(Canvas.GetLeft(plan), 7, 9);
        Assert.InRange(Canvas.GetLeft(build), 93, 95);
        Assert.InRange(Canvas.GetLeft(verify), 179, 181);
        Assert.Equal(Canvas.GetTop(plan), Canvas.GetTop(build));
        Assert.Equal(Canvas.GetTop(build), Canvas.GetTop(verify));

        var connectorLines = LogicalDescendants<Line>(view.Document);
        Assert.True(connectorLines.Count >= 2,
            $"expected shared basic process geometry to render connector lines; got {connectorLines.Count}");
    }

    [StaFact]
    public void BasicProcessLayout_UsesWordDefaultAccentAndFilledArrows()
    {
        var sa = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        sa.LayoutId = "process1";
        var view = ViewWithSmartArt(sa);

        var nodeBorders = new[] { "Plan", "Build", "Verify" }
            .Select(text => NodeBorder(view, text))
            .ToList();

        nodeBorders.Select(border => Assert.IsType<SolidColorBrush>(border.Background).Color)
            .Should().OnlyContain(color => color == Color.FromRgb(0x15, 0x60, 0x82));
        nodeBorders.Should().OnlyContain(border => border.CornerRadius.TopLeft >= 4);

        var arrows = LogicalDescendants<Polygon>(view.Document)
            .Where(polygon => polygon.Fill is SolidColorBrush)
            .ToList();
        arrows.Should().HaveCount(2);
        arrows.Select(polygon => Assert.IsType<SolidColorBrush>(polygon.Fill).Color)
            .Should().OnlyContain(color => color == Color.FromRgb(0xAA, 0xB6, 0xC1));
    }

    [StaFact]
    public void ContinuousBlockProcessLayout_RendersSharedProcessGeometry()
    {
        var sa = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        sa.LayoutId = "continuousBlockProcess";
        var view = ViewWithSmartArt(sa);

        var plan = NodeBorder(view, "Plan");
        var build = NodeBorder(view, "Build");
        var verify = NodeBorder(view, "Verify");

        Assert.InRange(Canvas.GetLeft(plan), 7, 9);
        Assert.InRange(Canvas.GetLeft(build), 87, 89);
        Assert.InRange(Canvas.GetLeft(verify), 167, 169);
        Assert.Equal(Canvas.GetTop(plan), Canvas.GetTop(build));
        Assert.Equal(Canvas.GetTop(build), Canvas.GetTop(verify));

        var connectorLines = LogicalDescendants<Line>(view.Document);
        Assert.True(connectorLines.Count >= 2,
            $"expected shared continuous process geometry to render connector lines; got {connectorLines.Count}");
    }

    [StaFact]
    public void ProcessDiagram_ConnectorFillDiffersFromAdjacentBoxFill()
    {
        var sa = SmartArt.Create(SmartArtKind.Process, ["Idea", "Prototype", "Launch"]);
        var view = ViewWithSmartArt(sa);

        var connectorLines = LogicalDescendants<Line>(view.Document)
            .Where(line => line.X2 > line.X1 && Math.Abs(line.Y2 - line.Y1) < 0.01)
            .ToList();
        Assert.True(connectorLines.Count >= 2, $"expected >=2 connector lines for a 3-node process, got {connectorLines.Count}");

        var borders = LogicalDescendants<Border>(view.Document)
            .Where(b => b.Background is SolidColorBrush)
            .Where(b => b.Background != null)
            .ToList();

        // For each connector, verify its stroke is NOT equal to the fill of its preceding node box.
        // Collect node box colors in order.
        var nodeColors = borders
            .Where(b => b.Background is SolidColorBrush sc &&
                        sc.Color.A > 0 &&
                        (sc.Color.R != 0xFF || sc.Color.G != 0xFF || sc.Color.B != 0xFF))
            .Select(b => ((SolidColorBrush)b.Background!).Color)
            .ToList();

        var connectorColors = connectorLines
            .Where(line => line.Stroke is SolidColorBrush)
            .Select(line => ((SolidColorBrush)line.Stroke!).Color)
            .ToList();

        // Each connector color must differ from the preceding node box color.
        for (var i = 0; i < Math.Min(connectorColors.Count, nodeColors.Count); i++)
        {
            Assert.NotEqual(nodeColors[i], connectorColors[i]);
        }
    }
}
