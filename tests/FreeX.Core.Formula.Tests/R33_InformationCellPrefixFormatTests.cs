using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R33-formula-information-cell-1: CELL("prefix") must return "'" for a General-aligned
/// TEXT cell (Excel left-justifies General text and reports the apostrophe label prefix)
/// and "\" for Fill alignment -- not "" for either. General-aligned numbers/blanks still
/// report "" since Excel right-justifies/has-no-label for those.
///
/// R33-formula-information-cell-2: CELL("format") must not leak a stray character from a
/// "_x"/"*x" padding-escape (e.g. the ")" in "$#,##0.00_);($#,##0.00)") into the normalized
/// format used for the exact-match lookup -- both '_' and '*' consume the character that
/// follows as a non-literal argument, so that char must be skipped too.
/// </summary>
public sealed class R33_InformationCellPrefixFormatTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) MakeWb(ScalarValue value, HorizontalAlignment? alignment = null, string? numberFormat = null)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), value);
        var style = new CellStyle();
        if (alignment is not null) style.HorizontalAlignment = alignment.Value;
        if (numberFormat is not null) style.NumberFormat = numberFormat;
        var styleId = wb.RegisterStyle(style);
        sheet.GetCell(1, 1)!.StyleId = styleId;
        return (wb, sheet);
    }

    // ── CELL("prefix") General alignment (bug case: text) + sibling (number) ──────────

    [Fact]
    public void Cell_Prefix_GeneralAlignedText_ReturnsApostrophe()
    {
        var (wb, sheet) = MakeWb(new TextValue("hello"), HorizontalAlignment.General);
        _eval.Evaluate("=CELL(\"prefix\",A1)", sheet, wb).Should().Be(new TextValue("'"));
    }

    [Fact]
    public void Cell_Prefix_GeneralAlignedNumber_ReturnsEmpty()
    {
        var (wb, sheet) = MakeWb(new NumberValue(42), HorizontalAlignment.General);
        _eval.Evaluate("=CELL(\"prefix\",A1)", sheet, wb).Should().Be(new TextValue(""));
    }

    [Fact]
    public void Cell_Prefix_GeneralAlignedBlank_ReturnsEmpty()
    {
        var (wb, sheet) = MakeWb(BlankValue.Instance, HorizontalAlignment.General);
        _eval.Evaluate("=CELL(\"prefix\",A1)", sheet, wb).Should().Be(new TextValue(""));
    }

    // ── CELL("prefix") Fill alignment (bug case) ──────────────────────────────────────

    [Fact]
    public void Cell_Prefix_FillAlignment_ReturnsBackslash()
    {
        var (wb, sheet) = MakeWb(new TextValue("ab"), HorizontalAlignment.Fill);
        _eval.Evaluate("=CELL(\"prefix\",A1)", sheet, wb).Should().Be(new TextValue("\\"));
    }

    // ── CELL("prefix") existing Left/Center/Right still correct (siblings) ────────────

    [Theory]
    [InlineData(HorizontalAlignment.Left, "'")]
    [InlineData(HorizontalAlignment.Center, "^")]
    [InlineData(HorizontalAlignment.Right, "\"")]
    public void Cell_Prefix_ExplicitAlignment_StillCorrect(HorizontalAlignment alignment, string expected)
    {
        var (wb, sheet) = MakeWb(new TextValue("x"), alignment);
        _eval.Evaluate("=CELL(\"prefix\",A1)", sheet, wb).Should().Be(new TextValue(expected));
    }

    // ── CELL("format") padding-escape leak (bug case: built-in Currency numFmtId 7) ───

    [Fact]
    public void Cell_Format_CurrencyWithPaddingEscape_ReturnsC2()
    {
        var (wb, sheet) = MakeWb(new NumberValue(-12.5), numberFormat: "$#,##0.00_);($#,##0.00)");
        _eval.Evaluate("=CELL(\"format\",A1)", sheet, wb).Should().Be(new TextValue("C2"));
    }

    // ── CELL("format") plain "0" still correct (sibling) ──────────────────────────────

    [Fact]
    public void Cell_Format_PlainZero_ReturnsF0()
    {
        var (wb, sheet) = MakeWb(new NumberValue(5), numberFormat: "0");
        _eval.Evaluate("=CELL(\"format\",A1)", sheet, wb).Should().Be(new TextValue("F0"));
    }
}
