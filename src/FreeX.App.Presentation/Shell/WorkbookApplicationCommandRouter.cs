using FreeX.Core.Model;

namespace FreeX.App.Presentation.Shell;

public enum WorkbookApplicationCommandSource
{
    QuickAccessToolbar,
    WorksheetContextMenu,
    KeyboardShortcut
}

public enum WorkbookApplicationCommandAvailability
{
    Never,
    Always,
    Undo,
    Redo,
    Worksheet,
    Selection
}

public enum WorkbookApplicationCommandIntent
{
    NewWorkbook,
    OpenWorkbook,
    SaveWorkbook,
    SaveWorkbookAs,
    PrintWorkbook,
    ExportPdfXps,
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    PasteSpecial,
    FormatPainter,
    ToggleBold,
    ToggleItalic,
    ToggleUnderline,
    ToggleStrikethrough,
    OpenFillColor,
    OpenFontColor,
    OpenFormatCells,
    InsertFunction,
    AutoSum,
    CalculateNow,
    CalculateActiveSheet,
    RefreshAll,
    SortAscending,
    SortDescending,
    CustomSort,
    ToggleFilter,
    ClearFilter,
    ReapplyFilter,
    OpenDataValidation,
    OpenNameManager,
    OpenSpelling,
    CheckAccessibility,
    ShareWorkbook,
    Zoom100,
    ZoomSelection,
    FreezePanes,
    InsertWorksheet,
    Find,
    Replace,
    GoTo,
    OpenSelectionPane,
    InsertCopiedCells,
    InsertCells,
    InsertRowAbove,
    InsertRowBelow,
    InsertColumnLeft,
    InsertColumnRight,
    DeleteCells,
    DeleteRows,
    DeleteColumns,
    PickFromDropDown,
    QuickAnalysis,
    DefineName,
    CreateTable,
    FormatAsTable,
    TextToColumns,
    RemoveDuplicates,
    HideRows,
    UnhideRows,
    RowHeight,
    AutoFitRowHeight,
    HideColumns,
    UnhideColumns,
    ColumnWidth,
    AutoFitColumnWidth,
    Group,
    Ungroup,
    NewThreadedComment,
    EditThreadedComment,
    ResolveThreadedComment,
    UnresolveThreadedComment,
    DeleteThreadedComment,
    NewNote,
    EditNote,
    DeleteNote,
    ShowNotes,
    ShowHideNote,
    ShowAllNotes,
    OpenHyperlink,
    EditHyperlink,
    PivotTableOptions,
    ClearAll,
    ClearFormats,
    ClearComments,
    ClearHyperlinks,
    RemoveHyperlinks,
    ClearContents,
    FillDown,
    FillRight,
    FlashFill,
    ToggleShowFormulas,
    ActivatePreviousSheet,
    ActivateNextSheet,
    SelectPreviousSheetGroup,
    SelectNextSheetGroup,
    NumberFormatGeneral,
    NumberFormatNumber,
    NumberFormatTime,
    NumberFormatDate,
    NumberFormatCurrency,
    NumberFormatPercentage,
    NumberFormatScientific,
    ApplyOutlineBorder,
    ClearOutlineBorder,
    WorkbookStatistics
}

public readonly record struct WorkbookApplicationCommandContext(
    bool CanUndo,
    bool CanRedo,
    bool HasActiveWorksheet,
    bool HasSelection);

public sealed record WorkbookApplicationCommandRoute(
    WorkbookApplicationCommandSource Source,
    string SourceKey,
    WorkbookApplicationCommandIntent Intent,
    WorkbookApplicationCommandAvailability Availability);

public readonly record struct WorkbookApplicationCommandInvocation(
    WorkbookApplicationCommandRoute Route,
    CellAddress? TargetAddress = null,
    object? NativeSource = null,
    object? NativeEventArgs = null);

public readonly record struct WorkbookApplicationCommandExecutionResult(bool IsBound, bool Handled)
{
    public static WorkbookApplicationCommandExecutionResult NotBound { get; } = new(false, false);
}

public sealed class WorkbookApplicationCommandBindings
{
    private readonly Dictionary<
        WorkbookApplicationCommandIntent,
        Func<WorkbookApplicationCommandInvocation, ValueTask<bool>>> _callbacks = [];

