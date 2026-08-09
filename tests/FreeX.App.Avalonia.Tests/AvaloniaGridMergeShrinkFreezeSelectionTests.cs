using System.IO;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guards for review3 findings H29/H30/H31/H55: the Avalonia worksheet grid used to
/// render merged regions as separate 1x1 cells (no span, no merge-aware selection), never applied
/// Shrink to fit when measuring/rendering cell text, and drew no divider line for Freeze Panes.
///
/// These are source-content assertions (matching the established convention for this class — see
/// <c>AvaloniaGridInputSourceTests</c> / <c>DataValidationDialogSourceTests</c>) because the relevant
/// state (<c>_sheetGridHost</c>, <c>BuildSheetGrid</c>) is private and not exposed for direct visual-tree
/// inspection from tests.
/// </summary>
public sealed class AvaloniaGridMergeShrinkFreezeSelectionTests
{
    // ── H29: merged regions must span, not render as separate 1x1 cells ──────────────────────

    [Fact]
    public void BuildSheetGrid_LooksUpMergeRegionAndSkipsNonAnchorMemberCells()
    {
        var source = MainWindowSource();

        // The anchor/member distinction must be resolved via the sheet's real merge lookup.
        source.Should().Contain("_session.ActiveSheet.GetMergeRegion(address)");
        // Updated for J4 (review4): the anchor/member check is no longer a plain merge.Start
        // comparison — when the true anchor has scrolled out of the viewport, the topmost/leftmost
        // still-visible member becomes a substitute anchor via ResolveVisibleMergeAnchor, so the
        // merge keeps rendering instead of leaving a blank hole. See
        // AvaloniaMainWindowGridRenderStage1Tests for full J4 coverage.
        source.Should().Contain(
            "ViewportGeometryPlanner.ResolveVisibleMergeAnchor(merge, rowMetrics, colMetrics) is { } visibleAnchor",
            "non-anchor member cells of a merge must be detected via the visible-anchor resolver (which falls back past a scrolled-off true anchor), not a plain merge.Start comparison");
        source.Should().Contain(
            "continue;",
            "the loop must skip adding a separate grid child for non-anchor merge members");
    }

    [Fact]
    public void BuildSheetGrid_SpansMergeAnchorAcrossRowsAndColumns()
    {
        var source = MainWindowSource();

        source.Should().Contain("ViewportGeometryPlanner.CalculateVisibleMergeSpan(");
        source.Should().Contain("AvaloniaGrid.SetRowSpan(cellControl, rowSpan)");
        source.Should().Contain("AvaloniaGrid.SetColumnSpan(cellControl, colSpan)");
    }

    [Fact]
    public void ResolveVisibleMergeSpan_IsOwnedByPortableGeometryPlanner()
    {
        // ResolveVisibleMergeSpan must feed the anchor's own summed dimensions into CreateCell so
        // alignment/ShrinkToFit measurement operates on the FULL merged rectangle, not just the
        // anchor's single row/column.
        //
        // Updated for K10 (review5): ResolveVisibleMergeSpan now takes explicit rowMetrics/colMetrics
        // lists instead of a ViewportModel, so BuildSheetGrid can pass the split-pane-combined lists
        // (see AvaloniaMainWindowSplitPaneRtlTests) instead of always the main pane's
        // viewport.RowMetrics/ColMetrics. See AvaloniaMainWindowSplitPaneRtlTests for the split
        // coverage this enables.
        var source = MainWindowSource();

        source.Should().Contain("ViewportGeometryPlanner.CalculateVisibleMergeSpan(");
        source.Should().Contain("mergeSpan.RowSpan");
        source.Should().Contain("mergeSpan.ColumnSpan");
        source.Should().NotContain("private static (int RowSpan, int ColSpan, double Height, double Width) ResolveVisibleMergeSpan(");
    }

    // ── H55: selection highlight must expand to the full merge bounds ────────────────────────

    [Fact]
    public void IsSelectedCell_MergeOverload_TreatsMergedRegionAsOneSelectableUnit()
    {
        var source = MainWindowSource();

        source.Should().Contain("private bool IsSelectedCell(CellAddress address, GridRange? mergeRegion) =>");
        source.Should().Contain("_session.SelectedRanges.Any(range => range.Overlaps(merge))");
    }

    [Fact]
    public void CreateCell_PassesMergeRegionIntoSelectionCheck()
    {
        var source = MainWindowSource();

        source.Should().Contain(
            "private Border CreateCell(DisplayCell cell, uint row, uint col, double zoomFactor, double cellWidth, double cellHeight, GridRange? mergeRegion = null)");
        source.Should().Contain("var selected = IsSelectedCell(address, mergeRegion);");
    }

    // ── H30: Shrink to fit must shrink the font, not ellipsis-truncate at full size ──────────

    [Fact]
    public void CreateCellBorder_AppliesShrinkToFitBeforeMeasuringFillOrTrimming()
    {
        var source = MainWindowSource();

        source.Should().Contain(
            "if (style?.ShrinkToFit == true && textWrapping != TextWrapping.Wrap && !isFillAlign && !CellRichTextInlinesBuilder.HasRuns(richRuns))",
            "Shrink to fit must be gated the same way Excel gates it: off when WrapText is on, and independent of Fill alignment / rich runs");
        source.Should().Contain("adjustedFontSize = ResolveShrinkToFitFontSize(effectiveText, fontWeight, fontStyle, adjustedFontSize, availableWidth);");
    }

    [Fact]
    public void ResolveShrinkToFitFontSize_ShrinksInWholeStepsDownToTheFloor()
    {
        var method = ExtractMethod(
            "private static double ResolveShrinkToFitFontSize(",
            "private void AddSelectionOverlayToGrid(");

        method.Should().Contain("ShrinkToFitMinimumFontSize");
        method.Should().Contain("MeasureInlineCellTextWidth(text, fontSize, fontWeight, fontStyle) > availableWidth");
        method.Should().Contain("fontSize = Math.Max(ShrinkToFitMinimumFontSize, fontSize - 1);");
    }

    // ── H31: Freeze Panes divider line must be drawn ──────────────────────────────────────────

    [Fact]
    public void BuildDrawingObjectOverlay_DrawsFreezePaneDivider()
    {
        var source = MainWindowSource();

        source.Should().Contain("AddFreezePaneDividerOverlay(overlay, viewport, showHeadings, zoomFactor);");
    }

    [Fact]
    public void AddFreezePaneDividerOverlay_DrawsHorizontalAndVerticalDividersAtFrozenBoundary()
    {
        var method = ExtractMethod(
            "private void AddFreezePaneDividerOverlay(",
            "private void AddDataValidationDropdownOverlay(");

        method.Should().Contain("viewport.FrozenPanes");
        method.Should().Contain("frozenPanes.Rows > 0");
        method.Should().Contain("frozenPanes.Cols > 0");
        method.Should().Contain("Background = FreezeDividerBrush");
    }

    // ── Shared helpers ─────────────────────────────────────────────────────────────────────────

    private static string MainWindowSource() => File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

    private static string ExtractMethod(string startMarker, string endMarker)
    {
        var source = MainWindowSource();
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"expected to find '{startMarker}' in MainWindow.cs");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"expected to find '{endMarker}' after '{startMarker}' in MainWindow.cs");
        return source[start..end];
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts);
}
