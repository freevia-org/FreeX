using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class SetWorkbookThemeCommand : IWorkbookCommand
{
    private readonly WorkbookTheme? _theme;
    private WorkbookTheme? _previousTheme;

    public SetWorkbookThemeCommand(WorkbookTheme? theme)
    {
        _theme = theme;
    }

    public string Label => "Change Workbook Theme";

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_theme is null)
            return new CommandOutcome(false, "Theme is required.");

        // r209: an equal-value setter -- see SetPageOrientationCommand.
        if (ReferenceEquals(ctx.Workbook.Theme, _theme))
            return new CommandOutcome(true, IsNoOp: true);

        _previousTheme = ctx.Workbook.Theme;
        ctx.Workbook.Theme = _theme;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousTheme is not null)
            ctx.Workbook.Theme = _previousTheme;
    }
}
