using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-61 fresh-lens review fixes (formula-a bucket):
///
/// R61-off-by-one-boundary-sweep-1: StructuredReferenceResolver.DataBodyRange/IsDataBodyRow
/// misclassified a table's Totals row as a data row when the table has zero data rows (header +
/// totals only) -- the "table.Range.End.Row > startRow" guard disabled the -1 totals decrement
/// exactly in the one case (0 data rows) where it was needed to detect the empty data body.
///
/// R61-formula-cell-info-6-1: ROW(A:A)/COLUMN(1:1) were silently clamped to the sheet's used
/// range before Row/RowNumbers ever saw them, so INDEX(ROW(A:A),100) returned #REF! instead of
/// 100 -- ROW/COLUMN report POSITIONAL numbers (unlike ROWS/COLUMNS/aggregates, which only count
/// or fold), so clamping to the used range silently changes their result.
///
/// R61-formula-cell-info-6-3: CELL("prefix") returned a label-prefix character for explicitly
/// Left/Center/Right/Fill-aligned NUMBER cells, when Excel's "label prefix" is a Lotus-1-2-3-era
/// concept that only ever applies to TEXT.
///
/// R61-formula-lookup-array-form-6-1: LOOKUP silently skipped error cells encountered during its
/// approximate-match scan (continue) while VLOOKUP/HLOOKUP/MATCH return them, diverging from
/// Excel's well-known "an error in the lookup column poisons the whole lookup" behavior.
/// </summary>
public sealed class R61_FormulaBucketATests
{
    private readonly FormulaEvaluator _eval = new();

    // ── R61-off-by-one-boundary-sweep-1 ───────────────────────────────────────────────

    [Fact]
    public void Resolve_ZeroDataRowTableWithTotalsRow_DoesNotTreatTotalsRowAsDataBody()
    {
        // Header row 1 ("Amount"), Totals row 2 -- zero data rows in between. The totals row
        // holds a sentinel value (999) that must never be treated as if it were a data cell.
        var workbook = new Workbook("ZeroDataRowTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(999));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "ZeroRowTable",
            DisplayName = "ZeroRowTable",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            TotalsRowShown = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Amount", TotalsRowFunction: "sum"));
        sheet.StructuredTables.Add(table);

        var range = StructuredReferenceResolver.Resolve(workbook, sheet, "ZeroRowTable", "Amount");

        // Pre-fix: the inverted guard left endRow at the totals row (2), so this incorrectly
        // resolved to a non-null range spanning row 2 (the totals row) as if it were data.
        // Post-fix: startRow(2) > endRow(1) correctly reports an empty/absent data body.
        range.Should().BeNull();
    }

    [Fact]
    public void Resolve_SingleDataRowTableWithTotalsRow_StillExcludesTotalsRowOnly()
    {
        // Sibling no-regression: the ordinary (non-degenerate) totals-row case must keep
        // resolving to just the one data row, excluding the totals row, exactly as before.
        var workbook = new Workbook("SingleDataRowTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(10));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "OneRowTable",
            DisplayName = "OneRowTable",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            TotalsRowShown = true,
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Amount", TotalsRowFunction: "sum"));
        sheet.StructuredTables.Add(table);

        var range = StructuredReferenceResolver.Resolve(workbook, sheet, "OneRowTable", "Amount");

        range.Should().NotBeNull();
        range!.Value.Start.Row.Should().Be(2);
        range.Value.End.Row.Should().Be(2);
    }

    // ── R61-formula-cell-info-6-1 ──────────────────────────────────────────────────────

    private static Sheet MakeUsedRangeA1ToA10()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        for (uint r = 1; r <= 10; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 1), new NumberValue(r));
        return sheet;
    }

