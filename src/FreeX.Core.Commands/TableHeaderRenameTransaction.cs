using System.Globalization;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Tracks the workbook-wide effects of editing structured-table headers.</summary>
internal sealed class TableHeaderRenameTransaction
{
    private static readonly AsyncLocal<int> SuppressionDepth = new();
    internal static T Suppress<T>(Func<T> action)
    {
        SuppressionDepth.Value++;
        try { return action(); }
        finally { SuppressionDepth.Value--; }
    }
    internal static void Suppress(Action action)
    {
        SuppressionDepth.Value++;
        try { action(); }
        finally { SuppressionDepth.Value--; }
    }
    private sealed record Candidate(Sheet Sheet, StructuredTableModel Table, string[] Names, HashSet<int> Edited);
    private sealed record Rename(StructuredTableModel Table, Dictionary<string, string> Names);
    private readonly List<Candidate> _candidates = [];
    private readonly List<Action> _undo = [];
    private readonly HashSet<CellAddress> _affected = [];
    internal IReadOnlyCollection<CellAddress> Affected => _affected;
    private readonly List<GridRange> _invalidTables = [];
    internal bool HasCandidates => _invalidTables.Count != 0 || _candidates.Count != 0;
    internal int EstimatedBytes => (int)Math.Min(int.MaxValue, 256L * _undo.Count + _estimatedTextBytes);
    private long _estimatedTextBytes;

    internal static TableHeaderRenameTransaction CaptureRange(ICommandContext ctx, Sheet sheet, GridRange range)
        => Capture(ctx, IntersectingHeaders(sheet, range));

    internal static IEnumerable<CellAddress> IntersectingHeaders(Sheet sheet, GridRange range)
    {
        foreach (var table in sheet.StructuredTables)
        {
            var row = table.Range.Start.Row;
            if (table.HeaderRowCount == 0 || row < range.Start.Row || row > range.End.Row) continue;
            var first = Math.Max(table.Range.Start.Col, range.Start.Col);
            var last = Math.Min(table.Range.End.Col, range.End.Col);
            if (first > last) continue;
            for (var col = first; ; col++)
            {
                yield return new CellAddress(sheet.Id, row, col);
                if (col == last) break;
            }
        }
    }

    internal static TableHeaderRenameTransaction Capture(ICommandContext ctx, IEnumerable<CellAddress> addresses)
    {
        var result = new TableHeaderRenameTransaction();
        if (SuppressionDepth.Value > 0) return result;
        var tableSheets = ctx.Workbook.Sheets.Where(sheet => sheet.StructuredTables.Count != 0).Select(sheet => sheet.Id).ToHashSet();
        if (tableSheets.Count == 0) return result;
        foreach (var group in addresses.Where(a => tableSheets.Contains(a.Sheet)).Distinct().GroupBy(a => a.Sheet))
        {
            var sheet = ctx.GetSheet(group.Key);
            var edited = group.ToArray();
            foreach (var table in sheet.StructuredTables)
            {
                if (table.HeaderRowCount == 0) continue;
                var indices = edited.Where(a => a.Row == table.Range.Start.Row && a.Col >= table.Range.Start.Col && a.Col <= table.Range.End.Col)
                    .Select(a => checked((int)(a.Col - table.Range.Start.Col))).ToHashSet();
                if (indices.Count == 0) continue;
                if (table.Columns.Count != table.Range.ColCount)
                {
                    result._invalidTables.Add(table.Range);
                    continue;
                }
                var names = table.Columns.Select((c, i) => HeaderName(sheet, table, i, c.Name)).ToArray();
                result._candidates.Add(new Candidate(sheet, table, names, indices));
            }
        }
        return result;
    }

    private static string HeaderName(Sheet sheet, StructuredTableModel table, int index, string fallback)
        => sheet.GetCell(table.Range.Start.Row, table.Range.Start.Col + (uint)index)?.Value is TextValue { Value.Length: > 0 } text
            ? text.Value : fallback;

