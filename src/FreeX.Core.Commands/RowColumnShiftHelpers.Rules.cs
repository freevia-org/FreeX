using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    internal static (
        List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? DataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo)>? ConditionalFormats)
        CaptureRuleRanges(Sheet sheet)
    {
        return (
            sheet.DataValidations.Count == 0
                ? null
                : sheet.DataValidations.Select(rule => (rule, rule.AppliesTo, rule.AdditionalRanges.ToList())).ToList(),
            sheet.ConditionalFormats.Count == 0
                ? null
                : sheet.ConditionalFormats.Select(rule => (rule, rule.AppliesTo)).ToList());
    }

    internal static void RestoreRuleRangesInPlace(
        Sheet sheet,
        List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? dataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo)>? conditionalFormats)
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
            foreach (var (rule, appliesTo) in conditionalFormats)
                rule.AppliesTo = appliesTo;

            sheet.ConditionalFormats.NotifyRulesChanged();
        }
    }

    // Full rebuild variant: used when rules may have been removed (e.g. DeleteRows/DeleteColumns).
    internal static void RestoreRuleRanges(
        Sheet sheet,
        List<(DataValidation Rule, GridRange AppliesTo, List<GridRange> AdditionalRanges)>? dataValidations,
        List<(ConditionalFormat Rule, GridRange AppliesTo)>? conditionalFormats)
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
            foreach (var (rule, appliesTo) in conditionalFormats)
            {
                rule.AppliesTo = appliesTo;
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
        foreach (var rule in sheet.ConditionalFormats)
            rule.AppliesTo = ShiftRangeRowsUp(rule.AppliesTo, start, count);
    }

    internal static void ShiftRuleRowsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.DataValidations.Count != 0)
        {
            for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
            {
                var shifted = ShiftRangeRowsDown(sheet.DataValidations[i].AppliesTo, start, count);
                if (shifted is null) sheet.DataValidations.RemoveAt(i);
                else
                {
                    sheet.DataValidations[i].AppliesTo = shifted.Value;
                    ShiftAdditionalRanges(sheet.DataValidations[i], range => ShiftRangeRowsDown(range, start, count));
                }
            }
            sheet.DataValidations.NotifyRulesChanged();
        }
        for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeRowsDown(sheet.ConditionalFormats[i].AppliesTo, start, count);
            if (shifted is null) sheet.ConditionalFormats.RemoveAt(i);
            else sheet.ConditionalFormats[i].AppliesTo = shifted.Value;
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
        foreach (var rule in sheet.ConditionalFormats)
            rule.AppliesTo = ShiftRangeColumnsUp(rule.AppliesTo, start, count);
    }

    internal static void ShiftRuleColumnsDown(Sheet sheet, uint start, uint count)
    {
        if (sheet.DataValidations.Count != 0)
        {
            for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
            {
                var shifted = ShiftRangeColumnsDown(sheet.DataValidations[i].AppliesTo, start, count);
                if (shifted is null) sheet.DataValidations.RemoveAt(i);
                else
                {
                    sheet.DataValidations[i].AppliesTo = shifted.Value;
                    ShiftAdditionalRanges(sheet.DataValidations[i], range => ShiftRangeColumnsDown(range, start, count));
                }
            }
            sheet.DataValidations.NotifyRulesChanged();
        }
        for (int i = sheet.ConditionalFormats.Count - 1; i >= 0; i--)
        {
            var shifted = ShiftRangeColumnsDown(sheet.ConditionalFormats[i].AppliesTo, start, count);
            if (shifted is null) sheet.ConditionalFormats.RemoveAt(i);
            else sheet.ConditionalFormats[i].AppliesTo = shifted.Value;
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

    // ── Band-scoped rule adjustments for Insert/Delete Cells ─────────────────
    // Only rules whose range is FULLY INSIDE the shift band are translated.
    // Partial-overlap rules are left unchanged (documented limitation).

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
        uint deletedStartRow, uint deletedEndRow, uint count)
    {
        bool dvChanged = false;
        for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var rule = sheet.DataValidations[i];
            var result = TranslateRangeDeleteUp(rule.AppliesTo, bandStartCol, bandEndCol, deletedStartRow, deletedEndRow, count);
            if (result == RangeDeleteResult.Remove)
            {
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
                sheet.ConditionalFormats.RemoveAt(i);
                cfChanged = true;
            }
            else if (result.Translated.HasValue)
            {
                rule.AppliesTo = result.Translated.Value;
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
        uint deletedStartCol, uint deletedEndCol, uint count)
    {
        bool dvChanged = false;
        for (int i = sheet.DataValidations.Count - 1; i >= 0; i--)
        {
            var rule = sheet.DataValidations[i];
            var result = TranslateRangeDeleteLeft(rule.AppliesTo, bandStartRow, bandEndRow, deletedStartCol, deletedEndCol, count);
            if (result == RangeDeleteResult.Remove)
            {
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
                sheet.ConditionalFormats.RemoveAt(i);
                cfChanged = true;
            }
            else if (result.Translated.HasValue)
            {
                rule.AppliesTo = result.Translated.Value;
                cfChanged = true;
            }
        }

        if (cfChanged)
            sheet.ConditionalFormats.NotifyRulesChanged();
    }

    // ── Band-scoped translation helpers ──────────────────────────────────────

    /// <summary>
    /// Returns the translated range if it is fully inside the Insert-Down band,
    /// or null if it should be left unchanged (outside the band or partial overlap).
    /// </summary>
    private static GridRange? TranslateRangeInsertDown(
        GridRange range,
        uint bandStartCol, uint bandEndCol,
        uint insertBeforeRow, uint count)
    {
        // Rule cols must be fully within the band column span.
        if (range.Start.Col < bandStartCol || range.End.Col > bandEndCol)
            return null;

        // Rule must start at or below the insert point (fully inside the shift zone).
        if (range.Start.Row < insertBeforeRow)
            return null;

        return new GridRange(
            new CellAddress(range.Start.Sheet, Math.Min(range.Start.Row + count, CellAddress.MaxRow), range.Start.Col),
            new CellAddress(range.End.Sheet,   Math.Min(range.End.Row   + count, CellAddress.MaxRow), range.End.Col));
    }

    /// <summary>
    /// Returns the translated range if it is fully inside the Insert-Right band,
    /// or null if unchanged.
    /// </summary>
    private static GridRange? TranslateRangeInsertRight(
        GridRange range,
        uint bandStartRow, uint bandEndRow,
        uint insertBeforeCol, uint count)
    {
        // Rule rows must be fully within the band row span.
        if (range.Start.Row < bandStartRow || range.End.Row > bandEndRow)
            return null;

        // Rule must start at or right of the insert point.
        if (range.Start.Col < insertBeforeCol)
            return null;

        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, Math.Min(range.Start.Col + count, CellAddress.MaxCol)),
            new CellAddress(range.End.Sheet,   range.End.Row,   Math.Min(range.End.Col   + count, CellAddress.MaxCol)));
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

        // Partial overlap with the delete boundary: leave unchanged.
        return RangeDeleteResult.Unchanged;
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

        // Partial overlap: leave unchanged.
        return RangeDeleteResult.Unchanged;
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
}
