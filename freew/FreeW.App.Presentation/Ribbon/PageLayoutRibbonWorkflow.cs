using Free.Shared.Ribbon;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record PageLayoutRibbonPorts(
    Func<PageSettings> GetPageSettings,
    Action<Action<PageSettings>> ApplyPageSettings,
    Func<bool> IsEnabled,
    Action? PrepareExecution = null);

public sealed record PageLayoutRibbonCommand(
    RibbonCommandId Id,
    IRibbonStatefulCommand Command);

public sealed record PageLayoutRibbonCommands(
    IReadOnlyList<PageLayoutRibbonCommand> StatefulCommands);

/// <summary>
/// Owns the renderer-neutral Layout ribbon quick actions. Renderers supply only the current page,
/// the undoable page-settings commit adapter, and their editing-lock state.
/// </summary>
public static class PageLayoutRibbonWorkflow
{
    public static IReadOnlyList<FreeWRibbonCommandAction> Actions { get; } =
    [
        FreeWRibbonCommandAction.Orientation,
        FreeWRibbonCommandAction.Margins,
        FreeWRibbonCommandAction.Size,
        FreeWRibbonCommandAction.ColumnsOne,
        FreeWRibbonCommandAction.ColumnsTwo,
        FreeWRibbonCommandAction.ColumnsThree,
        FreeWRibbonCommandAction.ColumnsLeft,
        FreeWRibbonCommandAction.ColumnsRight,
        FreeWRibbonCommandAction.LineNumbers,
        FreeWRibbonCommandAction.LineNumbersNone,
        FreeWRibbonCommandAction.LineNumbersContinuous,
        FreeWRibbonCommandAction.LineNumbersRestartPage,
        FreeWRibbonCommandAction.LineNumbersRestartSection,
        FreeWRibbonCommandAction.Hyphenation,
        FreeWRibbonCommandAction.HyphenationNone,
        FreeWRibbonCommandAction.HyphenationAuto,
        FreeWRibbonCommandAction.PageValign,
        FreeWRibbonCommandAction.DifferentFirstPage,
    ];

    public static PageLayoutRibbonCommands Register(
        IRibbonCommandRegistry registry,
        PageLayoutRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.GetPageSettings);
        ArgumentNullException.ThrowIfNull(ports.ApplyPageSettings);
        ArgumentNullException.ThrowIfNull(ports.IsEnabled);

        var commands = new List<PageLayoutRibbonCommand>();

        var orientation = Bind(
            FreeWRibbonCommandAction.Orientation,
            PageLayoutCommandPlanner.ToggleOrientation);
        registry.Register("freew.page-orientation", orientation);

        Bind(FreeWRibbonCommandAction.Margins, PageLayoutCommandPlanner.ToggleNormalNarrowMargins);
        Register("freew.page-margins-normal", page =>
            PageLayoutCommandPlanner.ApplyMarginPreset(page, PageMarginPreset.Normal));
        Register("freew.page-margins-narrow", page =>
            PageLayoutCommandPlanner.ApplyMarginPreset(page, PageMarginPreset.Narrow));
        Register("freew.page-margins-wide", page =>
            PageLayoutCommandPlanner.ApplyMarginPreset(page, PageMarginPreset.Wide));

        Bind(FreeWRibbonCommandAction.Size, PageLayoutCommandPlanner.ToggleLetterA4Paper);
        Register("freew.page-size-letter", page =>
            PageLayoutCommandPlanner.ApplyPaperSize(page, PagePaperSizePreset.Letter));
        Register("freew.page-size-a4", page =>
            PageLayoutCommandPlanner.ApplyPaperSize(page, PagePaperSizePreset.A4));

