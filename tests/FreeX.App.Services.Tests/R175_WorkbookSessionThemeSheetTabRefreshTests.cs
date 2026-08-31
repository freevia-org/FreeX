using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R175-render-tab-color-theme-reresolution: <see cref="SetWorkbookThemeCommand"/> mutates
/// <c>Workbook.Theme</c> with no <c>AffectedCells</c> at all, so the generic
/// <c>WorkbookSession.ExecuteReviewCommand</c> path's own sheet-tab refresh
/// (<c>ApplySuccessfulEditResult</c>, which only re-derives <c>SheetTabs</c> when the edit's
/// affected cell happens to land on a DIFFERENT sheet) never runs for it. <c>SheetTabs</c> is a
/// cached snapshot (<c>WorkbookSheetSelectionService.CreateSelection</c>, fixed in the sibling
/// R175_WorkbookSheetSelectionServiceTabColorThemeReResolutionTests to resolve TabThemeColor
/// against the CURRENT theme) -- so even with that reader fixed, a theme swap alone would leave
/// every tab's resolved color stale in the cached list until some UNRELATED action (switching
/// sheets, renaming a sheet, setting a tab color explicitly, ...) happened to refresh it. Both
/// shells now call the newly-public <see cref="WorkbookSession.RefreshSheetTabsForActiveSheet"/>
/// immediately after applying a theme command (see MainWindow.PageLayout.cs's
/// ApplyWorkbookTheme / MainWindow.Themes.cs's ApplyDerivedTheme+ShowThemesGalleryAsync) -- this
/// test exercises that exact production sequence at the session level.
/// </summary>
public sealed class R175_WorkbookSessionThemeSheetTabRefreshTests
{
    private static readonly CellColor StaleBakedRed = new(200, 0, 0);
    private static readonly CellColor NewThemeBlue = new(10, 20, 230);

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    [Fact]
    public void ExecuteReviewCommand_ThemeSwapAlone_DoesNotRefreshCachedSheetTabs_KnownGap()
    {
        // Documents the gap this fix works around: the generic executor alone is not enough.
        // Note: TabThemeColor always wins over the baked TabColor in ResolveTabColor (mirrors
        // CellStyle's Font/FillThemeColor precedence), so SheetTabs already resolves against the
        // theme in effect at SESSION CONSTRUCTION time (Office's default Accent2), not the raw
        // StaleBakedRed -- the gap this documents is that a LATER theme swap doesn't re-resolve it.
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TabColor = StaleBakedRed;
        sheet.TabThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2);
        var session = CreateSession(workbook);
        var colorBeforeThemeSwap = session.SheetTabs.Single().TabColor;

        var result = session.ExecuteReviewCommand(
            new SetWorkbookThemeCommand(workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent2, NewThemeBlue)));

        result.Success.Should().BeTrue();
        session.SheetTabs.Should().ContainSingle()
            .Which.TabColor.Should().Be(colorBeforeThemeSwap,
                "ExecuteReviewCommand alone does not refresh SheetTabs for a theme command with no AffectedCells -- this is the gap RefreshSheetTabsForActiveSheet must be called explicitly to close")
            .And.NotBe(NewThemeBlue,
                "without the explicit refresh, the new theme's Accent2 color must NOT yet be reflected");
    }

    [Fact]
    public void ExecuteReviewCommand_ThenExplicitRefresh_UpdatesCachedSheetTabColor_MatchingProductionSequence()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TabColor = StaleBakedRed;
        sheet.TabThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2);
        var session = CreateSession(workbook);

        var result = session.ExecuteReviewCommand(
            new SetWorkbookThemeCommand(workbook.Theme.WithColor(WorkbookThemeColorSlot.Accent2, NewThemeBlue)));
        result.Success.Should().BeTrue();

        // Matches exactly what both shells' theme-apply methods now do immediately afterward.
        session.RefreshSheetTabsForActiveSheet();

        session.SheetTabs.Should().ContainSingle()
            .Which.TabColor.Should().Be(NewThemeBlue,
                "after the shell's explicit RefreshSheetTabsForActiveSheet call, the cached SheetTabs entry must reflect the new theme's resolved color");
    }
}