    public int Count => _callbacks.Count;

    public void Bind(
        WorkbookApplicationCommandIntent intent,
        Action<WorkbookApplicationCommandInvocation> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Add(intent, invocation =>
        {
            callback(invocation);
            return ValueTask.FromResult(true);
        });
    }

    public void BindHandled(
        WorkbookApplicationCommandIntent intent,
        Func<WorkbookApplicationCommandInvocation, bool> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Add(intent, invocation => ValueTask.FromResult(callback(invocation)));
    }

    public void BindAsync(
        WorkbookApplicationCommandIntent intent,
        Func<WorkbookApplicationCommandInvocation, Task> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Add(intent, async invocation =>
        {
            await callback(invocation).ConfigureAwait(true);
            return true;
        });
    }

    public void BindHandledAsync(
        WorkbookApplicationCommandIntent intent,
        Func<WorkbookApplicationCommandInvocation, Task<bool>> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Add(intent, async invocation => await callback(invocation).ConfigureAwait(true));
    }

    public async ValueTask<WorkbookApplicationCommandExecutionResult> TryExecuteAsync(
        WorkbookApplicationCommandRoute route,
        CellAddress? targetAddress = null,
        object? nativeSource = null,
        object? nativeEventArgs = null)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (!_callbacks.TryGetValue(route.Intent, out var callback))
            return WorkbookApplicationCommandExecutionResult.NotBound;

        var handled = await callback(new WorkbookApplicationCommandInvocation(
            route,
            targetAddress,
            nativeSource,
            nativeEventArgs)).ConfigureAwait(true);
        return new WorkbookApplicationCommandExecutionResult(true, handled);
    }

    public void EnsureBound(IEnumerable<WorkbookApplicationCommandRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var missing = routes
            .Select(route => route.Intent)
            .Distinct()
            .Where(intent => !_callbacks.ContainsKey(intent))
            .Order()
            .ToArray();
        if (missing.Length == 0)
            return;

        throw new InvalidOperationException(
            $"Workbook application callbacks are missing for: {string.Join(", ", missing)}.");
    }

    private void Add(
        WorkbookApplicationCommandIntent intent,
        Func<WorkbookApplicationCommandInvocation, ValueTask<bool>> callback)
    {
        if (!_callbacks.TryAdd(intent, callback))
            throw new InvalidOperationException($"A workbook application callback is already bound for {intent}.");
    }
}

public sealed record WorkbookApplicationFrameCommandHandlers(
    Func<WorkbookApplicationCommandInvocation, Task> NewWorkbookAsync,
    Func<WorkbookApplicationCommandInvocation, Task> OpenWorkbookAsync,
    Func<WorkbookApplicationCommandInvocation, Task> SaveWorkbookAsync,
    Func<WorkbookApplicationCommandInvocation, Task> SaveWorkbookAsAsync,
    Func<WorkbookApplicationCommandInvocation, Task> PrintWorkbookAsync,
    Func<WorkbookApplicationCommandInvocation, Task> ExportPdfXpsAsync);

/// <summary>
/// Registers the application-frame commands that every FreeX renderer exposes. Renderers provide only
/// their native effects; this binder owns the portable intent-to-handler contract.
/// </summary>
public static class WorkbookApplicationFrameCommandBinder
{
    public static void Bind(
        WorkbookApplicationCommandBindings bindings,
        WorkbookApplicationFrameCommandHandlers handlers)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(handlers);

        bindings.BindAsync(WorkbookApplicationCommandIntent.NewWorkbook, handlers.NewWorkbookAsync);
        bindings.BindAsync(WorkbookApplicationCommandIntent.OpenWorkbook, handlers.OpenWorkbookAsync);
        bindings.BindAsync(WorkbookApplicationCommandIntent.SaveWorkbook, handlers.SaveWorkbookAsync);
        bindings.BindAsync(WorkbookApplicationCommandIntent.SaveWorkbookAs, handlers.SaveWorkbookAsAsync);
        bindings.BindAsync(WorkbookApplicationCommandIntent.PrintWorkbook, handlers.PrintWorkbookAsync);
        bindings.BindAsync(WorkbookApplicationCommandIntent.ExportPdfXps, handlers.ExportPdfXpsAsync);
    }
}

