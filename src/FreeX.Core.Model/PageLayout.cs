using Free.Shared.PageSetup;

namespace FreeX.Core.Model;

public enum WorksheetPageOrientation
{
    Portrait,
    Landscape
}

public enum WorksheetPaperSize
{
    Letter,
    A4,
    Legal,
    // Extended sizes — OOXML codes added for dialog and round-trip fidelity
    Tabloid,
    Ledger,
    Statement,
    Executive,
    A3,
    A5,
    B4,
    B5,
    Folio,
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
    public static WorksheetPageMargins Normal { get; } = new(0.7, 0.7, 0.75, 0.75);
    public static WorksheetPageMargins Wide { get; } = new(1.25, 1.25, 1.0, 1.0);
    public static WorksheetPageMargins Narrow { get; } = new(0.25, 0.25, 0.75, 0.75);
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
    int ColumnIndex,
    CellCommentDisplayKind Kind = CellCommentDisplayKind.Note);

public static class WorksheetPageLayout
{
    /// <summary>
    /// Physical page size in inches, landscape swap applied. The dimensions themselves come from the
    /// cross-app <see cref="PaperSizeCatalog"/> (authored in millimetres, projected to inches at the
    /// two-decimal precision this table has always used), so FreeX no longer carries its own table.
    /// </summary>
    public static WorksheetPageSize GetPageSizeInches(
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation)
    {
        var (width, height) = PaperSizeCatalog.GetSizeInches(
            WorksheetPaperSizes.ToShared(paperSize),
            orientation == WorksheetPageOrientation.Landscape
                ? SharedPageOrientation.Landscape
                : SharedPageOrientation.Portrait);

        return new WorksheetPageSize(width, height);
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
        return GetDisplayedCommentOverlaysCore(comments, threadedComments, pageRows, pageColumns, shownComments: null);
    }

    /// <summary>
    /// Same as the overload without <paramref name="shownComments"/>, except only notes/threaded
    /// comments whose address is "pinned" (present in <paramref name="shownComments"/> — i.e.
    /// <see cref="Sheet.ShownComments"/>) are emitted. Excel's Comments &amp; Notes "Indicators only"
    /// display state means the "As displayed on sheet" print/PDF mode must draw a box only for the
    /// notes the user actually pinned open, not every note/threaded comment on the sheet.
    /// </summary>
    public static IReadOnlyList<WorksheetDisplayedComment> GetDisplayedCommentOverlays(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlySet<CellAddress> shownComments)
    {
        return GetDisplayedCommentOverlaysCore(comments, EmptyThreadedComments, pageRows, pageColumns, shownComments);
    }

    /// <inheritdoc cref="GetDisplayedCommentOverlays(IReadOnlyDictionary{CellAddress,string},IReadOnlyList{uint},IReadOnlyList{uint},IReadOnlySet{CellAddress})"/>
    public static IReadOnlyList<WorksheetDisplayedComment> GetDisplayedCommentOverlays(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlySet<CellAddress> shownComments)
    {
        return GetDisplayedCommentOverlaysCore(comments, threadedComments, pageRows, pageColumns, shownComments);
    }

    private static IReadOnlyList<WorksheetDisplayedComment> GetDisplayedCommentOverlaysCore(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlySet<CellAddress>? shownComments)
    {
        var rowIndexes = pageRows
            .Select((row, index) => (row, index))
            .ToDictionary(item => item.row, item => item.index);
        var columnIndexes = pageColumns
            .Select((column, index) => (column, index))
            .ToDictionary(item => item.column, item => item.index);

        // Excel always writes a legacy <comment> compatibility placeholder alongside a modern
        // threaded comment at the same cell (for older-client fallback), so both dictionaries can
        // have an entry for the same address. The threaded comment is the real, currently-authored
        // content (author, replies, resolution) and must win; the legacy dictionary only fills in
        // addresses that have a plain (non-threaded) note and no threaded comment at all.
        //
        // The display kind (Note / ThreadedComment / Mixed) mirrors the same address-presence
        // logic the live on-screen indicator uses (see ViewportService.CreateCellCommentDisplay),
        // so a print/PDF renderer can recover which color the on-screen triangle used for that
        // address instead of the merge silently collapsing everything to plain text.
        var mergedComments = threadedComments
            .Select(pair => (
                Key: pair.Key,
                Text: pair.Value.Text,
                Kind: comments.ContainsKey(pair.Key)
                    ? CellCommentDisplayKind.Mixed
                    : CellCommentDisplayKind.ThreadedComment))
            .Concat(comments
                .Where(pair => !threadedComments.ContainsKey(pair.Key))
                .Select(pair => (Key: pair.Key, Text: pair.Value, Kind: CellCommentDisplayKind.Note)));

        return mergedComments
            .Where(item => rowIndexes.ContainsKey(item.Key.Row) && columnIndexes.ContainsKey(item.Key.Col))
            .Where(item => shownComments is null || shownComments.Contains(item.Key))
            .OrderBy(item => rowIndexes[item.Key.Row])
            .ThenBy(item => columnIndexes[item.Key.Col])
            .Select(item => new WorksheetDisplayedComment(
                item.Key,
                item.Text,
                rowIndexes[item.Key.Row],
                columnIndexes[item.Key.Col],
                item.Kind))
            .ToList();
    }

