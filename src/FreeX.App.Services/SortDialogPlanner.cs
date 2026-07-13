using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using CoreSortKey = FreeX.Core.Commands.SortKey;

namespace FreeX.App.Services;

public sealed record SortColumnChoice(string Label, uint ColumnOffset);

public sealed record SortDirectionChoice(string Label, bool Ascending);

public sealed record SortOnChoice(string Label);

public sealed record SortColorChoice(string Label);

public sealed record SortDialogOptions(
    bool CaseSensitive = false,
    bool LeftToRight = false,
    string FirstKeySortOrder = "Normal");

public sealed record SortDialogPlannerText(
    string SortOnCellValues,
    string SortOnCellColor,
    string SortOnFontColor,
    string OrderAToZ,
    string OrderZToA,
    string OrderOnTop,
    string OrderOnBottom,
    string ColumnLabelFormat,
    string RowLabelFormat)
{
    public static SortDialogPlannerText Default { get; } = new(
        "Cell Values",
        "Cell Color",
        "Font Color",
        "A to Z",
        "Z to A",
        "On Top",
        "On Bottom",
        "Column {0}",
        "Row {0}");

    public string FormatColumnLabel(string columnName) =>
        string.Format(CultureInfo.CurrentCulture, ColumnLabelFormat, columnName);

    public string FormatRowLabel(uint rowNumber) =>
        string.Format(CultureInfo.CurrentCulture, RowLabelFormat, rowNumber);
}

public sealed class SortDialogLevel : IEquatable<SortDialogLevel>, INotifyPropertyChanged
{
    private uint _columnOffset;
    private bool _ascending;
    private string _sortOn;
    private string _targetColor = "";
    private IReadOnlyList<SortColorChoice> _colorChoices = [new SortColorChoice("")];
    private SortDialogPlannerText _text;

