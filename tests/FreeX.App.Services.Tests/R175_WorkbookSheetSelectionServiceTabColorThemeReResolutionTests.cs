using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R175-render-tab-color-theme-reresolution (Avalonia sibling of the WPF-facing
/// R175_SheetTabColorThemeReResolutionTests fix): the Avalonia shell's worksheet tab strip does
/// NOT go through <see cref="FreeX.App.Presentation.SheetUI.SheetTabListPlanner"/> at all -- it
/// consumes <c>WorkbookSession.SheetTabs</c>, which is built entirely by
/// <see cref="WorkbookSheetSelectionService"/>'s <c>CreateSelection</c>. That method read
/// <c>sheet.TabColor</c> directly with no theme resolution, the exact same defect as the WPF
/// planner, but in an entirely separate source location -- fixing only
/// <see cref="FreeX.App.Presentation.SheetUI.SheetTabListPlanner"/> would have left the Avalonia
/// worksheet tab strip just as broken as before.
/// </summary>
public sealed class R175_WorkbookSheetSelectionServiceTabColorThemeReResolutionTests
{
    private static readonly CellColor StaleBakedRed = new(200, 0, 0);
    private static readonly CellColor NewThemeBlue = new(10, 20, 230);
    private static readonly CellColor PlainExplicitPurple = new(120, 10, 140);

    [Fact]
    public void EnsureActiveSheet_TabColor_ReResolvesAgainstCurrentTheme_NotStaleBakedColor()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TabColor = StaleBakedRed;
        sheet.TabThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2);
        workbook.Theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent2, NewThemeBlue);

        var selection = new WorkbookSheetSelectionService().EnsureActiveSheet(workbook);

        selection.Tabs.Should().ContainSingle()
            .Which.TabColor.Should().Be(NewThemeBlue,
                "the sheet's TabThemeColor must be re-resolved against the CURRENT WorkbookTheme (Accent2 -> NewThemeBlue), not the stale baked TabColor");
    }

    [Fact]
    public void EnsureActiveSheet_PlainExplicitTabColor_WithNoThemeReference_StillRendersExactColor_NoRegression()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TabColor = PlainExplicitPurple;
        // TabThemeColor intentionally left null: a plain, non-themed tab color.
        workbook.Theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent2, NewThemeBlue);

        var selection = new WorkbookSheetSelectionService().EnsureActiveSheet(workbook);

        selection.Tabs.Should().ContainSingle()
            .Which.TabColor.Should().Be(PlainExplicitPurple,
                "a tab with no TabThemeColor reference must keep rendering its plain explicit TabColor unchanged, regardless of the active theme");
    }
}
