using System;
using System.IO;
using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed partial class GridViewDrawingObjectThemeTests
{
    [Fact]
    public void TryCreateDrawingAnchorRect_MapsTwoCellAnchorToViewportPixels()
    {
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(3, 20, 0),
                new RowMetric(4, 20, 20),
                new RowMetric(5, 20, 40)
            ],
            [
                new ColMetric(2, 80, 0),
                new ColMetric(3, 80, 80),
                new ColMetric(4, 80, 160)
            ]);
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 95250, 2, 190500),
            new DrawingAnchorPoint(3, 47625, 4, 95250));

        var created = GridView.TryCreateDrawingAnchorRect(
            viewport,
            anchor,
            rowHeaderWidth: 30,
            columnHeaderHeight: 18,
            out var rect);

        created.Should().BeTrue();
        rect.Should().Be(new Rect(40, 38, 155, 30));
    }

    [Fact]
    public void TryCreateDrawingAnchorRect_UsesFirstMatchingAnchorMetrics()
    {
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(3, 20, 0),
                new RowMetric(5, 20, 40),
                new RowMetric(3, 20, 200)
            ],
            [
                new ColMetric(2, 80, 0),
                new ColMetric(4, 80, 160),
                new ColMetric(2, 80, 300)
            ]);
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(1, 0, 2, 0),
            new DrawingAnchorPoint(3, 0, 4, 0));

        GridView.TryCreateDrawingAnchorRect(
                viewport,
                anchor,
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                out var rect)
            .Should()
            .BeTrue();
        rect.Should().Be(new Rect(30, 18, 160, 40));
    }

    [Fact]
    public void TryCreateDrawingAnchorRect_ReturnsFalseForMaxValueAnchorPoint()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 80, 0)]);
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(uint.MaxValue, 0, 0, 0),
            new DrawingAnchorPoint(0, 0, 0, 0));

        GridView.TryCreateDrawingAnchorRect(
                viewport,
                anchor,
                rowHeaderWidth: 30,
                columnHeaderHeight: 18,
                out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryCreateDrawingAnchorRect_UsesSinglePassAnchorMetricLookups()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridDrawingObjectPlanner.cs"));
        var anchorRange = source[
            source.IndexOf("public static bool TryCreateDrawingAnchorRect", StringComparison.Ordinal)..
            source.IndexOf("public static bool TryCreateAnchoredObjectRect", StringComparison.Ordinal)];
        var anchorHelpers = source[
            source.IndexOf("private static bool TryGetAnchorPoints", StringComparison.Ordinal)..
            source.IndexOf("private static double EmusToPixels", StringComparison.Ordinal)];

        anchorRange.Should().Contain("TryGetAnchorPoints(viewport, anchor");
        anchorRange.Should().NotContain("TryGetAnchorPoint(viewport, anchor.From");
        anchorRange.Should().NotContain("TryGetAnchorPoint(viewport, anchor.To");
        anchorHelpers.Should().Contain("TryFindAnchorColumns(viewport.ColMetrics");
        anchorHelpers.Should().Contain("TryFindAnchorRows(viewport.RowMetrics");
        anchorHelpers.Should().Contain("foreach (var metric in metrics)");
        anchorHelpers.Should().Contain("if (metric.Col > toColumn)");
        anchorHelpers.Should().Contain("if (metric.Row > toRow)");
        anchorHelpers.Should().Contain("break;");
        anchorHelpers.Should().NotContain("FirstOrDefault");
        anchorHelpers.Should().NotContain(".Where(");
        anchorHelpers.Should().NotContain(".ToList()");
    }

    [Fact]
    public void AnchoredObjectRendering_UsesSharedSinglePassMetricPlanner()
    {
        var planner = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridDrawingObjectPlanner.cs"));
        var drawingObjects = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var pictures = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var plannerMethod = planner[
            planner.IndexOf("public static bool TryCreateAnchoredObjectRect", StringComparison.Ordinal)..
            planner.IndexOf("public static string GetNativeControlCaption", StringComparison.Ordinal)];
        var anchorHelpers = planner[
            planner.IndexOf("private static bool TryFindAnchorRow", StringComparison.Ordinal)..
            planner.IndexOf("private static double EmusToPixels", StringComparison.Ordinal)];
        var renderTextBoxes = drawingObjects[
            drawingObjects.IndexOf("private void RenderTextBoxes", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)];
        var renderDrawingShapes = drawingObjects[
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)];
        var renderNativeControls = drawingObjects[
            drawingObjects.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)..
            drawingObjects.IndexOf("public static bool TryCreateDrawingAnchorRect", StringComparison.Ordinal)];
        var renderPlaceholders = drawingObjects[
            drawingObjects.IndexOf("private void RenderObjectPlaceholders", StringComparison.Ordinal)..
            drawingObjects.IndexOf("public static string CreateObjectPlaceholderLabel", StringComparison.Ordinal)];
        var renderPictures = pictures[
            pictures.IndexOf("private void RenderPictures", StringComparison.Ordinal)..
            pictures.IndexOf("private void DrawPictureSelectionAdorner", StringComparison.Ordinal)];

        plannerMethod.Should().Contain("TryFindAnchorRow(viewport.RowMetrics, anchor.Row");
        plannerMethod.Should().Contain("TryFindAnchorColumn(viewport.ColMetrics, anchor.Col");
        plannerMethod.Should().Contain("IReadOnlyDictionary<uint, RowMetric> rows");
        plannerMethod.Should().Contain("rows.TryGetValue(anchor.Row");
        plannerMethod.Should().Contain("columns.TryGetValue(anchor.Col");
        planner.Should().Contain("IReadOnlyDictionary<uint, RowMetric> rows");
        planner.Should().Contain("IReadOnlyDictionary<uint, ColMetric> columns");
        planner.Should().Contain("DrawingAnchorRange anchor");
        planner.Should().Contain("rows.TryGetValue(fromRowIndex");
        planner.Should().Contain("columns.TryGetValue(fromColumnIndex");
        plannerMethod.Should().NotContain("FirstOrDefault");
        anchorHelpers.Should().Contain("if (metric.Row > row)");
        anchorHelpers.Should().Contain("if (metric.Col > column)");
        anchorHelpers.Should().Contain("break;");
        renderTextBoxes.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        renderTextBoxes.Should().Contain("metricLookups,");
        renderTextBoxes.Should().NotContain("FirstOrDefault");
        renderDrawingShapes.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        renderDrawingShapes.Should().Contain("metricLookups,");
        renderDrawingShapes.Should().NotContain("FirstOrDefault");
        renderNativeControls.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        renderNativeControls.Should().Contain("TryCreateDrawingAnchorRect(metricLookups, anchor");
        renderNativeControls.Should().NotContain("TryCreateDrawingAnchorRect(Viewport, anchor");
        renderPlaceholders.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        renderPlaceholders.Should().Contain("TryCreateDrawingAnchorRect(metricLookups, anchor");
        renderPlaceholders.Should().NotContain("TryCreateDrawingAnchorRect(Viewport, anchor");
        renderPictures.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        renderPictures.Should().Contain("metricLookups,");
        renderPictures.Should().NotContain("FirstOrDefault");
    }
}