    private static string? OwnerAt(Sheet sheet, CellAddress address)
        => sheet.StructuredTables.FirstOrDefault(t => address.Row >= t.Range.Start.Row && address.Row <= t.Range.End.Row && address.Col >= t.Range.Start.Col && address.Col <= t.Range.End.Col)?.Name;

    private static string? OwnerForRanges(Sheet sheet, GridRange primary, IEnumerable<GridRange> additional)
        => sheet.StructuredTables.FirstOrDefault(t => t.Range.Contains(primary) && additional.All(t.Range.Contains))?.Name;

    private static bool ContainsChangedHeader(GridRange tableRange, IReadOnlySet<CellAddress> changedHeaders)
        => changedHeaders.Any(address => address.Sheet == tableRange.Start.Sheet &&
            address.Row == tableRange.Start.Row && address.Col >= tableRange.Start.Col && address.Col <= tableRange.End.Col);

    internal void Apply(ICommandContext ctx, IReadOnlySet<CellAddress>? changedHeaders = null)
    {
        if (_invalidTables.Any(tableRange => changedHeaders is null || ContainsChangedHeader(tableRange, changedHeaders)))
            throw new InvalidOperationException("The table's column metadata does not match its header range.");
        var renames = new List<Rename>();
        foreach (var candidate in _candidates)
        {
            if (changedHeaders is not null && !ContainsChangedHeader(candidate.Table.Range, changedHeaders))
                continue;
            var sheet = candidate.Sheet;
            var index = sheet.StructuredTables.FindIndex(t => t.Id == candidate.Table.Id);
            if (index < 0)
                throw new InvalidOperationException("The table changed during a header edit.");
            var table = sheet.StructuredTables[index];
            if (table.Range.Start != candidate.Table.Range.Start || table.Columns.Count < candidate.Table.Columns.Count ||
                !candidate.Table.Columns.Select(c => c.Id).SequenceEqual(table.Columns.Take(candidate.Table.Columns.Count).Select(c => c.Id)))
                throw new InvalidOperationException("The table changed during a header edit.");

            var names = new string[table.Columns.Count];
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Explicitly edited names take priority over existing names; process a batch at once
            // so a swap never temporarily collides with the other original header.
            foreach (var i in Enumerable.Range(0, names.Length).OrderBy(i => candidate.Edited.Contains(i) ? 0 : 1))
            {
                var cell = sheet.GetCell(table.Range.Start.Row, table.Range.Start.Col + (uint)i);
                if (cell?.HasFormula == true)
                    throw new InvalidOperationException("Formula-valued table headers cannot currently be normalized safely.");
                var raw = cell?.Value switch
                {
                    TextValue text => text.Value,
                    NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
                    BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
                    DateTimeValue dateTime => dateTime.ToDateTime().ToShortDateString(),
                    ErrorValue error => error.Code,
                    _ => ""
                };
                var basis = string.IsNullOrWhiteSpace(raw) ? $"Column{i + 1}" : raw;
                var name = basis;
                for (var suffix = 2; !used.Add(name); suffix++) name = basis + suffix.ToString(CultureInfo.InvariantCulture);
                names[i] = name;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < candidate.Names.Length; i++)
            {
                if (!string.Equals(candidate.Names[i], names[i], StringComparison.Ordinal))
                {
                    if (!map.TryAdd(candidate.Names[i], names[i]))
                        throw new InvalidOperationException("Ambiguous original table column names cannot be renamed safely.");
                }
            }
            var columns = table.Columns.Select((c, i) => c with { Name = names[i] }).ToArray();
            if (!columns.SequenceEqual(table.Columns))
            {
                var previous = table;
                _undo.Add(() => { var at = sheet.StructuredTables.FindIndex(t => t.Id == previous.Id); if (at >= 0) sheet.StructuredTables[at] = previous; });
                sheet.StructuredTables[index] = StructuredTableDesignCommandHelpers.CopyTable(table, columns: columns);
                _affected.Add(table.Range.Start);
            }
            for (var i = 0; i < names.Length; i++)
            {
                var address = new CellAddress(sheet.Id, table.Range.Start.Row, table.Range.Start.Col + (uint)i);
                if (sheet.GetCell(address)?.Value is TextValue text && text.Value == names[i]) continue;
                if (sheet.IsProtected && !CommandGuards.CanEditCell(ctx.Workbook, sheet, address))
                    throw new InvalidOperationException("The normalized table header is protected.");
                var snapshot = CellEditCompanionSnapshot.Capture(sheet, address);
                _undo.Add(() => snapshot.Restore(sheet));
                var replacement = Cell.FromValue(new TextValue(names[i]));
                replacement.StyleId = sheet.GetCell(address)?.StyleId ?? sheet.GetStyleOnly(address.Row, address.Col) ?? StyleId.Default;
                sheet.SetCell(address, replacement);
                sheet.RichTextRuns.Remove(address);
                sheet.Hyperlinks.Remove(address);
                sheet.HyperlinkMetadata.Remove(address);
                sheet.CellPhoneticGuides.Remove(address);
                _affected.Add(address);
            }
            if (map.Count > 0) renames.Add(new Rename(table, map));
        }
        if (renames.Count == 0) return;
        RewriteWorkbook(ctx.Workbook, renames);
    }

