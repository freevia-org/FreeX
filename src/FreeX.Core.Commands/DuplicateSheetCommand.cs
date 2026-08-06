using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Command to duplicate a worksheet immediately after the source sheet.</summary>
public sealed class DuplicateSheetCommand : IWorkbookCommand, IWholeWorkbookRecalcCommand, IEstimatesMemory
{
    // R125-commands-undo-byte-budget: Undo of a Duplicate Sheet removes the ENTIRE cloned sheet
    // (every cell, style, drawing, etc.) -- mirrors RemoveSheetCommand's IEstimatesMemory, which
    // uses the same 200 bytes/occupied-cell estimate for the same "whole sheet" retention shape.
    private const int BytesPerCell = 200;

    private readonly SheetId _sourceSheetId;
    private readonly string? _requestedName;
    private SheetId? _copySheetId;
    private int _insertIndex;
    private List<SlicerModel>? _clonedSlicers;
    private List<TimelineModel>? _clonedTimelines;
    private int _copyOccupiedCellCount;

    public string Label => "Duplicate Sheet";

    /// <inheritdoc/>
    /// <remarks>
    /// Estimated from the cloned sheet's occupied-cell count captured once Apply has run (0
    /// before that, in which case CommandBus never actually queries this).
    /// </remarks>
    public int EstimatedBytes => (int)Math.Min((long)_copyOccupiedCellCount * BytesPerCell, int.MaxValue);

