using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void DuplicateValues_TreatsNegativeZeroAsDuplicateOfZero()
    {
        // Regression: GetDuplicateValueKey keyed numbers by n.Value.ToString("R"), which
        // renders 0 as "0" and -0.0 as "-0" -- two different bucket keys for the same Excel
        // value, so a cell holding -0.0 was never flagged as a duplicate of a plain 0.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0.0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(-0.0)));

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132),
            "0 and -0.0 are the same Excel value and must both be flagged as duplicates");
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132),
            "0 and -0.0 are the same Excel value and must both be flagged as duplicates");
    }

    [Fact]
    public void DuplicateValues_SiblingCase_DistinctNumbersAreNotFlagged()
    {
        // Sibling case: genuinely different numbers must not be conflated as duplicates.
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0.0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(1.0)));

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
    }
}
