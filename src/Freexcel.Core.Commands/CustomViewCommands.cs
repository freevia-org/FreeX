using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class SaveCustomViewCommand : IWorkbookCommand
{
    private readonly string _name;
    private readonly bool _includePrintSettings;
    private readonly bool _includeHiddenRowsColumnsAndFilterSettings;
    private WorkbookCustomView? _previousView;
    private bool _hadPreviousView;

    public string Label => "Save Custom View";

    public SaveCustomViewCommand(
        string name,
        bool includePrintSettings = true,
        bool includeHiddenRowsColumnsAndFilterSettings = true)
    {
        _name = name.Trim();
        _includePrintSettings = includePrintSettings;
        _includeHiddenRowsColumnsAndFilterSettings = includeHiddenRowsColumnsAndFilterSettings;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (string.IsNullOrWhiteSpace(_name))
            return new CommandOutcome(false, "Custom view name cannot be blank.");

        var workbook = ctx.Workbook;
        var index = FindViewIndex(workbook, _name);
        _hadPreviousView = index >= 0;
        _previousView = _hadPreviousView ? workbook.CustomViews[index] : null;

        var view = new WorkbookCustomView(
            _name,
            workbook.Sheets.Select(CaptureSheetState).ToList(),
            IncludePrintSettings: _includePrintSettings,
            IncludeHiddenRowsColumnsAndFilterSettings: _includeHiddenRowsColumnsAndFilterSettings);

        if (_hadPreviousView)
            workbook.CustomViews[index] = view;
        else
            workbook.CustomViews.Add(view);

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var workbook = ctx.Workbook;
        var index = FindViewIndex(workbook, _name);
        if (_hadPreviousView && _previousView is not null)
        {
            if (index >= 0)
                workbook.CustomViews[index] = _previousView;
            else
                workbook.CustomViews.Add(_previousView);
            return;
        }

        if (index >= 0)
            workbook.CustomViews.RemoveAt(index);
    }

    internal static int FindViewIndex(Workbook workbook, string name)
    {
        for (var i = 0; i < workbook.CustomViews.Count; i++)
            if (string.Equals(workbook.CustomViews[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    internal static WorksheetCustomViewState CaptureSheetState(Sheet sheet) =>
        SanitizePaneState(new WorksheetCustomViewState(
            sheet.Name,
            sheet.ViewMode,
            sheet.FrozenRows,
            sheet.FrozenCols,
            sheet.SplitRow,
            sheet.SplitColumn,
            sheet.ShowGridlines,
            sheet.ShowHeadings,
            sheet.ShowRulers,
            sheet.ZoomPercent,
            sheet.ShowFormulas));

    internal static WorksheetCustomViewState SanitizePaneState(WorksheetCustomViewState state)
    {
        if (state.FrozenRows == 0 && state.FrozenCols == 0)
            return state;

        return state with
        {
            SplitRow = null,
            SplitColumn = null
        };
    }
}

public sealed class ApplyCustomViewCommand : IWorkbookCommand
{
    private readonly string _name;
    private List<WorksheetCustomViewState>? _previousStates;

    public string Label => "Apply Custom View";

    public ApplyCustomViewCommand(string name)
    {
        _name = name.Trim();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var index = SaveCustomViewCommand.FindViewIndex(ctx.Workbook, _name);
        if (index < 0)
            return new CommandOutcome(false, $"Custom view '{_name}' was not found.");

        _previousStates = Capture(ctx.Workbook);
        foreach (var state in ctx.Workbook.CustomViews[index].Sheets)
        {
            var sheet = ctx.Workbook.GetSheet(state.SheetName);
            if (sheet is null) continue;
            ApplyState(sheet, state);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousStates is null) return;
        foreach (var state in _previousStates)
        {
            var sheet = ctx.Workbook.GetSheet(state.SheetName);
            if (sheet is null) continue;
            ApplyState(sheet, state);
        }
    }

    private static List<WorksheetCustomViewState> Capture(Workbook workbook) =>
        workbook.Sheets.Select(SaveCustomViewCommand.CaptureSheetState).ToList();

    private static void ApplyState(Sheet sheet, WorksheetCustomViewState state)
    {
        state = SaveCustomViewCommand.SanitizePaneState(state);
        sheet.ViewMode = state.ViewMode;
        sheet.FrozenRows = state.FrozenRows;
        sheet.FrozenCols = state.FrozenCols;
        sheet.SplitRow = state.SplitRow;
        sheet.SplitColumn = state.SplitColumn;
        sheet.ShowGridlines = state.ShowGridlines;
        sheet.ShowHeadings = state.ShowHeadings;
        sheet.ShowRulers = state.ShowRulers;
        sheet.ZoomPercent = state.ZoomPercent;
        sheet.ShowFormulas = state.ShowFormulas;
    }
}

public sealed class DeleteCustomViewCommand : IWorkbookCommand
{
    private readonly string _name;
    private WorkbookCustomView? _deletedView;
    private int _deletedIndex = -1;

    public string Label => "Delete Custom View";

    public DeleteCustomViewCommand(string name)
    {
        _name = name.Trim();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _deletedIndex = SaveCustomViewCommand.FindViewIndex(ctx.Workbook, _name);
        if (_deletedIndex < 0)
            return new CommandOutcome(false, $"Custom view '{_name}' was not found.");

        _deletedView = ctx.Workbook.CustomViews[_deletedIndex];
        ctx.Workbook.CustomViews.RemoveAt(_deletedIndex);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_deletedView is null) return;
        var index = Math.Clamp(_deletedIndex, 0, ctx.Workbook.CustomViews.Count);
        ctx.Workbook.CustomViews.Insert(index, _deletedView);
    }
}
