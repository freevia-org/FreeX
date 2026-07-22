namespace FreeX.Core.Model;

/// <summary>
/// Vertical alignment of a rich-text run within its cell — maps to the OOXML
/// <c>&lt;vertAlign val="superscript|subscript"/&gt;</c> element inside <c>&lt;rPr&gt;</c>.
/// </summary>
public enum CellTextRunVertAlign
{
    /// <summary>Normal baseline position.</summary>
    None,
    /// <summary>Raised above the baseline (Excel: superscript).</summary>
    Superscript,
    /// <summary>Lowered below the baseline (Excel: subscript).</summary>
    Subscript,
}

/// <summary>
/// Discriminator for how a rich-text run color is expressed in OOXML.
/// Preserving the original kind is required so round-trips do not flatten
/// <c>&lt;color theme="…"/&gt;</c> or <c>&lt;color indexed="…"/&gt;</c> to a
/// hard-coded <c>&lt;color rgb="…"/&gt;</c>.
/// </summary>
public enum CellRunColorKind
{
    /// <summary>Color expressed as <c>&lt;color rgb="FFrrggbb"/&gt;</c>.</summary>
    Rgb,
    /// <summary>Color expressed as <c>&lt;color theme="N" tint="T"/&gt;</c> — follows the workbook theme.</summary>
    Theme,
    /// <summary>Color expressed as <c>&lt;color indexed="N"/&gt;</c> — legacy indexed palette.</summary>
    Indexed,
    /// <summary>Color expressed as <c>&lt;color auto="1"/&gt;</c> — automatic (system default).</summary>
    Auto,
}

/// <summary>
/// A color reference for a rich-text run that preserves the original OOXML color-reference kind
/// (theme index, indexed palette, explicit RGB, or auto) so the writer can emit the same form
/// it read.  Use the static factory methods to construct instances.
/// </summary>
public readonly record struct CellRunColor
{
    /// <summary>Which form this color takes in OOXML.</summary>
    public CellRunColorKind Kind { get; init; }

    /// <summary>For <see cref="CellRunColorKind.Rgb"/>: the RGB value.</summary>
    public CellColor Rgb { get; init; }

    /// <summary>For <see cref="CellRunColorKind.Theme"/>: the zero-based theme-color index (0–11).</summary>
    public int ThemeIndex { get; init; }

    /// <summary>For <see cref="CellRunColorKind.Theme"/>: luminance tint in [−1, 1]; 0 = no tint.</summary>
    public double? Tint { get; init; }

    /// <summary>For <see cref="CellRunColorKind.Indexed"/>: the zero-based OOXML indexed-color value.</summary>
    public int IndexedIndex { get; init; }

    // ── Factories ────────────────────────────────────────────────────────────

    /// <summary>Creates an RGB color.</summary>
    public static CellRunColor FromRgb(CellColor rgb) => new() { Kind = CellRunColorKind.Rgb, Rgb = rgb };

    /// <summary>Creates a theme-color reference with optional tint.</summary>
    public static CellRunColor FromTheme(int themeIndex, double tint = 0) =>
        new() { Kind = CellRunColorKind.Theme, ThemeIndex = themeIndex, Tint = Math.Abs(tint) < 0.000001 ? null : tint };

    /// <summary>Creates a legacy indexed-color reference.</summary>
    public static CellRunColor FromIndexed(int indexedIndex) =>
        new() { Kind = CellRunColorKind.Indexed, IndexedIndex = indexedIndex };

    /// <summary>Creates an automatic color.</summary>
    public static CellRunColor Auto() => new() { Kind = CellRunColorKind.Auto };

    // OOXML reserves indexed=64 for "System Foreground" (black) and indexed=65 for
    // "System Background" (white); these lie outside the 56-entry standard palette
    // (indices 1-56) that WorkbookIndexedColorPalette resolves, so they must be
    // special-cased rather than forwarded to TryResolveColor. Mirrors
    // XlsxColorReader.SystemForegroundIndexedValue / SystemBackgroundIndexedValue.
    private const int SystemForegroundIndexedValue = 64;
    private const int SystemBackgroundIndexedValue = 65;

    /// <summary>
    /// Resolves this color to a concrete RGB value using the workbook theme and indexed-color palette.
    /// </summary>
    public CellColor Resolve(WorkbookTheme theme, WorkbookIndexedColorPalette indexedColors)
    {
        return Kind switch
        {
            CellRunColorKind.Rgb => Rgb,
            CellRunColorKind.Theme =>
                theme.ResolveColor(
                    MapThemeSlot(ThemeIndex),
                    Tint ?? 0),
            CellRunColorKind.Indexed => ResolveIndexed(indexedColors),
            _ => default,
        };
    }

    private CellColor ResolveIndexed(WorkbookIndexedColorPalette indexedColors)
    {
        if (IndexedIndex == SystemForegroundIndexedValue)
            return CellColor.Black;

        if (IndexedIndex == SystemBackgroundIndexedValue)
            return CellColor.White;

        // OOXML indexed colors are zero-based; WorkbookIndexedColorPalette stores Excel
        // ColorIndex values one-based starting at palette entry 8 (indexed=8 -> ColorIndex 1),
        // so the OOXML value maps to ColorIndex via index-7 (see XlsxColorReader.TryReadIndexedColor).
        return indexedColors.TryResolveColor(IndexedIndex - 7, out var c) ? c : default;
    }

    private static WorkbookThemeColorSlot MapThemeSlot(int index) => index switch
    {
        0  => WorkbookThemeColorSlot.Light1,
        1  => WorkbookThemeColorSlot.Dark1,
        2  => WorkbookThemeColorSlot.Light2,
        3  => WorkbookThemeColorSlot.Dark2,
        4  => WorkbookThemeColorSlot.Accent1,
        5  => WorkbookThemeColorSlot.Accent2,
        6  => WorkbookThemeColorSlot.Accent3,
        7  => WorkbookThemeColorSlot.Accent4,
        8  => WorkbookThemeColorSlot.Accent5,
        9  => WorkbookThemeColorSlot.Accent6,
        10 => WorkbookThemeColorSlot.Hyperlink,
        11 => WorkbookThemeColorSlot.FollowedHyperlink,
        _  => WorkbookThemeColorSlot.Dark1,
    };
}

