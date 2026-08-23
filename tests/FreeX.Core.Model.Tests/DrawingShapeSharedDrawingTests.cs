using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DrawingShapeSharedDrawingTests
{
    public static TheoryData<DrawingShapeKind, string> CanonicalPresetCases =>
        new()
        {
            { DrawingShapeKind.Rectangle, "rect" },
            { DrawingShapeKind.RoundedRectangle, "roundRect" },
            { DrawingShapeKind.Ellipse, "ellipse" },
            { DrawingShapeKind.Line, "line" },
            { DrawingShapeKind.ElbowConnector, "bentConnector2" },
            { DrawingShapeKind.CurvedConnector, "curvedConnector2" },
            { DrawingShapeKind.Triangle, "triangle" },
            { DrawingShapeKind.RightTriangle, "rtTriangle" },
            { DrawingShapeKind.Diamond, "diamond" },
            { DrawingShapeKind.Parallelogram, "parallelogram" },
            { DrawingShapeKind.Trapezoid, "trapezoid" },
            { DrawingShapeKind.Pentagon, "pentagon" },
            { DrawingShapeKind.Hexagon, "hexagon" },
            { DrawingShapeKind.Octagon, "octagon" },
            { DrawingShapeKind.Cross, "cross" },
            { DrawingShapeKind.RightArrow, "rightArrow" },
            { DrawingShapeKind.LeftArrow, "leftArrow" },
            { DrawingShapeKind.UpArrow, "upArrow" },
            { DrawingShapeKind.DownArrow, "downArrow" },
            { DrawingShapeKind.LeftRightArrow, "leftRightArrow" },
            { DrawingShapeKind.UpDownArrow, "upDownArrow" },
            { DrawingShapeKind.PlusSign, "mathPlus" },
            { DrawingShapeKind.MinusSign, "mathMinus" },
            { DrawingShapeKind.MultiplySign, "mathMultiply" },
            { DrawingShapeKind.DivideSign, "mathDivide" },
            { DrawingShapeKind.EqualSign, "mathEqual" },
            { DrawingShapeKind.NotEqualSign, "mathNotEqual" },
            { DrawingShapeKind.FlowchartProcess, "flowChartProcess" },
            { DrawingShapeKind.FlowchartDecision, "flowChartDecision" },
            { DrawingShapeKind.FlowchartData, "flowChartInputOutput" },
            { DrawingShapeKind.FlowchartPredefinedProcess, "flowChartPredefinedProcess" },
            { DrawingShapeKind.FlowchartDocument, "flowChartDocument" },
            { DrawingShapeKind.FlowchartTerminator, "flowChartTerminator" },
            { DrawingShapeKind.Star5, "star5" },
            { DrawingShapeKind.Star8, "star8" },
            { DrawingShapeKind.Explosion, "irregularSeal1" },
            { DrawingShapeKind.Ribbon, "ribbon" },
            { DrawingShapeKind.Wave, "wave" },
            { DrawingShapeKind.RectangularCallout, "wedgeRectCallout" },
            { DrawingShapeKind.RoundedRectangularCallout, "wedgeRoundRectCallout" },
            { DrawingShapeKind.OvalCallout, "wedgeEllipseCallout" },
            { DrawingShapeKind.LineCallout, "lineCallout1" },
            { DrawingShapeKind.Chevron, "chevron" },
            { DrawingShapeKind.HomePlate, "homePlate" },
            { DrawingShapeKind.Cylinder, "can" },
            { DrawingShapeKind.QuadArrow, "quadArrow" },
        };

    public static TheoryData<string, DrawingShapeKind, string> AliasPresetCases =>
        new()
        {
            { "roundrect", DrawingShapeKind.RoundedRectangle, "roundRect" },
            { "straightConnector1", DrawingShapeKind.Line, "line" },
            { "bentConnector5", DrawingShapeKind.ElbowConnector, "bentConnector2" },
            { "curvedConnector5", DrawingShapeKind.CurvedConnector, "curvedConnector2" },
            { "flowchartinputoutput", DrawingShapeKind.FlowchartData, "flowChartInputOutput" },
            { "irregularSeal2", DrawingShapeKind.Explosion, "irregularSeal1" },
            { "ribbon2", DrawingShapeKind.Ribbon, "ribbon" },
            { "lineCallout4", DrawingShapeKind.LineCallout, "lineCallout1" },
            { "borderCallout4", DrawingShapeKind.LineCallout, "lineCallout1" },
        };

    [Fact]
    public void DrawingShapeModel_UsesSharedDrawingShapeKind()
    {
        var kindProperty = typeof(DrawingShapeModel).GetProperty(nameof(DrawingShapeModel.Kind));

        kindProperty.Should().NotBeNull();
        kindProperty!.PropertyType.Should().Be(typeof(DrawingShapeKind));
        kindProperty.PropertyType.Assembly.FullName.Should().Be(typeof(DrawingShapeKindSupport).Assembly.FullName);
        kindProperty.PropertyType.Namespace.Should().Be("Free.Shared.Drawing");
    }

    [Fact]
    public void CoreModelShapeKindSources_RemainNeutralized()
    {
        var sharedRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("shared", "Free.Shared.Drawing");
        var coreModelRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.Model");

        File.Exists(Path.Combine(sharedRoot, "DrawingShapeKind.cs"))
            .Should()
            .BeTrue("DrawingShapeKind should remain owned by Free.Shared.Drawing");
        File.Exists(Path.Combine(sharedRoot, "DrawingShapeKindSupport.cs"))
            .Should()
            .BeTrue("DrawingShapeKindSupport should remain owned by Free.Shared.Drawing");
        File.Exists(Path.Combine(coreModelRoot, "DrawingShapeKindSupport.cs"))
            .Should()
            .BeFalse("Core.Model should consume the shared shape-kind support instead of keeping a facade copy");

        var coreModelSources = Directory
            .EnumerateFiles(coreModelRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        coreModelSources.Should().NotContain(source => source.Contains("public enum DrawingShapeKind", StringComparison.Ordinal));
        coreModelSources.Should().NotContain(source => source.Contains("public static class DrawingShapeKindSupport", StringComparison.Ordinal));
    }

    [Fact]
    public void SharedShapeKindSupport_PreservesRenderableCatalog()
    {
        DrawingShapeKindSupport.IsRenderable(DrawingShapeKind.Rectangle).Should().BeTrue();
        DrawingShapeKindSupport.IsRenderable(DrawingShapeKind.Cylinder).Should().BeTrue();
        DrawingShapeKindSupport.IsLineLike(DrawingShapeKind.Line).Should().BeTrue();
        DrawingShapeKindSupport.IsLineLike(DrawingShapeKind.Cylinder).Should().BeFalse();
        ((int)DrawingShapeKind.Cylinder).Should().Be(44);
        DrawingShapeKindSupport.IsRenderable(DrawingShapeKind.QuadArrow).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(CanonicalPresetCases))]
    public void DrawingMlPresetGeometryMap_RoundTripsCanonicalPresets(
        DrawingShapeKind kind,
        string preset)
    {
        DrawingMlPresetGeometryMap.GetPreset(kind).Should().Be(preset);

        DrawingMlPresetGeometryMap.TryGetShapeKind(preset, out var parsed).Should().BeTrue();
        parsed.Should().Be(kind);
    }

    [Theory]
    [MemberData(nameof(AliasPresetCases))]
    public void DrawingMlPresetGeometryMap_MapsAliasesToCanonicalKinds(
        string alias,
        DrawingShapeKind kind,
        string canonicalPreset)
    {
        DrawingMlPresetGeometryMap.TryGetShapeKind(alias, out var parsed).Should().BeTrue();
        parsed.Should().Be(kind);
        DrawingMlPresetGeometryMap.GetPreset(parsed).Should().Be(canonicalPreset);
    }

    [Fact]
    public void DrawingMlPresetGeometryMap_ReturnsCallerFallbackForUnknownPreset()
    {
        DrawingMlPresetGeometryMap.TryGetShapeKind("freeform", out _).Should().BeFalse();

        DrawingMlPresetGeometryMap.GetShapeKindOrDefault("freeform", DrawingShapeKind.Line)
            .Should()
            .Be(DrawingShapeKind.Line);
    }

    [Fact]
    public void AppIoPresetGeometryMaps_DelegateToSharedDrawing()
    {
        var sharedRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("shared", "Free.Shared.Drawing");
        var freeXIoRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.Core.IO");

        File.Exists(Path.Combine(sharedRoot, "DrawingMlPresetGeometryMap.cs"))
            .Should()
            .BeTrue("DrawingML preset geometry mapping should remain owned by Free.Shared.Drawing");

        var writerSource = File.ReadAllText(Path.Combine(freeXIoRoot, "XlsxWorksheetDrawingObjectWriter.cs"));
        var partsSource = File.ReadAllText(Path.Combine(freeXIoRoot, "XlsxWorksheetDrawingParts.cs"));

        writerSource.Should().Contain("DrawingMlPresetGeometryMap.GetPreset");
        partsSource.Should().Contain("DrawingMlPresetGeometryMap.TryGetShapeKind");
        partsSource.Should().Contain("DrawingMlPresetGeometryMap.GetShapeKindOrDefault");

        writerSource.Should().NotContain("private static string ToDrawingPreset");
        partsSource.Should().NotContain("private static DrawingShapeKind? ToDrawingShapeKind");
        writerSource.Should().NotContain("DrawingShapeKind.Cylinder => \"can\"");
        partsSource.Should().NotContain("\"can\" => DrawingShapeKind.Cylinder");
    }
}
