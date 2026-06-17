using System.Globalization;

using FreeX.App.Presentation.TextToColumns;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// The dialog state captured from the Avalonia Text-to-Columns dialog, in a form the portable shell
/// planner can consume without any Avalonia UI types. Mirrors the controls the dialog exposes:
/// the split mode, the delimiter checkboxes (plus the "Other" character), treat-consecutive, the
/// text qualifier, the fixed-width break positions, and the per-output-column format hints.
/// </summary>
internal sealed record TextToColumnsDialogState(
    TextToColumnsSplitMode SplitMode,
    bool Tab,
    bool Semicolon,
    bool Comma,
    bool Space,
    bool Other,
    char? OtherDelimiter,
    bool TreatConsecutiveDelimitersAsOne,
    TextToColumnsTextQualifier TextQualifier,
    IReadOnlyList<int> FixedWidthBreakPositions,
    IReadOnlyList<TextToColumnsColumnFormat> ColumnFormats);

/// <summary>
/// Portable, UI-free glue between the Avalonia Text-to-Columns dialog and the cell-write command path.
/// It turns dialog state into <see cref="TextToColumnsOptions"/>, maps a planned
/// <see cref="TextToColumnsResult"/> over the source column into the concrete set of cell edits (honoring
/// <see cref="TextToColumnsColumnFormat.Skip"/> columns and the per-column format hints), and reports
/// which non-empty cells to the right of the source column an apply would overwrite. No Avalonia types,
/// so it is unit-testable without a running window.
/// </summary>
internal static class TextToColumnsShellPlanner
{
    /// <summary>True when the dialog state names at least one delimiter the splitter can act on.</summary>
    public static bool HasAnyDelimiter(TextToColumnsDialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return SelectedDelimiterKinds(state).Count > 0;
    }

    /// <summary>The well-known delimiter kinds the dialog state selects, in a stable order.</summary>
    public static IReadOnlyList<TextToColumnsDelimiterKind> SelectedDelimiterKinds(TextToColumnsDialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var kinds = new List<TextToColumnsDelimiterKind>();
        if (state.Tab) kinds.Add(TextToColumnsDelimiterKind.Tab);
        if (state.Semicolon) kinds.Add(TextToColumnsDelimiterKind.Semicolon);
        if (state.Comma) kinds.Add(TextToColumnsDelimiterKind.Comma);
        if (state.Space) kinds.Add(TextToColumnsDelimiterKind.Space);
        if (state.Other && state.OtherDelimiter is not null) kinds.Add(TextToColumnsDelimiterKind.Custom);
        return kinds;
    }

    /// <summary>
    /// Builds the portable split options from dialog state. Throws <see cref="ArgumentException"/> when
    /// delimited mode selects no usable delimiter, or fixed-width mode supplies no break positions.
    /// </summary>
    public static TextToColumnsOptions BuildOptions(TextToColumnsDialogState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.SplitMode == TextToColumnsSplitMode.FixedWidth)
        {
            var positions = NormalizeBreakPositions(state.FixedWidthBreakPositions);
            if (positions.Count == 0)
                throw new ArgumentException("Enter at least one fixed-width break position.", nameof(state));

            return TextToColumnsOptions.FixedWidth(positions, state.ColumnFormats);
        }

        var kinds = SelectedDelimiterKinds(state);
        if (kinds.Count == 0)
            throw new ArgumentException("Select at least one delimiter.", nameof(state));

