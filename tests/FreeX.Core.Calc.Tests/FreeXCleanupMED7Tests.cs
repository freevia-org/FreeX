using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for FreeX cleanup batch MED7 (round-10 MED/LOW findings): P54 (DateOccurring
/// week semantics), P55 (Top/Bottom-10 tie inclusion), P107 (RelocateTotalsRowIfNeeded blanking
/// non-table cells / losing style), and P82 (row/column shifts only rewriting hyperlink bookmarks on
/// the edited sheet).
/// </summary>
public sealed class FreeXCleanupMED7Tests
{
    // ── P54: DateOccurring "this/last/next week" must use Excel's Sunday-start week ────────────

    [Fact]
    public void ConditionalFormat_ThisWeek_TreatsSundayAsStartOfWeekLikeExcel()
    {
        // Excel's WEEKDAY()-based timePeriod formula (Sunday=1 default) anchors "this week" at
        // the most recent Sunday on/before today, through the following Saturday. A cell dated on
        // that Sunday must be classified as "this week" (not "last week", which a Monday-anchored
        // week would wrongly produce whenever today is itself a Sunday-through-Tuesday). Computed
        // relative to DateTime.Today (what the evaluator actually reads) so this is stable on any
        // day the suite runs.
        var today = DateTime.Today;
        var sunday = today.AddDays(-(int)today.DayOfWeek);
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(sunday));

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "thisWeek",
            FormatIfTrue = green
        });

        var svc = new ViewportService();
        var vp = svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 10, 10));
        var cell = vp.Cells.Single(c => c.Row == 1 && c.Col == 1);

        cell.Style!.FillColor.Should().Be(
            new CellColor(198, 239, 206),
            "Excel's timePeriod week formulas are Sunday-start (WEEKDAY default), so a Sunday-dated cell falls in 'this week'");
    }

    // ── P55: Top/Bottom-10 must highlight every cell tied at the cutoff value ──────────────────

    [Fact]
    public void ConditionalFormat_Top1_HighlightsAllCellsTiedAtCutoffValue()
    {
        // A1:A4 = 5, 5, 5, 1 with a "Top 1 items" rule. Excel highlights every cell whose value
        // ranks within the top N, ties included, so all three 5s must highlight (not just A1).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromValue(new NumberValue(1)));

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 1,
            AboveAverage = true,
            FormatIfTrue = green
        });

        var svc = new ViewportService();
        var vp = svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));
        DisplayCell Get(uint row) => vp.Cells.Single(c => c.Row == row && c.Col == 1);

        Get(1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
        Get(2).Style!.FillColor.Should().Be(new CellColor(198, 239, 206), "A2 ties the top-ranked value and must highlight too");
        Get(3).Style!.FillColor.Should().Be(new CellColor(198, 239, 206), "A3 ties the top-ranked value and must highlight too");
        Get(4).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
    }

    // ── P107: resizing a table wider AND taller must not blank cells that were never part of ──
    // ── the old table, and must preserve the relocated totals row's formatting ─────────────────

    [Fact]
    public void ResizeStructuredTableCommand_GrowingRowsAndColumns_PreservesUserCellsOutsideOldTableAndTotalsRowStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");

        // A1:B5 table with a shown totals row (row 5).
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            TotalsRowShown = true,
            Columns =
            {
                new StructuredTableColumnModel(1, "Region"),
                new StructuredTableColumnModel(2, "Amount", TotalsRowFunction: "sum")
            }
        };
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Total"));

        var totalsStyle = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(217, 217, 217) });
        var totalsCell = Cell.FromFormula("SUBTOTAL(109,[Amount])");
        totalsCell.StyleId = totalsStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), totalsCell);

        sheet.StructuredTables.Add(table);

        // User data OUTSIDE the table at C5:D5 — never part of the old table, must survive.
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("UserNote"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new NumberValue(42));

        var ctx = new TestCommandContext(wb);

        // Resize grows BOTH rows and columns: A1:B5 -> A1:D8.
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 8, 4));
        var command = new ResizeStructuredTableCommand(sheet.Id, table.Id, newRange);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        // The user's pre-existing data outside the OLD table (C5:D5) must survive untouched —
        // it is now ordinary data-body content of the grown table, not blanked totals-row spillover.
        sheet.GetValue(5, 3).Should().Be(new TextValue("UserNote"), "C5 was never part of the old table and must not be blanked");
        sheet.GetValue(5, 4).Should().Be(new NumberValue(42), "D5 was never part of the old table and must not be blanked");

        // The old table's own totals-row cells (A5:B5) ARE relocated (turned into ordinary blank
        // data-body cells), but must keep their prior formatting rather than reverting to default.
        sheet.GetValue(5, 1).Should().Be(BlankValue.Instance);
        sheet.GetCell(5, 2)!.StyleId.Should().Be(totalsStyle, "the relocated totals cell's formatting must be preserved, not wiped to default");

        command.Revert(ctx);

        sheet.GetValue(5, 3).Should().Be(new TextValue("UserNote"));
        sheet.GetValue(5, 4).Should().Be(new NumberValue(42));
        sheet.GetCell(5, 2)!.FormulaText.Should().Be("SUBTOTAL(109,[Amount])");
    }

    // ── P82: row/column structural edits must rewrite hyperlink bookmarks on EVERY sheet ────────
    // ── that targets the edited sheet, not just the sheet being edited ─────────────────────────

    [Fact]
    public void InsertRowsCommand_RewritesCrossSheetHyperlinkBookmark_AndUndoRestoresOriginal()
    {
        // Sheet2!B2 carries a "Place in This Document" hyperlink whose Bookmark targets
        // "Sheet1!A10". Inserting a row above row 10 on Sheet1 must correct that bookmark to
        // "Sheet1!A11" even though the hyperlink itself lives on Sheet2, not Sheet1.
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        var linkAddress = CellAddress.Parse("B2", sheet2.Id);
        var setHyperlink = new SetHyperlinkCommand(
            sheet2.Id,
            linkAddress,
            target: "Sheet1!A10",
            displayText: "Go to Sheet1!A10",
            metadata: new HyperlinkMetadata(HyperlinkTargetKind.PlaceInThisDocument, Bookmark: "Sheet1!A10"));
        var ctx = new TestCommandContext(wb);
        setHyperlink.Apply(ctx).Success.Should().BeTrue();

        var insertRows = new InsertRowsCommand(sheet1.Id, beforeRow: 10, count: 1);
        insertRows.Apply(ctx).Success.Should().BeTrue();

        sheet2.HyperlinkMetadata[linkAddress].Bookmark.Should().Be(
            "Sheet1!A11",
            "the cross-sheet bookmark on Sheet2 must be corrected even though the structural edit happened on Sheet1");

        insertRows.Revert(ctx);

        sheet2.HyperlinkMetadata[linkAddress].Bookmark.Should().Be("Sheet1!A10");
    }
}
