using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R43-render-cell-fill-pattern-3-1 / -2: a matching CF fill must fully replace the base cell's
/// background in the merged style - a stale gradient fill or pattern hatch inherited from the
/// base cell's <see cref="CellStyle"/> must never survive alongside/over the CF's own fill,
/// exactly as Excel's "Fill color" conditional format replaces the entire cell background.
/// </summary>
public partial class ConditionalFormatTests
{
    private static CellGradientFill MakeGradientFill() => new()
    {
        Type = CellGradientFillType.Linear,
        Degree = 90,
        Stops =
        [
            new CellGradientStop(0.0, new CellColor(0, 0, 255)),
            new CellGradientStop(1.0, new CellColor(255, 255, 255)),
        ],
    };

    // R43-render-cell-fill-pattern-3-1: CF fill must clear a gradient-filled base cell's GradientFill.
    [Fact]
    public void MergeStyles_CfFillColor_ClearsBaseGradientFill()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { GradientFill = MakeGradientFill() };
        var styleId = wb.RegisterStyle(baseStyle);

        var cell = Cell.FromValue(new NumberValue(150));
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
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.FillColor.Should().Be(new CellColor(255, 0, 0), "CF fill color must win");
        style.GradientFill.Should().BeNull(
            "Excel's CF fill fully replaces the base cell's gradient - it must not keep rendering");
    }

    // Sibling no-regression case: a base cell with NO fill/gradient still gets the CF's fill,
    // and a base cell's gradient survives untouched when no CF rule matches.
    [Fact]
    public void MergeStyles_CfNotMatching_LeavesBaseGradientFillUntouched()
    {
        var (wb, sheet) = MakeWorkbook();
        var gradient = MakeGradientFill();
        var baseStyle = new CellStyle { GradientFill = gradient };
        var styleId = wb.RegisterStyle(baseStyle);

        var cell = Cell.FromValue(new NumberValue(5));
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
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.GradientFill.Should().Be(gradient, "no CF rule matched - base gradient must be preserved");
    }

    // R43-render-cell-fill-pattern-3-2: a plain solid CF fill (no patternType in the dxf, so
    // FillPatternStyle stays None/FillPatternColor stays null) must clear a base cell's hatch
    // pattern instead of leaving the old pattern hatch drawn on top of the new CF color.
    [Fact]
    public void MergeStyles_PlainCfFillColor_ClearsBasePatternHatch()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle
        {
            FillColor = new CellColor(255, 255, 255),
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillPatternColor = new CellColor(0, 0, 0),
        };
        var styleId = wb.RegisterStyle(baseStyle);

        var cell = Cell.FromValue(new NumberValue(150));
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
            Value1 = "100",
            // Mirrors what XlsxDifferentialStyleReader produces for a plain "Fill: solid red" CF
            // rule: patternType omitted -> FillPatternStyle stays None, fgColor consumed as
            // FillColor (not FillPatternColor) per XlsxDifferentialStyleReader.cs:99-108.
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(255, 0, 0),
                FillPatternStyle = CellFillPatternStyle.None,
                FillPatternColor = null,
            },
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.FillColor.Should().Be(new CellColor(255, 0, 0), "CF solid fill color must win");
        style.FillPatternStyle.Should().Be(CellFillPatternStyle.None,
            "Excel's plain CF fill replaces the base pattern - no hatch should survive");
        style.FillPatternColor.Should().BeNull(
            "the base cell's pattern foreground color must not leak through a plain CF fill");
    }

    // Sibling no-regression case: when the CF rule itself specifies a pattern, that pattern
    // (and its own color) must still be applied on top of the base cell.
    [Fact]
    public void MergeStyles_CfWithOwnPattern_AppliesCfPatternOverBaseFill()
    {
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { FillColor = new CellColor(255, 255, 255) };
        var styleId = wb.RegisterStyle(baseStyle);

        var cell = Cell.FromValue(new NumberValue(150));
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
            Value1 = "100",
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(255, 0, 0),
                FillPatternStyle = CellFillPatternStyle.LightGrid,
                FillPatternColor = new CellColor(0, 255, 0),
            },
        });

        var vp = GetViewport(wb, sheet);
        var style = GetCell(vp, 1, 1).Style!;

        style.FillColor.Should().Be(new CellColor(255, 0, 0));
        style.FillPatternStyle.Should().Be(CellFillPatternStyle.LightGrid);
        style.FillPatternColor.Should().Be(new CellColor(0, 255, 0));
    }
}
