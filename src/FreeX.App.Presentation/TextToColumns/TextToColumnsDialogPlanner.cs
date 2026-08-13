using FreeX.App.Presentation;
using Free.Shared.Localization;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.TextToColumns;

public enum TextToColumnsDialogValidationIssue
{
    InvalidDestination,
    MissingFixedWidthBreaks,
    MissingDelimiter,
    MissingCustomDelimiter,
    InvalidDecimalSeparator,
    InvalidThousandsSeparator,
    NoColumnsToWrite
}

public enum TextToColumnsDialogFocusTarget
{
    Destination,
    FixedWidthBreaks,
    DelimiterSelection,
    CustomDelimiter,
    DecimalSeparator,
    ThousandsSeparator,
    Preview
}

/// <summary>
/// Portable, UI-free glue between a Text-to-Columns dialog and the cell-write command path. It turns
/// <see cref="TextToColumnsDialogState"/> into <see cref="TextToColumnsOptions"/>, maps a planned
/// <see cref="TextToColumnsResult"/> over the source column into the concrete set of cell edits (honoring
/// <see cref="TextToColumnsColumnFormat.Skip"/> columns and the per-column format hints), and reports
/// which non-empty cells to the right of the source column an apply would overwrite. No UI types, so it is
/// unit-testable without a running window and shareable across shells.
/// </summary>
public static class TextToColumnsDialogPlanner
{
    public static ValidationPresentationDescriptor<TextToColumnsDialogFocusTarget> DescribeValidationIssue(
        TextToColumnsDialogValidationIssue issue) =>
        issue switch
        {
            TextToColumnsDialogValidationIssue.InvalidDestination => Describe(
                "TextToColumns_EnterASingleDestinationCellSuchAsF2",
                TextToColumnsDialogFocusTarget.Destination),
            TextToColumnsDialogValidationIssue.MissingFixedWidthBreaks => Describe(
                "TextToColumns_EnterAtLeastOneFixedWidthBreakPosition",
                TextToColumnsDialogFocusTarget.FixedWidthBreaks),
            TextToColumnsDialogValidationIssue.MissingDelimiter => Describe(
                "TextToColumns_SelectAtLeastOneDelimiter",
                TextToColumnsDialogFocusTarget.DelimiterSelection),
            TextToColumnsDialogValidationIssue.MissingCustomDelimiter => Describe(
                "TextToColumns_CustomDelimiterIsRequired",
                TextToColumnsDialogFocusTarget.CustomDelimiter),
            TextToColumnsDialogValidationIssue.InvalidDecimalSeparator => Describe(
                "TextToColumns_EnterASingleDecimalSeparator",
                TextToColumnsDialogFocusTarget.DecimalSeparator),
            TextToColumnsDialogValidationIssue.InvalidThousandsSeparator => Describe(
                "TextToColumns_EnterASingleThousandsSeparator",
                TextToColumnsDialogFocusTarget.ThousandsSeparator),
            TextToColumnsDialogValidationIssue.NoColumnsToWrite => Describe(
                "TableLoc_TtcNoColumnsToWrite",
                TextToColumnsDialogFocusTarget.Preview),
            _ => throw new ArgumentOutOfRangeException(nameof(issue), issue, null)
        };

    public static bool TryBuildOptions(
        TextToColumnsDialogState state,
        out TextToColumnsOptions options,
        out TextToColumnsDialogValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SplitMode == TextToColumnsSplitMode.FixedWidth &&
            NormalizeBreakPositions(state.FixedWidthBreakPositions).Count == 0)
        {
            options = default!;
            issue = TextToColumnsDialogValidationIssue.MissingFixedWidthBreaks;
            return false;
        }

        if (state.SplitMode != TextToColumnsSplitMode.FixedWidth && SelectedDelimiterKinds(state).Count == 0)
        {
            options = default!;
            issue = TextToColumnsDialogValidationIssue.MissingDelimiter;
            return false;
        }

