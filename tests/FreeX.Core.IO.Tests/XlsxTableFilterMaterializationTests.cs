using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

// A table autofilter can use criteria FreeX cannot evaluate directly (icon/color/custom/dynamic/top-N filters,
// preserved as native XML). Excel still saves the filtered rows as hidden. Those rows must be treated as
// FILTER-hidden (not just manually hidden) so SUBTOTAL/AGGREGATE codes 1-11 exclude them, matching Excel.
// Mirrors the ConditionalFormattingSamples fidelity finding (Table69 filtered by an <iconFilter>).
public class XlsxTableFilterMaterializationTests
{
    private static (Sheet sheet, StructuredTableModel table) BuildSheetWithTable(
        StructuredTableFilterColumnModel filterColumn, params uint[] hiddenRows)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        // Header row 1; data rows 2..5 in column B.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (uint r = 2; r <= 5; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r));
        foreach (var r in hiddenRows)
            sheet.HiddenRows.Add(r);

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T",
            DisplayName = "T",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HasAutoFilter = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        table.FilterColumns.Add(filterColumn);
        return (sheet, table);
    }

    [Fact]
    public void UnevaluatableTableFilter_ReclassifiesHiddenDataRowsAsFilterHidden()
    {
        // Column 1 (Amount) filtered by an icon filter FreeX cannot evaluate -> native XML only.
        var iconFilter = new StructuredTableFilterColumnModel(
            ColumnId: 1, Values: [], IncludeBlank: false,
            NativeFilterXml: "<iconFilter iconSet=\"3TrafficLights1\" iconId=\"1\"/>");
        var (sheet, table) = BuildSheetWithTable(iconFilter, hiddenRows: [3, 4]);

        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        sheet.FilterHiddenRows.Should().Contain([3u, 4u]);
    }

    [Fact]
    public void EvaluatableValueFilter_StillComputesHiddenRowsFromCriteria()
    {
        // Column 1 (Amount) value filter keeping only "3" -> rows whose Amount != 3 are filter-hidden.
        var valueFilter = new StructuredTableFilterColumnModel(ColumnId: 1, Values: ["3"]);
        var (sheet, table) = BuildSheetWithTable(valueFilter);

        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        sheet.FilterHiddenRows.Should().Contain([2u, 4u, 5u]); // Amount 2,4,5 hidden; row 3 (Amount 3) kept
        sheet.FilterHiddenRows.Should().NotContain(3u);
    }

    [Fact]
    public void TableWithoutFilterColumns_DoesNothing()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.HiddenRows.Add(3);
        var table = new StructuredTableModel
        {
            Id = 1, Name = "T", DisplayName = "T",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
        };

        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        sheet.FilterHiddenRows.Should().BeEmpty();
    }
}
