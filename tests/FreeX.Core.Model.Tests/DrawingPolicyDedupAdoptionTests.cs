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
