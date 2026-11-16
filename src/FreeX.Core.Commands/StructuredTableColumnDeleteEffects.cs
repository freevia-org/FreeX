using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Invalidates structured references before a column deletion relocates their owners.</summary>
internal sealed class StructuredTableColumnDeleteEffects
{
    private sealed record Target(StructuredTableModel Table, string[] Removed, string[] MatchNames, bool Whole);
    private readonly List<Target> _targets = [];
    private readonly List<Action> _undo = [];
    private readonly HashSet<CellAddress> _affected = [];
    private long _textBytes;
    internal IReadOnlyCollection<CellAddress> Affected => _affected;
    internal int EstimatedBytes => (int)Math.Min(int.MaxValue, _textBytes + _undo.Count * 256L);

    internal static StructuredTableColumnDeleteEffects Capture(Sheet sheet, uint start, uint count)
    {
        var result = new StructuredTableColumnDeleteEffects();
        var end = (ulong)start + count - 1;
        foreach (var table in sheet.StructuredTables)
        {
            if ((ulong)table.Range.End.Col < start || table.Range.Start.Col > end) continue;
            if (table.Columns.Count != table.Range.ColCount)
                throw new InvalidOperationException("The table's column metadata does not match its range.");
            var whole = start <= table.Range.Start.Col && end >= table.Range.End.Col;
            var removed = FindRemovedNames(sheet, table, start, end);
            result._targets.Add(new Target(table, removed,
                removed.Select(name => name.Replace("'", "", StringComparison.Ordinal)).ToArray(), whole));
        }
        return result;
    }

