using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Portable, UI-free planning for the "PivotTable Name" rename dialog: capturing the active pivot's current
/// name as the dialog's seed text, normalizing the typed name (trim), and validating it (non-empty plus not
/// colliding with another PivotTable's name). Name-collision lookup is supplied by the host as a delegate
/// (the shells pass a closure over the workbook's pivot tables) so the planner stays free of workbook/UI
/// types and is unit-testable. Building the dialog and running the command stays with each shell's command
/// glue (the host hands <see cref="PivotNameResult.Name"/> to <c>RenamePivotTableCommand</c>).
/// </summary>
public static class PivotNamePlanner
{
    public const string EmptyNameMessage = "Enter a name for the PivotTable.";

    public const string DuplicateNameMessage = "A PivotTable with that name already exists.";

    /// <summary>
    /// Reports whether <paramref name="candidateName"/> is already used by a PivotTable other than the one
    /// being renamed. The shells pass a closure over the workbook's pivot tables; the planner only needs the
    /// answer, not the workbook model.
    /// </summary>
    public delegate bool NameCollisionCheck(string candidateName);

    /// <summary>The validated outcome of the name dialog: the normalized (trimmed) name to apply.</summary>
    public sealed record PivotNameResult(string Name);

    /// <summary>Trims the typed name; null/blank collapses to an empty string.</summary>
    public static string Normalize(string? name) => name?.Trim() ?? string.Empty;

    /// <summary>Snapshots the pivot's current name as the dialog's initial input text.</summary>
    public static string Capture(PivotTableModel pivotTable)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return pivotTable.Name;
    }

    /// <summary>
    /// Validates the typed name: non-empty after trimming, and (when changed) not colliding with another
    /// PivotTable per <paramref name="isNameInUse"/>. On success returns the normalized result; on failure
    /// reports a user-facing message. A no-op rename to the current name is allowed (returns success).
    /// </summary>
    public static bool TryCreateResult(
        PivotTableModel pivotTable,
        string? typedName,
        NameCollisionCheck isNameInUse,
        out PivotNameResult? result,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(isNameInUse);
        result = null;
        error = null;

        var normalized = Normalize(typedName);
        if (normalized.Length == 0)
        {
            error = EmptyNameMessage;
            return false;
        }

        // A no-op rename (same name, ignoring case-only changes is still allowed via the unchanged check) must
        // not be rejected by the collision check; only a genuinely different name is checked for duplicates.
        var unchanged = string.Equals(normalized, pivotTable.Name, StringComparison.Ordinal);
        if (!unchanged && isNameInUse(normalized))
        {
            error = DuplicateNameMessage;
            return false;
        }

        result = new PivotNameResult(normalized);
        return true;
    }
}