    public SortDialogLevel(uint columnOffset, bool ascending, SortDialogPlannerText? text = null)
    {
        _columnOffset = columnOffset;
        _ascending = ascending;
        _text = text ?? SortDialogPlannerText.Default;
        _sortOn = _text.SortOnCellValues;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public uint ColumnOffset
    {
        get => _columnOffset;
        set => SetField(ref _columnOffset, value);
    }

    public bool Ascending
    {
        get => _ascending;
        set => SetField(ref _ascending, value);
    }

    public string SortOn
    {
        get => _sortOn;
        set
        {
            if (SetField(ref _sortOn, value))
                OnPropertyChanged(nameof(OrderChoices));
        }
    }

    public string TargetColor
    {
        get => _targetColor;
        set => SetField(ref _targetColor, value);
    }

    public IReadOnlyList<SortDirectionChoice> OrderChoices =>
        SortDialogPlanner.BuildOrderChoices(SortOn, _text);

    public IReadOnlyList<SortColorChoice> ColorChoices => _colorChoices;

    internal SortDialogPlannerText Text => _text;

    public bool Equals(SortDialogLevel? other) =>
        other is not null &&
        ColumnOffset == other.ColumnOffset &&
        Ascending == other.Ascending &&
        string.Equals(SortOn, other.SortOn, StringComparison.Ordinal) &&
        string.Equals(TargetColor, other.TargetColor, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as SortDialogLevel);

    public override int GetHashCode() => HashCode.Combine(ColumnOffset, Ascending, SortOn, TargetColor.ToUpperInvariant());

    public override string ToString() => $"Column offset {ColumnOffset}, {(Ascending ? "Ascending" : "Descending")}";

    public void SetColorChoices(IReadOnlyList<SortColorChoice> colorChoices)
    {
        _colorChoices = colorChoices.Count == 0 ? [new SortColorChoice("")] : colorChoices;
        if (!string.IsNullOrWhiteSpace(TargetColor) &&
            !_colorChoices.Any(choice => string.Equals(choice.Label, TargetColor, StringComparison.OrdinalIgnoreCase)))
            TargetColor = "";
        OnPropertyChanged(nameof(ColorChoices));
    }

    internal void SetPlannerText(SortDialogPlannerText text)
    {
        var previousCellValuesLabel = _text.SortOnCellValues;
        var previousCellColorLabel = _text.SortOnCellColor;
        var previousFontColorLabel = _text.SortOnFontColor;
        _text = text;

        if (string.Equals(SortOn, previousCellValuesLabel, StringComparison.Ordinal))
            SortOn = text.SortOnCellValues;
        else if (string.Equals(SortOn, previousCellColorLabel, StringComparison.Ordinal))
            SortOn = text.SortOnCellColor;
        else if (string.Equals(SortOn, previousFontColorLabel, StringComparison.Ordinal))
            SortOn = text.SortOnFontColor;
        else
            OnPropertyChanged(nameof(OrderChoices));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public static class SortDialogPlanner
{
    public static IReadOnlyList<CoreSortKey> BuildSortKeys(
        IEnumerable<SortDialogLevel> levels,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        var normalized = NormalizeLevels(levels, resolvedText);
        var keys = new List<CoreSortKey>(normalized.Count);
        foreach (var level in normalized)
        {
            var sortOn = SortOnFromLabel(level.SortOn, resolvedText);
            keys.Add(new CoreSortKey(level.ColumnOffset, level.Ascending, sortOn, TargetColorFromText(level.TargetColor, sortOn)));
        }

        return keys;
    }

    /// <summary>
    /// Applies the Sort Options "First key sort order" custom list to the primary sort key,
    /// mirroring Excel which applies the chosen custom list only to the first key and only when
    /// it sorts on cell values. Returns the keys unchanged when no custom order is supplied.
    /// </summary>
    public static IReadOnlyList<CoreSortKey> ApplyCustomOrderToFirstKey(
        IReadOnlyList<CoreSortKey> keys,
        CustomSortOrder? customOrder)
    {
        if (customOrder is null || keys.Count == 0 ||
            keys[0].SortOn != SortOn.CellValues)
            return keys;

        var updated = keys.ToList();
        updated[0] = updated[0] with { CustomOrder = customOrder };
        return updated;
    }

    public static IReadOnlyList<SortDirectionChoice> BuildOrderChoices(
        string? sortOn,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        return SortOnFromLabel(sortOn, resolvedText) is SortOn.CellColor or SortOn.FontColor
            ? BuildColorDirectionChoices(resolvedText)
            : BuildDirectionChoices(resolvedText);
    }

    public static IReadOnlyList<SortDialogLevel> AddLevel(
        IEnumerable<SortDialogLevel> levels,
        uint columnOffset = 0,
        bool ascending = true,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        var normalized = NormalizeLevels(levels, resolvedText);
        var updated = new List<SortDialogLevel>(normalized.Count + 1);
        updated.AddRange(normalized);
        updated.Add(new SortDialogLevel(columnOffset, ascending, resolvedText));
        return updated;
    }

    public static IReadOnlyList<SortDialogLevel> RemoveLevel(
        IEnumerable<SortDialogLevel> levels,
        int index,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        var updated = NormalizeLevels(levels, resolvedText).ToList();
        if (index >= 0 && index < updated.Count)
            updated.RemoveAt(index);

        return updated.Count == 0 ? [new SortDialogLevel(0, true, resolvedText)] : updated;
    }

    public static IReadOnlyList<SortDialogLevel> CopyLevel(
        IEnumerable<SortDialogLevel> levels,
        int index,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        var updated = NormalizeLevels(levels, resolvedText).ToList();
        if (index >= 0 && index < updated.Count)
            updated.Insert(index + 1, CloneLevel(updated[index], resolvedText));

        return updated;
    }

    public static IReadOnlyList<SortDialogLevel> MoveLevel(
        IEnumerable<SortDialogLevel> levels,
        int index,
        int direction,
        SortDialogPlannerText? text = null)
    {
        var updated = NormalizeLevels(levels, text).ToList();
        var targetIndex = index + Math.Sign(direction);
        if (index < 0 || index >= updated.Count || targetIndex < 0 || targetIndex >= updated.Count)
            return updated;

        (updated[index], updated[targetIndex]) = (updated[targetIndex], updated[index]);
        return updated;
    }

    public static IReadOnlyList<SortDialogLevel> UpdateLevel(
        IEnumerable<SortDialogLevel> levels,
        int index,
        uint columnOffset,
        bool ascending,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        var updated = NormalizeLevels(levels, resolvedText).ToList();
        if (index >= 0 && index < updated.Count)
        {
            var existing = updated[index];
            var replacement = new SortDialogLevel(columnOffset, ascending, resolvedText)
            {
                SortOn = existing.SortOn
            };
            replacement.SetColorChoices(existing.ColorChoices);
            replacement.TargetColor = existing.TargetColor;
            updated[index] = replacement;
        }

        return updated;
    }

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(
        GridRange range,
        SortDialogPlannerText? text = null)
    {
        return BuildColumnChoices(null, range, hasHeaders: false, text);
    }

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(
        Sheet? sheet,
        GridRange range,
        bool hasHeaders,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        var choices = new List<SortColumnChoice>();
        for (uint offset = 0; offset < range.ColCount; offset++)
        {
            var columnName = CellAddress.NumberToColumnName(range.Start.Col + offset);
            var label = hasHeaders && sheet is not null
                ? GetHeaderLabel(sheet, range, offset, columnName, resolvedText)
                : resolvedText.FormatColumnLabel(columnName);
            choices.Add(new SortColumnChoice(label, offset));
        }

        return choices.Count == 0 ? [new SortColumnChoice(resolvedText.FormatColumnLabel("A"), 0)] : choices;
    }

    public static IReadOnlyList<SortColumnChoice> BuildRowChoices(
        GridRange range,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        var choices = new List<SortColumnChoice>();
        for (uint offset = 0; offset < range.RowCount; offset++)
            choices.Add(new SortColumnChoice(resolvedText.FormatRowLabel(range.Start.Row + offset), offset));

        return choices.Count == 0 ? [new SortColumnChoice(resolvedText.FormatRowLabel(1), 0)] : choices;
    }

    public static IReadOnlyList<SortColumnChoice> BuildActiveColumnChoices(
        SortDialogOptions options,
        bool hasHeaders,
        IReadOnlyList<SortColumnChoice> columnChoices,
        IReadOnlyList<SortColumnChoice> genericColumnChoices,
        IReadOnlyList<SortColumnChoice> rowChoices)
    {
        return options.LeftToRight
            ? rowChoices
            : hasHeaders
            ? columnChoices
            : genericColumnChoices;
    }

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(Workbook workbook, Sheet? sheet, GridRange range)
    {
        if (sheet is null)
            return [new SortColorChoice("")];

        var colors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in range.AllCells())
        {
            var style = GetCellStyle(workbook, sheet, address);
            if (style.FillColor is { } fillColor)
                colors.Add(CellColorPalettePlanner.FormatHexColor(fillColor));
            if (style.FontColor is { } fontColor)
                colors.Add(CellColorPalettePlanner.FormatHexColor(fontColor));
        }

        return BuildColorChoices(colors);
    }

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(
        Workbook workbook,
        Sheet? sheet,
        GridRange range,
        SortOn sortOn)
    {
        if (sheet is null)
            return [new SortColorChoice("")];

        var colors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in range.AllCells())
        {
            var style = GetCellStyle(workbook, sheet, address);
            var color = sortOn == SortOn.FontColor
                ? style.FontColor
                : style.FillColor;
            if (color is { } resolvedColor)
                colors.Add(CellColorPalettePlanner.FormatHexColor(resolvedColor));
        }

        return BuildColorChoices(colors);
    }

    /// <summary>
    /// R39-commands-sort-custom-2-3: a per-level color-swatch scan, scoped to the single column
    /// (<paramref name="columnOffset"/>, relative to <paramref name="range"/>.Start.Col) that sort
    /// level actually targets, with the header row excluded when <paramref name="hasHeaders"/> is
    /// set — mirroring Excel, which only ever offers the colors actually present in that column's
    /// data rows. The whole-range overload above scans every column and is only appropriate when
    /// no single target column is known yet (e.g. before a level's column is chosen).
    /// </summary>
    public static IReadOnlyList<SortColorChoice> BuildColorChoices(
        Workbook workbook,
        Sheet? sheet,
        GridRange range,
        SortOn sortOn,
        uint columnOffset,
        bool hasHeaders)
    {
        if (sheet is null)
            return [new SortColorChoice("")];

        var col = range.Start.Col + columnOffset;
        if (col > range.End.Col)
            return [new SortColorChoice("")];

        var columnRange = new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row, col),
            new CellAddress(range.Start.Sheet, range.End.Row, col));
        var dataRange = ExcludeHeaderRow(columnRange, hasHeaders);

