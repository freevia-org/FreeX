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
    int ColumnIndex,
    CellCommentDisplayKind Kind = CellCommentDisplayKind.Note);

public static class WorksheetPageLayout
{
    public static WorksheetPageSize GetPageSizeInches(
        WorksheetPaperSize paperSize,
        WorksheetPageOrientation orientation)
    {
        var (width, height) = paperSize switch
        {
            WorksheetPaperSize.Letter    => (8.5,  11.0),
            WorksheetPaperSize.Legal     => (8.5,  14.0),
            WorksheetPaperSize.Tabloid   => (11.0, 17.0),
            WorksheetPaperSize.Ledger    => (17.0, 11.0),
            WorksheetPaperSize.Statement => (5.5,  8.5),
            WorksheetPaperSize.Executive => (7.25, 10.5),
            WorksheetPaperSize.A3        => (11.69, 16.54),
            WorksheetPaperSize.A5        => (5.83,  8.27),
            WorksheetPaperSize.B4        => (9.84,  13.90),
            WorksheetPaperSize.B5        => (6.93,  9.84),
            WorksheetPaperSize.Folio     => (8.5,  13.0),
            _                            => (8.27, 11.69)   // A4 fallback
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
    public const int DefaultCode = 9;

    // ECMA-376 §18.18.43 — selected codes
    private static readonly IReadOnlyDictionary<int, WorksheetPaperSize> CodeToEnum =
        new Dictionary<int, WorksheetPaperSize>
        {
            { 1,  WorksheetPaperSize.Letter    },
            { 3,  WorksheetPaperSize.Tabloid   },
            { 4,  WorksheetPaperSize.Ledger    },
            { 5,  WorksheetPaperSize.Legal     },
            { 6,  WorksheetPaperSize.Statement },
            { 7,  WorksheetPaperSize.Executive },
            { 8,  WorksheetPaperSize.A3        },
            { 9,  WorksheetPaperSize.A4        },
            { 11, WorksheetPaperSize.A5        },
            { 12, WorksheetPaperSize.B4        },
            { 13, WorksheetPaperSize.B5        },
            { 14, WorksheetPaperSize.Folio     },
        };

    private static readonly IReadOnlyDictionary<WorksheetPaperSize, int> EnumToCode =
        new Dictionary<WorksheetPaperSize, int>
        {
            { WorksheetPaperSize.Letter,    1  },
            { WorksheetPaperSize.Tabloid,   3  },
            { WorksheetPaperSize.Ledger,    4  },
            { WorksheetPaperSize.Legal,     5  },
            { WorksheetPaperSize.Statement, 6  },
            { WorksheetPaperSize.Executive, 7  },
            { WorksheetPaperSize.A3,        8  },
            { WorksheetPaperSize.A4,        9  },
            { WorksheetPaperSize.A5,        11 },
            { WorksheetPaperSize.B4,        12 },
            { WorksheetPaperSize.B5,        13 },
            { WorksheetPaperSize.Folio,     14 },
        };

    /// <summary>
    /// Tries to resolve an OOXML paper-size code to its <see cref="WorksheetPaperSize"/> enum value.
    /// Returns <see langword="false"/> for unknown codes; the caller should preserve the raw code and
    /// leave <see cref="Sheet.PaperSize"/> at its default.
    /// </summary>
    public static bool TryGetEnum(int code, out WorksheetPaperSize size) =>
        CodeToEnum.TryGetValue(code, out size);

    /// <summary>
    /// Returns the OOXML paper-size code for a <see cref="WorksheetPaperSize"/> enum value.
    /// Returns <see cref="DefaultCode"/> for any value not in the map.
    /// </summary>
    public static int GetCode(WorksheetPaperSize size) =>
        EnumToCode.TryGetValue(size, out var code) ? code : DefaultCode;
}
