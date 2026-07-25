using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R86-order-guard-invented-sweep-3: Freeze Panes must be per-window in Excel, just like Zoom (fixed
/// for Zoom in R85 via <c>WorkbookSession._viewZoomOverrides</c>). Opening a second window on a
/// workbook (<see cref="WorkbookSession.CreateSiblingView"/>, e.g. View ▸ New Window) and freezing
/// panes in one window must not silently change what a sibling window scrolls/renders around, even
/// though both windows share the same underlying <see cref="Sheet"/> instance (and therefore
/// <see cref="Sheet.FrozenRows"/>/<see cref="Sheet.FrozenCols"/>) for save/round-trip purposes. Exercised
/// through <see cref="WorkbookSession.SetViewportOrigin"/>, which clamps the scroll origin to just past
/// the frozen boundary via the internal scroll-range helpers the finding cited
/// (<c>TryGetScrollableRowRange</c>/<c>TryGetScrollableColumnRange</c>/<c>IsFrozenRow</c>/
/// <c>IsFrozenColumn</c>/<c>GetScrollableRowStart</c>/<c>GetScrollableColumnStart</c>).
/// </summary>
public sealed class R86_FreezePanesSiblingViewIndependenceTests
{
    /// <summary>
    /// Fails before the R86 fix because the scroll-range helpers read <c>ActiveSheet.FrozenRows</c>/
    /// <c>FrozenCols</c> directly, so freezing panes in one window would instantly clamp a sibling
    /// window's scrollable region too, even though the sibling never froze anything and may be
    /// scrolled to view a completely different part of the sheet.
    /// </summary>
    [Fact]
    public void R86_FreezePanes_DoesNotLeakScrollClampAcrossSiblingViews()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        // Both windows must already be open before the freeze happens -- this is the scenario the
        // finding describes (two windows already on the same sheet, one of them freezes).
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        var c3 = new CellAddress(sheet.Id, 3, 3);
        session.SelectCell(c3);
        var result = session.FreezePanesAtActiveCell();

        result.Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(2);
        sheet.FrozenCols.Should().Be(2);

        // The window that froze: scrolling toward the top-left clamps to just past the frozen
        // boundary.
        session.SetViewportOrigin(1, 1);
        session.ViewportOrigin.Should().Be((3u, 3u));

        // The sibling window never froze anything -- its scrollable region must still start at row/col
        // 1, unaffected by the other window's freeze.
        sibling.SetViewportOrigin(1, 1);
        sibling.ViewportOrigin.Should().Be((1u, 1u));
    }

    /// <summary>
    /// No-regression sibling: per-view independence must not come at the cost of
    /// <see cref="WorkbookSession.FreezePanesAtActiveCell"/> still clamping the scroll origin (and
    /// undo/redo still working) normally for a single-window session.
    /// </summary>
    [Fact]
    public void R86_FreezePanes_SingleSessionScrollClampReflectsFrozenBoundaryAndUndoRestoresIt()
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
        session.ViewportOrigin.Should().Be((3u, 3u));

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
        session.SetViewportOrigin(1, 1);
        session.ViewportOrigin.Should().Be((1u, 1u));
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
