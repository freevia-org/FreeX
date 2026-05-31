namespace FreeX.App.Host;

public enum ViewWindowCommandKind
{
    NewWindow,
    Hide,
    Unhide,
    ViewSideBySide,
    SynchronousScrolling,
    ResetWindowPosition,
    SwitchWindows
}

public enum ViewWindowCommandAvailability
{
    Ready,
    DeferredMultiWindowHosting,
    CannotHideOnlyVisibleWindow,
    NoHiddenWorkbookWindows,
    RequiresSecondVisibleWindow,
    RequiresSideBySidePair
}

public sealed record ViewWorkbookWindowState
{
    public static ViewWorkbookWindowState SingleVisibleWorkbook { get; } = new(1, 0, false, false);

    public ViewWorkbookWindowState(
        int visibleWindowCount,
        int hiddenWindowCount,
        bool isSideBySideActive,
        bool isSynchronousScrollingEnabled)
    {
        VisibleWindowCount = Math.Max(0, visibleWindowCount);
        HiddenWindowCount = Math.Max(0, hiddenWindowCount);
        IsSideBySideActive = isSideBySideActive && VisibleWindowCount > 1;
        IsSynchronousScrollingEnabled = isSynchronousScrollingEnabled && IsSideBySideActive;
    }

    public int VisibleWindowCount { get; }
    public int HiddenWindowCount { get; }
    public bool IsSideBySideActive { get; }
    public bool IsSynchronousScrollingEnabled { get; }
}

public sealed record ViewWindowCommandPlan(
    ViewWindowCommandKind Command,
    bool IsEnabled,
    bool IsChecked,
    ViewWindowCommandAvailability Availability,
    string TooltipDescriptionResourceKey,
    string MessageBodyResourceKey);

public static class ViewWindowCommandPlanner
{
    public static bool TryParseCommandName(string? commandName, out ViewWindowCommandKind command)
    {
        command = commandName?.Trim() switch
        {
            "New Window" => ViewWindowCommandKind.NewWindow,
            "Hide" => ViewWindowCommandKind.Hide,
            "Unhide" => ViewWindowCommandKind.Unhide,
            "View Side by Side" => ViewWindowCommandKind.ViewSideBySide,
            "Synchronous Scrolling" => ViewWindowCommandKind.SynchronousScrolling,
            "Reset Window Position" => ViewWindowCommandKind.ResetWindowPosition,
            "Switch Windows" => ViewWindowCommandKind.SwitchWindows,
            _ => default
        };

        return commandName?.Trim() is
            "New Window" or
            "Hide" or
            "Unhide" or
            "View Side by Side" or
            "Synchronous Scrolling" or
            "Reset Window Position" or
            "Switch Windows";
    }

    public static ViewWindowCommandPlan CreatePlan(
        ViewWindowCommandKind command,
        ViewWorkbookWindowState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var availability = GetAvailability(command, state);
        return new ViewWindowCommandPlan(
            command,
            IsEnabled(availability),
            IsChecked(command, state),
            availability,
            GetTooltipDescriptionResourceKey(command, availability),
            GetMessageBodyResourceKey(availability));
    }

    public static DeferredCommandMessage CreateMessage(
        string commandName,
        ViewWindowCommandPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(plan);

        return new DeferredCommandMessage(
            commandName,
            UiText.Format(plan.MessageBodyResourceKey, commandName));
    }

    private static ViewWindowCommandAvailability GetAvailability(
        ViewWindowCommandKind command,
        ViewWorkbookWindowState state) =>
        command switch
        {
            ViewWindowCommandKind.NewWindow => ViewWindowCommandAvailability.DeferredMultiWindowHosting,
            ViewWindowCommandKind.Hide => state.VisibleWindowCount > 1
                ? ViewWindowCommandAvailability.Ready
                : ViewWindowCommandAvailability.CannotHideOnlyVisibleWindow,
            ViewWindowCommandKind.Unhide => state.HiddenWindowCount > 0
                ? ViewWindowCommandAvailability.Ready
                : ViewWindowCommandAvailability.NoHiddenWorkbookWindows,
            ViewWindowCommandKind.ViewSideBySide => state.VisibleWindowCount > 1
                ? ViewWindowCommandAvailability.Ready
                : ViewWindowCommandAvailability.RequiresSecondVisibleWindow,
            ViewWindowCommandKind.SynchronousScrolling => state.IsSideBySideActive
                ? ViewWindowCommandAvailability.Ready
                : ViewWindowCommandAvailability.RequiresSideBySidePair,
            ViewWindowCommandKind.ResetWindowPosition => state.IsSideBySideActive
                ? ViewWindowCommandAvailability.Ready
                : ViewWindowCommandAvailability.RequiresSideBySidePair,
            ViewWindowCommandKind.SwitchWindows => state.VisibleWindowCount > 1
                ? ViewWindowCommandAvailability.Ready
                : ViewWindowCommandAvailability.RequiresSecondVisibleWindow,
            _ => ViewWindowCommandAvailability.DeferredMultiWindowHosting
        };

