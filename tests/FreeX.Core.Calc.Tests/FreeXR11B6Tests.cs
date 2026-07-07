using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-11 fix bucket R6 focused regression tests.
/// </summary>
public partial class ConditionalFormatTests
{
    // ── R11-conditional-format-3: overlapping sqref ranges must not double-count cells ────────────

    /// <summary>
    /// A single rule whose sqref lists two overlapping ranges (A1:B2 and B2:C3, sharing cell B2)
    /// must treat the covered cell set as a set — B2's value counted once, not once per covering
    /// range. Values A1=1, B1=1, A2=1, B2=100: the correct average (over the 4 distinct cells) is
    /// (1+1+1+100)/4 = 25.75, so only B2 (100) is above average. The pre-fix bug double-counts B2,
    /// producing average (1+1+1+100+100)/5 = 40.6 — still only flagging B2 in this example, so we
    /// assert directly on the cached average via the AboveAverage matching outcome for a value
    /// between the two averages (30), which the bug would incorrectly flag as "above average" too.
    /// </summary>
    [Fact]
    public void OverlappingSqrefRanges_AboveAverage_DoesNotDoubleCountSharedCell()
    {
        var (wb, sheet) = MakeWorkbook();
        var sheetId = sheet.Id;

        sheet.SetCell(new CellAddress(sheetId, 1, 1), Cell.FromValue(new NumberValue(1)));   // A1
        sheet.SetCell(new CellAddress(sheetId, 1, 2), Cell.FromValue(new NumberValue(1)));   // B1
        sheet.SetCell(new CellAddress(sheetId, 2, 1), Cell.FromValue(new NumberValue(1)));   // A2
        sheet.SetCell(new CellAddress(sheetId, 2, 2), Cell.FromValue(new NumberValue(100))); // B2 (shared by both ranges)
        sheet.SetCell(new CellAddress(sheetId, 3, 3), Cell.FromValue(new NumberValue(30)));  // C3 (only in second range)

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };

        // sqref = "A1:B2 B2:C3" -- B2 is covered by BOTH ranges.
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 2, 2)),
            AdditionalRanges =
            [
                new GridRange(
                    new CellAddress(sheetId, 2, 2),
                    new CellAddress(sheetId, 3, 3))
            ],
            Priority = 1,
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = true,
            EqualAverage = false,
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // Correct (de-duplicated) cell set is {A1=1, B1=1, A2=1, B2=100, C3=30}: average = 133/5 = 26.6.
        // C3=30 > 26.6, so C3 IS above average with the correct de-duplicated computation.
        var c3 = GetCell(vp, 3, 3);
        c3.Style!.FillColor.Should().Be(new CellColor(255, 0, 0),
            "with B2 counted once, average=(1+1+1+100+30)/5=26.6 and C3=30 is above that average");

        // The double-counted (buggy) average would be (1+1+1+100+100+30)/6 = 38.83, under which
        // C3=30 would NOT be above average. This confirms de-duplication actually changed the result
        // (not just coincidentally matching either way) by checking B2 itself is still flagged.
        var b2 = GetCell(vp, 2, 2);
        b2.Style!.FillColor.Should().Be(new CellColor(255, 0, 0),
            "B2=100 is far above average under either computation, sanity-checking the rule fired at all");
    }

    /// <summary>
    /// Same overlapping-sqref shape but targeting the aggregate cache's raw average, isolating the
    /// double-counting root cause precisely: with values A1=1,B1=1,A2=1,B2=100 and sqref
    /// "A1:B2 B2:C3" where C3 is blank, the only distinct cells are A1,B1,A2,B2 — average must be
    /// (1+1+1+100)/4=25.75 exactly as Excel computes (sqref cell set, not multiset). A value of 30
    /// placed nowhere in the rule is not needed; instead we use a probe cell inside the overlap-free
    /// tail of the second range to observe the average via the AboveAverage cutoff.
    /// </summary>
    [Fact]
    public void OverlappingSqrefRanges_AverageMatchesDeduplicatedCellSet_NotMultiset()
    {
        var (wb, sheet) = MakeWorkbook();
        var sheetId = sheet.Id;

        sheet.SetCell(new CellAddress(sheetId, 1, 1), Cell.FromValue(new NumberValue(1)));   // A1
        sheet.SetCell(new CellAddress(sheetId, 1, 2), Cell.FromValue(new NumberValue(1)));   // B1
        sheet.SetCell(new CellAddress(sheetId, 2, 1), Cell.FromValue(new NumberValue(1)));   // A2
        sheet.SetCell(new CellAddress(sheetId, 2, 2), Cell.FromValue(new NumberValue(100))); // B2 shared cell
        // C2 = 26 sits strictly between the deduplicated average (25.75) and the buggy
        // double-counted average (40.6), so it discriminates between the two computations.
        sheet.SetCell(new CellAddress(sheetId, 2, 3), Cell.FromValue(new NumberValue(26)));  // C2

        var greenStyle = new CellStyle { FillColor = new CellColor(0, 255, 0) };

        // sqref = "A1:B2 B2:C3" -- B2 covered by both ranges; C2 only by the second.
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 2, 2)),
            AdditionalRanges =
            [
                new GridRange(
                    new CellAddress(sheetId, 2, 2),
                    new CellAddress(sheetId, 3, 3))
            ],
            Priority = 1,
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = true,
            EqualAverage = false,
            FormatIfTrue = greenStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // Deduplicated average = (1+1+1+100+26)/5 = 25.8 -> C2(26) IS above average.
        // Buggy double-counted average = (1+1+1+100+100+26)/6 = 38.17 -> C2(26) would NOT be above average.
        var c2 = GetCell(vp, 2, 3);
        c2.Style!.FillColor.Should().Be(new CellColor(0, 255, 0),
            "de-duplicated average=(1+1+1+100+26)/5=25.8, so C2=26 must be flagged as above average; " +
            "the pre-fix double-counted average (38.17) would incorrectly leave C2 unflagged");
    }
}
