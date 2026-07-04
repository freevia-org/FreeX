using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for review-wave group J-cf-eval:
///  - G14: the CF formula fast-path simple-comparison must apply Excel's blank-coercion
///    rule (blank = 0 / empty-string / FALSE) the same way the general evaluator does.
///  - G29: Icon Set rules must test membership across AdditionalRanges, not just AppliesTo.
/// </summary>
public partial class ConditionalFormatTests
{
    [Fact]
    public void Formula_Rule_SimpleComparison_BlankCellCoercesToZero_MatchingGeneralEvaluator()
    {
        var (wb, sheet) = MakeWorkbook();
        // A1 is intentionally left blank (no cell entry at all). The rule is anchored on B1 —
        // a cell that exists — because the viewport only materializes DisplayCells for occupied
        // cells (a CF hit on a fully blank cell does not create one; separate known limitation).
        // The coercion under test is the COMPARISON's: $A1 evaluated from anchor B1 targets the
        // blank A1, which must coerce to 0 exactly like the general evaluator does.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(1)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 1, 2)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            // Simple top-level comparison — routes through the CF fast path (MatchesSimpleComparison).
            FormulaText = "$A1=0",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 2).Style!.FillColor.Should().Be(
            new CellColor(255, 0, 0),
            "Excel coerces a blank cell to 0 for a numeric comparison, so =$A1=0 is TRUE while A1 is empty");
    }

    [Fact]
    public void Formula_Rule_SimpleComparison_BlankCellCoercesToEmptyString()
    {
        var (wb, sheet) = MakeWorkbook();
        // A1 is intentionally left blank; rule anchored on occupied B1 (see the coerces-to-zero
        // test for why the anchor cell must exist).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(1)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 1, 2)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "$A1=\"\"",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 2).Style!.FillColor.Should().Be(
            new CellColor(255, 0, 0),
            "Excel coerces a blank cell to an empty string for a text comparison, so =$A1=\"\" is TRUE while A1 is empty");
    }

    [Fact]
    public void Formula_Rule_SimpleComparison_NonBlankZeroCellStillMatchesEqualsZero()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "$A1=0",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(
            new CellColor(255, 0, 0),
            "an explicit zero value must still satisfy =$A1=0 regardless of the blank-coercion fix");
    }

    [Fact]
    public void Formula_Rule_SimpleComparison_BlankCellNotEqualToNonZero()
    {
        var (wb, sheet) = MakeWorkbook();
        // A1 is intentionally left blank; rule anchored on occupied B1 (see the coerces-to-zero
        // test for why the anchor cell must exist).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(1)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 2),
                new CellAddress(sheet.Id, 1, 2)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "$A1<>0",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 2).Style?.FillColor.Should().NotBe(
            new CellColor(255, 0, 0),
            "blank coerces to 0, so =$A1<>0 must be FALSE while A1 is empty");
    }

    [Fact]
    public void IconSet_Rule_AppliesAcrossAdditionalRanges_NotJustFirstRange()
    {
        var (wb, sheet) = MakeWorkbook();
        var sheetId = sheet.Id;

        // Place matching values in both ranges: A1:A3 and C1:C3.
        var values = new[] { 10, 50, 90 };
        for (uint row = 1; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheetId, row, 1), Cell.FromValue(new NumberValue(values[row - 1])));
            sheet.SetCell(new CellAddress(sheetId, row, 3), Cell.FromValue(new NumberValue(values[row - 1])));
        }

        // B column: present so viewport contains the cell, but not covered by the rule.
        sheet.SetCell(new CellAddress(sheetId, 1, 2), Cell.FromValue(new NumberValue(10)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 3, 1)),
            AdditionalRanges =
            [
                new GridRange(
                    new CellAddress(sheetId, 1, 3),
                    new CellAddress(sheetId, 3, 3))
            ],
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // First range (A column) gets icons as before.
        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 0, 3, true));
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 1, 3, true));
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 2, 3, true));

        // Second (additional) range (C column) must also receive icons.
        GetCell(vp, 1, 3).ConditionalIcon.Should().Be(
            new ConditionalFormatIcon("3TrafficLights1", 0, 3, true),
            "icon-set rules must render across AdditionalRanges, not just AppliesTo");
        GetCell(vp, 2, 3).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 1, 3, true));
        GetCell(vp, 3, 3).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 2, 3, true));

        // Sanity: a cell outside both ranges gets no icon from this rule.
        GetCell(vp, 1, 2).ConditionalIcon.Should().BeNull();
    }
}
