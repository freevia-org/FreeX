using FreeX.App.Presentation.Shell;

namespace FreeX.App.Services.Ribbon;

public readonly record struct QuickAccessCommandState(
    bool CanUndo,
    bool CanRedo,
    bool HasActiveWorksheet,
    bool HasSelection)
{
    public QuickAccessCommandState WithSelectionContext(bool hasActiveWorksheet, bool hasSelection) =>
        new(CanUndo, CanRedo, hasActiveWorksheet, hasSelection);
}

public enum QuickAccessCommandAvailability
{
    Never,
    Always,
    Undo,
    Redo,
    Worksheet,
    Selection
}

public static class QuickAccessCommandStateResolver
{
    public static bool CanExecute(string commandId, QuickAccessCommandState state) =>
        WorkbookApplicationCommandRouter.TryRouteQuickAccess(commandId, out var route) &&
        WorkbookApplicationCommandRouter.CanExecute(route, ToApplicationContext(state));

    public static bool CanExecute(QuickAccessCommandAvailability availability, QuickAccessCommandState state) =>
        WorkbookApplicationCommandRouter.CanExecute(
            ToApplicationAvailability(availability),
            ToApplicationContext(state));

    public static QuickAccessCommandAvailability GetAvailability(string commandId) =>
        WorkbookApplicationCommandRouter.TryRouteQuickAccess(commandId, out var route)
            ? ToQuickAccessAvailability(route.Availability)
            : QuickAccessCommandAvailability.Never;

    private static WorkbookApplicationCommandContext ToApplicationContext(QuickAccessCommandState state) =>
        new(state.CanUndo, state.CanRedo, state.HasActiveWorksheet, state.HasSelection);

    private static QuickAccessCommandAvailability ToQuickAccessAvailability(
        WorkbookApplicationCommandAvailability availability) =>
        availability switch
        {
            WorkbookApplicationCommandAvailability.Always => QuickAccessCommandAvailability.Always,
            WorkbookApplicationCommandAvailability.Undo => QuickAccessCommandAvailability.Undo,
            WorkbookApplicationCommandAvailability.Redo => QuickAccessCommandAvailability.Redo,
            WorkbookApplicationCommandAvailability.Worksheet => QuickAccessCommandAvailability.Worksheet,
            WorkbookApplicationCommandAvailability.Selection => QuickAccessCommandAvailability.Selection,
            _ => QuickAccessCommandAvailability.Never
        };

    private static WorkbookApplicationCommandAvailability ToApplicationAvailability(
        QuickAccessCommandAvailability availability) =>
        availability switch
        {
            QuickAccessCommandAvailability.Always => WorkbookApplicationCommandAvailability.Always,
            QuickAccessCommandAvailability.Undo => WorkbookApplicationCommandAvailability.Undo,
            QuickAccessCommandAvailability.Redo => WorkbookApplicationCommandAvailability.Redo,
            QuickAccessCommandAvailability.Worksheet => WorkbookApplicationCommandAvailability.Worksheet,
            QuickAccessCommandAvailability.Selection => WorkbookApplicationCommandAvailability.Selection,
            _ => WorkbookApplicationCommandAvailability.Never
        };
}
