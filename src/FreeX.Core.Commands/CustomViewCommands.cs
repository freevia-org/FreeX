using FreeX.Core.Model;

namespace FreeX.Core.Commands;

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
        if (CustomViewStatePlanner.RejectIfWorkbookHasTable(workbook) is { } tableOutcome)
            return tableOutcome;

        var index = FindViewIndex(workbook, _name);
        _hadPreviousView = index >= 0;
        _previousView = _hadPreviousView ? workbook.CustomViews[index] : null;

        var view = new WorkbookCustomView(
            _name,
            CaptureWorkbookState(workbook, _includePrintSettings, _includeHiddenRowsColumnsAndFilterSettings),
            IncludePrintSettings: _includePrintSettings,
            IncludeHiddenRowsColumnsAndFilterSettings: _includeHiddenRowsColumnsAndFilterSettings,
            ActiveSheetIndex: CustomViewStatePlanner.CaptureActiveSheetIndex(workbook));

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

    // N14: IncludePrintSettings / IncludeHiddenRowsColumnsAndFilterSettings must actually gate
    // what gets captured — matching Excel, where unchecking either checkbox in the "Add View"
    // dialog means that state is NOT snapshotted (and so is left untouched on Show View later).
    // CustomViewStatePlanner.CaptureWorkbookState/CaptureSheetState only ever produce the base
    // pane/zoom/gridline fields; augment each sheet's state here with the hidden-rows/cols/filter
    // and print-setting fields the WorksheetCustomViewState record already has room for (see N13).
    internal static List<WorksheetCustomViewState> CaptureWorkbookState(
        Workbook workbook, bool includePrintSettings, bool includeHiddenRowsColumnsAndFilterSettings) =>
        workbook.Sheets
            .Select(sheet => AugmentCapturedState(
                CustomViewStatePlanner.CaptureSheetState(sheet),
                sheet,
                includePrintSettings,
                includeHiddenRowsColumnsAndFilterSettings))
            .ToList();

    private static WorksheetCustomViewState AugmentCapturedState(
        WorksheetCustomViewState state, Sheet sheet,
        bool includePrintSettings, bool includeHiddenRowsColumnsAndFilterSettings)
    {
        if (includeHiddenRowsColumnsAndFilterSettings)
        {
            state = state with
            {
                HiddenRows = sheet.HiddenRows.Count > 0 ? sheet.HiddenRows.ToList() : [],
                HiddenCols = sheet.HiddenCols.Count > 0 ? sheet.HiddenCols.ToList() : [],
                FilterHiddenRows = sheet.FilterHiddenRows.Count > 0 ? sheet.FilterHiddenRows.ToList() : [],
                // R111-custom-view-autofilter-alias: sheet.AutoFilter is a live, mutable object
                // that ordinary filter commands mutate in place (WorksheetAutoFilterColumnSync).
                // Deep-clone it here (like the HiddenRows/HiddenCols/FilterHiddenRows lists above
                // already are) so a later filter edit on the live sheet can never retroactively
                // rewrite this saved/undo snapshot.
                AutoFilter = WorksheetAutoFilterCloner.Clone(sheet.AutoFilter),
            };
        }

        if (includePrintSettings)
        {
            state = state with
            {
                PrintAreas = sheet.PrintAreas.ToList(),
                PageOrientation = sheet.PageOrientation,
                PaperSize = sheet.PaperSize,
                PaperSizeCode = sheet.PaperSizeCode,
                PageMargins = sheet.PageMargins,
                HeaderMargin = sheet.HeaderMargin,
                FooterMargin = sheet.FooterMargin,
                PrintGridlines = sheet.PrintGridlines,
                PrintHeadings = sheet.PrintHeadings,
                ScaleToFit = sheet.ScaleToFit,
                FitToPage = sheet.FitToPage,
            };
        }

        return state;
    }

    /// <summary>
    /// N14: applies a saved view's hidden-row/col/filter and print-setting fields back onto
    /// <paramref name="sheet"/> when present (i.e. when the owning view's flag was set at save
    /// time — see <see cref="AugmentCapturedState"/>). Null/omitted fields mean "not captured",
    /// so current sheet state for that facet is left untouched, matching Excel's behavior when
    /// a custom view was saved with the corresponding checkbox unchecked.
    /// </summary>
    internal static void ApplyExtendedState(Sheet sheet, WorksheetCustomViewState state)
    {
        if (state.HiddenRows is { } hiddenRows)
        {
            sheet.HiddenRows.Clear();
            foreach (var row in hiddenRows)
                sheet.HiddenRows.Add(row);
        }
        if (state.HiddenCols is { } hiddenCols)
        {
            sheet.HiddenCols.Clear();
            foreach (var col in hiddenCols)
                sheet.HiddenCols.Add(col);
        }
        if (state.FilterHiddenRows is { } filterHiddenRows)
        {
            sheet.FilterHiddenRows.Clear();
            foreach (var row in filterHiddenRows)
                sheet.FilterHiddenRows.Add(row);
        }
        var hasCapturedHiddenFilterState =
            state.HiddenRows is not null ||
            state.HiddenCols is not null ||
            state.FilterHiddenRows is not null;
        if (hasCapturedHiddenFilterState || state.AutoFilter is not null)
            // R111-custom-view-autofilter-alias: clone rather than aliasing state.AutoFilter onto
            // the live sheet -- state may be a persisted WorkbookCustomView's own stored snapshot
            // (view.Sheets[i]), and assigning it by reference would let a subsequent ordinary
            // filter edit on the sheet mutate the saved view in place.
            sheet.AutoFilter = WorksheetAutoFilterCloner.Clone(state.AutoFilter);

        if (state.PrintAreas is { } printAreas)
            sheet.SetPrintAreas(printAreas);
        if (state.PageOrientation is { } pageOrientation)
            sheet.PageOrientation = pageOrientation;
        if (state.PaperSize is { } paperSize)
            sheet.PaperSize = paperSize;
        if (state.PaperSizeCode is { } paperSizeCode)
            sheet.PaperSizeCode = paperSizeCode;
        if (state.PageMargins is { } pageMargins)
            sheet.PageMargins = pageMargins;
        if (state.HeaderMargin is { } headerMargin)
            sheet.HeaderMargin = headerMargin;
        if (state.FooterMargin is { } footerMargin)
            sheet.FooterMargin = footerMargin;
        if (state.PrintGridlines is { } printGridlines)
            sheet.PrintGridlines = printGridlines;
        if (state.PrintHeadings is { } printHeadings)
            sheet.PrintHeadings = printHeadings;
        if (state.ScaleToFit is { } scaleToFit)
            sheet.ScaleToFit = scaleToFit;
        if (state.FitToPage is { } fitToPage)
            sheet.FitToPage = fitToPage;
    }

    /// <summary>
    /// N14: captures the current extended (hidden-rows/cols/filter + print-setting) state of
    /// <paramref name="sheet"/> as a <see cref="WorksheetCustomViewState"/>-shaped snapshot for
    /// undo, regardless of which fields a saved view happens to include — Revert must restore
    /// exactly what was on the sheet before Apply, not merely the fields the view captured.
    /// </summary>
    internal static WorksheetCustomViewState CaptureExtendedState(Sheet sheet, WorksheetCustomViewState baseState) =>
        AugmentCapturedState(baseState, sheet, includePrintSettings: true, includeHiddenRowsColumnsAndFilterSettings: true);
}

