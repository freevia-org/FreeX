using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

internal static partial class RowColumnShiftHelpers
{
    /// <summary>
    /// Rewrites all NamedFormulas (defined names whose refers-to is a formula expression)
    /// for a structural insert/delete operation.  Entries that change are recorded in
    /// <paramref name="snapshot"/> so they can be restored by <see cref="RestoreNamedFormulas"/>.
    /// The host-sheet name required by <see cref="FormulaRewriter"/> is the structural operation's
    /// own target sheet (<see cref="RewriteOperationSheetName"/>) — <b>not</b> a fixed workbook
    /// sheet — because a workbook-global NamedFormula has no single owning sheet: a
    /// sheet-unqualified reference inside it is resolved by the evaluator and dependency tracker
    /// relative to whichever sheet the calling cell lives on (<c>Workbook.TryGetNamedFormulaText</c>
    /// takes the caller's <c>contextSheetId</c>; see <c>FormulaEvaluator.References.cs</c> and
    /// <c>RecalcEngine.CollectReferences</c>'s <c>NamedRangeNode</c> case). So an unqualified
    /// reference must be treated as belonging to whichever sheet is currently being edited — the
    /// same convention <see cref="FormulaRewriter.Matches"/> already uses to decide whether an
    /// unqualified <em>cell</em> reference needs shifting for a structural edit on that sheet.
    /// Also rewrites sheet-scoped named formulas; changes are recorded in
    /// <paramref name="scopedSnapshot"/> for undo.
    /// </summary>
    internal static void RewriteNamedFormulas(
        Workbook workbook, RewriteOperation op, Dictionary<string, string> snapshot,
        Dictionary<(string Name, SheetId Sheet), string>? scopedSnapshot = null)
    {
        if (workbook.NamedFormulas.Count > 0)
        {
            var hostSheetName = RewriteOperationSheetName(op) ??
                (workbook.Sheets.Count > 0 ? workbook.Sheets[0].Name : string.Empty);

            foreach (var name in workbook.NamedFormulas.Keys.ToList())
            {
                var original = workbook.NamedFormulas[name];
                var rewritten = FormulaRewriter.Rewrite(original, op, hostSheetName);
                if (rewritten is null || rewritten == original)
                    continue;

                snapshot[name] = original;
                workbook.NamedFormulas[name] = rewritten;
            }
        }

        if (scopedSnapshot is not null && workbook.ScopedNamedFormulas.Count > 0)
        {
            foreach (var ((name, sheetId), original) in workbook.ScopedNamedFormulas.ToList())
            {
                // Use the scope sheet's name as the host-sheet context for the rewriter.
                var sheet = workbook.Sheets.FirstOrDefault(s => s.Id == sheetId);
                var hostSheetName = sheet?.Name ?? string.Empty;
                var rewritten = FormulaRewriter.Rewrite(original, op, hostSheetName);
                if (rewritten is null || rewritten == original)
                    continue;

                scopedSnapshot[(name, sheetId)] = original;
                workbook.DefineNamedFormula(name, rewritten, sheetId);
            }
        }
    }

    /// <summary>
    /// The sheet a structural <see cref="RewriteOperation"/> targets, for the ops that carry one
    /// (row/column insert-delete and the partial insert/delete-cells shift ops used for a
    /// workbook-global NamedFormula's "host sheet" — see <see cref="RewriteNamedFormulas"/>).
    /// Returns null for ops with no single target sheet (e.g. <c>PasteOffsetOp</c>, which always
    /// adjusts regardless of host sheet, or rename/delete-sheet ops, which only ever touch
    /// sheet-qualified references and so never consult the host sheet).
    /// </summary>
    private static string? RewriteOperationSheetName(RewriteOperation op) => op switch
    {
        InsertRowsOp ins => ins.SheetName,
        DeleteRowsOp del => del.SheetName,
        InsertColsOp ins => ins.SheetName,
        DeleteColsOp del => del.SheetName,
        MoveRangeOp move => move.SheetName,
        InsertCellsShiftDownOp ins => ins.SheetName,
        InsertCellsShiftRightOp ins => ins.SheetName,
        DeleteCellsShiftUpOp del => del.SheetName,
        DeleteCellsShiftLeftOp del => del.SheetName,
        _ => null
    };

    /// <summary>
    /// Restores NamedFormulas from a snapshot captured by <see cref="RewriteNamedFormulas"/>.
    /// </summary>
    internal static void RestoreNamedFormulas(Workbook workbook, Dictionary<string, string>? snapshot,
        Dictionary<(string Name, SheetId Sheet), string>? scopedSnapshot = null)
    {
        if (snapshot is not null)
        {
            foreach (var (name, original) in snapshot)
                workbook.NamedFormulas[name] = original;
        }

        if (scopedSnapshot is not null)
        {
            foreach (var ((name, sheetId), original) in scopedSnapshot)
                workbook.DefineNamedFormula(name, original, sheetId);
        }
    }

