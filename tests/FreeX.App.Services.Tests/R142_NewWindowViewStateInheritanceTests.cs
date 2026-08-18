using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R142-freeze-split-views-new-window-view-state-not-inherited: View &gt; New Window
/// (<see cref="WorkbookSession.CreateSiblingView"/>) must open the new sibling as a copy of the
/// INVITING window's own per-window Freeze/Split/Zoom/ViewMode -- matching Excel's own New Window,
/// which duplicates the invoking window's current view -- not whichever shared <see cref="Sheet"/>
/// field a THIRD, unrelated sibling happened to write last. Before this fix,
/// <see cref="WorkbookSession.CreateSiblingView"/> always seeded a new sibling straight from the
/// shared <see cref="Sheet"/> fields (<c>SeedViewSplitAndFrozenOverrides</c>), so a chain of two New
/// Windows (A opens B; B changes its own view, mutating the shared fields; A -- still showing its
/// OWN unchanged view -- opens C) produced a C that matched B's view instead of A's.
/// </summary>
public sealed class R142_NewWindowViewStateInheritanceTests
{
    [Fact]
    public void CreateSiblingView_InheritsInvitingWindowsOwnFrozenPanesNotAThirdWindowsLaterChange()
    {
        // Fails before the fix: windowC.GetEffectiveFrozenRows() would be 10 (windowB's later
        // change, which is what's left on the shared Sheet.FrozenRows field) instead of 3
        // (windowA's own effective value, which is what windowA is actually displaying when it
        // spawns windowC).
        var (windowA, sheet) = CreateSession();
        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowA.SetFreezePanes(frozenRows: 3, frozenCols: 0).Success.Should().BeTrue();
        windowA.GetEffectiveFrozenRows().Should().Be(3);
        sheet.FrozenRows.Should().Be(3);

        windowB.SetFreezePanes(frozenRows: 10, frozenCols: 0).Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(10, "windowB's freeze mutates the shared Sheet field, exactly like R86/R87 established");
        windowA.GetEffectiveFrozenRows().Should().Be(3, "windowA's own per-window override must be untouched by windowB's freeze (pre-existing R86/R87 guarantee)");

        var windowC = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowC.GetEffectiveFrozenRows().Should().Be(3, "windowC was spawned from windowA and must match what windowA is actually showing (3), not windowB's later change (10) or the shared field");
    }

    [Fact]
    public void CreateSiblingView_InheritsInvitingWindowsOwnSplitPanesNotAThirdWindowsLaterChange()
    {
        var (windowA, sheet) = CreateSession();
        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowA.SetSplitPanes(splitRow: 5, splitColumn: null).Success.Should().BeTrue();
        windowA.GetEffectiveSplitRow().Should().Be(5u);

        windowB.SetSplitPanes(splitRow: 12, splitColumn: null).Success.Should().BeTrue();
        sheet.SplitRow.Should().Be(12u);
        windowA.GetEffectiveSplitRow().Should().Be(5u, "windowA's own split override must be untouched by windowB's split change");

        var windowC = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowC.GetEffectiveSplitRow().Should().Be(5u, "windowC must inherit windowA's own split row, not windowB's");
    }

    [Fact]
    public void CreateSiblingView_InheritsInvitingWindowsOwnZoomNotAThirdWindowsLaterChange()
    {
        var (windowA, sheet) = CreateSession();
        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowA.SetZoomPercent(150).Success.Should().BeTrue();
        windowB.SetZoomPercent(60).Success.Should().BeTrue();
        sheet.ZoomPercent.Should().Be(60);

        var windowC = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowC.ZoomPercent.Should().Be(150, "windowC must inherit windowA's own zoom (150), not windowB's later change (60)");
    }

    [Fact]
    public void CreateSiblingView_InheritsInvitingWindowsOwnViewModeNotAThirdWindowsLaterChange()
    {
        var (windowA, sheet) = CreateSession();
        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowA.SetWorksheetViewMode(WorksheetViewMode.PageLayout).Success.Should().BeTrue();
        windowB.SetWorksheetViewMode(WorksheetViewMode.PageBreakPreview).Success.Should().BeTrue();
        sheet.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);

        var windowC = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowC.ViewMode.Should().Be(WorksheetViewMode.PageLayout, "windowC must inherit windowA's own view mode, not windowB's later change");
    }

    /// <summary>
    /// No-regression sibling: a fresh sibling spawned before any window has diverged from the
    /// shared Sheet fields (the common/default case -- most New Windows happen before anyone has
    /// changed Freeze/Split/Zoom/ViewMode at all) must still work exactly as before, seeding from
    /// whatever the shared fields already hold.
    /// </summary>
    [Fact]
    public void CreateSiblingView_NoPriorDivergence_StillSeedsFromSharedSheetDefaults()
    {
        var (windowA, sheet) = CreateSession();
        sheet.FrozenRows.Should().Be(0);
        sheet.ZoomPercent.Should().Be(100);

        var windowB = windowA.CreateSiblingView(viewportHeight: 240, viewportWidth: 320);

        windowB.GetEffectiveFrozenRows().Should().Be(0);
        windowB.ZoomPercent.Should().Be(100);
        windowB.ViewMode.Should().Be(WorksheetViewMode.Normal);
    }

    private static (WorkbookSession Session, Sheet Sheet) CreateSession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        return (session, sheet);
    }
}
