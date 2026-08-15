using Free.Shared.Ribbon;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Ribbon;

public sealed record ParagraphEditingRibbonPorts(
    Action PrepareExecution,
    Func<ListKind> CurrentListKind,
    Action ToggleBullets,
    Action ToggleNumbering,
    IRibbonCommand AlignLeft,
    IRibbonCommand AlignCenter,
    IRibbonCommand AlignRight,
    IRibbonCommand AlignJustify,
    Action IncreaseIndent,
    Action DecreaseIndent,
    Action ToggleSpaceBefore,
    Action ToggleSpaceAfter,
    Action ToggleKeepWithNext,
    Action ToggleKeepLinesTogether,
    Action ToggleWidowControl,
    Action ToggleParagraphBorder,
    IRibbonCommand Sort);

public sealed record ParagraphEditingRibbonStatefulCommand(
    RibbonCommandId Id,
    IRibbonStatefulCommand Command);

public sealed record ParagraphEditingRibbonCommands(
    IReadOnlyList<ParagraphEditingRibbonStatefulCommand> StatefulCommands);

/// <summary>
/// Owns Home/Layout paragraph command identity and execution preparation. Renderers retain only
/// native routed-command, dialog, and editor adapters; the semantic mapping is shared.
/// </summary>
public static class ParagraphEditingRibbonWorkflow
{
    public static IReadOnlyList<FreeWRibbonCommandAction> Actions { get; } =
    [
        FreeWRibbonCommandAction.Bullets,
        FreeWRibbonCommandAction.Numbering,
        FreeWRibbonCommandAction.AlignLeft,
        FreeWRibbonCommandAction.AlignCenter,
        FreeWRibbonCommandAction.AlignRight,
        FreeWRibbonCommandAction.AlignJustify,
        FreeWRibbonCommandAction.IndentIncrease,
        FreeWRibbonCommandAction.IndentDecrease,
        FreeWRibbonCommandAction.SpaceBeforeToggle,
        FreeWRibbonCommandAction.SpaceAfterToggle,
        FreeWRibbonCommandAction.KeepWithNext,
        FreeWRibbonCommandAction.KeepLines,
        FreeWRibbonCommandAction.WidowControl,
        FreeWRibbonCommandAction.ParaBorder,
        FreeWRibbonCommandAction.Sort,
    ];

    public static ParagraphEditingRibbonCommands Register(
        IRibbonCommandRegistry bindings,
        ParagraphEditingRibbonPorts ports)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(ports.PrepareExecution);

        var bullets = BindListToggle(
            bindings,
            ports,
            FreeWRibbonCommandAction.Bullets,
            ListKind.Bullet,
            ports.ToggleBullets);
        var numbering = BindListToggle(
            bindings,
            ports,
            FreeWRibbonCommandAction.Numbering,
            ListKind.Number,
            ports.ToggleNumbering);
        BindCommand(bindings, ports, FreeWRibbonCommandAction.AlignLeft, ports.AlignLeft);
        BindCommand(bindings, ports, FreeWRibbonCommandAction.AlignCenter, ports.AlignCenter);
        BindCommand(bindings, ports, FreeWRibbonCommandAction.AlignRight, ports.AlignRight);
        BindCommand(bindings, ports, FreeWRibbonCommandAction.AlignJustify, ports.AlignJustify);

        var increaseIndent = BindAction(
            bindings,
            ports,
            FreeWRibbonCommandAction.IndentIncrease,
            ports.IncreaseIndent);
        bindings.Register("freew.increase-indent", increaseIndent);

        var decreaseIndent = BindAction(
            bindings,
            ports,
            FreeWRibbonCommandAction.IndentDecrease,
            ports.DecreaseIndent);
        bindings.Register("freew.decrease-indent", decreaseIndent);

        BindAction(bindings, ports, FreeWRibbonCommandAction.SpaceBeforeToggle, ports.ToggleSpaceBefore);
        BindAction(bindings, ports, FreeWRibbonCommandAction.SpaceAfterToggle, ports.ToggleSpaceAfter);
        BindAction(bindings, ports, FreeWRibbonCommandAction.KeepWithNext, ports.ToggleKeepWithNext);
        BindAction(bindings, ports, FreeWRibbonCommandAction.KeepLines, ports.ToggleKeepLinesTogether);
        BindAction(bindings, ports, FreeWRibbonCommandAction.WidowControl, ports.ToggleWidowControl);
        BindAction(bindings, ports, FreeWRibbonCommandAction.ParaBorder, ports.ToggleParagraphBorder);
        BindCommand(bindings, ports, FreeWRibbonCommandAction.Sort, ports.Sort);

        return new ParagraphEditingRibbonCommands(
        [
            new("freew.bullets", bullets),
            new("freew.numbering", numbering),
        ]);
    }

    private static IRibbonCommand BindAction(
        IRibbonCommandRegistry bindings,
        ParagraphEditingRibbonPorts ports,
        FreeWRibbonCommandAction action,
        Action execute) =>
        bindings.BindAction(action, execute, prepareExecution: ports.PrepareExecution);

    private static void BindCommand(
        IRibbonCommandRegistry bindings,
        ParagraphEditingRibbonPorts ports,
        FreeWRibbonCommandAction action,
        IRibbonCommand command) =>
        bindings.Bind(action, new PreparedCommand(ports.PrepareExecution, command));

    private static IRibbonStatefulCommand BindListToggle(
        IRibbonCommandRegistry bindings,
        ParagraphEditingRibbonPorts ports,
        FreeWRibbonCommandAction action,
        ListKind kind,
        Action execute)
    {
        var command = new PreparedCommand(
            ports.PrepareExecution,
            new ParagraphListToggleCommand(execute, ports.CurrentListKind, kind));
        bindings.Bind(action, command);
        return command;
    }

    private sealed class ParagraphListToggleCommand(
        Action execute,
        Func<ListKind> currentListKind,
        ListKind kind) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context) => execute();

        public RibbonCommandState GetState() =>
            new(IsChecked: currentListKind() == kind);
    }

    private sealed class PreparedCommand(Action prepareExecution, IRibbonCommand inner) : IRibbonStatefulCommand
    {
        public void Execute(RibbonCommandContext context)
        {
            prepareExecution();
            inner.Execute(context);
        }

        public RibbonCommandState GetState() =>
            inner is IRibbonStatefulCommand stateful
                ? stateful.GetState()
                : RibbonCommandState.Default;
    }
}