public sealed class ApplyCustomViewCommand : IWorkbookCommand
{
    private readonly string _name;
    private List<WorksheetCustomViewState>? _previousStates;
    private int? _previousActiveSheetIndex;

    public string Label => "Apply Custom View";

    public ApplyCustomViewCommand(string name)
    {
        _name = name.Trim();
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CustomViewStatePlanner.RejectIfWorkbookHasTable(ctx.Workbook) is { } tableOutcome)
            return tableOutcome;

        var index = SaveCustomViewCommand.FindViewIndex(ctx.Workbook, _name);
        if (index < 0)
            return new CommandOutcome(false, $"Custom view '{_name}' was not found.");

        var view = ctx.Workbook.CustomViews[index];

        // N14: undo must restore exactly what was on each sheet before Apply, independent of
        // which fields this particular view captured — always snapshot the full extended state,
        // not just the subset view.IncludePrintSettings/IncludeHiddenRowsColumnsAndFilterSettings
        // gate for the forward apply below.
        _previousStates = ctx.Workbook.Sheets
            .Select(sheet => SaveCustomViewCommand.CaptureExtendedState(sheet, CustomViewStatePlanner.CaptureSheetState(sheet)))
            .ToList();
        _previousActiveSheetIndex = CustomViewStatePlanner.CaptureActiveSheetIndex(ctx.Workbook);

        foreach (var state in view.Sheets)
        {
            var sheet = ctx.Workbook.GetSheet(state.SheetName);
            if (sheet is null) continue;
            ApplyState(sheet, state, view);
        }
        if (CustomViewStatePlanner.SanitizeActiveSheetIndex(ctx.Workbook, view.ActiveSheetIndex) is { } activeSheetIndex)
            ctx.Workbook.ActiveSheetIndex = activeSheetIndex;

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousStates is null) return;
        foreach (var state in _previousStates)
        {
            var sheet = ctx.Workbook.GetSheet(state.SheetName);
            if (sheet is null) continue;
            // Undo always restores the full previously-captured state (both base and extended
            // fields were captured unconditionally in Apply above), so no view flags gate this.
            CustomViewStatePlanner.ApplyState(sheet, state);
            SaveCustomViewCommand.ApplyExtendedState(sheet, state);
        }
        ctx.Workbook.ActiveSheetIndex = CustomViewStatePlanner.SanitizeActiveSheetIndex(ctx.Workbook, _previousActiveSheetIndex);
    }

    private static void ApplyState(Sheet sheet, WorksheetCustomViewState state, WorkbookCustomView view)
    {
        CustomViewStatePlanner.ApplyState(sheet, state);
        // N14: only restore hidden-rows/cols/filter and print settings when the saved view
        // actually captured them (i.e. its flag was set at save time) — matching Excel, where
        // a view saved with either checkbox unchecked leaves that facet of the current sheet
        // state untouched when the view is later shown.
        if (view.IncludeHiddenRowsColumnsAndFilterSettings || view.IncludePrintSettings)
            SaveCustomViewCommand.ApplyExtendedState(sheet, state);
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
        if (CustomViewStatePlanner.RejectIfWorkbookHasTable(ctx.Workbook) is { } tableOutcome)
            return tableOutcome;

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
