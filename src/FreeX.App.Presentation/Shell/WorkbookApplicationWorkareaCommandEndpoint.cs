using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Shell;

public delegate ValueTask<bool> WorkbookApplicationWorkareaEndpoint();

public delegate ValueTask<bool> WorkbookApplicationWorkareaEndpoint<in T>(T argument);

public delegate ValueTask<bool> WorkbookApplicationWorkareaEndpoint<in T1, in T2>(
    T1 firstArgument,
    T2 secondArgument);

/// <summary>Adapts native synchronous and asynchronous operations into workarea endpoints.</summary>
public static class WorkbookApplicationWorkareaCommandEndpoint
{
    public static WorkbookApplicationWorkareaEndpoint Handled(Action endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return () =>
        {
            endpoint();
            return ValueTask.FromResult(true);
        };
    }

    public static WorkbookApplicationWorkareaEndpoint Handled(Func<Task> endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return async () =>
        {
            await endpoint();
            return true;
        };
    }

    public static WorkbookApplicationWorkareaEndpoint<T> Handled<T>(Action<T> endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return argument =>
        {
            endpoint(argument);
            return ValueTask.FromResult(true);
        };
    }

    public static WorkbookApplicationWorkareaEndpoint<T> Handled<T>(Func<T, Task> endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return async argument =>
        {
            await endpoint(argument);
            return true;
        };
    }

    public static WorkbookApplicationWorkareaEndpoint<T1, T2> Handled<T1, T2>(Action<T1, T2> endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return (firstArgument, secondArgument) =>
        {
            endpoint(firstArgument, secondArgument);
            return ValueTask.FromResult(true);
        };
    }

    public static WorkbookApplicationWorkareaEndpoint<T1, T2> Handled<T1, T2>(
        Func<T1, T2, Task> endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return async (firstArgument, secondArgument) =>
        {
            await endpoint(firstArgument, secondArgument);
            return true;
        };
    }

    public static WorkbookApplicationWorkareaEndpoint<T> Result<T>(Func<T, bool> endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return argument => ValueTask.FromResult(endpoint(argument));
    }
}

