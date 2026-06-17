namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Platform-neutral planner that describes the Backstage recent/pinned file right-click menus as a
/// declarative command tree, mirroring <see cref="SheetTabContextMenuPlanner"/>. The recent-file list and
/// the pinned-file list each expose a near-identical two-item menu (Pin/Unpin + Remove); single-sourcing
/// them here keeps the headers/order/keytips/automation ids identical while removing the XAML duplication.
/// <para>
/// Each command's per-item automation <see cref="AutomationNamePath"/>/<see cref="AutomationHelpTextPath"/>
/// are <c>RecentFileViewModel</c> property paths the host binds against the per-item DataContext (the menu's
/// DataContext flows from its <c>PlacementTarget.DataContext</c>, exactly as the previous XAML did), so the
/// announced name/help text still describe the specific file.
/// </para>
/// </summary>
public static class BackstageRecentFileContextMenuPlanner
{
    private static readonly IReadOnlyList<BackstageRecentFileMenuCommand> RecentCommands = BuildRecentCommands();
    private static readonly IReadOnlyList<BackstageRecentFileMenuCommand> PinnedCommands = BuildPinnedCommands();

    public static IReadOnlyList<BackstageRecentFileMenuCommand> BuildRecentFileCommands() => RecentCommands;

    public static IReadOnlyList<BackstageRecentFileMenuCommand> BuildPinnedFileCommands() => PinnedCommands;

    private static IReadOnlyList<BackstageRecentFileMenuCommand> BuildRecentCommands() =>
        Array.AsReadOnly(new[]
        {
            new BackstageRecentFileMenuCommand(
                "MainWindow_Header_PinToList",
                BackstageRecentFileMenuAction.Pin,
                KeyTip: "P",
                CommandName: "Pin to list",
                AutomationId: "BackstageRecentPinMenuItem",
                AutomationNamePath: "PinAutomationName",
                AutomationHelpTextPath: "PinAutomationHelpText"),
            new BackstageRecentFileMenuCommand(
                "MainWindow_Header_RemoveFromList",
                BackstageRecentFileMenuAction.Remove,
                KeyTip: "R",
                CommandName: "Remove from list",
                AutomationId: "BackstageRecentRemoveMenuItem",
                AutomationNamePath: "RemoveAutomationName",
                AutomationHelpTextPath: "RemoveAutomationHelpText"),
        });

    private static IReadOnlyList<BackstageRecentFileMenuCommand> BuildPinnedCommands() =>
        Array.AsReadOnly(new[]
        {
            new BackstageRecentFileMenuCommand(
                "MainWindow_Header_UnpinFromList",
                BackstageRecentFileMenuAction.Unpin,
                KeyTip: "U",
                CommandName: "Unpin from list",
                AutomationId: "BackstagePinnedUnpinMenuItem",
                AutomationNamePath: "PinAutomationName",
                AutomationHelpTextPath: "PinAutomationHelpText"),
            new BackstageRecentFileMenuCommand(
                "MainWindow_Header_RemoveFromList",
                BackstageRecentFileMenuAction.Remove,
                KeyTip: "R",
                CommandName: "Remove from list",
                AutomationId: "BackstagePinnedRemoveMenuItem",
                AutomationNamePath: "RemoveAutomationName",
                AutomationHelpTextPath: "RemoveAutomationHelpText"),
        });
}

public sealed record BackstageRecentFileMenuCommand(
    string ResourceKey,
    BackstageRecentFileMenuAction Action,
    string? KeyTip = null,
    string? CommandName = null,
    string? AutomationId = null,
    string? AutomationNamePath = null,
    string? AutomationHelpTextPath = null)
{
    public string KeyTip { get; init; } = KeyTip ?? "";

    public string CommandName { get; init; } = CommandName ?? "";

    public string AutomationId { get; init; } = AutomationId ?? "";

    public string AutomationNamePath { get; init; } = AutomationNamePath ?? "";

    public string AutomationHelpTextPath { get; init; } = AutomationHelpTextPath ?? "";
}

public enum BackstageRecentFileMenuAction
{
    Pin,
    Unpin,
    Remove
}
