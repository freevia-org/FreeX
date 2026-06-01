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

internal static class QuickAccessCommandStateResolver
{
    public static bool CanExecute(string commandId, QuickAccessCommandState state) =>
        commandId switch
        {
            QuickAccessToolbarCommandIds.Undo => state.CanUndo,
            QuickAccessToolbarCommandIds.Redo => state.CanRedo,

            QuickAccessToolbarCommandIds.New or
            QuickAccessToolbarCommandIds.Open or
            QuickAccessToolbarCommandIds.Save or
            QuickAccessToolbarCommandIds.SaveAs or
            QuickAccessToolbarCommandIds.CalculateNow or
            QuickAccessToolbarCommandIds.RefreshAll or
            QuickAccessToolbarCommandIds.NameManager or
            QuickAccessToolbarCommandIds.InsertSheet => true,

            QuickAccessToolbarCommandIds.Print or
            QuickAccessToolbarCommandIds.ExportPdfXps or
            QuickAccessToolbarCommandIds.CalculateSheet or
            QuickAccessToolbarCommandIds.Spelling or
            QuickAccessToolbarCommandIds.Zoom100 or
            QuickAccessToolbarCommandIds.FindSelect => state.HasActiveWorksheet,

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
            QuickAccessToolbarCommandIds.FreezePanes => state.HasActiveWorksheet && state.HasSelection,

            _ => false
        };
}
