using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R87-render-freeze-split-scroll-5-1: <see cref="WorkbookSession.CreateSiblingView"/> lets two
/// windows share the same underlying <see cref="Sheet"/> (and therefore its <see cref="Sheet.SplitRow"/>/
/// <see cref="Sheet.SplitColumn"/> fields) while each window is supposed to have its own independent
/// Window ▸ Split. R87-order-guard-window-state-sweep-2 already made the ribbon-facing
/// <see cref="WorkbookSession.HasIndependentSplitPaneTopRight"/>/<see cref="WorkbookSession.HasIndependentSplitPaneBottomLeft"/>
/// flags per-view, but the actual rendered grid (<see cref="WorkbookSession.Viewport"/>'s
/// <see cref="ViewportModel.SplitPanes"/>, produced by <c>ViewportService.GetViewport</c>) still read
/// <c>Sheet.SplitRow</c>/<c>Sheet.SplitColumn</c> straight off the shared sheet, so a split applied in
/// one window still rendered a split divider and split-pane bands in every sibling window's grid.
/// </summary>
public sealed class R87_SplitPaneViewportBandsSiblingViewTests
{
    /// <summary>
    /// Fails before the fix because <c>ViewportService.GetViewport</c> built <c>SplitPanes</c> from
    /// <c>sheet.SplitRow</c>/<c>sheet.SplitColumn</c> directly instead of the per-view override
    /// <see cref="WorkbookSession"/> now threads through <c>ViewportRequest.SplitOverride</c>, so the
    /// sibling window's own rendered viewport would show a split it never asked for.
    /// </summary>
    [Fact]
    public void R87_SplitPaneViewport_DoesNotRenderSplitBandsInSiblingView()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        var result = session.ExecuteReviewCommand(new SetSplitPanesCommand(sheet.Id, splitRow: 6, splitColumn: 5));
        result.Success.Should().BeTrue();
        sheet.SplitRow.Should().Be(6u);
        sheet.SplitColumn.Should().Be(5u);

        // Force both viewports to actually rebuild -- SetViewportOrigin is a no-op (and skips the
        // RefreshViewport it would otherwise trigger) when the requested origin already matches the
        // view's current one, so (1, 1) would silently do nothing for either window here.
        session.SetViewportOrigin(5, 5);
        sibling.SetViewportOrigin(5, 5);

        // The window that split: its rendered viewport carries the split-pane bands.
        session.Viewport.SplitPanes.Should().NotBeNull();
        session.Viewport.SplitPanes!.Row.Should().Be(6u);
        session.Viewport.SplitPanes!.Column.Should().Be(5u);
        session.Viewport.SplitPanes!.TopRows.Should().NotBeEmpty();
        session.Viewport.SplitPanes!.LeftColumns.Should().NotBeEmpty();

        // The sibling window never split anything -- its rendered viewport must not show a split,
        // even though the shared Sheet.SplitRow/SplitColumn fields are now set to 6/5.
        sibling.Viewport.SplitPanes.Should().BeNull();
    }

    /// <summary>
    /// No-regression sibling: per-view independence must not come at the cost of a single-window
    /// session's own rendered viewport still reflecting its split (and undo still clearing it).
    /// </summary>
    [Fact]
    public void R87_SplitPaneViewport_SingleSessionRendersSplitAndUndoClearsIt()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.ExecuteReviewCommand(new SetSplitPanesCommand(sheet.Id, splitRow: 6, splitColumn: 5));
        result.Success.Should().BeTrue();
        session.SetViewportOrigin(5, 5);

        session.Viewport.SplitPanes.Should().NotBeNull();
        session.Viewport.SplitPanes!.Row.Should().Be(6u);
        session.Viewport.SplitPanes!.Column.Should().Be(5u);

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.SetViewportOrigin(1, 1);
        session.Viewport.SplitPanes.Should().BeNull();
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