    private string? Rewrite(string? formula, string? owner, IReadOnlyList<Rename> renames)
    {
        _estimatedTextBytes += formula?.Length * 2L ?? 0;
        if (string.IsNullOrEmpty(formula) || !formula.Contains('[')) return formula;
        var current = formula;
        foreach (var rename in renames)
        {
            if (!StructuredFormulaColumnRename.TryRewrite(current, rename.Table.Name, rename.Table.DisplayName, owner, rename.Names, out var next))
            {
                // Structured selectors escape special characters with apostrophes. Comparing
                // after removing them prevents an escaped old name from evading this guard.
                // Also reject any ambiguous reference that names this table, regardless of
                // whether its selector could be interpreted by our parser.
                var unescaped = current.Replace("'", "", StringComparison.Ordinal);
                var namesTarget = current.Contains(rename.Table.Name, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(rename.Table.DisplayName) && current.Contains(rename.Table.DisplayName, StringComparison.OrdinalIgnoreCase));
                if (namesTarget || rename.Names.Keys.Any(k => unescaped.Contains(k.Replace("'", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("A formula with a potentially affected structured reference could not be rewritten safely.");
                continue;
            }
            current = next;
        }
        return current;
    }

    private void RewriteWorkbook(Workbook workbook, IReadOnlyList<Rename> renames)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var address in sheet.EnumerateFormulaCells().ToArray())
            {
                var cell = sheet.GetCell(address);
                if (cell?.FormulaText is not { } old) continue;
                var owner = OwnerAt(sheet, address);
                var updated = Rewrite(old, owner, renames);
                if (updated == old) continue;
                _undo.Add(() => RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(cell, old));
                RowColumnShiftHelpers.SetFormulaTextPreservingArrayIdentity(cell, updated);
                _affected.Add(address);
            }

            foreach (var table in sheet.StructuredTables.ToArray())
            {
                var columns = table.Columns.Select(c => c with
                {
                    CalculatedColumnFormula = Rewrite(c.CalculatedColumnFormula, table.Name, renames),
                    TotalsRowFormula = Rewrite(c.TotalsRowFormula, table.Name, renames)
                }).ToArray();
                if (columns.SequenceEqual(table.Columns)) continue;
                var tableIndex = sheet.StructuredTables.FindIndex(t => t.Id == table.Id);
                _undo.Add(() => { var at = sheet.StructuredTables.FindIndex(t => t.Id == table.Id); if (at >= 0) sheet.StructuredTables[at] = table; });
                sheet.StructuredTables[tableIndex] = StructuredTableDesignCommandHelpers.CopyTable(table, columns: columns);
            }

            var cfChanged = false;
            foreach (var rule in sheet.ConditionalFormats)
            {
                var owner = OwnerForRanges(sheet, rule.AppliesTo, rule.AdditionalRanges ?? []);
                void Change(string? old, Action<string?> setter)
                {
                    var updated = Rewrite(old, owner, renames);
                    if (updated == old) return;
                    _undo.Add(() => { setter(old); sheet.ConditionalFormats.NotifyRulesChanged(); });
                    setter(updated);
                    cfChanged = true;
                }
                Change(rule.FormulaText, value => rule.FormulaText = value);
                if (rule.MinThresholdType == CfThresholdType.Formula) Change(rule.MinThresholdValue, value => rule.MinThresholdValue = value);
                if (rule.MidThresholdType == CfThresholdType.Formula) Change(rule.MidThresholdValue, value => rule.MidThresholdValue = value);
                if (rule.MaxThresholdType == CfThresholdType.Formula) Change(rule.MaxThresholdValue, value => rule.MaxThresholdValue = value);
                if (rule.DataBarMinThresholdType == CfThresholdType.Formula) Change(rule.DataBarMinThresholdValue, value => rule.DataBarMinThresholdValue = value);
                if (rule.DataBarMaxThresholdType == CfThresholdType.Formula) Change(rule.DataBarMaxThresholdValue, value => rule.DataBarMaxThresholdValue = value);
                for (var i = 0; i < rule.IconSetThresholds.Count; i++)
                {
                    var slot = i;
                    var threshold = rule.IconSetThresholds[slot];
                    if (threshold.Type == CfThresholdType.Formula)
                        Change(threshold.Value, value => rule.IconSetThresholds[slot] = rule.IconSetThresholds[slot] with { Value = value });
                }
            }
            if (cfChanged) sheet.ConditionalFormats.NotifyRulesChanged();
            foreach (var rule in sheet.DataValidations)
            {
                var owner = OwnerForRanges(sheet, rule.AppliesTo, rule.AdditionalRanges);
                var first = rule.Formula1;
                var second = rule.Formula2;
                var updatedFirst = Rewrite(first, owner, renames);
                var updatedSecond = Rewrite(second, owner, renames);
                if (first == updatedFirst && second == updatedSecond) continue;
                _undo.Add(() => { rule.Formula1 = first; rule.Formula2 = second; });
                rule.Formula1 = updatedFirst;
                rule.Formula2 = updatedSecond;
            }
        }

        var chartSnapshot = RowColumnShiftHelpers.CaptureChartVerbatimFormulas(workbook);
        _undo.Add(() => RowColumnShiftHelpers.RestoreChartVerbatimFormulas(workbook, chartSnapshot));
        foreach (var sheet in workbook.Sheets)
            foreach (var chart in sheet.Charts)
                ChartFormulaFieldTransformer.Transform(chart, formula => Rewrite(formula, null, renames));

        var namesChanged = false;
        foreach (var (name, old) in workbook.NamedFormulas.ToArray())
        {
            var updated = Rewrite(old, null, renames);
            if (updated == old) continue;
            _undo.Add(() => workbook.NamedFormulas[name] = old);
            workbook.NamedFormulas[name] = updated!;
            namesChanged = true;
        }
        foreach (var (key, old) in workbook.ScopedNamedFormulas.ToArray())
        {
            var updated = Rewrite(old, null, renames);
            if (updated == old) continue;
            _undo.Add(() => workbook.DefineNamedFormula(key.Name, old, key.Sheet));
            workbook.DefineNamedFormula(key.Name, updated!, key.Sheet);
            namesChanged = true;
        }
        // Dependencies through defined names may not themselves contain the table text.
        // Refresh every formula cell when a name changes, in both forward and undo directions.
        if (namesChanged)
            foreach (var sheet in workbook.Sheets)
                foreach (var address in sheet.EnumerateFormulaCells()) _affected.Add(address);
    }

    internal void Revert()
    {
        for (var i = _undo.Count - 1; i >= 0; i--) _undo[i]();
        _undo.Clear();
    }
}
