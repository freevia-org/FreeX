using System.Globalization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.DefinedNames;

/// <summary>Which edges of the selection carry the labels that become defined names.</summary>
public readonly record struct CreateNamesFromSelectionOptions(
    bool UseTopRow,
    bool UseLeftColumn,
    bool UseBottomRow,
    bool UseRightColumn)
{
    /// <summary>True when at least one edge is selected.</summary>
    public bool HasAnyEdge => UseTopRow || UseLeftColumn || UseBottomRow || UseRightColumn;
}

/// <summary>A single defined name planned from a selection: its name, refers-to range, and source label edge.</summary>
public readonly record struct PlannedDefinedName(string Name, GridRange Range, CreateNamesLabelEdge Edge);

/// <summary>The selection edge a planned name's label came from.</summary>
public enum CreateNamesLabelEdge
{
    /// <summary>Labels along the top row name the columns beneath them.</summary>
    TopRow,

    /// <summary>Labels along the bottom row name the columns above them.</summary>
    BottomRow,

    /// <summary>Labels in the left column name the rows to their right.</summary>
    LeftColumn,

    /// <summary>Labels in the right column name the rows to their left.</summary>
    RightColumn
}

/// <summary>
/// Portable planner for "Create Names from Selection". Given a selection range, the cell values that fill it,
/// and which label edges to use, it produces the set of <see cref="PlannedDefinedName"/>s a host would then
/// commit. It faithfully mirrors the desktop hosts' Core command: labels are sanitized into legal name text
/// (illegal characters collapse to single underscores, leading non-letter/underscore gets an underscore
/// prefix, length is capped at 255); names that would still be invalid are prefixed with '_'; and collisions
/// — against both already-planned names and names already defined elsewhere — are de-duplicated with a
/// numeric suffix. Pure data in, pure data out; no renderer or host types are involved.
/// </summary>
public static class CreateNamesFromSelectionPlanner
{
    /// <summary>
    /// Plans the names to create. <paramref name="cellText"/> supplies the displayed/label text for a cell at
    /// a given (row, col) — return null or empty for blank cells. <paramref name="existingNames"/> are the
    /// names already defined that the planner must avoid colliding with (compared case-insensitively).
    /// </summary>
    /// <param name="selection">The selected range. Must be on a single sheet.</param>
    /// <param name="options">Which label edges to use.</param>
    /// <param name="cellText">Maps a cell address to its label text; null/empty means no label.</param>
    /// <param name="existingNames">Names already defined in the workbook to avoid colliding with.</param>
    public static IReadOnlyList<PlannedDefinedName> Plan(
        GridRange selection,
        CreateNamesFromSelectionOptions options,
        Func<CellAddress, string?> cellText,
        IEnumerable<string>? existingNames = null)
    {
        ArgumentNullException.ThrowIfNull(cellText);

        var results = new List<PlannedDefinedName>();
        if (!options.HasAnyEdge)
            return results;

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reservedNames = existingNames is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        var sheet = selection.Start.Sheet;
        var startRow = selection.Start.Row;
        var endRow = selection.End.Row;
        var startCol = selection.Start.Col;
        var endCol = selection.End.Col;
        var hasMultipleRows = selection.RowCount > 1;
        var hasMultipleCols = selection.ColCount > 1;

        if (options.UseTopRow && hasMultipleRows)
        {
            for (var col = startCol; col <= endCol; col++)
            {
                if (TryPlanName(cellText, sheet, startRow, col, usedNames, reservedNames, out var name))
                {
                    results.Add(new PlannedDefinedName(
                        name,
                        new GridRange(
                            new CellAddress(sheet, startRow + 1, col),
                            new CellAddress(sheet, endRow, col)),
                        CreateNamesLabelEdge.TopRow));
                }
            }
        }

        if (options.UseBottomRow && hasMultipleRows)
        {
            for (var col = startCol; col <= endCol; col++)
            {
                if (TryPlanName(cellText, sheet, endRow, col, usedNames, reservedNames, out var name))
                {
                    results.Add(new PlannedDefinedName(
                        name,
                        new GridRange(
                            new CellAddress(sheet, startRow, col),
                            new CellAddress(sheet, endRow - 1, col)),
                        CreateNamesLabelEdge.BottomRow));
                }
            }
        }

        if (options.UseLeftColumn && hasMultipleCols)
        {
            for (var row = startRow; row <= endRow; row++)
            {
                if (TryPlanName(cellText, sheet, row, startCol, usedNames, reservedNames, out var name))
                {
                    results.Add(new PlannedDefinedName(
                        name,
                        new GridRange(
                            new CellAddress(sheet, row, startCol + 1),
                            new CellAddress(sheet, row, endCol)),
                        CreateNamesLabelEdge.LeftColumn));
                }
            }
        }

        if (options.UseRightColumn && hasMultipleCols)
        {
            for (var row = startRow; row <= endRow; row++)
            {
                if (TryPlanName(cellText, sheet, row, endCol, usedNames, reservedNames, out var name))
                {
                    results.Add(new PlannedDefinedName(
                        name,
                        new GridRange(
                            new CellAddress(sheet, row, startCol),
                            new CellAddress(sheet, row, endCol - 1)),
                        CreateNamesLabelEdge.RightColumn));
                }
            }
        }

        return results;
    }

    private static bool TryPlanName(
        Func<CellAddress, string?> cellText,
        SheetId sheet,
        uint row,
        uint col,
        HashSet<string> usedNames,
        HashSet<string> reservedNames,
        out string name)
    {
        name = "";
        var label = cellText(new CellAddress(sheet, row, col));
        if (string.IsNullOrWhiteSpace(label))
            return false;

        var candidate = SanitizeName(label);
        if (string.IsNullOrWhiteSpace(candidate))
            return false;
        if (!DefinedNameValidator.Validate(candidate).IsValid)
            candidate = "_" + candidate;

        name = MakeUnique(candidate, usedNames, reservedNames);
        usedNames.Add(name);
        return true;
    }

    /// <summary>
    /// Sanitize a raw label into legal defined-name text: illegal characters become underscores, runs of
    /// underscores collapse, leading/trailing underscores are trimmed, a leading non-letter/underscore gets an
    /// underscore prefix, and the result is capped at 255 characters. Mirrors the desktop hosts' Core command.
    /// </summary>
    public static string SanitizeName(string label)
    {
        var chars = label.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '.' ? ch : '_')
            .ToArray();
        var name = new string(chars);
        while (name.Contains("__", StringComparison.Ordinal))
            name = name.Replace("__", "_", StringComparison.Ordinal);
        name = name.Trim('_');
        if (name.Length == 0)
            return "";
        if (!char.IsLetter(name[0]) && name[0] != '_')
            name = "_" + name;
        return name.Length > DefinedNameValidator.MaxNameLength
            ? name[..DefinedNameValidator.MaxNameLength]
            : name;
    }

    private static string MakeUnique(string baseName, HashSet<string> usedNames, HashSet<string> reservedNames)
    {
        var name = baseName;
        var suffix = 2;
        while (usedNames.Contains(name)
            || reservedNames.Contains(name)
            || !DefinedNameValidator.Validate(name).IsValid)
        {
            var suffixText = "_" + suffix.ToString(CultureInfo.InvariantCulture);
            var maxBaseLength = Math.Max(1, DefinedNameValidator.MaxNameLength - suffixText.Length);
            name = (baseName.Length > maxBaseLength ? baseName[..maxBaseLength] : baseName) + suffixText;
            suffix++;
        }

        return name;
    }
}