    private static bool IsEnabled(ViewWindowCommandAvailability availability) =>
        availability is
            ViewWindowCommandAvailability.Ready or
            ViewWindowCommandAvailability.DeferredMultiWindowHosting;

    private static bool IsChecked(
        ViewWindowCommandKind command,
        ViewWorkbookWindowState state) =>
        command switch
        {
            ViewWindowCommandKind.ViewSideBySide => state.IsSideBySideActive,
            ViewWindowCommandKind.SynchronousScrolling => state.IsSynchronousScrollingEnabled,
            _ => false
        };

    private static string GetTooltipDescriptionResourceKey(
        ViewWindowCommandKind command,
        ViewWindowCommandAvailability availability) =>
        availability switch
        {
            ViewWindowCommandAvailability.CannotHideOnlyVisibleWindow =>
                "MainWindow_TooltipDescription_UnavailableCannotHideOnlyVisibleWorkbookWindow",
            ViewWindowCommandAvailability.NoHiddenWorkbookWindows =>
                "MainWindow_TooltipDescription_UnavailableNoHiddenWorkbookWindows",
            ViewWindowCommandAvailability.RequiresSecondVisibleWindow =>
                command == ViewWindowCommandKind.SwitchWindows
                    ? "MainWindow_TooltipDescription_UnavailableSwitchWindowsRequiresSecondVisibleWindow"
                    : "MainWindow_TooltipDescription_UnavailableRequiresSecondVisibleWorkbookWindow",
            ViewWindowCommandAvailability.RequiresSideBySidePair =>
                command == ViewWindowCommandKind.SynchronousScrolling
                    ? "MainWindow_TooltipDescription_UnavailableSynchronousScrollingRequiresSideBySidePair"
                    : "MainWindow_TooltipDescription_UnavailableResetPositionRequiresSideBySidePair",
            ViewWindowCommandAvailability.Ready => GetReadyTooltipDescriptionResourceKey(command),
            _ => "MainWindow_TooltipDescription_DeferredRequiresMultipleLiveWindowsOverTheSameWorkbookSession"
        };

    private static string GetReadyTooltipDescriptionResourceKey(ViewWindowCommandKind command) =>
        command switch
        {
            ViewWindowCommandKind.Hide => "MainWindow_TooltipDescription_HideTheActiveWorkbookWindow",
            ViewWindowCommandKind.Unhide => "MainWindow_TooltipDescription_UnhideAHiddenWorkbookWindow",
            ViewWindowCommandKind.ViewSideBySide => "MainWindow_TooltipDescription_ViewTwoWorkbookWindowsSideBySide",
            ViewWindowCommandKind.SynchronousScrolling => "MainWindow_TooltipDescription_SynchronizeScrollingAcrossSideBySideWorkbookWindows",
            ViewWindowCommandKind.ResetWindowPosition => "MainWindow_TooltipDescription_ResetTheSideBySideWorkbookWindowLayout",
            ViewWindowCommandKind.SwitchWindows => "MainWindow_TooltipDescription_SwitchToAnotherVisibleWorkbookWindow",
            _ => "MainWindow_TooltipDescription_DeferredRequiresMultipleLiveWindowsOverTheSameWorkbookSession"
        };

    private static string GetMessageBodyResourceKey(ViewWindowCommandAvailability availability) =>
        availability switch
        {
            ViewWindowCommandAvailability.CannotHideOnlyVisibleWindow =>
                "DeferredCommand_ViewWindowCannotHideOnlyVisibleWindow_Body",
            ViewWindowCommandAvailability.NoHiddenWorkbookWindows =>
                "DeferredCommand_ViewWindowNoHiddenWorkbookWindows_Body",
            ViewWindowCommandAvailability.RequiresSecondVisibleWindow =>
                "DeferredCommand_ViewWindowRequiresSecondVisibleWindow_Body",
            ViewWindowCommandAvailability.RequiresSideBySidePair =>
                "DeferredCommand_ViewWindowRequiresSideBySidePair_Body",
            ViewWindowCommandAvailability.Ready =>
                "DeferredCommand_ViewWindowHostNotConnected_Body",
            _ => "DeferredCommand_ViewWindowMultiWindowHosting_Body"
        };
}