        options = BuildOptions(state);
        issue = default;
        return true;
    }

    public static IReadOnlyList<string> BuildPreviewRows(Sheet? sheet, GridRange range, int maxRows = 3)
    {
        if (sheet is null)
            return [];

        var rows = new List<string>();
        for (var row = range.Start.Row; row <= range.End.Row && rows.Count < maxRows; row++)
        {
            if (sheet.GetValue(row, range.Start.Col) is TextValue text && !string.IsNullOrWhiteSpace(text.Value))
                rows.Add(text.Value);
        }

        return rows;
    }

    public static bool CanConvertRange(GridRange range) =>
        range.Start.Col == range.End.Col;

    public static bool TryParseDestination(string? input, CellAddress defaultDestination, out CellAddress destination)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            destination = default;
            return false;
        }

        return CellReferenceInputParser.TryParseCell(input, defaultDestination.Sheet, out destination);
    }

    public static IReadOnlyList<TextToColumnsColumnFormat> NormalizeColumnFormats(
        IReadOnlyList<TextToColumnsColumnFormat>? columnFormats)
    {
        if (columnFormats is null || columnFormats.Count == 0)
            return [];

        var normalized = columnFormats.ToList();
        while (normalized.Count > 0 && normalized[^1] == TextToColumnsColumnFormat.General)
            normalized.RemoveAt(normalized.Count - 1);
        return normalized;
    }

    public static bool TryParseAdvancedSeparator(string? value, out string separator)
    {
        separator = string.Empty;
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length != 1)
            return false;

        separator = trimmed;
        return true;
    }

    public static TextToColumnsTextQualifier TextQualifierFromSelectedIndex(int selectedIndex) =>
        selectedIndex switch
        {
            1 => TextToColumnsTextQualifier.SingleQuote,
            2 => TextToColumnsTextQualifier.None,
            _ => TextToColumnsTextQualifier.DoubleQuote
        };

    public static TextToColumnsColumnFormat DateColumnFormatFromLabel(string? label) =>
        label switch
        {
            "DMY" => TextToColumnsColumnFormat.DateDMY,
            "YMD" => TextToColumnsColumnFormat.DateYMD,
            "MYD" => TextToColumnsColumnFormat.DateMYD,
            "DYM" => TextToColumnsColumnFormat.DateDYM,
            "YDM" => TextToColumnsColumnFormat.DateYDM,
            _ => TextToColumnsColumnFormat.DateMDY
        };

    public static bool IsDateColumnFormat(TextToColumnsColumnFormat format) =>
        format is TextToColumnsColumnFormat.DateMDY
            or TextToColumnsColumnFormat.DateDMY
            or TextToColumnsColumnFormat.DateYMD
            or TextToColumnsColumnFormat.DateMYD
            or TextToColumnsColumnFormat.DateDYM
            or TextToColumnsColumnFormat.DateYDM;

    public static string DateColumnFormatLabel(TextToColumnsColumnFormat format) =>
        format switch
        {
            TextToColumnsColumnFormat.DateDMY => "DMY",
            TextToColumnsColumnFormat.DateYMD => "YMD",
            TextToColumnsColumnFormat.DateMYD => "MYD",
            TextToColumnsColumnFormat.DateDYM => "DYM",
            TextToColumnsColumnFormat.DateYDM => "YDM",
            _ => "MDY"
        };

    public static IReadOnlyList<TextToColumnsColumnFormat> BuildColumnFormats(
        int columnCount,
        IReadOnlyDictionary<int, TextToColumnsColumnFormat> storedFormats)
    {
        var formats = Enumerable.Range(0, columnCount)
            .Select(index => storedFormats.TryGetValue(index, out var format)
                ? format
                : TextToColumnsColumnFormat.General)
            .ToList();
        return NormalizeColumnFormats(formats);
    }

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

    private static ValidationPresentationDescriptor<TextToColumnsDialogFocusTarget> Describe(
        string resourceKey,
        TextToColumnsDialogFocusTarget focusTarget) =>
        new(LocalizedTextDescriptor.Resource(resourceKey), focusTarget);

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
    /// consumes a target column). Values are converted from their format hint, and each field's leading
    /// and trailing whitespace is trimmed, matching Excel's General-format Text to Columns behavior. Only
    /// the columns an individual row actually splits into are written; columns beyond a shorter row's
    /// field count are left untouched (matching Excel, which does not clear stale trailing cells left
    /// over from a previous, wider split).
    /// </summary>
    public static IReadOnlyList<(CellAddress Address, Cell NewCell)> MapToEdits(
        SheetId sheetId,
        TextToColumnsResult result,
        GridRange sourceRange,
        TextToColumnsAdvancedOptions? advancedOptions = null) =>
        TextToColumnsApplyPlanner.MapResultToEdits(
            sheetId,
            result,
            sourceRange,
            advancedOptions: advancedOptions);

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
}