    public DuplicateSheetCommand(SheetId sourceSheetId, string? name = null)
    {
        _sourceSheetId = sourceSheetId;
        _requestedName = name;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var source = ctx.GetSheet(_sourceSheetId);
        var sourceIndex = FindSheetIndex(ctx.Workbook, _sourceSheetId);
        if (sourceIndex < 0)
            return CommandGuards.RejectSourceSheetNotFound();

        var name = _requestedName ?? DuplicateSheetNameGenerator.GenerateCopyName(ctx.Workbook, source.Name);
        var validationError = ctx.Workbook.ValidateSheetName(name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        // R17: redo. Sheet.Clone takes the new SheetId as an explicit parameter (unlike
        // Workbook.AddSheet, which always mints a fresh one), so reusing the id captured on the
        // first Apply here is enough to keep a later redo-stack command that captured the
        // original copy id (e.g. an edit on the duplicated sheet) from throwing
        // "Sheet {id} not found" — mirrors AddSheetCommand's R16 redo fix.
        var copyId = _copySheetId ?? SheetId.New();
        var copy = source.Clone(copyId, name);
        copy.ResetViewStateToA1();

        // Sheet.Clone copies CodeName (the VBA identifier) verbatim from the source; regenerate
        // it here so the copy gets a fresh, workbook-unique codeName, matching Excel's Duplicate
        // Sheet behavior and avoiding two sheets sharing the same codeName on save.
        if (!string.IsNullOrWhiteSpace(copy.CodeName))
            copy.CodeName = DuplicateSheetCodeNameGenerator.GenerateUniqueCodeName(ctx.Workbook);

        DuplicateSheetDrawingCloner.CopyDrawingCollections(source, copy, copyId);

        // R103: Slicers/Timelines are workbook-level collections keyed to a host sheet only
        // indirectly (SlicerModel.SourceSheetName / TimelineModel.SourceSheetName), so
        // CopyDrawingCollections above -- which only ever sees the two Sheet objects, not the
        // owning Workbook -- can never reach them. Without this, a slicer/timeline filtering a
        // pivot table on the duplicated sheet silently vanished from the copy even though the
        // pivot table itself is faithfully cloned, unlike real Excel's Duplicate Sheet/Move-or-Copy.
        (_clonedSlicers, _clonedTimelines) = DuplicateSheetDrawingCloner.CopySlicersAndTimelines(ctx.Workbook, source, copy);

        // R17-table-listobject-3: Sheet.Clone copies StructuredTables verbatim (same Id, Name,
        // and DisplayName as the source's tables), which would otherwise leave two tables in the
        // workbook sharing an identity -> corrupt XLSX (Excel repairs by dropping a table) and
        // ambiguous Table1[...] formula references. Give the copy's tables a workbook-unique
        // identity before the copy joins the workbook. Undo is symmetric for free: Revert removes
        // the whole duplicated sheet, so the source sheet's original table identity is untouched.
        var tableRenames = UniquifyClonedTables(ctx.Workbook, copy);

        // R99: Sheet.Clone copied every cell formula (and every table's own
        // CalculatedColumnFormula/TotalsRowFormula) verbatim from the source sheet, including any
        // Table[...] structured reference naming the OLD table name UniquifyClonedTables just
        // renamed away from. Table-name resolution is workbook-global by name
        // (StructuredReferenceResolver), not "whichever table lives on this sheet" -- so without
        // this rewrite, a formula like "=SUM(Table1[Price])" that Sheet.Clone copied onto the
        // duplicate would keep resolving to the SOURCE sheet's still-named table instead of the
        // copy's own renamed one. Mirrors RenameStructuredTableCommand's formula rewrite for the
        // manual-rename path, but deliberately scoped to just this one (not-yet-inserted) sheet:
        // unlike a real rename, every OTHER sheet in the workbook must keep referencing the
        // original table unchanged. Undo needs no snapshot/restore here: Revert discards the whole
        // duplicated sheet (and its StructuredTables) below, taking these rewrites with it.
        RewriteClonedTableReferences(copy, tableRenames);

        _insertIndex = sourceIndex + 1;
        _copySheetId = copyId;
        _copyOccupiedCellCount = copy.GetOccupiedCells().Count;
        ctx.Workbook.InsertSheet(_insertIndex, copy);
        CopyScopedNamedRangesAndFormulas(ctx.Workbook, _sourceSheetId, copyId, source.Name, copy.Name, tableRenames);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_copySheetId.HasValue)
            return;

        // R103: undo the workbook-level Slicer/Timeline clones CopySlicersAndTimelines added --
        // Workbook.RemoveSheet only removes the Sheet itself and its named ranges, it has no idea
        // Slicers/Timelines exist (they're keyed to a host sheet only indirectly, by name), so
        // without this a Duplicate-Sheet-then-Undo would leave the cloned slicer/timeline behind,
        // now dangling (SourceSheetName pointing at a sheet name that no longer exists).
        if (_clonedSlicers is { Count: > 0 })
        {
            foreach (var slicer in _clonedSlicers)
                ctx.Workbook.Slicers.Remove(slicer);
        }

        if (_clonedTimelines is { Count: > 0 })
        {
            foreach (var timeline in _clonedTimelines)
                ctx.Workbook.Timelines.Remove(timeline);
        }

        ctx.Workbook.RemoveSheet(_copySheetId.Value);
    }

