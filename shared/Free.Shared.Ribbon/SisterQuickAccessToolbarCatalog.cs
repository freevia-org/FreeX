namespace Free.Shared.Ribbon;

public sealed record SisterQuickAccessToolbarActions(
    Action Save,
    Action Undo,
    Action Redo);

public sealed record SisterQuickAccessToolbarCommand(
    string CommandId,
    string Tooltip,
    RibbonCommandIconKind IconKind);

/// <summary>
/// Platform-neutral Save/Undo/Redo Quick Access Toolbar contract shared by the sister apps.
/// Platform renderers own only control construction; command order, text, icons, and dispatch live here.
/// </summary>
public static class SisterQuickAccessToolbarCatalog
{
    public const string SaveCommandId = "Save";
    public const string UndoCommandId = "Undo";
    public const string RedoCommandId = "Redo";

    public static IReadOnlyList<SisterQuickAccessToolbarCommand> DefaultCommands { get; } =
    [
        new(SaveCommandId, "Save (Ctrl+S)", RibbonCommandIconKind.Save),
        new(UndoCommandId, "Undo (Ctrl+Z)", RibbonCommandIconKind.Undo),
        new(RedoCommandId, "Redo (Ctrl+Y)", RibbonCommandIconKind.Redo)
    ];

    public static bool Execute(SisterQuickAccessToolbarActions actions, string commandId)
    {
        ArgumentNullException.ThrowIfNull(actions);

        switch (commandId)
        {
            case SaveCommandId:
                actions.Save();
                return true;
            case UndoCommandId:
                actions.Undo();
                return true;
            case RedoCommandId:
                actions.Redo();
                return true;
            default:
                return false;
        }
    }
}
