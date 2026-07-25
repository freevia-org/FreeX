using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R86-order-guard-invented-sweep-2: Show Formulas (Ctrl+`) must be per-window in Excel, just like Zoom
/// (fixed for Zoom in R85 via <c>WorkbookSession._viewZoomOverrides</c>). Opening a second window on a
/// workbook (<see cref="WorkbookSession.CreateSiblingView"/>, e.g. View ▸ New Window) and pressing
/// Ctrl+` in one window must not silently flip what a sibling window reports/renders, even though both
/// windows share the same underlying <see cref="Sheet"/> instance (and therefore
/// <see cref="Sheet.ShowFormulas"/>) for save/round-trip purposes.
/// </summary>
public sealed class R86_ShowFormulasSiblingViewIndependenceTests
{
    /// <summary>
    /// Fails before the R86 fix because <c>WorkbookSession.IsShowingFormulas</c> read
    /// <c>ActiveSheet.ShowFormulas</c> directly, so a sibling window turning Show Formulas on would
    /// instantly leak into every other open window on the same sheet.
    /// </summary>
    [Fact]
    public void R86_SetShowFormulas_DoesNotLeakAcrossSiblingViews()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);
        sibling.IsShowingFormulas.Should().BeFalse();
        session.IsShowingFormulas.Should().BeFalse();

        var result = session.SetShowFormulas(true);

        result.Success.Should().BeTrue();
        session.IsShowingFormulas.Should().BeTrue();
        sibling.IsShowingFormulas.Should().BeFalse();

        // The sibling's own (still-false) cached state must govern its no-op decision -- it must not
        // be misled by the now-shared-true field into thinking "set to false" is a real change (which
        // would silently apply a command that flips the session's change back out from under it).
        var siblingNoOpResult = sibling.SetShowFormulas(false);

        siblingNoOpResult.Success.Should().BeTrue();
        sibling.IsShowingFormulas.Should().BeFalse();
        session.IsShowingFormulas.Should().BeTrue();
    }

    /// <summary>
    /// No-regression sibling: per-view independence must not come at the cost of
    /// <see cref="WorkbookSession.SetShowFormulas"/> still applying (and undo/redo still working)
    /// normally for a single-window session.
    /// </summary>
    [Fact]
    public void R86_SetShowFormulas_SingleSessionAppliesAndUndoes()
    {
        var workbook = CreateWorkbook();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        var result = session.SetShowFormulas(true);

        result.Success.Should().BeTrue();
        session.IsShowingFormulas.Should().BeTrue();
        session.ActiveSheet.ShowFormulas.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();

        var undo = session.UndoLastEdit();

        undo.Success.Should().BeTrue();
        session.IsShowingFormulas.Should().BeFalse();
        session.ActiveSheet.ShowFormulas.Should().BeFalse();
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
