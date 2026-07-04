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

    // ── Bug #5: process arrow fill contrasts with box fill ───────────────────────────────────────

    /// <summary>
    /// A process diagram with 3 nodes must have at least 2 arrow Polygons, and each arrow's Fill must
    /// differ from the fill of the node box that precedes it. Before the fix the arrow had the same
    /// color as the box (invisible), making the topology indistinguishable from a list.
    /// </summary>
    [StaFact]
    public void ProcessDiagram_ArrowFillDiffersFromAdjacentBoxFill()
    {
        var sa = SmartArt.Create(SmartArtKind.Process, ["Idea", "Prototype", "Launch"]);
        var view = ViewWithSmartArt(sa);

        var polygons = LogicalDescendants<Polygon>(view.Document);
        Assert.True(polygons.Count >= 2, $"expected ≥2 arrow polygons for a 3-node process, got {polygons.Count}");

        var borders = LogicalDescendants<Border>(view.Document)
            .Where(b => b.Background is SolidColorBrush)
            .Where(b => b.Background != null)
            .ToList();

        // For each arrow, verify its fill is NOT equal to the fill of its preceding node box.
        // Borders and Polygons are added in order: [box0, arrow0, box1, arrow1, box2].
        // Collect node box colors in order.
        var nodeColors = borders
            .Where(b => b.Background is SolidColorBrush sc &&
                        sc.Color.A > 0 &&
                        (sc.Color.R != 0xFF || sc.Color.G != 0xFF || sc.Color.B != 0xFF))
            .Select(b => ((SolidColorBrush)b.Background!).Color)
            .ToList();

        var arrowColors = polygons
            .Where(p => p.Fill is SolidColorBrush)
            .Select(p => ((SolidColorBrush)p.Fill!).Color)
            .ToList();

        // Each arrow color must differ from the preceding node box color.
        for (var i = 0; i < Math.Min(arrowColors.Count, nodeColors.Count); i++)
        {
            Assert.NotEqual(nodeColors[i], arrowColors[i]);
        }
    }
}
