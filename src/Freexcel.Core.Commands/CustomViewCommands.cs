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
            CustomViewStatePlanner.CaptureWorkbookState(workbook),
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
        => CustomViewStatePlanner.FindViewIndex(workbook, name);

    internal static WorksheetCustomViewState CaptureSheetState(Sheet sheet) =>
        CustomViewStatePlanner.CaptureSheetState(sheet);

    internal static WorksheetCustomViewState SanitizePaneState(WorksheetCustomViewState state)
        => CustomViewStatePlanner.SanitizePaneState(state);
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
        CustomViewStatePlanner.CaptureWorkbookState(workbook);

    private static void ApplyState(Sheet sheet, WorksheetCustomViewState state)
        => CustomViewStatePlanner.ApplyState(sheet, state);
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
