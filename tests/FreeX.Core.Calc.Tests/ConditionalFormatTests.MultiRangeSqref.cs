using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    /// <summary>
    /// Verifies that a CF rule with two non-contiguous ranges (multi-range sqref)
    /// applies the format to cells in BOTH ranges, not just the first one.
    /// </summary>
    [Fact]
    public void MultiRangeSqref_RuleAppliesToCellsInSecondRange()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        var sheetId = sheet.Id;

        // Place values in both ranges: A1:A5 and C1:C5
        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheetId, row, 1), Cell.FromValue(new NumberValue(10)));
            sheet.SetCell(new CellAddress(sheetId, row, 3), Cell.FromValue(new NumberValue(10)));
        }

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };

        // CF rule covers A1:A5 AND C1:C5 via AdditionalRanges
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 5, 1)),
            AdditionalRanges =
            [
                new GridRange(
                    new CellAddress(sheetId, 1, 3),
                    new CellAddress(sheetId, 5, 3))
            ],
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Add a value at B1 so the cell appears in the viewport (helps with the "not affected" check)
        sheet.SetCell(new CellAddress(sheetId, 1, 2), Cell.FromValue(new NumberValue(10)));

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert – cells in first range (col 1 = A) get the red fill
        var a1 = GetCell(vp, 1, 1);
        a1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0),
            "A1=10 > 5, so the rule must apply to the first range");

        // Assert – cells in second range (col 3 = C) also get the red fill
        var c1 = GetCell(vp, 1, 3);
        c1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0),
            "C1=10 > 5, so the rule must apply to the second (additional) range too");

        // Sanity – a cell in neither range is not affected (B1 has value=10 but is not in the CF ranges)
        var b1 = GetCell(vp, 1, 2);
        b1.Style?.FillColor.Should().NotBe(new CellColor(255, 0, 0),
            "B1 is not in any range covered by this rule");
    }

    /// <summary>
    /// Verifies that a single-range rule (AdditionalRanges == null) still works correctly
    /// so the multi-range change does not regress the common case.
    /// </summary>
    [Fact]
    public void SingleRangeSqref_RuleAppliesToOnlyThatRange_Regression()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        var sheetId = sheet.Id;

        sheet.SetCell(new CellAddress(sheetId, 1, 1), Cell.FromValue(new NumberValue(10)));
        // B1: data present so the cell appears in the viewport, but it is NOT in the CF range
        sheet.SetCell(new CellAddress(sheetId, 1, 2), Cell.FromValue(new NumberValue(10)));

        var blueStyle = new CellStyle { FillColor = new CellColor(0, 0, 255) };

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheetId, 1, 1),
                new CellAddress(sheetId, 1, 1)),
            // AdditionalRanges intentionally null (single-range, common case)
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = blueStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert
        var a1 = GetCell(vp, 1, 1);
        a1.Style!.FillColor.Should().Be(new CellColor(0, 0, 255),
            "A1 is in the rule's range and value > 5");

        var b1 = GetCell(vp, 1, 2);
        b1.Style?.FillColor.Should().NotBe(new CellColor(0, 0, 255),
            "B1 is not in the rule's range; single-range rules must not bleed");
    }
}
