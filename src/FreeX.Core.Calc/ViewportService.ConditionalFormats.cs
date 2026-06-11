using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public sealed partial class ViewportService
{
    // Keyed by (SheetId, contentVersion, cfRuleVersion).
    // A small bounded dictionary: we evict the oldest entry when the cache grows beyond MaxCachedSheets
    // to prevent unbounded memory growth in multi-sheet workbooks. In practice a single host instance
    // serves one active sheet at a time, so one entry is the common case.
    private const int MaxCachedContexts = 8;

    private readonly Dictionary<CfContextCacheKey, CfEvaluationContext> _cfContextCache = new(MaxCachedContexts);
    private readonly Queue<CfContextCacheKey> _cfContextCacheOrder = new(MaxCachedContexts);

    // Exposed internal so tests can verify cache hits/misses without reflection.
    internal int CfContextBuildCount { get; private set; }

    private CfEvaluationContext BuildConditionalFormatContext(Sheet sheet, Workbook workbook)
    {
        var key = new CfContextCacheKey(sheet.Id, sheet.ContentVersion, sheet.ConditionalFormats.Version);

        if (_cfContextCache.TryGetValue(key, out var cached))
            return cached;

        var context = ViewportConditionalFormatEvaluator.BuildContext(sheet, workbook);
        CfContextBuildCount++;

        if (_cfContextCache.Count >= MaxCachedContexts && _cfContextCacheOrder.TryDequeue(out var oldest))
            _cfContextCache.Remove(oldest);

        _cfContextCache[key] = context;
        _cfContextCacheOrder.Enqueue(key);
        return context;
    }

    private static CfStyleResult? EvaluateConditionalFormats(
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext) =>
        ViewportConditionalFormatEvaluator.Evaluate(sheet, addr, value, workbook, cfContext, MatchesFormula);

    private static CellStyle MergeStyles(CellStyle? baseStyle, CellStyle cfStyle) =>
        ViewportConditionalFormatEvaluator.MergeStyles(baseStyle, cfStyle);

    private static bool TryGetDouble(ScalarValue value, out double result) =>
        ViewportConditionalFormatEvaluator.TryGetDouble(value, out result);

    private static bool TryParseDouble(string? text, out double result) =>
        ViewportConditionalFormatEvaluator.TryParseDouble(text, out result);

    private static ConditionalFormatDataBar? EvaluateConditionalDataBar(
        Sheet sheet,
        CellAddress addr,
        ScalarValue value,
        Workbook workbook,
        CfEvaluationContext cfContext) =>
        ViewportConditionalFormatEvaluator.EvaluateDataBar(sheet, addr, value, workbook, cfContext);
}

internal readonly record struct CfContextCacheKey(SheetId SheetId, int ContentVersion, int CfRuleVersion);
