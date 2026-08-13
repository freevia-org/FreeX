using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Shell;

public enum WorkbookApplicationCommandVariant
{
    Default,
    QuickAccessToolbar,
    KeyboardShortcut
}

public readonly record struct WorkbookApplicationWorkareaCommandRequest(
    WorkbookApplicationCommandInvocation Invocation,
    WorkbookApplicationCommandVariant Variant = WorkbookApplicationCommandVariant.Default,
    CellAddress? TargetAddress = null,
    uint Index = 0,
    int Direction = 0,
    bool State = false,
    NumberFormatShortcut? NumberFormat = null)
{
    public WorkbookApplicationCommandIntent Intent => Invocation.Route.Intent;
}

public sealed record WorkbookApplicationWorkareaCommandHandlers(
    WorkbookApplicationWorkareaCommandEndpointProfile Endpoints,
    Func<WorkbookApplicationCommandInvocation, CellAddress> ResolveTargetAddress,
    Func<bool> HasSelectedDrawingObject);

/// <summary>
/// Owns the portable intent registration and routing policy for commands inside the workbook workarea.
/// Renderers receive semantic requests and retain only their native effects.
/// </summary>
public static class WorkbookApplicationWorkareaCommandBinder
{
    private static readonly HashSet<WorkbookApplicationCommandIntent> FrameIntents =
    [
        WorkbookApplicationCommandIntent.NewWorkbook,
        WorkbookApplicationCommandIntent.OpenWorkbook,
        WorkbookApplicationCommandIntent.SaveWorkbook,
        WorkbookApplicationCommandIntent.SaveWorkbookAs,
        WorkbookApplicationCommandIntent.PrintWorkbook,
        WorkbookApplicationCommandIntent.ExportPdfXps
    ];

    public static void Bind(
        WorkbookApplicationCommandBindings bindings,
        WorkbookApplicationWorkareaCommandHandlers handlers)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(handlers.Endpoints);

        foreach (var intent in Enum.GetValues<WorkbookApplicationCommandIntent>())
        {
            if (FrameIntents.Contains(intent))
                continue;

            bindings.BindHandledValueTask(intent, invocation => ExecuteAsync(handlers, invocation));
        }
    }

    private static ValueTask<bool> ExecuteAsync(
        WorkbookApplicationWorkareaCommandHandlers handlers,
        WorkbookApplicationCommandInvocation invocation)
    {
        var intent = invocation.Route.Intent;
        if ((intent is WorkbookApplicationCommandIntent.FillDown or WorkbookApplicationCommandIntent.FillRight) &&
            handlers.HasSelectedDrawingObject())
        {
            return ValueTask.FromResult(true);
        }

        var request = intent switch
        {
            WorkbookApplicationCommandIntent.ToggleBold
                or WorkbookApplicationCommandIntent.ToggleItalic
                or WorkbookApplicationCommandIntent.ToggleUnderline =>
                new WorkbookApplicationWorkareaCommandRequest(
                    invocation,
                    invocation.Route.Source == WorkbookApplicationCommandSource.QuickAccessToolbar
                        ? WorkbookApplicationCommandVariant.QuickAccessToolbar
                        : WorkbookApplicationCommandVariant.Default),

            WorkbookApplicationCommandIntent.ReapplyFilter =>
                new WorkbookApplicationWorkareaCommandRequest(
                    invocation,
                    invocation.Route.Source == WorkbookApplicationCommandSource.KeyboardShortcut
                        ? WorkbookApplicationCommandVariant.KeyboardShortcut
                        : WorkbookApplicationCommandVariant.Default),

            WorkbookApplicationCommandIntent.InsertRowAbove =>
                WithIndex(invocation, handlers.ResolveTargetAddress(invocation).Row),
            WorkbookApplicationCommandIntent.InsertRowBelow =>
                WithIndex(invocation, handlers.ResolveTargetAddress(invocation).Row + 1),
            WorkbookApplicationCommandIntent.InsertColumnLeft =>
                WithIndex(invocation, handlers.ResolveTargetAddress(invocation).Col),
            WorkbookApplicationCommandIntent.InsertColumnRight =>
                WithIndex(invocation, handlers.ResolveTargetAddress(invocation).Col + 1),

            WorkbookApplicationCommandIntent.ResolveThreadedComment =>
                WithTargetAndState(invocation, handlers.ResolveTargetAddress(invocation), state: true),
            WorkbookApplicationCommandIntent.UnresolveThreadedComment =>
                WithTargetAndState(invocation, handlers.ResolveTargetAddress(invocation), state: false),
            WorkbookApplicationCommandIntent.ShowHideNote
                or WorkbookApplicationCommandIntent.OpenHyperlink
                or WorkbookApplicationCommandIntent.PivotTableOptions =>
                WithTarget(invocation, handlers.ResolveTargetAddress(invocation)),

            WorkbookApplicationCommandIntent.ActivatePreviousSheet
                or WorkbookApplicationCommandIntent.SelectPreviousSheetGroup =>
                WithDirection(invocation, -1),
            WorkbookApplicationCommandIntent.ActivateNextSheet
                or WorkbookApplicationCommandIntent.SelectNextSheetGroup =>
                WithDirection(invocation, 1),

            WorkbookApplicationCommandIntent.NumberFormatGeneral =>
                WithNumberFormat(invocation, NumberFormatShortcut.General),
            WorkbookApplicationCommandIntent.NumberFormatNumber =>
                WithNumberFormat(invocation, NumberFormatShortcut.Number),
            WorkbookApplicationCommandIntent.NumberFormatTime =>
                WithNumberFormat(invocation, NumberFormatShortcut.Time),
            WorkbookApplicationCommandIntent.NumberFormatDate =>
                WithNumberFormat(invocation, NumberFormatShortcut.Date),
            WorkbookApplicationCommandIntent.NumberFormatCurrency =>
                WithNumberFormat(invocation, NumberFormatShortcut.Currency),
            WorkbookApplicationCommandIntent.NumberFormatPercentage =>
                WithNumberFormat(invocation, NumberFormatShortcut.Percentage),
            WorkbookApplicationCommandIntent.NumberFormatScientific =>
                WithNumberFormat(invocation, NumberFormatShortcut.Scientific),

            _ => new WorkbookApplicationWorkareaCommandRequest(invocation)
        };

        return WorkbookApplicationWorkareaCommandDispatcher.DispatchAsync(request, handlers.Endpoints);
    }

    private static WorkbookApplicationWorkareaCommandRequest WithIndex(
        WorkbookApplicationCommandInvocation invocation,
        uint index) =>
        new(invocation, Index: index);

    private static WorkbookApplicationWorkareaCommandRequest WithDirection(
        WorkbookApplicationCommandInvocation invocation,
        int direction) =>
        new(invocation, Direction: direction);

    private static WorkbookApplicationWorkareaCommandRequest WithTarget(
        WorkbookApplicationCommandInvocation invocation,
        CellAddress targetAddress) =>
        new(invocation, TargetAddress: targetAddress);

    private static WorkbookApplicationWorkareaCommandRequest WithTargetAndState(
        WorkbookApplicationCommandInvocation invocation,
        CellAddress targetAddress,
        bool state) =>
        new(invocation, TargetAddress: targetAddress, State: state);

    private static WorkbookApplicationWorkareaCommandRequest WithNumberFormat(
        WorkbookApplicationCommandInvocation invocation,
        NumberFormatShortcut numberFormat) =>
        new(invocation, NumberFormat: numberFormat);
}