        var colors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in dataRange.AllCells())
        {
            var style = GetCellStyle(workbook, sheet, address);
            var color = sortOn == SortOn.FontColor
                ? style.FontColor
                : style.FillColor;
            if (color is { } resolvedColor)
                colors.Add(CellColorPalettePlanner.FormatHexColor(resolvedColor));
        }

        return BuildColorChoices(colors);
    }

    public static IReadOnlyList<SortColorChoice> BuildColorChoicesForSortOn(
        string? sortOn,
        IReadOnlyList<SortColorChoice> cellColorChoices,
        IReadOnlyList<SortColorChoice> fontColorChoices,
        SortDialogPlannerText? text = null)
    {
        return SortOnFromLabel(sortOn, text) switch
        {
            SortOn.CellColor => cellColorChoices,
            SortOn.FontColor => fontColorChoices,
            _ => [new SortColorChoice("")]
        };
    }

    public static GridRange ExcludeHeaderRow(GridRange range, bool hasHeaders)
    {
        if (!hasHeaders || range.Start.Row >= range.End.Row)
            return range;

        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row + 1, range.Start.Col),
            range.End);
    }

    public static SortOn SortOnFromLabel(string? label, SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        return label switch
        {
            var value when string.Equals(value, resolvedText.SortOnCellColor, StringComparison.Ordinal) ||
                string.Equals(value, SortDialogPlannerText.Default.SortOnCellColor, StringComparison.Ordinal) => SortOn.CellColor,
            var value when string.Equals(value, resolvedText.SortOnFontColor, StringComparison.Ordinal) ||
                string.Equals(value, SortDialogPlannerText.Default.SortOnFontColor, StringComparison.Ordinal) => SortOn.FontColor,
            _ => SortOn.CellValues
        };
    }

    public static IReadOnlyList<SortDialogLevel> NormalizeLevels(
        IEnumerable<SortDialogLevel>? levels,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        if (levels is IReadOnlyList<SortDialogLevel> { Count: > 0 } existingLevels)
        {
            ApplyText(existingLevels, resolvedText);
            return existingLevels;
        }

        var normalized = levels?.ToList() ?? [];
        if (normalized.Count == 0)
            return [new SortDialogLevel(0, true, resolvedText)];

        ApplyText(normalized, resolvedText);
        return normalized;
    }

    public static IReadOnlyList<SortColumnChoice> NormalizeColumnChoices(
        IEnumerable<SortColumnChoice>? choices,
        SortDialogPlannerText? text = null)
    {
        var resolvedText = ResolveText(text);
        if (choices is IReadOnlyList<SortColumnChoice> { Count: > 0 } existingChoices)
            return existingChoices;

        var normalized = choices?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortColumnChoice(resolvedText.FormatColumnLabel("A"), 0)] : normalized;
    }

    public static IReadOnlyList<SortColorChoice> NormalizeColorChoices(IEnumerable<SortColorChoice>? choices)
    {
        if (choices is IReadOnlyList<SortColorChoice> { Count: > 0 } existingChoices)
            return existingChoices;

        var normalized = choices?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortColorChoice("")] : normalized;
    }

    private static IReadOnlyList<SortDirectionChoice> BuildDirectionChoices(SortDialogPlannerText text) =>
        [
            new(text.OrderAToZ, true),
            new(text.OrderZToA, false)
        ];

    private static IReadOnlyList<SortDirectionChoice> BuildColorDirectionChoices(SortDialogPlannerText text) =>
        [
            new(text.OrderOnTop, true),
            new(text.OrderOnBottom, false)
        ];

    private static SortDialogLevel CloneLevel(SortDialogLevel level, SortDialogPlannerText? text = null)
    {
        var clone = new SortDialogLevel(level.ColumnOffset, level.Ascending, text ?? level.Text)
        {
            SortOn = level.SortOn
        };
        clone.SetColorChoices(level.ColorChoices);
        clone.TargetColor = level.TargetColor;
        return clone;
    }

    private static CellStyle GetCellStyle(Workbook workbook, Sheet sheet, CellAddress address)
    {
        var cell = sheet.GetCell(address);
        return workbook.GetStyle(cell?.StyleId ?? StyleId.Default);
    }

    private static IReadOnlyList<SortColorChoice> BuildColorChoices(SortedSet<string> colors)
    {
        var choices = new List<SortColorChoice>(colors.Count + 1)
        {
            new("")
        };
        foreach (var color in colors)
            choices.Add(new SortColorChoice(color));

        return choices;
    }

    private static string GetHeaderLabel(
        Sheet sheet,
        GridRange range,
        uint offset,
        string fallbackColumnName,
        SortDialogPlannerText text)
    {
        var address = new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col + offset);
        var headerText = sheet.GetCell(address)?.Value switch
        {
            TextValue value => value.Value.Trim(),
            NumberValue value => value.Value.ToString("G15", CultureInfo.CurrentCulture),
            DateTimeValue value => value.Value.ToString("d", CultureInfo.CurrentCulture),
            BoolValue value => value.Value ? "TRUE" : "FALSE",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(headerText) ? text.FormatColumnLabel(fallbackColumnName) : headerText;
    }

    private static CellColor? TargetColorFromText(string? text, SortOn sortOn)
    {
        if (sortOn is not SortOn.CellColor and not SortOn.FontColor)
            return null;

        return TryParseColorText(text ?? "", out var color) ? color : null;
    }

    private static bool TryParseColorText(string text, out CellColor color)
    {
        color = default;
        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];

        if (normalized.Length == 6 &&
            byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            byte.TryParse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            byte.TryParse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            color = new CellColor(r, g, b);
            return true;
        }

        var parts = text.Trim().Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 3 &&
            byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out r) &&
            byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out g) &&
            byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
        {
            color = new CellColor(r, g, b);
            return true;
        }

        return false;
    }

    private static SortDialogPlannerText ResolveText(SortDialogPlannerText? text) =>
        text ?? SortDialogPlannerText.Default;

    private static void ApplyText(IEnumerable<SortDialogLevel> levels, SortDialogPlannerText text)
    {
        foreach (var level in levels)
            level.SetPlannerText(text);
    }
}
