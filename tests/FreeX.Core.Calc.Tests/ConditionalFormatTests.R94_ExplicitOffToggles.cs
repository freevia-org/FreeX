using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R94: a CF dxf that explicitly turns Bold/Italic/Underline/Strikethrough OFF (e.g. Format Cells >
/// Font > Font style = Regular, which OOXML serializes as <c>&lt;font&gt;&lt;b val="0"/&gt;&lt;/font&gt;</c>
/// in the dxf) must actually clear that attribute on an already-on base cell, exactly like Excel. Before
/// this fix, <c>MergeStyles</c>/<c>StackDifferentialStyle</c> only ever turned these four attributes ON
/// (<c>if (cfStyle.Bold) result.Bold = true;</c>), so an explicit dxf "off" - which
/// <see cref="FreeX.Core.IO.XlsxDifferentialStyleReader"/>'s reader deliberately distinguishes from
/// "dxf never mentions this attribute" - was silently discarded and the base formatting always won.
/// </summary>
public partial class ConditionalFormatTests
{
    [Fact]
    public void R94_MergeStyles_CfExplicitBoldOff_ClearsBoldOnBoldBaseCell()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { Bold = true };
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
            // <font><b val="0"/></font>: Bold=false AND DxfBold=false (explicit off), as opposed to a
            // dxf that never mentions <b> at all, which leaves DxfBold null.
            FormatIfTrue = new CellStyle { Bold = false, DxfBold = false },
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.Bold.Should().BeFalse(
            "Excel's CF format wins over the base format for every attribute the dxf specifies, including turning bold off");
    }

    // Sibling no-regression: a CF rule that does NOT mention Bold at all (DxfBold null, plain Bold
    // false, exactly what the reader produces for a dxf with no <b> child) must never clear an
    // already-bold base cell.
    [Fact]
    public void R94_MergeStyles_CfWithoutBoldElement_LeavesBoldBaseCellUnaffected()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { Bold = true };
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

        style.Bold.Should().BeTrue("CF rule doesn't mention bold - base formatting must survive");
        style.FillColor.Should().Be(new CellColor(255, 199, 206));
    }

    // Sibling no-regression: the pre-existing on-only case (CF dxf turns Italic ON over a non-italic
    // base cell) must still work exactly as before - the new tri-state plumbing must not regress the
    // simple "attribute not present on base, CF turns it on" path.
    [Fact]
    public void R94_MergeStyles_CfExplicitItalicOn_AppliesToNonItalicBaseCell()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { Italic = false };
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
            FormatIfTrue = new CellStyle { Italic = true, DxfItalic = true },
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.Italic.Should().BeTrue("Excel italicizes the text when a matching CF dxf sets italic on");
    }

    // Multi-rule stacking path (StackDifferentialStyle): the highest-priority rule that explicitly
    // decides Underline wins, exactly like the pre-existing "first matching rule wins" behavior for
    // borders/number-format - a lower-priority rule's explicit "turn underline off" must not undo a
    // higher-priority rule's explicit "turn underline on".
    [Fact]
    public void R94_StackDifferentialStyle_HigherPriorityExplicitUnderlineOn_WinsOverLowerPriorityOff()
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
            FormatIfTrue = new CellStyle { Underline = true, DxfUnderline = true }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 2,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            // Lower-priority rule explicitly turns underline OFF - must not win over rule 1's explicit on.
            FormatIfTrue = new CellStyle { Underline = false, DxfUnderline = false }
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.Underline.Should().BeTrue(
            "the higher-priority rule already explicitly decided underline on; a lower-priority explicit off must not override it");
    }

    // Multi-rule stacking path: a lower-priority rule's explicit "turn strikethrough off" DOES apply
    // when no higher-priority rule in the stack has touched strikethrough at all.
    [Fact]
    public void R94_StackDifferentialStyle_LowerPriorityExplicitStrikethroughOff_ClearsStruckBaseCell()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { Strikethrough = true };
        var styleId = wb.RegisterStyle(baseStyle);

        var address = new CellAddress(sheet.Id, 1, 1);
        var range = new GridRange(address, address);

        var cell = Cell.FromValue(new NumberValue(5));
        cell.StyleId = styleId;
        sheet.SetCell(address, cell);

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
            FormatIfTrue = new CellStyle { Strikethrough = false, DxfStrikethrough = false }
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.Strikethrough.Should().BeFalse(
            "rule 2 explicitly turns strikethrough off and no higher-priority rule touched it, so the already-struck base cell must un-strike");
        style.FillColor.Should().Be(new CellColor(255, 199, 206));
    }
}
