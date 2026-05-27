using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed partial class SortDialog
{
    public static IReadOnlyList<SortKey> BuildSortKeys(IEnumerable<SortDialogLevel> levels) =>
        SortDialogPlanner.BuildSortKeys(levels);

    public static IReadOnlyList<SortDirectionChoice> BuildOrderChoices(string? sortOn) =>
        SortDialogPlanner.BuildOrderChoices(sortOn);

    public static IReadOnlyList<SortDialogLevel> AddLevel(
        IEnumerable<SortDialogLevel> levels,
        uint columnOffset = 0,
        bool ascending = true) =>
        SortDialogPlanner.AddLevel(levels, columnOffset, ascending);

    public static IReadOnlyList<SortDialogLevel> RemoveLevel(IEnumerable<SortDialogLevel> levels, int index) =>
        SortDialogPlanner.RemoveLevel(levels, index);

    public static IReadOnlyList<SortDialogLevel> CopyLevel(IEnumerable<SortDialogLevel> levels, int index) =>
        SortDialogPlanner.CopyLevel(levels, index);

    public static IReadOnlyList<SortDialogLevel> MoveLevel(IEnumerable<SortDialogLevel> levels, int index, int direction) =>
        SortDialogPlanner.MoveLevel(levels, index, direction);

    public static IReadOnlyList<SortDialogLevel> UpdateLevel(
        IEnumerable<SortDialogLevel> levels,
        int index,
        uint columnOffset,
        bool ascending) =>
        SortDialogPlanner.UpdateLevel(levels, index, columnOffset, ascending);

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(GridRange range) =>
        SortDialogPlanner.BuildColumnChoices(range);

    public static IReadOnlyList<SortColumnChoice> BuildColumnChoices(Sheet? sheet, GridRange range, bool hasHeaders) =>
        SortDialogPlanner.BuildColumnChoices(sheet, range, hasHeaders);

    public static IReadOnlyList<SortColumnChoice> BuildRowChoices(GridRange range) =>
        SortDialogPlanner.BuildRowChoices(range);

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(Workbook workbook, Sheet? sheet, GridRange range) =>
        SortDialogPlanner.BuildColorChoices(workbook, sheet, range);

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(
        Workbook workbook,
        Sheet? sheet,
        GridRange range,
        Freexcel.Core.Commands.SortOn sortOn) =>
        SortDialogPlanner.BuildColorChoices(workbook, sheet, range, sortOn);

    public static GridRange ExcludeHeaderRow(GridRange range, bool hasHeaders) =>
        SortDialogPlanner.ExcludeHeaderRow(range, hasHeaders);

    private static IReadOnlyList<SortDialogLevel> NormalizeLevels(IEnumerable<SortDialogLevel>? levels) =>
        SortDialogPlanner.NormalizeLevels(levels);

    private static IReadOnlyList<SortColumnChoice> NormalizeColumnChoices(IEnumerable<SortColumnChoice>? choices) =>
        SortDialogPlanner.NormalizeColumnChoices(choices);

    private static IReadOnlyList<SortColorChoice> NormalizeColorChoices(IEnumerable<SortColorChoice>? choices) =>
        SortDialogPlanner.NormalizeColorChoices(choices);
}

internal static class SortDialogPlanner
{
    private static readonly IReadOnlyList<SortDirectionChoice> DirectionChoices =
    [
        new("A to Z", true),
        new("Z to A", false)
    ];

    private static readonly IReadOnlyList<SortDirectionChoice> ColorDirectionChoices =
    [
        new("On Top", true),
        new("On Bottom", false)
    ];

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
            updated.Insert(index + 1, CloneLevel(updated[index]));

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
        {
            var existing = updated[index];
            updated[index] = new SortDialogLevel(columnOffset, ascending)
            {
                SortOn = existing.SortOn,
                TargetColor = existing.TargetColor
            };
        }

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
                colors.Add(ColorInputParser.FormatHexColor(fillColor));
            if (style.FontColor is { } fontColor)
                colors.Add(ColorInputParser.FormatHexColor(fontColor));
        }

        return BuildColorChoices(colors);
    }

    public static IReadOnlyList<SortColorChoice> BuildColorChoices(
        Workbook workbook,
        Sheet? sheet,
        GridRange range,
        Freexcel.Core.Commands.SortOn sortOn)
    {
        if (sheet is null)
            return [new SortColorChoice("")];

        var colors = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in range.AllCells())
        {
            var style = GetCellStyle(workbook, sheet, address);
            var color = sortOn == Freexcel.Core.Commands.SortOn.FontColor
                ? style.FontColor
                : style.FillColor;
            if (color is { } resolvedColor)
                colors.Add(ColorInputParser.FormatHexColor(resolvedColor));
        }

        return BuildColorChoices(colors);
    }

    public static IReadOnlyList<SortColorChoice> BuildColorChoicesForSortOn(
        string? sortOn,
        IReadOnlyList<SortColorChoice> cellColorChoices,
        IReadOnlyList<SortColorChoice> fontColorChoices)
    {
        return SortOnFromLabel(sortOn) switch
        {
            Freexcel.Core.Commands.SortOn.CellColor => cellColorChoices,
            Freexcel.Core.Commands.SortOn.FontColor => fontColorChoices,
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

    public static Freexcel.Core.Commands.SortOn SortOnFromLabel(string? label) =>
        label switch
        {
            "Cell Color" => Freexcel.Core.Commands.SortOn.CellColor,
            "Font Color" => Freexcel.Core.Commands.SortOn.FontColor,
            _ => Freexcel.Core.Commands.SortOn.CellValues
        };

    public static IReadOnlyList<SortDialogLevel> NormalizeLevels(IEnumerable<SortDialogLevel>? levels)
    {
        var normalized = levels?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortDialogLevel(0, true)] : normalized;
    }

    public static IReadOnlyList<SortColumnChoice> NormalizeColumnChoices(IEnumerable<SortColumnChoice>? choices)
    {
        var normalized = choices?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortColumnChoice("Column A", 0)] : normalized;
    }

    public static IReadOnlyList<SortColorChoice> NormalizeColorChoices(IEnumerable<SortColorChoice>? choices)
    {
        var normalized = choices?.ToList() ?? [];
        return normalized.Count == 0 ? [new SortColorChoice("")] : normalized;
    }

    private static SortDialogLevel CloneLevel(SortDialogLevel level) =>
        new(level.ColumnOffset, level.Ascending)
        {
            SortOn = level.SortOn,
            TargetColor = level.TargetColor
        };

    private static CellStyle GetCellStyle(Workbook workbook, Sheet sheet, CellAddress address)
    {
        var cell = sheet.GetCell(address);
        return workbook.GetStyle(cell?.StyleId ?? StyleId.Default);
    }

    private static IReadOnlyList<SortColorChoice> BuildColorChoices(SortedSet<string> colors) =>
        [new SortColorChoice(""), .. colors.Select(color => new SortColorChoice(color))];

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

    private static CellColor? TargetColorFromText(string? text, Freexcel.Core.Commands.SortOn sortOn)
    {
        if (sortOn is not Freexcel.Core.Commands.SortOn.CellColor and not Freexcel.Core.Commands.SortOn.FontColor)
            return null;

        return ColorInputParser.TryParseColorText(text ?? "", out var color) ? color : null;
    }
}
