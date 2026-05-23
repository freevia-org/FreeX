using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class SortDialog
{
    public static IReadOnlyList<SortKey> BuildSortKeys(IEnumerable<SortDialogLevel> levels)
    {
        return NormalizeLevels(levels)
            .Select(level =>
            {
                var sortOn = SortOnFromLabel(level.SortOn);
                return new SortKey(level.ColumnOffset, level.Ascending, sortOn, TargetColorFromText(level.TargetColor, sortOn));
            })
            .ToList();
    }

    public static IReadOnlyList<SortDirectionChoice> BuildOrderChoices(string? sortOn) =>
        SortOnFromLabel(sortOn) is Freexcel.Core.Commands.SortOn.CellColor or Freexcel.Core.Commands.SortOn.FontColor
            ? ColorDirectionChoices
            : DirectionChoices;

    public static IReadOnlyList<SortDialogLevel> AddLevel(
        IEnumerable<SortDialogLevel> levels,
        uint columnOffset = 0,
        bool ascending = true)
    {
        return NormalizeLevels(levels)
            .Append(new SortDialogLevel(columnOffset, ascending))
            .ToList();
    }

    public static IReadOnlyList<SortDialogLevel> RemoveLevel(IEnumerable<SortDialogLevel> levels, int index)
    {
        var updated = NormalizeLevels(levels).ToList();
        if (index >= 0 && index < updated.Count)
            updated.RemoveAt(index);

        return updated.Count == 0 ? [new SortDialogLevel(0, true)] : updated;
    }

    public static IReadOnlyList<SortDialogLevel> CopyLevel(IEnumerable<SortDialogLevel> levels, int index)
    {
        var updated = NormalizeLevels(levels).ToList();
        if (index >= 0 && index < updated.Count)
        {
            var level = updated[index];
            updated.Insert(index + 1, new SortDialogLevel(level.ColumnOffset, level.Ascending)
            {
                SortOn = level.SortOn,
                TargetColor = level.TargetColor
            });
        }

        return updated;
    }

    public static IReadOnlyList<SortDialogLevel> MoveLevel(IEnumerable<SortDialogLevel> levels, int index, int direction)
    {
        var updated = NormalizeLevels(levels).ToList();
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
        bool ascending)
    {
        var updated = NormalizeLevels(levels).ToList();
        if (index >= 0 && index < updated.Count)
            updated[index] = new SortDialogLevel(columnOffset, ascending)
            {
                SortOn = updated[index].SortOn,
                TargetColor = updated[index].TargetColor
            };

        return updated;
    }

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(GridRange range)
    {
        return BuildColumnChoices(null, range, hasHeaders: false);
    }

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(Sheet? sheet, GridRange range, bool hasHeaders)
    {
        var choices = new List<SortColumnChoice>();
        for (uint offset = 0; offset < range.ColCount; offset++)
        {
            var columnName = CellAddress.NumberToColumnName(range.Start.Col + offset);
            var label = hasHeaders && sheet is not null
                ? GetHeaderLabel(sheet, range, offset, columnName)
                : $"Column {columnName}";
            choices.Add(new SortColumnChoice(label, offset));
        }

        return choices.Count == 0 ? [new SortColumnChoice("Column A", 0)] : choices;
    }

    public static IReadOnlyList<SortColumnChoice> BuildRowChoices(GridRange range)
    {
        var choices = new List<SortColumnChoice>();
        for (uint offset = 0; offset < range.RowCount; offset++)
            choices.Add(new SortColumnChoice($"Row {range.Start.Row + offset}", offset));

        return choices.Count == 0 ? [new SortColumnChoice("Row 1", 0)] : choices;
    }

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(Workbook workbook, Sheet? sheet, GridRange range)
    {
        if (sheet is null)
            return [new SortColorChoice("")];

        var colors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in range.AllCells())
        {
            var cell = sheet.GetCell(address);
            var style = workbook.GetStyle(cell?.StyleId ?? StyleId.Default);
            if (style.FillColor is { } fillColor)
                colors.Add(ColorInputParser.FormatHexColor(fillColor));
            if (style.FontColor is { } fontColor)
                colors.Add(ColorInputParser.FormatHexColor(fontColor));
        }

        return [new SortColorChoice(""), .. colors.Select(color => new SortColorChoice(color))];
    }

    public static GridRange ExcludeHeaderRow(GridRange range, bool hasHeaders)
    {
        if (!hasHeaders || range.Start.Row >= range.End.Row)
            return range;

        return new GridRange(
            new CellAddress(range.Start.Sheet, range.Start.Row + 1, range.Start.Col),
            range.End);
    }

    private static IReadOnlyList<SortDialogLevel> NormalizeLevels(IEnumerable<SortDialogLevel>? levels)
    {
        var normalized = levels?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortDialogLevel(0, true)] : normalized;
    }

    private static IReadOnlyList<SortColumnChoice> NormalizeColumnChoices(IEnumerable<SortColumnChoice>? choices)
    {
        var normalized = choices?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortColumnChoice("Column A", 0)] : normalized;
    }

    private static IReadOnlyList<SortColorChoice> NormalizeColorChoices(IEnumerable<SortColorChoice>? choices)
    {
        var normalized = choices?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortColorChoice("")] : normalized;
    }

    private static string GetHeaderLabel(Sheet sheet, GridRange range, uint offset, string fallbackColumnName)
    {
        var address = new CellAddress(range.Start.Sheet, range.Start.Row, range.Start.Col + offset);
        var text = sheet.GetCell(address)?.Value switch
        {
            TextValue value => value.Value.Trim(),
            NumberValue value => value.Value.ToString("G15", System.Globalization.CultureInfo.CurrentCulture),
            DateTimeValue value => value.Value.ToString("d", System.Globalization.CultureInfo.CurrentCulture),
            BoolValue value => value.Value ? "TRUE" : "FALSE",
            _ => string.Empty
        };

        return string.IsNullOrWhiteSpace(text) ? $"Column {fallbackColumnName}" : text;
    }

    private static Freexcel.Core.Commands.SortOn SortOnFromLabel(string? label) =>
        label switch
        {
            "Cell Color" => Freexcel.Core.Commands.SortOn.CellColor,
            "Font Color" => Freexcel.Core.Commands.SortOn.FontColor,
            _ => Freexcel.Core.Commands.SortOn.CellValues
        };

    private static CellColor? TargetColorFromText(string? text, Freexcel.Core.Commands.SortOn sortOn)
    {
        if (sortOn is not Freexcel.Core.Commands.SortOn.CellColor and not Freexcel.Core.Commands.SortOn.FontColor)
            return null;

        return ColorInputParser.TryParseColorText(text ?? "", out var color) ? color : null;
    }
}
