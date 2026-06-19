using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.TableUI;

/// <summary>
/// The validated outcome of the Table Name dialog: the normalized (trimmed) name to apply.
/// </summary>
public sealed record TableNameValues(string Name);

/// <summary>
/// Portable, UI-free planning for the structured-table "Table Name" dialog: capturing the table's current
/// display name as the dialog's initial text, and validating a typed name (uniqueness across tables and
/// named ranges, the letter/underscore start + letters/digits/underscore/period body rule, the no-cell-
/// reference rule, and the 255-char limit) before the rename command runs. The validation is single-sourced
/// onto the shared Core <see cref="StructuredTableDesignCommandHelpers.ValidateTableName"/> guard (the same
/// rule <see cref="RenameStructuredTableCommand"/> enforces on apply) so every desktop host validates
/// identically and the dialog can surface the exact error inline. Building the dialog and running the command
/// (the host hands <see cref="TableNameValues.Name"/> to <see cref="RenameStructuredTableCommand"/>) stays
/// with each shell's command glue.
/// </summary>
public static class TableNamePlanner
{
    public const string EmptyNameMessage = "Table name is invalid: it cannot be blank.";

    /// <summary>The table's current display name (falling back to its internal name) as the dialog's seed.</summary>
    public static string Capture(StructuredTableModel table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return string.IsNullOrWhiteSpace(table.DisplayName) ? table.Name : table.DisplayName;
    }

    /// <summary>
    /// Validates the dialog's typed name against the shared Core rule (uniqueness, format, no cell-reference,
    /// length), excluding the table being renamed from the uniqueness check. On success returns the trimmed
    /// name to apply; on failure reports the exact user-facing message.
    /// </summary>
    public static bool TryCreateRename(
        Workbook workbook,
        SheetId sheetId,
        int tableId,
        string? typedName,
        out TableNameValues? values,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        values = null;
        error = null;

        var trimmed = typedName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = EmptyNameMessage;
            return false;
        }

        if (StructuredTableDesignCommandHelpers.ValidateTableName(workbook, trimmed, sheetId, tableId) is { } message)
        {
            error = message;
            return false;
        }

        values = new TableNameValues(trimmed);
        return true;
    }
}
