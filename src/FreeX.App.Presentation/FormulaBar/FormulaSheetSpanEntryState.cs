using FreeX.Core.Model;

namespace FreeX.App.Presentation.FormulaBar;

/// <summary>
/// The sheet-tab portion of an Excel formula point-entry gesture. A plain tab click starts a
/// pending span; Shift+click chooses its end sheet. Cell/range selection is applied separately.
/// </summary>
public readonly record struct FormulaSheetSpanEntryState(
    string? StartSheetName,
    string? EndSheetName)
{
    public bool HasStart => !string.IsNullOrWhiteSpace(StartSheetName);

    public bool HasSpan =>
        HasStart &&
        !string.IsNullOrWhiteSpace(EndSheetName) &&
        !string.Equals(StartSheetName, EndSheetName, StringComparison.OrdinalIgnoreCase);

    public static FormulaSheetSpanEntryState Empty => new(null, null);
}

public static class FormulaSheetSpanEntryPlanner
{
    public static FormulaSheetSpanEntryState PlanTabSelection(
        FormulaSheetSpanEntryState current,
        string activeSheetName,
        string clickedSheetName,
        bool shiftHeld)
    {
        if (string.IsNullOrWhiteSpace(clickedSheetName))
            return current;

        if (!shiftHeld)
            return new FormulaSheetSpanEntryState(clickedSheetName, null);

        var startSheetName = current.HasStart ? current.StartSheetName : activeSheetName;
        return new FormulaSheetSpanEntryState(startSheetName, clickedSheetName);
    }

    public static string FormatSheetQualifier(FormulaSheetSpanEntryState state) =>
        state.HasSpan
            ? FormatSheetSpan(state.StartSheetName!, state.EndSheetName!)
            : throw new ArgumentException("A complete sheet span is required.", nameof(state));

    private static string FormatSheetSpan(string startSheetName, string endSheetName)
    {
        if (!SheetNameFormatter.NeedsQuoting(startSheetName) &&
            !SheetNameFormatter.NeedsQuoting(endSheetName))
        {
            return $"{startSheetName}:{endSheetName}";
        }

        var escapedStart = startSheetName.Replace("'", "''", StringComparison.Ordinal);
        var escapedEnd = endSheetName.Replace("'", "''", StringComparison.Ordinal);
        return $"'{escapedStart}:{escapedEnd}'";
    }
}
