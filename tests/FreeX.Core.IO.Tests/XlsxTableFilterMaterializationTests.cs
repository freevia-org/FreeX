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

    // R118-io-table-autofilter-activevaluefilter-1: a table's value-list AutoFilter criteria must be
    // re-registered into sheet.ActiveValueFilterColumns/ValueFilterHiddenRows on load, exactly like
    // XlsxWorksheetAutoFilterMaterializer.MaterializeFilters already does for a plain worksheet
    // AutoFilter range. Without this, FilterCommand.RecomputeHiddenRows sees
    // ActiveValueFilterColumns.Count == 0 for the table's column and treats it as "no active value
    // filter", so a later Clear Filter / Select-All on that column permanently no-ops instead of
    // restoring the rows this criterion hid.
    [Fact]
    public void R118_EvaluatableValueFilter_RegistersActiveValueFilterColumnAndValueFilterHiddenRows()
    {
        // Column 1 (Amount, table-relative) value filter keeping only "3" -> absolute column is
        // table.Range.Start.Col (1) + ColumnId (1) = 2, matching the "Amount" data column (col 2).
        var valueFilter = new StructuredTableFilterColumnModel(ColumnId: 1, Values: ["3"]);
        var (sheet, table) = BuildSheetWithTable(valueFilter);

        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        sheet.ActiveValueFilterColumns.Should().ContainKey(2u);
        sheet.ActiveValueFilterColumns[2u].Should().BeEquivalentTo(new[] { "3" });
        sheet.ValueFilterHiddenRows.Should().Contain([2u, 4u, 5u]);
        sheet.ValueFilterHiddenRows.Should().NotContain(3u);
    }

    // No-regression sibling: a table filter column FreeX cannot evaluate (native-XML-only, e.g. an
    // icon filter) must NOT register anything into ActiveValueFilterColumns/ValueFilterHiddenRows --
    // those are owned exclusively by plain value-list criteria (see
    // XlsxWorksheetAutoFilterMaterializer's identical distinction via ColumnFilterOwnedRows for its
    // own unsupported-column fallback). Registering a fabricated allowed-value set here would make
    // FilterCommand.RecomputeHiddenRows re-evaluate this column against criteria that don't reflect
    // the real (unrepresentable) Excel filter, corrupting the row set the "hidden rows" fallback
    // above just correctly reclassified.
    [Fact]
    public void R118_UnevaluatableTableFilter_DoesNotRegisterActiveValueFilterColumn()
    {
        var iconFilter = new StructuredTableFilterColumnModel(
            ColumnId: 1, Values: [], IncludeBlank: false,
            NativeFilterXml: "<iconFilter iconSet=\"3TrafficLights1\" iconId=\"1\"/>");
        var (sheet, table) = BuildSheetWithTable(iconFilter, hiddenRows: [3, 4]);

        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        sheet.ActiveValueFilterColumns.Should().BeEmpty();
        sheet.ValueFilterHiddenRows.Should().BeEmpty();
    }
}
