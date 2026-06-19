using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.CustomViews;

/// <summary>
/// Portable (no UI framework) planning for the Custom Views feature, shared by the desktop hosts and the
/// macOS shell. A Custom View captures the workbook's per-sheet view state (the state the model can
/// represent today: view mode, frozen/split panes, gridlines/headings/rulers/formulas toggles, zoom, the
/// active cell and the scrolled-to top-left cell) plus the active-sheet index, and restores it on demand.
///
/// This planner single-sources the list projection (what the manager dialog shows), the new-view name
/// validation + default-name generation + uniqueness, and the mapping of a user action onto the Core
/// <see cref="SaveCustomViewCommand"/> / <see cref="ApplyCustomViewCommand"/> / <see cref="DeleteCustomViewCommand"/>
/// commands (which carry undo/redo). It deliberately holds no capture of state the model cannot represent:
/// the print-settings and hidden-rows/columns + filter flags are recorded on the view (so they round-trip and
/// match Excel's customWorkbookView toggles) but the shell does not yet snapshot the underlying page setup or
/// filter state — those are noted, not fabricated.
/// </summary>
public static class CustomViewsPlanner
{
    /// <summary>The longest a custom-view name may be (matches Excel's customWorkbookView name cap).</summary>
    public const int MaxNameLength = 255;

    /// <summary>A reason a proposed custom-view name was rejected.</summary>
    public enum NameError
    {
        None = 0,
        Blank,
        TooLong,
        Duplicate,
    }

    /// <summary>The outcome of validating a proposed custom-view name.</summary>
    public readonly record struct NameValidation(bool IsValid, NameError Error)
    {
        public static NameValidation Ok { get; } = new(true, NameError.None);

        public static NameValidation Fail(NameError error) => new(false, error);
    }

    /// <summary>
    /// A single row shown by the Custom Views manager: the view's name, how many sheets it captured, and the
    /// two Excel-parity inclusion flags (print settings / hidden rows-columns + filter settings).
    /// </summary>
    public readonly record struct Row(
        string Name,
        int SheetCount,
        bool IncludePrintSettings,
        bool IncludeHiddenRowsColumnsAndFilterSettings);

    /// <summary>
    /// Projects the workbook's stored custom views into manager rows, in workbook order. Pure: reads the model
    /// only.
    /// </summary>
    public static IReadOnlyList<Row> BuildRows(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var rows = new List<Row>(workbook.CustomViews.Count);
        foreach (var view in workbook.CustomViews)
        {
            rows.Add(new Row(
                view.Name,
                view.Sheets.Count,
                view.IncludePrintSettings,
                view.IncludeHiddenRowsColumnsAndFilterSettings));
        }

        return rows;
    }

    /// <summary>
    /// Suggests the next default name for a new view ("View 1", "View 2", …) based on the current view count.
    /// The shell passes the resulting <paramref name="format"/> ("View {0}") so the label is localizable.
    /// </summary>
    public static string SuggestDefaultName(Workbook workbook, string format)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(format);
        return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, workbook.CustomViews.Count + 1);
    }

    /// <summary>
    /// Validates a proposed new-view name against the workbook's existing views: non-blank, within
    /// <see cref="MaxNameLength"/>, and not a case-insensitive duplicate of an existing view (matching the Core
    /// <see cref="SaveCustomViewCommand"/>'s replace-by-name semantics, which we surface as a duplicate here so
    /// the manager warns instead of silently overwriting).
    /// </summary>
    public static NameValidation ValidateName(Workbook workbook, string? name)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return NameValidation.Fail(NameError.Blank);
        if (trimmed.Length > MaxNameLength)
            return NameValidation.Fail(NameError.TooLong);
        if (CustomViewStatePlanner.FindViewIndex(workbook, trimmed) >= 0)
            return NameValidation.Fail(NameError.Duplicate);

        return NameValidation.Ok;
    }

    /// <summary>
    /// Builds the Core command that captures the current workbook view state under <paramref name="name"/>.
    /// Callers validate the name first (via <see cref="ValidateName"/>); the trimming here mirrors the command.
    /// </summary>
    public static SaveCustomViewCommand BuildSaveCommand(
        string name,
        bool includePrintSettings = true,
        bool includeHiddenRowsColumnsAndFilterSettings = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new SaveCustomViewCommand(name, includePrintSettings, includeHiddenRowsColumnsAndFilterSettings);
    }

    /// <summary>Builds the Core command that restores the named view's captured state to the workbook.</summary>
    public static ApplyCustomViewCommand BuildApplyCommand(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ApplyCustomViewCommand(name);
    }

    /// <summary>Builds the Core command that deletes the named view.</summary>
    public static DeleteCustomViewCommand BuildDeleteCommand(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new DeleteCustomViewCommand(name);
    }
}
