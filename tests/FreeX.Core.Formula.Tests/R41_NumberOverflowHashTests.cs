using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-41 bucket "number-overflow-hash": Excel shows a run of '#' characters ("###") whenever a
/// formatted number/date is too wide for its column -- never a clipped/truncated value, and never
/// for text (which overflows into neighboring blank cells instead). Three related gaps:
///
/// R41-render-number-overflow-hash-3-1 (NumberFormatter.cs): explicit (non-General) number/date
/// formats had NO width-based '#' fallback at all -- an over-wide result was returned as-is and
/// silently clipped by the grid.
///
/// R41-render-number-overflow-hash-3-2 (NumberFormatter.General.cs): the General-format digit-fit
/// loop returned the narrowest (but still over-budget) candidate instead of falling back to '#'
/// when nothing fit the column's character budget.
///
/// R41-render-number-overflow-hash-3-3 (ViewportService.cs): a merged cell's number formatting
/// used only the anchor column's width, not the merged range's combined width, so a value that
/// should fit (or need less aggressive scientific-notation fallback) across several merged columns
/// was formatted as if confined to the single anchor column.
/// </summary>
public sealed class R41_NumberOverflowHashTests
{
    // ── 3-1: explicit (non-General) format width overflow ─────────────────────────────────────

    [Fact]
    public void ExplicitNumberFormat_TooWideForColumn_ShowsHashFillSizedToColumn()
    {
        // "123456.79" (9 chars) does not fit a 4-character-wide column -- Excel shows "####".
        NumberFormatter.Format(new NumberValue(123456.789), "0.00", 4)
            .Should().Be("####");
    }

    [Fact]
    public void ExplicitNumberFormat_FitsColumn_IsUnchanged()
    {
        // Sibling/no-regression case: the same format/value with a column wide enough to hold the
        // formatted text must render exactly as before (no spurious '#').
        NumberFormatter.Format(new NumberValue(123456.789), "0.00", 20)
            .Should().Be("123456.79");
    }

    [Fact]
    public void ExplicitNumberFormat_WithoutColumnWidthContext_NeverOverflowsToHash()
    {
        // Callers with no column-width context (TEXT()/formula-bar evaluation) must be completely
        // unaffected -- there is no column to compare against, so the full text is always returned.
        NumberFormatter.Format(new NumberValue(123456.789), "0.00")
            .Should().Be("123456.79");
    }

    [Fact]
    public void ExplicitDateFormat_TooWideForColumn_ShowsHashFillSizedToColumn()
    {
        // A long explicit date format squeezed into a narrow column must also fall back to '#',
        // matching the same Excel behavior as narrow numeric columns.
        var text = NumberFormatter.Format(new DateTimeValue(45000.0), "dddd, mmmm d, yyyy", 5);
        text.Should().Be("#####");
    }

    [Fact]
    public void TextValue_TooWideForColumn_NeverShowsHash()
    {
        // Text cells overflow into neighboring blank cells in Excel -- they must NEVER be replaced
        // with '#', regardless of how narrow the target column is.
        NumberFormatter.Format(new TextValue("a very long piece of text"), "@", 3)
            .Should().Be("a very long piece of text");
    }

    // ── 3-2: General-format digit-fit loop over-budget fallback ────────────────────────────────

    [Fact]
    public void GeneralFormat_LargeIntegerNarrowerThanEvenScientificNotation_FallsBackToHash()
    {
        // At width 1 (digitBudget = 1 + 3 = 4), even the narrowest General candidate for
        // 123456789012345 ("1E+14", 5 chars via G1) still exceeds the budget -- Excel shows '#'
        // rather than the still-too-wide scientific-notation text.
        NumberFormatter.Format(new NumberValue(123456789012345d), "General", 1)
            .Should().Be("#");
    }

    [Fact]
    public void GeneralFormat_ValueThatFitsAtNarrowWidth_IsUnchanged()
    {
        // Sibling/no-regression case: a value whose scientific-notation form DOES fit the budget
        // must still render normally, not fall back to '#'.
        NumberFormatter.Format(new NumberValue(1e14), "General", 8)
            .Should().Be("1E+14");
    }

    // ── 3-3: merged-cell width awareness (ViewportService) ─────────────────────────────────────

    [Fact]
    public void MergedCell_NumberFormatting_UsesCombinedMergedWidthNotJustAnchorColumn()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // Three narrow columns (Excel width 2 units each -> 19px each -> EstimateCharacterWidth 2).
        sheet.ColumnWidths[1] = 2;
        sheet.ColumnWidths[2] = 2;
        sheet.ColumnWidths[3] = 2;

        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(123456789));

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        var anchorCell = viewport.Cells.Should().ContainSingle(c => c.Row == 1 && c.Col == 1).Subject;

        // Bug: a single anchor column (width 2, digitBudget 5) can only fit "1E+08" (5 chars).
        // Fixed: the merged A1:C1 combined width (57px -> EstimateCharacterWidth 7, digitBudget 10)
        // fits the full 9-digit "123456789", matching what real Excel would display for the same
        // merged width.
        anchorCell.DisplayText.Should().Be("123456789");
    }

    [Fact]
    public void UnmergedCell_SameNarrowColumnWidth_StillFallsBackToScientificNotation()
    {
        // Sibling/no-regression case: without a merge, the same value in the same single narrow
        // column must keep using ONLY that column's own width (unaffected by the merge-width fix).
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 2;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(123456789));

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        var cell = viewport.Cells.Should().ContainSingle(c => c.Row == 1 && c.Col == 1).Subject;
        cell.DisplayText.Should().Be("1E+08");
    }

    [Fact]
    public void MergedCell_ValueThatFitsEvenTheAnchorColumnAlone_IsUnchanged()
    {
        // Sibling/no-regression case: a merge whose value already fits the anchor column alone
        // must render identically whether or not the merge-width widening applies.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 20;
        sheet.ColumnWidths[2] = 20;

        sheet.AddMergedRegion(new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 2)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 400, 400));

        var anchorCell = viewport.Cells.Should().ContainSingle(c => c.Row == 1 && c.Col == 1).Subject;
        anchorCell.DisplayText.Should().Be("42");
    }
}
