using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R22-io-sharedstrings-names-tables-2 regression test.
///
/// <c>XlsxStructuredTableModelMapper.MaterializeFilters</c> hardcoded
/// <c>firstDataRow = table.Range.Start.Row + 1</c>, assuming exactly one header row regardless of
/// <see cref="StructuredTableModel.HeaderRowCount"/>. For a headerless table (Excel's "Table has
/// headers" unchecked, <c>headerRowCount="0"</c>), <c>Range.Start.Row</c> IS itself a data row, but
/// the old code never evaluated it against the table's filter criteria, so it was never added to
/// <see cref="Sheet.FilterHiddenRows"/> even when it should have been hidden -- letting it stay
/// visible and be wrongly included by SUBTOTAL/AGGREGATE. The fix mirrors
/// <c>TableStyleSections.From</c>'s <c>Math.Clamp(table.HeaderRowCount ?? 1, 0, rowCount)</c>.
/// </summary>
public sealed class R22_HeaderlessTableFilterMaterializationTests
{
    [Fact]
    public void HeaderlessTable_EvaluatesFirstDataRowAgainstFilter()
    {
        // headerRowCount=0: Range.Start.Row (row 1) IS itself a data row, not a header row.
        var sheet = new Sheet(SheetId.New(), "S");
        for (uint r = 1; r <= 5; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "T",
            DisplayName = "T",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            HasAutoFilter = true,
            HeaderRowCount = 0,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Amount"));
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(ColumnId: 1, Values: ["3"]));

        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        // Only row 3 (Amount=3) matches the filter. Every other data row -- including row 1, the
        // FIRST data row of a headerless table -- must be filter-hidden, matching Excel.
        sheet.FilterHiddenRows.Should().Contain([1u, 2u, 4u, 5u]);
        sheet.FilterHiddenRows.Should().NotContain(3u);
    }

    [Fact]
    public void HeaderedTable_StillSkipsHeaderRow()
    {
        // Regression guard: the default (HeaderRowCount unset -> treated as 1) behavior must be
        // unchanged -- row 1 (the header) must never be evaluated/hidden by the filter.
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        for (uint r = 2; r <= 5; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r));

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
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(ColumnId: 1, Values: ["3"]));

        XlsxStructuredTableModelMapper.MaterializeFilters(sheet, table);

        sheet.FilterHiddenRows.Should().Contain([2u, 4u, 5u]);
        sheet.FilterHiddenRows.Should().NotContain(1u);
        sheet.FilterHiddenRows.Should().NotContain(3u);
    }
}