/// <summary>
/// Native workbook operations supplied by a renderer. Presentation owns intent classification
/// and supplies only normalized endpoint arguments.
/// </summary>
public sealed class WorkbookApplicationWorkareaCommandEndpointProfile
{
    public WorkbookApplicationWorkareaEndpoint? Undo { get; init; }
    public WorkbookApplicationWorkareaEndpoint? Redo { get; init; }
    public WorkbookApplicationWorkareaEndpoint? Cut { get; init; }
    public WorkbookApplicationWorkareaEndpoint? Copy { get; init; }
    public WorkbookApplicationWorkareaEndpoint? Paste { get; init; }
    public WorkbookApplicationWorkareaEndpoint? PasteSpecial { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? FormatPainter { get; init; }
    public WorkbookApplicationWorkareaEndpoint<
        WorkbookApplicationCommandInvocation,
        WorkbookApplicationCommandVariant>? ToggleBold { get; init; }
    public WorkbookApplicationWorkareaEndpoint<
        WorkbookApplicationCommandInvocation,
        WorkbookApplicationCommandVariant>? ToggleItalic { get; init; }
    public WorkbookApplicationWorkareaEndpoint<
        WorkbookApplicationCommandInvocation,
        WorkbookApplicationCommandVariant>? ToggleUnderline { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ToggleStrikethrough { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? OpenFillColor { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? OpenFontColor { get; init; }
    public WorkbookApplicationWorkareaEndpoint? OpenFormatCells { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? InsertFunction { get; init; }
    public WorkbookApplicationWorkareaEndpoint? AutoSum { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? CalculateNow { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? CalculateActiveSheet { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? RefreshAll { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? SortAscending { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? SortDescending { get; init; }
    public WorkbookApplicationWorkareaEndpoint? CustomSort { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? ToggleFilter { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ClearFilter { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandVariant>? ReapplyFilter { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? OpenDataValidation { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? OpenNameManager { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? OpenSpelling { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? CheckAccessibility { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? ShareWorkbook { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? Zoom100 { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? ZoomSelection { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? FreezePanes { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? InsertWorksheet { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? Find { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? Replace { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? GoTo { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? OpenSelectionPane { get; init; }
    public WorkbookApplicationWorkareaEndpoint? InsertCopiedCells { get; init; }
    public WorkbookApplicationWorkareaEndpoint? InsertCells { get; init; }
    public WorkbookApplicationWorkareaEndpoint<uint>? InsertRow { get; init; }
    public WorkbookApplicationWorkareaEndpoint<uint>? InsertColumn { get; init; }
    public WorkbookApplicationWorkareaEndpoint? DeleteCells { get; init; }
    public WorkbookApplicationWorkareaEndpoint? DeleteRows { get; init; }
    public WorkbookApplicationWorkareaEndpoint? DeleteColumns { get; init; }
    public WorkbookApplicationWorkareaEndpoint? PickFromDropDown { get; init; }
    public WorkbookApplicationWorkareaEndpoint? QuickAnalysis { get; init; }
    public WorkbookApplicationWorkareaEndpoint? DefineName { get; init; }
    public WorkbookApplicationWorkareaEndpoint? CreateTable { get; init; }
    public WorkbookApplicationWorkareaEndpoint? FormatAsTable { get; init; }
    public WorkbookApplicationWorkareaEndpoint? TextToColumns { get; init; }
    public WorkbookApplicationWorkareaEndpoint? RemoveDuplicates { get; init; }
    public WorkbookApplicationWorkareaEndpoint? HideRows { get; init; }
    public WorkbookApplicationWorkareaEndpoint? UnhideRows { get; init; }
    public WorkbookApplicationWorkareaEndpoint? RowHeight { get; init; }
    public WorkbookApplicationWorkareaEndpoint? AutoFitRowHeight { get; init; }
    public WorkbookApplicationWorkareaEndpoint? HideColumns { get; init; }
    public WorkbookApplicationWorkareaEndpoint? UnhideColumns { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ColumnWidth { get; init; }
    public WorkbookApplicationWorkareaEndpoint? AutoFitColumnWidth { get; init; }
    public WorkbookApplicationWorkareaEndpoint? Group { get; init; }
    public WorkbookApplicationWorkareaEndpoint? Ungroup { get; init; }
    public WorkbookApplicationWorkareaEndpoint? NewThreadedComment { get; init; }
    public WorkbookApplicationWorkareaEndpoint? EditThreadedComment { get; init; }
    public WorkbookApplicationWorkareaEndpoint<CellAddress, bool>? SetThreadedCommentResolution { get; init; }
    public WorkbookApplicationWorkareaEndpoint? DeleteThreadedComment { get; init; }
    public WorkbookApplicationWorkareaEndpoint? NewNote { get; init; }
    public WorkbookApplicationWorkareaEndpoint? EditNote { get; init; }
    public WorkbookApplicationWorkareaEndpoint? DeleteNote { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ShowNotes { get; init; }
    public WorkbookApplicationWorkareaEndpoint<CellAddress>? ShowHideNote { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ShowAllNotes { get; init; }
    public WorkbookApplicationWorkareaEndpoint<CellAddress>? OpenHyperlink { get; init; }
    public WorkbookApplicationWorkareaEndpoint? EditHyperlink { get; init; }
    public WorkbookApplicationWorkareaEndpoint<CellAddress>? PivotTableOptions { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ClearAll { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ClearFormats { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ClearComments { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ClearHyperlinks { get; init; }
    public WorkbookApplicationWorkareaEndpoint? RemoveHyperlinks { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ClearContents { get; init; }
    public WorkbookApplicationWorkareaEndpoint? FillDown { get; init; }
    public WorkbookApplicationWorkareaEndpoint? FillRight { get; init; }
    public WorkbookApplicationWorkareaEndpoint? FlashFill { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ToggleShowFormulas { get; init; }
    public WorkbookApplicationWorkareaEndpoint<int>? ActivateAdjacentSheet { get; init; }
    public WorkbookApplicationWorkareaEndpoint<int>? SelectAdjacentSheetGroup { get; init; }
    public WorkbookApplicationWorkareaEndpoint<NumberFormatShortcut>? ApplyNumberFormat { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ApplyOutlineBorder { get; init; }
    public WorkbookApplicationWorkareaEndpoint? ClearOutlineBorder { get; init; }
    public WorkbookApplicationWorkareaEndpoint<WorkbookApplicationCommandInvocation>? WorkbookStatistics { get; init; }
}

/// <summary>Exhaustive UI-free routing from portable workbook intents to native delegates.</summary>
public static class WorkbookApplicationWorkareaCommandDispatcher
{
    public static ValueTask<bool> DispatchAsync(
        WorkbookApplicationWorkareaCommandRequest request,
        WorkbookApplicationWorkareaCommandEndpointProfile endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return request.Intent switch
        {
            WorkbookApplicationCommandIntent.NewWorkbook
                or WorkbookApplicationCommandIntent.OpenWorkbook
                or WorkbookApplicationCommandIntent.SaveWorkbook
                or WorkbookApplicationCommandIntent.SaveWorkbookAs
                or WorkbookApplicationCommandIntent.PrintWorkbook
                or WorkbookApplicationCommandIntent.ExportPdfXps => FrameCommand(request),
            WorkbookApplicationCommandIntent.Undo => Invoke(request, endpoints.Undo),
            WorkbookApplicationCommandIntent.Redo => Invoke(request, endpoints.Redo),
            WorkbookApplicationCommandIntent.Cut => Invoke(request, endpoints.Cut),
            WorkbookApplicationCommandIntent.Copy => Invoke(request, endpoints.Copy),
            WorkbookApplicationCommandIntent.Paste => Invoke(request, endpoints.Paste),
            WorkbookApplicationCommandIntent.PasteSpecial => Invoke(request, endpoints.PasteSpecial),
            WorkbookApplicationCommandIntent.FormatPainter =>
                Invoke(request, endpoints.FormatPainter, request.Invocation),
            WorkbookApplicationCommandIntent.ToggleBold =>
                Invoke(request, endpoints.ToggleBold, request.Invocation, request.Variant),
            WorkbookApplicationCommandIntent.ToggleItalic =>
                Invoke(request, endpoints.ToggleItalic, request.Invocation, request.Variant),
            WorkbookApplicationCommandIntent.ToggleUnderline =>
                Invoke(request, endpoints.ToggleUnderline, request.Invocation, request.Variant),
            WorkbookApplicationCommandIntent.ToggleStrikethrough => Invoke(request, endpoints.ToggleStrikethrough),
            WorkbookApplicationCommandIntent.OpenFillColor =>
                Invoke(request, endpoints.OpenFillColor, request.Invocation),
            WorkbookApplicationCommandIntent.OpenFontColor =>
                Invoke(request, endpoints.OpenFontColor, request.Invocation),
            WorkbookApplicationCommandIntent.OpenFormatCells => Invoke(request, endpoints.OpenFormatCells),
            WorkbookApplicationCommandIntent.InsertFunction =>
                Invoke(request, endpoints.InsertFunction, request.Invocation),
            WorkbookApplicationCommandIntent.AutoSum => Invoke(request, endpoints.AutoSum),
            WorkbookApplicationCommandIntent.CalculateNow =>
                Invoke(request, endpoints.CalculateNow, request.Invocation),
            WorkbookApplicationCommandIntent.CalculateActiveSheet =>
                Invoke(request, endpoints.CalculateActiveSheet, request.Invocation),
            WorkbookApplicationCommandIntent.RefreshAll =>
                Invoke(request, endpoints.RefreshAll, request.Invocation),
            WorkbookApplicationCommandIntent.SortAscending =>
                Invoke(request, endpoints.SortAscending, request.Invocation),
            WorkbookApplicationCommandIntent.SortDescending =>
                Invoke(request, endpoints.SortDescending, request.Invocation),
            WorkbookApplicationCommandIntent.CustomSort => Invoke(request, endpoints.CustomSort),
            WorkbookApplicationCommandIntent.ToggleFilter =>
                Invoke(request, endpoints.ToggleFilter, request.Invocation),
            WorkbookApplicationCommandIntent.ClearFilter => Invoke(request, endpoints.ClearFilter),
            WorkbookApplicationCommandIntent.ReapplyFilter =>
                Invoke(request, endpoints.ReapplyFilter, request.Variant),
            WorkbookApplicationCommandIntent.OpenDataValidation =>
                Invoke(request, endpoints.OpenDataValidation, request.Invocation),
            WorkbookApplicationCommandIntent.OpenNameManager =>
                Invoke(request, endpoints.OpenNameManager, request.Invocation),
            WorkbookApplicationCommandIntent.OpenSpelling =>
                Invoke(request, endpoints.OpenSpelling, request.Invocation),
            WorkbookApplicationCommandIntent.CheckAccessibility =>
                Invoke(request, endpoints.CheckAccessibility, request.Invocation),
            WorkbookApplicationCommandIntent.ShareWorkbook =>
                Invoke(request, endpoints.ShareWorkbook, request.Invocation),
            WorkbookApplicationCommandIntent.Zoom100 =>
                Invoke(request, endpoints.Zoom100, request.Invocation),
            WorkbookApplicationCommandIntent.ZoomSelection =>
                Invoke(request, endpoints.ZoomSelection, request.Invocation),
            WorkbookApplicationCommandIntent.FreezePanes =>
                Invoke(request, endpoints.FreezePanes, request.Invocation),
            WorkbookApplicationCommandIntent.InsertWorksheet =>
                Invoke(request, endpoints.InsertWorksheet, request.Invocation),
            WorkbookApplicationCommandIntent.Find => Invoke(request, endpoints.Find, request.Invocation),
            WorkbookApplicationCommandIntent.Replace => Invoke(request, endpoints.Replace, request.Invocation),
            WorkbookApplicationCommandIntent.GoTo => Invoke(request, endpoints.GoTo, request.Invocation),
            WorkbookApplicationCommandIntent.OpenSelectionPane =>
                Invoke(request, endpoints.OpenSelectionPane, request.Invocation),
            WorkbookApplicationCommandIntent.InsertCopiedCells => Invoke(request, endpoints.InsertCopiedCells),
            WorkbookApplicationCommandIntent.InsertCells => Invoke(request, endpoints.InsertCells),
            WorkbookApplicationCommandIntent.InsertRowAbove
                or WorkbookApplicationCommandIntent.InsertRowBelow =>
                Invoke(request, endpoints.InsertRow, request.Index),
            WorkbookApplicationCommandIntent.InsertColumnLeft
                or WorkbookApplicationCommandIntent.InsertColumnRight =>
                Invoke(request, endpoints.InsertColumn, request.Index),
            WorkbookApplicationCommandIntent.DeleteCells => Invoke(request, endpoints.DeleteCells),
            WorkbookApplicationCommandIntent.DeleteRows => Invoke(request, endpoints.DeleteRows),
            WorkbookApplicationCommandIntent.DeleteColumns => Invoke(request, endpoints.DeleteColumns),
            WorkbookApplicationCommandIntent.PickFromDropDown => Invoke(request, endpoints.PickFromDropDown),
            WorkbookApplicationCommandIntent.QuickAnalysis => Invoke(request, endpoints.QuickAnalysis),
            WorkbookApplicationCommandIntent.DefineName => Invoke(request, endpoints.DefineName),
            WorkbookApplicationCommandIntent.CreateTable => Invoke(request, endpoints.CreateTable),
            WorkbookApplicationCommandIntent.FormatAsTable => Invoke(request, endpoints.FormatAsTable),
            WorkbookApplicationCommandIntent.TextToColumns => Invoke(request, endpoints.TextToColumns),
            WorkbookApplicationCommandIntent.RemoveDuplicates => Invoke(request, endpoints.RemoveDuplicates),
            WorkbookApplicationCommandIntent.HideRows => Invoke(request, endpoints.HideRows),
            WorkbookApplicationCommandIntent.UnhideRows => Invoke(request, endpoints.UnhideRows),
            WorkbookApplicationCommandIntent.RowHeight => Invoke(request, endpoints.RowHeight),
            WorkbookApplicationCommandIntent.AutoFitRowHeight => Invoke(request, endpoints.AutoFitRowHeight),
            WorkbookApplicationCommandIntent.HideColumns => Invoke(request, endpoints.HideColumns),
            WorkbookApplicationCommandIntent.UnhideColumns => Invoke(request, endpoints.UnhideColumns),
            WorkbookApplicationCommandIntent.ColumnWidth => Invoke(request, endpoints.ColumnWidth),
            WorkbookApplicationCommandIntent.AutoFitColumnWidth => Invoke(request, endpoints.AutoFitColumnWidth),
            WorkbookApplicationCommandIntent.Group => Invoke(request, endpoints.Group),
            WorkbookApplicationCommandIntent.Ungroup => Invoke(request, endpoints.Ungroup),
            WorkbookApplicationCommandIntent.NewThreadedComment => Invoke(request, endpoints.NewThreadedComment),
            WorkbookApplicationCommandIntent.EditThreadedComment => Invoke(request, endpoints.EditThreadedComment),
            WorkbookApplicationCommandIntent.ResolveThreadedComment
                or WorkbookApplicationCommandIntent.UnresolveThreadedComment =>
                Invoke(
                    request,
                    endpoints.SetThreadedCommentResolution,
                    RequiredTarget(request),
                    request.State),
            WorkbookApplicationCommandIntent.DeleteThreadedComment =>
                Invoke(request, endpoints.DeleteThreadedComment),
            WorkbookApplicationCommandIntent.NewNote => Invoke(request, endpoints.NewNote),
            WorkbookApplicationCommandIntent.EditNote => Invoke(request, endpoints.EditNote),
            WorkbookApplicationCommandIntent.DeleteNote => Invoke(request, endpoints.DeleteNote),
            WorkbookApplicationCommandIntent.ShowNotes => Invoke(request, endpoints.ShowNotes),
            WorkbookApplicationCommandIntent.ShowHideNote =>
                Invoke(request, endpoints.ShowHideNote, RequiredTarget(request)),
            WorkbookApplicationCommandIntent.ShowAllNotes => Invoke(request, endpoints.ShowAllNotes),
            WorkbookApplicationCommandIntent.OpenHyperlink =>
                Invoke(request, endpoints.OpenHyperlink, RequiredTarget(request)),
            WorkbookApplicationCommandIntent.EditHyperlink => Invoke(request, endpoints.EditHyperlink),
            WorkbookApplicationCommandIntent.PivotTableOptions =>
                Invoke(request, endpoints.PivotTableOptions, RequiredTarget(request)),
            WorkbookApplicationCommandIntent.ClearAll => Invoke(request, endpoints.ClearAll),
            WorkbookApplicationCommandIntent.ClearFormats => Invoke(request, endpoints.ClearFormats),
            WorkbookApplicationCommandIntent.ClearComments => Invoke(request, endpoints.ClearComments),
            WorkbookApplicationCommandIntent.ClearHyperlinks => Invoke(request, endpoints.ClearHyperlinks),
            WorkbookApplicationCommandIntent.RemoveHyperlinks => Invoke(request, endpoints.RemoveHyperlinks),
            WorkbookApplicationCommandIntent.ClearContents => Invoke(request, endpoints.ClearContents),
            WorkbookApplicationCommandIntent.FillDown => Invoke(request, endpoints.FillDown),
            WorkbookApplicationCommandIntent.FillRight => Invoke(request, endpoints.FillRight),
            WorkbookApplicationCommandIntent.FlashFill => Invoke(request, endpoints.FlashFill),
            WorkbookApplicationCommandIntent.ToggleShowFormulas => Invoke(request, endpoints.ToggleShowFormulas),
            WorkbookApplicationCommandIntent.ActivatePreviousSheet
                or WorkbookApplicationCommandIntent.ActivateNextSheet =>
                Invoke(request, endpoints.ActivateAdjacentSheet, request.Direction),
            WorkbookApplicationCommandIntent.SelectPreviousSheetGroup
                or WorkbookApplicationCommandIntent.SelectNextSheetGroup =>
                Invoke(request, endpoints.SelectAdjacentSheetGroup, request.Direction),
            WorkbookApplicationCommandIntent.NumberFormatGeneral
                or WorkbookApplicationCommandIntent.NumberFormatNumber
                or WorkbookApplicationCommandIntent.NumberFormatTime
                or WorkbookApplicationCommandIntent.NumberFormatDate
                or WorkbookApplicationCommandIntent.NumberFormatCurrency
                or WorkbookApplicationCommandIntent.NumberFormatPercentage
                or WorkbookApplicationCommandIntent.NumberFormatScientific =>
                Invoke(request, endpoints.ApplyNumberFormat, RequiredNumberFormat(request)),
            WorkbookApplicationCommandIntent.ApplyOutlineBorder => Invoke(request, endpoints.ApplyOutlineBorder),
            WorkbookApplicationCommandIntent.ClearOutlineBorder => Invoke(request, endpoints.ClearOutlineBorder),
            WorkbookApplicationCommandIntent.WorkbookStatistics =>
                Invoke(request, endpoints.WorkbookStatistics, request.Invocation),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Intent, null),
        };
    }

    private static ValueTask<bool> Invoke(
        WorkbookApplicationWorkareaCommandRequest request,
        WorkbookApplicationWorkareaEndpoint? endpoint) =>
        endpoint is null ? MissingEndpoint(request) : endpoint();

    private static ValueTask<bool> Invoke<T>(
        WorkbookApplicationWorkareaCommandRequest request,
        WorkbookApplicationWorkareaEndpoint<T>? endpoint,
        T argument) =>
        endpoint is null ? MissingEndpoint(request) : endpoint(argument);

    private static ValueTask<bool> Invoke<T1, T2>(
        WorkbookApplicationWorkareaCommandRequest request,
        WorkbookApplicationWorkareaEndpoint<T1, T2>? endpoint,
        T1 firstArgument,
        T2 secondArgument) =>
        endpoint is null ? MissingEndpoint(request) : endpoint(firstArgument, secondArgument);

    private static ValueTask<bool> MissingEndpoint(WorkbookApplicationWorkareaCommandRequest request) =>
        throw new InvalidOperationException(
            $"Workbook workarea command '{request.Intent}' has no native endpoint.");

    private static ValueTask<bool> FrameCommand(WorkbookApplicationWorkareaCommandRequest request) =>
        throw new InvalidOperationException(
            $"Workbook application-frame command '{request.Intent}' cannot use the workarea dispatcher.");

    private static CellAddress RequiredTarget(WorkbookApplicationWorkareaCommandRequest request) =>
        request.TargetAddress ?? throw MissingPolicy(request);

    private static NumberFormatShortcut RequiredNumberFormat(
        WorkbookApplicationWorkareaCommandRequest request) =>
        request.NumberFormat ?? throw MissingPolicy(request);

    private static InvalidOperationException MissingPolicy(
        WorkbookApplicationWorkareaCommandRequest request) =>
        new($"Workbook workarea command '{request.Intent}' is missing portable policy data.");
}
