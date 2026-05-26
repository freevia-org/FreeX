using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

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
        var sourceIndex = ctx.Workbook.Sheets.ToList().FindIndex(s => s.Id == _sourceSheetId);
        if (sourceIndex < 0)
            return new CommandOutcome(false, "Source sheet was not found.");

        var name = _requestedName ?? GenerateCopyName(ctx.Workbook, source.Name);
        var validationError = ctx.Workbook.ValidateSheetName(name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        var copyId = SheetId.New();
        var copy = source.Clone(copyId, name);

        DuplicateSheetDrawingCloner.CopyDrawingCollections(source, copy, copyId);

        _insertIndex = sourceIndex + 1;
        _copySheetId = copyId;
        ctx.Workbook.InsertSheet(_insertIndex, copy);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_copySheetId.HasValue)
            ctx.Workbook.RemoveSheet(_copySheetId.Value);
    }

    private static string GenerateCopyName(Workbook workbook, string sourceName)
    {
        for (int n = 2; n < 10_000; n++)
        {
            var suffix = $" ({n})";
            var baseName = sourceName.Length + suffix.Length <= 31
                ? sourceName
                : sourceName[..(31 - suffix.Length)];
            var candidate = baseName + suffix;
            if (workbook.ValidateSheetName(candidate) is null)
                return candidate;
        }

        return $"Sheet{Guid.NewGuid():N}"[..31];
    }
}
