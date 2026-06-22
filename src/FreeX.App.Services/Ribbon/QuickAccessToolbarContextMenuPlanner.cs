namespace FreeX.App.Services.Ribbon;

/// <summary>
/// Platform-neutral planner that describes the Quick Access Toolbar (QAT) right-click context menus as
/// declarative command trees, mirroring <see cref="SheetTabContextMenuPlanner"/> and
/// <see cref="Free.Shared.AppServices.StatusBarCustomizeContextMenuPlanner"/>. Two menus live here:
/// <list type="bullet">
/// <item>
/// The <b>customization</b> menu opened from a ribbon command (or a QAT button): a single dynamic item that
/// either adds the command to the QAT or removes it. The host resolves <see cref="QuickAccessToolbarMenuCommand.ResourceKey"/>
/// to a localized header, applies the carried <see cref="QuickAccessToolbarMenuCommand.AutomationId"/>, honours
/// <see cref="QuickAccessToolbarMenuCommand.IsEnabled"/> (Remove is disabled when it would empty the QAT), and
/// dispatches the <see cref="QuickAccessToolbarMenuAction"/> back to the existing apply handler.
/// </item>
/// <item>
/// The <b>history</b> dropdown opened from the Undo/Redo split-button chevrons: a list of one item per
/// progressively-larger history span, or a single disabled placeholder when there is nothing to undo/redo.
/// Each entry carries its 1-based <see cref="QuickAccessToolbarMenuCommand.ActionCount"/> so the host can
/// execute that many undos/redos.
/// </item>
/// </list>
/// Keeping the structure here lets the WPF in-app menus and a future Avalonia native menu single-source their
/// labels, order, enablement, and automation ids from one place. State (current QAT command set, the target
/// command, and the live history entries) is threaded in explicitly so the planner stays platform-neutral.
/// </summary>
public static class QuickAccessToolbarContextMenuPlanner
{
    public const string AddHeaderResourceKey = "MainWindow_QatContext_AddToQuickAccessToolbar";
    public const string RemoveHeaderResourceKey = "MainWindow_QatContext_RemoveFromQuickAccessToolbar";
    public const string AddAutomationId = "AddToQuickAccessToolbarMenuItem";
    public const string RemoveAutomationId = "RemoveFromQuickAccessToolbarMenuItem";

    /// <summary>
    /// Builds the single-item customization menu for <paramref name="commandId"/> given the QAT's current
    /// command set. Emits an "Add" item when the command is not on the QAT, otherwise a "Remove" item that is
    /// disabled when removing it would leave the QAT empty (the QAT always keeps at least one command).
    /// </summary>
    public static IReadOnlyList<QuickAccessToolbarMenuCommand> BuildCustomizationCommands(
        QuickAccessToolbarCustomizationMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var contains = state.CurrentCommandIds.Contains(state.CommandId, StringComparer.OrdinalIgnoreCase);
        var command = contains
            ? new QuickAccessToolbarMenuCommand(
                RemoveHeaderResourceKey,
                QuickAccessToolbarMenuAction.Remove,
                CommandId: state.CommandId,
                IsEnabled: state.CurrentCommandIds.Count > 1,
                AutomationId: RemoveAutomationId)
            : new QuickAccessToolbarMenuCommand(
                AddHeaderResourceKey,
                QuickAccessToolbarMenuAction.Add,
                CommandId: state.CommandId,
                IsEnabled: true,
                AutomationId: AddAutomationId);

        return Freeze([command]);
    }

    /// <summary>
    /// Builds the Undo/Redo history dropdown for <paramref name="state"/>. With no entries, emits a single
    /// disabled placeholder ("No actions to undo"/"No actions to redo"). Otherwise emits one item per history
    /// span: item N carries the entry label and an <see cref="QuickAccessToolbarMenuCommand.ActionCount"/> of N,
    /// plus a stable per-item automation id ("{Undo|Redo}QatHistoryItem{N}").
    /// </summary>
    public static IReadOnlyList<QuickAccessToolbarMenuCommand> BuildHistoryCommands(
        QuickAccessToolbarHistoryMenuState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.EntryLabels.Count == 0)
        {
            // Preserve the host's existing literal placeholders verbatim (these were never resource keys, so
            // routing them through the planner as literal headers avoids introducing new resx entries).
            var placeholderHeader = state.IsRedo ? "No actions to redo" : "No actions to undo";
            return Freeze([QuickAccessToolbarMenuCommand.Placeholder(placeholderHeader)]);
        }

        var prefix = state.IsRedo ? "Redo" : "Undo";
        var commands = new QuickAccessToolbarMenuCommand[state.EntryLabels.Count];
        for (var index = 0; index < state.EntryLabels.Count; index++)
        {
            var actionCount = index + 1;
            commands[index] = new QuickAccessToolbarMenuCommand(
                ResourceKey: "",
                QuickAccessToolbarMenuAction.ExecuteHistory,
                Header: state.EntryLabels[index],
                ActionCount: actionCount,
                AutomationId: $"{prefix}QatHistoryItem{actionCount}");
        }

        return Freeze(commands);
    }

    private static IReadOnlyList<QuickAccessToolbarMenuCommand> Freeze(QuickAccessToolbarMenuCommand[] commands) =>
        Array.AsReadOnly(commands);
}

/// <summary>
/// State for the QAT customization menu: the command being right-clicked and the QAT's current command set.
/// </summary>
public sealed record QuickAccessToolbarCustomizationMenuState(
    string CommandId,
    IReadOnlyList<string> CurrentCommandIds);

/// <summary>
/// State for the QAT Undo/Redo history dropdown: whether this is the Redo button and the live entry labels
/// (already trimmed to the host's display cap, ordered shallowest span first).
/// </summary>
public sealed record QuickAccessToolbarHistoryMenuState(
    bool IsRedo,
    IReadOnlyList<string> EntryLabels);

public enum QuickAccessToolbarMenuAction
{
    None,
    Add,
    Remove,
    ExecuteHistory
}

public sealed record QuickAccessToolbarMenuCommand(
    string ResourceKey,
    QuickAccessToolbarMenuAction Action,
    string? Header = null,
    string? CommandId = null,
    int ActionCount = 0,
    bool IsEnabled = true,
    string? AutomationId = null)
{
    /// <summary>A disabled placeholder item carrying a literal <paramref name="header"/> (no resource lookup).</summary>
    public static QuickAccessToolbarMenuCommand Placeholder(string header) =>
        new("", QuickAccessToolbarMenuAction.None, Header: header, IsEnabled: false);

    /// <summary>Literal header text for history entries (entry labels are not resource keys).</summary>
    public string Header { get; init; } = Header ?? "";

    public string CommandId { get; init; } = CommandId ?? "";

    public string AutomationId { get; init; } = AutomationId ?? "";
}
