using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.ThemeUI;

public sealed record WorkbookThemeCommandPlan(
    SetWorkbookThemeCommand Command,
    WorkbookTheme Theme,
    string CommandLabel);

public static class WorkbookThemeCommandPlanner
{
    public const string CommandLabel = "Themes";

    public static WorkbookThemeCommandPlan PlanApply(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new WorkbookThemeCommandPlan(
            new SetWorkbookThemeCommand(theme),
            theme,
            CommandLabel);
    }
}
