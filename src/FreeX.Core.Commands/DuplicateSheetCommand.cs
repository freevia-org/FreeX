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

        var copyId = SheetId.New();
        var copy = source.Clone(copyId, name);
        copy.ResetViewStateToA1();

        // Sheet.Clone copies CodeName (the VBA identifier) verbatim from the source; regenerate
        // it here so the copy gets a fresh, workbook-unique codeName, matching Excel's Duplicate
        // Sheet behavior and avoiding two sheets sharing the same codeName on save.
        if (!string.IsNullOrWhiteSpace(copy.CodeName))
            copy.CodeName = DuplicateSheetCodeNameGenerator.GenerateUniqueCodeName(ctx.Workbook);

        DuplicateSheetDrawingCloner.CopyDrawingCollections(source, copy, copyId);

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
}
