using System.Diagnostics;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit.Abstractions;

namespace FreeX.Core.Formula.Tests;

public sealed class BuiltInFunctionsPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public BuiltInFunctionsPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Names_ReusesCachedReadOnlyCatalog()
    {
        var names = BuiltInFunctions.Names;

        BuiltInFunctions.Names.Should().BeSameAs(names);
        names.Should().Contain(["SUM", "LET", "LAMBDA"]);
        names.Should().NotBeAssignableTo<string[]>();
    }

    [Fact]
    public void Names_RepeatedAccessDoesNotAllocateFunctionNameArrays()
    {
        _ = BuiltInFunctions.Names.Count;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 10_000; i++)
            _ = BuiltInFunctions.Names.Count;

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        allocatedBytes.Should().BeLessThan(1_024);
    }

    [Fact]
    public void Countif_LargeArrayCriteriaReusesArgumentBuffer()
    {
        var source = new RangeValue(new ScalarValue[,]
        {
            { new NumberValue(1) },
            { new NumberValue(2) },
        }) { IsSheetReference = true };
        var criteriaCells = new ScalarValue[5_000, 1];
        for (var row = 0; row < criteriaCells.GetLength(0); row++)
            criteriaCells[row, 0] = new NumberValue(row % 2 == 0 ? 1 : 2);
        ScalarValue[] args = [source, new RangeValue(criteriaCells)];
        var countif = BuiltInFunctions.Get("COUNTIF").Func;

        countif(args, EmptyEvalContext.Instance).Should().BeOfType<RangeValue>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var scalarResult = countif(args, EmptyEvalContext.Instance);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        var result = scalarResult.Should().BeOfType<RangeValue>().Subject;
        result.RowCount.Should().Be(5_000);
        result.Cells[0, 0].Should().Be(new NumberValue(1));
        result.Cells[1, 0].Should().Be(new NumberValue(1));
        _output.WriteLine($"COUNTIF array criteria allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            250_000,
            "array-criteria expansion should reuse one argument buffer instead of copying the full argument list for every result cell");
    }

    [Fact]
    public void Rept_LargeResultPreallocatesOutputBuffer()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        const string formula = "=REPT(\"abcd\",8191)";

        evaluator.Evaluate(formula, sheet).Should().BeOfType<TextValue>()
            .Which.Value.Length.Should().Be(32_764);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = evaluator.Evaluate(formula, sheet);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().BeOfType<TextValue>().Which.Value.Length.Should().Be(32_764);
        Console.WriteLine($"PERF REPT_LARGE_RESULT allocated_bytes={allocatedBytes}");
        allocatedBytes.Should().BeLessThan(
            80_000,
            "REPT should write directly into the final output string instead of allocating an intermediate builder buffer");
    }

    [Fact]
    public void Unique_LargeSingleColumnAvoidsSelectionAllocationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 20_000; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var warmup = evaluator.Evaluate("=UNIQUE(A1:A20000)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        warmup.RowCount.Should().Be(20_000);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate("=UNIQUE(A1:A20000)", sheet)
            .Should().BeOfType<RangeValue>().Subject;
        stopwatch.Stop();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.RowCount.Should().Be(20_000);
        Console.WriteLine($"PERF UNIQUE_SINGLE_COLUMN allocated_bytes={allocatedBytes}");
        _output.WriteLine(
            $"UNIQUE large single-column elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            250_000,
            "strictly monotonic single-column numeric ranges are already unique and should skip HashSet/result copy allocation");
    }

    [Fact]
    public void Xnpv_LargeCashFlowRangeAvoidsDateListAllocationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var firstDate = new DateTime(2026, 1, 1);
        for (uint row = 1; row <= 20_000; row++)
        {
            var value = row == 1 ? -10_000d : 1d;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(value));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(firstDate.AddDays(row - 1).ToOADate()));
        }

        evaluator.Evaluate("=XNPV(0.08,A1:A20000,B1:B20000)", sheet)
            .Should().BeOfType<NumberValue>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var result = evaluator.Evaluate("=XNPV(0.08,A1:A20000,B1:B20000)", sheet);

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().BeOfType<NumberValue>();
        _output.WriteLine($"XNPV large cash-flow range allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            80_000,
            "scalar-rate XNPV should stream direct value/date ranges instead of materializing RangeValue arrays");
    }

    [Fact]
    public void Npv_LargeCashFlowRangeAvoidsRangeMaterializationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 20_000; row++)
        {
            var value = row == 1 ? -10_000d : 1d;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(value));
        }

        evaluator.Evaluate("=NPV(0.08,A1:A20000)", sheet)
            .Should().BeOfType<NumberValue>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate("=NPV(0.08,A1:A20000)", sheet);
        stopwatch.Stop();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().BeOfType<NumberValue>();
        _output.WriteLine(
            $"NPV large cash-flow range elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            80_000,
            "scalar-rate NPV should stream direct value ranges instead of materializing RangeValue arrays");
    }

    [Fact]
    public void Textjoin_LargeDirectRangeAvoidsRangeAndListMaterialization()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 20_000; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue("x"));

        evaluator.Evaluate("=TEXTJOIN(\"\",TRUE,A1:A20000)", sheet)
            .Should().Be(new TextValue(new string('x', 20_000)));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate("=TEXTJOIN(\"\",TRUE,A1:A20000)", sheet);
        stopwatch.Stop();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().Be(new TextValue(new string('x', 20_000)));
        _output.WriteLine(
            $"TEXTJOIN large direct range elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            120_000,
            "TEXTJOIN should avoid materializing direct text ranges into RangeValue arrays and intermediate string lists");
    }

    [Fact]
    public void Irr_LargeCashFlowRangeAvoidsRangeMaterializationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(-10_000d));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3_000d));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(4_000d));
        sheet.SetCell(new CellAddress(sheet.Id, 20_000, 1), new NumberValue(5_000d));

        evaluator.Evaluate("=IRR(A1:A20000)", sheet)
            .Should().BeOfType<NumberValue>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();

        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate("=IRR(A1:A20000)", sheet);
        stopwatch.Stop();

        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        result.Should().BeOfType<NumberValue>();
        _output.WriteLine(
            $"IRR large cash-flow range elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            40_000,
            "IRR should collect direct value ranges without materializing RangeValue arrays");
    }

    private sealed class EmptyEvalContext : IEvalContext
    {
        public static readonly EmptyEvalContext Instance = new();

        public Sheet? CurrentSheet => null;
        public Workbook? CurrentWorkbook => null;
        public ScalarValue GetCellValue(uint row, uint col) => BlankValue.Instance;
        public ScalarValue GetCellValue(string sheetName, uint row, uint col) => BlankValue.Instance;
        public IReadOnlyList<ScalarValue> GetRangeValues(uint startRow, uint startCol, uint endRow, uint endCol) => [];
        public IReadOnlyList<ScalarValue> GetRangeValues(string sheetName, uint startRow, uint startCol, uint endRow, uint endCol) => [];
        public GridRange? TryResolveNamedRange(string name) => null;
        public string? TryGetSheetName(SheetId sheetId) => null;
        public bool SheetExists(string sheetName) => false;
        public bool IsRowHidden(uint row) => false;
        public bool IsRowHidden(string sheetName, uint row) => false;
        public bool IsRowFilterHidden(uint row) => false;
        public bool IsRowFilterHidden(string sheetName, uint row) => false;
        public Cell? TryGetCell(uint row, uint col) => null;
        public Cell? TryGetCell(string sheetName, uint row, uint col) => null;
    }
}
