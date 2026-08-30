using FreeX.Core.Formula;
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
        var crossSheetVersion = SheetHasConditionalFormatReachingAnotherSheet(sheet)
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

    // True when a rule on this sheet evaluates a formula that can actually REACH ANOTHER SHEET --
    // a sheet-qualified reference, a defined name, or a structured/table reference, any of which
    // may resolve elsewhere in the workbook.
    //
    // r171 remediation: the first version of this gate asked only whether the sheet had a
    // FORMULA-driven rule at all, which is a different and much broader question. A rule like
    // "=RAND()>0.5" reaches nothing outside its own cell, but under that gate any recalc anywhere
    // in the workbook changed the cross-sheet checksum and so invalidated this sheet's cached
    // evaluation -- re-rolling a volatile rule that nothing had touched. That broke the deliberate
    // contract in R147_ShiftF9RerollsVolatileCfWithNoFormulaCellsTests, which requires Calculate
    // Sheet on one sheet to leave another sheet's volatile conditional formatting frozen.
    private static bool SheetHasConditionalFormatReachingAnotherSheet(Sheet sheet)
    {
        var rules = sheet.ConditionalFormats;
        for (var i = 0; i < rules.Count; i++)
        {
            var cf = rules[i];
            if (cf.RuleType == CfRuleType.Formula && FormulaTextReachesAnotherSheet(cf.FormulaText))
                return true;

            if ((cf.MinThresholdType == CfThresholdType.Formula && FormulaTextReachesAnotherSheet(cf.MinThresholdValue)) ||
                (cf.MidThresholdType == CfThresholdType.Formula && FormulaTextReachesAnotherSheet(cf.MidThresholdValue)) ||
                (cf.MaxThresholdType == CfThresholdType.Formula && FormulaTextReachesAnotherSheet(cf.MaxThresholdValue)) ||
                (cf.DataBarMinThresholdType == CfThresholdType.Formula && FormulaTextReachesAnotherSheet(cf.DataBarMinThresholdValue)) ||
                (cf.DataBarMaxThresholdType == CfThresholdType.Formula && FormulaTextReachesAnotherSheet(cf.DataBarMaxThresholdValue)))
                return true;

            var iconThresholds = cf.IconSetThresholds;
            for (var j = 0; j < iconThresholds.Count; j++)
            {
                if (iconThresholds[j].Type == CfThresholdType.Formula &&
                    FormulaTextReachesAnotherSheet(iconThresholds[j].Value))
                    return true;
            }
        }

        return false;
    }

    // Parsing a rule formula is not free and the answer depends only on the text, so it is memoised
    // in a small bounded cache -- BuildConditionalFormatContext runs on every viewport pass,
    // including pure scroll/resize renders.
    private static readonly Dictionary<string, bool> _cfExternalReachCache = new(StringComparer.Ordinal);
    private static readonly Queue<string> _cfExternalReachOrder = new();
    private static readonly object _cfExternalReachGate = new();
    private const int MaxCachedExternalReachFormulas = 256;

    private static bool FormulaTextReachesAnotherSheet(string? formulaText)
    {
        if (string.IsNullOrWhiteSpace(formulaText))
            return false;

        lock (_cfExternalReachGate)
        {
            if (_cfExternalReachCache.TryGetValue(formulaText, out var cached))
                return cached;
        }

        bool reaches;
        try
        {
            reaches = NodeReachesAnotherSheet(FormulaEvaluator.ParseFormula(formulaText));
        }
        catch
        {
            // An unparseable rule never matches anything (see MatchesFormula), so it cannot depend
            // on another sheet either.
            reaches = false;
        }

        lock (_cfExternalReachGate)
        {
            while (_cfExternalReachCache.Count >= MaxCachedExternalReachFormulas &&
                   _cfExternalReachOrder.TryDequeue(out var stale))
                _cfExternalReachCache.Remove(stale);
            _cfExternalReachCache[formulaText] = reaches;
            _cfExternalReachOrder.Enqueue(formulaText);
        }

        return reaches;
    }

    private static bool NodeReachesAnotherSheet(FormulaNode node)
    {
        switch (node)
        {
            // A sheet qualifier means the reference may resolve on a different sheet. A defined
            // name or a table reference resolves through the workbook, so it may too.
            case CellRefNode cellRef:
                return cellRef.SheetName is not null;
            case RangeRefNode range:
                return range.SheetName is not null || range.EndSheetName is not null;
            case FullColumnRangeRefNode fullCol:
                return fullCol.SheetName is not null;
            case FullRowRangeRefNode fullRow:
                return fullRow.SheetName is not null;
            case NamedRangeNode:
            case StructuredReferenceNode:
            case StructuredCurrentRowReferenceNode:
                return true;

            case BinaryOpNode binary:
                return NodeReachesAnotherSheet(binary.Left) || NodeReachesAnotherSheet(binary.Right);
            case UnaryOpNode unary:
                return NodeReachesAnotherSheet(unary.Operand);
            case IntersectionNode intersection:
                return NodeReachesAnotherSheet(intersection.Left) || NodeReachesAnotherSheet(intersection.Right);
            case NamedRangeEndpointNode endpoint:
                return NodeReachesAnotherSheet(endpoint.Start) || NodeReachesAnotherSheet(endpoint.End);
            case UnionNode union:
                return AnyReachesAnotherSheet(union.Areas);
            case ArrayConstantNode array:
                foreach (var row in array.Rows)
                {
                    if (AnyReachesAnotherSheet(row))
                        return true;
                }
                return false;
            case FunctionCallNode call:
                // INDIRECT/OFFSET and friends build a reference at evaluation time, so their target
                // cannot be known from the text. Treat them as reaching outward rather than risk
                // serving a stale colour.
                if (BuildsReferenceDynamically(call.FunctionName))
                    return true;
                return AnyReachesAnotherSheet(call.Arguments);

            default:
                return false;
        }
    }

    private static bool AnyReachesAnotherSheet(IReadOnlyList<FormulaNode> nodes)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (NodeReachesAnotherSheet(nodes[i]))
                return true;
        }

        return false;
    }

    private static bool BuildsReferenceDynamically(string functionName) =>
        functionName.Equals("INDIRECT", StringComparison.OrdinalIgnoreCase) ||
        functionName.Equals("OFFSET", StringComparison.OrdinalIgnoreCase);

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
