using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

public sealed record SisterQuickAccessToolbarActions(
    Action Save,
    Action Undo,
    Action Redo);

/// <summary>
/// Builds and dispatches the shared Save/Undo/Redo title-bar Quick Access Toolbar used by the WPF sister apps.
/// Hosts still own the actual commands; this helper owns only the common descriptors and command-id routing.
/// </summary>
public static class SisterQuickAccessToolbarBuilder
{
    public const string SaveCommandId = "Save";
    public const string UndoCommandId = "Undo";
    public const string RedoCommandId = "Redo";

    public static IReadOnlyList<QuickAccessToolbarItem> BuildDefaultItems() =>
    [
        new(SaveCommandId, "Save (Ctrl+S)", RibbonCommandIconKind.Save),
        new(UndoCommandId, "Undo (Ctrl+Z)", RibbonCommandIconKind.Undo),
        new(RedoCommandId, "Redo (Ctrl+Y)", RibbonCommandIconKind.Redo)
    ];

    public static QuickAccessToolbarHandle Render(
        Panel host,
        FrameworkElement resourceHost,
        SisterQuickAccessToolbarActions actions,
        QuickAccessToolbarRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(actions);

        return QuickAccessToolbarRenderer.Render(
            host,
            resourceHost,
            BuildDefaultItems(),
            commandId => Execute(actions, commandId),
            options);
    }

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