    [Fact]
    public void Row_OfFullColumn_ReportsPositionsBeyondUsedRange()
    {
        // Sheet's used range only reaches row 10, but ROW(A:A) must still be the literal
        // {1;2;...;1048576} array (not clamped down to {1;...;10}), so that indexing/testing a
        // position beyond the populated data (e.g. row 100) still reports the true row number.
        var sheet = MakeUsedRangeA1ToA10();

        var result = _eval.Evaluate("=ROW(A:A)", sheet)
            .Should().BeOfType<RangeValue>()
            .Subject;

        result.RowCount.Should().Be((int)CellAddress.MaxRow);
        result.ColCount.Should().Be(1);
        result.At(100, 1).Should().Be(new NumberValue(100));
        result.At((int)CellAddress.MaxRow, 1).Should().Be(new NumberValue(CellAddress.MaxRow));
    }

    [Fact]
    public void Sum_OfFullColumn_StillClampsToUsedRangeAndAggregatesCorrectly()
    {
        // Sibling no-regression: aggregate functions (SUM, unlike ROW/COLUMN) must still clamp
        // an open-ended full-column reference to the sheet's used range -- this only counts/folds
        // real values, so the clamp must remain in effect and the sum must stay correct.
        var sheet = MakeUsedRangeA1ToA10();

        var result = _eval.Evaluate("=SUM(A:A)", sheet);

        result.Should().Be(new NumberValue(55));
    }

    // ── R61-formula-cell-info-6-3 ──────────────────────────────────────────────────────

    private static (Workbook wb, Sheet sheet) MakeStyledCellWorkbook(ScalarValue value, HorizontalAlignment alignment)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), value);
        var style = new CellStyle { HorizontalAlignment = alignment };
        var styleId = wb.RegisterStyle(style);
        sheet.GetCell(1, 1)!.StyleId = styleId;
        return (wb, sheet);
    }

    [Fact]
    public void Cell_Prefix_ExplicitLeftAlignedNumber_ReturnsEmpty()
    {
        // A number with an explicit Left alignment (e.g. a left-aligned numeric ID column) has
        // no label prefix in Excel -- the label prefix only ever applies to TEXT.
        var (wb, sheet) = MakeStyledCellWorkbook(new NumberValue(42), HorizontalAlignment.Left);

        var result = _eval.Evaluate("=CELL(\"prefix\",A1)", sheet, wb);

        result.Should().Be(new TextValue(""));
    }

    [Fact]
    public void Cell_Prefix_ExplicitLeftAlignedText_StillReturnsApostrophe()
    {
        // Sibling no-regression: an explicitly Left-aligned TEXT cell must keep reporting the
        // apostrophe label prefix, exactly as before this fix.
        var (wb, sheet) = MakeStyledCellWorkbook(new TextValue("x"), HorizontalAlignment.Left);

        var result = _eval.Evaluate("=CELL(\"prefix\",A1)", sheet, wb);

        result.Should().Be(new TextValue("'"));
    }

    // ── R61-formula-lookup-array-form-6-1 ──────────────────────────────────────────────

    private static (Workbook wb, Sheet sheet) MakeLookupWorkbook(ScalarValue a3Value)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), a3Value);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(40));
        return (wb, sheet);
    }

    [Fact]
    public void Lookup_ErrorInLookupVector_ReturnsError()
    {
        // A1:A4 = {1,2,#DIV/0!,4}, B1:B4 = {10,20,30,40}. VLOOKUP over the identical lookup
        // column already returns #DIV/0! the instant it hits the error cell; LOOKUP must match.
        var (wb, sheet) = MakeLookupWorkbook(ErrorValue.DivByZero);

        var result = _eval.Evaluate("=LOOKUP(3,A1:A4,B1:B4)", sheet, wb);

        result.Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Lookup_NoErrorInLookupVector_StillReturnsApproximateMatch()
    {
        // Sibling no-regression: with no error cell present, LOOKUP's ordinary approximate-match
        // behavior over the same shaped vectors must be unaffected by the fix.
        var (wb, sheet) = MakeLookupWorkbook(new NumberValue(3));

        var result = _eval.Evaluate("=LOOKUP(3,A1:A4,B1:B4)", sheet, wb);

        result.Should().Be(new NumberValue(30));
    }
}
