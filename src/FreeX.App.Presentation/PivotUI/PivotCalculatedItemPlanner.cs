using FreeX.Core.Model;

namespace FreeX.App.Presentation.PivotUI;

/// <summary>
/// Portable, UI-free planning for the "Calculated Item" PivotTable dialog: enumerating the row/column source
/// fields a calculated item can belong to, listing the existing calculated items the dialog lets the user
/// pick/modify/delete, seeding the name/formula boxes off a chosen item, validating the name+formula+field,
/// inserting a source-field reference token into the formula at the caret, and rebuilding the pivot's
/// <see cref="PivotTableModel.CalculatedItems"/> list (add/modify by field+name, or delete). Single-sourced
/// here so the desktop host and the cross-platform shell share identical behavior; building the dialog and
/// running the command stays with each shell's command glue (both hand the rebuilt calculated-item list to
/// <c>ConfigurePivotTableCalculatedItemsCommand</c>, leaving the row/column/page fields and calculated fields
/// untouched). Mirrors the Core <see cref="PivotCalculatedItemModel"/> model exactly, so no Core change is
/// required.
/// </summary>
public static class PivotCalculatedItemPlanner
{
    public const string EmptyNameMessage = "Enter a name for the calculated item.";

    public const string EmptyFormulaMessage = "Enter a formula for the calculated item.";

    public const string NoSourceFieldMessage = "Add a row or column field to the PivotTable before adding a calculated item.";

    public const string NoItemToDeleteMessage = "Select an existing calculated item to delete.";

    /// <summary>A row/column source field a calculated item can be added to, with its display caption.</summary>
    public sealed record CalculatedItemField(int SourceFieldIndex, string Caption);

    /// <summary>The validated outcome of the calculated-item dialog: the trimmed name, formula, and target field.</summary>
    public sealed record PivotCalculatedItemResult(int SourceFieldIndex, string Name, string Formula)
    {
        public PivotCalculatedItemModel ToModel() => new(SourceFieldIndex, Name, Formula);
    }

    /// <summary>
    /// The row/column fields, in order, calculated items can attach to (Excel only lets calculated items live
    /// on row/column fields, not page/value fields). Deduplicated by source field index.
    /// </summary>
    public static IReadOnlyList<CalculatedItemField> AvailableFields(
        PivotTableModel pivotTable,
        IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(headers);

        var seen = new HashSet<int>();
        var result = new List<CalculatedItemField>();
        foreach (var field in pivotTable.RowFields.Concat(pivotTable.ColumnFields))
        {
            if (!seen.Add(field.SourceFieldIndex))
                continue;
            result.Add(new CalculatedItemField(field.SourceFieldIndex, FieldCaption(headers, field.SourceFieldIndex)));
        }

        return result;
    }

    /// <summary>Projects non-empty source headers into field options while preserving source indexes.</summary>
    public static IReadOnlyList<CalculatedItemField> AvailableSourceFields(IReadOnlyList<string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return headers
            .Select((name, index) => new CalculatedItemField(index, name?.Trim() ?? string.Empty))
            .Where(field => field.Caption.Length > 0)
            .ToList();
    }

    /// <summary>The source-field captions available to insert as formula references.</summary>
    public static IReadOnlyList<string> AvailableFieldReferences(IReadOnlyList<string> headers)
        => PivotCalculatedFieldPlanner.AvailableFieldReferences(headers);

    /// <summary>The existing calculated items for the given source field, in order, for the dialog's picker.</summary>
    public static IReadOnlyList<string> ExistingItemNames(PivotTableModel pivotTable, int sourceFieldIndex)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        return pivotTable.CalculatedItems
            .Where(item => item.SourceFieldIndex == sourceFieldIndex)
            .Select(item => item.Name)
            .ToList();
    }

    /// <summary>The existing calculated item on the field whose name matches (case-insensitively), or null.</summary>
    public static PivotCalculatedItemModel? FindByName(PivotTableModel pivotTable, int sourceFieldIndex, string? name)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var needle = name.Trim();
        return pivotTable.CalculatedItems.FirstOrDefault(
            item => item.SourceFieldIndex == sourceFieldIndex &&
                    string.Equals(item.Name, needle, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Validates the typed field + name + formula; on success yields the trimmed add/modify result.</summary>
    public static bool TryCreateResult(
        int sourceFieldIndex,
        string? name,
        string? formula,
        out PivotCalculatedItemResult? result,
        out string? error)
    {
        result = null;
        error = null;

        if (sourceFieldIndex < 0)
        {
            error = NoSourceFieldMessage;
            return false;
        }

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

        result = new PivotCalculatedItemResult(sourceFieldIndex, trimmedName, trimmedFormula);
        return true;
    }

    /// <summary>
    /// Rebuilds the calculated-item list with <paramref name="result"/> added or, when an item of the same
    /// field+name already exists, replaced in place (preserving order). Mirrors the desktop host's add/modify.
    /// </summary>
    public static IReadOnlyList<PivotCalculatedItemModel> Upsert(
        PivotTableModel pivotTable,
        PivotCalculatedItemResult result)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        ArgumentNullException.ThrowIfNull(result);

        var model = result.ToModel();
        var existing = pivotTable.CalculatedItems.ToList();
        for (var index = 0; index < existing.Count; index++)
        {
            if (existing[index].SourceFieldIndex == result.SourceFieldIndex &&
                string.Equals(existing[index].Name, result.Name, StringComparison.OrdinalIgnoreCase))
            {
                existing[index] = model;
                return existing;
            }
        }

        existing.Add(model);
        return existing;
    }

    /// <summary>
    /// Rebuilds the calculated-item list with the item named <paramref name="name"/> on
    /// <paramref name="sourceFieldIndex"/> removed. Reports an error (and leaves the list unchanged) when no
    /// calculated item matches.
    /// </summary>
    public static bool TryRemove(
        PivotTableModel pivotTable,
        int sourceFieldIndex,
        string? name,
        out IReadOnlyList<PivotCalculatedItemModel> calculatedItems,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(pivotTable);
        error = null;

        var needle = name?.Trim();
        var remaining = pivotTable.CalculatedItems
            .Where(item => !(item.SourceFieldIndex == sourceFieldIndex &&
                             string.Equals(item.Name, needle, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (remaining.Count == pivotTable.CalculatedItems.Count)
        {
            calculatedItems = pivotTable.CalculatedItems.ToList();
            error = NoItemToDeleteMessage;
            return false;
        }

        calculatedItems = remaining;
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
        => PivotCalculatedFieldPlanner.InsertReference(formula, reference, selectionStart, selectionLength);

    private static string FieldCaption(IReadOnlyList<string> headers, int sourceFieldIndex)
    {
        if (sourceFieldIndex >= 0 && sourceFieldIndex < headers.Count)
        {
            var header = headers[sourceFieldIndex]?.Trim();
            if (!string.IsNullOrEmpty(header))
                return header;
        }

        return $"Field {sourceFieldIndex + 1}";
    }
}
