using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R87-order-guard-window-state-sweep-3: R86 made <see cref="WorkbookSession.GetEffectiveFrozenRows"/>/
/// <see cref="WorkbookSession.GetEffectiveFrozenCols"/> per-view (so the ribbon's Freeze Panes checkbox
/// and the scroll-clamp helpers stopped leaking across <see cref="WorkbookSession.CreateSiblingView"/>
/// windows), but <c>WorkbookSession.BuildViewport()</c> never threaded that per-view value into
/// <c>ViewportRequest</c> -- so <c>ViewportService.GetViewport</c> kept reading the shared
/// <see cref="Sheet.FrozenRows"/>/<see cref="Sheet.FrozenCols"/> fields directly for both the pinned-row/
/// column metrics AND the <see cref="FrozenPaneState"/> divider the grid actually draws. Freezing panes
/// in one window still visually pinned rows/columns in a sibling window's rendered grid even though
/// that sibling never froze anything.
/// </summary>
public sealed class R87_FrozenPaneViewportMetricsSiblingViewTests
{
    /// <summary>
    /// Fails before the fix because <c>ViewportService.GetViewport</c>/<c>BuildFrozenAwareRowMetrics</c>/
    /// <c>BuildFrozenAwareColMetrics</c> read <c>sheet.FrozenRows</c>/<c>sheet.FrozenCols</c> straight off
    /// the shared <see cref="Sheet"/> instead of the per-view override now threaded through
    /// <c>ViewportRequest.FrozenRowsOverride</c>/<c>FrozenColsOverride</c>, so the sibling window's own
    /// rendered viewport would show pinned rows/columns and a frozen-pane divider it never asked for.
    /// </summary>
    [Fact]
    public void R87_FrozenPaneViewport_DoesNotPinRowsInSiblingView()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        var c3 = new CellAddress(sheet.Id, 3, 3);
        session.SelectCell(c3);
        var result = session.FreezePanesAtActiveCell();
        result.Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenCols.Should().Be(2);

        // Force both viewports to actually rebuild -- SetViewportOrigin is a no-op (and skips the
        // RefreshViewport it would otherwise trigger) when the requested origin already matches the
        // view's current one, so (1, 1) would silently do nothing for the sibling (which never froze
        // anything, so its scrollable start is still row/col 1).
        session.SetViewportOrigin(5, 5);
        sibling.SetViewportOrigin(5, 5);

        // The window that froze: its rendered viewport pins rows/cols 1-2 and carries the divider.
        session.Viewport.FrozenPanes.Should().NotBeNull();
        session.Viewport.FrozenPanes!.Rows.Should().Be(2u);
        session.Viewport.FrozenPanes!.Cols.Should().Be(2u);
        session.Viewport.RowMetrics[0].Row.Should().Be(1u);
        session.Viewport.RowMetrics.Should().Contain(m => m.Row == 2u);

        // The sibling window never froze anything -- its rendered viewport must show no frozen
        // divider at all, even though the shared Sheet.FrozenRows/FrozenCols fields are now 2/2.
        sibling.Viewport.FrozenPanes.Should().BeNull();
    }

    /// <summary>
    /// No-regression sibling: per-view independence must not come at the cost of a single-window
    /// session's own rendered viewport still reflecting its freeze (and undo still clearing it).
    /// </summary>
    [Fact]
    public void R87_FrozenPaneViewport_SingleSessionPinsRowsAndUndoClearsIt()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var c3 = new CellAddress(sheet.Id, 3, 3);
        session.SelectCell(c3);

        var result = session.FreezePanesAtActiveCell();
        result.Success.Should().BeTrue();
        session.SetViewportOrigin(1, 1);

        session.Viewport.FrozenPanes.Should().NotBeNull();
        session.Viewport.FrozenPanes!.Rows.Should().Be(2u);
        session.Viewport.FrozenPanes!.Cols.Should().Be(2u);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.SetViewportOrigin(1, 1);
        session.Viewport.FrozenPanes.Should().BeNull();
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
