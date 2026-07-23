using FluentAssertions;

using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

/// <summary>
/// R76-render-autofilter-dropdown-4-3: the AutoFilter checklist must render each row's DISPLAY
/// text through the cell's own number format (matching what the grid shows), while the value used
/// for filter matching stays the raw invariant text FilterCommand compares against.
/// </summary>
public sealed class R76_AutoFilterChecklistDisplayFormatTests
{
    [Fact]
    public void CreateItems_CurrencyColumn_ShowsFormattedDisplayText_ButKeepsRawInvariantValue()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1500));

        var currencyStyle = CellStyle.Default.Clone();
        currencyStyle.NumberFormat = "$#,##0.00";
        sheet.GetCell(2, 1)!.StyleId = workbook.RegisterStyle(currencyStyle);

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            FilterColumnOffset: 0);

        var items = AutoFilterChecklistPlanner.CreateItems(workbook, sheet, plan, "(Blanks)");

        items.Should().ContainSingle();
        items[0].DisplayText.Should().Be("$1,500.00");
        // The value used for filter matching stays the raw invariant text -- unchanged so
        // filtering still selects the right rows via FilterValueFormatter.ToText.
        items[0].Value.Should().Be("1500");
    }

    [Fact]
    public void CreateItems_DateColumn_ShowsCellsOwnDateFormat_NotIso()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), DateTimeValue.FromDateTime(new DateTime(2024, 3, 15)));

        var dateStyle = CellStyle.Default.Clone();
        dateStyle.NumberFormat = "m/d/yyyy";
        sheet.GetCell(2, 1)!.StyleId = workbook.RegisterStyle(dateStyle);

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            FilterColumnOffset: 0);

        var items = AutoFilterChecklistPlanner.CreateItems(workbook, sheet, plan, "(Blanks)");

        items.Should().ContainSingle();
        items[0].DisplayText.Should().Be("3/15/2024");
        // The raw ISO filter-match text (what FilterCommand actually compares against) is
        // untouched by the display format.
        items[0].Value.Should().Be("2024-03-15");
    }

    [Fact]
    public void CreateItems_GeneralFormatColumn_IsUnaffectedByDisplayFormatting()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1500));
        // No explicit style registered -- cell keeps the default "General" number format.

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            FilterColumnOffset: 0);

        var items = AutoFilterChecklistPlanner.CreateItems(workbook, sheet, plan, "(Blanks)");

        items.Should().ContainSingle();
        items[0].DisplayText.Should().Be("1500");
        items[0].Value.Should().Be("1500");
    }

    [Fact]
    public void CreateItems_WithoutWorkbook_FallsBackToRawInvariantDisplayText()
    {
        // The legacy no-workbook overload (still used where no Workbook is available) must keep
        // its prior behavior exactly -- raw invariant text for both DisplayText and Value.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1500));

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            FilterColumnOffset: 0);

        var items = AutoFilterChecklistPlanner.CreateItems(sheet, plan, "(Blanks)");

        items.Should().ContainSingle();
        items[0].DisplayText.Should().Be("1500");
        items[0].Value.Should().Be("1500");
    }

    [Fact]
    public void CreateMenuPlan_CurrencyColumn_ChecklistEntryShowsFormattedHeaderButMatchesOnRawValue()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1500));
        var currencyStyle = CellStyle.Default.Clone();
        currencyStyle.NumberFormat = "$#,##0.00";
        sheet.GetCell(2, 1)!.StyleId = workbook.RegisterStyle(currencyStyle);

        var plan = new AutoFilterDropdownPlan(
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            FilterColumnOffset: 0);

        var menu = AutoFilterDropdownMenuPlanner.CreateMenuPlan(
            workbook, sheet, plan, InvariantAutoFilterMenuTextProvider.Instance, InvariantAutoFilterMenuTextProvider.BlankDisplayText);

        var checklistEntry = menu.Entries.Single(entry => entry.Kind == AutoFilterMenuEntryKind.ChecklistItem);
        checklistEntry.Header.Should().Be("$1,500.00");
        checklistEntry.Value.Should().Be("1500");
    }
}
