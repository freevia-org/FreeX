using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Command to duplicate a worksheet immediately after the source sheet.</summary>
public sealed class DuplicateSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly string? _requestedName;
    private SheetId? _copySheetId;
    private int _insertIndex;

    public string Label => "Duplicate Sheet";

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

        // R17-table-listobject-3: Sheet.Clone copies StructuredTables verbatim (same Id, Name,
        // and DisplayName as the source's tables), which would otherwise leave two tables in the
        // workbook sharing an identity -> corrupt XLSX (Excel repairs by dropping a table) and
        // ambiguous Table1[...] formula references. Give the copy's tables a workbook-unique
        // identity before the copy joins the workbook. Undo is symmetric for free: Revert removes
        // the whole duplicated sheet, so the source sheet's original table identity is untouched.
        UniquifyClonedTables(ctx.Workbook, copy);

        _insertIndex = sourceIndex + 1;
        _copySheetId = copyId;
        ctx.Workbook.InsertSheet(_insertIndex, copy);
        CopyScopedNamedRangesAndFormulas(ctx.Workbook, _sourceSheetId, copyId);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_copySheetId.HasValue)
            ctx.Workbook.RemoveSheet(_copySheetId.Value);
    }

    /// <summary>
    /// Copies the source sheet's sheet-scoped defined names (plain ranges and formula
    /// expressions) onto the newly duplicated sheet, re-scoped to the copy — matching Excel,
    /// which carries a sheet's local names over to a duplicated copy. The range/formula text
    /// itself is copied verbatim, not remapped to the copy's sheet name, mirroring how cell
    /// formulas are left unrewritten by <see cref="Sheet.Clone"/>.
    /// </summary>
    private static void CopyScopedNamedRangesAndFormulas(Workbook workbook, SheetId sourceSheetId, SheetId copySheetId)
    {
        foreach (var ((name, sheetId), range) in workbook.ScopedNamedRanges.ToList())
        {
            if (sheetId != sourceSheetId)
                continue;

            workbook.TryGetScopedNamedRangeMetadata(name, sheetId, out var metadata);
            workbook.DefineNamedRange(name, range, metadata, copySheetId);
        }

        foreach (var ((name, sheetId), formulaText) in workbook.ScopedNamedFormulas.ToList())
        {
            if (sheetId != sourceSheetId)
                continue;

            workbook.DefineNamedFormula(name, formulaText, copySheetId);
        }
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
    /// </summary>
    private static void UniquifyClonedTables(Workbook workbook, Sheet copy)
    {
        if (copy.StructuredTables.Count == 0)
            return;

        var nextId = NextWorkbookTableId(workbook);
        for (var i = 0; i < copy.StructuredTables.Count; i++)
        {
            var table = copy.StructuredTables[i];
            var newName = GenerateUniqueTableName(workbook, copy, table.Name);
            copy.ReidentifyStructuredTable(i, nextId++, newName);
        }
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
