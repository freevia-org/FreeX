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

/// <summary>Why a Create Names from Selection option set is not actionable.</summary>
public enum CreateNamesFromSelectionInputError
{
    None,
    NoSelectedEdge,
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
    /// Auto-detects which label edges a host's Create Names from Selection dialog should pre-check for
    /// <paramref name="selection"/>, mirroring real Microsoft Excel 16.0 (en-US), whose behaviour was verified
    /// empirically by driving its dialog and reading the checkboxes back.
    /// <para>
    /// The rule: <b>Top row</b> is pre-checked when the selection spans more than one row, the selection's top
    /// row carries at least one text label (the top-left corner cell is ignored on a multi-column selection, so
    /// the blank-corner layout Excel treats as "top row + left column" still detects), and the body underneath
    /// it — the selection minus its top row and, when there is more than one column, minus its left column —
    /// contains no text at all. <b>Left column</b> is the exact transpose: more than one column, at least one
    /// text label down the left column (corner ignored on a multi-row selection), and the same body free of
    /// text. <b>Bottom row and Right column are never auto-detected</b> — Excel does not pre-check them even
    /// when only that edge carries labels. A wholly numeric selection and a wholly textual selection therefore
    /// both detect nothing: Excel only claims an edge when it can tell a label edge apart from a non-text body.
    /// </para>
    /// <para>
    /// Degenerate selections: the multi-row / multi-column guards above are deliberately the same guards
    /// <see cref="Plan"/> applies, so detection can never pre-check an edge that would produce no names. A
    /// single-row selection therefore never gets Top row (checking it would leave no data rows beneath) but may
    /// get Left column when its first cell is a label and the rest of the row is not text; a single-column
    /// selection is the transpose; a single cell and an empty range detect nothing.
    /// </para>
    /// </summary>
    /// <param name="selection">The selected range. Must be on a single sheet.</param>
    /// <param name="cellValue">
    /// Reads the raw value of a cell in the selection. Only <see cref="TextValue"/> with non-whitespace content
    /// counts as a label; numbers, dates, booleans, errors and blanks (including a null return) do not — a
    /// formula counts as whatever it evaluated to, exactly as Excel judges it.
    /// </param>
    public static CreateNamesFromSelectionOptions DetectOptions(
        GridRange selection,
        Func<CellAddress, ScalarValue?> cellValue)
    {
        ArgumentNullException.ThrowIfNull(cellValue);

        var sheet = selection.Start.Sheet;
        var startRow = selection.Start.Row;
        var endRow = selection.End.Row;
        var startCol = selection.Start.Col;
        var endCol = selection.End.Col;
        if (endRow < startRow || endCol < startCol)
            return default;

        var hasMultipleRows = selection.RowCount > 1;
        var hasMultipleCols = selection.ColCount > 1;

        // The corner cell only belongs to an edge when the other axis is a single line; case A (blank corner,
        // text top row, text left column) proves a blank corner must not veto detection.
        var bodyRow = hasMultipleRows ? startRow + 1 : startRow;
        var bodyCol = hasMultipleCols ? startCol + 1 : startCol;

        bool IsLabel(uint row, uint col) =>
            cellValue(new CellAddress(sheet, row, col)) is TextValue text
            && !string.IsNullOrWhiteSpace(text.Value);

        var bodyHasText = false;
        for (var row = bodyRow; row <= endRow && !bodyHasText; row++)
        {
            for (var col = bodyCol; col <= endCol; col++)
            {
                if (!IsLabel(row, col))
                    continue;
                bodyHasText = true;
                break;
            }
        }

        var useTopRow = false;
        if (hasMultipleRows && !bodyHasText)
        {
            for (var col = bodyCol; col <= endCol; col++)
            {
                if (!IsLabel(startRow, col))
                    continue;
                useTopRow = true;
                break;
            }
        }

        var useLeftColumn = false;
        if (hasMultipleCols && !bodyHasText)
        {
            for (var row = bodyRow; row <= endRow; row++)
            {
                if (!IsLabel(row, startCol))
                    continue;
                useLeftColumn = true;
                break;
            }
        }

        return new CreateNamesFromSelectionOptions(
            UseTopRow: useTopRow,
            UseLeftColumn: useLeftColumn,
            UseBottomRow: false,
            UseRightColumn: false);
    }

    public static bool TryCreateOptions(
        bool useTopRow,
        bool useLeftColumn,
        bool useBottomRow,
        bool useRightColumn,
        out CreateNamesFromSelectionOptions options,
        out CreateNamesFromSelectionInputError error)
    {
        options = new CreateNamesFromSelectionOptions(useTopRow, useLeftColumn, useBottomRow, useRightColumn);
        if (!options.HasAnyEdge)
        {
            error = CreateNamesFromSelectionInputError.NoSelectedEdge;
            return false;
        }

        error = CreateNamesFromSelectionInputError.None;
        return true;
    }

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
