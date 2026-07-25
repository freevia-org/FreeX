using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R87-order-guard-window-state-sweep-2: Window ▸ Split must be per-window in Excel, just like
/// Freeze Panes (fixed for Freeze Panes in R86 via <c>WorkbookSession._viewFrozenRowsOverrides</c>/
/// <c>_viewFrozenColsOverrides</c>). Opening a second window on a workbook (<see cref="WorkbookSession.CreateSiblingView"/>,
/// e.g. View ▸ New Window) and splitting one window must not silently show a split divider in a
/// sibling window, even though both windows share the same underlying <see cref="Sheet"/> instance
/// (and therefore <see cref="Sheet.SplitRow"/>/<see cref="Sheet.SplitColumn"/>) for save/round-trip
/// purposes. Exercised through <see cref="WorkbookSession.HasIndependentSplitPaneTopRight"/>/
/// <see cref="WorkbookSession.HasIndependentSplitPaneBottomLeft"/>, the exact properties the finding cited.
/// </summary>
public sealed class R87_SplitPanesSiblingViewIndependenceTests
{
    /// <summary>
    /// Fails before the R87 fix because <c>HasIndependentSplitPaneTopRight</c>/
    /// <c>HasIndependentSplitPaneBottomLeft</c> read <c>ActiveSheet.SplitColumn</c>/<c>SplitRow</c>
    /// directly, so splitting one window would instantly make a sibling window report a split too,
    /// even though the sibling never asked for one.
    /// </summary>
    [Fact]
    public void R87_SplitPanes_DoesNotLeakAcrossSiblingViews()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        // Both windows must already be open before the split happens -- this is the scenario the
        // finding describes (two windows already on the same sheet, one of them splits).
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        // Establish both windows have already read (and thus cached) the pre-split "no split" state.
        session.HasIndependentSplitPaneTopRight.Should().BeFalse();
        session.HasIndependentSplitPaneBottomLeft.Should().BeFalse();
        sibling.HasIndependentSplitPaneTopRight.Should().BeFalse();
        sibling.HasIndependentSplitPaneBottomLeft.Should().BeFalse();

        var result = session.ExecuteReviewCommand(new SetSplitPanesCommand(sheet.Id, splitRow: 6, splitColumn: 5));

        result.Success.Should().BeTrue();
        sheet.SplitRow.Should().Be(6u);
        sheet.SplitColumn.Should().Be(5u);

        // The window that split: reports an independent split boundary on both axes.
        session.HasIndependentSplitPaneTopRight.Should().BeTrue();
        session.HasIndependentSplitPaneBottomLeft.Should().BeTrue();

        // The sibling window never split anything -- it must still report no split, unaffected by
        // the other window's split.
        sibling.HasIndependentSplitPaneTopRight.Should().BeFalse();
        sibling.HasIndependentSplitPaneBottomLeft.Should().BeFalse();
    }

    /// <summary>
    /// No-regression sibling: per-view independence must not come at the cost of the split state
    /// still reflecting reality for a single-window session, including after Undo restores the
    /// pre-split shared state.
    /// </summary>
    [Fact]
    public void R87_SplitPanes_SingleSessionReflectsSplitAndUndoRestoresIt()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        session.HasIndependentSplitPaneTopRight.Should().BeFalse();
        session.HasIndependentSplitPaneBottomLeft.Should().BeFalse();

        var result = session.ExecuteReviewCommand(new SetSplitPanesCommand(sheet.Id, splitRow: 6, splitColumn: 5));

        result.Success.Should().BeTrue();
        session.HasIndependentSplitPaneTopRight.Should().BeTrue();
        session.HasIndependentSplitPaneBottomLeft.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();
        session.HasIndependentSplitPaneTopRight.Should().BeFalse();
        session.HasIndependentSplitPaneBottomLeft.Should().BeFalse();
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
