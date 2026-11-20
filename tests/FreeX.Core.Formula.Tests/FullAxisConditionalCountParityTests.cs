using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

public sealed class FullAxisConditionalCountParityTests
{
    private readonly FormulaEvaluator _evaluator = new();

    [Fact]
    public void EmptyFullAxes_CountTheNominalGridWithoutMaterializingIt()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.DefineNamedRange("AllA", new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, CellAddress.MaxRow, 1)));
        _evaluator.Evaluate("COUNTIF(A:A,\"\")", sheet, workbook).Should().Be(new NumberValue(1048576));
        _evaluator.Evaluate("COUNTIF(AllA,\"\")", sheet, workbook).Should().Be(new NumberValue(1048576));
        _evaluator.Evaluate("COUNTIFS(A:A,\"\",B:B,\"\")", sheet, workbook).Should().Be(new NumberValue(1048576));
        _evaluator.Evaluate("COUNTIF(1:1048576,\"\")", sheet, workbook).Should().Be(new NumberValue(17179869184d));
    }

    [Theory]
    [InlineData("\"\"", 1048572, 2, 34)]
    [InlineData("\"=\"", 1048571, 1, 32)]
    [InlineData("\"<>\"", 5, 5, 31)]
    [InlineData("\"<>0\"", 1048575, 5, 55)]
    [InlineData("\"=0\"", 1, 1, 8)]
    [InlineData("0", 1, 1, 8)]
    [InlineData("\"*\"", 2, 2, 6)]
    [InlineData("\">0\"", 1, 1, 1)]
    [InlineData("\"<0\"", 0, 0, 0)]
    [InlineData("\"<=0\"", 1, 1, 8)]
    [InlineData("\">=0\"", 2, 2, 9)]
    public void CriteriaMatchExcelForBlanksEmptyTextAndOtherValues(
        string criterion, double fullCount, double boundedCount, double boundedSum)
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        // A cached formula result with empty text is distinct from a physical blank.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue(""));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(0));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), ErrorValue.NA);
        for (uint row = 1; row <= 6; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(Math.Pow(2, row - 1)));
        _evaluator.Evaluate($"COUNTIF(A:A,{criterion})", sheet, workbook).Should().Be(new NumberValue(fullCount));
        _evaluator.Evaluate($"COUNTIF(A1:A6,{criterion})", sheet, workbook).Should().Be(new NumberValue(boundedCount));
        _evaluator.Evaluate($"COUNTIFS(A1:A6,{criterion})", sheet, workbook).Should().Be(new NumberValue(boundedCount));
        _evaluator.Evaluate($"SUMIF(A1:A6,{criterion},B1:B6)", sheet, workbook).Should().Be(new NumberValue(boundedSum));
    }

    [Fact]
    public void Countifs_FullColumns_AlignOffsetsAcrossSheetsAndSparseValues()
    {
        var workbook = new Workbook("test");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        first.SetCell(new CellAddress(first.Id, 900000, 1), new TextValue("x"));
        second.SetCell(new CellAddress(second.Id, 800000, 2), new TextValue("y"));
        _evaluator.Evaluate("COUNTIFS(First!A:A,\"\",Second!B:B,\"\")", first, workbook)
            .Should().Be(new NumberValue(1048574));
        _evaluator.Evaluate("COUNTIFS(First!A:A,\"<>\",Second!B:B,\"\")", first, workbook)
            .Should().Be(new NumberValue(1));
        _evaluator.Evaluate("COUNTIFS(First!A:A,\"\",Second!B1:B20,\"\")", first, workbook)
            .Should().Be(ErrorValue.Value);
    }
    [Fact]
    public void FullAxisConditionalAggregates_UseNominalOffsetsForBlankCriteria()
    {
        var workbook = new Workbook("test");
        var first = workbook.AddSheet("First");
        var second = workbook.AddSheet("Second");
        second.SetCell(new CellAddress(second.Id, 1, 2), new NumberValue(2));
        second.SetCell(new CellAddress(second.Id, 900000, 2), new NumberValue(4));
        _evaluator.Evaluate("SUMIF(First!A:A,\"\",Second!B1)", first, workbook).Should().Be(new NumberValue(6));
        _evaluator.Evaluate("AVERAGEIF(First!A:A,\"\",Second!B:B)", first, workbook).Should().Be(new NumberValue(3));
        _evaluator.Evaluate("SUMIFS(Second!B:B,First!A:A,\"\")", first, workbook).Should().Be(new NumberValue(6));
        _evaluator.Evaluate("AVERAGEIFS(Second!B:B,First!A:A,\"\")", first, workbook).Should().Be(new NumberValue(3));
        first.SetCell(new CellAddress(first.Id, 900000, 1), new TextValue("x"));
        _evaluator.Evaluate("SUMIFS(Second!B:B,First!A:A,\"\")", first, workbook).Should().Be(new NumberValue(2));
        _evaluator.Evaluate("COUNTIFS(First!A:A,\"\",First!A:A,\"\")", first, workbook).Should().Be(new NumberValue(1048575));
    }

    [Fact]
    public void FullAxisSum_PreservesRowMajorNumberAndErrorOrder()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(-1e16));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1e16));
        _evaluator.Evaluate("SUMIF(A:A,\"\",B:B)", sheet, workbook).Should().Be(new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), ErrorValue.DivByZero);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), ErrorValue.NA);
        _evaluator.Evaluate("SUMIF(A:A,\"\",B:B)", sheet, workbook).Should().Be(ErrorValue.NA);
    }
    [Fact]
    public void FullAxisSum_DoesNotBufferRejectedValues()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 50000; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
        sheet.SetCell(new CellAddress(sheet.Id, 40000, 1), new TextValue("selected"));
        const string formula = "SUMIF(A:A,\"selected\",B:B)";
        for (int i = 0; i < 3; i++) _evaluator.Evaluate(formula, sheet, workbook);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = _evaluator.Evaluate(formula, sheet, workbook);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().Be(new NumberValue(40000));
        allocated.Should().BeLessThan(256_000, "the 49,999 rejected values must not be sorted or buffered");
    }
    [Fact]
    public void FullAxisSum_WithMatchingErrorDoesNotBufferNumbersAndSelectsFirstError()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 50000; row >= 1; row--)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), ErrorValue.DivByZero);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), ErrorValue.NA);
        const string formula = "SUMIF(A:A,\"\",B:B)";
        for (int i = 0; i < 3; i++) _evaluator.Evaluate(formula, sheet, workbook);
        long before = GC.GetAllocatedBytesForCurrentThread();
        var result = _evaluator.Evaluate(formula, sheet, workbook);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().Be(ErrorValue.NA);
        allocated.Should().BeLessThan(256_000, "a matching error must not buffer the numeric cells");
    }
}
