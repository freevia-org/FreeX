using System.IO;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R87-meta-2: the r86 per-view IsShowingHeadings/IsShowingGridlines/GetEffectiveFrozenCols
/// overrides on WorkbookSession exist specifically so a sibling window's Freeze Panes /
/// Show Headings / Show Gridlines toggle never leaks into another open view of the same
/// sheet. Several MainWindow rendering/hit-test/layout consumers were left reading the
/// shared ActiveSheet.ShowHeadings/ShowGridlines/FrozenCols fields directly, bypassing the
/// per-view cache and re-introducing the exact cross-window split-brain the r86 fix set
/// out to eliminate (header click-to-select stops working, layout math shifts). These
/// tests assert every listed consumer now routes through the per-view accessor instead of
/// the raw ActiveSheet field.
/// </summary>
public sealed class R87_MainWindowPerViewShowHeadingsGridlinesFrozenPanesSourceTests
{
    [Fact]
    public void MainWindow_RenderingAndHitTestConsumers_DoNotReadSharedActiveSheetViewFieldsDirectly()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        // None of the r87 finding's cited consumer call sites should read the raw shared
        // ActiveSheet.ShowHeadings / ShowGridlines / FrozenCols / FrozenRows fields anymore -
        // they must all route through the per-view WorkbookSession accessors instead.
        source.Should().NotContain("_session.ActiveSheet.ShowHeadings");
        source.Should().NotContain("_session.ActiveSheet.ShowGridlines");
        source.Should().NotContain("_session.ActiveSheet.FrozenCols");
        source.Should().NotContain("_session.ActiveSheet.FrozenRows");

        // The UpdateSheetGrid-path headings rebuild (was line ~4573).
        source.Should().Contain("var viewport = _session.Viewport;\n        var showHeadings = _session.IsShowingHeadings;");

        // The drawing-object overlay builder (was line ~5127).
        source.Should().Contain(
            "private Canvas BuildDrawingObjectOverlay(ViewportModel viewport)\n    {\n        var showHeadings = _session.IsShowingHeadings;");

        // Both overflow directions delegate geometry to the shared planner while supplying the
        // per-view frozen-column cache.
        var rightOverflow = source[
            source.IndexOf("private double ResolveOverflowRightLimit(", StringComparison.Ordinal)..
            source.IndexOf("private double ResolveOverflowLeftLimit(", StringComparison.Ordinal)];
        var leftOverflow = source[
            source.IndexOf("private double ResolveOverflowLeftLimit(", StringComparison.Ordinal)..
            source.IndexOf("private bool IsOverflowOccupied(", StringComparison.Ordinal)];
        rightOverflow.Should().Contain("ViewportGeometryPlanner.CalculateOverflowAvailability(");
        rightOverflow.Should().Contain("_session.GetEffectiveFrozenCols()");
        leftOverflow.Should().Contain("ViewportGeometryPlanner.CalculateOverflowAvailability(");
        leftOverflow.Should().Contain("_session.GetEffectiveFrozenCols()");

        // The column/row header hit-test gates (were lines ~7320/~7347).
        source.Should().Contain(
            "private bool TryResolveColumnHeaderPointerIndex(PointerEventArgs args, out uint col)\n    {\n        col = 0;\n        if (!_session.IsShowingHeadings)\n            return false;");
        source.Should().Contain(
            "private bool TryResolveRowHeaderPointerIndex(PointerEventArgs args, out uint row)\n    {\n        row = 0;\n        if (!_session.IsShowingHeadings)\n            return false;");

        // The cell-border gridline flag passed into rendering (was line ~8707).
        source.Should().Contain(
            "indentPadding,\n            textRotation,\n            borderStyle,\n            _session.IsShowingGridlines,\n            zoomFactor,\n            cellWidth,\n            cellHeight,");

        // The formula-reference highlight overlay (was line ~10077).
        source.Should().Contain(
            "AddFormulaReferenceHighlightOverlay(\n            overlay,\n            _session.Viewport,\n            _session.IsShowingHeadings,\n            GetActiveZoomFactor());");

        // The scroll-viewport header-offset layout math (was line ~27767).
        source.Should().Contain(
            "var bounds = _sheetScrollViewer.Bounds;\n        var zoomFactor = GetActiveZoomFactor();\n        var showHeadings = _session.IsShowingHeadings;\n        var headerHeight = showHeadings ? GetColumnHeaderHeight(_session.Viewport, zoomFactor) : 0;");
    }

    [Fact]
    public void MainWindowPartials_ValidationTraceAndSlicerOverlays_UsePerViewIsShowingHeadings()
    {
        var dataToolsSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.DataTools.cs"));
        var formulaAuditingSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.FormulaAuditing.cs"));
        var slicerTimelineSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.SlicerTimeline.cs"));

        dataToolsSource.Should().NotContain("_session.ActiveSheet.ShowHeadings");
        formulaAuditingSource.Should().NotContain("_session.ActiveSheet.ShowHeadings");
        slicerTimelineSource.Should().NotContain("_session.ActiveSheet.ShowHeadings");

        dataToolsSource.Should().Contain("var showHeadings = _session.IsShowingHeadings;");
        formulaAuditingSource.Should().Contain("var showHeadings = _session.IsShowingHeadings;");
        slicerTimelineSource.Should().Contain("var showHeadings = _session.IsShowingHeadings;");
    }

    /// <summary>
    /// No-regression sibling: the r86-fixed ViewMode call sites and the already-correct
    /// gridlines/headings ribbon-state/toggle call sites (lines 1655-1656, 4337-4338,
    /// 11968-11989, 12339 per the finding evidence) must keep using the per-view
    /// WorkbookSession accessors -- this fix must not have touched or regressed them.
    /// </summary>
    [Fact]
    public void MainWindow_PreviouslyFixedPerViewAccessorCallSites_RemainIntact()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().NotContain("_session.ActiveSheet.ViewMode");
        source.Should().Contain("WorksheetViewModeUiStatePlanner.Build(_session.ViewMode)");

        source.Should().Contain("[\"Gridlines\"] = () => new RibbonCommandState(IsChecked: _session.IsShowingGridlines),");
        source.Should().Contain("[\"Headings\"] = () => new RibbonCommandState(IsChecked: _session.IsShowingHeadings),");
        source.Should().Contain("IsShowingGridlines: _session.IsShowingGridlines,");
        source.Should().Contain("IsShowingHeadings: _session.IsShowingHeadings,");
        source.Should().Contain("var showGridlines = !_session.IsShowingGridlines;");
        source.Should().Contain("var result = _session.SetShowGridlines(showGridlines);");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
