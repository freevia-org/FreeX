using FluentAssertions;
using FreeX.App.Presentation.SheetUI;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SheetUI;

/// <summary>
/// R175-render-tab-color-theme-reresolution: <see cref="Sheet.TabColor"/> can carry a theme
/// reference the same way <see cref="CellStyle"/> does for fonts/fills/borders -- a live
/// <see cref="Sheet.TabThemeColor"/> reference meant to be re-resolved against the CURRENT
/// <see cref="WorkbookTheme"/> via <see cref="Sheet.ResolveTabColor"/>, plus a baked
/// <see cref="Sheet.TabColor"/> fallback that is only correct at load time. Before this fix,
/// <see cref="SheetTabListPlanner.Build"/> (the WPF host's sheet-tab-strip source) read
/// <c>sheet.TabColor</c> directly with no theme resolution at all -- <c>Sheet.ResolveTabColor</c>
/// had zero production callers -- so a worksheet tab colored via the ribbon's Theme Colors picker
/// kept its stale baked color forever after a Theme Colors swap.
/// </summary>
public sealed class R175_SheetTabColorThemeReResolutionTests
{
    private static readonly CellColor StaleBakedRed = new(200, 0, 0);
    private static readonly CellColor NewThemeBlue = new(10, 20, 230);
    private static readonly CellColor PlainExplicitPurple = new(120, 10, 140);

    [Fact]
    public void Build_TabColor_ReResolvesAgainstCurrentTheme_NotStaleBakedColor()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        // TabColor's setter clears TabThemeColor (see Sheet.cs), so set it AFTER, mirroring how a
        // file-format loader populates a theme-relative tab color (Sheet.cs's own documented order).
        sheet.TabColor = StaleBakedRed;
        sheet.TabThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2);
        workbook.Theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent2, NewThemeBlue);

        var plan = SheetTabListPlanner.Build(workbook, sheet.Id, new HashSet<SheetId>());

        var entry = plan.Tabs.Single(t => t.Id == sheet.Id);
        entry.TabColor.Should().Be(NewThemeBlue,
            "the sheet's TabThemeColor must be re-resolved against the CURRENT WorkbookTheme (Accent2 -> NewThemeBlue), not the stale baked TabColor");
    }

    [Fact]
    public void Build_PlainExplicitTabColor_WithNoThemeReference_StillRendersExactColor_NoRegression()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TabColor = PlainExplicitPurple;
        // TabThemeColor intentionally left null: a plain, non-themed tab color.
        workbook.Theme = WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent2, NewThemeBlue);

        var plan = SheetTabListPlanner.Build(workbook, sheet.Id, new HashSet<SheetId>());

        var entry = plan.Tabs.Single(t => t.Id == sheet.Id);
        entry.TabColor.Should().Be(PlainExplicitPurple,
            "a tab with no TabThemeColor reference must keep rendering its plain explicit TabColor unchanged, regardless of the active theme");
    }
}
