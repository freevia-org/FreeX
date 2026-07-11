using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R27-information-functions-deep-1: CELL("format") must recognize the built-in plain
/// 24-hour "h:mm" time format as "D9" (Excel's documented format-code table), not fall
/// through to the "G" default.
///
/// R27-information-functions-deep-3: CELL("color")/CELL("format") must not misdetect a
/// locale-currency bracket tag whose symbol is not a plain "$" (e.g. "[$£-809]",
/// "[$€-407]") as a color token -- any bracketed token starting with '$' is an OOXML
/// locale/currency tag, never a color specifier.
/// </summary>
public sealed class R27_InformationCellFormatColorTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) MakeWb(ScalarValue value, string numberFormat)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), value);
        var styleId = wb.RegisterStyle(new CellStyle { NumberFormat = numberFormat });
        sheet.GetCell(1, 1)!.StyleId = styleId;
        return (wb, sheet);
    }

    // ── CELL("format") h:mm => D9 (bug case) + existing siblings still correct ────────

    [Theory]
    [InlineData("h:mm", "D9")]
    [InlineData("h:mm:ss", "D8")]
    [InlineData("h:mmAM/PM", "D7")]
    [InlineData("h:mm:ssAM/PM", "D6")]
    public void Cell_Format_RecognizesTimeFormatCodes(string numberFormat, string expected)
    {
        var (wb, sheet) = MakeWb(new NumberValue(0.5), numberFormat);
        _eval.Evaluate("=CELL(\"format\",A1)", sheet, wb).Should().Be(new TextValue(expected));
    }

    // ── CELL("color") / CELL("format") locale-currency tags are not colors (bug case) ──

    [Theory]
    [InlineData("[$£-809]#,##0.00;([$£-809]#,##0.00)")]
    [InlineData("[$€-407]#,##0.00;([$€-407]#,##0.00)")]
    [InlineData("[$-409]#,##0.00;([$-409]#,##0.00)")]
    public void Cell_Color_LocaleCurrencyTagIsNotColor(string numberFormat)
    {
        var (wb, sheet) = MakeWb(new NumberValue(-12), numberFormat);
        _eval.Evaluate("=CELL(\"color\",A1)", sheet, wb).Should().Be(new NumberValue(0));
    }

    [Theory]
    [InlineData("#,##0;[$£-809]-#,##0", ",0")]
    [InlineData("#,##0;[Red]-#,##0", ",0-")]
    public void Cell_Format_OnlyGenuineColorAppendsSuffix(string numberFormat, string expected)
    {
        // Sibling comparison: a real named-color negative section ("[Red]") still gets the
        // "-" suffix, but a locale-currency tag with the same bracket shape does not.
        var (wb, sheet) = MakeWb(new NumberValue(-12), numberFormat);
        _eval.Evaluate("=CELL(\"format\",A1)", sheet, wb).Should().Be(new TextValue(expected));
    }

    // ── Sibling case: a genuine named-color negative section still reports color=1 ────

    [Theory]
    [InlineData("#,##0;[Red]-#,##0", 1)]
    [InlineData("#,##0;[Color10](#,##0)", 1)]
    [InlineData("#,##0;[<=-100]#,##0", 0)]
    [InlineData("#,##0;-#,##0", 0)]
    public void Cell_Color_StillReportsGenuineNamedColors(string numberFormat, double expected)
    {
        var (wb, sheet) = MakeWb(new NumberValue(-12), numberFormat);
        _eval.Evaluate("=CELL(\"color\",A1)", sheet, wb).Should().Be(new NumberValue(expected));
    }
}
