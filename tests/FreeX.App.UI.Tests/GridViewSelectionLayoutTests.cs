using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using System.IO;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed class GridViewSelectionLayoutTests
{
    [Fact]
    public void CalculateVisibleSelectionRect_ReturnsNullWhenSelectedRowIsNotVisible()
    {
        var sheetId = SheetId.New();
        var viewport = Viewport();
        var range = new GridRange(
            new CellAddress(sheetId, 5, 2),
            new CellAddress(sheetId, 5, 2));

        var rect = GridView.CalculateVisibleSelectionRect(
            viewport,
            range,
            GridView.RowHeaderWidth,
            GridView.ColHeaderHeight);

        rect.Should().BeNull();
    }

    [Fact]
    public void CalculateVisibleSelectionRect_ReturnsNullWhenSelectedColumnIsNotVisible()
    {
        var sheetId = SheetId.New();
        var viewport = Viewport();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 5),
            new CellAddress(sheetId, 2, 5));

        var rect = GridView.CalculateVisibleSelectionRect(
            viewport,
            range,
            GridView.RowHeaderWidth,
            GridView.ColHeaderHeight);

        rect.Should().BeNull();
    }

    [Fact]
    public void CalculateVisibleSelectionRect_UsesVisibleIntersectionForWholeRowSelection()
    {
        var sheetId = SheetId.New();
        var viewport = Viewport();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 2, CellAddress.MaxCol));

        var rect = GridView.CalculateVisibleSelectionRect(
            viewport,
            range,
            GridView.RowHeaderWidth,
            GridView.ColHeaderHeight);

        rect.Should().Be(new Rect(30, 38, 200, 20));
    }

    [Fact]
    public void CalculateVisibleSelectionRect_DoesNotTreatNonContiguousFrozenMetricsAsContinuous()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(20, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(20, 64, 64)]);
        var range = new GridRange(
            new CellAddress(sheetId, 10, 10),
            new CellAddress(sheetId, 10, 10));

        var rect = GridView.CalculateVisibleSelectionRect(
            viewport,
            range,
            GridView.RowHeaderWidth,
            GridView.ColHeaderHeight);

        rect.Should().BeNull();
    }

    [Fact]
    public void CalculateClipboardMarquee_ReturnsVisibleRectangle_ForCopiedRange()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 3, 3));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(3, 20, 40)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64), new ColMetric(3, 64, 128)]);

        var rect = GridView.CalculateClipboardMarquee(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rect.Should().Be(new Rect(94, 38, 128, 40));
    }

    [Fact]
    public void CalculateClipboardMarquee_ReturnsNull_WhenRangeIsOutsideViewport()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 10, 10),
            new CellAddress(sheetId, 11, 11));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)]);

        GridView.CalculateClipboardMarquee(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18).Should().BeNull();
    }

    [Fact]
    public void CalculateQuickAnalysisPreviewRect_ReturnsVisibleRectangle_ForHoverRange()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 2),
            new CellAddress(sheetId, 3, 3));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(3, 20, 40)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64), new ColMetric(3, 64, 128)]);

        var rect = GridView.CalculateQuickAnalysisPreviewRect(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rect.Should().Be(new Rect(94, 38, 128, 40));
    }

    [Fact]
    public void CalculateQuickAnalysisDataBarPreviewRects_ReturnsProportionalBarsForVisibleNumericCells()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2, 2));
        var viewport = new ViewportModel(
            [
                new DisplayCell(1, 1, new NumberValue(10), "10", null, StyleId.Default, null),
                new DisplayCell(1, 2, new NumberValue(20), "20", null, StyleId.Default, null),
                new DisplayCell(2, 1, new TextValue("n/a"), "n/a", null, StyleId.Default, null),
                new DisplayCell(2, 2, new NumberValue(0), "0", null, StyleId.Default, null)
            ],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)]);

        var rects = GridView.CalculateQuickAnalysisDataBarPreviewRects(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rects.Should().Equal(
            new Rect(33, 22, 29, 12),
            new Rect(97, 22, 58, 12),
            new Rect(97, 42, 0, 12));
    }

    [Fact]
    public void CalculateQuickAnalysisDataBarPreviewRects_CalculatesMaxWithoutNumericCellList()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "QuickAnalysisPreviewLayoutPlanner.cs"));
        var dataBars = source[
            source.IndexOf("public static IReadOnlyList<Rect> CalculateDataBarPreviewRects", StringComparison.Ordinal)..
            source.IndexOf("public static IReadOnlyList<Rect> CalculateCellPreviewRects", StringComparison.Ordinal)];

        dataBars.Should().Contain("var hasNumericCell = false;");
        dataBars.Should().Contain("hasNumericCell = true;");
        dataBars.Should().Contain("if (positiveValue > max)");
        dataBars.Should().Contain("foreach (var cell in viewport.Cells)");
        dataBars.Should().Contain("IsCellInRange(cell, range)");
        dataBars.Should().Contain("BuildRowMetricLookup(viewport.RowMetrics)");
        dataBars.Should().Contain("BuildColMetricLookup(viewport.ColMetrics)");
        dataBars.Should().NotContain("numericCells");
        dataBars.Should().NotContain(".Where(");
        dataBars.Should().NotContain(".Select(");
        dataBars.Should().NotContain(".DefaultIfEmpty(");
        dataBars.Should().NotContain(".ToDictionary(");
        source.Should().Contain("new Dictionary<uint, RowMetric>(metrics.Count)");
        source.Should().Contain("new Dictionary<uint, ColMetric>(metrics.Count)");
    }

    [Fact]
    public void CalculateQuickAnalysisCellAndSparklinePreviews_AvoidFilteredMetricLists()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "QuickAnalysisPreviewLayoutPlanner.cs"));
        var cellPreview = source[
            source.IndexOf("public static IReadOnlyList<Rect> CalculateCellPreviewRects", StringComparison.Ordinal)..
            source.IndexOf("public static IReadOnlyList<Rect> CalculateSparklinePreviewRects", StringComparison.Ordinal)];
        var sparklinePreview = source[
            source.IndexOf("public static IReadOnlyList<Rect> CalculateSparklinePreviewRects", StringComparison.Ordinal)..
            source.IndexOf("private static bool TryGetPreviewNumber", StringComparison.Ordinal)];

        cellPreview.Should().Contain("foreach (var row in viewport.RowMetrics)");
        cellPreview.Should().Contain("foreach (var col in viewport.ColMetrics)");
        cellPreview.Should().NotContain(".Where(");
        cellPreview.Should().NotContain(".ToList()");
        sparklinePreview.Should().Contain("FirstVisibleColumnInRange(viewport.ColMetrics, range)");
        sparklinePreview.Should().Contain("foreach (var row in viewport.RowMetrics)");
        sparklinePreview.Should().NotContain("FirstOrDefault");
        sparklinePreview.Should().NotContain(".Where(");
        sparklinePreview.Should().NotContain(".ToList()");
    }

    [Fact]
    public void CalculateQuickAnalysisCellPreviewRects_ReturnsInsetCellsForColorScalePreview()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2, 2));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64)]);

        var rects = GridView.CalculateQuickAnalysisCellPreviewRects(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rects.Should().Equal(
            new Rect(33, 21, 58, 14),
            new Rect(97, 21, 58, 14),
            new Rect(33, 41, 58, 14),
            new Rect(97, 41, 58, 14));
    }

    [Fact]
    public void CalculateQuickAnalysisSparklinePreviewRects_ReturnsCompactRectPerVisibleTargetRow()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 3),
            new CellAddress(sheetId, 3, 3));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 24, 20), new RowMetric(4, 20, 44)],
            [new ColMetric(1, 64, 0), new ColMetric(3, 72, 64)]);

        var rects = GridView.CalculateQuickAnalysisSparklinePreviewRects(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rects.Should().Equal(
            new Rect(100, 25, 60, 6),
            new Rect(100, 46, 60, 8));
    }

    [Fact]
    public void CalculateQuickAnalysisSparklinePreviewRects_SkipsTinyTargetsThatCannotContainPreviewBars()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 3),
            new CellAddress(sheetId, 1, 3));
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(3, 10, 64)]);

        var rects = GridView.CalculateQuickAnalysisSparklinePreviewRects(
            viewport,
            range,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18);

        rects.Should().BeEmpty();
    }

    private static ViewportModel Viewport() =>
        new(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(3, 20, 40)],
            [new ColMetric(1, 60, 0), new ColMetric(2, 80, 60), new ColMetric(3, 60, 140)]);

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(relativeParts));
    }
}
