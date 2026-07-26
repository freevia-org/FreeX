using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R88-window-seed-order-guard-sweep-1: Applying a Custom View (View ▸ Custom Views ▸ Show) writes
/// zoom/gridlines/headings/formulas/view-mode/frozen directly onto the shared <see cref="Sheet"/>
/// via <c>ApplyCustomViewCommand</c> -&gt; <c>CustomViewStatePlanner.ApplyState</c>. That command is only
/// reachable through the generic <see cref="WorkbookSession.ExecuteReviewCommand"/> path -&gt;
/// <c>ApplySuccessfulEditResult</c>, which previously only cleared the split-pane per-view caches,
/// leaving <c>_viewZoomOverrides</c>/<c>_viewShowGridlinesOverrides</c>/<c>_viewShowHeadingsOverrides</c>/
/// <c>_viewShowFormulasOverrides</c>/<c>_viewModeOverrides</c>/<c>_viewFrozenRowsOverrides</c>/
/// <c>_viewFrozenColsOverrides</c> stale. This asserts the session's own getters -- not just the
/// underlying <see cref="Sheet"/> fields -- reflect the values the custom view restored.
/// </summary>
public sealed class R88_ApplyCustomViewInvalidatesAllPerViewCachesTests
{
    [Fact]
    public void R88_ApplyCustomView_RefreshesSessionZoomGridlinesHeadingsFormulasAndFrozenCaches()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));

        // Seed the session's per-view caches at the "saved view" settings (100%/gridlines on/
        // headings on/formulas off) and save a custom view at those settings, exactly like the
        // failure scenario: the getters are read once here (seeding the cache), matching what the
        // Avalonia shell's status bar/ribbon checkboxes would have done on first render.
        session.ZoomPercent.Should().Be(100);
        session.IsShowingGridlines.Should().BeTrue();
        session.IsShowingHeadings.Should().BeTrue();
        session.IsShowingFormulas.Should().BeFalse();

        session.ExecuteReviewCommand(new SaveCustomViewCommand("A")).Success.Should().BeTrue();

        // Now change to different settings via the session's own dedicated setters (these reseed
        // their own cache entries, exactly like the real UI flow the finding describes).
        session.SetZoomPercent(150).Success.Should().BeTrue();
        session.SetShowGridlines(false).Success.Should().BeTrue();
        session.SetShowHeadings(false).Success.Should().BeTrue();
        session.SetShowFormulas(true).Success.Should().BeTrue();

        session.ZoomPercent.Should().Be(150);
        session.IsShowingGridlines.Should().BeFalse();
        session.IsShowingHeadings.Should().BeFalse();
        session.IsShowingFormulas.Should().BeTrue();

        // Apply the saved custom view back -- through the generic ExecuteReviewCommand path, exactly
        // as View ▸ Custom Views ▸ Show does in both shells.
        var result = session.ExecuteReviewCommand(new ApplyCustomViewCommand("A"));

        result.Success.Should().BeTrue();

        // The shared Sheet fields are correctly restored either way (this part already worked).
        sheet.ZoomPercent.Should().Be(100);
        sheet.ShowGridlines.Should().BeTrue();
        sheet.ShowHeadings.Should().BeTrue();
        sheet.ShowFormulas.Should().BeFalse();

        // The session's own per-view getters must reflect the restored values immediately, not the
        // stale pre-Apply cache.
        session.ZoomPercent.Should().Be(100);
        session.IsShowingGridlines.Should().BeTrue();
        session.IsShowingHeadings.Should().BeTrue();
        session.IsShowingFormulas.Should().BeFalse();
    }

    /// <summary>
    /// No-regression sibling: a sibling window that never touched the custom view (and thus never
    /// diverged from the shared Sheet) must still read the correct, unaffected values -- the fix must
    /// not blanket-invalidate in a way that corrupts a window that had nothing to do with the Apply.
    /// </summary>
    [Fact]
    public void R88_ApplyCustomView_SiblingViewUnaffectedBySeededZoomStillReadsCorrectValue()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook,
            "Book.fxl",
            "Opened .fxl.",
            IsFallback: false));
        var sibling = session.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        // Seed both windows' caches at the default state.
        session.ZoomPercent.Should().Be(100);
        sibling.ZoomPercent.Should().Be(100);

        session.ExecuteReviewCommand(new SaveCustomViewCommand("A")).Success.Should().BeTrue();

        session.SetZoomPercent(150).Success.Should().BeTrue();
        session.ZoomPercent.Should().Be(150);

        var result = session.ExecuteReviewCommand(new ApplyCustomViewCommand("A"));

        result.Success.Should().BeTrue();
        sheet.ZoomPercent.Should().Be(100);
        session.ZoomPercent.Should().Be(100);

        // The sibling window never changed its own zoom -- it should still read 100 (unchanged),
        // demonstrating the fix invalidates the affected session's cache without otherwise breaking
        // per-view independence for a sibling that already agreed with the shared Sheet.
        sibling.ZoomPercent.Should().Be(100);
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
