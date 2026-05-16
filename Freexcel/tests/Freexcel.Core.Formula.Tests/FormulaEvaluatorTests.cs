using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Formula.Tests;

/// <summary>
/// The canonical first test from §9 of the build plan, plus comprehensive formula coverage.
/// </summary>
public class FormulaEvaluatorTests
{
    private readonly FormulaEvaluator _evaluator = new();

    private static (Sheet sheet, CellAddress a1, CellAddress a2, CellAddress a3) SetupSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        return (sheet, a1, a2, a3);
    }

    // ── THE canonical first test (§9) ──

    [Fact]
    public void SumOfRange_ReturnsExpectedTotal()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(3));

        var result = _evaluator.Evaluate("=SUM(A1:A3)", sheet);

        result.Should().Be(new NumberValue(6));
    }

    // ── Literal values ──

    [Fact]
    public void Number_Literal()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=42", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void Decimal_Literal()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=3.14", sheet).Should().Be(new NumberValue(3.14));
    }

    [Fact]
    public void String_Literal()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=\"hello\"", sheet).Should().Be(new TextValue("hello"));
    }

    [Fact]
    public void Boolean_True()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=TRUE", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Boolean_False()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=FALSE", sheet).Should().Be(new BoolValue(false));
    }

    // ── Arithmetic operators ──

    [Fact]
    public void Addition()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1+2", sheet).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Subtraction()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=10-3", sheet).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Multiplication()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=4*5", sheet).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Division()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=10/4", sheet).Should().Be(new NumberValue(2.5));
    }

    [Fact]
    public void DivisionByZero_ReturnsError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1/0", sheet).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void Power()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=2^10", sheet).Should().Be(new NumberValue(1024));
    }

    // ── Operator precedence ──

    [Fact]
    public void Precedence_MultiplyBeforeAdd()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1+2*3", sheet).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Precedence_ParensOverride()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=(1+2)*3", sheet).Should().Be(new NumberValue(9));
    }

    [Fact]
    public void Precedence_PowerBeforeMultiply()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=2*3^2", sheet).Should().Be(new NumberValue(18));
    }

    [Fact]
    public void Precedence_UnaryNegation()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=-5+3", sheet).Should().Be(new NumberValue(-2));
    }

    [Fact]
    public void Precedence_PowerBeforeUnaryNegation()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=-2^2", sheet).Should().Be(new NumberValue(-4));
    }

    // ── Cell references ──

    [Fact]
    public void CellRef_ReadsValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        _evaluator.Evaluate("=A1", sheet).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void CellRef_EmptyCell_ReturnsBlank()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=A1", sheet).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void CellRef_Arithmetic()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        _evaluator.Evaluate("=A1+B1", sheet).Should().Be(new NumberValue(15));
    }

    // ── String concatenation ──

    [Fact]
    public void Concatenation()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=\"Hello\" & \" \" & \"World\"", sheet)
            .Should().Be(new TextValue("Hello World"));
    }

    // ── Comparison operators ──

    [Fact]
    public void Comparison_Equal_True()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1=1", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Comparison_Equal_False()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1=2", sheet).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Comparison_LessThan()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1<2", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Comparison_GreaterThan()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=5>3", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Comparison_NotEqual()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=1<>2", sheet).Should().Be(new BoolValue(true));
    }

    // ── SUM function ──

    [Fact]
    public void Sum_SingleValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        _evaluator.Evaluate("=SUM(A1)", sheet).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Sum_MultipleArgs()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=SUM(1,2,3)", sheet).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void Sum_IgnoresText()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(5));
        _evaluator.Evaluate("=SUM(A1:A3)", sheet).Should().Be(new NumberValue(15));
    }

    // ── AVERAGE function ──

    [Fact]
    public void Average_OfRange()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(2));
        sheet.SetCell(a2, new NumberValue(4));
        sheet.SetCell(a3, new NumberValue(6));
        _evaluator.Evaluate("=AVERAGE(A1:A3)", sheet).Should().Be(new NumberValue(4));
    }

    // ── MIN / MAX ──

    [Fact]
    public void Min_OfRange()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(8));
        _evaluator.Evaluate("=MIN(A1:A3)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Max_OfRange()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetCell(a2, new NumberValue(2));
        sheet.SetCell(a3, new NumberValue(8));
        _evaluator.Evaluate("=MAX(A1:A3)", sheet).Should().Be(new NumberValue(8));
    }

    // ── COUNT / COUNTA ──

    [Fact]
    public void Count_CountsNumbers()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new TextValue("hi"));
        sheet.SetCell(a3, new NumberValue(3));
        _evaluator.Evaluate("=COUNT(A1:A3)", sheet).Should().Be(new NumberValue(2));
    }

    [Fact]
    public void CountA_CountsNonBlanks()
    {
        var (sheet, a1, a2, _) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetCell(a2, new TextValue("hi"));
        // a3 is blank
        _evaluator.Evaluate("=COUNTA(A1:A3)", sheet).Should().Be(new NumberValue(2));
    }

    // ── IF function ──

    [Fact]
    public void If_TrueCondition()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=IF(TRUE,\"yes\",\"no\")", sheet)
            .Should().Be(new TextValue("yes"));
    }

    [Fact]
    public void If_FalseCondition()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=IF(FALSE,\"yes\",\"no\")", sheet)
            .Should().Be(new TextValue("no"));
    }

    [Fact]
    public void If_NumericCondition()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        _evaluator.Evaluate("=IF(A1>5,\"big\",\"small\")", sheet)
            .Should().Be(new TextValue("big"));
    }

    // ── AND / OR / NOT ──

    [Fact]
    public void And_AllTrue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=AND(TRUE,TRUE,TRUE)", sheet)
            .Should().Be(new BoolValue(true));
    }

    [Fact]
    public void And_OneFalse()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=AND(TRUE,FALSE,TRUE)", sheet)
            .Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Or_OneTrueIsSufficient()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=OR(FALSE,TRUE,FALSE)", sheet)
            .Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Not_InvertsTrue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=NOT(TRUE)", sheet).Should().Be(new BoolValue(false));
    }

    // ── ROUND / ABS ──

    [Fact]
    public void Round_TwoDecimals()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=ROUND(3.14159,2)", sheet)
            .Should().Be(new NumberValue(3.14));
    }

    [Fact]
    public void Abs_NegativeNumber()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=ABS(-42)", sheet).Should().Be(new NumberValue(42));
    }

    // ── String functions ──

    [Fact]
    public void Concat_JoinsStrings()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=CONCAT(\"A\",\"B\",\"C\")", sheet)
            .Should().Be(new TextValue("ABC"));
    }

    [Fact]
    public void Len_ReturnsLength()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=LEN(\"hello\")", sheet)
            .Should().Be(new NumberValue(5));
    }

    [Fact]
    public void Left_ExtractsChars()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=LEFT(\"hello\",3)", sheet)
            .Should().Be(new TextValue("hel"));
    }

    [Fact]
    public void Right_ExtractsChars()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=RIGHT(\"hello\",3)", sheet)
            .Should().Be(new TextValue("llo"));
    }

    // ── Error propagation ──

    [Fact]
    public void UnknownFunction_ReturnsNameError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=NOTAFUNCTION(1)", sheet)
            .Should().Be(ErrorValue.Name);
    }

    [Fact]
    public void ErrorPropagates_ThroughArithmetic()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        var result = _evaluator.Evaluate("=1/0+5", sheet);
        result.Should().Be(ErrorValue.DivByZero);
    }

    [Theory]
    [InlineData("#REF!")]
    [InlineData("#N/A")]
    [InlineData("#DIV/0!")]
    [InlineData("#VALUE!")]
    [InlineData("#NAME?")]
    [InlineData("#NULL!")]
    [InlineData("#NUM!")]
    public void ErrorLiteral_EvaluatesToErrorValue(string errorCode)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=" + errorCode, sheet).Should().Be(new ErrorValue(errorCode));
    }

    // ── Percent operator ──

    [Fact]
    public void Percent_DividesByHundred()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=50%", sheet).Should().Be(new NumberValue(0.5));
    }

    // ── Nested functions ──

    [Fact]
    public void Nested_SumAndIf()
    {
        var (sheet, a1, a2, a3) = SetupSheet();
        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(a2, new NumberValue(20));
        sheet.SetCell(a3, new NumberValue(30));
        _evaluator.Evaluate("=IF(SUM(A1:A3)>50,\"big\",\"small\")", sheet)
            .Should().Be(new TextValue("big"));
    }

    // ── Parser: $ flags ──

    [Fact]
    public void Parse_AbsoluteRef_BothAnchors_IsColAbsolute_And_IsRowAbsolute()
    {
        var tokens = new Lexer("=$A$1").Tokenize();
        var ast = new Parser(tokens).Parse();
        var cell = ast.Should().BeOfType<CellRefNode>().Subject;
        cell.IsColAbsolute.Should().BeTrue();
        cell.IsRowAbsolute.Should().BeTrue();
        cell.ColumnName.Should().Be("A");
        cell.Row.Should().Be(1);
    }

    [Fact]
    public void Parse_AbsoluteRef_ColOnly_IsColAbsolute_True_RowAbsolute_False()
    {
        var tokens = new Lexer("=$B3").Tokenize();
        var ast = new Parser(tokens).Parse();
        var cell = ast.Should().BeOfType<CellRefNode>().Subject;
        cell.IsColAbsolute.Should().BeTrue();
        cell.IsRowAbsolute.Should().BeFalse();
        cell.ColumnName.Should().Be("B");
        cell.Row.Should().Be(3);
    }

    [Fact]
    public void Parse_AbsoluteRef_RowOnly_IsColAbsolute_False_RowAbsolute_True()
    {
        var tokens = new Lexer("=C$5").Tokenize();
        var ast = new Parser(tokens).Parse();
        var cell = ast.Should().BeOfType<CellRefNode>().Subject;
        cell.IsColAbsolute.Should().BeFalse();
        cell.IsRowAbsolute.Should().BeTrue();
        cell.ColumnName.Should().Be("C");
        cell.Row.Should().Be(5);
    }

    [Fact]
    public void Parse_RelativeRef_BothFlags_False()
    {
        var tokens = new Lexer("=D10").Tokenize();
        var ast = new Parser(tokens).Parse();
        var cell = ast.Should().BeOfType<CellRefNode>().Subject;
        cell.IsColAbsolute.Should().BeFalse();
        cell.IsRowAbsolute.Should().BeFalse();
    }

    [Fact]
    public void Parse_CellRefBeyondWorksheetRows_ReturnsRefError()
    {
        var tokens = new Lexer("=A1048577").Tokenize();
        var ast = new Parser(tokens).Parse();

        var error = ast.Should().BeOfType<ErrorNode>().Subject;
        error.Error.Should().Be(ErrorValue.Ref);
    }
}