    // Mirror StructuredReferenceResolver's canonical-live, stale-model, then live
    // precedence. A stale name reused by another column must be invalidated only if
    // its actual resolved owner was deleted.
    internal static string[] FindRemovedNames(Sheet sheet, StructuredTableModel table, uint start, ulong end)
    {
        string Live(int index) => table.HeaderRowCount != 0 &&
            sheet.GetCell(table.Range.Start.Row, table.Range.Start.Col + (uint)index)?.Value is TextValue { Value.Length: > 0 } text
                ? text.Value : table.Columns[index].Name;
        var live = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stale = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var all = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var current = Live(i);
            var stored = table.Columns[i].Name;
            if (!string.IsNullOrEmpty(current)) { live.TryAdd(current, i); all.Add(current); }
            if (!string.IsNullOrEmpty(stored))
            {
                all.Add(stored);
                if (!string.Equals(stored, current, StringComparison.OrdinalIgnoreCase)) stale.TryAdd(stored, i);
            }
        }
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in all)
        {
            var hasLive = live.TryGetValue(name, out var liveIndex);
            var canonical = hasLive && string.Equals(table.Columns[liveIndex].Name, Live(liveIndex), StringComparison.OrdinalIgnoreCase);
            var owner = canonical ? liveIndex : stale.TryGetValue(name, out var staleIndex) ? staleIndex : hasLive ? liveIndex : -1;
            if (owner >= 0 && (ulong)table.Range.Start.Col + (uint)owner >= start &&
                (ulong)table.Range.Start.Col + (uint)owner <= end) result.Add(name);
        }
        return result.ToArray();
    }

    private string? Rewrite(string? text, string? owner)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('[')) return text;
        _textBytes += text.Length * 2L;
        var current = text;
        var unescaped = NormalizeSourceForMatch(current);
        foreach (var target in _targets)
        {
            var namesTarget = current.Contains(target.Table.Name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(target.Table.DisplayName) && current.Contains(target.Table.DisplayName, StringComparison.OrdinalIgnoreCase));
            var ownerMatches = string.Equals(owner, target.Table.Name, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(target.Table.DisplayName) && string.Equals(owner, target.Table.DisplayName, StringComparison.OrdinalIgnoreCase));
            if (!namesTarget && owner is not null && !ownerMatches) continue;
            var namesColumn = target.MatchNames.Any(name => unescaped.Contains(name, StringComparison.OrdinalIgnoreCase));
            // If no alias or removed column text appears, a surviving table's selectors
            // cannot refer to the deleted columns. Whole-table unqualified selectors with
            // unknown owner remain ambiguous and must still be checked conservatively.
            if (!namesTarget && !namesColumn && !ownerMatches && !(target.Whole && owner is null)) continue;
            if (StructuredFormulaColumnDelete.TryRewrite(current, target.Table.Name, target.Table.DisplayName, owner,
                    target.Removed, target.Whole, out var rewritten))
            {
                current = rewritten;
                unescaped = NormalizeSourceForMatch(current);
                continue;
            }
            var relevant = namesTarget || namesColumn || ownerMatches || (target.Whole && owner is null);
            if (relevant) throw new InvalidOperationException("A structured reference could not be safely updated for column deletion.");
        }
        return current;
    }

    private static string NormalizeSourceForMatch(string text)
        => text.Replace("'", "", StringComparison.Ordinal).Replace("]]", "]", StringComparison.Ordinal);

    internal void Apply(Workbook workbook)
    {
        if (_targets.Count == 0) return;
        try { ApplyCore(workbook); }
        catch { Revert(); throw; }
    }

    private void ApplyCore(Workbook workbook)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var address in sheet.EnumerateFormulaCells().ToArray())
            {
                var cell = sheet.GetCell(address);
                if (cell?.FormulaText is not { } old) continue;
                var owner = sheet.StructuredTables.FirstOrDefault(t => t.Range.Contains(address))?.Name;
                var updated = Rewrite(old, owner);
                if (updated == old) continue;
                _undo.Add(() => { var current = sheet.GetCell(address); if (current is not null) RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(current, old); });
                RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(cell, updated);
                _affected.Add(address);
            }
            foreach (var table in sheet.StructuredTables.ToArray())
            {
                var columns = table.Columns.Select(c => c with
                {
                    CalculatedColumnFormula = Rewrite(c.CalculatedColumnFormula, table.Name),
                    TotalsRowFormula = Rewrite(c.TotalsRowFormula, table.Name)
                }).ToArray();
                if (columns.SequenceEqual(table.Columns)) continue;
                var index = sheet.StructuredTables.FindIndex(t => t.Id == table.Id);
                _undo.Add(() => { var at = sheet.StructuredTables.FindIndex(t => t.Id == table.Id); if (at >= 0) sheet.StructuredTables[at] = table; });
                sheet.StructuredTables[index] = StructuredTableDesignCommandHelpers.CopyTable(table, columns: columns);
            }
            foreach (var rule in sheet.ConditionalFormats)
            {
                var owner = sheet.StructuredTables.FirstOrDefault(t => t.Range.Contains(rule.AppliesTo) && (rule.AdditionalRanges ?? []).All(t.Range.Contains))?.Name;
                void Change(string? old, Action<ConditionalFormat, string?> set)
                {
                    var updated = Rewrite(old, owner);
                    if (updated == old) return;
                    _undo.Add(() =>
                    {
                        var restored = sheet.ConditionalFormats.FirstOrDefault(item => item.Id == rule.Id);
                        if (restored is not null) set(restored, old);
                        sheet.ConditionalFormats.NotifyRulesChanged();
                    });
                    set(rule, updated);
                    sheet.ConditionalFormats.NotifyRulesChanged();
                }
                Change(rule.FormulaText, (item, value) => item.FormulaText = value);
                if (rule.MinThresholdType == CfThresholdType.Formula) Change(rule.MinThresholdValue, (item, value) => item.MinThresholdValue = value);
                if (rule.MidThresholdType == CfThresholdType.Formula) Change(rule.MidThresholdValue, (item, value) => item.MidThresholdValue = value);
                if (rule.MaxThresholdType == CfThresholdType.Formula) Change(rule.MaxThresholdValue, (item, value) => item.MaxThresholdValue = value);
                if (rule.DataBarMinThresholdType == CfThresholdType.Formula) Change(rule.DataBarMinThresholdValue, (item, value) => item.DataBarMinThresholdValue = value);
                if (rule.DataBarMaxThresholdType == CfThresholdType.Formula) Change(rule.DataBarMaxThresholdValue, (item, value) => item.DataBarMaxThresholdValue = value);
                for (var i = 0; i < rule.IconSetThresholds.Count; i++)
                {
                    var slot = i;
                    var threshold = rule.IconSetThresholds[slot];
                    if (threshold.Type == CfThresholdType.Formula)
                        Change(threshold.Value, (item, value) => item.IconSetThresholds[slot] = item.IconSetThresholds[slot] with { Value = value });
                }
            }
            foreach (var rule in sheet.DataValidations)
            {
                var owner = sheet.StructuredTables.FirstOrDefault(t => t.Range.Contains(rule.AppliesTo) && rule.AdditionalRanges.All(t.Range.Contains))?.Name;
                var first = rule.Formula1;
                var second = rule.Formula2;
                var updatedFirst = Rewrite(first, owner);
                var updatedSecond = Rewrite(second, owner);
                if (updatedFirst == first && updatedSecond == second) continue;
                _undo.Add(() =>
                {
                    var restored = sheet.DataValidations.FirstOrDefault(item => item.Id == rule.Id);
                    if (restored is not null) { restored.Formula1 = first; restored.Formula2 = second; }
                });
                rule.Formula1 = updatedFirst;
                rule.Formula2 = updatedSecond;
            }
        }

        var namesChanged = false;
        foreach (var (name, old) in workbook.NamedFormulas.ToArray())
        {
            var updated = Rewrite(old, null);
            if (updated == old) continue;
            _undo.Add(() => workbook.NamedFormulas[name] = old);
            workbook.NamedFormulas[name] = updated!;
            namesChanged = true;
        }
        foreach (var (key, old) in workbook.ScopedNamedFormulas.ToArray())
        {
            var updated = Rewrite(old, null);
            if (updated == old) continue;
            _undo.Add(() => workbook.DefineNamedFormula(key.Name, old, key.Sheet));
            workbook.DefineNamedFormula(key.Name, updated!, key.Sheet);
            namesChanged = true;
        }
        if (namesChanged)
            foreach (var sheet in workbook.Sheets)
                foreach (var address in sheet.EnumerateFormulaCells()) _affected.Add(address);
        var charts = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(workbook);
        _undo.Add(() => RowColumnShiftHelpers.RestoreChartVerbatimFormulas(workbook, charts));
        foreach (var sheet in workbook.Sheets)
            foreach (var chart in sheet.Charts)
                ChartFormulaFieldTransformer.Transform(chart, value => Rewrite(value, null));
    }

    internal void Revert()
    {
        for (var i = _undo.Count - 1; i >= 0; i--) _undo[i]();
        _undo.Clear();
    }
}
