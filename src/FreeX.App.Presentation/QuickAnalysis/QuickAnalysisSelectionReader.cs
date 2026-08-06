using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.QuickAnalysis;

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

        var tableContext = StructuredTableSelectionPlanner.Describe(sheet, range);
        var hasHeaderRow = tableContext?.IncludesHeader ?? DetectHeaderRow(sheet, range);
        var dataRange = ResolveDataRange(range, hasHeaderRow, tableContext);
        var contentRange = TryGetContentRange(sheet, dataRange);

        var columnKinds = new QuickAnalysisColumnKind[range.ColCount];
        for (var i = 0u; i < range.ColCount; i++)
        {
            columnKinds[i] = ClassifyColumn(
                sheet,
                contentRange,
                range.Start.Col + i);
        }

        return new QuickAnalysisSelectionDescription(range, hasHeaderRow, columnKinds)
        {
            StructuredTableContext = tableContext,
            OverlapsStructuredTable = StructuredTableSelectionPlanner.OverlapsAnyTable(sheet, range),
            DataRowCountOverride = tableContext is null
                ? null
                : dataRange?.RowCount ?? 0
        };
    }

    /// <summary>
    /// Classifies a single column over the bounded content range. A numeric column with trailing blanks is
    /// still Numeric; a
    /// fully blank column is Empty. Numbers win ties when a column mixes kinds, matching the desktop hosts'
    /// bias toward offering number-driven suggestions.
    /// </summary>
    private static QuickAnalysisColumnKind ClassifyColumn(Sheet sheet, GridRange? contentRange, uint col)
    {
        if (contentRange is not { } range || col < range.Start.Col || col > range.End.Col)
            return QuickAnalysisColumnKind.Empty;

        var sawNumeric = false;
        var sawDate = false;
        var sawText = false;

        for (var row = range.Start.Row; row <= range.End.Row; row++)
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
    /// Heuristic header detection: a multi-row selection has a header row when at least one column shows the
    /// classic "labels over values" shape -- a non-empty text cell in the first row sitting over numeric/date
    /// data below. Unlike a strict "first row is entirely text" rule, a column whose header cell happens to be
    /// numeric/date itself (e.g. a year used as a column heading) does not veto detection, as long as it does not
    /// contradict the pattern -- i.e. as long as that column's data rows below aren't purely text (which would
    /// suggest the "header" is really just a stray data value sitting over label data, not a heading). A
    /// text-only grid (no numeric/date data anywhere) is treated as having no header so it does not masquerade
    /// as a typed table.
    /// </summary>
    private static bool DetectHeaderRow(Sheet sheet, GridRange range)
    {
        if (range.RowCount < 2)
            return false;

        var contentRange = TryGetContentRange(sheet, range);
        if (contentRange is null)
            return false;

        var headerRow = range.Start.Row;
        var sawLabelOverValueColumn = false;

        for (var col = contentRange.Value.Start.Col; col <= contentRange.Value.End.Col; col++)
        {
            var headerIsText = sheet.GetValue(headerRow, col) is TextValue { Value.Length: > 0 };

            var belowHasNumericOrDate = false;
            var belowHasNonText = false;
            var belowHasAnyValue = false;
            var firstContentRow = Math.Max(headerRow + 1, contentRange.Value.Start.Row);
            for (var row = firstContentRow; row <= contentRange.Value.End.Row; row++)
            {
                var value = sheet.GetValue(row, col);
                if (value is BlankValue)
                    continue;

                belowHasAnyValue = true;
                if (value is NumberValue or DateTimeValue or BoolValue)
                    belowHasNumericOrDate = true;
                if (value is not TextValue)
                    belowHasNonText = true;
            }

            if (headerIsText)
            {
                // A text heading over numeric/date data is the classic "labels over values" signal.
                if (belowHasNumericOrDate)
                    sawLabelOverValueColumn = true;

                continue;
            }

            // The header cell itself isn't text (e.g. a numeric column heading such as a year). That is
            // only consistent with a header row when the column below isn't purely text -- otherwise the
            // "header" looks like a stray data value sitting over label data, so bail conservatively rather
            // than let another column's signal override an outright contradiction.
            if (belowHasAnyValue && !belowHasNonText)
                return false;
        }

        return sawLabelOverValueColumn;
    }

    private static GridRange? ResolveDataRange(
        GridRange selection,
        bool hasHeaderRow,
        StructuredTableSelectionContext? tableContext)
    {
        if (tableContext is not null)
        {
            return tableContext.DataBodyRange is { } body &&
                   GridRange.TryIntersect(selection, body, out var selectedBody)
                ? selectedBody
                : null;
        }

        var firstDataRow = hasHeaderRow && selection.RowCount > 1
            ? selection.Start.Row + 1
            : selection.Start.Row;
        return firstDataRow <= selection.End.Row
            ? new GridRange(
                new CellAddress(selection.Start.Sheet, firstDataRow, selection.Start.Col),
                selection.End)
            : null;
    }

    private static GridRange? TryGetContentRange(Sheet sheet, GridRange? range)
    {
        if (range is not { } candidate ||
            sheet.GetUsedRange() is not { } usedRange ||
            !GridRange.TryIntersect(candidate, usedRange, out var intersection))
        {
            return null;
        }

        return intersection;
    }
}
