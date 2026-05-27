using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityModernTextTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=TEXTBEFORE(\"Little Red Riding Hood's red hood\",\"Red\")", "Little ")]
    [InlineData("=TEXTBEFORE(\"Little red Riding Hood's red hood\",\"red\",2)", "Little red Riding Hood's ")]
    [InlineData("=TEXTBEFORE(\"Little red Riding Hood's red hood\",\"red\",-2)", "Little ")]
    [InlineData("=TEXTBEFORE(\"Socrates\",\" \",,,1)", "Socrates")]
    [InlineData("=TEXTBEFORE(\"abc\",\"\",1)", "")]
    [InlineData("=TEXTBEFORE(\"abc\",\"\",-1)", "abc")]
    [InlineData("=TEXTAFTER(\"Little Red Riding Hood's red hood\",\"Red\")", " Riding Hood's red hood")]
    [InlineData("=TEXTAFTER(\"Little red Riding Hood's red hood\",\"red\",2)", " hood")]
    [InlineData("=TEXTAFTER(\"Little red Riding Hood's red hood\",\"red\",-2)", " Riding Hood's red hood")]
    [InlineData("=TEXTAFTER(\"Marcus Aurelius\",\" \",,,1)", "Aurelius")]
    [InlineData("=TEXTAFTER(\"abc\",\"\",1)", "abc")]
    [InlineData("=TEXTAFTER(\"abc\",\"\",-1)", "")]
    public void TextBeforeAfter_ReturnDocumentedExcelResults(string formula, string expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void TextBeforeAfter_SupportCaseInsensitiveSearchAndFallback()
    {
        _eval.Evaluate("=TEXTBEFORE(\"alpha-BETA-gamma\",\"beta\",,1)", Sheet())
            .Should().Be(new TextValue("alpha-"));
        _eval.Evaluate("=TEXTAFTER(\"alpha-BETA-gamma\",\"beta\",,1)", Sheet())
            .Should().Be(new TextValue("-gamma"));
        _eval.Evaluate("=TEXTBEFORE(\"alpha\",\"z\",,,,\"missing\")", Sheet())
            .Should().Be(new TextValue("missing"));
        _eval.Evaluate("=TEXTAFTER(\"alpha\",\"z\",,,,\"missing\")", Sheet())
            .Should().Be(new TextValue("missing"));
    }

    [Theory]
    [InlineData("=TEXTBEFORE(\"alpha\",\"z\")")]
    [InlineData("=TEXTAFTER(\"alpha\",\"z\")")]
    public void TextBeforeAfter_ReturnNaWhenDelimiterIsMissing(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.NA);
    }

    [Theory]
    [InlineData("=TEXTBEFORE(\"alpha\",\"a\",0)")]
    [InlineData("=TEXTAFTER(\"alpha\",\"a\",0)")]
    [InlineData("=TEXTBEFORE(\"alpha\",\"a\",6)")]
    [InlineData("=TEXTAFTER(\"alpha\",\"a\",6)")]
    [InlineData("=TEXTBEFORE(\"alpha\",\"a\",,2)")]
    [InlineData("=TEXTAFTER(\"alpha\",\"a\",,,2)")]
    public void TextBeforeAfter_ReturnValueForExcelArgumentDomainErrors(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void TextBeforeAfter_SpillOverTextRanges()
    {
        var sheet = Sheet(
            (1, 1, new TextValue("a-b")),
            (2, 1, new TextValue("c-d")));

        AssertColumn(_eval.Evaluate("=TEXTBEFORE(A1:A2,\"-\")", sheet), new TextValue("a"), new TextValue("c"));
        AssertColumn(_eval.Evaluate("=TEXTAFTER(A1:A2,\"-\")", sheet), new TextValue("b"), new TextValue("d"));
    }

    [Fact]
    public void Textsplit_SplitsColumnsRowsAndPadsRaggedRows()
    {
        var rv = _eval.Evaluate("=TEXTSPLIT(\"1,2;3\",\",\",\";\",FALSE,0,\"x\")", Sheet())
            .Should().BeOfType<RangeValue>().Subject;

        rv.RowCount.Should().Be(2);
        rv.ColCount.Should().Be(2);
        rv.At(1, 1).Should().Be(new TextValue("1"));
        rv.At(1, 2).Should().Be(new TextValue("2"));
        rv.At(2, 1).Should().Be(new TextValue("3"));
        rv.At(2, 2).Should().Be(new TextValue("x"));
    }

    [Fact]
    public void Textsplit_UsesDelimiterArraysAndIgnoreEmpty()
    {
        AssertRow(
            _eval.Evaluate("=TEXTSPLIT(\"Do. Or-do\",{\".\",\"-\"},,TRUE)", Sheet()),
            "Do",
            " Or",
            "do");

        AssertColumn(
            _eval.Evaluate("=TEXTSPLIT(\"Do. Or-do\",,{\".\",\"-\"},TRUE)", Sheet()),
            new TextValue("Do"),
            new TextValue(" Or"),
            new TextValue("do"));
    }

    [Fact]
    public void Textsplit_MatchModeSupportsCaseInsensitiveDelimiters()
    {
        AssertRow(
            _eval.Evaluate("=TEXTSPLIT(\"oneXtwoxtHree\",\"x\",,,,)", Sheet()),
            "oneXtwo",
            "tHree");

        AssertRow(
            _eval.Evaluate("=TEXTSPLIT(\"oneXtwoxtHree\",\"x\",,FALSE,1)", Sheet()),
            "one",
            "two",
            "tHree");
    }

    [Fact]
    public void Textsplit_DefaultPaddingIsNa()
    {
        var rv = _eval.Evaluate("=TEXTSPLIT(\"a,b;c\",\",\",\";\")", Sheet())
            .Should().BeOfType<RangeValue>().Subject;

        rv.At(2, 2).Should().Be(ErrorValue.NA);
    }

    [Theory]
    [InlineData("=TEXTSPLIT(\"abc\",,)")]
    [InlineData("=TEXTSPLIT(\"abc\",\"\")")]
    [InlineData("=TEXTSPLIT(\"abc\",\"a\",,,2)")]
    public void Textsplit_ReturnsValueForExcelArgumentDomainErrors(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    [Theory]
    [InlineData("=VALUETOTEXT(TRUE,0)", "TRUE")]
    [InlineData("=VALUETOTEXT(1234.01234,0)", "1234.01234")]
    [InlineData("=VALUETOTEXT(\"Hello\",0)", "Hello")]
    [InlineData("=VALUETOTEXT(TRUE,1)", "TRUE")]
    [InlineData("=VALUETOTEXT(1234.01234,1)", "1234.01234")]
    [InlineData("=VALUETOTEXT(\"Hello\",1)", "\"Hello\"")]
    [InlineData("=VALUETOTEXT(\"He said \"\"hi\"\"\",1)", "\"He said \"\"hi\"\"\"")]
    public void ValueToText_ReturnsExcelConciseAndStrictText(string formula, string expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new TextValue(expected));
    }

    [Fact]
    public void ValueToText_FormatsErrorsAndArrayValues()
    {
        _eval.Evaluate("=VALUETOTEXT(NA(),0)", Sheet()).Should().Be(new TextValue("#N/A"));
        _eval.Evaluate("=VALUETOTEXT({TRUE,#VALUE!;1234,\"Seattle\"},1)", Sheet())
            .Should().Be(new TextValue("{TRUE,#VALUE!;1234,\"Seattle\"}"));
    }

    [Fact]
    public void ArrayToText_ReturnsExcelConciseAndStrictArrayText()
    {
        var sheet = Sheet(
            (1, 1, new BoolValue(true)),
            (1, 2, ErrorValue.Value),
            (2, 1, new NumberValue(1234.01234)),
            (2, 2, new TextValue("Seattle")),
            (3, 1, new TextValue("Hello")),
            (3, 2, new NumberValue(1123)));

        _eval.Evaluate("=ARRAYTOTEXT(A1:B3,0)", sheet)
            .Should().Be(new TextValue("TRUE, #VALUE!, 1234.01234, Seattle, Hello, 1123"));
        _eval.Evaluate("=ARRAYTOTEXT(A1:B3,1)", sheet)
            .Should().Be(new TextValue("{TRUE,#VALUE!;1234.01234,\"Seattle\";\"Hello\",1123}"));
    }

    [Theory]
    [InlineData("=VALUETOTEXT(\"x\",2)")]
    [InlineData("=ARRAYTOTEXT({1,2},2)")]
    public void ValueTextFunctions_ReturnValueForUnsupportedFormat(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    private static Sheet Sheet(params (int Row, int Col, ScalarValue Value)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), value);
        return sheet;
    }

    private static void AssertRow(ScalarValue value, params string[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(1);
        range.ColCount.Should().Be(expected.Length);
        for (int col = 0; col < expected.Length; col++)
            range.At(1, col + 1).Should().Be(new TextValue(expected[col]));
    }

    private static void AssertColumn(ScalarValue value, params ScalarValue[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.At(row + 1, 1).Should().Be(expected[row]);
    }
}