public static class WorkbookApplicationCommandRouter
{
    private static readonly IReadOnlyDictionary<string, WorkbookApplicationCommandRoute> QuickAccessByKey =
        CreateKeyedRoutes(BuildQuickAccessRoutes());

    private static readonly IReadOnlyDictionary<string, WorkbookApplicationCommandRoute> WorksheetContextByKey =
        CreateKeyedRoutes(BuildWorksheetContextRoutes());

    private static readonly IReadOnlyDictionary<WorkbookShortcutRoute, WorkbookApplicationCommandRoute> ShortcutByRoute =
        BuildShortcutRoutes().ToDictionary(entry => entry.Shortcut, entry => entry.Route);

    public static IReadOnlyList<WorkbookApplicationCommandRoute> QuickAccessRoutes { get; } =
        QuickAccessByKey.Values.OrderBy(route => route.SourceKey, StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<WorkbookApplicationCommandRoute> WorksheetContextMenuRoutes { get; } =
        WorksheetContextByKey.Values.OrderBy(route => route.SourceKey, StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<WorkbookApplicationCommandRoute> KeyboardShortcutRoutes { get; } =
        ShortcutByRoute.Values.OrderBy(route => route.SourceKey, StringComparer.Ordinal).ToArray();

    public static bool TryRouteQuickAccess(
        string commandId,
        out WorkbookApplicationCommandRoute route) =>
        TryGet(QuickAccessByKey, commandId, out route);

    public static bool TryRouteWorksheetContextMenu(
        string actionKey,
        out WorkbookApplicationCommandRoute route) =>
        TryGet(WorksheetContextByKey, actionKey, out route);

    public static bool TryRouteShortcut(
        WorkbookShortcutRoute shortcut,
        out WorkbookApplicationCommandRoute route) =>
        ShortcutByRoute.TryGetValue(shortcut, out route!);

    public static bool CanExecute(
        WorkbookApplicationCommandRoute route,
        WorkbookApplicationCommandContext context) =>
        CanExecute(route.Availability, context);

    public static bool CanExecute(
        WorkbookApplicationCommandAvailability availability,
        WorkbookApplicationCommandContext context) =>
        availability switch
        {
            WorkbookApplicationCommandAvailability.Always => true,
            WorkbookApplicationCommandAvailability.Undo => context.CanUndo,
            WorkbookApplicationCommandAvailability.Redo => context.CanRedo,
            WorkbookApplicationCommandAvailability.Worksheet => context.HasActiveWorksheet,
            WorkbookApplicationCommandAvailability.Selection =>
                context.HasActiveWorksheet && context.HasSelection,
            _ => false
        };

    private static bool TryGet(
        IReadOnlyDictionary<string, WorkbookApplicationCommandRoute> routes,
        string sourceKey,
        out WorkbookApplicationCommandRoute route)
    {
        if (!string.IsNullOrWhiteSpace(sourceKey) && routes.TryGetValue(sourceKey, out route!))
            return true;

        route = null!;
        return false;
    }

    private static IReadOnlyDictionary<string, WorkbookApplicationCommandRoute> CreateKeyedRoutes(
        IEnumerable<WorkbookApplicationCommandRoute> routes) =>
        routes.ToDictionary(route => route.SourceKey, StringComparer.OrdinalIgnoreCase);

    private static WorkbookApplicationCommandRoute Route(
        WorkbookApplicationCommandSource source,
        string sourceKey,
        WorkbookApplicationCommandIntent intent,
        WorkbookApplicationCommandAvailability availability) =>
        new(source, sourceKey, intent, availability);

    private static IEnumerable<WorkbookApplicationCommandRoute> BuildQuickAccessRoutes()
    {
        const WorkbookApplicationCommandSource source = WorkbookApplicationCommandSource.QuickAccessToolbar;
        const WorkbookApplicationCommandAvailability always = WorkbookApplicationCommandAvailability.Always;
        const WorkbookApplicationCommandAvailability worksheet = WorkbookApplicationCommandAvailability.Worksheet;
        const WorkbookApplicationCommandAvailability selection = WorkbookApplicationCommandAvailability.Selection;

        yield return Route(source, "Save", WorkbookApplicationCommandIntent.SaveWorkbook, always);
        yield return Route(source, "Undo", WorkbookApplicationCommandIntent.Undo, WorkbookApplicationCommandAvailability.Undo);
        yield return Route(source, "Redo", WorkbookApplicationCommandIntent.Redo, WorkbookApplicationCommandAvailability.Redo);
        yield return Route(source, "New", WorkbookApplicationCommandIntent.NewWorkbook, always);
        yield return Route(source, "Open", WorkbookApplicationCommandIntent.OpenWorkbook, always);
        yield return Route(source, "SaveAs", WorkbookApplicationCommandIntent.SaveWorkbookAs, always);
        yield return Route(source, "Print", WorkbookApplicationCommandIntent.PrintWorkbook, worksheet);
        yield return Route(source, "ExportPdfXps", WorkbookApplicationCommandIntent.ExportPdfXps, worksheet);
        yield return Route(source, "Cut", WorkbookApplicationCommandIntent.Cut, selection);
        yield return Route(source, "Copy", WorkbookApplicationCommandIntent.Copy, selection);
        yield return Route(source, "Paste", WorkbookApplicationCommandIntent.Paste, selection);
        yield return Route(source, "FormatPainter", WorkbookApplicationCommandIntent.FormatPainter, selection);
        yield return Route(source, "Bold", WorkbookApplicationCommandIntent.ToggleBold, selection);
        yield return Route(source, "Italic", WorkbookApplicationCommandIntent.ToggleItalic, selection);
        yield return Route(source, "Underline", WorkbookApplicationCommandIntent.ToggleUnderline, selection);
        yield return Route(source, "FillColor", WorkbookApplicationCommandIntent.OpenFillColor, selection);
        yield return Route(source, "FontColor", WorkbookApplicationCommandIntent.OpenFontColor, selection);
        yield return Route(source, "FormatCells", WorkbookApplicationCommandIntent.OpenFormatCells, selection);
        yield return Route(source, "InsertFunction", WorkbookApplicationCommandIntent.InsertFunction, selection);
        yield return Route(source, "AutoSum", WorkbookApplicationCommandIntent.AutoSum, selection);
        yield return Route(source, "CalculateNow", WorkbookApplicationCommandIntent.CalculateNow, always);
        yield return Route(source, "CalculateSheet", WorkbookApplicationCommandIntent.CalculateActiveSheet, worksheet);
        yield return Route(source, "RefreshAll", WorkbookApplicationCommandIntent.RefreshAll, always);
        yield return Route(source, "SortAscending", WorkbookApplicationCommandIntent.SortAscending, selection);
        yield return Route(source, "SortDescending", WorkbookApplicationCommandIntent.SortDescending, selection);
        yield return Route(source, "Filter", WorkbookApplicationCommandIntent.ToggleFilter, selection);
        yield return Route(source, "DataValidation", WorkbookApplicationCommandIntent.OpenDataValidation, selection);
        yield return Route(source, "NameManager", WorkbookApplicationCommandIntent.OpenNameManager, always);
        yield return Route(source, "Spelling", WorkbookApplicationCommandIntent.OpenSpelling, worksheet);
        yield return Route(source, "CheckAccessibility", WorkbookApplicationCommandIntent.CheckAccessibility, worksheet);
        yield return Route(source, "ShareWorkbook", WorkbookApplicationCommandIntent.ShareWorkbook, worksheet);
        yield return Route(source, "Zoom100", WorkbookApplicationCommandIntent.Zoom100, worksheet);
        yield return Route(source, "ZoomSelection", WorkbookApplicationCommandIntent.ZoomSelection, selection);
        yield return Route(source, "FreezePanes", WorkbookApplicationCommandIntent.FreezePanes, selection);
        yield return Route(source, "InsertSheet", WorkbookApplicationCommandIntent.InsertWorksheet, always);
        yield return Route(source, "FindSelect", WorkbookApplicationCommandIntent.Find, worksheet);
        yield return Route(source, "SelectionPane", WorkbookApplicationCommandIntent.OpenSelectionPane, worksheet);
    }

    private static IEnumerable<WorkbookApplicationCommandRoute> BuildWorksheetContextRoutes()
    {
        const WorkbookApplicationCommandSource source = WorkbookApplicationCommandSource.WorksheetContextMenu;
        const WorkbookApplicationCommandAvailability selection = WorkbookApplicationCommandAvailability.Selection;

        yield return Route(source, "Cut", WorkbookApplicationCommandIntent.Cut, selection);
        yield return Route(source, "Copy", WorkbookApplicationCommandIntent.Copy, selection);
        yield return Route(source, "Paste", WorkbookApplicationCommandIntent.Paste, selection);
        yield return Route(source, "PasteSpecial", WorkbookApplicationCommandIntent.PasteSpecial, selection);
        yield return Route(source, "InsertCopiedCells", WorkbookApplicationCommandIntent.InsertCopiedCells, selection);
        yield return Route(source, "InsertCells", WorkbookApplicationCommandIntent.InsertCells, selection);
        yield return Route(source, "InsertRowAbove", WorkbookApplicationCommandIntent.InsertRowAbove, selection);
        yield return Route(source, "InsertRowBelow", WorkbookApplicationCommandIntent.InsertRowBelow, selection);
        yield return Route(source, "InsertColumnLeft", WorkbookApplicationCommandIntent.InsertColumnLeft, selection);
        yield return Route(source, "InsertColumnRight", WorkbookApplicationCommandIntent.InsertColumnRight, selection);
        yield return Route(source, "DeleteCells", WorkbookApplicationCommandIntent.DeleteCells, selection);
        yield return Route(source, "DeleteRows", WorkbookApplicationCommandIntent.DeleteRows, selection);
        yield return Route(source, "DeleteColumns", WorkbookApplicationCommandIntent.DeleteColumns, selection);
        yield return Route(source, "SortAscending", WorkbookApplicationCommandIntent.SortAscending, selection);
        yield return Route(source, "SortDescending", WorkbookApplicationCommandIntent.SortDescending, selection);
        yield return Route(source, "CustomSort", WorkbookApplicationCommandIntent.CustomSort, selection);
        yield return Route(source, "Filter", WorkbookApplicationCommandIntent.ToggleFilter, selection);
        yield return Route(source, "ClearFilter", WorkbookApplicationCommandIntent.ClearFilter, selection);
        yield return Route(source, "ReapplyFilter", WorkbookApplicationCommandIntent.ReapplyFilter, selection);
        yield return Route(source, "PickFromDropDown", WorkbookApplicationCommandIntent.PickFromDropDown, selection);
        yield return Route(source, "QuickAnalysis", WorkbookApplicationCommandIntent.QuickAnalysis, selection);
        yield return Route(source, "DefineName", WorkbookApplicationCommandIntent.DefineName, selection);
        yield return Route(source, "CreateTable", WorkbookApplicationCommandIntent.CreateTable, selection);
        yield return Route(source, "FormatAsTable", WorkbookApplicationCommandIntent.FormatAsTable, selection);
        yield return Route(source, "TextToColumns", WorkbookApplicationCommandIntent.TextToColumns, selection);
        yield return Route(source, "RemoveDuplicates", WorkbookApplicationCommandIntent.RemoveDuplicates, selection);
        yield return Route(source, "DataValidation", WorkbookApplicationCommandIntent.OpenDataValidation, selection);
        yield return Route(source, "HideRows", WorkbookApplicationCommandIntent.HideRows, selection);
        yield return Route(source, "UnhideRows", WorkbookApplicationCommandIntent.UnhideRows, selection);
        yield return Route(source, "RowHeight", WorkbookApplicationCommandIntent.RowHeight, selection);
        yield return Route(source, "AutoFitRowHeight", WorkbookApplicationCommandIntent.AutoFitRowHeight, selection);
        yield return Route(source, "HideColumns", WorkbookApplicationCommandIntent.HideColumns, selection);
        yield return Route(source, "UnhideColumns", WorkbookApplicationCommandIntent.UnhideColumns, selection);
        yield return Route(source, "ColumnWidth", WorkbookApplicationCommandIntent.ColumnWidth, selection);
        yield return Route(source, "AutoFitColumnWidth", WorkbookApplicationCommandIntent.AutoFitColumnWidth, selection);
        yield return Route(source, "Group", WorkbookApplicationCommandIntent.Group, selection);
        yield return Route(source, "Ungroup", WorkbookApplicationCommandIntent.Ungroup, selection);
        yield return Route(source, "NewComment", WorkbookApplicationCommandIntent.NewThreadedComment, selection);
        yield return Route(source, "EditComment", WorkbookApplicationCommandIntent.EditThreadedComment, selection);
        yield return Route(source, "ResolveComment", WorkbookApplicationCommandIntent.ResolveThreadedComment, selection);
        yield return Route(source, "UnresolveComment", WorkbookApplicationCommandIntent.UnresolveThreadedComment, selection);
        yield return Route(source, "DeleteComment", WorkbookApplicationCommandIntent.DeleteThreadedComment, selection);
        yield return Route(source, "NewNote", WorkbookApplicationCommandIntent.NewNote, selection);
        yield return Route(source, "EditNote", WorkbookApplicationCommandIntent.EditNote, selection);
        yield return Route(source, "DeleteNote", WorkbookApplicationCommandIntent.DeleteNote, selection);
        yield return Route(source, "ShowNotes", WorkbookApplicationCommandIntent.ShowNotes, selection);
        yield return Route(source, "ShowHideNote", WorkbookApplicationCommandIntent.ShowHideNote, selection);
        yield return Route(source, "ShowAllNotes", WorkbookApplicationCommandIntent.ShowAllNotes, selection);
        yield return Route(source, "OpenHyperlink", WorkbookApplicationCommandIntent.OpenHyperlink, selection);
        yield return Route(source, "Hyperlink", WorkbookApplicationCommandIntent.EditHyperlink, selection);
        yield return Route(source, "PivotTableOptions", WorkbookApplicationCommandIntent.PivotTableOptions, selection);
        yield return Route(source, "FormatCells", WorkbookApplicationCommandIntent.OpenFormatCells, selection);
        yield return Route(source, "ClearAll", WorkbookApplicationCommandIntent.ClearAll, selection);
        yield return Route(source, "ClearFormats", WorkbookApplicationCommandIntent.ClearFormats, selection);
        yield return Route(source, "ClearComments", WorkbookApplicationCommandIntent.ClearComments, selection);
        yield return Route(source, "ClearHyperlinks", WorkbookApplicationCommandIntent.ClearHyperlinks, selection);
        yield return Route(source, "RemoveHyperlinks", WorkbookApplicationCommandIntent.RemoveHyperlinks, selection);
        yield return Route(source, "ClearContents", WorkbookApplicationCommandIntent.ClearContents, selection);
    }

    private static IEnumerable<(WorkbookShortcutRoute Shortcut, WorkbookApplicationCommandRoute Route)> BuildShortcutRoutes()
    {
        const WorkbookApplicationCommandSource source = WorkbookApplicationCommandSource.KeyboardShortcut;
        const WorkbookApplicationCommandAvailability always = WorkbookApplicationCommandAvailability.Always;
        const WorkbookApplicationCommandAvailability worksheet = WorkbookApplicationCommandAvailability.Worksheet;
        const WorkbookApplicationCommandAvailability selection = WorkbookApplicationCommandAvailability.Selection;

        yield return Shortcut(WorkbookShortcutRoute.NewWorkbook, WorkbookApplicationCommandIntent.NewWorkbook, always);
        yield return Shortcut(WorkbookShortcutRoute.OpenWorkbook, WorkbookApplicationCommandIntent.OpenWorkbook, always);
        yield return Shortcut(WorkbookShortcutRoute.SaveWorkbook, WorkbookApplicationCommandIntent.SaveWorkbook, always);
        yield return Shortcut(WorkbookShortcutRoute.PrintWorkbook, WorkbookApplicationCommandIntent.PrintWorkbook, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.Copy, WorkbookApplicationCommandIntent.Copy, selection);
        yield return Shortcut(WorkbookShortcutRoute.Cut, WorkbookApplicationCommandIntent.Cut, selection);
        yield return Shortcut(WorkbookShortcutRoute.Paste, WorkbookApplicationCommandIntent.Paste, selection);
        yield return Shortcut(WorkbookShortcutRoute.PasteSpecial, WorkbookApplicationCommandIntent.PasteSpecial, selection);
        yield return Shortcut(WorkbookShortcutRoute.Undo, WorkbookApplicationCommandIntent.Undo, WorkbookApplicationCommandAvailability.Undo);
        yield return Shortcut(WorkbookShortcutRoute.Redo, WorkbookApplicationCommandIntent.Redo, WorkbookApplicationCommandAvailability.Redo);
        yield return Shortcut(WorkbookShortcutRoute.ToggleBold, WorkbookApplicationCommandIntent.ToggleBold, selection);
        yield return Shortcut(WorkbookShortcutRoute.ToggleItalic, WorkbookApplicationCommandIntent.ToggleItalic, selection);
        yield return Shortcut(WorkbookShortcutRoute.ToggleUnderline, WorkbookApplicationCommandIntent.ToggleUnderline, selection);
        yield return Shortcut(WorkbookShortcutRoute.ToggleStrikethrough, WorkbookApplicationCommandIntent.ToggleStrikethrough, selection);
        yield return Shortcut(WorkbookShortcutRoute.FillDown, WorkbookApplicationCommandIntent.FillDown, selection);
        yield return Shortcut(WorkbookShortcutRoute.FillRight, WorkbookApplicationCommandIntent.FillRight, selection);
        yield return Shortcut(WorkbookShortcutRoute.FlashFill, WorkbookApplicationCommandIntent.FlashFill, selection);
        yield return Shortcut(WorkbookShortcutRoute.ToggleShowFormulas, WorkbookApplicationCommandIntent.ToggleShowFormulas, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.ActivatePreviousSheet, WorkbookApplicationCommandIntent.ActivatePreviousSheet, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.ActivateNextSheet, WorkbookApplicationCommandIntent.ActivateNextSheet, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.SelectPreviousSheetGroup, WorkbookApplicationCommandIntent.SelectPreviousSheetGroup, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.SelectNextSheetGroup, WorkbookApplicationCommandIntent.SelectNextSheetGroup, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.OpenFormatCells, WorkbookApplicationCommandIntent.OpenFormatCells, selection);
        yield return Shortcut(WorkbookShortcutRoute.NumberFormatGeneral, WorkbookApplicationCommandIntent.NumberFormatGeneral, selection);
        yield return Shortcut(WorkbookShortcutRoute.NumberFormatNumber, WorkbookApplicationCommandIntent.NumberFormatNumber, selection);
        yield return Shortcut(WorkbookShortcutRoute.NumberFormatTime, WorkbookApplicationCommandIntent.NumberFormatTime, selection);
        yield return Shortcut(WorkbookShortcutRoute.NumberFormatDate, WorkbookApplicationCommandIntent.NumberFormatDate, selection);
        yield return Shortcut(WorkbookShortcutRoute.NumberFormatCurrency, WorkbookApplicationCommandIntent.NumberFormatCurrency, selection);
        yield return Shortcut(WorkbookShortcutRoute.NumberFormatPercentage, WorkbookApplicationCommandIntent.NumberFormatPercentage, selection);
        yield return Shortcut(WorkbookShortcutRoute.NumberFormatScientific, WorkbookApplicationCommandIntent.NumberFormatScientific, selection);
        yield return Shortcut(WorkbookShortcutRoute.ApplyOutlineBorder, WorkbookApplicationCommandIntent.ApplyOutlineBorder, selection);
        yield return Shortcut(WorkbookShortcutRoute.ClearOutlineBorder, WorkbookApplicationCommandIntent.ClearOutlineBorder, selection);
        yield return Shortcut(WorkbookShortcutRoute.Find, WorkbookApplicationCommandIntent.Find, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.Replace, WorkbookApplicationCommandIntent.Replace, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.GoTo, WorkbookApplicationCommandIntent.GoTo, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.InsertFunction, WorkbookApplicationCommandIntent.InsertFunction, selection);
        yield return Shortcut(WorkbookShortcutRoute.AutoSum, WorkbookApplicationCommandIntent.AutoSum, selection);
        yield return Shortcut(WorkbookShortcutRoute.WorkbookStatistics, WorkbookApplicationCommandIntent.WorkbookStatistics, worksheet);
        yield return Shortcut(WorkbookShortcutRoute.InsertWorksheet, WorkbookApplicationCommandIntent.InsertWorksheet, always);

        static (WorkbookShortcutRoute Shortcut, WorkbookApplicationCommandRoute Route) Shortcut(
            WorkbookShortcutRoute shortcut,
            WorkbookApplicationCommandIntent intent,
            WorkbookApplicationCommandAvailability availability) =>
            (shortcut, Route(source, shortcut.ToString(), intent, availability));
    }
}
