using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Excel never re-evaluates an AutoFilter when opening a file: the persisted row hidden bits are the
/// filtered state. FreeX used to re-evaluate the criteria against its own cell text and hid rows Excel
/// shows (Capacity_Planning corpus file). When the file persists any hidden data row, non-hidden rows
/// must stay visible; hidden rows are filter-owned while a filter is active (so SUBTOTAL 1-11 skip them).
/// </summary>
public sealed class AutoFilterPersistedVisibilityTests
{
    private static Sheet BuildSheet()
    {
        var wb = new Workbook("t");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Key"));
        for (uint r = 2; r <= 6; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:A6", null);
        // Allowed value list only admits "2": rows 3..6 fail FreeX's re-evaluation.
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(0, ["2"]));
        return sheet;
    }

    [Fact]
    public void PersistedHiddenBits_AreAuthoritative_NonHiddenFailingRowsStayVisible()
    {
        var sheet = BuildSheet();
        sheet.HiddenRows.Add(3); // file hides only row 3
        sheet.HiddenRows.Add(4);

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u]);
        sheet.IsRowEffectivelyHidden(5).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(6).Should().BeFalse();
        sheet.IsRowEffectivelyHidden(2).Should().BeFalse();
    }

    [Fact]
    public void NoPersistedHiddenBits_FallsBackToReEvaluation()
    {
        var sheet = BuildSheet();

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u, 5u, 6u]);
    }

    [Fact]
    public void HiddenRowPassingFilterAndOutsideRange_IsFilteredForSubtotalWhileFilterActive()
    {
        var sheet = BuildSheet();
        sheet.HiddenRows.Add(2); // passes the criteria, but is hidden in the file
        sheet.HiddenRows.Add(3);
        sheet.HiddenRows.Add(20); // outside the AutoFilter range

        XlsxWorksheetAutoFilterMaterializer.MaterializeFilters(sheet);

        sheet.IsFilterModeActive.Should().BeTrue();
        sheet.IsRowFilterHidden(2).Should().BeTrue();
        sheet.IsRowFilterHidden(3).Should().BeTrue();
        sheet.IsRowFilterHidden(20).Should().BeTrue();
        sheet.IsRowFilterHidden(5).Should().BeFalse();
    }

    [Fact]
    public void TableFilter_PersistedHiddenBits_AreAuthoritative()
    {
        var wb = new Workbook("t");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Key"));
        for (uint r = 2; r <= 6; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T",
            DisplayName = "T",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 1)),
            HasAutoFilter = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Key"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(ColumnId: 0, Values: ["2"]));
        sheet.HiddenRows.Add(3);

        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
        sheet.IsRowEffectivelyHidden(4).Should().BeFalse();
    }
}