    internal static Dictionary<string, NamedRangeSnapshot> CaptureNamedRanges(Workbook workbook) =>
        workbook.NamedRanges.ToDictionary(
            pair => pair.Key,
            pair => new NamedRangeSnapshot(
                pair.Value,
                workbook.TryGetNamedRangeMetadata(pair.Key, out var metadata) ? metadata : NamedRangeMetadata.WorkbookScope),
            StringComparer.OrdinalIgnoreCase);

    internal static void RestoreNamedRanges(Workbook workbook, Dictionary<string, NamedRangeSnapshot>? snapshot)
    {
        if (snapshot is null)
            return;

        workbook.NamedRanges.Clear();
        workbook.NamedRangeMetadataByName.Clear();
        foreach (var (name, namedRange) in snapshot)
            workbook.DefineNamedRange(name, namedRange.Range, namedRange.Metadata);
    }

    /// <summary>
    /// Captures a full snapshot of <see cref="Workbook.ScopedNamedRanges"/> so it can be
    /// restored by <see cref="RestoreScopedNamedRanges"/> on undo.
    /// </summary>
    internal static Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>
        CaptureScopedNamedRanges(Workbook workbook)
    {
        var result =
            new Dictionary<(string, SheetId), (GridRange, NamedRangeMetadata)>();
        foreach (var ((name, sheetId), range) in workbook.ScopedNamedRanges)
        {
            workbook.TryGetScopedNamedRangeMetadata(name, sheetId, out var metadata);
            result[(name, sheetId)] = (range, metadata);
        }
        return result;
    }

    /// <summary>
    /// Restores <see cref="Workbook.ScopedNamedRanges"/> from a snapshot created by
    /// <see cref="CaptureScopedNamedRanges"/>.
    /// </summary>
    internal static void RestoreScopedNamedRanges(
        Workbook workbook,
        Dictionary<(string Name, SheetId Sheet), (GridRange Range, NamedRangeMetadata Metadata)>? snapshot)
    {
        if (snapshot is null)
            return;

        // Remove all current scoped ranges then re-add the snapshotted set.
        foreach (var (name, sheetId) in workbook.ScopedNamedRanges.Keys.ToList())
            workbook.RemoveScopedNamedRange(name, sheetId);

        foreach (var ((name, sheetId), (range, metadata)) in snapshot)
            workbook.DefineNamedRange(name, range, metadata, sheetId);
    }

    internal static void ShiftNamedRangeRowsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
                workbook.NamedRanges[name] = ShiftRangeRowsUp(range, start, count);
        }

        foreach (var ((name, scopeSheet), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
            {
                workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata);
                workbook.DefineNamedRange(name, ShiftRangeRowsUp(range, start, count), metadata, scopeSheet);
            }
        }
    }

    internal static void ShiftNamedRangeRowsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeRowsDown(range, start, count);
            if (shifted is null) workbook.RemoveNamedRange(name);
            else workbook.NamedRanges[name] = shifted.Value;
        }

        foreach (var ((name, scopeSheet), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeRowsDown(range, start, count);
            if (shifted is null)
                workbook.RemoveScopedNamedRange(name, scopeSheet);
            else
            {
                workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata);
                workbook.DefineNamedRange(name, shifted.Value, metadata, scopeSheet);
            }
        }
    }

    internal static void ShiftNamedRangeColumnsUp(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
                workbook.NamedRanges[name] = ShiftRangeColumnsUp(range, start, count);
        }

        foreach (var ((name, scopeSheet), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (range.Start.Sheet == sheetId)
            {
                workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata);
                workbook.DefineNamedRange(name, ShiftRangeColumnsUp(range, start, count), metadata, scopeSheet);
            }
        }
    }

    internal static void ShiftNamedRangeColumnsDown(Workbook workbook, SheetId sheetId, uint start, uint count)
    {
        foreach (var (name, range) in workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeColumnsDown(range, start, count);
            if (shifted is null) workbook.RemoveNamedRange(name);
            else workbook.NamedRanges[name] = shifted.Value;
        }

        foreach (var ((name, scopeSheet), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (range.Start.Sheet != sheetId) continue;
            var shifted = ShiftRangeColumnsDown(range, start, count);
            if (shifted is null)
                workbook.RemoveScopedNamedRange(name, scopeSheet);
            else
            {
                workbook.TryGetScopedNamedRangeMetadata(name, scopeSheet, out var metadata);
                workbook.DefineNamedRange(name, shifted.Value, metadata, scopeSheet);
            }
        }
    }
}
