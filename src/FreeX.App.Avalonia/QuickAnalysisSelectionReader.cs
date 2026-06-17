using FreeX.App.Presentation.QuickAnalysis;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Builds a portable <see cref="QuickAnalysisSelectionDescription"/> from a live sheet and selection by
/// reading the selected cells' values. UI-free so it can be unit tested without a running shell: it maps
/// each selected column to a <see cref="QuickAnalysisColumnKind"/> and detects a header row, exactly the
/// inputs <see cref="QuickAnalysisModelBuilder"/> needs.
/// </summary>
public static class QuickAnalysisSelectionReader
{
    /// <summary>
    /// Describes <paramref name="range"/> on <paramref name="sheet"/> for Quick Analysis: the data kind of
    /// each column (left to right) and whether the first row looks like a header. Reading uses
    /// <see cref="Sheet.GetValue(uint, uint)"/>, so blank cells map to <see cref="QuickAnalysisColumnKind.Empty"/>.
    /// </summary>
    public static QuickAnalysisSelectionDescription Describe(Sheet sheet, GridRange range)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        var hasHeaderRow = DetectHeaderRow(sheet, range);

        // The header row is labels, not data, so classify columns from the data rows only when one exists.
        var firstDataRow = hasHeaderRow && range.RowCount > 1 ? range.Start.Row + 1 : range.Start.Row;

        var columnKinds = new QuickAnalysisColumnKind[range.ColCount];
        for (var i = 0u; i < range.ColCount; i++)
            columnKinds[i] = ClassifyColumn(sheet, range, firstDataRow, range.Start.Col + i);

        return new QuickAnalysisSelectionDescription(range, hasHeaderRow, columnKinds);
    }

    /// <summary>
    /// Classifies a single column over the data rows [<paramref name="firstDataRow"/>, range end]. The kind
    /// is the first non-blank value's kind, so a numeric column with trailing blanks is still Numeric; a
    /// fully blank column is Empty. Numbers win ties when a column mixes kinds, matching the desktop hosts'
    /// bias toward offering number-driven suggestions.
    /// </summary>
    private static QuickAnalysisColumnKind ClassifyColumn(Sheet sheet, GridRange range, uint firstDataRow, uint col)
    {
        var sawNumeric = false;
        var sawDate = false;
        var sawText = false;

        for (var row = firstDataRow; row <= range.End.Row; row++)
        {
            switch (sheet.GetValue(row, col))
            {
                case NumberValue:
                case BoolValue:
                    sawNumeric = true;
                    break;
                case DateTimeValue:
                    sawDate = true;
                    break;
                case TextValue text when !string.IsNullOrEmpty(text.Value):
                    sawText = true;
                    break;
            }
        }

        if (sawNumeric)
            return QuickAnalysisColumnKind.Numeric;
        if (sawDate)
            return QuickAnalysisColumnKind.Date;
        if (sawText)
            return QuickAnalysisColumnKind.Text;

        return QuickAnalysisColumnKind.Empty;
    }

    /// <summary>
    /// Heuristic header detection: a multi-row selection has a header row when its first row is all text and
    /// at least one column's data rows are non-text (numbers or dates). That mirrors the common "labels over
    /// values" table shape; a text-only grid (no numeric/date data) is treated as having no header so it does
    /// not masquerade as a typed table.
    /// </summary>
    private static bool DetectHeaderRow(Sheet sheet, GridRange range)
    {
        if (range.RowCount < 2)
            return false;

        var headerRow = range.Start.Row;
        var firstRowAllText = true;
        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            if (sheet.GetValue(headerRow, col) is not TextValue { Value.Length: > 0 })
            {
                firstRowAllText = false;
                break;
            }
        }

        if (!firstRowAllText)
            return false;

        for (var col = range.Start.Col; col <= range.End.Col; col++)
        {
            for (var row = headerRow + 1; row <= range.End.Row; row++)
            {
                if (sheet.GetValue(row, col) is NumberValue or DateTimeValue or BoolValue)
                    return true;
            }
        }

        return false;
    }
}
