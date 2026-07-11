using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for two round-26 findings in BuiltInFunctions.TextValueText.cs's strict
/// (format 1) branch of VALUETOTEXT/ARRAYTOTEXT:
///
/// R26-text-functions-modern-deep-1: the strict-mode switch called
/// n.Value.ToString(CultureInfo.InvariantCulture) / d.Value.ToString(CultureInfo.InvariantCulture)
/// directly on the raw double instead of routing through NumberToExcelText's Excel-General
/// "G15" rounding (the same helper the concise/format-0 path already uses via ToText). This
/// produced up to 17-significant-digit .NET round-trip text (e.g. "0.30000000000000004")
/// instead of Excel's 15-significant-digit General text ("0.3"). Fixed by calling
/// NumberToExcelText for both NumberValue and DateTimeValue in the strict arm.
///
/// R26-text-functions-modern-deep-2: the strict-mode switch mapped BlankValue to
/// QuoteValueText("") -- the two-character string `""` -- instead of Excel's documented
/// behavior of contributing genuinely empty text for a blank cell (e.g. ARRAYTOTEXT(A1:A2,1)
/// for A1="Automobile", A2 blank returns {"Automobile",}, not {"Automobile",""}). Fixed by
/// mapping BlankValue to "" directly (no quoting) in the strict arm.
/// </summary>
public sealed class R26_ValueToTextArrayToTextStrictModeTests
{
    private readonly FormulaEvaluator _eval = new();

    // 45292.0 + 1.0/3.0 == "45292.333333333336" via plain ToString(InvariantCulture) (17
    // significant digits), but "45292.3333333333" via the Excel-General G15 rule.
    private const double FractionalSerial = 45292.0 + 1.0 / 3.0;
    private const string ExpectedG15DateText = "45292.3333333333";

    [Theory]
    // Bug case: 0.1+0.2 has a non-terminating binary fraction; strict mode must round through
    // Excel's 15-significant-digit General rule, not print .NET's raw 17-digit round-trip text.
    [InlineData("=VALUETOTEXT(0.1+0.2,1)", "0.3")]
    [InlineData("=ARRAYTOTEXT(1/3,1)", "{0.333333333333333}")]
    // Sibling already-working cases: concise mode (format 0) already routed through
    // NumberToExcelText and must keep doing so unchanged.
    [InlineData("=VALUETOTEXT(0.1+0.2,0)", "0.3")]
    [InlineData("=ARRAYTOTEXT(1/3,0)", "0.333333333333333")]
    // Sibling already-working case: a number whose G15 text is exact and short must keep
    // rendering identically in both formats (no regression from touching the strict branch).
    [InlineData("=VALUETOTEXT(1234.01234,1)", "1234.01234")]
    public void ValueToTextArrayToText_StrictMode_UsesG15NumberFormatting(string formula, string expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void ValueToText_StrictMode_DateTimeValue_MatchesConciseG15Text()
    {
        var sheet = Sheet((1, 1, new DateTimeValue(FractionalSerial)));

        // Bug case: strict mode must apply the same G15 rounding as concise mode for dates.
        _eval.Evaluate("=VALUETOTEXT(A1,1)", sheet).Should().Be(new TextValue(ExpectedG15DateText));
        // Sibling already-working case: concise mode is unaffected.
        _eval.Evaluate("=VALUETOTEXT(A1,0)", sheet).Should().Be(new TextValue(ExpectedG15DateText));
    }

    [Fact]
    public void ArrayToText_StrictMode_BlankCell_ContributesEmptyTextNotQuotedEmptyString()
    {
        // Documented Microsoft example: ARRAYTOTEXT({"Automobile",""}, 1) with the second
        // element a genuinely blank cell (not an empty-string literal) returns {"Automobile",}.
        // A1:B1 is a single-row, two-column range so entries are comma-joined (not
        // semicolon-joined, which is reserved for row breaks).
        var sheet = Sheet((1, 1, new TextValue("Automobile")));

        _eval.Evaluate("=ARRAYTOTEXT(A1:B1,1)", sheet)
            .Should().Be(new TextValue("{\"Automobile\",}"));
    }

    [Fact]
    public void ValueToText_StrictMode_BlankCell_ReturnsEmptyTextNotQuotedEmptyString()
    {
        var sheet = Sheet((1, 1, new TextValue("Automobile")));

        // Bug case: a single blank-cell reference in strict mode must return empty text, not
        // the two-character string `""`.
        _eval.Evaluate("=VALUETOTEXT(A2,1)", sheet).Should().Be(new TextValue(""));
        // Sibling already-working case: concise mode already returns empty text for a blank.
        _eval.Evaluate("=VALUETOTEXT(A2,0)", sheet).Should().Be(new TextValue(""));
        // Sibling already-working case: a non-blank text value must still be quoted in strict
        // mode (the blank fix must not affect ordinary text quoting).
        _eval.Evaluate("=VALUETOTEXT(A1,1)", sheet).Should().Be(new TextValue("\"Automobile\""));
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }
}
