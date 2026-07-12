using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Covers round-34 regex-lens findings:
///  - R34-formula-text-modern-2-2: REGEXEXTRACT(range, pattern, return_mode 1/2) over a MULTI-cell
///    text range must not nest a RangeValue inside a RangeValue cell; it must surface #CALC!
///    (matching real Excel), while single-cell + return_mode 0 keep working exactly as before.
///  - R34-formula-text-modern-2-1: named-group syntax translation ((?P&lt;name&gt;...) -> (?&lt;name&gt;...))
///    and rejection of RE2-unsupported backreferences/lookaround with #VALUE!.
/// </summary>
public sealed class RegexFunctionsRe2CompatAndArrayGuardTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void RegexExtract_MultiCellRangeWithAllMatchesMode_ReturnsCalcInsteadOfNestedArray()
    {
        var sheet = Sheet(
            (1, 1, new TextValue("A1 B22 C333")),
            (2, 1, new TextValue("X1")));

        var result = _eval.Evaluate("=REGEXEXTRACT(A1:A2,\"[0-9]+\",1)", sheet);

        // Must be the #CALC! error, never a RangeValue (which would mean a nested
        // RangeValue got embedded verbatim into an outer cell -- the corruption bug).
        result.Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void RegexExtract_MultiCellRangeWithCaptureGroupsMode_ReturnsCalcInsteadOfNestedArray()
    {
        var sheet = Sheet(
            (1, 1, new TextValue("Ada Lovelace")),
            (2, 1, new TextValue("Grace Hopper")));

        var result = _eval.Evaluate("=REGEXEXTRACT(A1:A2,\"(\\w+)\\s+(\\w+)\",2)", sheet);

        result.Should().Be(ErrorValue.Calc);
    }

    [Fact]
    public void RegexExtract_MultiCellRangeWithFirstMatchMode_StillSpillsNormally()
    {
        // return_mode 0 (first match, the default/scalar-per-cell shape) is NOT the nested-array
        // case -- each cell's result is a plain TextValue, so this sibling case must keep working.
        var sheet = Sheet(
            (1, 1, new TextValue("Order SO-12345")),
            (2, 1, new TextValue("Order SO-67890")));

        AssertColumn(
            _eval.Evaluate("=REGEXEXTRACT(A1:A2,\"SO-[0-9]+\",0)", sheet),
            new TextValue("SO-12345"),
            new TextValue("SO-67890"));
    }

    [Fact]
    public void RegexExtract_SingleCellLiteralWithAllMatchesMode_StillSpillsAsColumn()
    {
        // Pre-existing, already-covered sibling case (scalar text argument, not a range) must be
        // unaffected by the new multi-cell guard.
        AssertColumn(
            _eval.Evaluate("=REGEXEXTRACT(\"A1 B22 C333\",\"[0-9]+\",1)", Sheet()),
            new TextValue("1"),
            new TextValue("22"),
            new TextValue("333"));
    }

    [Fact]
    public void RegexExtract_PythonStyleNamedGroups_AreTranslatedAndExtractGroupValues()
    {
        var result = _eval.Evaluate("=REGEXEXTRACT(\"2024-01\",\"(?P<y>[0-9]+)-(?P<m>[0-9]+)\",2)", Sheet())
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(2);
        result.At(1, 1).Should().Be(new TextValue("2024"));
        result.At(1, 2).Should().Be(new TextValue("01"));
    }

    [Fact]
    public void RegexTest_PythonStyleNamedGroup_MatchesLikeDotNetNamedGroup()
    {
        _eval.Evaluate("=REGEXTEST(\"abc123\",\"(?P<digits>[0-9]+)\")", Sheet())
            .Should().Be(new BoolValue(true));
    }

    [Theory]
    [InlineData("=REGEXTEST(\"abcabc\",\"(abc)\\1\")")]
    [InlineData("=REGEXEXTRACT(\"abcabc\",\"(abc)\\1\")")]
    [InlineData("=REGEXREPLACE(\"abcabc\",\"(abc)\\1\",\"x\")")]
    public void RegexFunctions_Backreference_ReturnsValueLikeRe2(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=REGEXTEST(\"foobar\",\"foo(?=bar)\")")]
    [InlineData("=REGEXTEST(\"foobar\",\"foo(?!baz)\")")]
    [InlineData("=REGEXTEST(\"foobar\",\"(?<=foo)bar\")")]
    [InlineData("=REGEXTEST(\"foobar\",\"(?<!baz)bar\")")]
    public void RegexFunctions_Lookaround_ReturnsValueLikeRe2(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void RegexFunctions_DigitEscapesLikeSlashDAreNotMistakenForBackreferences()
    {
        // \d, \s, \w etc. must not trip the backreference-rejection heuristic (only \1-\9 do).
        _eval.Evaluate("=REGEXTEST(\"abc 123\",\"\\d+\")", Sheet()).Should().Be(new BoolValue(true));
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }

    private static void AssertColumn(ScalarValue value, params ScalarValue[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (var i = 0; i < expected.Length; i++)
            range.At(i + 1, 1).Should().Be(expected[i]);
    }
}