public class CrossSheetReferenceTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void CrossSheetCellRef_ReadsValueFromOtherSheet()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(42));

        var result = _evaluator.Evaluate("=Sheet2!A1", sheet1, workbook);

        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void QuotedCrossSheetCellRef_ReadsValueFromSheetWithSpace()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("My Sheet");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(42));

        var result = _evaluator.Evaluate("='My Sheet'!A1", sheet1, workbook);

        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void QuotedCrossSheetCellRef_ReadsValueFromSheetWithApostrophe()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Bob's Sheet");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(99));

        var result = _evaluator.Evaluate("='Bob''s Sheet'!A1", sheet1, workbook);

        result.Should().Be(new NumberValue(99));
    }

    [Fact]
    public void CrossSheetRange_SumWorksAcrossSheets()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(2));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new NumberValue(3));

        var result = _evaluator.Evaluate("=SUM(Sheet2!A1:A3)", sheet1, workbook);

        result.Should().Be(new NumberValue(6));
    }

    [Fact]
    public void CrossSheetRef_UnknownSheet_ReturnsRefError()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");

        var result = _evaluator.Evaluate("=NonExistent!A1", sheet1, workbook);

        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void SameSheetRef_StillWorks()
    {
        var workbook = new Workbook("Test");
        var sheet1 = workbook.AddSheet("Sheet1");
        sheet1.SetCell(new CellAddress(sheet1.Id, 1, 1), new NumberValue(99));

        var result = _evaluator.Evaluate("=A1", sheet1, workbook);

        result.Should().Be(new NumberValue(99));
    }

    // ── Safety: inverted range references ─────────────────────────────────

    [Fact]
    public void InvertedRange_VLOOKUP_DoesNotCrash()
    {
        // B5:A1 is an inverted range — must not throw ArgumentOutOfRangeException
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(99));

        // VLOOKUP uses BuildRangeValue; inverted row/col should not crash
        var result = _evaluator.Evaluate("=VLOOKUP(10,B1:A1,2,FALSE)", sheet);

        // Result may be an error (lookup not found in inverted range) but must not throw
        result.Should().NotBeNull();
    }

    [Fact]
    public void InvertedRange_SUM_ReturnsZeroOrValue()
    {
        // SUM with inverted range (B3:A1) uses GetRangeValues which handles gracefully
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));

        var result = _evaluator.Evaluate("=SUM(A2:A1)", sheet);

        result.Should().NotBeNull();
    }

}

