using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R163-DV-F1: a List data-validation rule sourced from a range of date-formatted cells rendered
/// its in-cell dropdown items as raw OADate serial numbers ("45293") instead of the dates the
/// user authored ("2024-01-02"), because <c>DataValidationService.GetListItems</c> (the dropdown's
/// real production entry point, consumed verbatim as ItemsSource by both the WPF and Avalonia
/// shells) shared the exact same value-rendering helper (<c>ToValidationText</c>) that value
/// acceptance/rejection matching needs to keep using the raw serial for (so a date compares equal
/// regardless of locale). The fix adds a display-only rendering path
/// (<c>ResolveListValues(..., forDisplay: true)</c>, wired only into <c>GetListItems</c>) that
/// formats a <see cref="DateTimeValue"/> the same way the grid itself would ("yyyy-MM-dd"),
/// while leaving the raw-serial <c>ToValidationText</c> path <see cref="DataValidationService.Validate"/>
/// itself, and every other List-source non-date scalar kind (text/number/bool), untouched.
/// </summary>
public sealed class R163_DataValidationDateListDisplayTests
{
    private static (Workbook workbook, Sheet sheet, CellAddress target) BuildSheetWithDateSourceAndTarget()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(45293)); // A1 = 2024-01-02
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(45294)); // A2 = 2024-01-03
        var target = new CellAddress(sheet.Id, 1, 2); // B1
        return (workbook, sheet, target);
    }

    [Fact]
    public void GetListItems_DateSourcedRange_ReturnsFormattedDatesNotRawSerials()
    {
        var (workbook, sheet, target) = BuildSheetWithDateSourceAndTarget();
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$2",
            AppliesTo = new GridRange(target, target),
            ShowDropdown = true,
        };

        var items = DataValidationService.GetListItems(dv, sheet, target, workbook);

        items.Should().Equal(
            new[] { "2024-01-02", "2024-01-03" },
            "the in-cell dropdown must show the dates the user authored, not their raw OADate " +
            "serials (45293/45294), matching how the grid itself would display these cells");
    }

    [Fact]
    public void GetListItems_DateSourcedNamedRange_ReturnsFormattedDatesNotRawSerials()
    {
        var (workbook, sheet, target) = BuildSheetWithDateSourceAndTarget();
        workbook.DefineNamedRange(
            "DateSource",
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            null,
            sheet.Id);
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=DateSource",
            AppliesTo = new GridRange(target, target),
            ShowDropdown = true,
        };

        var items = DataValidationService.GetListItems(dv, sheet, target, workbook);

        items.Should().Equal("2024-01-02", "2024-01-03");
    }

    // ── Sibling non-regression: the raw-serial matching path used for value acceptance/rejection
    // must stay exactly as it was -- a typed/pasted raw serial number must still validate against
    // a date-sourced list, and a date value must still validate, regardless of the display fix
    // above touching only GetListItems.
    [Fact]
    public void Validate_DateSourcedRange_StillMatchesByRawSerialNotDisplayText()
    {
        var (workbook, sheet, target) = BuildSheetWithDateSourceAndTarget();
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$2",
            AppliesTo = new GridRange(target, target),
            ErrorMessage = "No match",
        };

        // The cell's own current value, a DateTimeValue equal to one of the source cells, must
        // still validate -- exactly as it did before this fix.
        DataValidationService.Validate(dv, new DateTimeValue(45293), sheet, target, workbook)
            .Should().BeNull();

        // A value that is not in the source range must still be rejected.
        DataValidationService.Validate(dv, new DateTimeValue(45295), sheet, target, workbook)
            .Should().Be("No match");
    }

    // ── Sibling non-regression: a text/number list source (the overwhelmingly common case) must
    // render identically before and after this fix -- the display-only change only affects
    // DateTimeValue rendering.
    [Fact]
    public void GetListItems_TextAndNumberSourcedRange_StillRendersPlainInvariantText()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Red"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1.5));
        var target = new CellAddress(sheet.Id, 1, 2);
        var dv = new DataValidation
        {
            Type = DvType.List,
            Formula1 = "=$A$1:$A$2",
            AppliesTo = new GridRange(target, target),
            ShowDropdown = true,
        };

        DataValidationService.GetListItems(dv, sheet, target, workbook)
            .Should().Equal("Red", "1.5");
    }
}
