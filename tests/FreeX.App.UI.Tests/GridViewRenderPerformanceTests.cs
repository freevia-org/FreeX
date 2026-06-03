using System;
using System.IO;
using System.Reflection;
using FreeX.App.UI;
using FreeX.Core.Model;
using FluentAssertions;
using System.Windows;

namespace FreeX.App.UI.Tests;

public sealed class GridViewRenderPerformanceTests
{
    [Fact]
    public void RenderCells_UsesMetricDictionariesForExplicitBorderCells()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var borderPass = source[
            source.IndexOf("// Pass 2: explicit cell borders", StringComparison.Ordinal)..
            source.IndexOf("// Pass 2b: comment/note indicators", StringComparison.Ordinal)];

        borderPass.Should().Contain("rowLookupAll.TryGetValue(cell.Row");
        borderPass.Should().Contain("colLookupAll.TryGetValue(cell.Col");
        borderPass.Should().NotContain("Viewport.RowMetrics.FirstOrDefault");
        borderPass.Should().NotContain("Viewport.ColMetrics.FirstOrDefault");
    }

    [Fact]
    public void RenderCells_BuildsResizeLookupsWithoutLinqPipelines()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var setup = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];

        setup.Should().Contain("GetRenderCellLookups(viewport)");
        setup.Should().Contain("var styleLookup = lookups.Styles;");
        setup.Should().Contain("var rowLookupAll = lookups.Rows;");
        setup.Should().Contain("var colLookupAll = lookups.Columns;");
        setup.Should().NotContain(".Where(");
        setup.Should().NotContain(".ToDictionary(");

        source.Should().Contain("lookup.Add((cell.Row, cell.Col), style)");
        source.Should().Contain("lookup.Add(row.Row, row)");
        source.Should().Contain("lookup.Add(column.Col, column)");
    }

    [Fact]
    public void RenderCells_LazilyAllocatesStyleLookupForDefaultStyledViewports()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var buildStyleLookup = source[
            source.IndexOf("private static Dictionary<(uint Row, uint Col), CellStyle> BuildRenderCellStyleLookup", StringComparison.Ordinal)..
            source.IndexOf("private RenderCellLookupCache GetRenderCellLookups", StringComparison.Ordinal)];

        source.Should().Contain("private static readonly Dictionary<(uint Row, uint Col), CellStyle> EmptyRenderCellStyleLookup = new(0);");
        buildStyleLookup.Should().Contain("Dictionary<(uint Row, uint Col), CellStyle>? lookup = null;");
        buildStyleLookup.Should().Contain("cell.Style is { } style && HasVisibleCellSurface(style)");
        buildStyleLookup.Should().Contain("lookup ??= new Dictionary<(uint Row, uint Col), CellStyle>(cells.Count);");
        buildStyleLookup.Should().Contain("return lookup ?? EmptyRenderCellStyleLookup;");
        buildStyleLookup.Should().NotContain("var lookup = new Dictionary<(uint Row, uint Col), CellStyle>();");

        var buildLookup = typeof(GridView).GetMethod(
            "BuildRenderCellStyleLookup",
            BindingFlags.NonPublic | BindingFlags.Static);
        buildLookup.Should().NotBeNull();

        var defaultLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup!.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "default", CellStyle.Default) }])!;
        defaultLookup.Should().BeEmpty();

        var fontOnlyStyle = CellStyle.Default.Clone();
        fontOnlyStyle.Bold = true;
        var fontOnlyLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "font", fontOnlyStyle) }])!;
        fontOnlyLookup.Should().BeEmpty();

        var fillStyle = CellStyle.Default.Clone();
        fillStyle.FillColor = CellColor.White;
        var fillLookup = (IReadOnlyDictionary<(uint Row, uint Col), CellStyle>)buildLookup.Invoke(
            null,
            [new DisplayCell[] { Cell(1, 1, "fill", fillStyle) }])!;
        fillLookup.Should().ContainKey((1u, 1u));
    }

    [Fact]
    public void RenderCells_ReusesPixelsPerDipAcrossFormattedTextCalls()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderCells.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip).Width");
        renderCells.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderCells_ClipsTextOnlyWhenLaidOutBoundsOverflow()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var shouldClipText = source[
            source.IndexOf("private static bool ShouldClipText(", StringComparison.Ordinal)..
            source.IndexOf("private static Pen UnderlinePenForTextBrush", StringComparison.Ordinal)];

        shouldClipText.Should().Contain("if (wrapText && text.Height > clipRect.Height + tolerance)");
        shouldClipText.Should().Contain("textPoint.X + text.Width > clipRect.Right + tolerance");
        shouldClipText.Should().Contain("textPoint.Y + text.Height > clipRect.Bottom + tolerance");
        shouldClipText.Should().NotContain("style is not null || wrapText");
    }

    [Fact]
    public void RenderCells_SkipsOffscreenCellsBeforeTextLayout()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)];

        renderCells.Should().Contain("var visibleRight = ActualWidth;");
        renderCells.Should().Contain("var visibleBottom = ActualHeight;");
        source.Should().Contain("private static bool IntersectsVisibleGrid");
        renderCells.Should().Contain("var cellTop = rowMetric.TopOffset + columnHeaderHeight;");
        renderCells.Should().Contain("if (cellTop >= visibleBottom) continue;");
        renderCells.Should().Contain("var cellLeft = colMetric.LeftOffset + rowHeaderWidth;");
        renderCells.Should().Contain("if (cellLeft >= visibleRight) continue;");
        renderCells.Should().Contain("var hasMergedText = _mergeLookup.Count > 0;");
        renderCells.Should().Contain("var cellMerge = hasMergedText ? FindMerge(cell.Row, cell.Col) : null;");
        renderCells.Should().Contain("rect.Left >= visibleRight");
        renderCells.Should().Contain("if (!IntersectsVisibleGrid(clipRect, visibleLeft, visibleTop, visibleRight, visibleBottom))");

        renderCells.IndexOf("if (cellTop >= visibleBottom) continue;", StringComparison.Ordinal)
            .Should().BeLessThan(renderCells.IndexOf("if (!colLookup.TryGetValue(cell.Col, out var colMetric)) continue;", StringComparison.Ordinal));
        renderCells.IndexOf("rect.Left >= visibleRight", StringComparison.Ordinal)
            .Should().BeLessThan(renderCells.IndexOf("var typefaceKey = CreateCellTypefaceKey(style);", StringComparison.Ordinal));
        renderCells.IndexOf("if (!IntersectsVisibleGrid(clipRect, visibleLeft, visibleTop, visibleRight, visibleBottom))", StringComparison.Ordinal)
            .Should().BeLessThan(renderCells.IndexOf("dc.DrawText(text, textPoint);", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderHeaders_ReusesPixelsPerDipAcrossFormattedTextCalls()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
        var renderHeaders = source[
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)];

        renderHeaders.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderHeaders.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderHeaders_CachesA1ColumnLabelsAcrossRenderPasses()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
        var formatColumnHeader = source[
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)..];

        source.Should().Contain("private static readonly ConcurrentDictionary<uint, string> ColumnHeaderCache = new();");
        formatColumnHeader.Should().Contain("ColumnHeaderCache.GetOrAdd(column");
        formatColumnHeader.Should().Contain("CellAddress.NumberToColumnName(col)");
    }

    [Fact]
    public void RenderHeaders_CachesRowLabelsAcrossRenderPasses()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
        var drawRowHeader = source[
            source.IndexOf("private void DrawRowHeader", StringComparison.Ordinal)..
            source.IndexOf("private static IReadOnlyList<HeaderSelectionInterval>", StringComparison.Ordinal)];
        var formatRowHeader = source[
            source.IndexOf("internal static string FormatRowHeader", StringComparison.Ordinal)..];

        source.Should().Contain("private static readonly ConcurrentDictionary<uint, string> RowHeaderCache = new();");
        drawRowHeader.Should().Contain("FormatRowHeader(row.Row)");
        drawRowHeader.Should().NotContain("row.Row.ToString");
        formatRowHeader.Should().Contain("RowHeaderCache.GetOrAdd(row");
        formatRowHeader.Should().Contain("rowNumber.ToString(CultureInfo.InvariantCulture)");

        var formatter = typeof(GridView).GetMethod(
            "FormatRowHeader",
            BindingFlags.NonPublic | BindingFlags.Static);

        formatter.Should().NotBeNull();
        formatter!.Invoke(null, [1u]).Should().Be("1");
        formatter.Invoke(null, [1_048_576u]).Should().Be("1048576");
    }

    [Fact]
    public void RenderHeaders_CachesHeaderTextDrawingAcrossSelectionRepaints()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
        var drawColumnHeader = source[
            source.IndexOf("private void DrawColumnHeader", StringComparison.Ordinal)..
            source.IndexOf("private void DrawRowHeader", StringComparison.Ordinal)];
        var drawRowHeader = source[
            source.IndexOf("private void DrawRowHeader", StringComparison.Ordinal)..
            source.IndexOf("private void DrawHeaderText", StringComparison.Ordinal)];
        var drawHeaderText = source[
            source.IndexOf("private void DrawHeaderText", StringComparison.Ordinal)..
            source.IndexOf("private static IReadOnlyList<HeaderSelectionInterval>", StringComparison.Ordinal)];

        source.Should().Contain("private readonly Dictionary<HeaderTextDrawingKey, DrawingGroup> _headerTextDrawingCache = new();");
        drawColumnHeader.Should().Contain("DrawHeaderText(dc, textValue, text, 11, pixelsPerDip");
        drawRowHeader.Should().Contain("DrawHeaderText(dc, textValue, text, 11, pixelsPerDip");
        drawColumnHeader.Should().NotContain("dc.DrawText");
        drawRowHeader.Should().NotContain("dc.DrawText");
        drawHeaderText.Should().Contain("_headerTextDrawingCache.TryGetValue(key");
        drawHeaderText.Should().Contain("groupContext.DrawText(formattedText, origin);");
        drawHeaderText.Should().Contain("dc.DrawDrawing(drawing);");
    }

    [Fact]
    public void RenderHeaders_WalksSelectionIntervalsInsteadOfScanningRangesPerHeader()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
        var renderSelectedHeaders = source[
            source.IndexOf("private void RenderSelectedHeaders(", StringComparison.Ordinal)..
            source.IndexOf("private void DrawColumnHeader(", StringComparison.Ordinal)];
        var buildSelectionIntervals = source[
            source.IndexOf("private static IReadOnlyList<HeaderSelectionInterval> BuildHeaderSelectionIntervals(", StringComparison.Ordinal)..
            source.IndexOf("private static bool IsHeaderSelected(", StringComparison.Ordinal)];
        var isHeaderSelected = source[
            source.IndexOf("private static bool IsHeaderSelected(", StringComparison.Ordinal)..
            source.IndexOf("internal static string FormatColumnHeader", StringComparison.Ordinal)];
        var renderFreezeDivider = source[
            source.IndexOf("private void RenderFreezeDivider(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)];

        renderSelectedHeaders.Should().Contain("BuildColumnHeaderSelectionIntervals(selectedRanges, selRange)");
        renderSelectedHeaders.Should().Contain("BuildRowHeaderSelectionIntervals(selectedRanges, selRange)");
        renderSelectedHeaders.Should().Contain("IsHeaderSelected(col.Col, columnIntervals, ref columnIntervalIndex)");
        renderSelectedHeaders.Should().Contain("IsHeaderSelected(row.Row, rowIntervals, ref rowIntervalIndex)");
        buildSelectionIntervals.Should().Contain("if (selectedRanges.Count == 1)");
        buildSelectionIntervals.Should().Contain("return [selector(selectedRanges[0])];");
        buildSelectionIntervals.Should().Contain("intervals.Sort");
        isHeaderSelected.Should().Contain("while (intervalIndex < intervals.Count && index > intervals[intervalIndex].End)");
        renderSelectedHeaders.Should().NotContain(".Any(");
        renderSelectedHeaders.Should().NotContain("foreach (var range in selectedRanges)");
        renderFreezeDivider.Should().Contain("FindRowMetric(Viewport.RowMetrics, fp.Rows)");
        renderFreezeDivider.Should().Contain("FindColMetric(Viewport.ColMetrics, fp.Cols)");
        renderFreezeDivider.Should().NotContain("FirstOrDefault");
    }

    [Fact]
    public void RenderSelectionAndHeaders_FastPathSingleCellSelectionsWithMetricLookups()
    {
        var headerSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
        var renderSelectedHeaders = headerSource[
            headerSource.IndexOf("private void RenderSelectedHeaders(", StringComparison.Ordinal)..
            headerSource.IndexOf("private void DrawColumnHeader(", StringComparison.Ordinal)];
        renderSelectedHeaders.Should().Contain("TryRenderSingleCellSelectedHeaders(");
        renderSelectedHeaders.Should().Contain("TryGetSingleCellSelectedHeaderRange(");
        renderSelectedHeaders.Should().Contain("GetRenderMetricLookups(viewport)");
        renderSelectedHeaders.Should().Contain("lookups.Columns.TryGetValue(range.Start.Col");
        renderSelectedHeaders.Should().Contain("lookups.Rows.TryGetValue(range.Start.Row");
        renderSelectedHeaders.IndexOf("TryRenderSingleCellSelectedHeaders", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSelectedHeaders.IndexOf("BuildColumnHeaderSelectionIntervals", StringComparison.Ordinal));

        var selectionSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Selection.cs"));
        var renderSelectionRange = selectionSource[
            selectionSource.IndexOf("private void RenderSelectionRange(", StringComparison.Ordinal)..
            selectionSource.IndexOf("private static void DrawSelectionHandle", StringComparison.Ordinal)];
        renderSelectionRange.Should().Contain("CalculateSelectionRangeLayout(Viewport, range, rowHeaderWidth, columnHeaderHeight)");
        renderSelectionRange.Should().Contain("IsSingleCellRange(range)");
        renderSelectionRange.Should().Contain("CalculateVisibleSingleCellSelectionLayout(viewport, range, rowHeaderWidth, columnHeaderHeight)");
        renderSelectionRange.Should().Contain("GetRenderMetricLookups(viewport)");
        renderSelectionRange.Should().Contain("lookups.Rows.TryGetValue(range.Start.Row");
        renderSelectionRange.Should().Contain("lookups.Columns.TryGetValue(range.Start.Col");
        renderSelectionRange.IndexOf("IsSingleCellRange(range)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSelectionRange.IndexOf("CalculateVisibleSelectionLayout", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderHeaders_CachesUnselectedHeaderLayerAcrossSelectionRepaints()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));
        var renderHeaders = source[
            source.IndexOf("private void RenderHeaders(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderHeaderBaseLayer(", StringComparison.Ordinal)];
        var renderHeaderBaseLayer = source[
            source.IndexOf("private void RenderHeaderBaseLayer(", StringComparison.Ordinal)..
            source.IndexOf("private DrawingGroup BuildHeaderBaseLayerCache(", StringComparison.Ordinal)];
        var buildHeaderBaseLayer = source[
            source.IndexOf("private DrawingGroup BuildHeaderBaseLayerCache(", StringComparison.Ordinal)..
            source.IndexOf("private void RenderHeaderBase(", StringComparison.Ordinal)];
        var renderSelectedHeaderLayer = source[
            source.IndexOf("private void RenderSelectedHeaderLayer(", StringComparison.Ordinal)..
            source.IndexOf("private SelectedHeaderLayerCacheKey CreateSelectedHeaderLayerCacheKey", StringComparison.Ordinal)];

        source.Should().Contain("private DrawingGroup? _headerBaseLayerCache;");
        source.Should().Contain("private HeaderBaseLayerCacheKey _headerBaseLayerCacheKey;");
        renderHeaders.Should().Contain("RenderHeaderBaseLayer(dc, viewport, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        renderHeaders.Should().Contain("RenderSelectedHeaderLayer(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        renderHeaderBaseLayer.Should().Contain("_headerBaseLayerCache is { } cached && _headerBaseLayerCacheKey == key");
        renderHeaderBaseLayer.Should().Contain("dc.DrawDrawing(cached);");
        renderHeaderBaseLayer.Should().NotContain("SelectedRange");
        renderHeaderBaseLayer.Should().NotContain("SelectedRanges");
        buildHeaderBaseLayer.Should().Contain("RenderHeaderBase(groupContext, viewport, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        buildHeaderBaseLayer.Should().Contain("group.Freeze();");
        source.Should().Contain("private DrawingGroup? _selectedHeaderLayerCache;");
        source.Should().Contain("private SelectedHeaderLayerCacheKey _selectedHeaderLayerCacheKey;");
        renderSelectedHeaderLayer.Should().Contain("_selectedHeaderLayerCache is { } cached && _selectedHeaderLayerCacheKey == key");
        renderSelectedHeaderLayer.Should().Contain("ShouldBuildSelectedHeaderLayerCache(key)");
        renderSelectedHeaderLayer.Should().Contain("RenderSelectedHeaders(dc, viewport, selectedRanges, selRange, rowHeaderWidth, columnHeaderHeight, pixelsPerDip);");
        source.Should().Contain("_hasLastSelectedHeaderLayerRenderKey && _lastSelectedHeaderLayerRenderKey == key");
        source.Should().Contain("BuildSelectedHeaderLayerCache(");
        source.Should().Contain("CalculateGridRangeListSignature(selectedRanges)");
    }

    [Fact]
    public void FormatColumnHeader_UsesA1NamesOrR1C1Numbers()
    {
        var formatColumnHeader = typeof(GridView).GetMethod(
            "FormatColumnHeader",
            BindingFlags.NonPublic | BindingFlags.Static);

        formatColumnHeader.Should().NotBeNull();
        formatColumnHeader!.Invoke(null, [27u, false]).Should().Be("AA");
        formatColumnHeader.Invoke(null, [27u, true]).Should().Be("27");
    }

    [Fact]
    public void CalculateRowHeaderWidth_UsesLastVisibleRowWithoutMetricScan()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var calculateRowHeaderWidth = source[
            source.IndexOf("public static double CalculateRowHeaderWidth", StringComparison.Ordinal)..
            source.IndexOf("private const double ResizeHitZone", StringComparison.Ordinal)];

        calculateRowHeaderWidth.Should().Contain("viewport.RowMetrics[^1].Row");
        calculateRowHeaderWidth.Should().NotContain("foreach (var row in viewport.RowMetrics)");
        calculateRowHeaderWidth.Should().NotContain(".Max(");
        GridView.CalculateRowHeaderWidth(null).Should().Be(GridView.RowHeaderWidth);
        GridView.CalculateRowHeaderWidth(new ViewportModel([], [], [])).Should().Be(GridView.RowHeaderWidth);
        GridView.CalculateRowHeaderWidth(new ViewportModel(
            [],
            [new RowMetric(999, 20, 0), new RowMetric(1_000, 20, 20)],
            [])).Should().Be(36);
        GridView.CalculateRowHeaderWidth(new ViewportModel(
            [],
            [new RowMetric(999_999, 20, 0), new RowMetric(1_000_000, 20, 20)],
            [])).Should().Be(54);
    }

    [Fact]
    public void RenderSparklines_AvoidsEmptyRenderAllocations()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.Sparklines.cs"));
        var renderSparklines = source[
            source.IndexOf("private void RenderSparklines(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static SolidColorBrush FrozenBrush", StringComparison.Ordinal)];

        renderSparklines.Should().Contain("Sparklines is not { Count: > 0 }");
        renderSparklines.Should().Contain("SparklineValues is not { Count: > 0 }");
        renderSparklines.Should().Contain("GetRenderCellLookups(Viewport)");
        renderSparklines.Should().Contain("var rowLookup = lookups.Rows;");
        renderSparklines.Should().Contain("var colLookup = lookups.Columns;");
        source.Should().Contain("private static readonly SolidColorBrush SparklinePositiveBrush");
        source.Should().Contain("private static readonly Pen SparklineLinePen");
        renderSparklines.Should().Contain("DrawLineSparkline(dc, values, rect, SparklineLinePen)");
        renderSparklines.Should().Contain("DrawColumnSparkline(dc, values, rect, sparkline.Kind == SparklineKind.WinLoss, SparklinePositiveBrush, SparklineNegativeBrush)");
        renderSparklines.Should().Contain("dc.PushClip(GetCellClipGeometry(rect));");
        renderSparklines.Should().NotContain("new RectangleGeometry(rect)");
        source.Should().Contain("SparklineLayoutPlanner.VisitLineLayout(values, rect, ref consumer)");
        source.Should().Contain("SparklineLayoutPlanner.VisitColumnLayout(values, rect, winLoss, ref consumer)");
        source.Should().NotContain("BuildSparklineRowMetricLookup");
        source.Should().NotContain("BuildSparklineColumnMetricLookup");
        renderSparklines.Should().NotContain(".ToDictionary(");
        renderSparklines.Should().NotContain(".Select(");
        renderSparklines.Should().NotContain("new SolidColorBrush");
        renderSparklines.Should().NotContain("new Pen");
        source.Should().NotContain("CalculateLineLayout(values, rect)");
        source.Should().NotContain("CalculateColumnLayout(values, rect, winLoss)");
    }

    [Fact]
    public void DrawingObjectRenderAndHitTest_ReusesMetricLookupsForAnchoredObjects()
    {
        var drawingObjects = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var pictures = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.Pictures.cs"));
        var objectDrag = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.ObjectDrag.cs"));
        var planner = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridDrawingObjectPlanner.cs"));

        drawingObjects.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        pictures.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        objectDrag.Should().Contain("var metricLookups = GetRenderMetricLookups(Viewport);");
        drawingObjects.Should().Contain("metricLookups,");
        pictures.Should().Contain("metricLookups,");
        objectDrag.Should().Contain("metricLookups,");
        planner.Should().Contain("IReadOnlyDictionary<uint, RowMetric> rows");
        planner.Should().Contain("rows.TryGetValue(anchor.Row");
        planner.Should().Contain("columns.TryGetValue(anchor.Col");
    }

    [Fact]
    public void RenderTextBoxes_ReusesNamedClipRectForText()
    {
        var drawingObjects = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var renderTextBox = drawingObjects[
            drawingObjects.IndexOf("private void RenderTextBox(", StringComparison.Ordinal)..
            drawingObjects.IndexOf("private void RenderDrawingShapes", StringComparison.Ordinal)];

        renderTextBox.Should().Contain("var textWidth = Math.Max(1, rect.Width - 8);");
        renderTextBox.Should().Contain("var textHeight = Math.Max(1, rect.Height - 8);");
        renderTextBox.Should().Contain("var textClipRect = new Rect(rect.Left + 4, rect.Top + 4, textWidth, textHeight);");
        renderTextBox.Should().Contain("dc.PushClip(GetDrawingObjectClipGeometry(textClipRect));");
        renderTextBox.Should().NotContain("GetDrawingObjectClipGeometry(new Rect");
        renderTextBox.Should().NotContain("new RectangleGeometry");
    }

    [Fact]
    public void RenderAutofillPreview_ReusesFrozenStaticDashedPen()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var overlaysSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var renderAutofill = overlaysSource[
            overlaysSource.IndexOf("private void RenderAutofillPreview", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void RenderMarchingAnts", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private static readonly Pen AutofillPreviewPen = MakeAutofillPreviewPen();");
        gridViewSource.Should().Contain("private static Pen MakeAutofillPreviewPen()");
        gridViewSource.Should().Contain("DashStyle = new DashStyle([4.0, 4.0], 0)");
        gridViewSource.Should().Contain("pen.Freeze();");
        renderAutofill.Should().Contain("dc.DrawRectangle(null, AutofillPreviewPen, rect);");
        renderAutofill.Should().NotContain("new Pen");
        renderAutofill.Should().NotContain("new SolidColorBrush");
        renderAutofill.Should().NotContain("new DashStyle");
    }

    [Fact]
    public void RenderMarchingAnts_ReusesCachedPhasePens()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var overlaysSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var renderMarchingAnts = overlaysSource[
            overlaysSource.IndexOf("private void RenderMarchingAnts", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void RenderFormulaTraceArrows", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private const int MarchingAntsPhaseCount = 16;");
        gridViewSource.Should().Contain("private static readonly Pen[] MarchingAntsBlackPens = CreateMarchingAntsPens(Brushes.Black, 2.5);");
        gridViewSource.Should().Contain("private static readonly Pen[] MarchingAntsCopyOverlayPens = CreateMarchingAntsPens(Brushes.White, 1.5);");
        gridViewSource.Should().Contain("private static readonly Pen[] MarchingAntsCutOverlayPens = CreateMarchingAntsPens(MakeBrush(245, 124, 0), 1.5);");
        gridViewSource.Should().Contain("private static Pen[] CreateMarchingAntsPens");
        gridViewSource.Should().Contain("private static int GetMarchingAntsPhase(double offset)");
        renderMarchingAnts.Should().Contain("var phase = GetMarchingAntsPhase(_marchOffset);");
        renderMarchingAnts.Should().Contain("MarchingAntsBlackPens[phase]");
        renderMarchingAnts.Should().Contain("ClipboardIsCut ? MarchingAntsCutOverlayPens[phase] : MarchingAntsCopyOverlayPens[phase]");
        renderMarchingAnts.Should().NotContain("new Pen");
        renderMarchingAnts.Should().NotContain("new SolidColorBrush");
        renderMarchingAnts.Should().NotContain("new DashStyle");
    }

    [Fact]
    public void ClipboardRangeAnimationTimer_StopsWhenGridUnloads()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var stateSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.State.cs"));
        var constructorStart = gridViewSource.IndexOf("public GridView()", StringComparison.Ordinal);
        var constructor = gridViewSource[
            constructorStart..
            gridViewSource.IndexOf("/// <summary>", constructorStart, StringComparison.Ordinal)];
        var stopMarchTimer = stateSource[
            stateSource.IndexOf("private void StopMarchTimer()", StringComparison.Ordinal)..];

        constructor.Should().Contain("Unloaded += (_, _) => StopMarchTimer();");
        stopMarchTimer.Should().Contain("_marchTimer?.Stop();");
        stopMarchTimer.Should().Contain("_marchTimer = null;");
    }

    [Fact]
    public void GetMarchingAntsPhase_NormalizesAnimationOffset()
    {
        var getPhase = typeof(GridView).GetMethod(
            "GetMarchingAntsPhase",
            BindingFlags.NonPublic | BindingFlags.Static);

        getPhase.Should().NotBeNull();
        getPhase!.Invoke(null, [0d]).Should().Be(0);
        getPhase.Invoke(null, [1.5d]).Should().Be(3);
        getPhase.Invoke(null, [7.5d]).Should().Be(15);
        getPhase.Invoke(null, [8.0d]).Should().Be(0);
        getPhase.Invoke(null, [-0.5d]).Should().Be(15);
    }

    [Fact]
    public void OnRender_SkipsHeavyVisualLayersDuringLiveResize()
    {
        var properties = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var onRender = source[
            source.IndexOf("protected override void OnRender", StringComparison.Ordinal)..];

        properties.Should().Contain("public static readonly DependencyProperty IsLiveResizingProperty");
        properties.Should().Contain("FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender)");
        onRender.Should().Contain("var isLiveResizing = IsLiveResizing;");
        onRender.Should().Contain("var skipHeavyLayers = isLiveResizing || _resizeTarget != ResizeTarget.None;");
        onRender.Should().Contain("if (!skipHeavyLayers)");
        onRender.Should().Contain("RenderLiveResizeContinuation(dc);");
        onRender.Should().Contain("RenderCells(dc);");
        onRender.Should().Contain("RenderSelection(dc);");

        onRender.IndexOf("RenderCells(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderWorksheetViewOverlay(dc);", StringComparison.Ordinal));
        onRender.IndexOf("RenderSelection(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderFormulaTraceArrows(dc);", StringComparison.Ordinal));
        onRender.IndexOf("RenderDrawingObjectLayersWithCache(dc);", StringComparison.Ordinal)
            .Should().BeGreaterThan(onRender.LastIndexOf("if (!skipHeavyLayers)", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectionOnlyInvalidations_ReusePreSelectionLayerCache()
    {
        var properties = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var dispatch = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var cache = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderSurfaceCache.cs"));
        var onRender = dispatch[
            dispatch.IndexOf("protected override void OnRender", StringComparison.Ordinal)..
            dispatch.IndexOf("private void RenderPreSelectionLayers", StringComparison.Ordinal)];

        properties.Should().Contain("OnSelectionVisualPropertyChanged");
        properties.Should().Contain("grid.MarkSelectionVisualOnlyChange();");
        dispatch.Should().Contain("RenderPreSelectionLayersWithCache(dc, skipHeavyLayers, isLiveResizing);");
        cache.Should().Contain("RenderPreSelectionLayers(dc, skipHeavyLayers, isLiveResizing);");
        cache.Should().Contain("ShouldBuildPreSelectionLayerCache(key)");
        cache.Should().Contain("_selectionVisualOnlyChangePending ||");
        cache.Should().Contain("dc.DrawDrawing(cached);");
        cache.Should().Contain("BuildPreSelectionLayerCache(skipHeavyLayers, isLiveResizing)");
        cache.Should().NotContain("SelectedRange");
        cache.Should().NotContain("SelectedRanges");

        onRender.IndexOf("RenderHeaders(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderPreSelectionLayersWithCache", StringComparison.Ordinal));
        onRender.IndexOf("RenderPreSelectionLayersWithCache", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderSelection(dc);", StringComparison.Ordinal));
        onRender.IndexOf("RenderSelection(dc);", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("RenderPostSelectionLayers", StringComparison.Ordinal));
        onRender.IndexOf("RenderPostSelectionLayers", StringComparison.Ordinal)
            .Should().BeLessThan(onRender.IndexOf("_selectionVisualOnlyChangePending = false;", StringComparison.Ordinal));
    }

    [Fact]
    public void StableRenderInvalidations_WarmAndReusePreSelectionLayerCache()
    {
        var cache = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderSurfaceCache.cs"));
        var renderWithCache = cache[
            cache.IndexOf("private void RenderPreSelectionLayersWithCache", StringComparison.Ordinal)..
            cache.IndexOf("private static bool CanCachePreSelectionLayers", StringComparison.Ordinal)];

        renderWithCache.Should().Contain("_preSelectionLayerCache is { } cached");
        renderWithCache.Should().Contain("_preSelectionLayerCacheKey == key");
        renderWithCache.Should().Contain("dc.DrawDrawing(cached);");
        renderWithCache.Should().Contain("ShouldBuildPreSelectionLayerCache(key)");
        renderWithCache.Should().Contain("RememberPreSelectionLayerRenderKey(key);");
        cache.Should().Contain("bool SkipHeavyLayers");
        cache.Should().Contain("private static bool CanCachePreSelectionLayers(bool skipHeavyLayers, bool isLiveResizing) =>");
        cache.Should().Contain("!isLiveResizing;");
        cache.Should().NotContain("!skipHeavyLayers &&");
        cache.Should().Contain("_hasLastPreSelectionLayerRenderKey && _lastPreSelectionLayerRenderKey == key");
        cache.Should().Contain("_selectionVisualOnlyChangePending ||");
        cache.Should().Contain("_hasLastPreSelectionLayerRenderKey = false;");
    }

    [Fact]
    public void SelectionOnlyInvalidations_ReuseRenderClipGeometry()
    {
        var dispatch = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var onRender = dispatch[
            dispatch.IndexOf("protected override void OnRender", StringComparison.Ordinal)..
            dispatch.IndexOf("private RectangleGeometry GetRenderClipGeometry", StringComparison.Ordinal)];
        var getRenderClipGeometry = dispatch[
            dispatch.IndexOf("private RectangleGeometry GetRenderClipGeometry", StringComparison.Ordinal)..
            dispatch.IndexOf("private void RenderPreSelectionLayers", StringComparison.Ordinal)];

        dispatch.Should().Contain("private RectangleGeometry? _renderClipGeometryCache;");
        dispatch.Should().Contain("private Rect _renderClipGeometryCacheRect;");
        onRender.Should().Contain("dc.PushClip(GetRenderClipGeometry(new Rect(0, 0, ActualWidth / zoom, ActualHeight / zoom)));");
        onRender.Should().NotContain("new RectangleGeometry");
        getRenderClipGeometry.Should().Contain("_renderClipGeometryCache is { } cached && _renderClipGeometryCacheRect == clipRect");
        getRenderClipGeometry.Should().Contain("var geometry = new RectangleGeometry(clipRect);");
        getRenderClipGeometry.Should().Contain("geometry.Freeze();");
    }

    [Fact]
    public void SelectionOnlyInvalidations_SkipEmptyPostSelectionLayers()
    {
        var dispatch = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var splitPanes = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var drawingObjects = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var renderPostSelectionLayers = dispatch[
            dispatch.IndexOf("private void RenderPostSelectionLayers", StringComparison.Ordinal)..
            dispatch.IndexOf("private bool HasPostSelectionLayerWork", StringComparison.Ordinal)];
        var hasPostSelectionLayerWork = dispatch[
            dispatch.IndexOf("private bool HasPostSelectionLayerWork", StringComparison.Ordinal)..
            dispatch.IndexOf("private bool HasDrawingObjectLayerWork", StringComparison.Ordinal)];
        var hasDrawingObjectLayerWork = dispatch[
            dispatch.IndexOf("private bool HasDrawingObjectLayerWork", StringComparison.Ordinal)..];
        var renderSplitDivider = splitPanes[
            splitPanes.IndexOf("private void RenderSplitDivider", StringComparison.Ordinal)..
            splitPanes.IndexOf("private void RenderSplitDividerHandles", StringComparison.Ordinal)];
        var renderNativeControls = drawingObjects[
            drawingObjects.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)..
            drawingObjects.IndexOf("public static bool TryCreateDrawingAnchorRect", StringComparison.Ordinal)];

        renderPostSelectionLayers.Should().Contain("if (!HasPostSelectionLayerWork(skipHeavyLayers))");
        hasPostSelectionLayerWork.Should().Contain("Viewport?.FrozenPanes is not null");
        hasPostSelectionLayerWork.Should().Contain("Viewport?.SplitPanes is not null");
        hasPostSelectionLayerWork.Should().Contain("_resizeTarget != ResizeTarget.None");
        hasPostSelectionLayerWork.Should().Contain("FormulaTraceArrows is { Count: > 0 }");
        hasPostSelectionLayerWork.Should().Contain("ClipboardRange is not null");
        hasPostSelectionLayerWork.Should().Contain("HasDrawingObjectLayerWork()");
        hasDrawingObjectLayerWork.Should().Contain("SelectedObjectId != Guid.Empty && SelectedObjectKind != ObjectKind.None");
        hasDrawingObjectLayerWork.Should().Contain("ObjectDisplayMode == GridObjectDisplayMode.Nothing");
        hasDrawingObjectLayerWork.Should().Contain("Charts is { Count: > 0 }");
        hasDrawingObjectLayerWork.Should().Contain("TextBoxes is { Count: > 0 }");
        renderSplitDivider.Should().Contain("if (Viewport?.SplitPanes is null) return;");
        renderNativeControls.Should().Contain("NativeSlicers is not { Count: > 0 } && NativeTimelines is not { Count: > 0 }");
    }

    [Fact]
    public void LiveResizeContinuation_PaintsExpandedGridWithoutViewportRefresh()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var continuation = rendering[
            rendering.IndexOf("private void RenderLiveResizeContinuation", StringComparison.Ordinal)..
            rendering.IndexOf("private void RenderSplitPaneCells", StringComparison.Ordinal)];

        continuation.Should().Contain("ActualWidth > gridRight");
        continuation.Should().Contain("ActualHeight > gridBottom");
        continuation.Should().Contain("RenderLiveResizeColumnContinuation");
        continuation.Should().Contain("RenderLiveResizeRowContinuation");
        continuation.Should().Contain("DrawLiveResizeHorizontalGridLines");
        continuation.Should().Contain("DrawLiveResizeVerticalGridLines");
        continuation.Should().Contain("dc.DrawRectangle(Brushes.White, null");
        continuation.Should().NotContain("UpdateViewport");
        continuation.Should().NotContain("Viewport =");
    }

    [Fact]
    public void LiveResizeContinuation_ReusesPixelsPerDipForSyntheticHeaders()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var continuation = rendering[
            rendering.IndexOf("private void RenderLiveResizeContinuation", StringComparison.Ordinal)..
            rendering.IndexOf("private void RenderSplitPaneCells", StringComparison.Ordinal)];

        continuation.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        continuation.Should().Contain("RenderLiveResizeColumnContinuation(dc, gridRight, gridTop, pixelsPerDip);");
        continuation.Should().Contain("RenderLiveResizeRowContinuation(dc, gridLeft, gridRight, gridBottom, pixelsPerDip);");
        continuation.Should().Contain("DrawLiveResizeHeaderText(dc, FormatColumnHeader(++lastColumn, UseR1C1ReferenceStyle), headerRect, pixelsPerDip);");
        continuation.Should().Contain("DrawLiveResizeHeaderText(dc, FormatRowHeader(++lastRow), headerRect, pixelsPerDip);");
        continuation.Should().NotContain("(++lastRow).ToString");
        continuation.Should().NotContain("VisualTreeHelper.GetDpi(this).PixelsPerDip);");
    }

    [Fact]
    public void RenderCaches_AreClassLevelFieldsNotLocalAllocations()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));

        gridViewSource.Should().Contain("private readonly Dictionary<CellColor, SolidColorBrush> _brushCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<CellBorder, Pen> _borderPenCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<CellTypefaceKey, Typeface> _typefaceCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<Brush, Pen> _underlinePenCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<DefaultTextLayoutKey, FormattedText> _defaultTextLayoutCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<DefaultWrappedTextLayoutKey, FormattedText> _defaultWrappedTextLayoutCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<CellStyle, bool> _defaultTextLayoutStyleCache = new(CellStyleReferenceComparer.Instance);");
        gridViewSource.Should().Contain("private readonly Dictionary<TextWidthLayoutKey, double> _textWidthLayoutCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<ShrinkTextLayoutKey, double> _shrinkTextLayoutCache = new();");
        gridViewSource.Should().Contain("private readonly Dictionary<Rect, RectangleGeometry> _cellClipGeometryCache = new();");
        gridViewSource.Should().Contain("private RenderCellLookupCache? _renderCellLookupCache;");
        gridViewSource.Should().Contain("private OccupiedCellLookupCache? _occupiedCellLookupCache;");
    }

    [Fact]
    public void DefaultTextLayouts_AreCachedAcrossRenderPasses()
    {
        var cacheSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.TextLayoutCache.cs"));
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var headers = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.Headers.cs"));

        cacheSource.Should().Contain("private FormattedText GetDefaultFormattedText");
        cacheSource.Should().Contain("private FormattedText GetDefaultWrappedFormattedText");
        cacheSource.Should().Contain("private bool CanUseDefaultFormattedText");
        cacheSource.Should().Contain("private bool CanUseDefaultWrappedFormattedText");
        cacheSource.Should().Contain("_defaultTextLayoutStyleCache.TryGetValue");
        cacheSource.Should().Contain("_defaultTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_defaultWrappedTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_defaultTextLayoutCache.Count >= DefaultTextLayoutCacheLimit");
        cacheSource.Should().Contain("_defaultWrappedTextLayoutCache.Count >= DefaultWrappedTextLayoutCacheLimit");
        rendering.Should().Contain("_defaultTextLayoutStyleCache.Clear();");
        rendering.Should().Contain("CanUseDefaultFormattedText(style, wrapText)");
        rendering.Should().Contain("CanUseDefaultWrappedFormattedText(style)");
        rendering.Should().Contain("GetDefaultFormattedText(cell.DisplayText, fontSize, pixelsPerDip)");
        rendering.Should().Contain("GetDefaultWrappedFormattedText(cell.DisplayText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip)");
        headers.Should().Contain("GetDefaultFormattedText(");
    }

    [Fact]
    public void ShrinkToFitTextWidthMeasurements_AreCachedAcrossRenderPasses()
    {
        var cacheSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.TextLayoutCache.cs"));
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));

        cacheSource.Should().Contain("private double MeasureCellTextWidth");
        cacheSource.Should().Contain("private double ResolveCachedShrinkFontSize");
        cacheSource.Should().Contain("_textWidthLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_textWidthLayoutCache.Count >= TextWidthLayoutCacheLimit");
        cacheSource.Should().Contain("_shrinkTextLayoutCache.TryGetValue");
        cacheSource.Should().Contain("_shrinkTextLayoutCache.Count >= ShrinkTextLayoutCacheLimit");
        rendering.Should().Contain("var typefaceKey = CreateCellTypefaceKey(style);");
        rendering.Should().Contain("ResolveCachedShrinkFontSize(");
        cacheSource.Should().Contain("MeasureCellTextWidth(text, typefaceKey, typeface, size, pixelsPerDip)");
        rendering.Should().NotContain("size => new FormattedText(");
    }

    [Fact]
    public void RenderCells_ClearsCachesAtStartOfEachPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];

        renderCells.Should().Contain("_brushCache.Clear();");
        renderCells.Should().Contain("_borderPenCache.Clear();");
        renderCells.Should().Contain("_typefaceCache.Clear();");
        renderCells.Should().Contain("_underlinePenCache.Clear();");
        renderCells.Should().NotContain("new Dictionary<CellColor, SolidColorBrush>");
        renderCells.Should().NotContain("new Dictionary<CellBorder, Pen>");
        renderCells.Should().NotContain("new Dictionary<CellTypefaceKey, Typeface>");
        renderCells.Should().NotContain("new Dictionary<Brush, Pen>");
    }

    [Fact]
    public void RenderCells_CachesStableViewportLookupsAcrossRepaints()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var cacheSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderLookupCache.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var renderCells = rendering[
            rendering.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];

        renderCells.Should().Contain("GetRenderCellLookups(viewport)");
        rendering.Should().Contain("ReferenceEquals(cached.Viewport, viewport)");
        rendering.Should().Contain("occupied ??= GetOccupiedCellLookup(viewport, EditingCell);");
        cacheSource.Should().Contain("private sealed record RenderCellLookupCache");
        cacheSource.Should().Contain("private sealed record OccupiedCellLookupCache");
        propertiesSource.Should().Contain("OnViewportChanged");
        propertiesSource.Should().Contain("grid.ClearRenderLookupCache();");
    }

    [Fact]
    public void RenderCells_LazilyBuildsOverflowOccupancyLookup()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var textPass = source[
            source.IndexOf("// Pass 3: text", StringComparison.Ordinal)..
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)];
        var setup = textPass[..textPass.IndexOf("foreach (var cell in viewport.Cells)", StringComparison.Ordinal)];
        var overflowBlock = textPass[
            textPass.IndexOf("if (canOverflow)", StringComparison.Ordinal)..
            textPass.IndexOf("var typeface = CreateCellTypeface", StringComparison.Ordinal)];

        setup.Should().Contain("HashSet<(uint Row, uint Col)>? occupied = null;");
        setup.Should().NotContain("GetOccupiedCellLookup(viewport, EditingCell)");
        overflowBlock.Should().Contain("occupied ??= GetOccupiedCellLookup(viewport, EditingCell);");
        overflowBlock.Should().Contain("!occupied.Contains((cell.Row, nextCol))");
    }

    [Fact]
    public void RenderCells_BatchesDefaultBackgroundAndGridLines()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("// Pass 2: explicit cell borders", StringComparison.Ordinal)];
        var backgroundBase = source[
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)..
            source.IndexOf("private static Dictionary<(uint Row, uint Col), CellStyle>", StringComparison.Ordinal)];

        renderCells.Should().Contain("var rowHeaderWidth = ActualRowHeaderWidth;");
        renderCells.Should().Contain("var columnHeaderHeight = EffectiveColHeaderHeight;");
        renderCells.Should().Contain("RenderCellBackgroundBase(dc, rowHeaderWidth, columnHeaderHeight);");
        renderCells.Should().Contain("var hasCellSurfaces = styleLookup.Count > 0;");
        renderCells.Should().Contain("var hasMergedSurfaces = _mergeLookup.Count > 0;");
        renderCells.Should().Contain("if (hasCellSurfaces || hasMergedSurfaces)");
        renderCells.Should().Contain("if (bg is null && !merge.HasValue)");
        renderCells.Should().Contain("continue;");
        backgroundBase.Should().Contain("var visibleRight = Math.Min(right, ActualWidth);");
        backgroundBase.Should().Contain("var visibleBottom = Math.Min(bottom, ActualHeight);");
        backgroundBase.Should().Contain("dc.DrawRectangle(Brushes.White, null, rect);");
        backgroundBase.Should().Contain("foreach (var row in Viewport.RowMetrics)");
        backgroundBase.Should().Contain("if (y > visibleBottom)");
        backgroundBase.Should().Contain("dc.DrawLine(GridPen, new Point(left, y), new Point(visibleRight, y));");
        backgroundBase.Should().Contain("foreach (var column in Viewport.ColMetrics)");
        backgroundBase.Should().Contain("if (x > visibleRight)");
        backgroundBase.Should().Contain("dc.DrawLine(GridPen, new Point(x, top), new Point(x, visibleBottom));");
    }

    [Fact]
    public void RenderCells_SkipsDefaultLookingStyleBordersBeforeMetricLookup()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var borderPass = source[
            source.IndexOf("// Pass 2: explicit cell borders", StringComparison.Ordinal)..
            source.IndexOf("// Pass 2b: comment/note indicators", StringComparison.Ordinal)];

        borderPass.Should().Contain("cell.Style is not { } style || !HasVisibleCellBorder(style)");
        borderPass.IndexOf("!HasVisibleCellBorder(style)", StringComparison.Ordinal)
            .Should().BeLessThan(borderPass.IndexOf("rowLookupAll.TryGetValue(cell.Row", StringComparison.Ordinal));

        var hasVisibleBorder = typeof(GridView).GetMethod(
            "HasVisibleCellBorder",
            BindingFlags.NonPublic | BindingFlags.Static);
        hasVisibleBorder.Should().NotBeNull();
        hasVisibleBorder!.Invoke(null, [CellStyle.Default]).Should().Be(false);

        var borderedStyle = CellStyle.Default.Clone();
        borderedStyle.BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.Black);
        hasVisibleBorder.Invoke(null, [borderedStyle]).Should().Be(true);
    }

    [Fact]
    public void RenderCells_ReusesCellColorBrushesWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("BrushForCellColor(bg.FillColor.Value, _brushCache)");
        renderCells.Should().Contain("BrushForCellColor(fc, _brushCache)");
        renderCells.Should().NotContain("new SolidColorBrush");
    }

    [Fact]
    public void RenderCells_ReusesBorderPensWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("_brushCache, _borderPenCache");
    }

    [Fact]
    public void RenderCells_ReusesFillPatternPensWithinRenderPass()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var cellStyles = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.CellStyles.cs"));
        var renderCells = rendering[
            rendering.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("// Pass 1: non-default backgrounds and merged-cell surfaces", StringComparison.Ordinal)];
        var drawFillPattern = cellStyles[
            cellStyles.IndexOf("private static void DrawFillPattern", StringComparison.Ordinal)..
            cellStyles.IndexOf("private static Pen FillPatternPenForCellColor", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private readonly Dictionary<CellColor, Pen> _fillPatternPenCache = new();");
        renderCells.Should().Contain("_fillPatternPenCache.Clear();");
        rendering.Should().Contain("DrawFillPattern(dc, rect, bg, _brushCache, _fillPatternPenCache)");
        cellStyles.Should().Contain("FillPatternPenForCellColor(color, brushCache, fillPatternPenCache)");
        cellStyles.Should().Contain("pen.Freeze();");
        drawFillPattern.Should().NotContain("new Pen(");
        drawFillPattern.Should().NotContain("new CellStyle");
    }

    [Fact]
    public void RenderCells_ReusesTypefacesWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("var typefaceKey = CreateCellTypefaceKey(style);");
        renderCells.Should().Contain("CreateCellTypeface(typefaceKey, _typefaceCache)");
    }

    [Fact]
    public void RenderCells_DelaysCustomTextResourcesUntilCustomLayoutIsNeeded()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private void RenderCellBackgroundBase", StringComparison.Ordinal)];
        var textSetup = renderCells[
            renderCells.IndexOf("double fontSize = ToDisplayFontSize", StringComparison.Ordinal)..
            renderCells.IndexOf("if (style?.ShrinkToFit == true && !wrapText)", StringComparison.Ordinal)];

        textSetup.Should().NotContain("CreateCellTypeface");
        textSetup.Should().NotContain("BrushForCellColor");
        renderCells.Should().Contain("if (style?.FontColor is { } fc && !fc.IsBlack)");
        renderCells.Should().Contain("textBrush = BrushForCellColor(fc, _brushCache);");
    }

    [Fact]
    public void RenderCells_ReusesDoubleUnderlinePensWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderCells = source[
            source.IndexOf("private void RenderCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static void DrawCommentIndicator", StringComparison.Ordinal)];

        renderCells.Should().Contain("UnderlinePenForTextBrush(textBrush, _underlinePenCache)");
        source.Should().Contain("private static Pen UnderlinePenForTextBrush");
        source.Should().Contain("pen.Freeze();");
        renderCells.Should().NotContain("new Pen(textBrush");
    }

    [Fact]
    public void RenderSplitPaneCells_ReusesDoubleUnderlinePensWithinRenderPass()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = source[
            source.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            source.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("_underlinePenCache.Clear();");
        renderSplitPaneCells.Should().Contain("if (style?.DoubleUnderline == true)");
        renderSplitPaneCells.Should().Contain("UnderlinePenForTextBrush(textBrush, _underlinePenCache)");
        renderSplitPaneCells.Should().Contain("dc.DrawLine(underlinePen, new Point(textX, uY), new Point(textX + text.Width, uY));");
        renderSplitPaneCells.Should().NotContain("new Pen(textBrush");
    }

    [Fact]
    public void ConditionalIconGlyphRenderer_ReusesFrozenBrushesAndPens()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "ConditionalIconGlyphRenderer.cs"));
        var drawMethod = source[
            source.IndexOf("public static void Draw", StringComparison.Ordinal)..
            source.IndexOf("private static ConditionalIconAppearance ResolveAppearance", StringComparison.Ordinal)];

        source.Should().Contain("private static readonly SolidColorBrush IconDarkRedBrush");
        source.Should().Contain("private static readonly Pen OutlinePen");
        source.Should().Contain("private static readonly Pen WhiteThinPen");
        source.Should().Contain("private static readonly ConcurrentDictionary<ConditionalIconAppearanceKey, ConditionalIconAppearance> AppearanceCache");
        drawMethod.Should().Contain("if (rect.Width <= 0 || rect.Height <= 0)");
        drawMethod.IndexOf("if (rect.Width <= 0 || rect.Height <= 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(drawMethod.IndexOf("var appearance = ResolveAppearance(icon);", StringComparison.Ordinal));
        drawMethod.Should().Contain("var appearance = ResolveAppearance(icon);");
        drawMethod.Should().NotContain("ResolveColor(icon)");
        drawMethod.Should().NotContain("ResolveGlyphKind(icon)");
        source.Should().Contain("brush.Freeze();");
        source.Should().Contain("pen.Freeze();");
        source.Should().NotContain("new BrushConverter");
        source.Should().NotContain("new Pen(Brushes.White");
        source.Should().NotContain("var outline = new Pen");
    }

    [Fact]
    public void ConditionalIconLayoutPlanner_CachesStyleTraitClassification()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "ConditionalIconLayoutPlanner.cs"));
        var resolveGlyphKind = source[
            source.IndexOf("public static ConditionalIconGlyphKind ResolveGlyphKind", StringComparison.Ordinal)..
            source.IndexOf("private static ConditionalIconStyleTraits ResolveStyleTraits", StringComparison.Ordinal)];
        var resolveColor = source[
            source.IndexOf("public static string ResolveColor", StringComparison.Ordinal)..];

        source.Should().Contain("private static readonly ConcurrentDictionary<string, ConditionalIconStyleTraits> StyleTraitCache");
        source.Should().Contain("new(StringComparer.OrdinalIgnoreCase)");
        resolveGlyphKind.Should().Contain("ResolveStyleTraits(icon.Style)");
        resolveGlyphKind.Should().NotContain("Contains(");
        resolveColor.Should().Contain("ResolveStyleTraits(icon.Style).IsGray");
        resolveColor.Should().NotContain("icon.Style.Contains");
    }

    [Fact]
    public void CalculateSplitDividerLayout_AvoidsLinqMetricScans()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var calculateLayout = source[
            source.IndexOf("public static SplitDividerLayout CalculateSplitDividerLayout", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneScrollbarChrome CalculateSplitPaneScrollbarChrome", StringComparison.Ordinal)];

        calculateLayout.Should().Contain("FindRowMetric(viewport.RowMetrics, splitRow)");
        calculateLayout.Should().Contain("FindColMetric(viewport.ColMetrics, splitColumn)");
        calculateLayout.Should().Contain("SumRowHeights(pinnedRows)");
        calculateLayout.Should().Contain("SumColumnWidths(pinnedColumns)");
        calculateLayout.Should().NotContain("FirstOrDefault");
        calculateLayout.Should().NotContain(".Sum(");
        source.Should().NotContain(".Sum(");
    }

    [Fact]
    public void CalculateSplitDividerDragTarget_StopsSortedMetricScansAfterPointer()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var findSplitRow = source[
            source.IndexOf("private static uint? FindSplitRow", StringComparison.Ordinal)..
            source.IndexOf("private static uint? FindSplitColumn", StringComparison.Ordinal)];
        var findSplitColumn = source[
            source.IndexOf("private static uint? FindSplitColumn", StringComparison.Ordinal)..
            source.IndexOf("private static uint IncrementWithinLimit", StringComparison.Ordinal)];

        findSplitRow.Should().Contain("foreach (var row in mainRows)");
        findSplitRow.Should().Contain("if (y < top)");
        findSplitRow.Should().Contain("break;");
        findSplitRow.IndexOf("if (y < top)", StringComparison.Ordinal)
            .Should().BeLessThan(findSplitRow.IndexOf("if (y >= top && y <= top + row.Height)", StringComparison.Ordinal));

        findSplitColumn.Should().Contain("foreach (var column in mainColumns)");
        findSplitColumn.Should().Contain("if (x < left)");
        findSplitColumn.Should().Contain("break;");
        findSplitColumn.IndexOf("if (x < left)", StringComparison.Ordinal)
            .Should().BeLessThan(findSplitColumn.IndexOf("if (x >= left && x <= left + column.Width)", StringComparison.Ordinal));
    }

    [Fact]
    public void SplitPaneDividerHandles_ReuseFrozenStaticPen()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var splitPanesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var renderHandles = splitPanesSource[
            splitPanesSource.IndexOf("private void RenderSplitDividerHandles", StringComparison.Ordinal)..
            splitPanesSource.IndexOf("private void RenderSplitPaneScrollbarChrome", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private static readonly Brush SplitDividerHandleBrush = MakeBrush(112, 112, 112);");
        gridViewSource.Should().Contain("private static readonly Pen SplitDividerHandlePen = MakePen(SplitDividerHandleBrush, 1);");
        renderHandles.Should().Contain("SplitDividerHandlePen");
        renderHandles.Should().NotContain("MakeBrush(");
        renderHandles.Should().NotContain("new Pen(");
    }

    [Fact]
    public void SplitPaneViewportChrome_ReusesNormalizedScrollbarSpans()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "SplitPaneViewportChrome.cs"));
        var calculateChrome = source[
            source.IndexOf("public static SplitPaneScrollbarChrome CalculateScrollbarChrome", StringComparison.Ordinal)..
            source.IndexOf("public static SplitPaneScrollbarHit? HitTestScrollbar", StringComparison.Ordinal)];

        calculateChrome.Should().Contain("var visibleSpan = Math.Max(1, topRightColumns.Count);");
        calculateChrome.Should().Contain("var visibleSpan = Math.Max(1, bottomLeftRows.Count);");
        calculateChrome.Should().Contain("var maxStartIndex = Math.Max(1, CellAddress.MaxCol - (uint)visibleSpan + 1);");
        calculateChrome.Should().Contain("var maxStartIndex = Math.Max(1, CellAddress.MaxRow - (uint)visibleSpan + 1);");
        calculateChrome.Should().NotContain("(uint)Math.Max(1, topRightColumns.Count)");
        calculateChrome.Should().NotContain("(uint)Math.Max(1, bottomLeftRows.Count)");
    }

    [Fact]
    public void ResizeDragInput_ReusesMetricScanHelpersWithoutLinqIterators()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Input.cs"));
        var resizeMove = source[
            source.IndexOf("if (_resizeTarget == ResizeTarget.Column)", StringComparison.Ordinal)..
            source.IndexOf("public static GridAutoScrollRequest CalculateAutofillEdgeScrollIntent", StringComparison.Ordinal)];
        var resizeStart = source[
            source.IndexOf("if (target != ResizeTarget.None)", StringComparison.Ordinal)..
            source.IndexOf("protected override void OnMouseRightButtonDown", StringComparison.Ordinal)];

        resizeMove.Should().Contain("if (Viewport is null)");
        resizeMove.Should().Contain("FindColMetric(Viewport.ColMetrics, _resizeIndex)");
        resizeMove.Should().Contain("FindRowMetric(Viewport.RowMetrics, _resizeIndex)");
        resizeMove.Should().NotContain("Viewport!.ColMetrics");
        resizeMove.Should().NotContain("Viewport!.RowMetrics");
        resizeMove.Should().NotContain("FirstOrDefault");
        resizeStart.Should().Contain("FindColMetric(Viewport!.ColMetrics, index)");
        resizeStart.Should().Contain("FindRowMetric(Viewport!.RowMetrics, index)");
        resizeStart.Should().NotContain(".First(");
    }

    [Fact]
    public void PivotChartFieldButtonHitTest_ScansChartsBackToFrontWithoutLinqIterators()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.HitTesting.cs"));
        var hitTest = source[
            source.IndexOf("public static (ChartModel Chart, string FieldButton)? HitTestPivotChartFieldButton", StringComparison.Ordinal)..];

        hitTest.Should().Contain("for (var i = charts.Count - 1; i >= 0; i--)");
        hitTest.Should().NotContain(".Where(");
        hitTest.Should().NotContain(".Reverse(");
    }

    [Fact]
    public void ChartRenderer_BuildsChartCellLookupWithoutLinqFiltering()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "ChartRenderer.cs"));
        var buildLookup = source[
            source.IndexOf("private static Dictionary<(uint Row, uint Col), DisplayCell> BuildChartCellLookup", StringComparison.Ordinal)..
            source.IndexOf("private static LineSeries CreateLineSeries", StringComparison.Ordinal)];

        buildLookup.Should().Contain("foreach (var cell in viewport.ChartDataCells)");
        buildLookup.Should().Contain("if (cell.SheetId != sheetId)");
        buildLookup.Should().Contain("cell.RawValue");
        buildLookup.Should().NotContain(".Where(");
        buildLookup.Should().NotContain(".Select(");
    }

    [Fact]
    public void RenderCharts_ReusesCachedChartImagesAcrossRepaints()
    {
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var drawingSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var cacheSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.ChartRenderCache.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var renderCharts = drawingSource[
            drawingSource.IndexOf("private void RenderCharts", StringComparison.Ordinal)..
            drawingSource.IndexOf("private void RenderTextBoxes", StringComparison.Ordinal)];
        var getCachedChartImage = cacheSource[
            cacheSource.IndexOf("private ImageSource? GetCachedChartImage", StringComparison.Ordinal)..
            cacheSource.IndexOf("private void ClearChartRenderCache", StringComparison.Ordinal)];

        gridViewSource.Should().Contain("private readonly Dictionary<ChartRenderCacheKey, ImageSource> _chartRenderCache = new();");
        drawingSource.Should().Contain("GetCachedChartImage(chart, Viewport, WorkbookTheme, renderScale)");
        drawingSource.Should().NotContain("ChartRenderer.Render(chart, Viewport, WorkbookTheme)");
        cacheSource.Should().Contain("_chartRenderCache.TryGetValue");
        renderCharts.Should().Contain("var dpi = VisualTreeHelper.GetDpi(this);");
        renderCharts.Should().Contain("var zoom = ZoomFactor > 0 ? ZoomFactor : 1.0;");
        renderCharts.Should().Contain("var renderScale = Math.Clamp(Math.Max(dpi.DpiScaleX, dpi.DpiScaleY) * zoom, 0.25, 4.0);");
        getCachedChartImage.Should().Contain("double renderScale");
        getCachedChartImage.Should().NotContain("VisualTreeHelper.GetDpi(this)");
        getCachedChartImage.Should().NotContain("ZoomFactor > 0 ? ZoomFactor : 1.0");
        cacheSource.Should().Contain("private readonly double _renderScale;");
        cacheSource.Should().Contain("chart.Width * renderScale");
        cacheSource.Should().Contain("chart.Height * renderScale");
        cacheSource.Should().Contain("ChartRenderer.Render(chart, viewport, theme, renderScale)");
        propertiesSource.Should().Contain("OnChartRenderCacheInputChanged");
        propertiesSource.Should().Contain("grid.ClearChartRenderCache();");
    }

    [Fact]
    public void RenderNativeControls_ReusesPixelsPerDipAcrossClippedTextCalls()
    {
        var drawingSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var renderNativeControls = drawingSource[
            drawingSource.IndexOf("private void RenderNativeSlicerTimelineControls", StringComparison.Ordinal)..
            drawingSource.IndexOf("public static bool TryCreateDrawingAnchorRect", StringComparison.Ordinal)];
        var drawNativeSlicer = drawingSource[
            drawingSource.IndexOf("private void DrawNativeSlicerControl", StringComparison.Ordinal)..
            drawingSource.IndexOf("private void DrawNativeTimelineControl", StringComparison.Ordinal)];
        var drawNativeTimeline = drawingSource[
            drawingSource.IndexOf("private void DrawNativeTimelineControl", StringComparison.Ordinal)..
            drawingSource.IndexOf("private void DrawNativeControlFrame", StringComparison.Ordinal)];
        var drawNativeFrame = drawingSource[
            drawingSource.IndexOf("private void DrawNativeControlFrame", StringComparison.Ordinal)..
            drawingSource.IndexOf("private void DrawClippedText", StringComparison.Ordinal)];
        var drawClippedText = drawingSource[
            drawingSource.IndexOf("private void DrawClippedText", StringComparison.Ordinal)..
            drawingSource.IndexOf("private static string GetNativeControlCaption", StringComparison.Ordinal)];

        renderNativeControls.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderNativeControls.Should().Contain("DrawNativeSlicerControl(dc, controlRect, slicer, pixelsPerDip);");
        renderNativeControls.Should().Contain("DrawNativeTimelineControl(dc, controlRect, timeline, pixelsPerDip);");
        drawNativeSlicer.Should().Contain("DrawNativeControlFrame(dc, rect, GetNativeControlCaption(slicer.Caption, slicer.Name, slicer.DrawingShapeName), pixelsPerDip);");
        drawNativeSlicer.Should().Contain("DrawClippedText(dc, tileText, tileRect, NativeControlMutedTextBrush, 10, verticalPadding: 1, pixelsPerDip);");
        drawNativeTimeline.Should().Contain("DrawNativeControlFrame(dc, rect, GetNativeControlCaption(timeline.Caption, timeline.Name, timeline.DrawingShapeName), pixelsPerDip);");
        drawNativeTimeline.Should().Contain("DrawClippedText(dc, label, new Rect");
        drawNativeTimeline.Should().Contain("pixelsPerDip);");
        drawNativeFrame.Should().Contain("DrawClippedText(dc, caption, new Rect");
        drawNativeFrame.Should().Contain("pixelsPerDip);");
        drawClippedText.Should().Contain("double pixelsPerDip)");
        drawClippedText.Should().Contain("GetDrawingObjectText(");
        drawClippedText.Should().NotContain("VisualTreeHelper.GetDpi(this)");
    }

    [Fact]
    public void RenderObjectPlaceholders_ReusesPixelsPerDipAcrossPlaceholderLabels()
    {
        var drawingSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjects.cs"));
        var renderObjectPlaceholders = drawingSource[
            drawingSource.IndexOf("private void RenderObjectPlaceholders", StringComparison.Ordinal)..
            drawingSource.IndexOf("public static string CreateObjectPlaceholderLabel", StringComparison.Ordinal)];
        var drawObjectPlaceholder = drawingSource[
            drawingSource.IndexOf("private void DrawObjectPlaceholder", StringComparison.Ordinal)..
            drawingSource.IndexOf("private static void DrawPlaceholderDiagonals", StringComparison.Ordinal)];

        renderObjectPlaceholders.Should().Contain("var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel(\"Chart\", chart.Name, index), pixelsPerDip);");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel(\"Shape\", shape.Name, index), pixelsPerDip);");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel(\"Picture\", picture.Name, index), pixelsPerDip);");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, rect, CreateObjectPlaceholderLabel(\"Text Box\", textBox.Name, index), pixelsPerDip);");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, controlRect, CreateObjectPlaceholderLabel(\"Slicer\"");
        renderObjectPlaceholders.Should().Contain("DrawObjectPlaceholder(dc, controlRect, CreateObjectPlaceholderLabel(\"Timeline\"");
        drawObjectPlaceholder.Should().Contain("double pixelsPerDip)");
        drawObjectPlaceholder.Should().Contain("GetDrawingObjectText(");
        drawObjectPlaceholder.Should().Contain("var textClipRect = new Rect(rect.Left + 4, rect.Top + 4, textWidth, textHeight);");
        drawObjectPlaceholder.Should().Contain("dc.PushClip(GetDrawingObjectClipGeometry(textClipRect));");
        drawObjectPlaceholder.Should().NotContain("VisualTreeHelper.GetDpi(this)");
        drawObjectPlaceholder.Should().NotContain("new RectangleGeometry");
        drawObjectPlaceholder.Should().NotContain("GetDrawingObjectClipGeometry(new Rect");
    }

    [Fact]
    public void DrawingObjectLayers_ReuseFrozenDrawingLayerAcrossStableRepaints()
    {
        var dispatch = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.RenderDispatch.cs"));
        var cache = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.DrawingObjectLayerCache.cs"));
        var properties = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var renderPostSelectionLayers = dispatch[
            dispatch.IndexOf("private void RenderPostSelectionLayers", StringComparison.Ordinal)..
            dispatch.IndexOf("private bool HasPostSelectionLayerWork", StringComparison.Ordinal)];

        renderPostSelectionLayers.Should().Contain("RenderDrawingObjectLayersWithCache(dc);");
        renderPostSelectionLayers.Should().NotContain("RenderDrawingShapes(dc);");
        renderPostSelectionLayers.Should().NotContain("RenderTextBoxes(dc);");
        cache.Should().Contain("private DrawingGroup? _drawingObjectLayerCache;");
        cache.Should().Contain("private readonly record struct DrawingObjectLayerCacheKey");
        cache.Should().Contain("dc.DrawDrawing(cached);");
        cache.Should().Contain("RenderDrawingObjectLayers(groupContext);");
        cache.Should().Contain("group.Freeze();");
        cache.Should().Contain("GridRange? SelectedRange");
        cache.Should().Contain("IReadOnlyList<DrawingShapeModel>? DrawingShapes");
        cache.Should().Contain("IReadOnlyList<TextBoxModel>? TextBoxes");
        cache.Should().Contain("IReadOnlyList<PictureModel>? Pictures");
        cache.Should().Contain("private void ClearDrawingObjectLayerCache()");
        properties.Should().Contain("OnDrawingObjectLayerInputChanged");
        properties.Should().Contain("grid.ClearDrawingObjectLayerCache();");
        properties.Should().Contain("new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingObjectLayerInputChanged)");
    }

    [Fact]
    public void RenderManualPageBreaks_ScansVisibleMetricsOnce()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var gridViewSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var renderManualPageBreaks = source[
            source.IndexOf("private void RenderManualPageBreaks", StringComparison.Ordinal)..
            source.IndexOf("public enum FormulaTraceArrowLayoutKind", StringComparison.Ordinal)];

        renderManualPageBreaks.Should().Contain("GetPageBreakLookup(rowPageBreaks, ref _rowPageBreakLookupCache)");
        renderManualPageBreaks.Should().Contain("GetPageBreakLookup(columnPageBreaks, ref _columnPageBreakLookupCache)");
        renderManualPageBreaks.Should().Contain("pageBreaks is IReadOnlySet<uint> set");
        renderManualPageBreaks.Should().Contain("CalculatePageBreakFingerprint(pageBreaks)");
        renderManualPageBreaks.Should().Contain("cache.Fingerprint == fingerprint");
        renderManualPageBreaks.Should().Contain("foreach (var metric in Viewport.RowMetrics)");
        renderManualPageBreaks.Should().Contain("foreach (var metric in Viewport.ColMetrics)");
        renderManualPageBreaks.Should().NotContain("FirstOrDefault");
        gridViewSource.Should().Contain("private PageBreakLookupCache? _rowPageBreakLookupCache;");
        propertiesSource.Should().Contain("OnRowPageBreaksChanged");
        propertiesSource.Should().Contain("OnColumnPageBreaksChanged");
    }

    [Fact]
    public void FormulaTraceLayoutPlanner_AvoidsPerArrowLinqMetricScansAndLookupAllocations()
    {
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "FormulaTraceLayoutPlanner.cs"));
        var overlaysSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var calculateLayouts = source[
            source.IndexOf("public static IReadOnlyList<FormulaTraceArrowLayout> CalculateLayouts", StringComparison.Ordinal)..
            source.IndexOf("public static void VisitLayouts<TConsumer>", StringComparison.Ordinal)];
        var visitLayouts = source[
            source.IndexOf("public static void VisitLayouts<TConsumer>", StringComparison.Ordinal)..
            source.IndexOf("public static CellAddress? HitTestMarker", StringComparison.Ordinal)];
        var metricLookup = source[
            source.IndexOf("private readonly struct FormulaTraceMetricLookup", StringComparison.Ordinal)..
            source.IndexOf("private static bool TryGetMarkerHit", StringComparison.Ordinal)];
        var renderFormulaTrace = overlaysSource[
            overlaysSource.IndexOf("private void RenderFormulaTraceArrows", StringComparison.Ordinal)..
            overlaysSource.IndexOf("public static IReadOnlyList<FormulaTraceArrowLayout> CalculateFormulaTraceArrowLayouts", StringComparison.Ordinal)];

        source.Should().Contain("public interface IFormulaTraceArrowLayoutConsumer");
        calculateLayouts.Should().Contain("var consumer = new FormulaTraceArrowLayoutCollector(arrows.Count);");
        calculateLayouts.Should().Contain("VisitLayouts(viewport, arrows, sheetId, ref consumer);");
        visitLayouts.Should().Contain("where TConsumer : struct, IFormulaTraceArrowLayoutConsumer");
        visitLayouts.Should().Contain("var metrics = new FormulaTraceMetricLookup(viewport, GridView.CalculateRowHeaderWidth(viewport));");
        visitLayouts.Should().Contain("metrics.TryGetCellRect");
        visitLayouts.Should().Contain("consumer.AcceptLayout(");
        visitLayouts.Should().NotContain("new List<FormulaTraceArrowLayout>");
        visitLayouts.Should().NotContain("new FormulaTraceArrowLayout");
        source.Should().NotContain("Dictionary<");
        source.Should().NotContain("BuildRowMetricLookup");
        source.Should().NotContain("BuildColMetricLookup");
        metricLookup.Should().Contain("_rowArray = _rows as RowMetric[];");
        metricLookup.Should().Contain("_rowList = _rows as List<RowMetric>;");
        metricLookup.Should().Contain("_colArray = _columns as ColMetric[];");
        metricLookup.Should().Contain("_colList = _columns as List<ColMetric>;");
        renderFormulaTrace.Should().Contain("FormulaTraceLayoutPlanner.VisitLayouts(viewport, arrows, FormulaTraceSheetId, ref consumer);");
        renderFormulaTrace.Should().NotContain("CalculateFormulaTraceArrowLayouts");
        renderFormulaTrace.Should().NotContain("foreach");
        overlaysSource.Should().Contain("private readonly struct FormulaTraceArrowDrawingConsumer");
        metricLookup.Should().Contain("var row = FindRowMetric(address.Row);");
        metricLookup.Should().Contain("var col = FindColMetric(address.Col);");
        metricLookup.Should().Contain("_firstRow = _hasRows ? _rows[0].Row : 0;");
        metricLookup.Should().Contain("_lastRow = _hasRows ? _rows[^1].Row : 0;");
        metricLookup.Should().Contain("_firstCol = _hasColumns ? _columns[0].Col : 0;");
        metricLookup.Should().Contain("_lastCol = _hasColumns ? _columns[^1].Col : 0;");
        metricLookup.Should().Contain("row < _firstRow || row > _lastRow");
        metricLookup.Should().Contain("col < _firstCol || col > _lastCol");
        metricLookup.Should().Contain("FormulaTraceLayoutPlanner.FindRowMetric(_rowArray, row, _firstRow)");
        metricLookup.Should().Contain("FormulaTraceLayoutPlanner.FindColMetric(_colArray, col, _firstCol)");
        metricLookup.Should().NotContain("TryGetValue");
        metricLookup.Should().NotContain("FirstOrDefault");
        source.Should().Contain("private static RowMetric? FindRowMetric");
        source.Should().Contain("private static ColMetric? FindColMetric");
        source.Should().Contain("var index = row - firstRow;");
        source.Should().Contain("var index = col - firstCol;");
        source.Should().Contain("while (low <= high)");
    }

    [Fact]
    public void DrawFormulaTraceArrow_ReusesCachedFrozenArrowDrawingsAndArrowHeadGeometry()
    {
        var overlaysSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Overlays.cs"));
        var propertiesSource = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Properties.cs"));
        var drawFormulaTraceArrow = overlaysSource[
            overlaysSource.IndexOf("private void DrawFormulaTraceArrow", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private Drawing GetFormulaTraceArrowDrawing", StringComparison.Ordinal)];
        var getArrowDrawing = overlaysSource[
            overlaysSource.IndexOf("private Drawing GetFormulaTraceArrowDrawing", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private Drawing CreateFormulaTraceArrowDrawing", StringComparison.Ordinal)];
        var createArrowDrawing = overlaysSource[
            overlaysSource.IndexOf("private Drawing CreateFormulaTraceArrowDrawing", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private Geometry GetFormulaTraceArrowHeadGeometry", StringComparison.Ordinal)];
        var getArrowHeadGeometry = overlaysSource[
            overlaysSource.IndexOf("private Geometry GetFormulaTraceArrowHeadGeometry", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private static Geometry CreateFormulaTraceArrowHeadGeometry", StringComparison.Ordinal)];
        var createArrowHeadGeometry = overlaysSource[
            overlaysSource.IndexOf("private static Geometry CreateFormulaTraceArrowHeadGeometry", StringComparison.Ordinal)..
            overlaysSource.IndexOf("private void ClearFormulaTraceArrowHeadGeometryCache", StringComparison.Ordinal)];

        overlaysSource.Should().Contain("private const int FormulaTraceArrowHeadGeometryCacheLimit = 4096;");
        overlaysSource.Should().Contain("private const int FormulaTraceArrowDrawingCacheLimit = 4096;");
        overlaysSource.Should().Contain("private readonly Dictionary<FormulaTraceArrowHeadGeometryKey, Geometry> _formulaTraceArrowHeadGeometryCache = new();");
        overlaysSource.Should().Contain("private readonly Dictionary<FormulaTraceArrowDrawingKey, Drawing> _formulaTraceArrowDrawingCache = new();");
        overlaysSource.Should().Contain("private readonly record struct FormulaTraceArrowHeadGeometryKey(Point Start, Point End);");
        overlaysSource.Should().Contain("private readonly record struct FormulaTraceArrowDrawingKey(Point Start, Point End);");
        drawFormulaTraceArrow.Should().Contain("GetFormulaTraceArrowDrawing(start, end)");
        drawFormulaTraceArrow.Should().NotContain("CreateFormulaTraceArrowHeadGeometry");
        getArrowDrawing.Should().Contain("_formulaTraceArrowDrawingCache.TryGetValue(key, out var cached)");
        getArrowDrawing.Should().Contain("_formulaTraceArrowDrawingCache.Count >= FormulaTraceArrowDrawingCacheLimit");
        getArrowDrawing.Should().Contain("_formulaTraceArrowDrawingCache.Clear();");
        getArrowDrawing.Should().Contain("_formulaTraceArrowDrawingCache.Add(key, drawing);");
        createArrowDrawing.Should().Contain("new DrawingGroup()");
        createArrowDrawing.Should().Contain("GetFormulaTraceArrowHeadGeometry(start, end, vector, perpendicular)");
        createArrowDrawing.Should().Contain("drawing.Freeze();");
        getArrowHeadGeometry.Should().Contain("_formulaTraceArrowHeadGeometryCache.TryGetValue(key, out var cached)");
        getArrowHeadGeometry.Should().Contain("_formulaTraceArrowHeadGeometryCache.Count >= FormulaTraceArrowHeadGeometryCacheLimit");
        getArrowHeadGeometry.Should().Contain("_formulaTraceArrowHeadGeometryCache.Clear();");
        getArrowHeadGeometry.Should().Contain("_formulaTraceArrowHeadGeometryCache.Add(key, geometry);");
        createArrowHeadGeometry.Should().Contain("new StreamGeometry()");
        createArrowHeadGeometry.Should().Contain("geometry.Freeze();");
        overlaysSource.Should().Contain("_formulaTraceArrowHeadGeometryCache.Clear();");
        overlaysSource.Should().Contain("_formulaTraceArrowDrawingCache.Clear();");
        propertiesSource.Should().Contain("OnFormulaTraceRenderCacheInputChanged");
        propertiesSource.Should().Contain("grid.ClearFormulaTraceArrowHeadGeometryCache();");
        propertiesSource.Should().Contain("FormulaTraceArrowsProperty");
        propertiesSource.Should().Contain("FormulaTraceSheetIdProperty");
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_BoundsTallMergeWorkToVisibleCells()
    {
        var sheetId = SheetId.New();
        var viewport = new ViewportModel(
            [],
            [new RowMetric(500_000, 18, 0)],
            [new ColMetric(10, 64, 0)],
            SplitPanes: new SplitPaneState(
                4,
                4,
                [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)],
                [
                    Cell(1, 1, "anchor"),
                    Cell(500_000, 1, "covered"),
                    Cell(1, 10, "visible")
                ]));
        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, CellAddress.MaxRow, 2))
        };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var layouts = SplitPaneCellLayoutPlanner.CalculateLayouts(viewport, mergedRegions);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);
        allocatedBytes.Should().BeLessThan(1_000_000);
        layouts.Select(layout => (layout.Cell.Row, layout.Cell.Col, layout.Rect))
            .Should().Equal(
                (1u, 1u, new Rect(rowHeaderWidth, GridView.ColHeaderHeight, 144, 40)),
                (1u, 10u, new Rect(rowHeaderWidth + 144, GridView.ColHeaderHeight, 64, 18)));
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_BuildsMetricLookupsAndLazyOccupiedCellsWithoutLinqPipelines()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "SplitPaneCellLayoutPlanner.cs"));
        var calculateLayouts = source[
            source.IndexOf("public static IReadOnlyList<SplitPaneCellLayout> CalculateLayouts", StringComparison.Ordinal)..
            source.IndexOf("private static bool CanOverflowSplitPaneText", StringComparison.Ordinal)];
        var buildOccupiedCells = source[
            source.IndexOf("private static SplitPaneOccupiedCellMap BuildOccupiedCells", StringComparison.Ordinal)..
            source.IndexOf("private static double SumEmptyOverflowColumnWidths", StringComparison.Ordinal)];
        var mergeRangeIndex = source[
            source.IndexOf("public static MergeRangeIndex Create", StringComparison.Ordinal)..
            source.IndexOf("public GridRange? Find", StringComparison.Ordinal)];

        calculateLayouts.Should().Contain("BuildRowLookup(topRows)");
        calculateLayouts.Should().Contain("BuildRowLookup(bottomLeftRows)");
        calculateLayouts.Should().Contain("BuildColumnLookup(leftColumns)");
        calculateLayouts.Should().Contain("BuildColumnLookup(topRightColumns)");
        calculateLayouts.Should().Contain("ResolveSplitPaneRegion(isTopPane, isLeftPane)");
        calculateLayouts.Should().Contain("if (cells.Count == 0)");
        calculateLayouts.Should().Contain("var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);");
        calculateLayouts.Should().Contain("var verticalX = dividerLayout.VerticalX ?? rowHeaderWidth;");
        calculateLayouts.Should().Contain("? rowHeaderWidth + column.LeftOffset");
        calculateLayouts.Should().Contain("VisitLayouts(viewport, mergedRegions, editingCell, ref consumer);");
        calculateLayouts.Should().Contain("private struct SplitPaneCellLayoutCollector");
        calculateLayouts.Should().Contain("new List<SplitPaneCellLayout>(_capacity)");
        calculateLayouts.Should().Contain("SplitPaneOccupiedCellMap? occupied = null;");
        calculateLayouts.Should().Contain("occupied ??= BuildOccupiedCells(cells, editingCell)");
        calculateLayouts.Should().Contain("SumEmptyOverflowColumnWidths(cell, colMetrics, occupied.Value)");
        calculateLayouts.Should().Contain("foreach (var cell in cells)");
        calculateLayouts.Should().Contain("consumer.AcceptLayout(new SplitPaneCellLayout");
        calculateLayouts.IndexOf("if (cells.Count == 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(calculateLayouts.IndexOf("BuildRowLookup(topRows)", StringComparison.Ordinal));
        calculateLayouts.IndexOf("var rowHeaderWidth = GridView.CalculateRowHeaderWidth(viewport);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(calculateLayouts.IndexOf("foreach (var cell in cells)", StringComparison.Ordinal));
        calculateLayouts[
            calculateLayouts.IndexOf("foreach (var cell in cells)", StringComparison.Ordinal)..]
            .Should()
            .NotContain("GridView.CalculateRowHeaderWidth(viewport)");
        calculateLayouts.Should().NotContain("spansByRow.Add(cell.Row, spans)");
        buildOccupiedCells.Should().Contain("spansByRow.Add(cell.Row, spans)");
        buildOccupiedCells.Should().Contain("AddOccupiedColumn(spans, cell.Col, ref needsNormalize)");
        buildOccupiedCells.Should().Contain("NormalizeOccupiedColumnSpans(spansByRow)");
        buildOccupiedCells.Should().Contain("new SplitPaneOccupiedCellMap(spansByRow)");
        mergeRangeIndex.Should().Contain("var queryCells = BuildQueryCells(cells);");
        mergeRangeIndex.Should().Contain("mergedRegion.End.Row < queryCells.MinRow");
        mergeRangeIndex.Should().Contain("mergedRegion.Start.Row > queryCells.MaxRow");
        mergeRangeIndex.Should().Contain("mergedRegion.End.Col < queryCells.MinCol");
        mergeRangeIndex.Should().Contain("mergedRegion.Start.Col > queryCells.MaxCol");
        mergeRangeIndex.Should().Contain("foreach (var row in queryCells.Rows)");
        calculateLayouts.Should().NotContain(".ToDictionary(");
        calculateLayouts.Should().NotContain(".Where(");
        calculateLayouts.Should().NotContain(".Select(");
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_NumericCellsSkipOverflowOccupancyAllocation()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.App.UI", "SplitPaneCellLayoutPlanner.cs"));

        source.Should().Contain("SplitPaneOccupiedCellMap? occupied = null;");
        source.Should().Contain("occupied ??= BuildOccupiedCells(cells, editingCell);");

        var sheetId = SheetId.New();
        var cells = new List<DisplayCell>();
        for (uint col = 1; col <= 80; col++)
            cells.Add(new DisplayCell(1, col, new NumberValue(col), col.ToString(), null, StyleId.Default, null, null));

        var viewport = new ViewportModel(
            cells,
            [new RowMetric(1, 18, 0)],
            Enumerable.Range(1, 80)
                .Select(index => new ColMetric((uint)index, 64, (index - 1) * 64))
                .ToList(),
            SplitPanes: new SplitPaneState(
                2,
                2,
                [new RowMetric(1, 18, 0)],
                Enumerable.Range(1, 80)
                    .Select(index => new ColMetric((uint)index, 64, (index - 1) * 64))
                    .ToList(),
                cells));

        var mergedRegions = new[]
        {
            new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 2))
        };

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var layouts = SplitPaneCellLayoutPlanner.CalculateLayouts(viewport, mergedRegions);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        layouts.Should().HaveCount(79);
        allocatedBytes.Should().BeLessThan(
            45_000,
            "numeric split-pane cells cannot overflow, so the occupied-cell HashSet should stay unallocated");
    }

    [Fact]
    public void SplitPaneCellLayoutPlanner_EmptyFormulaCellsStopTextOverflow()
    {
        var cells = new[]
        {
            Cell(1, 1, "long text"),
            new DisplayCell(1, 2, new TextValue(""), "", "IF(A1,\"\",\"\")", StyleId.Default, null),
            Cell(1, 3, "")
        };
        var viewport = new ViewportModel(
            cells,
            [new RowMetric(1, 18, 0)],
            [
                new ColMetric(1, 40, 0),
                new ColMetric(2, 40, 40),
                new ColMetric(3, 40, 80)
            ],
            SplitPanes: new SplitPaneState(
                2,
                2,
                [new RowMetric(1, 18, 0)],
                [
                    new ColMetric(1, 40, 0),
                    new ColMetric(2, 40, 40),
                    new ColMetric(3, 40, 80)
                ],
                cells));

        var layouts = SplitPaneCellLayoutPlanner.CalculateLayouts(viewport);

        layouts.Single(layout => layout.Cell.Col == 1)
            .TextClipRect.Width
            .Should().Be(40);
    }

    [Fact]
    public void RenderSplitPaneCells_UsesPrecomputedLayoutRegionForClipping()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var splitPanes = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.SplitPanes.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private GridRange? FindMerge", StringComparison.Ordinal)];
        var setup = renderSplitPaneCells[..renderSplitPaneCells.IndexOf("var consumer = new SplitPaneCellRenderConsumer", StringComparison.Ordinal)];
        var renderConsumer = renderSplitPaneCells[
            renderSplitPaneCells.IndexOf("private readonly struct SplitPaneCellRenderConsumer", StringComparison.Ordinal)..];

        setup.Should().Contain("var topLeftClip = FrozenClipGeometry(clips.TopLeft)");
        setup.Should().Contain("var bottomRightClip = FrozenClipGeometry(clips.BottomRight)");
        renderSplitPaneCells.Should().Contain("SplitPaneCellLayoutPlanner.VisitLayouts(Viewport, MergedRegions, EditingCell, ref consumer);");
        renderConsumer.Should().Contain("GetSplitPaneClipGeometryForRegion(");
        renderConsumer.Should().Contain("layout.Region");
        renderConsumer.Should().Contain("if (clipGeometry.Rect.Width <= 0 || clipGeometry.Rect.Height <= 0)");
        renderConsumer.IndexOf("if (clipGeometry.Rect.Width <= 0 || clipGeometry.Rect.Height <= 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderConsumer.IndexOf("dc.PushClip(clipGeometry);", StringComparison.Ordinal));
        renderConsumer.Should().Contain("grid.RenderSplitPaneCell(dc, layout, gridPen, pixelsPerDip);");
        renderConsumer.Should().NotContain("new RectangleGeometry(clipRect)");
        renderConsumer.Should().NotContain("GetSplitPaneClipRectForCell");
        splitPanes.Should().NotContain("GetSplitPaneClipRectForCell");
        rendering.Should().Contain("geometry.Freeze();");
        splitPanes.Should().Contain("public readonly record struct SplitPaneCellLayout(DisplayCell Cell, Rect Rect, Rect TextClipRect, SplitPaneRegion Region)");
    }

    [Fact]
    public void RenderSplitPaneCells_RespectsHiddenGridLinesForDefaultCellBorders()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var gridPen = ShowGridLines ? GridPen : null;");
        renderSplitPaneCells.Should().Contain("dc.DrawRectangle(fill, gridPen, rect);");
        renderSplitPaneCells.Should().Contain("DrawBorderEdge(dc, style.BorderTop");
        renderSplitPaneCells.Should().NotContain("dc.DrawRectangle(fill, GridPen, rect);");
    }

    [Fact]
    public void RenderSplitPaneCells_SkipsZeroSizedCellsBeforeDrawingWork()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("if (rect.Width <= 0 || rect.Height <= 0)");
        renderSplitPaneCells.IndexOf("if (rect.Width <= 0 || rect.Height <= 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("var style = cell.Style;", StringComparison.Ordinal));
        renderSplitPaneCells.IndexOf("if (rect.Width <= 0 || rect.Height <= 0)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("dc.PushClip(clipGeometry);", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderSplitPaneCells_SkipsNoOpDefaultBackgroundDraw()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("if (fill is not null || gridPen is not null)");
        renderSplitPaneCells.Should().Contain("dc.DrawRectangle(fill, gridPen, rect);");
        renderSplitPaneCells.IndexOf("if (fill is not null || gridPen is not null)", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("DrawFillPattern(dc, rect, style", StringComparison.Ordinal));
        renderSplitPaneCells.IndexOf("DrawFillPattern(dc, rect, style", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("if (style is not null && HasVisibleCellBorder(style))", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderSplitPaneCells_ClipsConditionalIconTextToAdjustedTextRect()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];
        var conditionalIconBlock = renderSplitPaneCells[
            renderSplitPaneCells.IndexOf("if (cell.ConditionalIcon is { } splitIcon)", StringComparison.Ordinal)..
            renderSplitPaneCells.IndexOf("var hAlign = style?.HorizontalAlignment", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var textClipRect = layout.TextClipRect;");
        conditionalIconBlock.Should().Contain("rect = iconLayout.TextRect;");
        conditionalIconBlock.Should().Contain("textClipRect = AdjustConditionalIconTextClipRect(layout.TextClipRect, rect);");
        renderSplitPaneCells.Should().Contain("var shouldClipText = ShouldClipText(wrapText, textClipRect, text, textPoint);");
        renderSplitPaneCells.Should().Contain("if (shouldClipText)");
        renderSplitPaneCells.Should().Contain("dc.PushClip(GetCellClipGeometry(textClipRect));");
        renderSplitPaneCells.Should().Contain("private static Rect AdjustConditionalIconTextClipRect(Rect clipRect, Rect textRect)");
    }

    [Fact]
    public void RenderSplitPaneCells_OnlyPushesTextClipWhenTextNeedsClipping()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var textPoint = new Point(Math.Round(textX), Math.Round(textY));");
        renderSplitPaneCells.Should().Contain("var shouldClipText = ShouldClipText(wrapText, textClipRect, text, textPoint);");
        renderSplitPaneCells.IndexOf("dc.PushClip(GetCellClipGeometry(textClipRect));", StringComparison.Ordinal)
            .Should()
            .BeLessThan(renderSplitPaneCells.IndexOf("dc.DrawText(text, textPoint);", StringComparison.Ordinal));
        renderSplitPaneCells.LastIndexOf("dc.Pop();", StringComparison.Ordinal)
            .Should()
            .BeGreaterThan(renderSplitPaneCells.IndexOf("dc.DrawText(text, textPoint);", StringComparison.Ordinal));
    }

    [Fact]
    public void RenderSplitPaneCells_UsesWrappedTextLayoutCacheForDefaultWrappedCells()
    {
        var rendering = File.ReadAllText(FindWorkspaceFile("src", "FreeX.App.UI", "GridView.Rendering.cs"));
        var renderSplitPaneCells = rendering[
            rendering.IndexOf("private void RenderSplitPaneCells(DrawingContext dc)", StringComparison.Ordinal)..
            rendering.IndexOf("private static RectangleGeometry FrozenClipGeometry", StringComparison.Ordinal)];
        var textSetup = renderSplitPaneCells[
            renderSplitPaneCells.IndexOf("var hAlign = style?.HorizontalAlignment", StringComparison.Ordinal)..
            renderSplitPaneCells.IndexOf("if (style?.ShrinkToFit == true && !wrapText)", StringComparison.Ordinal)];

        renderSplitPaneCells.Should().Contain("var wrapText = style?.WrapText == true;");
        renderSplitPaneCells.Should().Contain("var useDefaultTextLayout = CanUseDefaultFormattedText(style, wrapText);");
        renderSplitPaneCells.Should().Contain("var wrapMaxTextWidth = wrapText ? Math.Max(1, rect.Width - 4) : 0;");
        renderSplitPaneCells.Should().Contain("var wrapTextAlignment = TextAlignment.Left;");
        renderSplitPaneCells.Should().Contain("if (!useDefaultTextLayout && wrapText)");
        renderSplitPaneCells.Should().Contain("useDefaultWrappedTextLayout = CanUseDefaultWrappedFormattedText(style);");
        renderSplitPaneCells.Should().Contain("GetDefaultWrappedFormattedText(cell.DisplayText, fontSize, wrapMaxTextWidth, wrapTextAlignment, pixelsPerDip)");
        textSetup.Should().NotContain("CreateCellTypeface");
        textSetup.Should().NotContain("BrushForCellColor");
        renderSplitPaneCells.Should().Contain("text.MaxTextWidth = wrapMaxTextWidth;");
        renderSplitPaneCells.Should().Contain("text.TextAlignment = wrapTextAlignment;");
        renderSplitPaneCells.Should().Contain("if (style?.ShrinkToFit == true && !wrapText)");
        renderSplitPaneCells.Should().NotContain("CanUseDefaultFormattedText(style, wrapText: false)");
        renderSplitPaneCells.Should().NotContain("style.WrapText != true");
    }

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

    private static DisplayCell Cell(uint row, uint col, string text, CellStyle? style = null) =>
        new(row, col, new TextValue(text), text, null, StyleId.Default, null, style);
}
