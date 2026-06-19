using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Portable, UI-free planning for the "Calculated Field" PivotTable dialog: listing the existing calculated
/// fields the dialog lets the user pick/modify/delete, seeding the name/formula boxes off a chosen field,
/// validating the name+formula, inserting a source-field reference token into the formula at the caret, and
/// rebuilding the pivot's <see cref="PivotTableModel.CalculatedFields"/> list (add/modify by name, or delete).
/// Single-sourced here so the desktop host and the Avalonia/macOS shell share identical behavior; building the
/// dialog and running the command stays with each shell's command glue (both hand the rebuilt calculated-field
/// list to <c>ConfigurePivotTableCalculatedItemsCommand</c>, leaving the row/column/page fields and calculated
/// items untouched). Mirrors the Core <see cref="PivotCalculatedFieldModel"/> model exactly, so no Core change
/// is required.
/// </summary>
public static class PivotCalculatedFieldPlanner
{
    public const string EmptyNameMessage = "Enter a name for the calculated field.";

    public const string EmptyFormulaMessage = "Enter a formula for the calculated field.";

    public const string NoFieldToDeleteMessage = "Select an existing calculated field to delete.";

    /// <summary>The validated outcome of the calculated-field dialog: the trimmed name and formula.</summary>
    public sealed record PivotCalculatedFieldResult(string Name, string Formula)
    {
        public PivotCalculatedFieldModel ToModel() => new(Name, Formula);
    }

    /// <summary>The existing calculated-field names, in order, for the dialog's picker.</summary>
    public static IReadOnlyList<string> ExistingFieldNames(PivotTableModel pivotTable)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return pivotTable.CalculatedFields.Select(field => field.Name).ToList();
    }

    /// <summary>The source-field captions available to insert as formula references.</summary>
    public static IReadOnlyList<string> AvailableFieldReferences(IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return headers
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The existing calculated field whose name matches (case-insensitively), or null.</summary>
    public static PivotCalculatedFieldModel? FindByName(PivotTableModel pivotTable, string? name)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var needle = name.Trim();
        return pivotTable.CalculatedFields.FirstOrDefault(
            field => string.Equals(field.Name, needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Validates the typed name + formula; on success yields the trimmed add/modify result.</summary>
    public static bool TryCreateResult(
        string? name,
        string? formula,
        out PivotCalculatedFieldResult? result,
        out string? error)
    {
        result = null;
        error = null;

        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
        {
            error = EmptyNameMessage;
            return false;
        }

        var trimmedFormula = formula?.Trim() ?? string.Empty;
        if (trimmedFormula.Length == 0)
        {
            error = EmptyFormulaMessage;
            return false;
        }

        result = new PivotCalculatedFieldResult(trimmedName, trimmedFormula);
        return true;
    }

    /// <summary>
    /// Rebuilds the calculated-field list with <paramref name="result"/> added or, when a field of the same
    /// name already exists, replaced in place (preserving order). Mirrors the desktop host's add/modify path.
    /// </summary>
    public static IReadOnlyList<PivotCalculatedFieldModel> Upsert(
        PivotTableModel pivotTable,
        PivotCalculatedFieldResult result)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(result);

        var model = result.ToModel();
        var existing = pivotTable.CalculatedFields.ToList();
        for (var index = 0; index < existing.Count; index++)
        {
            if (string.Equals(existing[index].Name, result.Name, StringComparison.OrdinalIgnoreCase))
            {
                existing[index] = model;
                return existing;
            }
        }

        existing.Add(model);
        return existing;
    }

    /// <summary>
    /// Rebuilds the calculated-field list with the field named <paramref name="name"/> removed. Reports an
    /// error (and leaves the list unchanged) when no calculated field matches the name.
    /// </summary>
    public static bool TryRemove(
        PivotTableModel pivotTable,
        string? name,
        out IReadOnlyList<PivotCalculatedFieldModel> calculatedFields,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        error = null;

        var remaining = pivotTable.CalculatedFields
            .Where(field => !string.Equals(field.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (remaining.Count == pivotTable.CalculatedFields.Count)
        {
            calculatedFields = pivotTable.CalculatedFields.ToList();
            error = NoFieldToDeleteMessage;
            return false;
        }

        calculatedFields = remaining;
        return true;
    }

    /// <summary>
    /// Inserts <paramref name="reference"/> into <paramref name="formula"/> at the selection, replacing the
    /// selected span. Returns the new formula text and the caret index just after the inserted token.
    /// </summary>
    public static (string Formula, int CaretIndex) InsertReference(
        string? formula,
        string? reference,
        int selectionStart,
        int selectionLength)
    {
        var safeFormula = formula ?? string.Empty;
        var safeReference = reference ?? string.Empty;
        var start = Math.Clamp(selectionStart, 0, safeFormula.Length);
        var length = Math.Clamp(selectionLength, 0, safeFormula.Length - start);
        var inserted = safeFormula.Remove(start, length).Insert(start, safeReference);
        var caret = Math.Min(inserted.Length, start + safeReference.Length);
        return (inserted, caret);
    }
}
