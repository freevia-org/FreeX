namespace Freexcel.Core.Model;

public enum WorksheetPageOrientation
{
    Portrait,
    Landscape
}

public enum WorksheetPaperSize
{
    Letter,
    A4,
    Legal
}

public enum WorksheetPageOrder
{
    DownThenOver,
    OverThenDown
}

public enum WorksheetPrintErrorValue
{
    Displayed,
    Blank,
    Dash,
    NotAvailable
}

public enum WorksheetPrintComments
{
    None,
    AtEnd,
    AsDisplayed
}

public sealed record WorksheetBackgroundImage(byte[] ImageBytes, string ContentType, string? FileName = null);

public enum WorksheetPageMarginEdge
{
    Left,
    Right,
    Top,
    Bottom
}

public readonly record struct WorksheetPageMargins(
    double Left,
    double Right,
    double Top,
    double Bottom)
{
    public static WorksheetPageMargins Normal { get; } = new(1.0, 1.0, 1.0, 1.0);
    public static WorksheetPageMargins Wide { get; } = new(1.25, 1.25, 1.0, 1.0);
    public static WorksheetPageMargins Narrow { get; } = new(0.5, 0.5, 0.5, 0.5);
}

public readonly record struct WorksheetScaleToFit(
    int? ScalePercent,
    int? FitToPagesWide,
    int? FitToPagesTall)
{
    public static WorksheetScaleToFit Default { get; } = new(100, null, null);
}

public readonly record struct WorksheetRepeatRange(uint Start, uint End);

public readonly record struct WorksheetHeaderFooter(
    string Left,
    string Center,
    string Right);

public sealed record WorksheetHeaderFooterPicture(
    byte[] ImageBytes,
    string ContentType,
    string? FileName = null,
    double Width = 96,
    double Height = 48)
{
    public WorksheetHeaderFooterPicture DeepClone() =>
        this with { ImageBytes = ImageBytes.ToArray() };
}

public readonly record struct WorksheetHeaderFooterPictureSet(
    WorksheetHeaderFooterPicture? Left,
    WorksheetHeaderFooterPicture? Center,
    WorksheetHeaderFooterPicture? Right)
{
    public static WorksheetHeaderFooterPictureSet Empty { get; } = new(null, null, null);

    public WorksheetHeaderFooterPictureSet DeepClone() =>
        new(Left?.DeepClone(), Center?.DeepClone(), Right?.DeepClone());
}

public readonly record struct WorksheetPageSize(double Width, double Height);

public readonly record struct WorksheetMarginGuideFractions(
    double Left,
    double Right,
    double Top,
    double Bottom);

public readonly record struct WorksheetDisplayedComment(
    CellAddress Address,
    string Text,
    int RowIndex,
    int ColumnIndex);

public static class WorksheetPageLayout
{
    public static WorksheetPageSize GetPageSizeInches(
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation)
    {
        var (width, height) = paperSize switch
        {
            WorksheetPaperSize.Letter => (8.5, 11.0),
            WorksheetPaperSize.Legal => (8.5, 14.0),
            _ => (8.27, 11.69)
        };

        return orientation == WorksheetPageOrientation.Landscape
            ? new WorksheetPageSize(height, width)
            : new WorksheetPageSize(width, height);
    }

    public static WorksheetMarginGuideFractions GetMarginGuideFractions(
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins margins)
    {
        var size = GetPageSizeInches(paperSize, orientation);
        return new WorksheetMarginGuideFractions(
            ClampFraction(margins.Left / size.Width),
            ClampFraction(1.0 - margins.Right / size.Width),
            ClampFraction(margins.Top / size.Height),
            ClampFraction(1.0 - margins.Bottom / size.Height));
    }

    public static WorksheetPageMargins GetMarginsFromGuideFraction(
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation,
        WorksheetPageMargins currentMargins,
        WorksheetPageMarginEdge edge,
        double guideFraction)
    {
        var size = GetPageSizeInches(paperSize, orientation);
        var fraction = ClampFraction(guideFraction);
        return edge switch
        {
            WorksheetPageMarginEdge.Left => currentMargins with { Left = size.Width * fraction },
            WorksheetPageMarginEdge.Right => currentMargins with { Right = size.Width * (1.0 - fraction) },
            WorksheetPageMarginEdge.Top => currentMargins with { Top = size.Height * fraction },
            WorksheetPageMarginEdge.Bottom => currentMargins with { Bottom = size.Height * (1.0 - fraction) },
            _ => currentMargins
        };
    }

    public static IReadOnlyList<WorksheetDisplayedComment> GetDisplayedCommentOverlays(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns)
    {
        return GetDisplayedCommentOverlays(
            comments,
            EmptyThreadedComments,
            pageRows,
            pageColumns);
    }

    public static IReadOnlyList<WorksheetDisplayedComment> GetDisplayedCommentOverlays(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns)
    {
        var rowIndexes = pageRows
            .Select((row, index) => (row, index))
            .ToDictionary(item => item.row, item => item.index);
        var columnIndexes = pageColumns
            .Select((column, index) => (column, index))
            .ToDictionary(item => item.column, item => item.index);

        var mergedComments = comments
            .Concat(threadedComments
                .Where(pair => !comments.ContainsKey(pair.Key))
                .Select(pair => new KeyValuePair<CellAddress, string>(pair.Key, pair.Value.Text)));

        return mergedComments
            .Where(pair => rowIndexes.ContainsKey(pair.Key.Row) && columnIndexes.ContainsKey(pair.Key.Col))
            .OrderBy(pair => rowIndexes[pair.Key.Row])
            .ThenBy(pair => columnIndexes[pair.Key.Col])
            .Select(pair => new WorksheetDisplayedComment(
                pair.Key,
                pair.Value,
                rowIndexes[pair.Key.Row],
                columnIndexes[pair.Key.Col]))
            .ToList();
    }

    private static readonly IReadOnlyDictionary<CellAddress, ThreadedComment> EmptyThreadedComments =
        new Dictionary<CellAddress, ThreadedComment>();

    private static double ClampFraction(double value) =>
        Math.Clamp(value, 0.0, 1.0);
}
