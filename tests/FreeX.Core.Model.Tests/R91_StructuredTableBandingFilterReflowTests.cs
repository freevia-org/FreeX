using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

// R91-meta-3: r90 added StructuredTableStyleService.RebandTable as the "re-flow banding after a
// mutation that changes which physical row a table's data occupies" entry point and wired it into
// InsertRowsCommand/SortCommand, but never into FilterCommand -- so a banded table's stripes went
// stale after an AutoFilter hid/showed rows. Excel's row banding alternates across VISIBLE row
// position (a hidden row does not consume a stripe slot), so this also requires
// StructuredTableStyleService to skip hidden rows when computing each row's stripe parity, not just
// the FilterCommand wiring alone. Exercised through the real command entry point: FilterCommand,
// driving StructuredTableStyleService.RebandTable via TestCommandContext (mirrors
// R90_StructuredTableBandingReflowTests's Insert/Sort coverage).
public sealed class R91_StructuredTableBandingFilterReflowTests
{
    [Fact]
    public void R91_ApplyFilter_ReflowsBandingAroundNewlyHiddenRows()
    {
        var workbook = new Workbook("BandingReflowFilter");
        var sheet = workbook.AddSheet("Data");
        // Header row1; data rows 2-5. Column 1 values alternate Keep/Drop so filtering to "Keep"
        // hides rows 3 and 5, leaving rows 2 and 4 visible.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Drop"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(40));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2));
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            DisplayName = "Table1",
            Range = range,
            HeaderRowCount = 1,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        sheet.StructuredTables.Add(table);

        // Bake the initial (pre-filter) banding: row2=even, row3=odd, row4=even, row5=odd.
        StructuredTableStyleService.ApplyLoadedTableStyles(workbook).Should().BeTrue();
        var banding = StructuredTableStyleBandingResolver.Resolve("TableStyleMedium2", workbook.Theme);
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill, "sanity: pre-filter row4 is even");

        var ctx = new TestCommandContext(workbook);
        new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Keep"]).Apply(ctx).Success.Should().BeTrue();

        // Rows 3 and 5 are now filter-hidden.
        sheet.FilterHiddenRows.Should().Contain([3u, 5u]);

        // Row2 is still the 1st VISIBLE row -> even. Row4 is now the 2nd VISIBLE row (row3 dropped
        // out of the count because it's hidden) -> odd, even though its own physical offset (2)
        // never changed and previously made it "even".
        StyleAt(workbook, sheet, 2, 1).FillColor.Should().Be(banding.EvenRowFill,
            "row2 is still the 1st visible row");
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.OddRowFill,
            "row4 is now the 2nd VISIBLE row since row3 is filtered out -- banding must re-flow around the gap");

        // Clearing the filter must re-flow banding back to the purely-positional pattern.
        new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: []).Apply(ctx).Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().BeEmpty();
        StyleAt(workbook, sheet, 4, 1).FillColor.Should().Be(banding.EvenRowFill,
            "clearing the filter must re-flow row4 back to its physical-offset parity");
        StyleAt(workbook, sheet, 5, 1).FillColor.Should().Be(banding.OddRowFill);
    }

    // No-regression sibling: filtering a plain worksheet range with no owning structured table must
    // not throw or attempt to reband anything.
    [Fact]
    public void R91_ApplyFilter_PlainRangeWithNoOwningTable_DoesNotThrow()
    {
        var workbook = new Workbook("BandingReflowFilterPlain");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Drop"));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1));
        var ctx = new TestCommandContext(workbook);

        var outcome = new FilterCommand(sheet.Id, range, filterColOffset: 0, allowedValues: ["Keep"]).Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.FilterHiddenRows.Should().Contain(3u);
    }

    private static CellStyle StyleAt(Workbook workbook, Sheet sheet, uint row, uint col) =>
        workbook.GetStyle(sheet.GetCell(new CellAddress(sheet.Id, row, col))!.StyleId);
}
