using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    // ── CF/DV formula-text rewrites for structural insert/delete ─────────────

    // Slot constants for cfThresholdSnapshot keys (Guid = rule.Id, int = slot below).
    // 0 = FormulaText (not a threshold — kept in cfSnapshot, listed here for reference only)
    // 1 = MinThresholdValue   (colorScale)
    // 2 = MidThresholdValue   (colorScale)
    // 3 = MaxThresholdValue   (colorScale)
    // 4 = DataBarMinThresholdValue
    // 5 = DataBarMaxThresholdValue
    // 10 + i = IconSetThresholds[i].Value  (i = 0..n-1)
    private const int SlotColorScaleMin = 1;
    private const int SlotColorScaleMax = 3;
    private const int SlotColorScaleMid = 2;
    private const int SlotDataBarMin    = 4;
    private const int SlotDataBarMax    = 5;
    private const int SlotIconSetBase   = 10;

    /// <summary>
    /// After geometry has already been shifted, rewrites FormulaText on any
    /// ConditionalFormat rule and Formula1/Formula2 on any DataValidation rule
    /// through <see cref="FormulaRewriter"/> with the supplied structural op.
    /// Changed values are recorded in <paramref name="cfSnapshot"/> /
    /// <paramref name="cfThresholdSnapshot"/> / <paramref name="dvSnapshot"/>
    /// for undo by <see cref="RestoreRuleFormulas"/>.
    /// <para>
    /// <paramref name="cfThresholdSnapshot"/> captures colorScale/dataBar/iconSet cfvo
    /// threshold values whose <c>ThresholdType</c> is <see cref="CfThresholdType.Formula"/>.
    /// The key is <c>(rule.Id, slot)</c> where slot is one of the <c>Slot*</c> constants above.
    /// </para>
    /// </summary>
    internal static void RewriteRuleFormulas(
        Sheet sheet,
        RewriteOperation op,
        Dictionary<Guid, string?> cfSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot,
        Dictionary<(Guid Id, int Slot), string?> dvSnapshot)
    {
        foreach (var rule in sheet.ConditionalFormats)
        {
            if (rule.FormulaText is { } ft)
            {
                var rewritten = FormulaRewriter.Rewrite(ft, op, sheet.Name);
                if (rewritten is not null && rewritten != ft)
                {
                    cfSnapshot[rule.Id] = ft;
                    rule.FormulaText = rewritten;
                }
            }

            // colorScale thresholds
            RewriteThreshold(rule, SlotColorScaleMin, rule.MinThresholdType, rule.MinThresholdValue,
                op, sheet.Name, cfThresholdSnapshot,
                rewritten => rule.MinThresholdValue = rewritten);
            RewriteThreshold(rule, SlotColorScaleMid, rule.MidThresholdType, rule.MidThresholdValue,
                op, sheet.Name, cfThresholdSnapshot,
                rewritten => rule.MidThresholdValue = rewritten);
            RewriteThreshold(rule, SlotColorScaleMax, rule.MaxThresholdType, rule.MaxThresholdValue,
                op, sheet.Name, cfThresholdSnapshot,
                rewritten => rule.MaxThresholdValue = rewritten);

            // dataBar thresholds
            RewriteThreshold(rule, SlotDataBarMin, rule.DataBarMinThresholdType, rule.DataBarMinThresholdValue,
                op, sheet.Name, cfThresholdSnapshot,
                rewritten => rule.DataBarMinThresholdValue = rewritten);
            RewriteThreshold(rule, SlotDataBarMax, rule.DataBarMaxThresholdType, rule.DataBarMaxThresholdValue,
                op, sheet.Name, cfThresholdSnapshot,
                rewritten => rule.DataBarMaxThresholdValue = rewritten);

            // iconSet thresholds
            for (var i = 0; i < rule.IconSetThresholds.Count; i++)
            {
                var threshold = rule.IconSetThresholds[i];
                if (threshold.Type == CfThresholdType.Formula && threshold.Value is { } tv)
                {
                    var rewritten = FormulaRewriter.Rewrite(tv, op, sheet.Name);
                    if (rewritten is not null && rewritten != tv)
                    {
                        cfThresholdSnapshot[(rule.Id, SlotIconSetBase + i)] = tv;
                        rule.IconSetThresholds[i] = threshold with { Value = rewritten };
                    }
                }
            }
        }

        foreach (var rule in sheet.DataValidations)
        {
            if (rule.Formula1 is { } f1)
            {
                var rewritten = FormulaRewriter.Rewrite(f1, op, sheet.Name);
                if (rewritten is not null && rewritten != f1)
                {
                    dvSnapshot[(rule.Id, 1)] = f1;
                    rule.Formula1 = rewritten;
                }
            }
            if (rule.Formula2 is { } f2)
            {
                var rewritten = FormulaRewriter.Rewrite(f2, op, sheet.Name);
                if (rewritten is not null && rewritten != f2)
                {
                    dvSnapshot[(rule.Id, 2)] = f2;
                    rule.Formula2 = rewritten;
                }
            }
        }
    }

    /// <summary>
    /// Workbook-wide variant of <see cref="RewriteRuleFormulas(Sheet, RewriteOperation, Dictionary{Guid, string}, Dictionary{ValueTuple{Guid, int}, string}, Dictionary{ValueTuple{Guid, int}, string})"/>:
    /// applies the same per-sheet rewrite to EVERY sheet in the workbook, not just the one whose
    /// rows/columns/cells were structurally edited. A ConditionalFormat/DataValidation rule can live on
    /// any sheet while its FormulaText/Formula1/Formula2/threshold formula holds a cross-sheet
    /// reference into the sheet actually being shifted (e.g. a List validation on Sheet1 sourced from
    /// "=Sheet2!$A$1:$A$10" when rows are inserted on Sheet2) -- exactly like an ordinary cell formula
    /// on any sheet can reference the shifted sheet and already gets rewritten workbook-wide by
    /// <see cref="RewriteAllFormulas"/>. Insert/Delete Rows/Columns/Cells and same-sheet MoveRange all
    /// call this overload (mirroring RenameSheetCommand/DeleteSheetCommand's own explicit
    /// <c>foreach (var s in ctx.Workbook.Sheets)</c> loop around the single-sheet primitive) so a
    /// surviving rule elsewhere in the workbook keeps pointing at the shifted range instead of being
    /// silently left stale.
    /// <para>
    /// The three snapshot dictionaries are flat and keyed by the rule's own globally-unique <c>Id</c>
    /// (or <c>(Id, Slot)</c>), so entries from every sheet safely coexist in the same dictionaries --
    /// this is the same convention SheetCommands.cs's RenameSheetCommand/DeleteSheetCommand already
    /// rely on when they merge per-sheet snapshots from this same primitive.
    /// </para>
    /// </summary>
    internal static void RewriteRuleFormulas(
        Workbook workbook,
        RewriteOperation op,
        Dictionary<Guid, string?> cfSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot,
        Dictionary<(Guid Id, int Slot), string?> dvSnapshot)
    {
        foreach (var s in workbook.Sheets)
        {
            var cfCountBefore = cfSnapshot.Count;
            var cfThresholdCountBefore = cfThresholdSnapshot.Count;
            RewriteRuleFormulas(s, op, cfSnapshot, cfThresholdSnapshot, dvSnapshot);

            // Mirrors RenameSheetCommand's T7/R102 cache-invalidation: the CF viewport context cache
            // is keyed on (sheet.Id, sheet.ContentVersion, sheet.ConditionalFormats.Version) and caches
            // a precompiled AST per CF rule, so mutating FormulaText/threshold values in place above
            // never invalidates it on its own -- bump Version explicitly so a stale cache hit doesn't
            // keep evaluating the old formula after this structural edit.
            if (cfSnapshot.Count > cfCountBefore || cfThresholdSnapshot.Count > cfThresholdCountBefore)
                s.ConditionalFormats.NotifyRulesChanged();
        }
    }

    /// <summary>
    /// Workbook-wide variant of <see cref="RestoreRuleFormulas(Sheet, Dictionary{Guid, string}, Dictionary{ValueTuple{Guid, int}, string}, Dictionary{ValueTuple{Guid, int}, string})"/>,
    /// undoing a rewrite performed by the workbook-wide <see cref="RewriteRuleFormulas(Workbook, RewriteOperation, Dictionary{Guid, string}, Dictionary{ValueTuple{Guid, int}, string}, Dictionary{ValueTuple{Guid, int}, string})"/>
    /// overload above. Looks up each rule by Id across every sheet, since the snapshot dictionaries may
    /// hold entries captured from any sheet in the workbook.
    /// </summary>
    internal static void RestoreRuleFormulas(
        Workbook workbook,
        Dictionary<Guid, string?> cfSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot,
        Dictionary<(Guid Id, int Slot), string?> dvSnapshot)
    {
        foreach (var s in workbook.Sheets)
            RestoreRuleFormulas(s, cfSnapshot, cfThresholdSnapshot, dvSnapshot);
    }

    private static void RewriteThreshold(
        ConditionalFormat rule,
        int slot,
        CfThresholdType type,
        string? value,
        RewriteOperation op,
        string sheetName,
        Dictionary<(Guid Id, int Slot), string?> snapshot,
        Action<string> apply)
    {
        if (type != CfThresholdType.Formula || value is null)
            return;
        var rewritten = FormulaRewriter.Rewrite(value, op, sheetName);
        if (rewritten is not null && rewritten != value)
        {
            snapshot[(rule.Id, slot)] = value;
            apply(rewritten);
        }
    }

    /// <summary>
    /// Restores CF/DV formula text (and CF Formula-type threshold values) from snapshots
    /// captured by <see cref="RewriteRuleFormulas"/>.
    /// Looks up each rule by its <see cref="ConditionalFormat.Id"/> / <see cref="DataValidation.Id"/>.
    /// </summary>
    internal static void RestoreRuleFormulas(
        Sheet sheet,
        Dictionary<Guid, string?> cfSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot,
        Dictionary<(Guid Id, int Slot), string?> dvSnapshot)
    {
        if (cfSnapshot.Count > 0 || cfThresholdSnapshot.Count > 0)
        {
            foreach (var rule in sheet.ConditionalFormats)
            {
                if (cfSnapshot.TryGetValue(rule.Id, out var original))
                    rule.FormulaText = original;

                if (cfThresholdSnapshot.Count > 0)
                {
                    if (cfThresholdSnapshot.TryGetValue((rule.Id, SlotColorScaleMin), out var minVal))
                        rule.MinThresholdValue = minVal;
                    if (cfThresholdSnapshot.TryGetValue((rule.Id, SlotColorScaleMid), out var midVal))
                        rule.MidThresholdValue = midVal;
                    if (cfThresholdSnapshot.TryGetValue((rule.Id, SlotColorScaleMax), out var maxVal))
                        rule.MaxThresholdValue = maxVal;
                    if (cfThresholdSnapshot.TryGetValue((rule.Id, SlotDataBarMin), out var dbMin))
                        rule.DataBarMinThresholdValue = dbMin;
                    if (cfThresholdSnapshot.TryGetValue((rule.Id, SlotDataBarMax), out var dbMax))
                        rule.DataBarMaxThresholdValue = dbMax;
                    for (var i = 0; i < rule.IconSetThresholds.Count; i++)
                    {
                        if (cfThresholdSnapshot.TryGetValue((rule.Id, SlotIconSetBase + i), out var iconVal))
                            rule.IconSetThresholds[i] = rule.IconSetThresholds[i] with { Value = iconVal };
                    }
                }
            }
        }

        if (dvSnapshot.Count > 0)
        {
            foreach (var rule in sheet.DataValidations)
            {
                if (dvSnapshot.TryGetValue((rule.Id, 1), out var f1))
                    rule.Formula1 = f1;
                if (dvSnapshot.TryGetValue((rule.Id, 2), out var f2))
                    rule.Formula2 = f2;
            }
        }
    }


    internal static (
        List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? DataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? ConditionalFormats)
        CaptureRuleRanges(Sheet sheet)
    {
        return (
            sheet.DataValidations.Count == 0
                ? null
                : sheet.DataValidations.Select(rule => (rule, rule.AppliesTo, rule.AdditionalRanges.ToList())).ToList(),
            sheet.ConditionalFormats.Count == 0
                ? null
                : sheet.ConditionalFormats.Select(rule => (rule, rule.AppliesTo, (rule.AdditionalRanges ?? []).ToList())).ToList());
    }

    internal static void RestoreRuleRangesInPlace(
        Sheet sheet,
        List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? dataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? conditionalFormats)
    {
        if (dataValidations is not null)
        {
            foreach (var (rule, appliesTo, additionalRanges) in dataValidations)
            {
                rule.AppliesTo = appliesTo;
                rule.AdditionalRanges.Clear();
                rule.AdditionalRanges.AddRange(additionalRanges);
            }

            sheet.DataValidations.NotifyRulesChanged();
        }

        if (conditionalFormats is not null)
        {
            foreach (var (rule, appliesTo, additionalRanges) in conditionalFormats)
            {
                rule.AppliesTo = appliesTo;
                rule.AdditionalRanges = additionalRanges.Count == 0 ? null : additionalRanges;
            }

            sheet.ConditionalFormats.NotifyRulesChanged();
        }
    }

    // Full rebuild variant: used when rules may have been removed (e.g. DeleteRows/DeleteColumns).
    internal static void RestoreRuleRanges(
        Sheet sheet,
        List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? dataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? conditionalFormats)
    {
        if (dataValidations is not null)
        {
            sheet.DataValidations.Clear();
            foreach (var (rule, appliesTo, additionalRanges) in dataValidations)
            {
                rule.AppliesTo = appliesTo;
                rule.AdditionalRanges.Clear();
                rule.AdditionalRanges.AddRange(additionalRanges);
                sheet.DataValidations.Add(rule);
            }
        }
        if (conditionalFormats is not null)
        {
            sheet.ConditionalFormats.Clear();
            foreach (var (rule, appliesTo, additionalRanges) in conditionalFormats)
            {
                rule.AppliesTo = appliesTo;
                rule.AdditionalRanges = additionalRanges.Count == 0 ? null : additionalRanges;
                sheet.ConditionalFormats.Add(rule);
            }
        }
    }

    internal static void ShiftRuleRowsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.DataValidations.Count != 0)
        {
            foreach (var rule in sheet.DataValidations)
            {
                rule.AppliesTo = ShiftRangeRowsUp(rule.AppliesTo, start, count);
                ShiftAdditionalRanges(rule, range => ShiftRangeRowsUp(range, start, count));
            }
            sheet.DataValidations.NotifyRulesChanged();
        }
        if (sheet.ConditionalFormats.Count != 0)
        {
            foreach (var rule in sheet.ConditionalFormats)
            {
                rule.AppliesTo = ShiftRangeRowsUp(rule.AppliesTo, start, count);
                ShiftCfAdditionalRanges(rule, range => ShiftRangeRowsUp(range, start, count));
            }
            sheet.ConditionalFormats.NotifyRulesChanged();
        }
    }

    internal static void ShiftRuleRowsDown(
        Sheet sheet, uint start, uint count,
        Dictionary<Guid, string?> cfFormulaSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot,
        Dictionary<(Guid Id, int Slot), string?> dvFormulaSnapshot)
    {
        if (sheet.DataValidations.Count != 0)
        {
            for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
            {
                var rule = sheet.DataValidations[i];
                var shifted = ShiftRangeRowsDown(rule.AppliesTo, start, count);
                // Shift AdditionalRanges regardless of whether the primary AppliesTo survives:
                // a surviving non-primary area must keep the rule alive even when the primary
                // area was fully consumed by the delete (R44-commands-insert-delete-shift-3-1).
                ShiftAdditionalRanges(rule, range => ShiftRangeRowsDown(range, start, count));
                if (shifted is null)
                {
                    if (PromoteDvSurvivorOrRemove(rule, sheet.Name, dvFormulaSnapshot))
                        sheet.DataValidations.RemoveAt(i);
                }
                else
                {
                    rule.AppliesTo = shifted.Value;
                }
            }
            sheet.DataValidations.NotifyRulesChanged();
        }
        if (sheet.ConditionalFormats.Count != 0)
        {
            for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
            {
                var rule = sheet.ConditionalFormats[i];
                var shifted = ShiftRangeRowsDown(rule.AppliesTo, start, count);
                ShiftCfAdditionalRanges(rule, range => ShiftRangeRowsDown(range, start, count));
                if (shifted is null)
                {
                    if (PromoteCfSurvivorOrRemove(rule, sheet.Name, cfFormulaSnapshot, cfThresholdSnapshot))
                        sheet.ConditionalFormats.RemoveAt(i);
                }
                else
                {
                    rule.AppliesTo = shifted.Value;
                }
            }
            sheet.ConditionalFormats.NotifyRulesChanged();
        }
    }

    internal static void ShiftRuleColumnsUp(Sheet sheet, uint start, uint count)
    {
        if (sheet.DataValidations.Count != 0)
        {
            foreach (var rule in sheet.DataValidations)
            {
                rule.AppliesTo = ShiftRangeColumnsUp(rule.AppliesTo, start, count);
                ShiftAdditionalRanges(rule, range => ShiftRangeColumnsUp(range, start, count));
            }
            sheet.DataValidations.NotifyRulesChanged();
        }
        if (sheet.ConditionalFormats.Count != 0)
        {
            foreach (var rule in sheet.ConditionalFormats)
            {
                rule.AppliesTo = ShiftRangeColumnsUp(rule.AppliesTo, start, count);
                ShiftCfAdditionalRanges(rule, range => ShiftRangeColumnsUp(range, start, count));
            }
            sheet.ConditionalFormats.NotifyRulesChanged();
        }
    }

    internal static void ShiftRuleColumnsDown(
        Sheet sheet, uint start, uint count,
        Dictionary<Guid, string?> cfFormulaSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot,
        Dictionary<(Guid Id, int Slot), string?> dvFormulaSnapshot)
    {
        if (sheet.DataValidations.Count != 0)
        {
            for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
            {
                var rule = sheet.DataValidations[i];
                var shifted = ShiftRangeColumnsDown(rule.AppliesTo, start, count);
                // See ShiftRuleRowsDown: shift AdditionalRanges first so a surviving non-primary
                // area can be promoted instead of dropping the whole rule.
                ShiftAdditionalRanges(rule, range => ShiftRangeColumnsDown(range, start, count));
                if (shifted is null)
                {
                    if (PromoteDvSurvivorOrRemove(rule, sheet.Name, dvFormulaSnapshot))
                        sheet.DataValidations.RemoveAt(i);
                }
                else
                {
                    rule.AppliesTo = shifted.Value;
                }
            }
            sheet.DataValidations.NotifyRulesChanged();
        }
        if (sheet.ConditionalFormats.Count != 0)
        {
            for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
            {
                var rule = sheet.ConditionalFormats[i];
                var shifted = ShiftRangeColumnsDown(rule.AppliesTo, start, count);
                ShiftCfAdditionalRanges(rule, range => ShiftRangeColumnsDown(range, start, count));
                if (shifted is null)
                {
                    if (PromoteCfSurvivorOrRemove(rule, sheet.Name, cfFormulaSnapshot, cfThresholdSnapshot))
                        sheet.ConditionalFormats.RemoveAt(i);
                }
                else
                {
                    rule.AppliesTo = shifted.Value;
                }
            }
            sheet.ConditionalFormats.NotifyRulesChanged();
        }
    }

    /// <summary>
    /// When a DV rule's primary <see cref="DataValidation.AppliesTo"/> area has been fully
    /// consumed by an insert/delete (its shift returned <see langword="null"/>), promotes the
    /// first surviving entry of <see cref="DataValidation.AdditionalRanges"/> (already shifted
    /// by the caller) to become the new primary area, mirroring Excel's behavior of shrinking a
    /// multi-area rule's sqref to whatever areas survived rather than dropping the whole rule
    /// (R44-commands-insert-delete-shift-3-1). Returns <see langword="true"/> when the rule has
    /// no surviving area at all and must be removed entirely.
    /// </summary>
    private static bool PromoteDvSurvivorOrRemove(
        DataValidation rule,
        string sheetName,
        Dictionary<(Guid Id, int Slot), string?> dvFormulaSnapshot)
    {
        if (rule.AdditionalRanges.Count == 0)
            return true;

        var oldAnchor = rule.AppliesTo.Start;
        rule.AppliesTo = rule.AdditionalRanges[0];
        rule.AdditionalRanges.RemoveAt(0);

        // R135-commands-cf-dv-promote-anchor-1: DataValidationService (Formula1/Formula2 relative
        // reference resolution, see e.g. DataValidationService.cs ResolveListValues/TryParseNumberBound)
        // shifts relative refs by (targetCell - AppliesTo.Start), exactly like the CF anchor below --
        // re-anchor the formulas so they keep meaning the same thing now that AppliesTo.Start moved.
        RewriteDvFormulaByAnchorDelta(rule, oldAnchor, sheetName, dvFormulaSnapshot);

        return false;
    }

    /// <summary>
    /// Rewrites <see cref="DataValidation.Formula1"/>/<see cref="DataValidation.Formula2"/>'s relative
    /// references by the delta between <paramref name="oldAnchor"/> and the rule's NEW
    /// <see cref="DataValidation.AppliesTo"/>.Start, so the formula keeps referencing the same cells
    /// after <see cref="PromoteDvSurvivorOrRemove"/> moves the anchor. DV formulas are evaluated
    /// "as if written for the anchor cell" (relative refs shifted by targetCell - AppliesTo.Start at
    /// evaluation time -- see DataValidationService.cs), so re-anchoring without this compensating
    /// shift silently changes which cells a relative reference resolves to. Uses the same
    /// <see cref="PasteOffsetOp"/> FreeX already uses for ordinary relative-reference paste, which
    /// leaves absolute ($) references untouched, matching Excel's own paste semantics.
    /// </summary>
    private static void RewriteDvFormulaByAnchorDelta(
        DataValidation rule,
        CellAddress oldAnchor,
        string sheetName,
        Dictionary<(Guid Id, int Slot), string?> dvFormulaSnapshot)
    {
        var newAnchor = rule.AppliesTo.Start;
        int rowDelta = (int)newAnchor.Row - (int)oldAnchor.Row;
        int colDelta = (int)newAnchor.Col - (int)oldAnchor.Col;
        if (rowDelta == 0 && colDelta == 0)
            return;

        var op = new PasteOffsetOp(rowDelta, colDelta);

        if (rule.Formula1 is { } f1)
        {
            var rewritten = FormulaRewriter.Rewrite(f1, op, sheetName);
            if (rewritten is not null && rewritten != f1)
            {
                if (!dvFormulaSnapshot.ContainsKey((rule.Id, 1)))
                    dvFormulaSnapshot[(rule.Id, 1)] = f1;
                rule.Formula1 = rewritten;
            }
        }
        if (rule.Formula2 is { } f2)
        {
            var rewritten = FormulaRewriter.Rewrite(f2, op, sheetName);
            if (rewritten is not null && rewritten != f2)
            {
                if (!dvFormulaSnapshot.ContainsKey((rule.Id, 2)))
                    dvFormulaSnapshot[(rule.Id, 2)] = f2;
                rule.Formula2 = rewritten;
            }
        }
    }

    /// <summary>
    /// CF analogue of <see cref="PromoteDvSurvivorOrRemove"/>: promotes the first surviving entry
    /// of <see cref="ConditionalFormat.AdditionalRanges"/> (already shifted by the caller) to
    /// become the new primary <see cref="ConditionalFormat.AppliesTo"/> when the primary area was
    /// fully consumed. Returns <see langword="true"/> when nothing survived and the rule must be
    /// removed entirely.
    /// </summary>
    private static bool PromoteCfSurvivorOrRemove(
        ConditionalFormat rule,
        string sheetName,
        Dictionary<Guid, string?> cfFormulaSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot)
    {
        if (rule.AdditionalRanges is not { Count: > 0 } survivors)
            return true;

        var oldAnchor = rule.AppliesTo.Start;
        rule.AppliesTo = survivors[0];
        if (survivors.Count > 1)
        {
            var remaining = new List<GridRange>(survivors);
            remaining.RemoveAt(0);
            rule.AdditionalRanges = remaining;
        }
        else
        {
            rule.AdditionalRanges = null;
        }

        // R135-commands-cf-dv-promote-anchor-1: ViewportService.ConditionalFormatFormulas.cs
        // (MatchesFormula/EvaluateFormulaUncached) and ViewportConditionalFormatEvaluator.Thresholds.cs
        // both shift FormulaText/threshold-formula relative references by
        // (targetCell - AppliesTo.Start) at evaluation time -- promoting AppliesTo to a different
        // area without a compensating rewrite here silently re-anchors every relative reference in
        // the rule to the wrong cell (the rule keeps evaluating, but against the wrong operands).
        // Re-anchor the formula/thresholds by the same delta the AppliesTo anchor just moved by, so
        // the rule keeps meaning exactly what it meant before the promotion.
        RewriteCfFormulaByAnchorDelta(rule, oldAnchor, sheetName, cfFormulaSnapshot, cfThresholdSnapshot);

        return false;
    }

    /// <summary>
    /// CF analogue of <see cref="RewriteDvFormulaByAnchorDelta"/>: rewrites FormulaText and every
    /// Formula-type threshold (colorScale/dataBar/iconSet cfvo) by the delta between
    /// <paramref name="oldAnchor"/> and the rule's NEW <see cref="ConditionalFormat.AppliesTo"/>.Start,
    /// so a promoted rule (see <see cref="PromoteCfSurvivorOrRemove"/>) keeps evaluating against the
    /// same cells it did before its primary area was consumed by a delete.
    /// </summary>
    private static void RewriteCfFormulaByAnchorDelta(
        ConditionalFormat rule,
        CellAddress oldAnchor,
        string sheetName,
        Dictionary<Guid, string?> cfFormulaSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot)
    {
        var newAnchor = rule.AppliesTo.Start;
        int rowDelta = (int)newAnchor.Row - (int)oldAnchor.Row;
        int colDelta = (int)newAnchor.Col - (int)oldAnchor.Col;
        if (rowDelta == 0 && colDelta == 0)
            return;

        var op = new PasteOffsetOp(rowDelta, colDelta);

        if (rule.FormulaText is { } ft)
        {
            var rewritten = FormulaRewriter.Rewrite(ft, op, sheetName);
            if (rewritten is not null && rewritten != ft)
            {
                if (!cfFormulaSnapshot.ContainsKey(rule.Id))
                    cfFormulaSnapshot[rule.Id] = ft;
                rule.FormulaText = rewritten;
            }
        }

        RewriteThreshold(rule, SlotColorScaleMin, rule.MinThresholdType, rule.MinThresholdValue,
            op, sheetName, cfThresholdSnapshot, v => rule.MinThresholdValue = v);
        RewriteThreshold(rule, SlotColorScaleMid, rule.MidThresholdType, rule.MidThresholdValue,
            op, sheetName, cfThresholdSnapshot, v => rule.MidThresholdValue = v);
        RewriteThreshold(rule, SlotColorScaleMax, rule.MaxThresholdType, rule.MaxThresholdValue,
            op, sheetName, cfThresholdSnapshot, v => rule.MaxThresholdValue = v);
        RewriteThreshold(rule, SlotDataBarMin, rule.DataBarMinThresholdType, rule.DataBarMinThresholdValue,
            op, sheetName, cfThresholdSnapshot, v => rule.DataBarMinThresholdValue = v);
        RewriteThreshold(rule, SlotDataBarMax, rule.DataBarMaxThresholdType, rule.DataBarMaxThresholdValue,
            op, sheetName, cfThresholdSnapshot, v => rule.DataBarMaxThresholdValue = v);

        for (var i = 0; i < rule.IconSetThresholds.Count; i++)
        {
            var threshold = rule.IconSetThresholds[i];
            if (threshold.Type == CfThresholdType.Formula && threshold.Value is { } tv)
            {
                var rewritten = FormulaRewriter.Rewrite(tv, op, sheetName);
                if (rewritten is not null && rewritten != tv)
                {
                    var key = (rule.Id, SlotIconSetBase + i);
                    if (!cfThresholdSnapshot.ContainsKey(key))
                        cfThresholdSnapshot[key] = tv;
                    rule.IconSetThresholds[i] = threshold with { Value = rewritten };
                }
            }
        }
    }

    private static void ShiftAdditionalRanges(DataValidation rule, Func<GridRange, GridRange?> shift)
    {
        for (var i = rule.AdditionalRanges.Count - 1; i >= 0; i--)
        {
            var shifted = shift(rule.AdditionalRanges[i]);
            if (shifted is null)
                rule.AdditionalRanges.RemoveAt(i);
            else
                rule.AdditionalRanges[i] = shifted.Value;
        }
    }

    /// <summary>
    /// Applies a shift function to all entries in <see cref="ConditionalFormat.AdditionalRanges"/>.
    /// Ranges for which <paramref name="shift"/> returns <see langword="null"/> are removed (deleted out of existence).
    /// Rebuilds and reassigns the list (CF's AdditionalRanges is IReadOnlyList, not mutable in-place).
    /// </summary>
    private static void ShiftCfAdditionalRanges(ConditionalFormat rule, Func<GridRange, GridRange?> shift)
    {
        if (rule.AdditionalRanges is null || rule.AdditionalRanges.Count == 0)
            return;

        var result = new List<GridRange>(rule.AdditionalRanges.Count);
        foreach (var range in rule.AdditionalRanges)
        {
            var shifted = shift(range);
            if (shifted.HasValue)
                result.Add(shifted.Value);
        }
        rule.AdditionalRanges = result.Count == 0 ? null : result;
    }

    // ── Band-scoped rule adjustments for Insert/Delete Cells ─────────────────
    // Rules fully inside the shift band are translated. Rules whose range STRADDLES the shift
    // boundary (e.g. B2:D2 straddling an insert-before-C or delete-C2 op) are grown/shrunk to track
    // the surviving/inserted cells (R38-commands-insert-delete-shift-2-1), matching Excel and
    // FreeX's own whole-row/whole-column ShiftRange*Up/Down helpers.

    /// <summary>
    /// Insert Shift Down: rules fully inside [bandStartCol..bandEndCol] × [insertBeforeRow..MaxRow]
    /// are translated down by <paramref name="count"/> rows.
    /// Rules outside the band or partially overlapping are unchanged.
    /// </summary>
    internal static void AdjustRulesInsertShiftDown(
        Sheet sheet,
        uint bandStartCol, uint bandEndCol,
        uint insertBeforeRow, uint count)
    {
        bool dvChanged = false;
        foreach (var rule in sheet.DataValidations)
        {
            var translated = TranslateRangeInsertDown(rule.AppliesTo, bandStartCol, bandEndCol, insertBeforeRow, count);
            if (translated.HasValue)
            {
                rule.AppliesTo = translated.Value;
                dvChanged = true;
            }

            for (var i = 0; i < rule.AdditionalRanges.Count; i++)
            {
                var t = TranslateRangeInsertDown(rule.AdditionalRanges[i], bandStartCol, bandEndCol, insertBeforeRow, count);
                if (t.HasValue)
                {
                    rule.AdditionalRanges[i] = t.Value;
                    dvChanged = true;
                }
            }
        }

        if (dvChanged)
            sheet.DataValidations.NotifyRulesChanged();

        bool cfChanged = false;
        foreach (var rule in sheet.ConditionalFormats)
        {
            var translated = TranslateRangeInsertDown(rule.AppliesTo, bandStartCol, bandEndCol, insertBeforeRow, count);
            if (translated.HasValue)
            {
                rule.AppliesTo = translated.Value;
                cfChanged = true;
            }
            if (TranslateCfAdditionalRangesInsert(rule, r => TranslateRangeInsertDown(r, bandStartCol, bandEndCol, insertBeforeRow, count)))
                cfChanged = true;
        }

        if (cfChanged)
            sheet.ConditionalFormats.NotifyRulesChanged();
    }

    /// <summary>
    /// Insert Shift Right: rules fully inside [bandStartRow..bandEndRow] × [insertBeforeCol..MaxCol]
    /// are translated right by <paramref name="count"/> columns.
    /// </summary>
    internal static void AdjustRulesInsertShiftRight(
        Sheet sheet,
        uint bandStartRow, uint bandEndRow,
        uint insertBeforeCol, uint count)
    {
        bool dvChanged = false;
        foreach (var rule in sheet.DataValidations)
        {
            var translated = TranslateRangeInsertRight(rule.AppliesTo, bandStartRow, bandEndRow, insertBeforeCol, count);
            if (translated.HasValue)
            {
                rule.AppliesTo = translated.Value;
                dvChanged = true;
            }

            for (var i = 0; i < rule.AdditionalRanges.Count; i++)
            {
                var t = TranslateRangeInsertRight(rule.AdditionalRanges[i], bandStartRow, bandEndRow, insertBeforeCol, count);
                if (t.HasValue)
                {
                    rule.AdditionalRanges[i] = t.Value;
                    dvChanged = true;
                }
            }
        }

        if (dvChanged)
            sheet.DataValidations.NotifyRulesChanged();

        bool cfChanged = false;
        foreach (var rule in sheet.ConditionalFormats)
        {
            var translated = TranslateRangeInsertRight(rule.AppliesTo, bandStartRow, bandEndRow, insertBeforeCol, count);
            if (translated.HasValue)
            {
                rule.AppliesTo = translated.Value;
                cfChanged = true;
            }
            if (TranslateCfAdditionalRangesInsert(rule, r => TranslateRangeInsertRight(r, bandStartRow, bandEndRow, insertBeforeCol, count)))
                cfChanged = true;
        }

        if (cfChanged)
            sheet.ConditionalFormats.NotifyRulesChanged();
    }

    /// <summary>
    /// Delete Shift Up: rules fully inside [bandStartCol..bandEndCol] are adjusted:
    /// - entirely within the deleted rows → removed
    /// - entirely below the deleted rows → translated up by <paramref name="count"/>
    /// - partially overlapping the delete boundary → unchanged
    /// </summary>
    internal static void AdjustRulesDeleteShiftUp(
        Sheet sheet,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count,
        Dictionary<Guid, string?> cfFormulaSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot,
        Dictionary<(Guid Id, int Slot), string?> dvFormulaSnapshot)
    {
        bool dvChanged = false;
        for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var rule = sheet.DataValidations[i];
            var result = TranslateRangeDeleteUp(rule.AppliesTo, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count);
            if (result == RangeDeleteResult.Remove)
            {
                // Primary area fully consumed by the deleted band, but a non-primary additional
                // area may still survive (fully or partially, or lie entirely outside the band);
                // shift/adjust those first so a surviving area can be promoted instead of
                // dropping the whole rule (R44-commands-insert-delete-shift-3-1).
                AdjustAdditionalRangesDeleteUp(rule, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count);
                if (PromoteDvSurvivorOrRemove(rule, sheet.Name, dvFormulaSnapshot))
                    sheet.DataValidations.RemoveAt(i);
                dvChanged = true;
            }
            else
            {
                if (result.Translated.HasValue)
                {
                    rule.AppliesTo = result.Translated.Value;
                    dvChanged = true;
                }
                // AdditionalRanges are adjusted independently of the primary outcome: even when the
                // primary AppliesTo is unchanged (partial overlap), additional ranges that are fully
                // inside the deleted band must still be removed or translated.
                if (AdjustAdditionalRangesDeleteUp(rule, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count))
                    dvChanged = true;
            }
        }

        if (dvChanged)
            sheet.DataValidations.NotifyRulesChanged();

        bool cfChanged = false;
        for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var rule = sheet.ConditionalFormats[i];
            var result = TranslateRangeDeleteUp(rule.AppliesTo, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count);
            if (result == RangeDeleteResult.Remove)
            {
                AdjustCfAdditionalRangesDeleteUp(rule, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count);
                if (PromoteCfSurvivorOrRemove(rule, sheet.Name, cfFormulaSnapshot, cfThresholdSnapshot))
                    sheet.ConditionalFormats.RemoveAt(i);
                cfChanged = true;
            }
            else
            {
                if (result.Translated.HasValue)
                {
                    rule.AppliesTo = result.Translated.Value;
                    cfChanged = true;
                }
                if (AdjustCfAdditionalRangesDeleteUp(rule, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count))
                    cfChanged = true;
            }
        }

        if (cfChanged)
            sheet.ConditionalFormats.NotifyRulesChanged();
    }

    /// <summary>
    /// Delete Shift Left: rules fully inside [bandStartRow..bandEndRow] are adjusted:
    /// - entirely within the deleted cols → removed
    /// - entirely right of the deleted cols → translated left by <paramref name="count"/>
    /// - partially overlapping the delete boundary → unchanged
    /// </summary>
    internal static void AdjustRulesDeleteShiftLeft(
        Sheet sheet,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count,
        Dictionary<Guid, string?> cfFormulaSnapshot,
        Dictionary<(Guid Id, int Slot), string?> cfThresholdSnapshot,
        Dictionary<(Guid Id, int Slot), string?> dvFormulaSnapshot)
    {
        bool dvChanged = false;
        for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var rule = sheet.DataValidations[i];
            var result = TranslateRangeDeleteLeft(rule.AppliesTo, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count);
            if (result == RangeDeleteResult.Remove)
            {
                // See AdjustRulesDeleteShiftUp: shift AdditionalRanges first so a surviving
                // non-primary area can be promoted instead of dropping the whole rule
                // (R44-commands-insert-delete-shift-3-1).
                AdjustAdditionalRangesDeleteLeft(rule, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count);
                if (PromoteDvSurvivorOrRemove(rule, sheet.Name, dvFormulaSnapshot))
                    sheet.DataValidations.RemoveAt(i);
                dvChanged = true;
            }
            else
            {
                if (result.Translated.HasValue)
                {
                    rule.AppliesTo = result.Translated.Value;
                    dvChanged = true;
                }
                // AdditionalRanges are adjusted independently of the primary outcome.
                if (AdjustAdditionalRangesDeleteLeft(rule, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count))
                    dvChanged = true;
            }
        }

        if (dvChanged)
            sheet.DataValidations.NotifyRulesChanged();

        bool cfChanged = false;
        for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var rule = sheet.ConditionalFormats[i];
            var result = TranslateRangeDeleteLeft(rule.AppliesTo, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count);
            if (result == RangeDeleteResult.Remove)
            {
                AdjustCfAdditionalRangesDeleteLeft(rule, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count);
                if (PromoteCfSurvivorOrRemove(rule, sheet.Name, cfFormulaSnapshot, cfThresholdSnapshot))
                    sheet.ConditionalFormats.RemoveAt(i);
                cfChanged = true;
            }
            else
            {
                if (result.Translated.HasValue)
                {
                    rule.AppliesTo = result.Translated.Value;
                    cfChanged = true;
                }
                if (AdjustCfAdditionalRangesDeleteLeft(rule, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count))
                    cfChanged = true;
            }
        }

        if (cfChanged)
            sheet.ConditionalFormats.NotifyRulesChanged();
    }

    // ── Band-scoped translation helpers ──────────────────────────────────────

    /// <summary>
    /// Returns the translated range if it is fully inside the Insert-Down band's column span and
    /// touches or straddles the insert point, or null if it should be left unchanged (outside the
    /// band, or entirely above the insert point).
    /// <para>
    /// R38-commands-insert-delete-shift-2-1: a range that STRADDLES <paramref name="insertBeforeRow"/>
    /// (Start.Row &lt; insertBeforeRow &lt;= End.Row) GROWS its End.Row by <paramref name="count"/>
    /// while its Start.Row stays put — matching Excel's own reference-adjustment behavior (and
    /// FreeX's own whole-column <see cref="ShiftRangeRowsUp"/>, which grows the same way), instead of
    /// being left stale. A range entirely at/below the insert point shifts both endpoints down.
    /// </para>
    /// </summary>
    private static GridRange? TranslateRangeInsertDown(
        GridRange range,
        uint bandStartCol, uint bandEndCol,
        uint insertBeforeRow, uint count)
    {
        // Rule cols must be fully within the band column span.
        if (range.Start.Col < bandStartCol || range.End.Col > bandEndCol)
            return null;

        // Rule entirely above the insert point: unaffected.
        if (range.End.Row < insertBeforeRow)
            return null;

        // Straddling the insert point keeps Start.Row put and only grows End.Row; a range fully
        // at/below the insert point shifts both endpoints down (mirrors ShiftRangeRowsUp).
        var newStartRow = range.Start.Row < insertBeforeRow
            ? range.Start.Row
            : Math.Min(range.Start.Row + count, CellAddress.MaxRow);
        var newEndRow = Math.Min(range.End.Row + count, CellAddress.MaxRow);

        return new GridRange(
            new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
            new CellAddress(range.End.Sheet,   newEndRow,   range.End.Col));
    }

    /// <summary>
    /// Returns the translated range if it is fully inside the Insert-Right band's row span and
    /// touches or straddles the insert point, or null if unchanged (outside the band, or entirely
    /// left of the insert point).
    /// <para>
    /// R38-commands-insert-delete-shift-2-1: a range that STRADDLES <paramref name="insertBeforeCol"/>
    /// (Start.Col &lt; insertBeforeCol &lt;= End.Col) GROWS its End.Col by <paramref name="count"/>
    /// while its Start.Col stays put — matching Excel's own reference-adjustment behavior (and
    /// FreeX's own whole-column <see cref="ShiftRangeColumnsUp"/>, which grows the same way), instead
    /// of being left stale.
    /// </para>
    /// </summary>
    private static GridRange? TranslateRangeInsertRight(
        GridRange range,
        uint bandStartRow, uint bandEndRow,
        uint insertBeforeCol, uint count)
    {
        // Rule rows must be fully within the band row span.
        if (range.Start.Row < bandStartRow || range.End.Row > bandEndRow)
            return null;

        // Rule entirely left of the insert point: unaffected.
        if (range.End.Col < insertBeforeCol)
            return null;

        // Straddling the insert point keeps Start.Col put and only grows End.Col; a range fully
        // at/right of the insert point shifts both endpoints right (mirrors ShiftRangeColumnsUp).
        var newStartCol = range.Start.Col < insertBeforeCol
            ? range.Start.Col
            : Math.Min(range.Start.Col + count, CellAddress.MaxCol);
        var newEndCol = Math.Min(range.End.Col + count, CellAddress.MaxCol);

        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet,   range.End.Row,   newEndCol));
    }

    // Tri-state result for delete translation: unchanged / remove / translated.
    private readonly struct RangeDeleteResult
    {
        public static readonly RangeDeleteResult Unchanged = new(null, false);
        public static readonly RangeDeleteResult Remove    = new(null, true);

        public static RangeDeleteResult Translate(GridRange r) => new(r, false);

        private RangeDeleteResult(GridRange? translated, bool remove)
        {
            Translated = translated;
            _remove    = remove;
        }

        public GridRange? Translated { get; }
        private readonly bool _remove;

        public static bool operator ==(RangeDeleteResult a, RangeDeleteResult b) => a._remove == b._remove && a.Translated == b.Translated;
        public static bool operator !=(RangeDeleteResult a, RangeDeleteResult b) => !(a == b);
        public override bool Equals(object? obj) => obj is RangeDeleteResult r && this == r;
        public override int GetHashCode() => HashCode.Combine(Translated, _remove);
    }

    private static RangeDeleteResult TranslateRangeDeleteUp(
        GridRange range,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        // Rule cols must be fully within the band column span.
        if (range.Start.Col < bandStartCol || range.End.Col > bandEndCol)
            return RangeDeleteResult.Unchanged;

        // Entirely above deleted region: unchanged.
        if (range.End.Row < deletedStartRow)
            return RangeDeleteResult.Unchanged;

        // Entirely within deleted region: remove.
        if (range.Start.Row >= deletedStartRow && range.End.Row <= deletedEndRow)
            return RangeDeleteResult.Remove;

        // Entirely below deleted region: shift up.
        if (range.Start.Row > deletedEndRow)
        {
            return RangeDeleteResult.Translate(new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row - count, range.Start.Col),
                new CellAddress(range.End.Sheet,   range.End.Row   - count, range.End.Col)));
        }

        // R38-commands-insert-delete-shift-2-1: partial overlap with the delete boundary shrinks
        // to the surviving portion (mirrors ShiftRangeRowsDown/ColumnsDown's overlap branch) instead
        // of being left stale, referencing rows that no longer hold the data they used to.
        var newStartRow = range.Start.Row < deletedStartRow ? range.Start.Row : deletedStartRow;
        var newEndRow = range.End.Row > deletedEndRow ? range.End.Row - count : deletedStartRow - 1;
        return RangeDeleteResult.Translate(new GridRange(
            new CellAddress(range.Start.Sheet, newStartRow, range.Start.Col),
            new CellAddress(range.End.Sheet,   newEndRow,   range.End.Col)));
    }

    private static RangeDeleteResult TranslateRangeDeleteLeft(
        GridRange range,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        // Rule rows must be fully within the band row span.
        if (range.Start.Row < bandStartRow || range.End.Row > bandEndRow)
            return RangeDeleteResult.Unchanged;

        // Entirely left of deleted region: unchanged.
        if (range.End.Col < deletedStartCol)
            return RangeDeleteResult.Unchanged;

        // Entirely within deleted region: remove.
        if (range.Start.Col >= deletedStartCol && range.End.Col <= deletedEndCol)
            return RangeDeleteResult.Remove;

        // Entirely right of deleted region: shift left.
        if (range.Start.Col > deletedEndCol)
        {
            return RangeDeleteResult.Translate(new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col - count),
                new CellAddress(range.End.Sheet,   range.End.Row,   range.End.Col   - count)));
        }

        // R38-commands-insert-delete-shift-2-1: partial overlap shrinks to the surviving portion
        // (mirrors ShiftRangeColumnsDown's overlap branch) instead of being left stale.
        var newStartCol = range.Start.Col < deletedStartCol ? range.Start.Col : deletedStartCol;
        var newEndCol = range.End.Col > deletedEndCol ? range.End.Col - count : deletedStartCol - 1;
        return RangeDeleteResult.Translate(new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, newStartCol),
            new CellAddress(range.End.Sheet,   range.End.Row,   newEndCol)));
    }

    private static bool AdjustAdditionalRangesDeleteUp(
        DataValidation rule,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        var changed = false;
        for (var i = rule.AdditionalRanges.Count - 1; i >= 0; i--)
        {
            var result = TranslateRangeDeleteUp(rule.AdditionalRanges[i], bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count);
            if (result == RangeDeleteResult.Remove)
            {
                rule.AdditionalRanges.RemoveAt(i);
                changed = true;
            }
            else if (result.Translated.HasValue)
            {
                rule.AdditionalRanges[i] = result.Translated.Value;
                changed = true;
            }
        }

        return changed;
    }

    private static bool AdjustAdditionalRangesDeleteLeft(
        DataValidation rule,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        var changed = false;
        for (var i = rule.AdditionalRanges.Count - 1; i >= 0; i--)
        {
            var result = TranslateRangeDeleteLeft(rule.AdditionalRanges[i], bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count);
            if (result == RangeDeleteResult.Remove)
            {
                rule.AdditionalRanges.RemoveAt(i);
                changed = true;
            }
            else if (result.Translated.HasValue)
            {
                rule.AdditionalRanges[i] = result.Translated.Value;
                changed = true;
            }
        }

        return changed;
    }

    // ── CF additional-range band-scoped helpers ───────────────────────────────
    // CF uses IReadOnlyList<GridRange>? (not a mutable List), so we must rebuild
    // and reassign rather than editing in-place like DV does.

    /// <summary>
    /// Applies <paramref name="translate"/> to each entry in <see cref="ConditionalFormat.AdditionalRanges"/>.
    /// Entries for which the delegate returns a new value are replaced; entries returning null are unchanged
    /// (for insert operations, null means "not in the shift band, leave alone").
    /// Returns true when any change was made.
    /// </summary>
    private static bool TranslateCfAdditionalRangesInsert(ConditionalFormat rule, Func<GridRange, GridRange?> translate)
    {
        if (rule.AdditionalRanges is null || rule.AdditionalRanges.Count == 0)
            return false;

        var changed = false;
        var result = new List<GridRange>(rule.AdditionalRanges.Count);
        foreach (var range in rule.AdditionalRanges)
        {
            var translated = translate(range);
            result.Add(translated ?? range);
            if (translated.HasValue)
                changed = true;
        }
        if (changed)
            rule.AdditionalRanges = result;
        return changed;
    }

    /// <summary>
    /// Adjusts each entry in CF's <see cref="ConditionalFormat.AdditionalRanges"/> for a Delete-Shift-Up
    /// band operation. Ranges fully in the deleted zone are removed; ranges below are translated up.
    /// Returns true when any change was made.
    /// </summary>
    private static bool AdjustCfAdditionalRangesDeleteUp(
        ConditionalFormat rule,
        uint bandStartCol, uint bandEndCol,
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        if (rule.AdditionalRanges is null || rule.AdditionalRanges.Count == 0)
            return false;

        var changed = false;
        var result = new List<GridRange>(rule.AdditionalRanges.Count);
        foreach (var range in rule.AdditionalRanges)
        {
            var res = TranslateRangeDeleteUp(range, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count);
            if (res == RangeDeleteResult.Remove)
            {
                changed = true;
            }
            else
            {
                result.Add(res.Translated ?? range);
                if (res.Translated.HasValue)
                    changed = true;
            }
        }
        if (changed)
            rule.AdditionalRanges = result.Count == 0 ? null : result;
        return changed;
    }

    /// <summary>
    /// Adjusts each entry in CF's <see cref="ConditionalFormat.AdditionalRanges"/> for a Delete-Shift-Left
    /// band operation. Ranges fully in the deleted zone are removed; ranges to the right are translated left.
    /// Returns true when any change was made.
    /// </summary>
    private static bool AdjustCfAdditionalRangesDeleteLeft(
        ConditionalFormat rule,
        uint bandStartRow, uint bandEndRow,
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        if (rule.AdditionalRanges is null || rule.AdditionalRanges.Count == 0)
            return false;

        var changed = false;
        var result = new List<GridRange>(rule.AdditionalRanges.Count);
        foreach (var range in rule.AdditionalRanges)
        {
            var res = TranslateRangeDeleteLeft(range, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count);
            if (res == RangeDeleteResult.Remove)
            {
                changed = true;
            }
            else
            {
                result.Add(res.Translated ?? range);
                if (res.Translated.HasValue)
                    changed = true;
            }
        }
        if (changed)
            rule.AdditionalRanges = result.Count == 0 ? null : result;
        return changed;
    }
}
