using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

public sealed class FormulaEvaluatorExternalSheetCachePerformanceTests
{
    [Fact]
    public void ExternalSheetCache_PreservesCaseNumericAndFirstDuplicateCachedSheetSemantics()
    {
        var workbook = new Workbook("Test");
        var host = workbook.AddSheet("Host");
        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Book.xlsx",
            TargetMode = "External"
        };
        link.SheetNames.Add("Data");

        var first = new ExternalCachedSheetModel { SheetId = 0 };
        first.Values[(1u, 1u)] = new NumberValue(10);
        first.Values[(3u, 1u)] = null!;
        link.CachedSheetData.Add(first);

        var duplicate = new ExternalCachedSheetModel { SheetId = 0 };
        duplicate.Values[(1u, 1u)] = new NumberValue(99);
        duplicate.Values[(2u, 1u)] = new NumberValue(20);
        link.CachedSheetData.Add(duplicate);
        workbook.ExternalLinks.Add(link);

        var evaluator = new FormulaEvaluator();

        evaluator.Evaluate(
                "=SUM('[BOOK.XLSX]data'!A1:A1,'[book.xlsx]DATA'!A1:A1)",
                host,
                workbook)
            .Should().Be(new NumberValue(20),
                "case variants must share the same case-insensitive evaluation cache entry");
        evaluator.Evaluate("=SUM('[1]Data'!A1:A1)", host, workbook)
            .Should().Be(new NumberValue(10));
        evaluator.Evaluate("=MEDIAN('[Book.xlsx]Data'!A1:A1)", host, workbook)
            .Should().Be(new NumberValue(10),
                "materialized external ranges must use the same cached first sheet-data entry");
        evaluator.Evaluate("=SUM('[Book.xlsx]Data'!A3:A3)", host, workbook)
            .Should().Be(new NumberValue(0),
                "a present cached cell with a null value retains the existing blank semantics");
        evaluator.Evaluate("=SUM('[Book.xlsx]Data'!A2:A2)", host, workbook)
            .Should().Be(ErrorValue.Value,
                "the first duplicate sheet-data entry owns misses as well as hits");
        evaluator.Evaluate("=MEDIAN('[Book.xlsx]Data'!A2:A2)", host, workbook)
            .Should().Be(ErrorValue.Value,
                "materialized ranges must not fall through to a later duplicate sheet-data entry");
    }

    [Fact]
    public void ExternalSheetCache_PreservesResolvedMissingCacheAndUnknownLocalSheetErrors()
    {
        var workbook = new Workbook("Test");
        var host = workbook.AddSheet("Host");
        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Book.xlsx",
            TargetMode = "External"
        };
        link.SheetNames.Add("Data");
        workbook.ExternalLinks.Add(link);

        var evaluator = new FormulaEvaluator();

        evaluator.Evaluate("=SUM('[Book.xlsx]Data'!A1:A1)", host, workbook)
            .Should().Be(ErrorValue.Value,
                "a resolved external sheet without cached sheet data must preserve the loaded value path");
        evaluator.Evaluate("=SUM(Missing!A1:A1)", host, workbook)
            .Should().Be(ErrorValue.Ref,
                "an unknown local sheet remains a genuine reference error");
    }

    [Fact]
    public void ExternalSheetLookups_UseOneEvaluationScopedResolutionAndValuesCache()
    {
        var source = FormulaSourceTestSupport.ReadFormulaSource("FormulaEvaluator.Contexts.cs");
        var getCellValue = Slice(
            source,
            "public ScalarValue GetCellValue(string sheetName",
            "public IReadOnlyList<ScalarValue> GetRangeValues(uint");
        var getRangeValues = Slice(
            source,
            "public IReadOnlyList<ScalarValue> GetRangeValues(string sheetName",
            "private static List<ScalarValue>? CreateRangeValueList");
        var sheetExists = Slice(
            source,
            "public bool SheetExists(string sheetName)",
            "public bool IsRowHidden(uint row)");
        var resolver = Slice(
            source,
            "private ExternalSheetCacheEntry? ResolveExternalSheet",
            "// Wraps an IEvalContext with an extra layer");

        getCellValue.Should().Contain("ResolveExternalSheet(sheetName)");
        getRangeValues.Should().Contain("ResolveExternalSheet(sheetName)");
        sheetExists.Should().Contain("ResolveExternalSheet(sheetName) is not null");
        getCellValue.Should().Contain("cachedValues.TryGetValue((row, col), out var cachedValue)");
        getRangeValues.Should().Contain("cachedValues.TryGetValue((r, c), out var cachedValue)");
        getCellValue.Should().NotContain("ExternalSheetReferenceResolver.TryResolve");
        getRangeValues.Should().NotContain("ExternalSheetReferenceResolver.TryResolve");
        resolver.Should().Contain(
            "new Dictionary<string, ExternalSheetCacheEntry?>(StringComparer.OrdinalIgnoreCase)");
        resolver.Should().Contain("_externalSheetCache.TryGetValue(sheetName, out var cachedEntry)");
        resolver.Should().Contain("ExternalSheetReferenceResolver.TryResolve(_workbook, sheetName)");
        resolver.Should().Contain("foreach (var cachedSheet in resolved.Link.CachedSheetData)");
        resolver.Should().Contain("cachedValues = cachedSheet.Values;");
        resolver.Should().NotContain("ToDictionary(",
            "the evaluation cache must retain the live values dictionary rather than copying mutable state");
        resolver.Should().Contain("break;",
            "the original first matching cached-sheet entry must retain duplicate precedence");
        resolver.Should().Contain("_externalSheetCache[sheetName] = entry;",
            "resolved, cacheless, and unresolved lookups must all be memoized for the evaluation");
    }

    [BenchmarkFact]
    public void Benchmark_ExternalSheetSum_FiveThousandCellsWithLookupNoise_ReportsTimingAndAllocations()
    {
        const int noiseCount = 100;
        const int cellCount = 5_000;
        var workbook = new Workbook("Test");
        var host = workbook.AddSheet("Host");
        for (var index = 0; index < noiseCount; index++)
        {
            workbook.ExternalLinks.Add(new ExternalLinkModel
            {
                PackagePart = $"xl/externalLinks/externalLink{index + 1}.xml",
                TargetUri = $"C:/external/noise/Noise{index}.xlsx",
                TargetMode = "External"
            });
        }

        var target = new ExternalLinkModel
        {
            PackagePart = $"xl/externalLinks/externalLink{noiseCount + 1}.xml",
            TargetUri = "C:/external/target/Target.xlsx",
            TargetMode = "External"
        };
        for (var index = 0; index < noiseCount; index++)
        {
            target.SheetNames.Add($"Noise{index}");
            target.CachedSheetData.Add(new ExternalCachedSheetModel { SheetId = index });
        }

        target.SheetNames.Add("Data");
        var cachedData = new ExternalCachedSheetModel { SheetId = noiseCount };
        for (uint row = 1; row <= cellCount; row++)
            cachedData.Values[(row, 1u)] = new NumberValue(row);
        target.CachedSheetData.Add(cachedData);
        workbook.ExternalLinks.Add(target);

        const string formula = "=SUM('[Target.xlsx]Data'!A1:A5000)";
        const double expected = 12_502_500d;
        var evaluator = new FormulaEvaluator();
        evaluator.Evaluate(formula, host, workbook).Should().Be(new NumberValue(expected));

        var stopwatch = new Stopwatch();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        stopwatch.Start();

        var result = evaluator.Evaluate(formula, host, workbook);

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            "PERF FORMULA_EXTERNAL_SHEET_CACHE " +
            $"cells={cellCount} noise={noiseCount} elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={allocatedBytes:N0}");
        result.Should().Be(new NumberValue(expected));
        allocatedBytes.Should().BeLessThan(256_000,
            "external sheet/link parsing and cached-sheet scans should happen once per evaluation, not per cell");
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }
}
