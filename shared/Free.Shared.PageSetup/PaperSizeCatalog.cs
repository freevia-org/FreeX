namespace Free.Shared.PageSetup;

/// <summary>
/// One canonical paper size: its portrait physical dimensions and its OOXML
/// <c>pageSetup/@paperSize</c> code (ECMA-376 §18.18.43).
/// </summary>
/// <remarks>
/// Dimensions are authored in millimetres because that is the only unit in which every entry is
/// exact: the ISO/JIS sizes are defined in millimetres, and the ANSI sizes (defined in whole or
/// quarter inches) convert to millimetres exactly at 25.4 mm/in. Per-app rounded projections are
/// derived (see <see cref="PaperSizeCatalog.GetSizeInches"/> and
/// <see cref="PaperSizeCatalog.GetSizePoints"/>) instead of being re-listed per app.
/// </remarks>
public sealed record PaperSizeEntry(
    SharedPaperSize Size,
    string CanonicalName,
    double WidthMm,
    double HeightMm,
    int OoxmlCode);

/// <summary>
/// The single paper-size table for the whole repo: which named sizes exist, their physical
/// dimensions, and their OOXML paper-size codes. Display labels stay app-side (FreeX resolves
/// localized resx keys; FreeW carries per-host label text), because they are presentation, not data.
/// </summary>
public static class PaperSizeCatalog
{
    /// <summary>Rounding used for the inch projection (FreeX's historical table precision).</summary>
    public const int InchDigits = 2;

    /// <summary>Rounding used for the point projection (FreeW's historical table precision).</summary>
    public const int PointDigits = 1;

    /// <summary>Default OOXML paper-size code (9 = A4).</summary>
    public const int DefaultOoxmlCode = 9;

    /// <summary>The size used when an OOXML code is unknown or a lookup misses.</summary>
    public const SharedPaperSize DefaultSize = SharedPaperSize.A4;

    // As-authored dimensions: portrait for every entry except Ledger, which is Tabloid's
    // landscape-oriented sibling and is authored wide-first exactly as Excel and Word treat it.
    // ANSI sizes are their exact inch definitions expressed in millimetres
    // (8.5 in = 215.9 mm, and so on); A-series are ISO 216; B4/B5 are the JIS B sizes Excel and Word
    // use for paperSize codes 12/13.
    public static IReadOnlyList<PaperSizeEntry> Entries { get; } =
    [
        new(SharedPaperSize.Letter,    "Letter",    215.9,  279.4,  1),
        new(SharedPaperSize.Tabloid,   "Tabloid",   279.4,  431.8,  3),
        new(SharedPaperSize.Ledger,    "Ledger",    431.8,  279.4,  4),
        new(SharedPaperSize.Legal,     "Legal",     215.9,  355.6,  5),
        new(SharedPaperSize.Statement, "Statement", 139.7,  215.9,  6),
        new(SharedPaperSize.Executive, "Executive", 184.15, 266.7,  7),
        new(SharedPaperSize.A3,        "A3",        297.0,  420.0,  8),
        new(SharedPaperSize.A4,        "A4",        210.0,  297.0,  9),
        new(SharedPaperSize.A5,        "A5",        148.0,  210.0,  11),
        new(SharedPaperSize.B4,        "B4",        250.0,  353.0,  12),
        new(SharedPaperSize.B5,        "B5",        176.0,  250.0,  13),
        new(SharedPaperSize.Folio,     "Folio",     215.9,  330.2,  14),
    ];

    private static readonly IReadOnlyDictionary<SharedPaperSize, PaperSizeEntry> BySize =
        Entries.ToDictionary(entry => entry.Size);

    private static readonly IReadOnlyDictionary<int, SharedPaperSize> ByCode =
        Entries.ToDictionary(entry => entry.OoxmlCode, entry => entry.Size);

    public static bool TryGetEntry(SharedPaperSize size, out PaperSizeEntry entry) =>
        BySize.TryGetValue(size, out entry!);

    /// <summary>The catalog entry for <paramref name="size"/>, falling back to A4 for undefined values.</summary>
    public static PaperSizeEntry GetEntry(SharedPaperSize size) =>
        BySize.TryGetValue(size, out var entry) ? entry : BySize[DefaultSize];

    /// <summary>Resolves an OOXML paper-size code; returns false (leaving the default) for unknown codes.</summary>
    public static bool TryGetSizeFromOoxmlCode(int code, out SharedPaperSize size) =>
        ByCode.TryGetValue(code, out size);

    /// <summary>The OOXML paper-size code for <paramref name="size"/>, or <see cref="DefaultOoxmlCode"/>.</summary>
    public static int GetOoxmlCode(SharedPaperSize size) =>
        BySize.TryGetValue(size, out var entry) ? entry.OoxmlCode : DefaultOoxmlCode;

    /// <summary>Resolves a canonical name (case-insensitive) back to its size.</summary>
    public static bool TryGetSizeFromName(string? name, out SharedPaperSize size)
    {
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.CanonicalName, name, StringComparison.OrdinalIgnoreCase))
            {
                size = entry.Size;
                return true;
            }
        }

        size = DefaultSize;
        return false;
    }

    /// <summary>Portrait dimensions in inches, rounded to <see cref="InchDigits"/> decimals.</summary>
    public static (double Width, double Height) GetSizeInches(SharedPaperSize size) =>
        GetSize(size, PageMeasureUnit.Inch, InchDigits);

    /// <summary>Portrait dimensions in points, rounded to <see cref="PointDigits"/> decimals.</summary>
    public static (double Width, double Height) GetSizePoints(SharedPaperSize size) =>
        GetSize(size, PageMeasureUnit.Point, PointDigits);

    /// <summary>Portrait dimensions in millimetres (exact, no rounding).</summary>
    public static (double Width, double Height) GetSizeMillimetres(SharedPaperSize size)
    {
        var entry = GetEntry(size);
        return (entry.WidthMm, entry.HeightMm);
    }

    /// <summary>Portrait dimensions in <paramref name="unit"/>, rounded to <paramref name="digits"/> decimals.</summary>
    public static (double Width, double Height) GetSize(SharedPaperSize size, PageMeasureUnit unit, int digits)
    {
        var entry = GetEntry(size);
        return (
            PageMeasure.ConvertRounded(entry.WidthMm, PageMeasureUnit.Millimetre, unit, digits),
            PageMeasure.ConvertRounded(entry.HeightMm, PageMeasureUnit.Millimetre, unit, digits));
    }

    /// <summary>
    /// Dimensions in inches with the orientation applied (landscape swaps width and height), which is
    /// the shape FreeX's page/pagination engine consumes.
    /// </summary>
    public static (double Width, double Height) GetSizeInches(SharedPaperSize size, SharedPageOrientation orientation) =>
        PageOrientationRules.ApplySwapWhenLandscape(GetSizeInches(size), orientation);

    /// <summary>Dimensions in points with the orientation applied.</summary>
    public static (double Width, double Height) GetSizePoints(SharedPaperSize size, SharedPageOrientation orientation) =>
        PageOrientationRules.ApplySwapWhenLandscape(GetSizePoints(size), orientation);
}
