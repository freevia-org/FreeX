using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// R80-commands-outline-subtotal-5-1: a NESTED Subtotal pass (second Data > Subtotal call,
/// "Replace current subtotals" unchecked) run after an earlier "Summary below data" UNCHECKED
/// (i.e. summary-above) pass must not conflate that earlier pass's leading Grand Total row into
/// the new pass's first group. BuildSummaryAbovePlan places the Grand Total row at
/// range.Start.Row + 1 (directly after the header, not at the end of the range like a
/// summary-below pass), so SubtotalPlanBuilder.GetGroups' seed row (also range.Start.Row + 1) can
/// itself be one of the prior pass's existing-subtotal rows. The seed/label picked up from that
/// row was never checked against `existingSubtotalRows` (unlike every row scanned by the loop
/// body, and unlike the range's own final row via the `scanEnd` trim), so it merged the blank-
/// labeled Grand Total row with the next real row into one bogus "&lt;blank&gt; Total" group.
/// </summary>
public sealed class R80_SubtotalNestedSummaryAboveTests
{
    private static (Workbook wb, Sheet sheet) BuildRegionTypeSheet()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Type"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Y"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new TextValue("Y"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 3), new NumberValue(50));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 2), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 3), new NumberValue(60));
        return (wb, sheet);
    }

    // R80-commands-outline-subtotal-5-1 ------------------------------------------------------

    [Fact]
    public void NestedSubtotal_AfterSummaryAbovePass_DoesNotConflateLeadingGrandTotalIntoFirstGroup()
    {
        var (wb, sheet) = BuildRegionTypeSheet();
        var ctx = new TestCommandContext(wb);

        // Pass 1: "Summary below data" UNCHECKED (summary-above), group by Region (offset 0), sum
        // Sales (offset 2). BuildSummaryAbovePlan puts the Grand Total at row 2 (range.Start.Row +
        // 1, directly after the header) and each region's own subtotal directly above its block.
        var range1 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 3));
        new SubtotalCommand(sheet.Id, range1, groupByColumnOffset: 0, subtotalColumnOffset: 2, summaryBelowData: false)
            .Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(2, 1).Should().Be(new TextValue("Grand Total"), "sanity: pass 1's grand total lands directly after the header");
        sheet.GetValue(3, 1).Should().Be(new TextValue("East Total"));
        sheet.GetValue(7, 1).Should().Be(new TextValue("West Total"));
        var lastRowAfterPass1 = 10u;
        sheet.GetCell(lastRowAfterPass1 + 1, 1).Should().BeNull("sanity: pass 1's table ends at row 10");

        // Pass 2 (nested): "Replace current subtotals" UNCHECKED, group by Type (offset 1), sum
        // Sales, still summary-above, over the now-expanded range including pass 1's rows.
        var range2 = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, lastRowAfterPass1, 3));
        new SubtotalCommand(sheet.Id, range2, groupByColumnOffset: 1, subtotalColumnOffset: 2, summaryBelowData: false)
            .Apply(ctx).Success.Should().BeTrue();

        // The bug: GetGroups seeds groupStart/currentLabel from row 2 (range.Start.Row + 1)
        // without checking it is itself pass 1's Grand Total row (blank Type cell), so it merged
        // rows 2-3 into one bogus GroupSpan(Label:"", ...) and emitted a spurious blank-labeled
        // " Total" row splicing into the sheet between the two Grand Total rows. Fixed: this
        // pass's new Grand Total (row 2) and pass 1's preserved Grand Total (shifted to row 3) sit
        // directly adjacent, with no bogus row between them.
        sheet.GetValue(2, 2).Should().Be(new TextValue("Grand Total"), "this pass's own grand total, seeded from the Type column");
        sheet.GetValue(3, 1).Should().Be(new TextValue("Grand Total"), "pass 1's preserved grand total must shift down intact, not be conflated away");
        sheet.GetCell(3, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C4:C16)", "its range must cover the whole shifted table, not be truncated by a bogus absorbed group");
        sheet.GetValue(4, 1).Should().Be(new TextValue("East Total"), "pass 1's East Total must directly follow the grand total row, with no spurious row spliced between them");

        // No row anywhere in the rebuilt table carries the bug's blank-labeled group ("" + " Total").
        for (uint r = 1; r <= 16; r++)
        {
            sheet.GetValue(r, 1).Should().NotBe(new TextValue(" Total"), $"row {r} must not be the bug's blank-labeled group total");
            sheet.GetValue(r, 2).Should().NotBe(new TextValue(" Total"), $"row {r} must not be the bug's blank-labeled group total");
        }

        // Pass 2 added exactly 6 rows (5 Type-group totals + 1 new grand total) to pass 1's
        // 10-row table -- not 7, which is what the bug's extra bogus group produced.
        sheet.GetCell(17, 1).Should().BeNull("no bogus extra row should push the table past its expected 16-row length");
    }

    // Sibling no-regression: a first (non-nested) summary-above pass, where existingSubtotalRows
    // is empty and the new leading-row skip loop is a no-op, must still produce the plain,
    // unaffected grouping.
    [Fact]
    public void Subtotal_FirstPass_SummaryAbove_StillProducesSimpleSingleLevelGroups()
    {
        var (wb, sheet) = BuildRegionTypeSheet();
        var ctx = new TestCommandContext(wb);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 3));
        var outcome = new SubtotalCommand(sheet.Id, range, groupByColumnOffset: 0, subtotalColumnOffset: 2, summaryBelowData: false)
            .Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.GetValue(2, 1).Should().Be(new TextValue("Grand Total"));
        sheet.GetCell(2, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C3:C10)");
        sheet.GetValue(3, 1).Should().Be(new TextValue("East Total"));
        sheet.GetCell(3, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C4:C6)");
        sheet.GetValue(4, 1).Should().Be(new TextValue("East"));
        sheet.GetValue(7, 1).Should().Be(new TextValue("West Total"));
        sheet.GetCell(7, 3)!.FormulaText.Should().Be("SUBTOTAL(9,C8:C10)");
        sheet.GetCell(11, 1).Should().BeNull("nothing should be inserted past the last data row");
    }
}
