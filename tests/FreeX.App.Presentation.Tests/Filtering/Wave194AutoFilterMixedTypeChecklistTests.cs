using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Filtering;

public sealed class Wave194AutoFilterMixedTypeChecklistTests
{
    [Fact]
    public void MixedTypeChecklist_GroupsNumericTextWithNumberAndKeepsFormattedDateAndBlank()
    {
        var workbook = new Workbook("Wave194 Mixed Type");
        var sheet = workbook.AddSheet("Data");
        var range = PopulateMixedTypeRows(workbook, sheet);
        var plan = new AutoFilterDropdownPlan(range, FilterColumnOffset: 0);

        var items = AutoFilterChecklistPlanner.CreateItems(workbook, sheet, plan, "(Blanks)");

        items.Select(item => (item.DisplayText, item.Value)).Should().Equal(
            ("7", "7"),
            ("42", "42"),
            ("2024-01-01", "45292"),
            ("Alpha", "Alpha"),
            ("(Blanks)", ""));
        items.Count(item => item.Value == "42").Should().Be(1,
            "numeric 42 and text '42' share Excel's value-filter criterion");
    }

    private static GridRange PopulateMixedTypeRows(Workbook workbook, Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Mixed"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("42"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(45292));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new NumberValue(7));

        var dateStyle = CellStyle.Default.Clone();
        dateStyle.NumberFormat = "yyyy-mm-dd";
        sheet.GetCell(6, 1)!.StyleId = workbook.RegisterStyle(dateStyle);

        return new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 7, 2));
    }
}
