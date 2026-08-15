namespace FreeX.App.Presentation.Editing;

public readonly record struct ClipboardRangePictureColor(byte Red, byte Green, byte Blue);

/// <summary>
/// Renderer-neutral plan for the picture flavor placed on the clipboard with a copied cell range.
/// Native renderers only draw this fixed grid/text projection and encode it as PNG.
/// </summary>
public sealed record ClipboardRangePicturePlan
{
    internal ClipboardRangePicturePlan(
        IReadOnlyList<IReadOnlyList<string>> rows,
        int columnCount)
    {
        Rows = rows;
        RowCount = rows.Count;
        ColumnCount = columnCount;
        PixelWidth = columnCount * ClipboardRangePicturePlanner.CellWidth;
        PixelHeight = rows.Count * ClipboardRangePicturePlanner.CellHeight;
    }

    public IReadOnlyList<IReadOnlyList<string>> Rows { get; }

    public int RowCount { get; }

    public int ColumnCount { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public string TextAt(int row, int column) =>
        column < Rows[row].Count ? Rows[row][column] : string.Empty;
}

public static class ClipboardRangePicturePlanner
{
    public const int CellWidth = 80;
    public const int CellHeight = 20;
    public const int MaximumCellCount = 2000;
    public const int MaximumPixelDimension = 32767;
    public const double FontSize = 12;
    public const int TextPaddingHorizontal = 2;
    public const int TextPaddingVertical = 1;

    public static ClipboardRangePictureColor BackgroundColor { get; } = new(255, 255, 255);

    public static ClipboardRangePictureColor GridlineColor { get; } = new(211, 211, 211);

    public static ClipboardRangePictureColor TextColor { get; } = new(0, 0, 0);

    public static ClipboardRangePicturePlan? TryBuild(string[][]? rows)
    {
        if (rows is null || rows.Length == 0)
            return null;

        var normalized = rows
            .Select(static row => (IReadOnlyList<string>)(row ?? []).Select(static text => text ?? string.Empty).ToArray())
            .ToArray();
        var columnCount = normalized.Max(static row => row.Count);
        if (columnCount == 0 || (long)normalized.Length * columnCount > MaximumCellCount)
            return null;

        var pixelWidth = (long)columnCount * CellWidth;
        var pixelHeight = (long)normalized.Length * CellHeight;
        if (pixelWidth > MaximumPixelDimension || pixelHeight > MaximumPixelDimension)
            return null;

        return new ClipboardRangePicturePlan(normalized, columnCount);
    }
}
