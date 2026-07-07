using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-12 fix bucket Q8 regression tests.
/// </summary>
public class FreeXR12Q8Tests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new TestCommandContext(wb));
    }

    private static void Set(Sheet sheet, uint row, uint col, string text) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new TextValue(text)));

    private static GridRange Range(Sheet sheet, uint r1, uint c1, uint r2, uint c2) =>
        new(new CellAddress(sheet.Id, r1, c1), new CellAddress(sheet.Id, r2, c2));

    /// <summary>
    /// R12-sort-filter-2: plain (unquoted) text in an Advanced Filter criteria cell means
    /// "begins with" in Excel, not exact match. List range A1:A4 (header "Name" + Smith/Smart/
    /// Jones); criteria range with header "Name" and criteria cell "Sm" must match both Smith and
    /// Smart (both begin with "Sm"), leaving only Jones hidden.
    /// </summary>
    [Fact]
    public void AdvancedFilter_PlainTextCriteria_MatchesBeginsWithNotExactEquals()
    {
        var (_, sheet, ctx) = Setup();
        Set(sheet, 1, 1, "Name");
        Set(sheet, 2, 1, "Smith");
        Set(sheet, 3, 1, "Smart");
        Set(sheet, 4, 1, "Jones");

        Set(sheet, 1, 3, "Name");
        Set(sheet, 2, 3, "Sm");

        var command = new AdvancedFilterCommand(
            ListRange: Range(sheet, 1, 1, 4, 1),
            CriteriaRange: Range(sheet, 1, 3, 2, 3),
            CopyTo: null,
            UniqueRecordsOnly: false);

        command.Apply(ctx).Success.Should().BeTrue();

        // Rows 2 (Smith) and 3 (Smart) both begin with "Sm" and must stay visible; row 4 (Jones)
        // does not match and must be hidden.
        sheet.FilterHiddenRows.Should().BeEquivalentTo([4u]);
        sheet.FilterHiddenRows.Should().NotContain([2u, 3u]);

        command.Revert(ctx);
        sheet.FilterHiddenRows.Should().BeEmpty();
    }

    /// <summary>
    /// R12-xlsx-defined-names-3: inserting rows above a named range whose START row is close to
    /// the sheet bottom must clamp the new Start.Row to MaxRow, not let it overflow past it.
    /// Range starts at row 1048570 (near MaxRow=1048576); inserting 10 rows above pushes the raw
    /// start to 1048580 (past MaxRow). Both Start and End must clamp to MaxRow so GridRange never
    /// normalizes to an out-of-bounds address (which would corrupt the saved file / trigger
    /// Excel's repair prompt).
    /// </summary>
    [Fact]
    public void InsertRows_NamedRangeStartNearSheetBottom_ClampsStartRowAtMaxAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var nearBottom = new GridRange(
            new CellAddress(sheet.Id, CellAddress.MaxRow - 6, 1),   // row 1_048_570
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));      // row 1_048_576
        wb.DefineNamedRange("NearBottom", nearBottom);

        var cmd = new InsertRowsCommand(sheet.Id, beforeRow: 1, count: 10);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var shifted = wb.NamedRanges["NearBottom"];
        shifted.Start.Row.Should().Be(CellAddress.MaxRow,
            "start would overflow to 1048580 unclamped; Excel's max row is 1048576");
        shifted.End.Row.Should().Be(CellAddress.MaxRow,
            "end must also stay clamped at MaxRow");

        cmd.Revert(ctx);

        var restored = wb.NamedRanges["NearBottom"];
        restored.Start.Row.Should().Be(CellAddress.MaxRow - 6);
        restored.End.Row.Should().Be(CellAddress.MaxRow);
    }

    /// <summary>
    /// R12-xlsx-defined-names-3 (columns): the identical overflow defect existed for column
    /// inserts — inserting columns to the left of a named range whose START column sits close to
    /// MaxCol must clamp Start.Col at MaxCol instead of overflowing past it.
    /// </summary>
    [Fact]
    public void InsertColumns_NamedRangeStartNearSheetEdge_ClampsStartColAtMaxAndUndoRestores()
    {
        var (wb, sheet, ctx) = Setup();
        var nearEdge = new GridRange(
            new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 4),  // col 16_380
            new CellAddress(sheet.Id, 2, CellAddress.MaxCol));     // col 16_384
        wb.DefineNamedRange("NearEdge", nearEdge);

        var cmd = new InsertColumnsCommand(sheet.Id, beforeCol: 1, count: 10);
        cmd.Apply(ctx).Success.Should().BeTrue();

        var shifted = wb.NamedRanges["NearEdge"];
        shifted.Start.Col.Should().Be(CellAddress.MaxCol,
            "start would overflow past 16384 unclamped; Excel's max column is 16384");
        shifted.End.Col.Should().Be(CellAddress.MaxCol);

        cmd.Revert(ctx);

        var restored = wb.NamedRanges["NearEdge"];
        restored.Start.Col.Should().Be(CellAddress.MaxCol - 4);
        restored.End.Col.Should().Be(CellAddress.MaxCol);
    }
}
