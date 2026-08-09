using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

/// <summary>
/// R132-commands-autofilter-date-serial-guard-1 [HIGH sibling]: a date-formatted cell holding a
/// <see cref="NumberValue"/> outside <see cref="DateTime"/>'s representable range (e.g. a huge or
/// negative number typed/pasted into a date-formatted cell, or a runaway date-arithmetic formula
/// result) made <see cref="AutoFilterChecklistPlanner.CreateItems(Workbook?, Sheet, AutoFilterDropdownPlan, string)"/>
/// call <c>new DateTimeValue(number.Value).ToDateTime()</c> unguarded while building the chronological
/// sort override for that row -- crashing the whole AutoFilter dropdown checklist, not just
/// misordering that one entry. Fixed via <see cref="DateTimeValue.TryToDateTime"/>: an unconvertible
/// serial is simply left out of the date-sort override and falls back to the checklist's ordinary
/// numeric-text sort bucket.
/// </summary>
public sealed class R132_AutoFilterChecklistDateSerialGuardTests
{
    [Fact]
    public void CreateItems_DateFormattedColumn_WithOutOfRangeSerial_DoesNotCrash_AndNormalDateStillSortsChronologically()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Due Date"));

        // Row 2: an ordinary formula-computed date (NumberValue holding a valid OADate serial) --
        // sibling no-regression check that this still sorts into the chronological date bucket.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(new DateTime(2026, 5, 1).ToOADate()));

        // Row 3: a date-formatted cell whose stored number is nowhere near a representable
        // DateTime (e.g. runaway date arithmetic, or a value loaded from a file) -- this is what
        // used to throw ArgumentOutOfRangeException and abort building the WHOLE checklist.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(1e18));

        var dateStyle = CellStyle.Default.Clone();
        dateStyle.NumberFormat = "m/d/yyyy";
        var dateStyleId = workbook.RegisterStyle(dateStyle);
        sheet.GetCell(2, 1)!.StyleId = dateStyleId;
        sheet.GetCell(3, 1)!.StyleId = dateStyleId;

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            FilterColumnOffset: 0);

        var act = () => AutoFilterChecklistPlanner.CreateItems(workbook, sheet, plan, "(Blanks)");

        var items = act.Should().NotThrow("an unconvertible date serial must not crash opening the filter dropdown").Which;

        items.Should().HaveCount(2);
        items.Select(item => item.DisplayText).Should().Contain("5/1/2026");
    }
}
