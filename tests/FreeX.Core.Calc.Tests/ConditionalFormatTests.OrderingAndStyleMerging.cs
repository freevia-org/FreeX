using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void ConditionalFormatContext_PreservesInsertionOrderForEqualPriorities()
    {
        var (wb, sheet) = MakeWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var range = new GridRange(address, address);

        sheet.SetCell(address, Cell.FromValue(new NumberValue(5)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(10, 20, 30) }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(200, 210, 220) }
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(
            new CellColor(10, 20, 30),
            "same-priority conditional formats should keep insertion order");
    }

    [Fact]
    public void ConditionalFormats_StackMatchingDifferentialStyles()
    {
        var (wb, sheet) = MakeWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var range = new GridRange(address, address);

        sheet.SetCell(address, Cell.FromValue(new NumberValue(5)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 199, 206) }
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.Bold.Should().BeTrue();
        style.FillColor.Should().Be(new CellColor(255, 199, 206));
    }

    [Fact]
    public void ConditionalFormats_StopIfTruePreventsLowerPriorityStyles()
    {
        var (wb, sheet) = MakeWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var range = new GridRange(address, address);

        sheet.SetCell(address, Cell.FromValue(new NumberValue(5)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            StopIfTrue = true,
            FormatIfTrue = new CellStyle { Bold = true }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 199, 206) }
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.Bold.Should().BeTrue();
        style.FillColor.Should().NotBe(new CellColor(255, 199, 206));
    }

    [Fact]
    public void MergeStyles_CfBoldOverridesBaseStyle()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { Bold = false, FillColor = new CellColor(200, 200, 200) };
        var styleId   = wb.RegisterStyle(baseStyle);

        var cell = Cell.FromValue(new NumberValue(99));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var boldStyle = new CellStyle { Bold = true };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.GreaterThan,
            Value1       = "0",
            FormatIfTrue = boldStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert
        var a1 = GetCell(vp, 1, 1);
        a1.Style!.Bold.Should().BeTrue("CF bold overrides base non-bold");
        // Base fill should be preserved (CF style has no fill)
        a1.Style!.FillColor.Should().Be(new CellColor(200, 200, 200), "base fill preserved when CF has none");
    }

    [Fact]
    public void ConditionalFormats_DefaultBaseStyleKeepsDifferentialMergeSemantics()
    {
        var (wb, sheet) = MakeWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, Cell.FromValue(new NumberValue(99)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(255, 199, 206),
                NumberFormat = "$0.00"
            }
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.FillColor.Should().Be(new CellColor(255, 199, 206));
        style.NumberFormat.Should().Be("General", "viewport CF style merging only applies supported differential style fields");
    }
}
