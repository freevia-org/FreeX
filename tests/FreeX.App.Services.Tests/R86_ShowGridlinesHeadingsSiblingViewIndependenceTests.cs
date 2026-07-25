using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R86-order-guard-invented-sweep-1: Show Gridlines and Show Headings must be per-window in Excel,
/// just like Zoom (fixed for Zoom in R85 via <c>WorkbookSession._viewZoomOverrides</c>) and View Mode
/// (R86 sweep-5, <c>_viewModeOverrides</c>). Opening a second window on a workbook
/// (<see cref="WorkbookSession.CreateSiblingView"/>, e.g. View ▸ New Window) and toggling one window's
/// gridlines/headings must not silently flip what a sibling window reports/renders, even though both
/// windows share the same underlying <see cref="Sheet"/> instance (and therefore
/// <see cref="Sheet.ShowGridlines"/>/<see cref="Sheet.ShowHeadings"/>) for save/round-trip purposes.
/// </summary>
public sealed class R86_ShowGridlinesHeadingsSiblingViewIndependenceTests
{
    /// <summary>
    /// Fails before the R86 fix because <c>WorkbookSession.IsShowingGridlines</c>/
    /// <c>IsShowingHeadings</c> read <c>ActiveSheet.ShowGridlines</c>/<c>ShowHeadings</c> directly, so a
    /// sibling window turning gridlines/headings off would instantly leak into every other open window
    /// on the same sheet.
    /// </summary>
    [Fact]
    public void R86_SetShowGridlinesAndHeadings_DoesNotLeakAcrossSiblingViews()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);
        sibling.IsShowingGridlines.Should().BeTrue();
        sibling.IsShowingHeadings.Should().BeTrue();
        session.IsShowingGridlines.Should().BeTrue();
        session.IsShowingHeadings.Should().BeTrue();

        var gridlinesResult = session.SetShowGridlines(false);

        gridlinesResult.Success.Should().BeTrue();
        session.IsShowingGridlines.Should().BeFalse();
        sibling.IsShowingGridlines.Should().BeTrue();

        // And the reverse direction: the sibling turning headings off independently must not pull the
        // original window's headings (or gridlines) along with it either.
        var siblingHeadingsResult = sibling.SetShowHeadings(false);

        siblingHeadingsResult.Success.Should().BeTrue();
        sibling.IsShowingHeadings.Should().BeFalse();
        sibling.IsShowingGridlines.Should().BeTrue();
        session.IsShowingHeadings.Should().BeTrue();
        session.IsShowingGridlines.Should().BeFalse();
    }

    /// <summary>
    /// No-regression sibling: per-view independence must not come at the cost of
    /// <see cref="WorkbookSession.SetShowGridlines"/>/<see cref="WorkbookSession.SetShowHeadings"/>
    /// still applying (and undo/redo still working) normally for a single-window session.
    /// </summary>
    [Fact]
    public void R86_SetShowGridlinesAndHeadings_SingleSessionAppliesAndUndoes()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var gridlinesResult = session.SetShowGridlines(false);

        gridlinesResult.Success.Should().BeTrue();
        session.IsShowingGridlines.Should().BeFalse();
        session.ActiveSheet.ShowGridlines.Should().BeFalse();
        session.CanUndo.Should().BeTrue();

        var headingsResult = session.SetShowHeadings(false);

        headingsResult.Success.Should().BeTrue();
        session.IsShowingHeadings.Should().BeFalse();
        session.ActiveSheet.ShowHeadings.Should().BeFalse();

        var undoHeadings = session.UndoLastEdit();
        undoHeadings.Success.Should().BeTrue();
        session.IsShowingHeadings.Should().BeTrue();
        session.IsShowingGridlines.Should().BeFalse();

        var undoGridlines = session.UndoLastEdit();
        undoGridlines.Success.Should().BeTrue();
        session.IsShowingGridlines.Should().BeTrue();
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
