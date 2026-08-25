using Free.Shared.Ribbon;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Ribbon;

public enum ViewRibbonBindingAvailability
{
    Omitted,
    Disabled,
}

public sealed record ViewRibbonActionBinding(
    Action? Execute = null,
    ViewRibbonBindingAvailability AvailabilityWhenUnbound = ViewRibbonBindingAvailability.Omitted);

public sealed record ViewRibbonToggleBinding(
    Action? Toggle = null,
    Func<bool>? IsChecked = null,
    ViewRibbonBindingAvailability AvailabilityWhenUnbound = ViewRibbonBindingAvailability.Omitted,
    Action? PrepareExecution = null);

public sealed record ViewRibbonChoiceBinding(
    Action<string>? Apply = null,
    ViewRibbonBindingAvailability AvailabilityWhenUnbound = ViewRibbonBindingAvailability.Omitted);

public sealed record ViewRibbonReadModeBindings(
    ViewRibbonToggleBinding? Toggle = null,
    ViewRibbonChoiceBinding? ColumnWidth = null,
    ViewRibbonChoiceBinding? PageColor = null);

public sealed record ViewRibbonModeBindings(
    ViewRibbonToggleBinding? Focus = null,
    ViewRibbonToggleBinding? PrintLayout = null,
    ViewRibbonToggleBinding? WebLayout = null,
    ViewRibbonToggleBinding? Draft = null,
    ViewRibbonToggleBinding? Outline = null,
    ViewRibbonToggleBinding? PagedEdit = null);

public sealed record ViewRibbonShowBindings(
    ViewRibbonToggleBinding? NavigationPane = null,
    ViewRibbonToggleBinding? RevealFormatting = null,
    ViewRibbonToggleBinding? Gridlines = null,
    ViewRibbonToggleBinding? Ruler = null);

public sealed record ViewRibbonZoomBindings(
    ViewRibbonActionBinding? Dialog = null,
    ViewRibbonActionBinding? ZoomIn = null,
    ViewRibbonActionBinding? ZoomOut = null,
    ViewRibbonActionBinding? Reset100 = null,
    ViewRibbonActionBinding? OnePage = null,
    ViewRibbonActionBinding? PageWidth = null,
    ViewRibbonToggleBinding? MultiplePages = null,
    ViewRibbonToggleBinding? SideToSide = null);

public sealed record ViewRibbonWindowBindings(
    ViewRibbonActionBinding? NewWindow = null,
    ViewRibbonActionBinding? ArrangeAll = null,
    ViewRibbonActionBinding? SwitchWindows = null,
    ViewRibbonToggleBinding? Split = null);

public sealed record ViewRibbonCommandBindings(
    ViewRibbonActionBinding? PrintPreview = null,
    ViewRibbonReadModeBindings? ReadMode = null,
    ViewRibbonModeBindings? Modes = null,
    ViewRibbonShowBindings? Show = null,
    ViewRibbonZoomBindings? Zoom = null,
    ViewRibbonWindowBindings? Window = null,
    bool RegisterCompatibilityAliases = false);

public sealed record ViewRibbonCommands(IRibbonStatefulCommand? Gridlines);

