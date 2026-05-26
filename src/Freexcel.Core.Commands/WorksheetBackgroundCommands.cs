using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class SetWorksheetBackgroundCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetBackgroundImage _background;
    private WorksheetBackgroundImage? _previousBackground;
    private bool _applied;

    public string Label => "Sheet Background";

    public SetWorksheetBackgroundCommand(SheetId sheetId, WorksheetBackgroundImage background)
    {
        _sheetId = sheetId;
        _background = background;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_background.ImageBytes.Length == 0)
            return new CommandOutcome(false, "Background image cannot be empty.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousBackground = sheet.BackgroundImage;
        sheet.BackgroundImage = _background;
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        ctx.GetSheet(_sheetId).BackgroundImage = _previousBackground;
        _applied = false;
    }
}

public sealed class ClearWorksheetBackgroundCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private WorksheetBackgroundImage? _previousBackground;
    private bool _applied;

    public string Label => "Clear Sheet Background";

    public ClearWorksheetBackgroundCommand(SheetId sheetId)
    {
        _sheetId = sheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousBackground = sheet.BackgroundImage;
        sheet.BackgroundImage = null;
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        ctx.GetSheet(_sheetId).BackgroundImage = _previousBackground;
        _applied = false;
    }
}