/// <summary>
/// Tests for short-circuit evaluation behaviour of IF, IFERROR, and IFNA,
/// and for edge-case argument validation.
/// </summary>
public class ShortCircuitEvaluationTests
{
    private readonly FormulaEvaluator _evaluator = new();

    // ── IF short-circuit ──────────────────────────────────────────────────

    [Fact]
    public void IF_ErrorInFalseBranch_DoesNotEvaluateFalseBranchWhenConditionIsTrue()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(1>0,\"yes\",1/0)", sheet, wb);
        result.Should().Be(new TextValue("yes"));
    }

    [Fact]
    public void IF_ErrorInTrueBranch_DoesNotEvaluateTrueBranchWhenConditionIsFalse()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(1>2,1/0,\"no\")", sheet, wb);
        result.Should().Be(new TextValue("no"));
    }

    [Fact]
    public void IF_TextCondition_ReturnsValueError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(\"TRUE\",\"yes\",\"no\")", sheet, wb);
        result.Should().Be(ErrorValue.Value, "text condition should produce #VALUE! as in Excel");
    }

    [Fact]
    public void IF_TwoArgs_FalseCondition_ReturnsFalse()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(1>2,\"yes\")", sheet, wb);
        result.Should().Be(new BoolValue(false), "IF with 2 args and false condition returns FALSE");
    }

    [Fact]
    public void IF_ConditionIsError_PropagatesError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IF(1/0,\"yes\",\"no\")", sheet, wb);
        result.Should().Be(ErrorValue.DivByZero, "error in condition propagates to IF result");
    }

    // ── IFERROR ───────────────────────────────────────────────────────────

    [Fact]
    public void IFERROR_DoesNotEvaluateFallback_WhenValueSucceeds()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IFERROR(42,1/0)", sheet, wb);
        result.Should().Be(new NumberValue(42));
    }

    [Fact]
    public void IFERROR_ReturnsFallback_WhenValueErrors()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IFERROR(1/0,\"err\")", sheet, wb);
        result.Should().Be(new TextValue("err"));
    }

    // ── IFNA ──────────────────────────────────────────────────────────────

    [Fact]
    public void IFNA_ReturnsFallback_OnlyForNA()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=IFNA(NA(),\"caught\")", sheet, wb)
            .Should().Be(new TextValue("caught"));
        _evaluator.Evaluate("=IFNA(1/0,\"caught\")", sheet, wb)
            .Should().Be(ErrorValue.DivByZero, "IFNA should only catch #N/A, not other errors");
    }

    [Fact]
    public void IFNA_CleanValue_PassesThrough()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IFNA(42,\"caught\")", sheet, wb);
        result.Should().Be(new NumberValue(42), "IFNA must not intercept non-error values");
    }

    // ── CHOOSE short-circuit ──────────────────────────────────────────────

    [Fact]
    public void CHOOSE_ErrorInUnselectedBranch_DoesNotPoison()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=CHOOSE(1,\"picked\",1/0)", sheet, wb);
        result.Should().Be(new TextValue("picked"), "CHOOSE must not evaluate untaken branches");
    }

    [Fact]
    public void CHOOSE_SelectsCorrectBranch()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=CHOOSE(2,\"a\",\"b\",\"c\")", sheet, wb)
            .Should().Be(new TextValue("b"));
    }

    [Fact]
    public void CHOOSE_OutOfRange_ReturnsValueError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=CHOOSE(5,\"a\",\"b\")", sheet, wb)
            .Should().Be(ErrorValue.Value);
    }

    // ── IFS short-circuit ─────────────────────────────────────────────────

    [Fact]
    public void IFS_ErrorInUnreachedPair_DoesNotPoison()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=IFS(1>0,\"first\",1>0,1/0)", sheet, wb);
        result.Should().Be(new TextValue("first"), "IFS must not evaluate pairs after the first true condition");
    }

    [Fact]
    public void IFS_NoTrueCondition_ReturnsNA()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=IFS(1>2,\"no\")", sheet, wb)
            .Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void IFS_ErrorCondition_Propagates()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=IFS(1/0,\"bad\")", sheet, wb)
            .Should().Be(ErrorValue.DivByZero, "error in a condition propagates");
    }

    // ── SWITCH short-circuit ──────────────────────────────────────────────

    [Fact]
    public void SWITCH_ErrorInUnmatchedBranch_DoesNotPoison()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=SWITCH(1,1,\"one\",2,1/0)", sheet, wb);
        result.Should().Be(new TextValue("one"), "SWITCH must not evaluate unmatched branches");
    }

    [Fact]
    public void SWITCH_UsesDefault_WhenNoMatchFound()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=SWITCH(99,1,\"one\",2,\"two\",\"default\")", sheet, wb)
            .Should().Be(new TextValue("default"));
    }

    [Fact]
    public void SWITCH_NoMatchNoDefault_ReturnsNA()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        _evaluator.Evaluate("=SWITCH(99,1,\"one\",2,\"two\")", sheet, wb)
            .Should().Be(ErrorValue.NA);
    }

    // ── Argument-count validation ─────────────────────────────────────────

    [Fact]
    public void SUM_WithZeroArguments_ReturnsValueError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        var result = _evaluator.Evaluate("=SUM()", sheet, wb);
        result.Should().Be(ErrorValue.Value);
    }

    // ── Parser row-bounds protection ──────────────────────────────────────

    [Fact]
    public void CellRef_WithRowBeyondMaxRow_ReturnsRefError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        // Row 2000000 is beyond the 1048576 limit
        var result = _evaluator.Evaluate("=A2000000", sheet, wb);
        result.Should().Be(ErrorValue.Ref);
    }

    [Fact]
    public void CellRef_WithRowZero_ReturnsRefError()
    {
        var wb = new Workbook("T"); var sheet = wb.AddSheet("S");
        // "A0" is not a valid cell reference
        var result = _evaluator.Evaluate("=A0", sheet, wb);
        result.Should().Be(ErrorValue.Ref);
    }
}