/// <summary>
/// Registers FreeW's renderer-neutral View ribbon commands over host-supplied UI operations.
/// Native surfaces, focus, viewport measurement, and window lifecycle remain in renderer bindings.
/// </summary>
public static class ViewRibbonWorkflow
{
    public static ViewRibbonCommands Register(
        IRibbonCommandRegistry registry,
        ViewRibbonCommandBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(bindings);

        RegisterAction(registry, "freew.print-preview", bindings.PrintPreview);

        var readMode = bindings.ReadMode;
        RegisterToggle(registry, "freew.read-mode", readMode?.Toggle);
        RegisterChoice(
            registry,
            "freew.read-mode-column-narrow",
            FreeWReadModePlanner.NarrowColumn,
            readMode?.ColumnWidth);
        RegisterChoice(
            registry,
            "freew.read-mode-column-default",
            FreeWReadModePlanner.DefaultColumn,
            readMode?.ColumnWidth);
        RegisterChoice(
            registry,
            "freew.read-mode-column-wide",
            FreeWReadModePlanner.WideColumn,
            readMode?.ColumnWidth);
        RegisterChoice(
            registry,
            "freew.read-mode-color-none",
            FreeWReadModePlanner.NoColor,
            readMode?.PageColor);
        RegisterChoice(
            registry,
            "freew.read-mode-color-sepia",
            FreeWReadModePlanner.SepiaColor,
            readMode?.PageColor);
        RegisterChoice(
            registry,
            "freew.read-mode-color-inverse",
            FreeWReadModePlanner.InverseColor,
            readMode?.PageColor);

        var modes = bindings.Modes;
        RegisterToggle(registry, "freew.focus", modes?.Focus);
        var printLayout = RegisterToggle(registry, "freew.print-layout", modes?.PrintLayout);
        var webLayout = RegisterToggle(registry, "freew.web-layout", modes?.WebLayout);
        var draft = RegisterToggle(registry, "freew.draft-view", modes?.Draft);
        RegisterToggle(registry, "freew.outline-view", modes?.Outline);
        RegisterToggle(registry, "freew.paged-edit-view", modes?.PagedEdit);

        var show = bindings.Show;
        var navigationPane = RegisterToggle(registry, "freew.nav-pane", show?.NavigationPane);
        RegisterToggle(registry, "freew.reveal-formatting", show?.RevealFormatting);
        var gridlines = RegisterToggle(registry, "freew.gridlines", show?.Gridlines);
        var ruler = RegisterToggle(registry, "freew.ruler", show?.Ruler);

        var zoom = bindings.Zoom;
        RegisterAction(registry, "freew.zoom-dialog", zoom?.Dialog);
        RegisterAction(registry, "freew.zoom-in", zoom?.ZoomIn);
        RegisterAction(registry, "freew.zoom-out", zoom?.ZoomOut);
        RegisterAction(registry, "freew.zoom-100", zoom?.Reset100);
        RegisterAction(registry, "freew.zoom-one-page", zoom?.OnePage);
        RegisterAction(registry, "freew.zoom-page-width", zoom?.PageWidth);
        RegisterToggle(registry, "freew.zoom-multiple-pages", zoom?.MultiplePages);
        RegisterToggle(registry, "freew.zoom-side-to-side", zoom?.SideToSide);

        var window = bindings.Window;
        RegisterAction(registry, "freew.new-window", window?.NewWindow);
        RegisterAction(registry, "freew.arrange-all", window?.ArrangeAll);
        RegisterAction(registry, "freew.switch-windows", window?.SwitchWindows);
        var split = RegisterToggle(registry, "freew.split-window", window?.Split);

        if (bindings.RegisterCompatibilityAliases)
        {
            RegisterAlias(registry, "freew.printlayout", printLayout);
            RegisterAlias(registry, "freew.weblayout", webLayout);
            RegisterAlias(registry, "freew.draftview", draft);
            RegisterAlias(registry, "freew.navigationpane", navigationPane);
            RegisterAlias(registry, "freew.view-gridlines", gridlines);
            RegisterAlias(registry, "freew.view-ruler", ruler);
            RegisterAlias(registry, "freew.split", split);
        }

        return new ViewRibbonCommands(gridlines);
    }

    private static IRibbonCommand? RegisterAction(
        IRibbonCommandRegistry registry,
        string commandId,
        ViewRibbonActionBinding? binding)
    {
        var command = binding?.Execute is { } execute
            ? new ActionRibbonCommand(execute)
            : CommandFor(binding?.AvailabilityWhenUnbound);
        if (command is not null)
            registry.Register(commandId, command);
        return command;
    }

    private static IRibbonStatefulCommand? RegisterToggle(
        IRibbonCommandRegistry registry,
        string commandId,
        ViewRibbonToggleBinding? binding)
    {
        IRibbonCommand? command = binding?.Toggle is { } toggle && binding.IsChecked is { } isChecked
            ? new FreeWStatefulToggleCommand(toggle, isChecked, binding.PrepareExecution)
            : CommandFor(binding?.AvailabilityWhenUnbound);
        if (command is not null)
            registry.Register(commandId, command);
        return command as IRibbonStatefulCommand;
    }

    private static void RegisterChoice(
        IRibbonCommandRegistry registry,
        string commandId,
        string token,
        ViewRibbonChoiceBinding? binding)
    {
        var command = binding?.Apply is { } apply
            ? new ActionRibbonCommand(() => apply(token))
            : CommandFor(binding?.AvailabilityWhenUnbound);
        if (command is not null)
            registry.Register(commandId, command);
    }

    private static void RegisterAlias(
        IRibbonCommandRegistry registry,
        string alias,
        IRibbonCommand? command)
    {
        if (command is not null)
            registry.Register(alias, command);
    }

    private static IRibbonCommand? CommandFor(ViewRibbonBindingAvailability? availability) =>
        availability switch
        {
            ViewRibbonBindingAvailability.Disabled => FreeWRibbonExecutionProfile.UnavailableCommand,
            _ => null,
        };
}
