using System.Diagnostics;
using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [BenchmarkFact]
    public void Benchmark_ConditionalFormatFormulaThresholds_ReportsTiming()
    {
        var (wb, sheet) = MakeWorkbook();
        for (uint row = 1; row <= 120; row++)
        {
            for (uint col = 1; col <= 40; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(row * col)));
            }
        }

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 120, 40)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Min,
            MidThresholdType = CfThresholdType.Formula,
            MidThresholdValue = "$A$1+600",
            MaxThresholdType = CfThresholdType.Max,
            MinColor = new RgbColor(0, 0, 255),
            MidColor = new RgbColor(255, 255, 255),
            MaxColor = new RgbColor(255, 0, 0)
        });

        var service = new ViewportService();
        var request = new ViewportRequest(1, 1, 2_600, 3_000);
        for (var i = 0; i < 2; i++)
            service.GetViewport(wb, sheet.Id, request);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(10);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        ViewportModel? viewport = null;
        for (var i = 0; i < 10; i++)
        {
            var step = Stopwatch.StartNew();
            viewport = service.GetViewport(wb, sheet.Id, request);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        timings.Sort();
        var mean = timings.Sum() / timings.Count;
        var p95 = timings[(int)Math.Min(timings.Count - 1, Math.Ceiling(timings.Count * 0.95) - 1)];
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF CF_FORMULA_THRESHOLDS " +
            $"steps={timings.Count} cells={viewport!.Cells.Count:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={mean:F2} " +
            $"p95_ms={p95:F2} max_ms={timings[^1]:F2} allocated_bytes={allocated:N0}");

        viewport.Cells.Should().HaveCount(4_800);
        allocated.Should().BeLessThan(
            14_500_000,
            "color-scale threshold evaluation should reuse per-viewport fill-only styles instead of allocating one style per displayed cell");
        total.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_ConditionalFormatFormulaRules_ReportsTiming()
    {
        var (wb, sheet) = MakeWorkbook();
        for (uint row = 1; row <= 120; row++)
        {
            for (uint col = 1; col <= 40; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(row * col)));
            }
        }

        sheet.SetCell(new CellAddress(sheet.Id, 1, 50), Cell.FromValue(new NumberValue(600)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 120, 40)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>$AX$1",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 235, 156) }
        });

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 120, 40)),
            Priority = 2,
            RuleType = CfRuleType.Formula,
            FormulaText = "$AX$1>500",
            FormatIfTrue = new CellStyle { FontColor = new CellColor(156, 87, 0) }
        });

        var service = new ViewportService();
        var request = new ViewportRequest(1, 1, 2_600, 3_000);
        for (var i = 0; i < 2; i++)
            service.GetViewport(wb, sheet.Id, request);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(10);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        ViewportModel? viewport = null;
        for (var i = 0; i < 10; i++)
        {
            var step = Stopwatch.StartNew();
            viewport = service.GetViewport(wb, sheet.Id, request);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        timings.Sort();
        var mean = timings.Sum() / timings.Count;
        var p95 = timings[(int)Math.Min(timings.Count - 1, Math.Ceiling(timings.Count * 0.95) - 1)];
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF CF_FORMULA_RULES " +
            $"steps={timings.Count} cells={viewport!.Cells.Count:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={mean:F2} " +
            $"p95_ms={p95:F2} max_ms={timings[^1]:F2} allocated_bytes={allocated:N0}");

        viewport.Cells.Should().HaveCount(4_800);
        total.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_ConditionalFormatAndFormulaRules_ReportsTiming()
    {
        var (wb, sheet) = MakeWorkbook();
        for (uint row = 1; row <= 120; row++)
        {
            for (uint col = 1; col <= 40; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(row * col)));
            }
        }

        sheet.SetCell(new CellAddress(sheet.Id, 1, 50), Cell.FromValue(new NumberValue(600)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 120, 40)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "AND(A1>0,A1<$AX$1)",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(198, 239, 206) }
        });

        var service = new ViewportService();
        var request = new ViewportRequest(1, 1, 10_000, 3_000);
        _ = service.GetViewport(wb, sheet.Id, request);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(10);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        ViewportModel? viewport = null;
        for (var i = 0; i < 10; i++)
        {
            var step = Stopwatch.StartNew();
            viewport = service.GetViewport(wb, sheet.Id, request);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        timings.Sort();
        var mean = timings.Sum() / timings.Count;
        var p95 = timings[(int)Math.Min(timings.Count - 1, Math.Ceiling(timings.Count * 0.95) - 1)];
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF CF_AND_FORMULA_RULES " +
            $"steps={timings.Count} cells={viewport!.Cells.Count:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={mean:F2} " +
            $"p95_ms={p95:F2} max_ms={timings[^1]:F2} allocated_bytes={allocated:N0}");

        viewport.Cells.Should().HaveCount(4_800);
        GetCell(viewport, 1, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
        GetCell(viewport, 120, 40).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
        allocated.Should().BeLessThan(
            10_000_000,
            "AND-of-comparison formula rules should avoid shifted AST allocation for every displayed cell");
        total.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [BenchmarkFact]
    public void Benchmark_ConditionalFormatIconSetThresholds_ReportsTiming()
    {
        var (wb, sheet) = MakeWorkbook();
        for (uint row = 1; row <= 120; row++)
        {
            for (uint col = 1; col <= 40; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new NumberValue(row * col)));
            }
        }

        var iconRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 120, 40)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "5Arrows"
        };
        iconRule.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Number, "500"),
            new CfThresholdModel(CfThresholdType.Number, "1500"),
            new CfThresholdModel(CfThresholdType.Number, "2500"),
            new CfThresholdModel(CfThresholdType.Number, "3500")
        ]);
        sheet.ConditionalFormats.Add(iconRule);

        var service = new ViewportService();
        var request = new ViewportRequest(1, 1, 2_600, 3_000);
        for (var i = 0; i < 2; i++)
            service.GetViewport(wb, sheet.Id, request);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(10);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        ViewportModel? viewport = null;
        for (var i = 0; i < 10; i++)
        {
            var step = Stopwatch.StartNew();
            viewport = service.GetViewport(wb, sheet.Id, request);
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        timings.Sort();
        var mean = timings.Sum() / timings.Count;
        var p95 = timings[(int)Math.Min(timings.Count - 1, Math.Ceiling(timings.Count * 0.95) - 1)];
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Console.WriteLine(
            "PERF CF_ICONSET_THRESHOLDS " +
            $"steps={timings.Count} cells={viewport!.Cells.Count:N0} " +
            $"total_ms={total.Elapsed.TotalMilliseconds:F2} mean_ms={mean:F2} " +
            $"p95_ms={p95:F2} max_ms={timings[^1]:F2} allocated_bytes={allocated:N0}");

        viewport.Cells.Should().HaveCount(4_800);
        GetCell(viewport, 120, 40).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 4, 5, true));
        total.Elapsed.TotalMilliseconds.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConditionalFormatIcon_IsValueTypedToAvoidPerCellIconAllocations()
    {
        typeof(ConditionalFormatIcon).IsValueType.Should().BeTrue();
    }

    [Fact]
    public void ConditionalFormatAggregates_DoNotEnumerateEveryCellInLargeAppliesToRanges()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("ViewportService.ConditionalFormats.cs");

        source.Should().NotContain(
            "cf.AppliesTo.AllCells()",
            "viewport refreshes for full-column or full-sheet conditional formats should scan sparse used cells instead of every address");
    }

    [Fact]
    public void ConditionalFormatAggregates_OnlyAllocateRankAndCountCachesForRulesThatNeedThem()
    {
        var source = ReadViewportConditionalFormatEvaluatorSources();

        source.Should().Contain(
            "cf.RuleType == CfRuleType.Top10 ? [] : null",
            "only top/bottom rules need a ranked-value cache while precomputing conditional-format aggregates");
        source.Should().Contain(
            "CfRuleType.DuplicateValues or CfRuleType.UniqueValues",
            "only duplicate/unique rules need display-value occurrence counts");
        source.Should().NotContain(
            "var rankedValues = new List<(CellAddress Address, double Value)>();",
            "color scales, icon sets, and above-average rules should avoid unused ranking-list allocations");
        source.Should().NotContain(
            "var valueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);",
            "non-duplicate aggregate rules should avoid unused value-count dictionary allocations");
    }

    [Fact]
    public void ConditionalFormatTopBottomRanking_SortsInPlaceWithoutLinqPipelines()
    {
        var source = ReadViewportConditionalFormatEvaluatorSources();
        var resolveTopBottom = source[
            source.IndexOf("private static IReadOnlySet<CellAddress>? ResolveTopBottomMatches", StringComparison.Ordinal)..
            source.IndexOf("private static IEnumerable<(CellAddress Address, ScalarValue Value)> EnumerateAggregateValues", StringComparison.Ordinal)];

        resolveTopBottom.Should().Contain("rankedValues.Sort(");
        resolveTopBottom.Should().Contain("new HashSet<CellAddress>(effectiveTake)");
        resolveTopBottom.Should().Contain("left.Index.CompareTo(right.Index)");
        resolveTopBottom.Should().NotContain(".OrderBy(");
        resolveTopBottom.Should().NotContain(".OrderByDescending(");
        resolveTopBottom.Should().NotContain(".Take(");
        resolveTopBottom.Should().NotContain(".Select(");
        resolveTopBottom.Should().NotContain(".ToHashSet(");
    }

    [Fact]
    public void ConditionalFormatContext_NoRulesReusesStaticEmptyContext()
    {
        var source = ReadViewportConditionalFormatEvaluatorSources();
        var buildContext = source[
            source.IndexOf("public static CfEvaluationContext BuildContext", StringComparison.Ordinal)..
            source.IndexOf("var rulesByPriority", StringComparison.Ordinal)];

        source.Should().Contain("private static readonly CfEvaluationContext EmptyContext");
        buildContext.Should().Contain("return EmptyContext;");
        buildContext.Should().NotContain("new CfEvaluationContext(");
        buildContext.Should().NotContain("new Dictionary<ConditionalFormat, CfAggregateCache>");
        buildContext.Should().NotContain("new Dictionary<ConditionalFormat, CfFormulaCache>");
    }

    [Fact]
    public void ConditionalFormatContext_NonEmptyRulesAvoidsLinqArrayPipelines()
    {
        var source = ReadViewportConditionalFormatEvaluatorSources();
        var buildContext = source[
            source.IndexOf("public static CfEvaluationContext BuildContext", StringComparison.Ordinal)..
            source.IndexOf("public static CfStyleResult? Evaluate", StringComparison.Ordinal)];

        buildContext.Should().Contain("CopyRulesByPriority(sheet.ConditionalFormats)");
        buildContext.Should().Contain("CopyIconRulesByPriority(rulesByPriority)");
        buildContext.Should().NotContain(".OrderBy(");
        buildContext.Should().NotContain(".Where(");
        buildContext.Should().NotContain(".ToArray(");
        source.Should().Contain("left.Index.CompareTo(right.Index)");
    }

    [Fact]
    public void ConditionalFormatEvaluation_DoesNotRunLinqRangeFiltersPerDisplayedCell()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("ViewportService.ConditionalFormats.cs");

        source.Should().NotContain(
            ".Where(cf => cf.AppliesTo.Contains(addr))",
            "conditional-format rules should be ordered once per viewport and checked with allocation-free loops per cell");
        source.Should().NotContain(
            ".Where(cf => cf.RuleType == CfRuleType.IconSet && cf.AppliesTo.Contains(addr))",
            "icon-set lookup runs for each displayed cell and should reuse preordered icon rules");
    }

    [Fact]
    public void IconSetThresholdResolution_UsesStackAllocatedThresholdBuffers()
    {
        var source = CalcSourceTestSupport.ReadCalcSource("ViewportService.ConditionalFormatIcons.cs");

        source.Should().Contain("cfContext.IconSetThresholds.TryGetValue");
        source.Should().Contain("ConditionalFormatEvaluationMath.ResolveIconBucket");
        source.Should().Contain("stackalloc double[thresholdCount]");
        source.Should().Contain("stackalloc bool[thresholdCount]");
        source.Should().NotContain("new List<(double Value, bool GreaterThanOrEqual)>");
        source.Should().NotContain("resolved.ToArray()");
    }

    [Fact]
    public void IconSetNumberThresholds_DoNotRequireAggregateScans()
    {
        var evaluatorSource = ReadViewportConditionalFormatEvaluatorSources();
        var iconsSource = CalcSourceTestSupport.ReadCalcSource("ViewportService.ConditionalFormatIcons.cs");
        var aggregateThresholds = evaluatorSource[
            evaluatorSource.IndexOf("private static bool RequiresAggregateThreshold", StringComparison.Ordinal)..
            evaluatorSource.IndexOf("private static IReadOnlySet<CellAddress>? ResolveTopBottomMatches", StringComparison.Ordinal)];

        evaluatorSource.Should().Contain("CfRuleType.IconSet => RequiresIconSetAggregateCache(cf)");
        evaluatorSource.Should().Contain("new ConditionalFormatEvaluationMath.StatisticsAccumulator(retainSortedValues)");
        evaluatorSource.Should().Contain("TryGetIconSetAggregateCache(cf, aggregates, out var cache)");
        aggregateThresholds.Should().NotContain(
            "CfThresholdType.Number",
            "static numeric icon-set thresholds should use the precomputed threshold cache without scanning the applies-to range");
        iconsSource.Should().Contain("cfContext.Aggregates.TryGetValue(rule, out var cache);");
        iconsSource.Should().NotContain(
            "!cfContext.Aggregates.TryGetValue(rule, out var cache)",
            "cached numeric icon-set thresholds should render even when no aggregate cache was built for the rule");
    }

    [Fact]
    public void FormulaConditionalFormatEvaluation_DoesNotSerializeShiftedFormulaPerDisplayedCell()
    {
        var formulaSource = CalcSourceTestSupport.ReadCalcSource("ViewportService.ConditionalFormatFormulas.cs");
        var evaluatorSource = ReadViewportConditionalFormatEvaluatorSources();

        formulaSource.Should().NotContain(
            "FormulaSerializer.Serialize",
            "viewport formula conditional formats should evaluate cached shifted ASTs instead of serializing formula text per cell");
        formulaSource.Should().NotContain(
            "Evaluate(\"=\" + formulaText",
            "serializing shifted formulas back to text makes FormulaEvaluator parse the same rule again per visible cell");
        evaluatorSource.Should().Contain(
            "PrecomputeThresholdFormulaCaches(sheet)",
            "formula thresholds should be parsed once while building the conditional-format viewport context");
        evaluatorSource.Should().Contain(
            "PrecomputeStaticThresholdFormulaValues",
            "absolute formula thresholds should be evaluated once per viewport instead of once per displayed cell");
        evaluatorSource.Should().Contain(
            "StaticThresholdFormulaValues",
            "resolved threshold formula values should be reused by color scales and icon sets");
        evaluatorSource.Should().Contain(
            "TryCreateSimpleAnd(ast",
            "AND-of-comparison formula rules should be precomputed once while building the viewport context");
        formulaSource.Should().Contain(
            "formulaCache.SimpleAnd",
            "AND-of-comparison formula rules should use the allocation-light per-cell path");
        evaluatorSource.Should().Contain(
            "IsCurrentCellSensitive",
            "relative or volatile threshold formulas must stay on the per-cell path");
        evaluatorSource.Should().NotContain(
            "new FormulaEvaluator().Evaluate(formula",
            "formula thresholds should reuse cached ASTs instead of parsing text for every displayed cell");
    }

    [Fact]
    public void ColorScaleThresholdResolution_ReusesCachedStaticThresholds()
    {
        var evaluatorSource = ReadViewportConditionalFormatEvaluatorSources();
        var colorScaleSource = evaluatorSource[
            evaluatorSource.IndexOf("private static CellStyle? ComputeColorScaleStyle", StringComparison.Ordinal)..
            evaluatorSource.IndexOf("internal static bool TryResolveThreshold", StringComparison.Ordinal)];

        evaluatorSource.Should().Contain("CfColorScaleThresholdCache");
        evaluatorSource.Should().Contain("PrecomputeColorScaleThresholdCaches(sheet, aggregates, staticThresholdFormulaValues)");
        colorScaleSource.Should().Contain("cfContext.ColorScaleThresholds.TryGetValue");
        colorScaleSource.Should().Contain("cachedThresholds.Min");
        colorScaleSource.Should().Contain("cachedThresholds.Mid");
        colorScaleSource.Should().Contain("GetThresholdFormula(cfContext, cf, CfThresholdFormulaSlot.ColorScaleMid)");
    }

    [Fact]
    public void ColorScaleStyleResolution_ReusesCachedFillStyles()
    {
        var evaluatorSource = ReadViewportConditionalFormatEvaluatorSources();
        var colorScaleSource = evaluatorSource[
            evaluatorSource.IndexOf("private static CellStyle? ComputeColorScaleStyle", StringComparison.Ordinal)..
            evaluatorSource.IndexOf("internal static bool TryResolveThreshold", StringComparison.Ordinal)];

        evaluatorSource.Should().Contain("CfColorScaleStyleCache");
        evaluatorSource.Should().Contain("CreateColorScaleStyleCache(rulesByPriority)");
        colorScaleSource.Should().Contain("GetColorScaleStyle(cfContext");
        colorScaleSource.Should().NotContain("return new CellStyle { FillColor = interpolated };");
        colorScaleSource.Should().NotContain("return new CellStyle { FillColor = cf.MinColor.ToCellColor() };");
    }

    [Fact]
    public void StackedConditionalFormatStyles_ReuseViewportCache()
    {
        var evaluatorSource = ReadViewportConditionalFormatEvaluatorSources();
        var evaluateSource = evaluatorSource[
            evaluatorSource.IndexOf("public static CfStyleResult? Evaluate", StringComparison.Ordinal)..
            evaluatorSource.IndexOf("private static Dictionary<ConditionalFormat, CellStyle> PrecomputeDefaultMergedFormatStyles", StringComparison.Ordinal)];

        evaluatorSource.Should().Contain("CfStackedStyleCache");
        evaluatorSource.Should().Contain("CreateStackedStyleCache(rulesByPriority)");
        evaluatorSource.Should().Contain("RuntimeHelpers.GetHashCode");
        evaluateSource.Should().Contain("GetStackedDifferentialStyle(cfContext");
        evaluateSource.Should().NotContain("StackDifferentialStyle(result.Value.Style, styleResult.Style)");
    }
}
