using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Sets the worksheet view mode with undo support.</summary>
public sealed class SetWorksheetViewModeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetViewMode _viewMode;
    private WorksheetViewMode? _previousViewMode;

    public string Label => _viewMode switch
    {
        WorksheetViewMode.Normal => "Normal View",
        WorksheetViewMode.PageBreakPreview => "Page Break Preview",
        WorksheetViewMode.PageLayout => "Page Layout View",
        _ => "Set Worksheet View"
    };

    public SetWorksheetViewModeCommand(SheetId sheetId, WorksheetViewMode viewMode)
    {
        _sheetId = sheetId;
        _viewMode = viewMode;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_viewMode))
            return new CommandOutcome(false, "Worksheet view mode is not supported.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousViewMode = sheet.ViewMode;
        sheet.ViewMode = _viewMode;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousViewMode is null) return;
        ctx.GetSheet(_sheetId).ViewMode = _previousViewMode.Value;
    }
}

/// <summary>Sets worksheet display options with undo support.</summary>
public sealed class SetWorksheetViewOptionsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _showGridlines;
    private readonly bool _showHeadings;
    private readonly bool _showRulers;
    private bool _previousShowGridlines;
    private bool _previousShowHeadings;
    private bool _previousShowRulers;

    public string Label => "Worksheet View Options";

    public SetWorksheetViewOptionsCommand(SheetId sheetId, bool showGridlines, bool showHeadings, bool showRulers = true)
    {
        _sheetId = sheetId;
        _showGridlines = showGridlines;
        _showHeadings = showHeadings;
        _showRulers = showRulers;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousShowGridlines = sheet.ShowGridlines;
        _previousShowHeadings = sheet.ShowHeadings;
        _previousShowRulers = sheet.ShowRulers;
        sheet.ShowGridlines = _showGridlines;
        sheet.ShowHeadings = _showHeadings;
        sheet.ShowRulers = _showRulers;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.ShowGridlines = _previousShowGridlines;
        sheet.ShowHeadings = _previousShowHeadings;
        sheet.ShowRulers = _previousShowRulers;
    }
}

/// <summary>Sets whether worksheet outline symbols are shown.</summary>
public sealed class SetWorksheetOutlineSymbolsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _showOutlineSymbols;
    private bool? _previousShowOutlineSymbols;

    public string Label => "Show Outline Symbols";

    public SetWorksheetOutlineSymbolsCommand(SheetId sheetId, bool showOutlineSymbols)
    {
        _sheetId = sheetId;
        _showOutlineSymbols = showOutlineSymbols;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousShowOutlineSymbols = sheet.ShowOutlineSymbols;
        sheet.ShowOutlineSymbols = _showOutlineSymbols;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).ShowOutlineSymbols = _previousShowOutlineSymbols;
    }
}

/// <summary>
/// Sets the worksheet outline "Settings": whether summary rows appear below detail rows, whether
/// summary columns appear to the right of detail columns, and whether automatic outline styles are
/// applied. Mirrors Excel's Data ▸ Outline ▸ Settings dialog. Undo restores every previous value.
/// </summary>
public sealed class SetWorksheetOutlineSettingsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _summaryBelow;
    private readonly bool _summaryRight;
    private readonly bool _applyStyles;
    private bool? _previousSummaryBelow;
    private bool? _previousSummaryRight;
    private bool? _previousApplyStyles;

    public string Label => "Outline Settings";

    public SetWorksheetOutlineSettingsCommand(
        SheetId sheetId,
        bool summaryBelow,
        bool summaryRight,
        bool applyStyles)
    {
        _sheetId = sheetId;
        _summaryBelow = summaryBelow;
        _summaryRight = summaryRight;
        _applyStyles = applyStyles;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousSummaryBelow = sheet.OutlineSummaryBelow;
        _previousSummaryRight = sheet.OutlineSummaryRight;
        _previousApplyStyles = sheet.ApplyOutlineStyles;
        sheet.OutlineSummaryBelow = _summaryBelow;
        sheet.OutlineSummaryRight = _summaryRight;
        sheet.ApplyOutlineStyles = _applyStyles;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.OutlineSummaryBelow = _previousSummaryBelow;
        sheet.OutlineSummaryRight = _previousSummaryRight;
        sheet.ApplyOutlineStyles = _previousApplyStyles;
    }
}

/// <summary>Sets worksheet zoom with undo support.</summary>
public sealed class SetWorksheetZoomCommand : IWorkbookCommand
{
    public const int MinZoomPercent = 10;
    public const int MaxZoomPercent = 400;

    private readonly SheetId _sheetId;
    private readonly int _zoomPercent;
    private int _previousZoomPercent;

    public string Label => "Zoom";

    public SetWorksheetZoomCommand(SheetId sheetId, int zoomPercent)
    {
        _sheetId = sheetId;
        _zoomPercent = zoomPercent;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_zoomPercent is < MinZoomPercent or > MaxZoomPercent)
            return new CommandOutcome(false, "Zoom must be between 10% and 400%.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousZoomPercent = sheet.ZoomPercent;
        sheet.ZoomPercent = _zoomPercent;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).ZoomPercent = _previousZoomPercent;
    }
}

/// <summary>Sets whether formulas are displayed in cells instead of calculated values.</summary>
public sealed class SetWorksheetShowFormulasCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _showFormulas;
    private bool _previousShowFormulas;

    public string Label => "Show Formulas";

    public SetWorksheetShowFormulasCommand(SheetId sheetId, bool showFormulas)
    {
        _sheetId = sheetId;
        _showFormulas = showFormulas;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousShowFormulas = sheet.ShowFormulas;
        sheet.ShowFormulas = _showFormulas;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).ShowFormulas = _previousShowFormulas;
    }
}
