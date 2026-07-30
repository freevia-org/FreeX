using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R94: a CF dxf that explicitly sets font color to black (a legitimate, deliberately-authored choice
/// in Excel's CF color picker, e.g. <c>&lt;font&gt;&lt;color rgb="FF000000"/&gt;&lt;/font&gt;</c>) must
/// win stacking/base-style precedence exactly like any other explicit CF font color. Before this fix,
/// <c>MergeStyles</c>/<c>StackDifferentialStyle</c> used <c>FontColor != CellColor.Black</c> as their
/// "was a color actually specified" test - since <see cref="CellStyle.FontColor"/> defaults to
/// <see cref="CellColor.Black"/>, an explicit dxf black was indistinguishable from "dxf never mentions
/// font color at all" and was silently skipped. <see cref="FreeX.Core.IO.XlsxDifferentialStyleReader"/>
/// now also populates <see cref="CellStyle.DxfFontColor"/> (mirroring the existing DxfBold/DxfItalic/
/// DxfUnderline/DxfStrikethrough tri-state pattern) so an explicit black is distinguishable from unset.
/// </summary>
public partial class ConditionalFormatTests
{
    // Single-rule path (MergeStyles): a CF rule that explicitly sets font color to black must override
    // a red-fonted base cell, exactly as Excel does.
    [Fact]
    public void R94_MergeStyles_CfExplicitBlackFontColor_OverridesNonBlackBaseCell()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { FontColor = new CellColor(255, 0, 0) };
        var styleId = wb.RegisterStyle(baseStyle);

        var cell = Cell.FromValue(new NumberValue(1));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            // Mirrors what XlsxDifferentialStyleReader produces for a dxf whose ONLY child is
            // <font><color rgb="FF000000"/></font>: FontColor=Black AND DxfFontColor=Black (explicit),
            // as opposed to a dxf that never mentions <color> at all, which leaves DxfFontColor null.
            FormatIfTrue = new CellStyle { FontColor = CellColor.Black, DxfFontColor = CellColor.Black },
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.FontColor.Should().Be(CellColor.Black,
            "Excel's CF format wins over the base format for every attribute the dxf specifies, including an explicit choice of black");
    }

    // Sibling no-regression: a CF rule that does NOT mention font color at all (DxfFontColor null,
    // plain FontColor left at its Black default, exactly what the reader produces for a dxf with no
    // <color> child) must never stomp a non-black base cell's font color.
    [Fact]
    public void R94_MergeStyles_CfWithoutFontColorElement_LeavesNonBlackBaseCellUnaffected()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { FontColor = new CellColor(255, 0, 0) };
        var styleId = wb.RegisterStyle(baseStyle);

        var cell = Cell.FromValue(new NumberValue(1));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 199, 206) },
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.FontColor.Should().Be(new CellColor(255, 0, 0),
            "CF rule doesn't mention font color - base font color must survive");
        style.FillColor.Should().Be(new CellColor(255, 199, 206));
    }

    // Multi-rule stacking path (StackDifferentialStyle): the exact scenario from the Excel ground truth
    // - a higher-priority rule's explicit black must win over a lower-priority rule's explicit red,
    // since Excel applies the first (highest-priority) matching rule's decision per attribute.
    [Fact]
    public void R94_StackDifferentialStyle_HigherPriorityExplicitBlack_WinsOverLowerPriorityRed()
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
            // Priority-1 dxf sets font color explicitly to black and nothing else.
            FormatIfTrue = new CellStyle { FontColor = CellColor.Black, DxfFontColor = CellColor.Black }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            // Lower-priority dxf sets font color to red and nothing else - must not win over rule 1's
            // explicit black.
            FormatIfTrue = new CellStyle { FontColor = new CellColor(255, 0, 0), DxfFontColor = new CellColor(255, 0, 0) }
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.FontColor.Should().Be(CellColor.Black,
            "the higher-priority rule already explicitly decided font color black; a lower-priority explicit red must not override it");
    }

    // Multi-rule stacking path: a lower-priority rule's explicit color DOES apply when no
    // higher-priority rule in the stack touched font color at all.
    [Fact]
    public void R94_StackDifferentialStyle_LowerPriorityExplicitColor_AppliesWhenHigherPriorityRuleDidNotTouchFontColor()
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
            FormatIfTrue = new CellStyle { FontColor = new CellColor(255, 0, 0), DxfFontColor = new CellColor(255, 0, 0) }
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.Bold.Should().BeTrue();
        style.FontColor.Should().Be(new CellColor(255, 0, 0),
            "rule 1 never touched font color, so rule 2's explicit red must apply");
    }
}
