using FreeX.Core.Model;

namespace FreeX.Core.Calc;

public sealed partial class ViewportService
{
    // Keyed by (SheetId, contentVersion, cfRuleVersion, crossSheetVersion) -- see the cross-sheet
    // checksum comment in BuildConditionalFormatContext below for the last field.
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
        // A Formula-type rule (or a ColorScale/DataBar/IconSet threshold of type Formula) can read a
        // defined name -- or reference another sheet directly -- that resolves to a cell on a
        // DIFFERENT sheet (e.g. "=A1>Threshold" where Threshold refers to Sheet2!$B$1). RecalcEngine
        // only bumps Sheet.ContentVersion for sheets that actually recalculated a formula CELL (see
        // RecalcEngine.NotifySheetsRecalculated); a CF rule is not a cell and has no node in the
        // dependency graph, so editing the referenced cell on another sheet leaves THIS sheet's own
        // ContentVersion/ConditionalFormats.Version untouched. Without folding in the other sheets'
        // versions here, the cache below would keep serving the pre-edit evaluation until this sheet
        // is itself edited or a full F9 recalc runs (see NotifyAllSheetsRecalculated). Scoped to only
        // sheets that actually have a formula-driven rule so a workbook full of ordinary (non-formula)
        // CF rules never pays the cost of invalidating on an unrelated sheet's edit.
        var crossSheetVersion = SheetHasFormulaDrivenConditionalFormat(sheet)
            ? ComputeCrossSheetContentVersionChecksum(workbook)
            : 0;
        var key = new CfContextCacheKey(sheet.Id, sheet.ContentVersion, sheet.ConditionalFormats.Version, crossSheetVersion);

        if (_cfContextCache.TryGetValue(key, out var cached))
            return cached;

        var context = ViewportConditionalFormatEvaluator.BuildContext(sheet, workbook);
        CfContextBuildCount++;

        // Evict the oldest entry when the cache is full.  When dequeuing, skip any key that is no
        // longer in the cache dictionary: those are stale duplicate order slots from a key that was
        // previously evicted and re-inserted (see enqueue note below).  Skipping them prevents
        // accidentally evicting the live re-inserted entry.
        while (_cfContextCache.Count >= MaxCachedContexts)
        {
            if (!_cfContextCacheOrder.TryDequeue(out var candidate))
                break;
            if (_cfContextCache.ContainsKey(candidate))
            {
                _cfContextCache.Remove(candidate);
                break;
            }
            // Stale slot (key already evicted) — loop to find a live entry to evict.
        }

        _cfContextCache[key] = context;
        // Always enqueue on ADD (this is a cache miss so the key was not in the cache when we
        // entered).  If the key had a prior order slot from before it was evicted, that slot is now
        // stale and will be skipped by the eviction loop above, so double-enqueueing is safe.
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
        ViewportConditionalFormatEvaluator.EvaluateDataBar(sheet, addr, value, workbook, cfContext, MatchesFormula);

    // True when any rule on the sheet evaluates a formula that COULD read another sheet (via a
    // direct cross-sheet reference or a defined name) -- either the main Formula rule type, or any
    // ColorScale/DataBar/IconSet threshold configured with CfThresholdType.Formula. See the cache-key
    // comment in BuildConditionalFormatContext for why this gates the cross-sheet version checksum.
    private static bool SheetHasFormulaDrivenConditionalFormat(Sheet sheet)
    {
        var rules = sheet.ConditionalFormats;
        for (var i = 0; i < rules.Count; i++)
        {
            var cf = rules[i];
            if (cf.RuleType == CfRuleType.Formula)
                return true;
            if (cf.MinThresholdType == CfThresholdType.Formula ||
                cf.MidThresholdType == CfThresholdType.Formula ||
                cf.MaxThresholdType == CfThresholdType.Formula ||
                cf.DataBarMinThresholdType == CfThresholdType.Formula ||
                cf.DataBarMaxThresholdType == CfThresholdType.Formula)
                return true;

            var iconThresholds = cf.IconSetThresholds;
            for (var j = 0; j < iconThresholds.Count; j++)
            {
                if (iconThresholds[j].Type == CfThresholdType.Formula)
                    return true;
            }
        }

        return false;
    }

    // Cheap combined checksum of every sheet's ContentVersion in the workbook. Used as part of the
    // CF context cache key only for sheets with a formula-driven rule (see above), so that a
    // recalculated cell on ANY sheet -- not just this one -- invalidates the cached evaluation.
    private static int ComputeCrossSheetContentVersionChecksum(Workbook workbook)
    {
        var sheets = workbook.Sheets;
        var checksum = 0;
        for (var i = 0; i < sheets.Count; i++)
            checksum = unchecked(checksum * 31 + sheets[i].ContentVersion);
        return checksum;
    }
}

internal readonly record struct CfContextCacheKey(
    SheetId SheetId,
    int ContentVersion,
    int CfRuleVersion,
    int CrossSheetVersion);