        return TextToColumnsOptions.Delimited(
            kinds,
            state.OtherDelimiter,
            state.TreatConsecutiveDelimitersAsOne,
            state.TextQualifier,
            state.ColumnFormats);
    }

    /// <summary>Sorts, de-duplicates and drops non-positive fixed-width break positions.</summary>
    public static IReadOnlyList<int> NormalizeBreakPositions(IReadOnlyList<int>? positions)
    {
        if (positions is null || positions.Count == 0)
            return [];

        return positions
            .Where(p => p > 0)
            .Distinct()
            .OrderBy(p => p)
            .ToList();
    }

    /// <summary>
    /// Maps a planned split <paramref name="result"/> over the single source column of
    /// <paramref name="sourceRange"/> into the cell edits that realize it. For each source row, the
    /// non-skipped fields are written across consecutive columns starting at the source column; a
    /// <see cref="TextToColumnsColumnFormat.Skip"/> column is dropped (it neither produces an edit nor
    /// consumes a target column). Values are converted from their format hint. Every written cell is
    /// emitted (including blanks), so shorter rows overwrite stale values left over from a previous split.
    /// </summary>
    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> MapToEdits(
        SheetId sheetId,
        TextToColumnsResult result,
        GridRange sourceRange)
    {
        ArgumentNullException.ThrowIfNull(result);

        var edits = new List<(CellAddress, Cell)>();
        var startRow = sourceRange.Start.Row;
        var startCol = sourceRange.Start.Col;

        for (var rowIndex = 0; rowIndex < result.Rows.Count; rowIndex++)
        {
            var row = result.Rows[rowIndex];
            var targetOffset = 0u;

            for (var fieldIndex = 0; fieldIndex < result.ColumnCount; fieldIndex++)
            {
                var format = result.FormatFor(fieldIndex);
                if (format == TextToColumnsColumnFormat.Skip)
                    continue;

                var text = fieldIndex < row.Fields.Count ? row.Fields[fieldIndex] : string.Empty;
                var address = new CellAddress(sheetId, startRow + (uint)rowIndex, startCol + targetOffset);
                edits.Add((address, Cell.FromValue(ConvertValue(text, format))));
                targetOffset++;
            }
        }

        return edits;
    }

    /// <summary>
    /// The non-empty cells an apply would overwrite that lie outside the source column. These are the
    /// edits whose column is to the right of the source column and whose target cell currently holds a
    /// value. The source column itself is excluded (Text to Columns always rewrites it).
    /// </summary>
    public static IReadOnlyList<CellAddress> FindOverwriteTargets(
        Sheet sheet,
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        GridRange sourceRange)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(edits);

        var sourceColumn = sourceRange.Start.Col;
        var targets = new List<CellAddress>();
        foreach (var (address, _) in edits)
        {
            if (address.Col <= sourceColumn)
                continue;

            if (sheet.GetValue(address.Row, address.Col) is not (null or BlankValue))
                targets.Add(address);
        }

        return targets;
    }

    /// <summary>
    /// Converts a raw field string to a scalar according to its format hint: <c>Text</c> keeps the raw
    /// string, the date hints parse with the matching part order, and <c>General</c> infers number/bool
    /// /text. <c>Skip</c> never reaches here (those fields are dropped before mapping).
    /// </summary>
    private static ScalarValue ConvertValue(string text, TextToColumnsColumnFormat format) => format switch
    {
        TextToColumnsColumnFormat.Text => new TextValue(text),
        TextToColumnsColumnFormat.DateMDY when TryParseDate(text, 0, 1, 2, out var d) => new DateTimeValue(d.ToOADate()),
        TextToColumnsColumnFormat.DateDMY when TryParseDate(text, 1, 0, 2, out var d) => new DateTimeValue(d.ToOADate()),
        TextToColumnsColumnFormat.DateYMD when TryParseDate(text, 1, 2, 0, out var d) => new DateTimeValue(d.ToOADate()),
        TextToColumnsColumnFormat.DateMYD when TryParseDate(text, 0, 2, 1, out var d) => new DateTimeValue(d.ToOADate()),
        TextToColumnsColumnFormat.DateDYM when TryParseDate(text, 2, 0, 1, out var d) => new DateTimeValue(d.ToOADate()),
        TextToColumnsColumnFormat.DateYDM when TryParseDate(text, 2, 1, 0, out var d) => new DateTimeValue(d.ToOADate()),
        _ => InferGeneral(text)
    };

    private static ScalarValue InferGeneral(string text)
    {
        var trimmed = text.Trim();
        if (TryParseNumber(trimmed, out var number))
            return new NumberValue(number);

        if (trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
        {
            return new BoolValue(trimmed.Equals("TRUE", StringComparison.OrdinalIgnoreCase));
        }

        return new TextValue(text);
    }

    private static bool TryParseNumber(string text, out double number) =>
        (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out number) ||
         double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number)) &&
        double.IsFinite(number);

    private static bool TryParseDate(string text, int monthIndex, int dayIndex, int yearIndex, out DateTime date)
    {
        date = default;
        var parts = text.Split(['/', '-', '.'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[monthIndex], out var month) ||
            !int.TryParse(parts[dayIndex], out var day) ||
            !int.TryParse(parts[yearIndex], out var year))
        {
            return false;
        }

        if (year is >= 0 and < 100)
            year += year < 30 ? 2000 : 1900;

        try
        {
            date = new DateTime(year, month, day);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