    private static readonly IReadOnlyDictionary<CellAddress, ThreadedComment> EmptyThreadedComments =
        new Dictionary<CellAddress, ThreadedComment>();

    private static double ClampFraction(double value) =>
        Math.Clamp(value, 0.0, 1.0);
}

/// <summary>
/// Maps OOXML <c>pageSetup/@paperSize</c> integer codes (ECMA-376 §18.18.43) to/from the
/// <see cref="WorksheetPaperSize"/> enum used by the dialog and print-preview engine.
/// Unknown codes are preserved as-is via <see cref="Sheet.PaperSizeCode"/> without touching
/// <see cref="Sheet.PaperSize"/>.
/// </summary>
public static class PaperSizeCodes
{
    /// <summary>Default OOXML paper-size code (9 = A4).</summary>
    public const int DefaultCode = PaperSizeCatalog.DefaultOoxmlCode;

    /// <summary>
    /// Tries to resolve an OOXML paper-size code to its <see cref="WorksheetPaperSize"/> enum value.
    /// Returns <see langword="false"/> for unknown codes; the caller should preserve the raw code and
    /// leave <see cref="Sheet.PaperSize"/> at its default.
    /// </summary>
    public static bool TryGetEnum(int code, out WorksheetPaperSize size)
    {
        if (PaperSizeCatalog.TryGetSizeFromOoxmlCode(code, out var shared))
        {
            size = WorksheetPaperSizes.FromShared(shared);
            return true;
        }

        size = WorksheetPaperSize.A4;
        return false;
    }

    /// <summary>
    /// Returns the OOXML paper-size code for a <see cref="WorksheetPaperSize"/> enum value.
    /// Returns <see cref="DefaultCode"/> for any value not in the map.
    /// </summary>
    public static int GetCode(WorksheetPaperSize size) =>
        PaperSizeCatalog.GetOoxmlCode(WorksheetPaperSizes.ToShared(size));
}

/// <summary>
/// Bridges FreeX's <see cref="WorksheetPaperSize"/> to the cross-app
/// <see cref="SharedPaperSize"/> catalog. FreeX keeps its own enum so the model's public shape (and
/// the XLSX IO layer built on it) is unchanged; only the dimensions and OOXML codes are shared.
/// </summary>
public static class WorksheetPaperSizes
{
    public static SharedPaperSize ToShared(WorksheetPaperSize size) => size switch
    {
        WorksheetPaperSize.Letter => SharedPaperSize.Letter,
        WorksheetPaperSize.Legal => SharedPaperSize.Legal,
        WorksheetPaperSize.Tabloid => SharedPaperSize.Tabloid,
        WorksheetPaperSize.Ledger => SharedPaperSize.Ledger,
        WorksheetPaperSize.Statement => SharedPaperSize.Statement,
        WorksheetPaperSize.Executive => SharedPaperSize.Executive,
        WorksheetPaperSize.A3 => SharedPaperSize.A3,
        WorksheetPaperSize.A5 => SharedPaperSize.A5,
        WorksheetPaperSize.B4 => SharedPaperSize.B4,
        WorksheetPaperSize.B5 => SharedPaperSize.B5,
        WorksheetPaperSize.Folio => SharedPaperSize.Folio,
        _ => SharedPaperSize.A4,   // A4 and any undefined value
    };

    public static WorksheetPaperSize FromShared(SharedPaperSize size) => size switch
    {
        SharedPaperSize.Letter => WorksheetPaperSize.Letter,
        SharedPaperSize.Legal => WorksheetPaperSize.Legal,
        SharedPaperSize.Tabloid => WorksheetPaperSize.Tabloid,
        SharedPaperSize.Ledger => WorksheetPaperSize.Ledger,
        SharedPaperSize.Statement => WorksheetPaperSize.Statement,
        SharedPaperSize.Executive => WorksheetPaperSize.Executive,
        SharedPaperSize.A3 => WorksheetPaperSize.A3,
        SharedPaperSize.A5 => WorksheetPaperSize.A5,
        SharedPaperSize.B4 => WorksheetPaperSize.B4,
        SharedPaperSize.B5 => WorksheetPaperSize.B5,
        SharedPaperSize.Folio => WorksheetPaperSize.Folio,
        _ => WorksheetPaperSize.A4,
    };
}
