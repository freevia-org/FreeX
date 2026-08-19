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
    private List<PivotCacheModel>? _clonedPivotCaches;
    private int _copyOccupiedCellCount;

    public string Label => "Duplicate Sheet";

    /// <summary>The stable id minted for the copy after the command has applied.</summary>
    public SheetId? CopySheetId => _copySheetId;

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
        // R139-workbook-protection: an individually-protected sheet must refuse Duplicate of its
        // own tab even when the workbook's structure is not protected -- see RenameSheetCommand's
        // matching comment in SheetCommands.cs.
        if (CommandGuards.RejectIfProtected(source) is { } sheetProtectedOutcome)
            return sheetProtectedOutcome;

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

        // R127-commands-pivot-cache-clone: Sheet.Clone's ClonePivotTable remaps a same-sheet
        // pivot's SourceRange onto the copy but has no Workbook reference to give it a matching,
        // independent PivotCacheModel -- CacheId travels over unchanged, so the copy's pivot still
        // resolves (via CommandGuards.FindPivotCache / the writer's cacheById lookup, both
        // CacheId-keyed) to the exact same cache instance as the source's pivot. Must run AFTER
        // UniquifyClonedTables/ReidentifyStructuredTable above, so a table-backed cache can be
        // rebased onto the copy's own (already-renamed) structured table rather than the source's.
        _clonedPivotCaches = CloneOwnedPivotCaches(ctx.Workbook, source, copy);

        // R151-commands-pivot-clone-identity: Sheet.Clone.ClonePivotTable copies PivotTableModel.Name
        // verbatim from the source, mirroring the exact "two tables sharing an identity -> corrupt
        // XLSX" hazard UniquifyClonedTables above exists to prevent, just for pivot tables. Give the
        // copy's pivot tables a workbook-unique Name before the copy joins the workbook, and repoint
        // every already-cloned slicer/timeline (built by CopySlicersAndTimelines above, before this
        // renumbering ran) that referenced the OLD name onto the new one -- otherwise the CLONED
        // slicer's own SourcePivotTableName keeps naming the source sheet's pivot table, which
        // XlsxSlicerTimelineWriter/XlsxSlicerTimelineStateRewriter's name-keyed ResolvePivotHostTabId
        // then resolves back to the SOURCE sheet's tabId (it always precedes its own copy in workbook
        // order) instead of the copy's own tabId where the slicer and its pivot table actually live.
        UniquifyClonedPivotTables(ctx.Workbook, copy, _clonedSlicers, _clonedTimelines);

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

        // R127-commands-pivot-cache-clone: undo the workbook-level PivotCacheModel clones
        // CloneOwnedPivotCaches added -- Workbook.RemoveSheet only removes the Sheet and its own
        // PivotTables, it has no idea a fresh PivotCacheModel was added to workbook.PivotCaches on
        // its behalf, so without this a Duplicate-Sheet-then-Undo would leave the cloned cache
        // behind, now orphaned (no PivotTableModel references its CacheId any more).
        if (_clonedPivotCaches is { Count: > 0 })
        {
            foreach (var cache in _clonedPivotCaches)
                ctx.Workbook.PivotCaches.Remove(cache);
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
    /// R151-commands-pivot-clone-identity: gives every pivot table on the freshly cloned sheet a
    /// workbook-unique Name, mirroring <see cref="UniquifyClonedTables"/> above for structured
    /// tables (R17) -- <c>Sheet.Clone.ClonePivotTable</c> otherwise copies
    /// <see cref="PivotTableModel.Name"/> verbatim from the source, leaving the duplicate's pivot
    /// table sharing the source's exact identity. <paramref name="copy"/> must not yet be inserted
    /// into <paramref name="workbook"/> when this runs, matching <see cref="UniquifyClonedTables"/>'s
    /// identical constraint. Also repoints every already-cloned <paramref name="clonedSlicers"/>/
    /// <paramref name="clonedTimelines"/> entry (built by
    /// <c>DuplicateSheetDrawingCloner.CopySlicersAndTimelines</c> before this renumbering ran) whose
    /// <c>SourcePivotTableName</c>/<c>ConnectedPivotTableNames</c> named the OLD pivot table name --
    /// otherwise a slicer/timeline that correctly followed its pivot table onto the copy sheet (via
    /// <c>SourceSheetName</c>) would keep referencing a pivot table name that no longer exists there.
    /// </summary>
    private static void UniquifyClonedPivotTables(
        Workbook workbook,
        Sheet copy,
        IReadOnlyList<SlicerModel> clonedSlicers,
        IReadOnlyList<TimelineModel> clonedTimelines)
    {
        if (copy.PivotTables.Count == 0)
            return;

        for (var i = 0; i < copy.PivotTables.Count; i++)
        {
            var oldName = copy.PivotTables[i].Name;
            var newName = GenerateUniquePivotTableName(workbook, copy, oldName);
            if (string.Equals(newName, oldName, StringComparison.Ordinal))
                continue;

            copy.ReidentifyPivotTable(i, newName);

            foreach (var slicer in clonedSlicers)
            {
                if (string.Equals(slicer.SourcePivotTableName, oldName, StringComparison.OrdinalIgnoreCase))
                    slicer.SourcePivotTableName = newName;

                for (var j = 0; j < slicer.ConnectedPivotTableNames.Count; j++)
                {
                    if (string.Equals(slicer.ConnectedPivotTableNames[j], oldName, StringComparison.OrdinalIgnoreCase))
                        slicer.ConnectedPivotTableNames[j] = newName;
                }
            }

            foreach (var timeline in clonedTimelines)
            {
                if (string.Equals(timeline.SourcePivotTableName, oldName, StringComparison.OrdinalIgnoreCase))
                    timeline.SourcePivotTableName = newName;

                for (var j = 0; j < timeline.ConnectedPivotTableNames.Count; j++)
                {
                    if (string.Equals(timeline.ConnectedPivotTableNames[j], oldName, StringComparison.OrdinalIgnoreCase))
                        timeline.ConnectedPivotTableNames[j] = newName;
                }
            }
        }
    }

    /// <summary>
    /// Generates a workbook-unique pivot table name derived from <paramref name="sourceName"/>,
    /// mirroring <see cref="GenerateUniqueTableName"/>'s "_N" suffix scheme for structured tables.
    /// </summary>
    private static string GenerateUniquePivotTableName(Workbook workbook, Sheet copy, string sourceName)
    {
        for (var n = 2; n < 10_000; n++)
        {
            var suffix = $"_{n}";
            var baseName = sourceName.Length + suffix.Length <= 255
                ? sourceName
                : sourceName[..(255 - suffix.Length)];
            var candidate = baseName + suffix;

            if (workbook.Sheets.Any(s => s.PivotTables.Any(p =>
                    string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase))))
                continue;

            // Guard against colliding with a sibling pivot table in the SAME duplicated sheet:
            // `copy` is not yet part of `workbook.Sheets`, so the scan above cannot see it (a source
            // sheet with more than one pivot table would otherwise let two renamed copies land on
            // the same generated name).
            if (copy.PivotTables.All(p => !string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return $"PivotTable_{Guid.NewGuid():N}";
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

    /// <summary>
    /// R127-commands-pivot-cache-clone: gives every pivot table on the freshly cloned sheet whose
    /// <see cref="PivotTableModel.SourceRange"/> Sheet.Clone's ClonePivotTable already remapped onto
    /// the copy (the same-sheet case -- see that method's doc comment) its own, independent
    /// <see cref="PivotCacheModel"/>, instead of leaving <see cref="PivotTableModel.CacheId"/>
    /// pointing at the exact same cache instance the source sheet's pivot still uses.
    /// <see cref="CommandGuards.FindPivotCache"/> and every save-time writer (<c>XlsxPivotTableWriter</c>'s
    /// <c>cacheById</c>/<c>cachePartById</c>) resolve a pivot's cache purely by CacheId against the
    /// flat, non-sheet-scoped <c>workbook.PivotCaches</c> list, so without this the duplicate's own
    /// cache stays split-brain forever: SourceRange follows the copy, but
    /// SourceSheetName/SourceReference/SourceTableId on the SHARED cache instance keep describing
    /// the ORIGINAL sheet's data -- and a table-backed cache's next refresh
    /// (<c>PivotTableRefreshService.Refresh</c>, which resolves the live table by
    /// <c>cache.SourceTableId</c>/<c>SourceTableName</c> across the WHOLE workbook, not just this
    /// sheet) silently snaps the copy's own <c>pivotTable.SourceRange</c> back onto the ORIGINAL
    /// table's range, corrupting the on-screen render too, not just the saved file.
    ///
    /// A cross-sheet-sourced pivot (SourceRange left untouched by ClonePivotTable) correctly keeps
    /// sharing the original cache, mirroring its SourceRange correctly staying on the original
    /// sheet. Two pivots on the source sheet that shared ONE cache get exactly one cloned cache
    /// between them too (deduped by the OLD CacheId), preserving that sharing relationship on the
    /// copy rather than silently multiplying the cache count. Must run AFTER
    /// <see cref="UniquifyClonedTables"/> so a table-backed cache can rebase
    /// SourceTableId/SourceTableName onto the copy's own already-renamed table.
    /// </summary>
    private static List<PivotCacheModel> CloneOwnedPivotCaches(Workbook workbook, Sheet source, Sheet copy)
    {
        var addedCaches = new List<PivotCacheModel>();
        if (copy.PivotTables.Count == 0)
            return addedCaches;

        var clonedByOldCacheId = new Dictionary<int, PivotCacheModel>();
        for (var i = 0; i < copy.PivotTables.Count; i++)
        {
            var clonedPt = copy.PivotTables[i];
            if (clonedPt.SourceRange.Start.Sheet != copy.Id)
                continue; // Cross-sheet source: correctly keeps sharing the original cache.

            var oldCacheId = source.PivotTables[i].CacheId;
            if (!clonedByOldCacheId.TryGetValue(oldCacheId, out var newCache))
            {
                var originalCache = workbook.PivotCaches.FirstOrDefault(c => c.CacheId == oldCacheId);
                if (originalCache is null)
                    continue; // No registered cache object to clone from (e.g. a bare test fixture).

                newCache = CloneRedirectedPivotCache(workbook, originalCache, source, copy);
                clonedByOldCacheId[oldCacheId] = newCache;
                workbook.PivotCaches.Add(newCache);
                addedCaches.Add(newCache);
            }

            clonedPt.CacheId = newCache.CacheId;
        }

        return addedCaches;
    }

    /// <summary>
    /// Builds the copy's own <see cref="PivotCacheModel"/> from <paramref name="original"/>, rebasing
    /// only what identifies WHERE the data lives (SourceSheetName always becomes the copy's own name;
    /// SourceTableName/SourceTableId rebased onto the copy's matching structured table when the cache
    /// is table-backed) -- SourceReference is left verbatim since <see cref="GridRange.ToString"/> is
    /// a bare A1 string with no sheet qualifier, so it already reads correctly against whichever sheet
    /// SourceSheetName now names. Every other field (style/refresh/connection settings, Fields,
    /// CalculatedItems) is copied as-is: the copy's cell data is an exact clone of the source's at
    /// duplication time, so the source's already-computed SharedItems/field metadata describes the
    /// copy's data just as accurately.
    /// </summary>
    private static PivotCacheModel CloneRedirectedPivotCache(Workbook workbook, PivotCacheModel original, Sheet source, Sheet copy)
    {
        var newSourceTableName = original.SourceTableName;
        var newSourceTableId = original.SourceTableId;
        if (original.SourceType == PivotCacheSourceType.Table)
        {
            var sourceTableIndex = original.SourceTableId is { } sourceTableId
                ? source.StructuredTables.ToList().FindIndex(t => t.Id == sourceTableId)
                : source.StructuredTables.ToList().FindIndex(t =>
                    string.Equals(t.Name, original.SourceTableName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(t.DisplayName, original.SourceTableName, StringComparison.OrdinalIgnoreCase));

            if (sourceTableIndex >= 0 && sourceTableIndex < copy.StructuredTables.Count)
            {
                // Sheet.Clone preserves StructuredTables order 1:1 with the source sheet's list, so
                // the same index in copy.StructuredTables (already reidentified by
                // UniquifyClonedTables by the time this runs) is the copy's own version of the same
                // table.
                var copyTable = copy.StructuredTables[sourceTableIndex];
                newSourceTableName = copyTable.Name;
                newSourceTableId = original.SourceTableId is not null ? copyTable.Id : null;
            }
        }

        var newCache = new PivotCacheModel
        {
            CacheId = NextCacheId(workbook),
            SourceType = original.SourceType,
            SourceSheetName = copy.Name,
            SourceReference = original.SourceReference,
            SourceTableName = newSourceTableName,
            SourceTableId = newSourceTableId,
            // R127B-commands-pivot-cache-clone-packagepart: deliberately NOT original.PackagePart.
            // PackagePart is the exact package-part path (e.g.
            // "xl/pivotCache/pivotCacheDefinition1.xml") the SOURCE cache was loaded from/last saved
            // to; copying it verbatim leaves the newly-minted cache and the original cache both
            // claiming that identical path in workbook.PivotCaches. XlsxFileAdapter's patch-save
            // eligibility guard (TryAddPatchSafePivotPackagePaths) keys a dictionary by that same
            // path across ALL of workbook.PivotCaches, so a duplicate throws an ArgumentException the
            // first time ANY sheet's patch-save eligibility is checked -- silently downgrading every
            // subsequent save of the whole workbook to the slow full-regenerate path. Leaving it
            // empty matches the established "brand-new pivot cache has no PackagePart yet"
            // convention the guard already tolerates (it filters out blank PackagePart entries before
            // building the dictionary); the full-write path always mints a fresh part path anyway.
            PackagePart = string.Empty,
            ConnectionId = original.ConnectionId,
            IsOlap = original.IsOlap,
            RefreshOnLoad = original.RefreshOnLoad,
            SaveData = original.SaveData,
            EnableRefresh = original.EnableRefresh,
            PreserveSourceSortFilter = original.PreserveSourceSortFilter,
            MissingItemsLimit = original.MissingItemsLimit,
            RecordCount = original.RecordCount,
            CreatedVersion = original.CreatedVersion,
            MinRefreshableVersion = original.MinRefreshableVersion,
            RefreshedVersion = original.RefreshedVersion,
            RefreshedBy = original.RefreshedBy,
            RefreshedDateIso = original.RefreshedDateIso,
            RawRecordsXml = original.RawRecordsXml,
        };
        newCache.Fields.AddRange(original.Fields);
        newCache.CalculatedItems.AddRange(original.CalculatedItems);
        return newCache;
    }

    private static int NextCacheId(Workbook workbook) =>
        workbook.PivotCaches.Count == 0
            ? 1
            : workbook.PivotCaches.Max(cache => cache.CacheId) + 1;

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
