using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// table-semantics-F1 regression test: every interactive/table-filter recompute path in
/// <c>FilterCommand.cs</c> unconditionally started row-hiding evaluation at
/// <c>range.Start.Row + 1</c>, assuming exactly one header row regardless of
/// <see cref="StructuredTableModel.HeaderRowCount"/>. For a Structured Table loaded with
/// <c>headerRowCount="0"</c> (a genuine, round-tripped Excel feature; see
/// <c>StructuredReferenceResolver.HeaderRowCount()</c>, <c>Commands.cs</c>'s
/// <c>hasHeaderRow = table.HeaderRowCount is null or > 0</c>, and
/// <c>XlsxStructuredTableModelMapper.MaterializeFilters</c>' own header-count-aware load path),
/// <c>table.Range.Start.Row</c> IS ITSELF a data row -- but the old recompute code never evaluated it
/// against any active filter criterion, so it stayed permanently visible no matter what the user
/// filtered on. Mirrors the fix in <c>R22_HeaderlessTableFilterMaterializationTests</c> for the
/// (already-correct) load-time path, but exercises the interactive recompute path instead --
/// <see cref="FilterCommand.Apply"/>, which backs both the ordinary "click the column's filter
/// dropdown arrow and check/uncheck values" gesture and the Slicer/table-resize recompute paths.
/// </summary>
public sealed class R146_HeaderlessTableFilterRecomputeTests
{
    private static StructuredTableModel AddTable(Sheet sheet, GridRange range, int? headerRowCount)
    {
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            Range = range,
            HeaderRowCount = headerRowCount,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Region"));
        sheet.StructuredTables.Add(table);
        return table;
    }

    [Fact]
    public void FilterCommand_Apply_HeaderlessTable_EvaluatesFirstDataRowAgainstFilter()
    {
        // headerRowCount=0: Range.Start.Row (row 1) IS itself a data row, not a header row.
        // Mirrors the finding's own probe: North/South/North/North, filter keeps only "South".
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: 0);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));

        var ctx = new TestCommandContext(wb);
        var command = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["South"]);

        command.Apply(ctx).Success.Should().BeTrue();

        // Row 1 ("North") fails the filter and, being a genuine data row of a headerless table,
        // must be hidden exactly like rows 3 and 4. Only row 2 ("South") stays visible.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([1u, 3u, 4u],
            "row 1 is the first DATA row of a headerless table (headerRowCount=0), not a header, " +
            "and must be evaluated against the active filter like every other data row");
    }

    [Fact]
    public void FilterCommand_Apply_HeaderedTable_StillSkipsHeaderRow()
    {
        // Sibling/no-regression guard: the default (HeaderRowCount unset -> treated as 1) behavior
        // must be unchanged -- row 1 (the header) must never be evaluated/hidden by the filter, even
        // though its text ("Region") would itself fail an allowed-values filter of ["South"].
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));
        AddTable(sheet, range, headerRowCount: null);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));

        var ctx = new TestCommandContext(wb);
        var command = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["South"]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u],
            "the header row (row 1) must never be evaluated or hidden by the filter");
    }

    [Fact]
    public void FilterCommand_Apply_PlainWorksheetAutoFilter_StillSkipsFirstRow()
    {
        // Sibling/no-regression guard: a range with NO matching structured table (a plain worksheet
        // AutoFilter) must keep the unconditional "first row is a header" assumption -- there is no
        // HeaderRowCount to consult, and Excel's own worksheet-level AutoFilter always treats the
        // first row of its range as headers.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("North"));

        var ctx = new TestCommandContext(wb);
        var command = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["South"]);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.FilterHiddenRows.Should().BeEquivalentTo([3u, 4u],
            "a plain worksheet AutoFilter range (no matching structured table) has no HeaderRowCount " +
            "to consult, so row 1 must still be treated as the header row");
    }
}
