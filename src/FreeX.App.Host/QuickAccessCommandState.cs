namespace FreeX.App.Host;

internal readonly record struct QuickAccessCommandState(
    bool CanUndo,
    bool CanRedo,
    bool HasActiveWorksheet,
    bool HasSelection)
{
    public QuickAccessCommandState WithSelectionContext(bool hasActiveWorksheet, bool hasSelection) =>
        new(CanUndo, CanRedo, hasActiveWorksheet, hasSelection);
}

internal enum QuickAccessCommandAvailability
{
    Never,
    Always,
    Undo,
    Redo,
    Worksheet,
    Selection
}

internal static class QuickAccessCommandStateResolver
{
    public static bool CanExecute(string commandId, QuickAccessCommandState state) =>
        CanExecute(GetAvailability(commandId), state);

    public static bool CanExecute(QuickAccessCommandAvailability availability, QuickAccessCommandState state) =>
        availability switch
        {
            QuickAccessCommandAvailability.Always => true,
            QuickAccessCommandAvailability.Undo => state.CanUndo,
            QuickAccessCommandAvailability.Redo => state.CanRedo,
            QuickAccessCommandAvailability.Worksheet => state.HasActiveWorksheet,
            QuickAccessCommandAvailability.Selection => state.HasActiveWorksheet && state.HasSelection,
            _ => false
        };

    public static QuickAccessCommandAvailability GetAvailability(string commandId) =>
        commandId switch
        {
            QuickAccessToolbarCommandIds.Undo => QuickAccessCommandAvailability.Undo,
            QuickAccessToolbarCommandIds.Redo => QuickAccessCommandAvailability.Redo,

            QuickAccessToolbarCommandIds.New or
            QuickAccessToolbarCommandIds.Open or
            QuickAccessToolbarCommandIds.Save or
            QuickAccessToolbarCommandIds.SaveAs or
            QuickAccessToolbarCommandIds.CalculateNow or
            QuickAccessToolbarCommandIds.RefreshAll or
            QuickAccessToolbarCommandIds.NameManager or
            QuickAccessToolbarCommandIds.InsertSheet => QuickAccessCommandAvailability.Always,

            QuickAccessToolbarCommandIds.Print or
            QuickAccessToolbarCommandIds.ExportPdfXps or
            QuickAccessToolbarCommandIds.CalculateSheet or
            QuickAccessToolbarCommandIds.Spelling or
            QuickAccessToolbarCommandIds.Zoom100 or
            QuickAccessToolbarCommandIds.FindSelect => QuickAccessCommandAvailability.Worksheet,

            QuickAccessToolbarCommandIds.Cut or
            QuickAccessToolbarCommandIds.Copy or
            QuickAccessToolbarCommandIds.Paste or
            QuickAccessToolbarCommandIds.FormatPainter or
            QuickAccessToolbarCommandIds.Bold or
            QuickAccessToolbarCommandIds.Italic or
            QuickAccessToolbarCommandIds.Underline or
            QuickAccessToolbarCommandIds.FillColor or
            QuickAccessToolbarCommandIds.FontColor or
            QuickAccessToolbarCommandIds.FormatCells or
            QuickAccessToolbarCommandIds.InsertFunction or
            QuickAccessToolbarCommandIds.AutoSum or
            QuickAccessToolbarCommandIds.SortAscending or
            QuickAccessToolbarCommandIds.SortDescending or
            QuickAccessToolbarCommandIds.Filter or
            QuickAccessToolbarCommandIds.DataValidation or
            QuickAccessToolbarCommandIds.ZoomSelection or
            QuickAccessToolbarCommandIds.FreezePanes => QuickAccessCommandAvailability.Selection,

            _ => QuickAccessCommandAvailability.Never
        };
}
