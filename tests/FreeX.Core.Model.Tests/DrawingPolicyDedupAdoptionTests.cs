using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class DrawingPolicyDedupAdoptionTests
{
    [Fact]
    public void ThemeConsumers_UseSharedNativeFontSchemePatcher()
    {
        var workbookTheme = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.Core.Model", "WorkbookTheme.cs");
        var presentationWriter = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "freep", "FreeP.Core.IO", "PptxPackageWriter.cs");

        workbookTheme.Should().Contain("DrawingMlThemeXml")
            .And.Contain(".TryPatchNativeFontScheme(")
            .And.NotContain("private static string? PatchNativeFontScheme(")
            .And.NotContain("private static XElement? TryParseFontScheme(");
        presentationWriter.Should().Contain("DrawingMlThemeXml.TryPatchNativeFontScheme(")
            .And.NotContain("private static XElement? TryPatchNativeFontScheme(");
    }

    [Fact]
    public void OutlineDashRenderers_ReadDashArraysFromSharedPresetTable()
    {
        var sites = new (string[] Path, string[] Forbidden)[]
        {
            (["freep", "FreeP.App.Rendering.Avalonia", "SlideCanvas.cs"], ["OutlineDash.LongDash       =>", "DashStyle.DashDot,"]),
            (["freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs"], ["OutlineDash.LongDash       =>", "DashStyles.DashDot,"]),
            (["src", "FreeX.App.Avalonia", "MainWindow.cs"], ["DrawingShapeOutlineDash.LongDash", "=> [2, 2]"]),
            (["src", "FreeX.App.UI", "GridView.DrawingObjects.cs"], ["DrawingShapeOutlineDash.LongDash", "DashStyles.DashDot"]),
        };

        foreach (var (path, forbidden) in sites)
        {
            var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(path);
            var site = string.Join('/', path);

            source.Should().Contain("DrawingMlPresetDashPattern.", $"{site} must read dash arrays from the shared table");
            source.Should().Contain(".ToPresetDash()", $"{site} must map its outline dash enum to the shared preset");
            foreach (var token in forbidden)
                source.Should().NotContain(token, $"{site} must not grow a local dash-array switch");
            source.Should().NotContain("new DashStyle([8.0", $"{site} must not hard-code preset dash lengths");
            source.Should().NotContain("new DashStyle(new[] { 8.0", $"{site} must not hard-code preset dash lengths");
        }
    }

    [Fact]
    public void WpfContourToGeometryConsumers_DelegateToSharedWpfShapeGeometryAdapter()
    {
        var freeX = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.UI", "ShapeGeometryWpfAdapter.cs");
        var freeP = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "freep", "FreeP.App.Rendering.Wpf", "SlideCanvas.cs");

        foreach (var (site, source) in new[] { ("FreeX.App.UI", freeX), ("FreeP.App.Rendering.Wpf", freeP) })
        {
            source.Should().Contain("WpfShapeGeometryAdapter.ToGeometry(", $"{site} must use the shared contour adapter")
                .And.NotContain("ShapeSegmentKind.", $"{site} must not replay contour segments itself")
                .And.NotContain("private static void AppendContour(");
        }
    }

    [Fact]
    public void GeometryAndHandleConsumers_UseSharedPresetAdjustmentMath()
    {
        var builder = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "shared", "Free.Shared.Drawing", "ShapeGeometryBuilder.cs");
        var planner = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "freep", "FreeP.App.Presentation", "ShapeGeometryAdjustmentPlanner.cs");

        builder.Should().Contain("PresetShapeAdjustmentMath.RoundedRectangleCornerRadius(")
            .And.Contain("PresetShapeAdjustmentMath.RibbonBandTop(")
            .And.NotContain("private static double CornerRadius(");
        planner.Should().Contain("PresetShapeAdjustmentMath.RoundedRectangleCornerRadius(")
            .And.Contain("PresetShapeAdjustmentMath.RibbonBandTop(")
            .And.NotContain("private static double ResolveRoundedRectangleCornerRadius(")
            .And.NotContain("private static double ResolveRibbonBandTop(");
    }
}