    /// <summary>
    /// Copies the source sheet's sheet-scoped defined names (plain ranges and formula
    /// expressions) onto the newly duplicated sheet, re-scoped to the copy — matching Excel,
    /// which carries a sheet's local names over to a duplicated copy AND rebases any RefersTo
    /// that pointed at the sheet's own cells onto the new copy (this is the entire point of a
    /// sheet-local name like a per-sheet "TaxRate" used in template sheets — if it didn't
    /// rebase, every duplicated sheet's local name would silently keep referencing the source
    /// sheet's cells forever). A scoped named range's <see cref="GridRange"/> carries its own
    /// Start.Sheet/End.Sheet (see <see cref="RemapScopedNamedRangeOntoCopy"/>: only rebased
    /// when it actually points at the sheet being duplicated, exactly like
    /// <c>Sheet.Clone.ClonePivotTable</c>'s SourceRange handling — a cross-sheet reference must
    /// keep pointing at the original sheet). A scoped named formula's text is rebased the same
    /// way <see cref="RewriteClonedTableReferences"/> already rebases ordinary cell formulas for
    /// a renamed table, via <see cref="FormulaRewriter.Rewrite"/> with a <see cref="RenameSheetOp"/>
    /// -- which only touches an EXPLICIT sheet-qualified reference matching the source sheet's
    /// name (an unqualified reference already means "this sheet" and needs no rewrite), mirroring
    /// <see cref="Sheet.RewriteSameSheetQualifiedFormula"/>'s same-sheet-qualified rebase already
    /// applied to cell formulas / CF / DV / hyperlinks in <see cref="Sheet.Clone"/> -- except for
    /// any renamed cloned table's structured references, which <paramref name="tableRenames"/>
    /// repoints at the copy's own renamed table for the same reason
    /// <see cref="RewriteClonedTableReferences"/> does for ordinary cell formulas.
    /// </summary>
    private static void CopyScopedNamedRangesAndFormulas(
        Workbook workbook,
        SheetId sourceSheetId,
        SheetId copySheetId,
        string sourceSheetName,
        string copySheetName,
        IReadOnlyList<(string OldName, string NewName)> tableRenames)
    {
        foreach (var ((name, sheetId), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (sheetId != sourceSheetId)
                continue;

            workbook.TryGetScopedNamedRangeMetadata(name, sheetId, out var metadata);
            var remapped = RemapScopedNamedRangeOntoCopy(range, sourceSheetId, copySheetId);
            workbook.DefineNamedRange(name, remapped, metadata, copySheetId);
        }

        foreach (var ((name, sheetId), formulaText) in workbook.ScopedNamedFormulas.ToList())
        {
            if (sheetId != sourceSheetId)
                continue;

            var rebased = FormulaRewriter.Rewrite(formulaText, new RenameSheetOp(sourceSheetName, copySheetName), string.Empty)
                ?? formulaText;
            var rewritten = RewriteFormulaForTableRenames(rebased, tableRenames);
            workbook.DefineNamedFormula(name, rewritten ?? rebased, copySheetId);
        }
    }

    /// <summary>
    /// Rebases a sheet-scoped named range's <see cref="GridRange"/> onto the copy's sheet only
    /// when it actually points at the sheet being duplicated (the overwhelmingly common case for
    /// a sheet-local name, e.g. a per-sheet "TaxRate" used as "=TaxRate" in that sheet's own
    /// formulas) -- a cross-sheet scoped name (e.g. a sheet-local name deliberately authored to
    /// read another sheet's cells) must keep pointing at its original target, matching Excel's
    /// Move-or-Copy behavior (only same-sheet references travel with the copy) and mirroring
    /// <c>Sheet.Clone.ClonePivotTable</c>'s identical SourceRange handling. A <see cref="GridRange"/>
    /// always has Start.Sheet == End.Sheet (enforced by its constructor), so checking Start.Sheet
    /// alone is sufficient.
    /// </summary>
    private static GridRange RemapScopedNamedRangeOntoCopy(GridRange range, SheetId sourceSheetId, SheetId copySheetId)
    {
        if (range.Start.Sheet != sourceSheetId)
            return range;

        return new GridRange(
            new CellAddress(copySheetId, range.Start.Row, range.Start.Col),
            new CellAddress(copySheetId, range.End.Row, range.End.Col));
    }

    private static int FindSheetIndex(Workbook workbook, SheetId sheetId)
    {
        for (var index = 0; index < workbook.Sheets.Count; index++)
        {
            if (workbook.Sheets[index].Id == sheetId)
                return index;
        }

        return -1;
    }

    /// <summary>
    /// R17-table-listobject-3: gives every structured table on the freshly cloned sheet a
    /// workbook-unique Id and Name/DisplayName, so a duplicated sheet never carries a table that
    /// collides with the source's (verbatim-copied) table identity. <paramref name="copy"/> must
    /// not yet be inserted into <paramref name="workbook"/> when this runs, so the uniqueness
    /// checks below (which scan <c>workbook.Sheets</c>) compare only against pre-existing tables.
    /// Returns the (old name, new name) pair for every table renamed, so the caller can rewrite
    /// any formula that referenced a table by its old (pre-rename) name.
    /// </summary>
    private static IReadOnlyList<(string OldName, string NewName)> UniquifyClonedTables(Workbook workbook, Sheet copy)
    {
        if (copy.StructuredTables.Count == 0)
            return [];

        var renames = new List<(string OldName, string NewName)>(copy.StructuredTables.Count);
        var nextId = NextWorkbookTableId(workbook);
        for (var i = 0; i < copy.StructuredTables.Count; i++)
        {
            var table = copy.StructuredTables[i];
            var newName = GenerateUniqueTableName(workbook, copy, table.Name);
            renames.Add((table.Name, newName));
            copy.ReidentifyStructuredTable(i, nextId++, newName);
        }

        return renames;
    }

    /// <summary>
    /// Rewrites every Table[...] structured reference on <paramref name="copy"/> -- ordinary cell
    /// formulas, each cloned table's own
    /// <see cref="StructuredTableColumnModel.CalculatedColumnFormula"/>/<see cref="StructuredTableColumnModel.TotalsRowFormula"/>
    /// metadata, and (R100) every cloned <see cref="ConditionalFormat.FormulaText"/> and
    /// <see cref="DataValidation.Formula1"/>/<see cref="DataValidation.Formula2"/> -- that named
    /// one of the tables <see cref="UniquifyClonedTables"/> just renamed, from its old
    /// (source-sheet) name to its new workbook-unique name. <see cref="Sheet.Clone"/> copies every
    /// cell's formula text, every table's self-reference formula metadata, AND every conditional
    /// format rule / data validation rule's formula text VERBATIM from the source sheet -- only
    /// rebasing same-sheet-qualified sheet-NAME references (<see cref="Sheet.RewriteSameSheetQualifiedFormula"/>
    /// via <c>Sheet.Clone.CloneConditionalFormat</c>/<c>CloneDataValidation</c>), never a table
    /// rename. So without this rewrite a formula such as "=SUM(Table1[Price])" on the copy would
    /// keep resolving -- via <see cref="StructuredReferenceResolver"/>'s workbook-global by-name
    /// lookup -- to the SOURCE sheet's still-named table instead of the copy's own renamed one.
    /// Deliberately scoped to just this one sheet (not the whole workbook, unlike
    /// <see cref="RowColumnShiftHelpers.RewriteAllFormulas"/>): formulas/rules on every OTHER sheet
    /// must keep referencing the original table unchanged, since only this one table identity
    /// moved. Chart series/error-bar formulas are handled separately by
    /// <see cref="DuplicateSheetDrawingCloner.RewriteClonedChartTableReferences"/> since charts are
    /// cloned/rewritten before this sheet's table identities are uniquified.
    /// </summary>
    private static void RewriteClonedTableReferences(
        Sheet copy, IReadOnlyList<(string OldName, string NewName)> renames)
    {
        if (renames.Count == 0)
            return;

        foreach (var address in copy.EnumerateFormulaCells().ToList())
        {
            var cell = copy.GetCell(address);
            if (cell?.FormulaText is null)
                continue;

            var rewritten = RewriteFormulaForTableRenames(cell.FormulaText, renames);
            if (rewritten is not null)
                cell.FormulaText = rewritten;
        }

        foreach (var table in copy.StructuredTables)
        {
            for (var i = 0; i < table.Columns.Count; i++)
            {
                var column = table.Columns[i];
                var calculated = RewriteNullableFormulaForTableRenames(column.CalculatedColumnFormula, renames);
                var totals = RewriteNullableFormulaForTableRenames(column.TotalsRowFormula, renames);
                if (!string.Equals(calculated, column.CalculatedColumnFormula, StringComparison.Ordinal) ||
                    !string.Equals(totals, column.TotalsRowFormula, StringComparison.Ordinal))
                {
                    table.Columns[i] = column with
                    {
                        CalculatedColumnFormula = calculated,
                        TotalsRowFormula = totals
                    };
                }
            }
        }

        foreach (var cf in copy.ConditionalFormats)
        {
            var rewritten = RewriteNullableFormulaForTableRenames(cf.FormulaText, renames);
            if (!string.Equals(rewritten, cf.FormulaText, StringComparison.Ordinal))
                cf.FormulaText = rewritten;
        }

        foreach (var dv in copy.DataValidations)
        {
            var formula1 = RewriteNullableFormulaForTableRenames(dv.Formula1, renames);
            var formula2 = RewriteNullableFormulaForTableRenames(dv.Formula2, renames);
            if (!string.Equals(formula1, dv.Formula1, StringComparison.Ordinal))
                dv.Formula1 = formula1;
            if (!string.Equals(formula2, dv.Formula2, StringComparison.Ordinal))
                dv.Formula2 = formula2;
        }

        DuplicateSheetDrawingCloner.RewriteClonedChartTableReferences(copy, renames);
    }

    /// <summary>
    /// Runs <paramref name="formulaText"/> through <see cref="FormulaRewriter.Rewrite"/> once per
    /// rename in <paramref name="renames"/> (a sheet can host more than one renamed table), and
    /// returns the fully rewritten text, or null if none of the renames touched this formula (a
    /// malformed formula is left untouched, same as <see cref="FormulaRewriter.Rewrite"/>'s own
    /// malformed-formula behavior elsewhere). The host-sheet-name parameter that ordinary
    /// structural rewrites need is irrelevant here: <see cref="RenameTableOp"/> matches purely by
    /// table name, with no sheet-qualification concept, so any non-null placeholder is safe to pass.
    /// </summary>
    private static string? RewriteFormulaForTableRenames(
        string formulaText, IReadOnlyList<(string OldName, string NewName)> renames)
    {
        string? current = null;
        var changed = false;
        foreach (var (oldName, newName) in renames)
        {
            var rewritten = FormulaRewriter.Rewrite(current ?? formulaText, new RenameTableOp(oldName, newName), string.Empty);
            if (rewritten is not null)
            {
                current = rewritten;
                changed = true;
            }
        }

        return changed ? current : null;
    }

    private static string? RewriteNullableFormulaForTableRenames(
        string? formulaText, IReadOnlyList<(string OldName, string NewName)> renames)
    {
        if (string.IsNullOrWhiteSpace(formulaText))
            return formulaText;

        return RewriteFormulaForTableRenames(formulaText, renames) ?? formulaText;
    }

    private static int NextWorkbookTableId(Workbook workbook)
    {
        var maxId = 0;
        foreach (var sheet in workbook.Sheets)
        foreach (var table in sheet.StructuredTables)
            maxId = Math.Max(maxId, table.Id);

        return maxId + 1;
    }

    private static string GenerateUniqueTableName(Workbook workbook, Sheet copy, string sourceName)
    {
        for (var n = 2; n < 10_000; n++)
        {
            var suffix = $"_{n}";
            var baseName = sourceName.Length + suffix.Length <= 255
                ? sourceName
                : sourceName[..(255 - suffix.Length)];
            var candidate = baseName + suffix;

            if (StructuredTableDesignCommandHelpers.ValidateTableName(workbook, candidate) is not null)
                continue;

            // Guard against colliding with a sibling table in the SAME duplicated sheet: `copy`
            // is not yet part of `workbook.Sheets`, so the ValidateTableName scan above cannot
            // see it (a source sheet with more than one table would otherwise let two renamed
            // copies land on the same generated name).
            if (copy.StructuredTables.All(t =>
                    !string.Equals(t.Name, candidate, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(t.DisplayName, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"Table_{Guid.NewGuid():N}"[..31];
    }
}