/// <summary>
/// A single formatted run of text inside a rich-text cell.
/// All formatting properties are nullable; a null value means "inherit from the cell's
/// <see cref="CellStyle"/>".  Only deviating properties need to be set.
/// </summary>
/// <remarks>
/// Mirrors the OOXML <c>&lt;r&gt;&lt;rPr&gt;…&lt;/rPr&gt;&lt;t&gt;…&lt;/t&gt;&lt;/r&gt;</c>
/// structure inside an inline-string <c>&lt;is&gt;</c> or shared-string <c>&lt;si&gt;</c> element.
/// Modelled after <c>HeaderFooterFormattedRun</c> in PageContentRenderModel.cs.
/// </remarks>
public sealed record CellTextRun(
    string Text,
    bool? Bold,
    bool? Italic,
    bool? Underline,
    bool? Strikethrough,
    string? FontName,
    double? FontSize,
    CellRunColor? FontColor,
    CellTextRunVertAlign VertAlign = CellTextRunVertAlign.None,
    // When Underline is true, distinguishes a double/double-accounting underline
    // (<u val="double"/> or val="doubleAccounting") from a plain single underline.
    // Mirrors CellStyle.DoubleUnderline for whole-cell fonts. null/false means single;
    // ignored when Underline is not true.
    bool? DoubleUnderline = null,
    // Raw OOXML <charset val="…"/> value (e.g. 128 = ShiftJIS), if present.
    int? Charset = null,
    // Raw OOXML <family val="…"/> value (0-5), if present.
    int? Family = null,
    // Raw OOXML <scheme val="major|minor|none"/> value, if present.
    string? Scheme = null);
