using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

public sealed class ExcelParityInformationTests
{
    private readonly FormulaEvaluator _eval = new();

    [Theory]
    [InlineData("=ISBLANK(A1)", true)]
    [InlineData("=ISERROR(1/0)", true)]
    [InlineData("=ISERR(1/0)", true)]
    [InlineData("=ISERR(NA())", false)]
    [InlineData("=ISEVEN(4)", true)]
    [InlineData("=ISFORMULA(B1)", true)]
    [InlineData("=ISLOGICAL(TRUE)", true)]
    [InlineData("=ISNA(NA())", true)]
    [InlineData("=ISNONTEXT(42)", true)]
    [InlineData("=ISNONTEXT(\"x\")", false)]
    [InlineData("=ISNUMBER(42)", true)]
    [InlineData("=ISODD(3)", true)]
    [InlineData("=ISREF(A1)", true)]
    [InlineData("=ISTEXT(\"x\")", true)]
    public void InformationPredicates_MatchExcelBooleanResults(string formula, bool expected)
    {
        var sheet = Sheet();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "1+1");

        _eval.Evaluate(formula, sheet).Should().Be(new BoolValue(expected));
    }

    [Theory]
    [InlineData("=ISREF(A:A)")]
    [InlineData("=ISREF(1:1)")]
    public void IsRef_ReturnsTrueForFullRowAndColumnReferences(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void FormulaPredicates_UseTopLeftCellForFullRowAndColumnReferences()
    {
        var sheet = Sheet();
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "2+3");

        _eval.Evaluate("=ISFORMULA(A:A)", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=ISFORMULA(1:1)", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=FORMULATEXT(A:A)", sheet).Should().Be(new TextValue("=2+3"));
        _eval.Evaluate("=FORMULATEXT(1:1)", sheet).Should().Be(new TextValue("=2+3"));
    }

    [Fact]
    public void IsErrAndIsNonText_SpillOverRanges()
    {
        var sheet = Sheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.Value);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), ErrorValue.NA);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(42));

        AssertColumn(_eval.Evaluate("=ISERR(A1:A4)", sheet), true, false, false, false);
        AssertColumn(_eval.Evaluate("=ISNONTEXT(A1:A4)", sheet), true, true, false, true);
    }

    [Theory]
    [InlineData("=ISEVEN(\"text\")")]
    [InlineData("=ISODD(\"text\")")]
    public void IsEvenAndIsOdd_ReturnValueForNonnumericText(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void IsEvenAndIsOdd_SpillValueErrorsForNonnumericText()
    {
        var sheet = Sheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("text"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        AssertColumn(_eval.Evaluate("=ISEVEN(A1:A3)", sheet), new BoolValue(true), ErrorValue.Value, new BoolValue(false));
        AssertColumn(_eval.Evaluate("=ISODD(A1:A3)", sheet), new BoolValue(false), ErrorValue.Value, new BoolValue(true));
    }

    [Fact]
    public void ErrorType_ReturnsExcelErrorCodes()
    {
        _eval.Evaluate("=ERROR.TYPE(NA())", Sheet()).Should().Be(new NumberValue(7));
        _eval.Evaluate("=ERROR.TYPE(1/0)", Sheet()).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void ErrorType_ClassifiesLoadedModernExcelErrors()
    {
        var sheet = Sheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.Spill);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new ErrorValue("#CONNECT!"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new ErrorValue("#BLOCKED!"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new ErrorValue("#UNKNOWN!"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new ErrorValue("#FIELD!"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), ErrorValue.Calc);

        var result = _eval.Evaluate("=ERROR.TYPE(A1:A6)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.At(1, 1).Should().Be(new NumberValue(9));
        result.At(2, 1).Should().Be(new NumberValue(10));
        result.At(3, 1).Should().Be(new NumberValue(11));
        result.At(4, 1).Should().Be(new NumberValue(12));
        result.At(5, 1).Should().Be(new NumberValue(13));
        result.At(6, 1).Should().Be(new NumberValue(14));
    }

    [Theory]
    [InlineData("=ERROR.TYPE(#SPILL!)", 9)]
    [InlineData("=ERROR.TYPE(#CONNECT!)", 10)]
    [InlineData("=ERROR.TYPE(#BLOCKED!)", 11)]
    [InlineData("=ERROR.TYPE(#UNKNOWN!)", 12)]
    [InlineData("=ERROR.TYPE(#FIELD!)", 13)]
    [InlineData("=ERROR.TYPE(#CALC!)", 14)]
    public void ErrorType_ClassifiesModernExcelErrorLiterals(string formula, int expected)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void Type_ReturnsArrayCodeForRangeReferences()
    {
        var sheet = Sheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("x"));

        _eval.Evaluate("=TYPE(A1:A2)", sheet).Should().Be(new NumberValue(64));
    }

    [Fact]
    public void Na_Type_N_Cell_Info_ReturnExcelCompatibleScalarValues()
    {
        _eval.Evaluate("=NA()", Sheet()).Should().Be(ErrorValue.NA);
        _eval.Evaluate("=TYPE(\"x\")", Sheet()).Should().Be(new NumberValue(2));
        _eval.Evaluate("=N(TRUE)", Sheet()).Should().Be(new NumberValue(1));
        _eval.Evaluate("=INFO(\"system\")", Sheet()).Should().BeOfType<TextValue>();
        _eval.Evaluate("=CELL(\"address\",B2)", Sheet()).Should().Be(new TextValue("$B$2"));
    }

    [Fact]
    public void CellProtect_ReportsLockedCellStyleIndependentOfSheetProtection()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Input");
        var unlocked = workbook.RegisterStyle(new CellStyle { Locked = false });

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.GetCell(new CellAddress(sheet.Id, 1, 2))!.StyleId = unlocked;

        _eval.Evaluate("=CELL(\"protect\",A1)", sheet, workbook).Should().Be(new NumberValue(1));
        _eval.Evaluate("=CELL(\"protect\",B1)", sheet, workbook).Should().Be(new NumberValue(0));
    }

    [Theory]
    [InlineData("=INFO(\"memavail\")")]
    [InlineData("=INFO(\"memused\")")]
    [InlineData("=INFO(\"totmem\")")]
    public void Info_ObsoleteMemoryTypes_ReturnNotAvailable(string formula)
    {
        _eval.Evaluate(formula, Sheet()).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void SheetAndSheets_ReturnWorkbookSheetOrdinals()
    {
        var workbook = new Workbook();
        var first = workbook.AddSheet("Input");
        var second = workbook.AddSheet("Data");
        workbook.AddSheet("Summary");

        _eval.Evaluate("=SHEET()", second, workbook).Should().Be(new NumberValue(2));
        _eval.Evaluate("=SHEET(\"Summary\")", first, workbook).Should().Be(new NumberValue(3));
        _eval.Evaluate("=SHEET(Data!B2)", first, workbook).Should().Be(new NumberValue(2));
        _eval.Evaluate("=SHEETS()", second, workbook).Should().Be(new NumberValue(3));
        _eval.Evaluate("=SHEETS(Data!B2:C4)", first, workbook).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void Sheet_ReturnsNotAvailableForMissingSheetName()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Input");

        _eval.Evaluate("=SHEET(\"Missing\")", sheet, workbook).Should().Be(ErrorValue.NA);
    }

    private static Sheet Sheet() => new(SheetId.New(), "S");

    private static void AssertColumn(ScalarValue value, params bool[] expected) =>
        AssertColumn(value, expected.Select(static value => (ScalarValue)new BoolValue(value)).ToArray());

    private static void AssertColumn(ScalarValue value, params ScalarValue[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            range.At(row + 1, 1).Should().Be(expected[row]);
    }
}
