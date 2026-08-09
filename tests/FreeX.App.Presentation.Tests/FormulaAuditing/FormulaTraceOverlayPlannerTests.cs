using FluentAssertions;
using FreeX.App.Presentation.FormulaAuditing;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.FormulaAuditing;

public sealed class FormulaTraceOverlayPlannerTests
{
    [Fact]
    public void MetricOffsetProjection_ResolvesVisibleEndpointsAndArrowKind()
    {
        var sheetId = SheetId.New();
        var viewport = CreateViewport(
            [new RowMetric(1, 20, 0), new RowMetric(2, 24, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)]);

        var layouts = FormulaTraceOverlayPlanner.CalculateLayouts(
            viewport,
            [new FormulaTraceArrow(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 2, 2),
                FormulaTraceArrowKind.Dependent)],
            sheetId,
            FormulaTraceViewportProjection.FromMetricOffsets(30, 18),
            FormulaTraceOverlayProfiles.Wpf);

        layouts.Should().ContainSingle().Which.Should().Be(new FormulaTraceArrowLayout(
            new LayoutPoint(62, 28),
            new LayoutPoint(134, 50),
            ArrowKind: FormulaTraceArrowKind.Dependent));
    }

    [Fact]
    public void SequentialProjection_PreservesAvaloniaMinimumSizesZoomAndHeadingOrigins()
    {
        var sheetId = SheetId.New();
        var viewport = CreateViewport(
            [new RowMetric(1, 10, 500), new RowMetric(4, 30, 900)],
            [new ColMetric(1, 20, 700), new ColMetric(4, 60, 1200)]);
        var projection = FormulaTraceViewportProjection.FromSequentialVisibleMetrics(
            rowHeaderWidth: 70,
            columnHeaderHeight: 40,
            zoomFactor: 2,
            minimumColumnWidth: 48,
            minimumRowHeight: 20);

        var layouts = FormulaTraceOverlayPlanner.CalculateLayouts(
            viewport,
            [new FormulaTraceArrow(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 4, 4))],
            sheetId,
            projection,
            FormulaTraceOverlayProfiles.Avalonia);

        layouts.Should().ContainSingle().Which.Should().Be(new FormulaTraceArrowLayout(
            new LayoutPoint(118, 60),
            new LayoutPoint(226, 110)));
    }

    [Fact]
    public void WpfProfile_RoutesOffscreenAndCrossSheetEndpointsToMarkers()
    {
        var sheetId = SheetId.New();
        var otherSheetId = SheetId.New();
        var visible = new CellAddress(sheetId, 1, 1);
        var offscreen = new CellAddress(sheetId, 8, 1);
        var crossSheet = new CellAddress(otherSheetId, 1, 1);
        var viewport = CreateViewport(
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 64, 0)]);
        var projection = FormulaTraceViewportProjection.FromMetricOffsets(30, 18);

        var layouts = FormulaTraceOverlayPlanner.CalculateLayouts(
            viewport,
            [new FormulaTraceArrow(visible, offscreen), new FormulaTraceArrow(crossSheet, visible)],
            sheetId,
            projection,
            FormulaTraceOverlayProfiles.Wpf);

        layouts.Should().Equal(
            new FormulaTraceArrowLayout(
                new LayoutPoint(62, 28),
                new LayoutPoint(62, 28),
                FormulaTraceArrowLayoutKind.OffscreenMarker,
                offscreen),
            new FormulaTraceArrowLayout(
                new LayoutPoint(62, 28),
                new LayoutPoint(62, 28),
                FormulaTraceArrowLayoutKind.CrossSheetMarker,
                crossSheet));
        FormulaTraceOverlayPlanner.HitTestMarker(
                viewport,
                [new FormulaTraceArrow(visible, offscreen)],
                sheetId,
                projection,
                FormulaTraceOverlayProfiles.Wpf,
                new LayoutPoint(69.9, 28))
            .Should().Be(offscreen);
        FormulaTraceOverlayPlanner.HitTestMarker(
                viewport,
                [new FormulaTraceArrow(visible, offscreen)],
                sheetId,
                projection,
                FormulaTraceOverlayProfiles.Wpf,
                new LayoutPoint(70.1, 28))
            .Should().BeNull();
    }

    [Fact]
    public void AvaloniaProfile_HidesMarkersAndCoincidentVisibleArrows()
    {
        var sheetId = SheetId.New();
        var viewport = CreateViewport(
            [new RowMetric(1, 0, 0)],
            [new ColMetric(1, 0, 0)]);
        var projection = FormulaTraceViewportProjection.FromSequentialVisibleMetrics(0, 0, 1, 0, 0);

        var layouts = FormulaTraceOverlayPlanner.CalculateLayouts(
            viewport,
            [
                new FormulaTraceArrow(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 1, 1)),
                new FormulaTraceArrow(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 2, 1))
            ],
            sheetId,
            projection,
            FormulaTraceOverlayProfiles.Avalonia);

        layouts.Should().BeEmpty();
    }

    [Fact]
    public void RendererProfiles_PinEstablishedStyleDifferences()
    {
        var wpf = FormulaTraceOverlayProfiles.Wpf;
        var avalonia = FormulaTraceOverlayProfiles.Avalonia;

        wpf.MarkerMode.Should().Be(FormulaTraceMarkerMode.VisibleEndpoint);
        wpf.Style.PrecedentColor.Should().Be(new FormulaTraceColor(0, 102, 204));
        wpf.Style.DependentColor.Should().Be(wpf.Style.PrecedentColor);
        wpf.Style.SourceMarkerRadius.Should().Be(0);
        wpf.Style.ArrowHeadLength.Should().Be(8);
        wpf.Style.ArrowHeadHalfWidth.Should().Be(4);

        avalonia.MarkerMode.Should().Be(FormulaTraceMarkerMode.None);
        avalonia.Style.PrecedentColor.Should().Be(new FormulaTraceColor(0, 102, 51));
        avalonia.Style.DependentColor.Should().Be(new FormulaTraceColor(0, 86, 179));
        avalonia.Style.SourceMarkerRadius.Should().Be(3);
        avalonia.Style.ArrowHeadLength.Should().Be(10);
        avalonia.Style.ArrowHeadHalfWidth.Should().Be(5);
    }

    [Fact]
    public void ArrowHeadGeometry_IsPortableAndDeterministic()
    {
        var geometry = FormulaTraceOverlayGeometryPlanner.CalculateArrowHead(
            new LayoutPoint(10, 10),
            new LayoutPoint(30, 10),
            FormulaTraceOverlayProfiles.Avalonia.Style);

        geometry.Should().Be(new FormulaTraceArrowHeadGeometry(
            IsVisible: true,
            Tip: new LayoutPoint(30, 10),
            Left: new LayoutPoint(20, 15),
            Right: new LayoutPoint(20, 5)));
    }

    [Fact]
    public void SourceOwnership_KeepsRenderersAsNativeAdapters()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sharedOwner = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Presentation",
            "FormulaAuditing",
            "FormulaTraceOverlayPlanner.cs"));
        var wpfAdapter = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.UI", "FormulaTraceLayoutPlanner.cs"));
        var wpfRenderer = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var avaloniaRenderer = File.ReadAllText(Path.Combine(
            root,
            "src",
            "FreeX.App.Avalonia",
            "MainWindow.FormulaAuditing.cs"));

        sharedOwner.Should().Contain("public static class FormulaTraceOverlayPlanner");
        sharedOwner.Should().Contain("public static class FormulaTraceOverlayGeometryPlanner");
        wpfAdapter.Should().Contain("FormulaTraceOverlayPlanner.CalculateLayouts");
        wpfAdapter.Should().NotContain("for (");
        wpfRenderer.Should().Contain("FormulaTraceOverlayGeometryPlanner.CalculateArrowHead");
        wpfRenderer.Should().NotContain("const double arrowHeadLength");
        avaloniaRenderer.Should().Contain("FormulaTraceOverlayPlanner.CalculateLayouts");
        avaloniaRenderer.Should().Contain("FormulaTraceOverlayGeometryPlanner.CalculateArrowHead");
        avaloniaRenderer.Should().NotContain("TryGetDisplayedCellBounds(viewport, arrow.From");
        avaloniaRenderer.Should().NotContain("Math.Sqrt");
    }

    private static ViewportModel CreateViewport(
        IReadOnlyList<RowMetric> rows,
        IReadOnlyList<ColMetric> columns) =>
        new([], rows, columns, null, []);
}
