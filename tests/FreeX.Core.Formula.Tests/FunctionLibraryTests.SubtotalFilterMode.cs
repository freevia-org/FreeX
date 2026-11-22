using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// SUBTOTAL 1-11 skip "filtered" rows only, but real Excel (probed via COM) treats EVERY hidden row on a
/// sheet as filtered while the sheet is in filter mode (an AutoFilter / table filter with criteria), so
/// SUBTOTAL(3/9) and SUBTOTAL(103/109) agree. Without an active filter, manually hidden rows are still
/// counted by 1-11 and skipped only by 101-111.
/// </summary>
public partial class FunctionLibraryTests
{
    private static (Workbook Wb, Sheet Sheet) BuildTenRowSheet()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        for (uint r = 1; r <= 10; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        return (wb, sheet);
    }

    [Fact]
    public void Subtotal_NoActiveFilter_ManualHiddenRow_CountedBy1To11_SkippedBy101To111()
    {
        var (wb, sheet) = BuildTenRowSheet();
        sheet.HiddenRows.Add(3);

        _eval.Evaluate("=SUBTOTAL(3,A1:A10)", sheet, wb).Should().Be(new NumberValue(10));
        _eval.Evaluate("=SUBTOTAL(9,A1:A10)", sheet, wb).Should().Be(new NumberValue(55));
        _eval.Evaluate("=SUBTOTAL(103,A1:A10)", sheet, wb).Should().Be(new NumberValue(9));
        _eval.Evaluate("=SUBTOTAL(109,A1:A10)", sheet, wb).Should().Be(new NumberValue(52));
    }

    [Fact]
    public void Subtotal_ActiveFilter_ManualHiddenRowInsideRange_TreatedAsFiltered()
    {
        var (wb, sheet) = BuildTenRowSheet();
        sheet.FilterHiddenRows.Add(2);
        sheet.HiddenRows.Add(3); // manual hide while the filter is active

        _eval.Evaluate("=SUBTOTAL(3,A1:A10)", sheet, wb).Should().Be(new NumberValue(8));
        _eval.Evaluate("=SUBTOTAL(9,A1:A10)", sheet, wb).Should().Be(new NumberValue(50));
        _eval.Evaluate("=SUBTOTAL(103,A1:A10)", sheet, wb).Should().Be(new NumberValue(8));
        _eval.Evaluate("=SUBTOTAL(109,A1:A10)", sheet, wb).Should().Be(new NumberValue(50));
    }

    [Fact]
    public void Subtotal_ActiveFilter_GroupCollapsedRow_TreatedAsFiltered()
    {
        var (wb, sheet) = BuildTenRowSheet();
        sheet.FilterHiddenRows.Add(2);
        sheet.GroupHiddenRows.Add(5);

        _eval.Evaluate("=SUBTOTAL(9,A1:A10)", sheet, wb).Should().Be(new NumberValue(48));
    }

    [Fact]
    public void Subtotal_ActiveFilterColumnModel_WithNoFilteredRows_StillFilterMode()
    {
        // Excel's FilterMode is true as soon as a filter criterion exists, even if it hides nothing.
        var (wb, sheet) = BuildTenRowSheet();
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A10", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["1"]));
        sheet.HiddenRows.Add(4);

        _eval.Evaluate("=SUBTOTAL(9,A1:A10)", sheet, wb).Should().Be(new NumberValue(51));
    }

    [Fact]
    public void Subtotal_AfterClearFilter_ManualHiddenRowsCountedAgain()
    {
        var (wb, sheet) = BuildTenRowSheet();
        sheet.FilterHiddenRows.Add(2);
        sheet.HiddenRows.Add(3);
        _eval.Evaluate("=SUBTOTAL(9,A1:A10)", sheet, wb).Should().Be(new NumberValue(50));

        // Clear Filter / ShowAllData: filter rows reappear, the manual hide stays but is no longer "filtered".
        sheet.FilterHiddenRows.Clear();
        sheet.ActiveValueFilterColumns.Clear();

        _eval.Evaluate("=SUBTOTAL(9,A1:A10)", sheet, wb).Should().Be(new NumberValue(55));
        _eval.Evaluate("=SUBTOTAL(109,A1:A10)", sheet, wb).Should().Be(new NumberValue(52));
    }

    [Fact]
    public void Subtotal_ActiveFilter_HiddenRowOutsideAutoFilterRange_TreatedAsFiltered()
    {
        // Probed in Excel: the filtered flag is sheet-wide, not limited to the AutoFilter range.
        var (wb, sheet) = BuildTenRowSheet();
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A4", null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["1"]));
        sheet.HiddenRows.Add(8);

        _eval.Evaluate("=SUBTOTAL(9,A1:A10)", sheet, wb).Should().Be(new NumberValue(47));
    }
}
