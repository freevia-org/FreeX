using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public partial class FormulaEvaluatorTests
{
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
    public void And_DirectTodayResult_TreatsDateSerialAsTrue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=AND(TODAY())", sheet)
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
    public void And_ReferencedText_IgnoresValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        _evaluator.Evaluate("=AND(A1:A2)", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void And_AllReferencedText_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        _evaluator.Evaluate("=AND(A1:A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void And_DirectText_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=AND(\"TRUE\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Or_OneTrueIsSufficient()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=OR(FALSE,TRUE,FALSE)", sheet)
            .Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Or_DirectTodayResult_TreatsDateSerialAsTrue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=OR(TODAY())", sheet)
            .Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Or_ReferencedText_IgnoresValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(false));
        _evaluator.Evaluate("=OR(A1:A2)", sheet).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Or_AllReferencedText_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        _evaluator.Evaluate("=OR(A1:A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Or_DirectText_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=OR(\"FALSE\")", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xor_ReferencedText_IgnoresValue()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new BoolValue(true));
        _evaluator.Evaluate("=XOR(A1:A2)", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Xor_AllReferencedText_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hello"));
        _evaluator.Evaluate("=XOR(A1:A1)", sheet).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Xor_DirectText_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=XOR(\"TRUE\")", sheet).Should().Be(ErrorValue.Value);
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

    [Fact]
    public void Abs_InvalidText_ReturnsValueError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=ABS(\"x\")", sheet).Should().Be(ErrorValue.Value);
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
    public void Left_NumCharsError_PropagatesError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=LEFT(\"hello\",NA())", sheet)
            .Should().Be(ErrorValue.NA);
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
    public void Right_NumCharsError_PropagatesError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=RIGHT(\"hello\",NA())", sheet)
            .Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void UnknownFunction_ReturnsNameError()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=NOTAFUNCTION(1)", sheet)
            .Should().Be(ErrorValue.Name);
    }

    [Fact]
    public void ExcelFutureFunctionPrefix_EvaluatesCanonicalBuiltInFunction()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        _evaluator.Evaluate("=_xlfn.XLOOKUP(\"B\",A1:A2,B1:B2)", sheet)
            .Should().Be(new NumberValue(20));
    }

    [Fact]
    public void ExcelFutureWorksheetFunctionPrefix_EvaluatesDynamicArrayFunction()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("keep"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("skip"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new BoolValue(false));

        var result = _evaluator.Evaluate("=_xlfn._xlws.FILTER(A1:A2,B1:B2)", sheet)
            .Should().BeOfType<RangeValue>().Subject;

        result.RowCount.Should().Be(1);
        result.ColCount.Should().Be(1);
        result.At(1, 1).Should().Be(new TextValue("keep"));
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
    [InlineData("#SPILL!")]
    [InlineData("#CALC!")]
    public void ErrorLiteral_EvaluatesToErrorValue(string errorCode)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        _evaluator.Evaluate("=" + errorCode, sheet).Should().Be(new ErrorValue(errorCode));
    }
}