        BindColumnPreset(FreeWRibbonCommandAction.ColumnsOne, PageColumnPreset.One);
        BindColumnPreset(FreeWRibbonCommandAction.ColumnsTwo, PageColumnPreset.Two);
        BindColumnPreset(FreeWRibbonCommandAction.ColumnsThree, PageColumnPreset.Three);
        BindColumnPreset(FreeWRibbonCommandAction.ColumnsLeft, PageColumnPreset.Left);
        BindColumnPreset(FreeWRibbonCommandAction.ColumnsRight, PageColumnPreset.Right);

        Bind(FreeWRibbonCommandAction.LineNumbers, PageLayoutCommandPlanner.CycleLineNumberMode);
        BindLineNumberMode(FreeWRibbonCommandAction.LineNumbersNone, LineNumberMode.None);
        BindLineNumberMode(FreeWRibbonCommandAction.LineNumbersContinuous, LineNumberMode.Continuous);
        BindLineNumberMode(FreeWRibbonCommandAction.LineNumbersRestartPage, LineNumberMode.RestartEachPage);
        BindLineNumberMode(FreeWRibbonCommandAction.LineNumbersRestartSection, LineNumberMode.RestartEachSection);

        Bind(
            FreeWRibbonCommandAction.Hyphenation,
            PageLayoutCommandPlanner.ToggleHyphenation,
            page => page.AutoHyphenation);
        Bind(
            FreeWRibbonCommandAction.HyphenationNone,
            page => page.AutoHyphenation = false,
            page => !page.AutoHyphenation);
        Bind(
            FreeWRibbonCommandAction.HyphenationAuto,
            page => page.AutoHyphenation = true,
            page => page.AutoHyphenation);
        Bind(
            FreeWRibbonCommandAction.PageValign,
            page => page.VerticalAlignment = PageVerticalAlignmentPlanner.Next(page.VerticalAlignment));
        Bind(
            FreeWRibbonCommandAction.DifferentFirstPage,
            page => page.DifferentFirstPage = !page.DifferentFirstPage,
            page => page.DifferentFirstPage);

        return new PageLayoutRibbonCommands(commands);

        PageLayoutCommand Bind(
            FreeWRibbonCommandAction action,
            Action<PageSettings> apply,
            Func<PageSettings, bool>? isChecked = null)
        {
            var id = FreeWRibbonCommandWorkflow.GetPrimaryCommandId(action);
            var command = new PageLayoutCommand(ports, apply, isChecked);
            registry.Bind(action, command);
            commands.Add(new PageLayoutRibbonCommand(id, command));
            return command;
        }

        PageLayoutCommand Register(
            RibbonCommandId id,
            Action<PageSettings> apply,
            Func<PageSettings, bool>? isChecked = null)
        {
            var command = new PageLayoutCommand(ports, apply, isChecked);
            registry.Register(id, command);
            commands.Add(new PageLayoutRibbonCommand(id, command));
            return command;
        }

        void BindColumnPreset(FreeWRibbonCommandAction action, PageColumnPreset preset) =>
            Bind(
                action,
                page => PageLayoutCommandPlanner.ApplyColumnPreset(page, preset),
                page => PageLayoutCommandPlanner.IsColumnPresetChecked(page, preset));

        void BindLineNumberMode(FreeWRibbonCommandAction action, LineNumberMode mode) =>
            Bind(
                action,
                page => page.LineNumberMode = mode,
                page => PageLayoutCommandPlanner.IsLineNumberModeChecked(page, mode));
    }

    private sealed class PageLayoutCommand(
        PageLayoutRibbonPorts ports,
        Action<PageSettings> apply,
        Func<PageSettings, bool>? isChecked) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            if (!ports.IsEnabled())
                return;

            ports.PrepareExecution?.Invoke();
            ports.ApplyPageSettings(apply);
        }

        public RibbonCommandState GetState()
        {
            var enabled = ports.IsEnabled();
            return new RibbonCommandState(
                IsEnabled: enabled,
                IsChecked: isChecked?.Invoke(ports.GetPageSettings()) == true);
        }
    }
}
