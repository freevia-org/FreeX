using System.Diagnostics;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit.Abstractions;

namespace FreeX.Core.Formula.Tests;

public sealed class FormulaEvaluatorPerformanceTests
{
    private const int RowCount = 100_000;
    private readonly ITestOutputHelper _output;

    public FormulaEvaluatorPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void FunctionArgumentClassification_UsesCachedLookupSets()
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource("FormulaEvaluator.FunctionClassification.cs");
        var classificationHelpers = source[
            source.IndexOf("private static bool IsAggregateFunction", StringComparison.Ordinal)..];

        source.Should().Contain("private static readonly HashSet<string> AggregateFunctions");
        source.Should().Contain("private static readonly HashSet<string> StructuredRangeFunctions");
        classificationHelpers.Should().Contain("AggregateFunctions.Contains(name)");
        classificationHelpers.Should().Contain("StructuredRangeFunctions.Contains(name)");
        classificationHelpers.Should().NotContain("private static bool IsStructuredRangeFunction(string name) =>\r\n        name is");
    }

    [Fact]
    public void LexerIdentifierScanning_AvoidsDuplicateIdentifierStringAllocation()
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource("Lexer.cs");
        var identifierScanner = source[
            source.IndexOf("private Token ReadIdentifierOrRef", StringComparison.Ordinal)..
            source.IndexOf("private Token ReadQuotedSheetQualifier", StringComparison.Ordinal)];
        var structuredSelectorScanner = source[
            source.IndexOf("private Token ReadStructuredReferenceSelector()", StringComparison.Ordinal)..
            source.IndexOf("private Token ReadIdentifierOrRef", StringComparison.Ordinal)];

        identifierScanner.Should().Contain(
            "_text.AsSpan(start, _pos - start)",
            "identifier scanning should classify from the formula text span and allocate only the final token value");
        identifierScanner.Should().NotContain(
            "var value = _text[start.._pos]",
            "slicing to a string before uppercasing creates duplicate identifier string allocations");
        structuredSelectorScanner.Should().Contain(
            "ReadStructuredReferenceSelectorSlow(start)",
            "simple structured selectors should avoid StringBuilder while preserving nested/escaped selector handling");
    }

    [Fact]
    public void NumberValueCache_CoversCommonSmallIntegerFormulaResults()
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource("FormulaEvaluator.cs");

        source.Should().Contain(
            "private const int CachedIntegerNumberMax = 64",
            "common scalar arithmetic and coercion results should reuse immutable NumberValue instances");
    }

    [Fact]
    public void ParsedReferenceNodes_CacheColumnNumbers()
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource("FormulaNode.cs");

        source.Should().Contain(
            "public uint ColumnNumber { get; } = Model.CellAddress.ColumnNameToNumber(ColumnName);",
            "parsed cell references should not recompute column names on every evaluation");
        source.Should().Contain(
            "public uint StartColumnNumber { get; } = Model.CellAddress.ColumnNameToNumber(StartColumnName);",
            "parsed whole-column references should cache their start column number");
        source.Should().Contain(
            "public uint EndColumnNumber { get; } = Model.CellAddress.ColumnNameToNumber(EndColumnName);",
            "parsed whole-column references should cache their end column number");
    }

    [Fact]
    public void DirectLookupFastPaths_ReuseResolvedSheetReader()
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource("FormulaEvaluator.LookupFastPaths.cs");
        var reader = source[
            source.IndexOf("private readonly record struct DirectLookupRangeReader", StringComparison.Ordinal)..
            source.IndexOf("private readonly record struct DirectLookupRangeVector", StringComparison.Ordinal)];

        source.Should().Contain(
            "CreateDirectLookupReader",
            "direct lookup scans should resolve the sheet once before entering large vector loops");
        reader.Should().Contain(
            "if (Sheet is not null)",
            "hot direct lookup reads should reuse the resolved Sheet instead of resolving per cell");
        reader.Should().NotContain(
            "ResolveSheetForFastRange",
            "per-cell sheet resolution reintroduces CPU overhead in VLOOKUP, MATCH, LOOKUP, XMATCH, and XLOOKUP fast paths");
    }

    [BenchmarkFact]
    public void RepeatedFormulaTextEvaluation_ReusesParsedAst()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new NumberValue(5));

        const string formula = "=A1+B1*C1-D1/E1+F1^2";
        const int iterations = 20_000;
        var expected = new NumberValue(30d);

        evaluator.Evaluate(formula, sheet).Should().Be(expected);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        ScalarValue result = BlankValue.Instance;
        for (var iteration = 0; iteration < iterations; iteration++)
            result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(expected);
        _output.WriteLine(
            $"PERF repeated formula text eval iterations={iterations:N0} elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            1_024,
            "cached-AST scalar arithmetic should reuse cached integer NumberValue results instead of allocating one result per evaluation");
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [BenchmarkFact]
    public void RepeatedComparisonFormulaTextEvaluation_AvoidsDelegateChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));

        const string formula = "=A1<B1";
        const int iterations = 100_000;
        var expected = new BoolValue(true);

        evaluator.Evaluate(formula, sheet).Should().Be(expected);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        ScalarValue result = BlankValue.Instance;
        for (var iteration = 0; iteration < iterations; iteration++)
            result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(expected);
        _output.WriteLine(
            $"PERF repeated comparison formula text eval iterations={iterations:N0} elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(1_024);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [BenchmarkFact]
    public void RepeatedBooleanCoercionFormulaTextEvaluation_AvoidsCoercedNumberChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new BoolValue(true));

        const string formula = "=A1+A1+A1+A1";
        const int iterations = 100_000;
        var expected = new NumberValue(4d);

        evaluator.Evaluate(formula, sheet).Should().Be(expected);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        ScalarValue result = BlankValue.Instance;
        for (var iteration = 0; iteration < iterations; iteration++)
            result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(expected);
        _output.WriteLine(
            $"PERF repeated boolean coercion formula text eval iterations={iterations:N0} elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(
            1_024,
            "boolean arithmetic should reuse cached small integer NumberValue results instead of allocating one result per evaluation");
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [BenchmarkFact]
    public void ParserRepeatedIdentifierFormula_AvoidsIdentifierAllocationChurn()
    {
        const string formula =
            "=SUM(A1:B20,Data_Sheet!$C$1:$D$20)+AVERAGE(TableName[Amount])+IF(TRUE,MAX($E$1:$E$20),MIN($F$1:$F$20))";
        const int iterations = 20_000;

        new Parser(new Lexer(formula).Tokenize()).Parse().Should().BeOfType<BinaryOpNode>();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        FormulaNode node = new NumberNode(0);
        for (var iteration = 0; iteration < iterations; iteration++)
            node = new Parser(new Lexer(formula).Tokenize()).Parse();
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        node.Should().BeOfType<BinaryOpNode>();
        _output.WriteLine(
            $"PERF parser repeated identifier formula iterations={iterations:N0} elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(20_000_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Fact]
    public void RepeatedParserTokenSequences_ReuseCachedAst()
    {
        const string formula = "=SUM(A1:B2)+IF(TRUE,MAX(C1:C2),MIN(D1:D2))";

        var first = new Parser(new Lexer(formula).Tokenize()).Parse();
        var second = new Parser(new Lexer(formula).Tokenize()).Parse();

        second.Should().BeSameAs(first);
    }

    [Theory]
    [InlineData("=SUM(A1:A100000)", 5_000_050_000d)]
    [InlineData("=AVERAGE(A1:A100000)", 50_000.5d)]
    [InlineData("=MIN(A1:A100000)", 1d)]
    [InlineData("=MAX(A1:A100000)", 100_000d)]
    [InlineData("=COUNT(A1:A100000)", 100_000d)]
    public void SingleDirectRangeAggregate_AvoidsPerCellReferenceAllocations(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(1_000_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    public static IEnumerable<object[]> DirectRangeVarianceAggregateCases()
    {
        double sampleVariance = (double)RowCount * (RowCount + 1) / 12;
        double populationVariance = ((double)RowCount * RowCount - 1) / 12;

        yield return ["=STDEV(A1:A100000)", Math.Sqrt(sampleVariance)];
        yield return ["=STDEV.P(A1:A100000)", Math.Sqrt(populationVariance)];
        yield return ["=VAR(A1:A100000)", sampleVariance];
        yield return ["=VAR.P(A1:A100000)", populationVariance];
    }

    [Theory]
    [MemberData(nameof(DirectRangeVarianceAggregateCases))]
    public void DirectRangeVarianceAggregates_AvoidListMaterialization(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();

        // R116: EvaluateFastRangeOnlyVariance now rounds its final variance to 15 significant
        // digits (matching Excel and the already-rounded SUM/AVERAGE fast paths -- see
        // R116_AggregateFunctions15SigRoundingTests), which shifts this ~8.3e8-magnitude
        // result by up to ~1 part in 1e9 relative to the un-rounded raw Welford accumulation
        // this idealized closed-form `expected` was derived from. The tolerance here only
        // needs to be loose enough to accommodate that deliberate final rounding step, not to
        // hide a real correctness regression -- this test's actual purpose (guarded by the
        // allocation/elapsed assertions below) is verifying VAR/STDEV over a 100k-cell range
        // avoids list materialization, not bit-exact precision.
        ((NumberValue)evaluator.Evaluate(formula, sheet)).Value.Should().BeApproximately(expected, 1e-6);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        ((NumberValue)result).Value.Should().BeApproximately(expected, 1e-6);
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(1_000_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Fact]
    public void CrossSheetSingleDirectRangeAggregate_CachesSheetNameLookup()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = MakeWorkbookWithDataSheetAfterLookupNoise();
        var formulaSheet = workbook.GetSheet("Formula")!;
        var dataSheet = workbook.GetSheet("Data")!;
        const string crossSheetFormula = "=SUM(Data!A1:A100000)";
        const string sameSheetFormula = "=SUM(A1:A100000)";
        const double expected = 5_000_050_000d;

        evaluator.Evaluate(crossSheetFormula, formulaSheet, workbook).Should().Be(new NumberValue(expected));
        evaluator.Evaluate(sameSheetFormula, dataSheet, workbook).Should().Be(new NumberValue(expected));
        evaluator.Evaluate("=SUM(data!A1:A2)", formulaSheet, workbook).Should().Be(new NumberValue(3d));
        evaluator.Evaluate("=SUM(Missing!A1:A2)", formulaSheet, workbook).Should().Be(ErrorValue.Ref);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeSameSheetBytes = GC.GetAllocatedBytesForCurrentThread();
        var sameSheetStopwatch = Stopwatch.StartNew();
        var sameSheetResult = evaluator.Evaluate(sameSheetFormula, dataSheet, workbook);
        sameSheetStopwatch.Stop();
        var sameSheetAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeSameSheetBytes;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeCrossSheetBytes = GC.GetAllocatedBytesForCurrentThread();
        var crossSheetStopwatch = Stopwatch.StartNew();
        var crossSheetResult = evaluator.Evaluate(crossSheetFormula, formulaSheet, workbook);
        crossSheetStopwatch.Stop();
        var crossSheetAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeCrossSheetBytes;

        sameSheetResult.Should().Be(new NumberValue(expected));
        crossSheetResult.Should().Be(new NumberValue(expected));
        _output.WriteLine(
            $"{sameSheetFormula}: elapsed={sameSheetStopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={sameSheetAllocatedBytes:N0} bytes");
        _output.WriteLine(
            $"{crossSheetFormula}: elapsed={crossSheetStopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={crossSheetAllocatedBytes:N0} bytes");

        crossSheetAllocatedBytes.Should().BeLessThan(1_000_000);
        crossSheetStopwatch.Elapsed.Should().BeLessThan(sameSheetStopwatch.Elapsed * 4 + TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void MultiRangeAggregateExpansion_AvoidsExcessAllocationChurn()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();
        const string formula = "=SUM(A1:A100000,A1:A100000)";
        const double expected = 10_000_100_000d;

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(1_000_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Fact]
    public void NamedRangeAggregate_AvoidsRangeExpansion()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = MakeWorkbookWithNamedNumericRange();
        var sheet = workbook.GetSheet("Sheet1")!;
        const string formula = "=SUM(BigInputs)";
        const double expected = 5_000_050_000d;

        evaluator.Evaluate(formula, sheet, workbook).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet, workbook);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(1_000_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=SUMIF(B1:B100000,\"A\",A1:A100000)", 2_500_050_000d, 8_000)]
    [InlineData("=COUNTIF(B1:B100000,\"A\")", 50_000d, 8_000)]
    [InlineData("=AVERAGEIF(B1:B100000,\"A\",A1:A100000)", 50_001d, 8_000)]
    [InlineData("=SUMIFS(A1:A100000,B1:B100000,\"A\",C1:C100000,\">50000\")", 1_875_025_000d, 8_000)]
    [InlineData("=COUNTIFS(B1:B100000,\"A\",C1:C100000,\">50000\")", 25_000d, 8_000)]
    [InlineData("=AVERAGEIFS(A1:A100000,B1:B100000,\"A\",C1:C100000,\">50000\")", 75_001d, 8_000)]
    public void ConditionalAggregatesLargeRanges_AvoidFlatteningRangeLists(string formula, double expected, long maxAllocatedBytes)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeConditionalAggregateSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(maxAllocatedBytes);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Fact]
    public void CountblankSingleDirectRange_AvoidsRangeAndFlattenAllocations()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeCountBlankSheet();
        const string formula = "=COUNTBLANK(A1:A100000)";
        const double expected = 50_000d;

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(1_000_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=SUBTOTAL(9,A1:A100000)", 5_000_050_000d)]
    [InlineData("=SUBTOTAL(1,A1:A100000)", 50_000.5d)]
    [InlineData("=SUBTOTAL(2,A1:A100000)", 100_000d)]
    public void SubtotalLargeRanges_AvoidsNumericListMaterialization(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();

        ((NumberValue)evaluator.Evaluate(formula, sheet)).Value.Should().BeApproximately(expected, 1e-10);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        ((NumberValue)result).Value.Should().BeApproximately(expected, 1e-10);
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [BenchmarkTheory]
    [InlineData("=LARGE(A1:A100000,10)", 99_991d, 16_000)]
    [InlineData("=SMALL(A1:A100000,10)", 10d, 16_000)]
    [InlineData("=PERCENTILE(A1:A100000,0.5)", 50_000.5d, 16_000)]
    public void StatisticalSelectionLargeRanges_AvoidExcessAllocationChurn(string formula, double expected, long maxAllocatedBytes)
    {
        AssertLargeRangeSelectionPerformance(formula, expected, maxAllocatedBytes);
    }

    [BenchmarkTheory]
    [InlineData("=AGGREGATE(12,4,A1:A100000)", 50_000.5d, 16_000)]
    [InlineData("=AGGREGATE(14,4,A1:A100000,10)", 99_991d, 16_000)]
    [InlineData("=AGGREGATE(15,4,A1:A100000,10)", 10d, 16_000)]
    [InlineData("=AGGREGATE(16,4,A1:A100000,0.5)", 50_000.5d, 16_000)]
    [InlineData("=AGGREGATE(18,4,A1:A100000,0.5)", 50_000.5d, 16_000)]
    public void AggregateStatisticalSelectionLargeRanges_AvoidExcessAllocationChurn(
        string formula,
        double expected,
        long maxAllocatedBytes)
    {
        AssertLargeRangeSelectionPerformance(formula, expected, maxAllocatedBytes);
    }

    [Fact]
    public void AggregateSelectionOversizedSparseDirectRanges_PreserveValuesWhenBufferGrows()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row + 5));
        }

        evaluator.Evaluate("=AGGREGATE(15,4,A1:A600000,B1:B600000,1)", sheet)
            .Should()
            .Be(new NumberValue(1d));
    }

    public static IEnumerable<object[]> AggregateNonSelectionStreamingCases()
    {
        yield return ["=AGGREGATE(1,4,A1:A100000)", 50_000.5d, false, 8_000];
        yield return ["=AGGREGATE(2,4,A1:A100000)", 100_000d, false, 8_000];
        yield return ["=AGGREGATE(3,4,A1:A100000)", 100_000d, false, 8_000];
        yield return ["=AGGREGATE(4,4,A1:A100000)", 100_000d, false, 8_000];
        yield return ["=AGGREGATE(5,4,A1:A100000)", 1d, false, 8_000];
        yield return ["=AGGREGATE(6,4,A1:A100000)", 2d, true, 8_000];
        yield return ["=AGGREGATE(7,4,A1:A100000)", Math.Sqrt((double)RowCount * (RowCount + 1) / 12), false, 8_000];
        yield return ["=AGGREGATE(8,4,A1:A100000)", Math.Sqrt(((double)RowCount * RowCount - 1) / 12), false, 8_000];
        yield return ["=AGGREGATE(9,4,A1:A100000)", 5_000_050_000d, false, 8_000];
        yield return ["=AGGREGATE(10,4,A1:A100000)", (double)RowCount * (RowCount + 1) / 12, false, 8_000];
        yield return ["=AGGREGATE(11,4,A1:A100000)", ((double)RowCount * RowCount - 1) / 12, false, 8_000];
    }

    [Theory]
    [MemberData(nameof(AggregateNonSelectionStreamingCases))]
    public void AggregateNonSelectionLargeRanges_AvoidNumericListMaterialization(
        string formula,
        double expected,
        bool useProductSheet,
        long maxAllocatedBytes)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = useProductSheet ? MakeAggregateProductSheet() : MakeNumericSheet();

        ((NumberValue)evaluator.Evaluate(formula, sheet)).Value.Should().BeApproximately(expected, 1e-7);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        ((NumberValue)result).Value.Should().BeApproximately(expected, 1e-7);
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(maxAllocatedBytes);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [BenchmarkFact]
    public void AggregateModeLargeRange_AvoidsGroupByMaterialization()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeModeSheet();
        const string formula = "=AGGREGATE(13,4,A1:A100000)";
        const double expected = 42d;

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    private void AssertLargeRangeSelectionPerformance(string formula, double expected, long maxAllocatedBytes)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();

        ((NumberValue)evaluator.Evaluate(formula, sheet)).Value.Should().BeApproximately(expected, 1e-10);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        ((NumberValue)result).Value.Should().BeApproximately(expected, 1e-10);
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(maxAllocatedBytes);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=MATCH(100000,A1:A100000,0)", 100_000d)]
    [InlineData("=MATCH(100000,A1:A100000,1)", 100_000d)]
    public void MatchLargeDirectRange_AvoidsVectorFlattenAllocation(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=LOOKUP(100000,A1:A100000,B1:B100000)", 200_000d)]
    [InlineData("=LOOKUP(100000,A1:B100000)", 200_000d)]
    public void LookupLargeDirectRange_AvoidsVectorFlattenAllocation(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeLookupSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Fact]
    public void VlookupLargeDirectTable_AvoidsTableMaterialization()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeVlookupSheet();
        const string formula = "=VLOOKUP(100000,A1:C100000,3,FALSE)";
        const double expected = 300_000d;

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        Console.WriteLine(
            $"PERF VLOOKUP_LARGE_DIRECT_TABLE elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes}");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Fact]
    public void HlookupLargeDirectTable_AvoidsTableMaterialization()
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeHorizontalLookupSheet();
        const string formula = "=HLOOKUP(16384,A1:XFD3,3,FALSE)";
        const double expected = 32_768d;

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        Console.WriteLine(
            $"PERF HLOOKUP_LARGE_DIRECT_TABLE elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes}");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=XMATCH(100000,A1:A100000,0,1)", 100_000d)]
    [InlineData("=XMATCH(1,A1:A100000,0,-1)", 1d)]
    public void XmatchLargeDirectRangeLinearSearch_AvoidsIndexListAllocation(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeNumericSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=XMATCH(100000,A1:A100000,0,2)", 100_000d, false)]
    [InlineData("=XMATCH(100000,A1:A100000,0,-2)", 1d, true)]
    public void XmatchLargeDirectRangeBinarySearch_AvoidsIndexListAllocation(string formula, double expected, bool descending)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = descending ? MakeDescendingNumericSheet() : MakeNumericSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=XLOOKUP(100000,A1:A100000,B1:B100000,,0,1)", 200_000d)]
    [InlineData("=XLOOKUP(1,A1:A100000,B1:B100000,,0,-1)", 2d)]
    public void XlookupLargeDirectRangeLinearSearch_AvoidsIndexListAllocation(string formula, double expected)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = MakeLookupSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    [Theory]
    [InlineData("=XLOOKUP(100000,A1:A100000,B1:B100000,,0,2)", 200_000d, false)]
    [InlineData("=XLOOKUP(100000,A1:A100000,B1:B100000,,0,-2)", 200_000d, true)]
    public void XlookupLargeDirectRangeBinarySearch_AvoidsIndexListAllocation(string formula, double expected, bool descending)
    {
        var evaluator = new FormulaEvaluator();
        var sheet = descending ? MakeDescendingLookupSheet() : MakeLookupSheet();

        evaluator.Evaluate(formula, sheet).Should().Be(new NumberValue(expected));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        var result = evaluator.Evaluate(formula, sheet);
        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;

        result.Should().Be(new NumberValue(expected));
        _output.WriteLine($"{formula}: elapsed={stopwatch.Elapsed.TotalMilliseconds:F2}ms allocated={allocatedBytes:N0} bytes");
        allocatedBytes.Should().BeLessThan(8_000);
        stopwatch.Elapsed.Should().BeLessThan(MaxElapsedForPerformanceAssertion());
    }

    private static Sheet MakeNumericSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        return sheet;
    }

    private static Sheet MakeDescendingNumericSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            var value = RowCount - (int)row + 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(value));
        }

        return sheet;
    }

    private static Workbook MakeWorkbookWithDataSheetAfterLookupNoise()
    {
        var workbook = new Workbook();
        workbook.AddSheet("Formula");
        for (var index = 0; index < 500; index++)
            workbook.AddSheet($"Noise{index}");

        var dataSheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= RowCount; row++)
            dataSheet.SetCell(new CellAddress(dataSheet.Id, row, 1), new NumberValue(row));

        return workbook;
    }

    private static Workbook MakeWorkbookWithNamedNumericRange()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= RowCount; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        workbook.DefineNamedRange("BigInputs", new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, RowCount, 1)));

        return workbook;
    }

    private static Sheet MakeConditionalAggregateSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(row % 2 == 0 ? "A" : "B"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row));
        }

        return sheet;
    }

    private static Sheet MakeCountBlankSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            switch (row % 4)
            {
                case 0:
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
                    break;
                case 2:
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(""));
                    break;
                case 3:
                    sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue("x"));
                    break;
            }
        }

        return sheet;
    }

    private static Sheet MakeModeSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            var value = row % 2 == 0 ? 42d : row;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(value));
        }

        return sheet;
    }

    private static Sheet MakeAggregateProductSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2));
        for (uint row = 2; row <= RowCount; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(1));
        return sheet;
    }

    private static Sheet MakeLookupSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 2));
        }

        return sheet;
    }

    private static Sheet MakeDescendingLookupSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            var value = RowCount - (int)row + 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(value));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(value * 2));
        }

        return sheet;
    }

    private static Sheet MakeVlookupSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= RowCount; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 2));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row * 3));
        }

        return sheet;
    }

    private static Sheet MakeHorizontalLookupSheet()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint col = 1; col <= CellAddress.MaxCol; col++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, 1, col), new NumberValue(col));
            sheet.SetCell(new CellAddress(sheet.Id, 3, col), new NumberValue(col * 2));
        }

        return sheet;
    }

    /// <summary>
    /// Wall-clock ceiling for the 23 performance assertions in this file. It is a catastrophe check,
    /// not the measurement of record: each of those tests also asserts allocated bytes, and that is
    /// the assertion that actually pins the behaviour being guarded (no list materialization, no
    /// group-by materialization), because allocation is deterministic while wall-clock time is not.
    /// </summary>
    /// <remarks>
    /// This used to allow 30s under GITHUB_ACTIONS and 2s everywhere else, on the theory that only
    /// CI runs contended. A developer machine running the full 31-assembly gate is contended in the
    /// same way and got the tight budget: AGGREGATE(11,4,A1:A100000) took 4.59s there while
    /// allocating 64 bytes against its 8,000-byte budget -- the guarded property held perfectly and
    /// the run failed anyway. Since the tests rotate through the gate, a different one of the 23 was
    /// failing on each run. The generous budget is the project's own answer to "what survives a
    /// contended machine", so apply it everywhere rather than only where an environment variable
    /// happens to say so. It still catches what it is for: a materializing or quadratic regression
    /// on a 100,000-cell range runs for minutes, not seconds.
    /// </remarks>
    private static TimeSpan MaxElapsedForPerformanceAssertion() => TimeSpan.FromSeconds(30);

}
