using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R86-order-guard-invented-sweep-5: worksheet View Mode (Normal/Page Layout/Page Break Preview)
/// must be per-window in Excel, just like Zoom (fixed for Zoom in R85 via
/// <c>WorkbookSession._viewZoomOverrides</c>). Opening a second window on a workbook
/// (<see cref="WorkbookSession.CreateSiblingView"/>, e.g. View ▸ New Window) and switching one
/// window's view mode must not silently flip what a sibling window reports/renders, even though
/// both windows share the same underlying <see cref="Sheet"/> instance (and therefore
/// <see cref="Sheet.ViewMode"/>) for save/round-trip purposes.
/// </summary>
public sealed class R86_ViewModeSiblingViewIndependenceTests
{
    /// <summary>
    /// Fails before the R86 fix because <c>WorkbookSession.ViewMode</c> did not exist and every
    /// caller (Avalonia shell status bar / page-break overlay / view-mode toggle buttons) read
    /// <c>ActiveSheet.ViewMode</c> directly -- so a sibling window switching to Page Layout would
    /// instantly leak into every other open window on the same sheet.
    /// </summary>
    [Fact]
    public void R86_SetWorksheetViewMode_DoesNotLeakAcrossSiblingViews()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);
        sibling.ViewMode.Should().Be(WorksheetViewMode.Normal);
        session.ViewMode.Should().Be(WorksheetViewMode.Normal);

        var result = session.SetWorksheetViewMode(WorksheetViewMode.PageLayout);

        result.Success.Should().BeTrue();
        session.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        sibling.ViewMode.Should().Be(WorksheetViewMode.Normal);

        // And the reverse direction: the sibling switching independently must not pull the
        // original view along with it either.
        var siblingResult = sibling.SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);

        siblingResult.Success.Should().BeTrue();
        sibling.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
        session.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
    }

    /// <summary>
    /// No-regression sibling: per-view independence must not come at the cost of
    /// <see cref="WorkbookSession.SetWorksheetViewMode"/> still applying (and undo/redo still
    /// working) normally for a single-window session -- exercising the ordinary case unaffected by
    /// the sibling-view cache.
    /// </summary>
    [Fact]
    public void R86_SetWorksheetViewMode_SingleSessionAppliesAndUndoes()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview);

        result.Success.Should().BeTrue();
        session.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
        session.ActiveSheet.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.ViewMode.Should().Be(WorksheetViewMode.Normal);
        session.ActiveSheet.ViewMode.Should().Be(WorksheetViewMode.Normal);
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
