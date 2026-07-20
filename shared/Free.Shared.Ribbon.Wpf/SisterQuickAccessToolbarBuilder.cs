using System.Windows;
using System.Windows.Controls;
using Free.Shared.Ribbon;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Builds and dispatches the shared Save/Undo/Redo title-bar Quick Access Toolbar used by the WPF sister apps.
/// Hosts still own the actual commands; this helper owns only the common descriptors and command-id routing.
/// </summary>
public static class SisterQuickAccessToolbarBuilder
{
    public const string SaveCommandId = SisterQuickAccessToolbarCatalog.SaveCommandId;
    public const string UndoCommandId = SisterQuickAccessToolbarCatalog.UndoCommandId;
    public const string RedoCommandId = SisterQuickAccessToolbarCatalog.RedoCommandId;

    public static IReadOnlyList<QuickAccessToolbarItem> BuildDefaultItems() =>
        SisterQuickAccessToolbarCatalog.DefaultCommands
            .Select(command => new QuickAccessToolbarItem(
                command.CommandId,
                command.Tooltip,
                command.IconKind))
            .ToArray();

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

    public static bool Execute(SisterQuickAccessToolbarActions actions, string commandId) =>
        SisterQuickAccessToolbarCatalog.Execute(actions, commandId);
}
