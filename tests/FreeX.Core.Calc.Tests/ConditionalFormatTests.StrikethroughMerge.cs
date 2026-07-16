using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R46-io-dxf-differential-format-2-1: a CF dxf that sets ONLY strikethrough (a common
/// "to-do list" pattern: strike through A1's text when B1 is checked) must actually strike
/// the text through in the merged style. Both merge paths - <c>MergeStyles</c> (single-rule
/// fast path) and <c>StackDifferentialStyle</c> (multi-rule stacking path) - must copy
/// <see cref="CellStyle.Strikethrough"/> from the CF style onto the result exactly like they
/// already do for Bold/Italic/Underline.
/// </summary>
public partial class ConditionalFormatTests
{
    [Fact]
    public void MergeStyles_CfStrikethroughOnly_AppliesToNonStruckBaseCell()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { Strikethrough = false, FillColor = new CellColor(200, 200, 200) };
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
            // <font><strike/></font>: Strikethrough=true, everything else default/unset.
            FormatIfTrue = new CellStyle { Strikethrough = true },
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.Strikethrough.Should().BeTrue("Excel strikes the text through when a matching CF dxf sets strike");
        // The base fill must be untouched since the CF rule doesn't specify a fill.
        style.FillColor.Should().Be(new CellColor(200, 200, 200), "base fill preserved when CF has none");
    }

    // Sibling no-regression case: a CF rule that does NOT set strikethrough must never turn
    // strikethrough on, and a base cell that IS already struck-through keeps that formatting
    // when the (non-strikethrough) CF rule matches and only touches an unrelated attribute.
    [Fact]
    public void MergeStyles_CfWithoutStrikethrough_LeavesStrikethroughUnaffected()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { Strikethrough = true };
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

        style.Strikethrough.Should().BeTrue("CF rule doesn't mention strikethrough - base formatting must survive");
        style.FillColor.Should().Be(new CellColor(255, 199, 206));
    }

    // Multi-rule stacking path (StackDifferentialStyle): a lower-priority strikethrough-only
    // rule must still apply on top of a higher-priority rule's fill, exactly like Bold does.
    [Fact]
    public void ConditionalFormats_StackedStrikethroughOnlyRule_AppliesOnTopOfEarlierRule()
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
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 199, 206) }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Strikethrough = true }
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.Strikethrough.Should().BeTrue("stacked lower-priority strikethrough-only rule must still apply");
        style.FillColor.Should().Be(new CellColor(255, 199, 206));
    }
}
