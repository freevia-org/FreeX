namespace FreeX.Core.Model;

/// <summary>
/// The font scheme (theme slot) a cell font is tied to.
/// When set to Minor or Major the displayed font follows the workbook theme's body or heading font respectively.
/// </summary>
public enum CellFontScheme
{
    /// <summary>Font is pinned to a specific name and does not follow the theme.</summary>
    None,
    /// <summary>Font follows the workbook theme's minor (body) font.</summary>
    Minor,
    /// <summary>Font follows the workbook theme's major (heading) font.</summary>
    Major,
}

/// <summary>
/// An RGB color value used in cell styling.
/// </summary>
public readonly record struct CellColor(byte R, byte G, byte B)
{
    /// <summary>Solid black.</summary>
    public static readonly CellColor Black = new(0, 0, 0);

    /// <summary>Solid white.</summary>
    public static readonly CellColor White = new(255, 255, 255);

    /// <summary>Create a color from RGB components.</summary>
    public static CellColor FromArgb(byte r, byte g, byte b) => new(r, g, b);

    /// <summary>True when this color is black (R=0, G=0, B=0).</summary>
    public bool IsBlack => this == Black;
}

/// <summary>
/// The line style of a cell border edge.
/// </summary>
public enum BorderStyle
{
    None,
    Thin,
    Medium,
    Thick,
    Dashed,
    Dotted,
    Double,
    /// <summary>Sub-pixel thin line, lighter/thinner than Thin. Excel: "hair".</summary>
    Hair,
    /// <summary>Slanted dash-dot pattern. Excel: "slantDashDot".</summary>
    SlantDashDot,
    /// <summary>Medium dashed line. Excel: "mediumDashed".</summary>
    MediumDashed,
    /// <summary>Dash-dot line. Excel: "dashDot".</summary>
    DashDot,
    /// <summary>Medium dash-dot line. Excel: "mediumDashDot".</summary>
    MediumDashDot,
    /// <summary>Dash-dot-dot line. Excel: "dashDotDot".</summary>
    DashDotDot,
    /// <summary>Medium dash-dot-dot line. Excel: "mediumDashDotDot".</summary>
    MediumDashDotDot,
}

/// <summary>
/// A single border edge on a cell.
/// </summary>
public readonly record struct CellBorder(BorderStyle Style = BorderStyle.None, CellColor Color = default);

/// <summary>
/// Pattern styles available for a cell fill.
/// </summary>
public enum CellFillPatternStyle
{
    None,
    Solid,
    Gray0625,
    Gray125,
    LightGray,
    MediumGray,
    DarkGray,
    LightHorizontal,
    LightVertical,
    LightDown,
    LightUp,
    LightGrid,
    LightTrellis,
    DarkHorizontal,
    DarkVertical,
    DarkDown,
    DarkUp,
    DarkGrid,
    DarkTrellis
}

/// <summary>
/// Horizontal alignment within a cell.
/// </summary>
public enum HorizontalAlignment
{
    General,
    Left,
    Center,
    Right,
    Justify,
    Distributed,
    /// <summary>Repeats the cell text to fill the column width. Excel: "fill".</summary>
    Fill,
}

/// <summary>
/// Vertical alignment within a cell.
/// </summary>
public enum VerticalAlignment
{
    Top,
    Center,
    Bottom,
    Justify,
    Distributed
}

/// <summary>
/// Per-cell text reading direction override, as specified by OOXML alignment <c>readingOrder</c>.
/// </summary>
public enum CellReadingOrder
{
    /// <summary>Follows the current UI/locale direction (OOXML readingOrder="0", the default).</summary>
    Context,
    /// <summary>Forces left-to-right reading order (OOXML readingOrder="1").</summary>
    LeftToRight,
    /// <summary>Forces right-to-left reading order (OOXML readingOrder="2").</summary>
    RightToLeft,
}

/// <summary>
/// The type of a gradient cell fill.
/// </summary>
public enum CellGradientFillType
{
    /// <summary>Linear gradient along a given degree angle (Excel default).</summary>
    Linear,
    /// <summary>Radial gradient emanating from a rectangular origin point.</summary>
    Path,
}

/// <summary>
/// One color stop in a gradient fill.
/// </summary>
public readonly record struct CellGradientStop(double Position, CellColor Color);

/// <summary>
/// A gradient fill for a cell, as specified by OOXML <c>&lt;gradientFill&gt;</c>.
/// Supports both linear (degree-based) and path (inset-based) gradient types with
/// an arbitrary list of color stops.
/// </summary>
public sealed class CellGradientFill : IEquatable<CellGradientFill>
{
    /// <summary>Gradient type: linear or path.</summary>
    public CellGradientFillType Type { get; set; } = CellGradientFillType.Linear;

    /// <summary>
    /// Rotation angle in degrees for a linear gradient. Excel measures clockwise from the left edge.
    /// 0 = left→right, 90 = top→bottom (default Excel vertical), 180 = right→left, 270 = bottom→top.
    /// Ignored for path gradients.
    /// </summary>
    public double Degree { get; set; }

    /// <summary>Color stops ordered by position (0.0 = start, 1.0 = end).</summary>
    public IReadOnlyList<CellGradientStop> Stops { get; set; } = [];

    // Path-gradient insets (0.0–1.0 fractions of cell size). Ignored for linear gradients.
    /// <summary>Left inset for path gradient.</summary>
    public double Left { get; set; }
    /// <summary>Right inset for path gradient.</summary>
    public double Right { get; set; }
    /// <summary>Top inset for path gradient.</summary>
    public double Top { get; set; }
    /// <summary>Bottom inset for path gradient.</summary>
    public double Bottom { get; set; }

    /// <summary>Deep-copy.</summary>
    public CellGradientFill Clone() => new()
    {
        Type   = Type,
        Degree = Degree,
        Stops  = [.. Stops],
        Left   = Left,
        Right  = Right,
        Top    = Top,
        Bottom = Bottom,
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CellGradientFill other && Equals(other);

    /// <summary>Structural equality.</summary>
    public bool Equals(CellGradientFill? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Type   == other.Type
            && Degree == other.Degree
            && Left   == other.Left
            && Right  == other.Right
            && Top    == other.Top
            && Bottom == other.Bottom
            && StopsEqual(Stops, other.Stops);
    }

    private static bool StopsEqual(IReadOnlyList<CellGradientStop> a, IReadOnlyList<CellGradientStop> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Type);
        h.Add(Degree);
        h.Add(Left);
        h.Add(Right);
        h.Add(Top);
        h.Add(Bottom);
        foreach (var stop in Stops)
            h.Add(stop);
        return h.ToHashCode();
    }
}

/// <summary>
/// Complete style definition for a cell, covering font, fill, borders, and alignment.
/// </summary>
public sealed class CellStyle : IEquatable<CellStyle>
{
    /// <summary>Font family name.</summary>
    public string FontName { get; set; } = "Calibri";

    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; } = 11;

    /// <summary>Bold text.</summary>
    public bool Bold { get; set; }

    /// <summary>Italic text.</summary>
    public bool Italic { get; set; }

    /// <summary>Underlined text.</summary>
    public bool Underline { get; set; }

    /// <summary>Strikethrough text.</summary>
    public bool Strikethrough { get; set; }

    /// <summary>Superscript text.</summary>
    public bool Superscript { get; set; }

    /// <summary>Subscript text.</summary>
    public bool Subscript { get; set; }

    /// <summary>Font color.</summary>
    public CellColor FontColor { get; set; } = CellColor.Black;

    /// <summary>Theme-backed font color. When present, resolves against the workbook theme before falling back to <see cref="FontColor"/>.</summary>
    public WorkbookThemeColorReference? FontThemeColor { get; set; }

    /// <summary>Background fill color. Null means transparent / no fill.</summary>
    public CellColor? FillColor { get; set; }

    /// <summary>Theme-backed fill color. When present, resolves against the workbook theme before falling back to <see cref="FillColor"/>.</summary>
    public WorkbookThemeColorReference? FillThemeColor { get; set; }

    /// <summary>Pattern rendered over the background fill.</summary>
    public CellFillPatternStyle FillPatternStyle { get; set; }

    /// <summary>Pattern foreground color. Null means the app default foreground.</summary>
    public CellColor? FillPatternColor { get; set; }

    /// <summary>Theme-backed pattern foreground color. When present, resolves against the workbook theme before falling back to <see cref="FillPatternColor"/>.</summary>
    public WorkbookThemeColorReference? FillPatternThemeColor { get; set; }

    /// <summary>
    /// Gradient fill definition. When non-null this cell uses a gradient fill rather than a solid/pattern fill.
    /// Corresponds to OOXML <c>&lt;gradientFill&gt;</c> inside <c>&lt;fill&gt;</c>.
    /// </summary>
    public CellGradientFill? GradientFill { get; set; }

    /// <summary>Top border.</summary>
    public CellBorder BorderTop { get; set; }

    /// <summary>Right border.</summary>
    public CellBorder BorderRight { get; set; }

    /// <summary>Bottom border.</summary>
    public CellBorder BorderBottom { get; set; }

    /// <summary>Left border.</summary>
    public CellBorder BorderLeft { get; set; }

    /// <summary>Diagonal-down border (top-left → bottom-right, i.e. Excel's xlDiagonalDown / OOXML diagonalDown="1").</summary>
    public CellBorder BorderDiagonalDown { get; set; }

    /// <summary>Diagonal-up border (bottom-left → top-right, i.e. Excel's xlDiagonalUp / OOXML diagonalUp="1").</summary>
    public CellBorder BorderDiagonalUp { get; set; }

    /// <summary>Number format string (e.g. "General", "0.00", "#,##0").</summary>
    public string NumberFormat { get; set; } = "General";

    /// <summary>Horizontal alignment.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.General;

    /// <summary>Vertical alignment.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Bottom;

    /// <summary>Per-cell reading-order override (OOXML alignment <c>readingOrder</c>).</summary>
    public CellReadingOrder ReadingOrder { get; set; } = CellReadingOrder.Context;

    /// <summary>Whether text wraps within the cell.</summary>
    public bool WrapText { get; set; }

    /// <summary>Whether text should shrink horizontally to fit within the cell.</summary>
    public bool ShrinkToFit { get; set; }

    /// <summary>Double-underline (accounting style).</summary>
    public bool DoubleUnderline { get; set; }

    /// <summary>Left-indent level (0–15 steps, each ~8 px).</summary>
    public int IndentLevel { get; set; }

    /// <summary>Text rotation in degrees: 0 = normal, 90 = rotated up, -90 = rotated down, 255 = vertical stacked.</summary>
    public int TextRotation { get; set; }

    /// <summary>Whether the cell is locked when worksheet protection is enabled.</summary>
    public bool Locked { get; set; } = true;

    /// <summary>Whether the cell formula is hidden when worksheet protection is enabled.</summary>
    public bool Hidden { get; set; }

    /// <summary>
    /// The font scheme this cell's font is tied to. When Minor or Major, the displayed font
    /// follows the workbook theme rather than the stored <see cref="FontName"/>.
    /// </summary>
    public CellFontScheme FontScheme { get; set; } = CellFontScheme.None;

    /// <summary>
    /// Raw OOXML font charset code (<c>&lt;charset val="…"/&gt;</c>), e.g. 2 = Symbol, 128 = ShiftJIS.
    /// Defaults to 1 ("Default"/unset) — ClosedXML's own sentinel for "no charset specified", which
    /// keeps a plain font from emitting a spurious <c>charset</c> attribute on save.
    /// </summary>
    public int Charset { get; set; } = 1;

    /// <summary>
    /// Raw OOXML font family-numbering code (<c>&lt;family val="…"/&gt;</c>), e.g. 1 = Roman, 3 = Modern.
    /// Defaults to 2 (Swiss) — ClosedXML's own sentinel for "no family specified", which keeps a
    /// plain font from emitting a spurious <c>family</c> attribute on save.
    /// </summary>
    public int FontFamily { get; set; } = 2;

    /// <summary>Native dxf attributes not modeled by FreeX, retained for conditional-format XLSX fidelity.</summary>
    public IReadOnlyDictionary<string, string>? NativeDifferentialAttributes { get; set; }

    /// <summary>Native dxf child elements not modeled by FreeX, retained for conditional-format XLSX fidelity.</summary>
    public IReadOnlyList<string>? NativeDifferentialChildXmls { get; set; }

    /// <summary>Original modeled dxf child XML used to merge nested native metadata into regenerated style XML.</summary>
    public IReadOnlyDictionary<string, string>? NativeDifferentialElementXmls { get; set; }

    /// <summary>Returns a fresh default-valued instance.</summary>
    public static readonly CellStyle Default = new();

    /// <summary>Deep-copies all fields into a new <see cref="CellStyle"/> instance.</summary>
    public CellStyle Clone() => new()
    {
        FontName = FontName,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        Strikethrough = Strikethrough,
        Superscript = Superscript,
        Subscript = Subscript,
        FontColor = FontColor,
        FontThemeColor = FontThemeColor,
        FillColor = FillColor,
        FillThemeColor = FillThemeColor,
        FillPatternStyle = FillPatternStyle,
        FillPatternColor = FillPatternColor,
        FillPatternThemeColor = FillPatternThemeColor,
        GradientFill = GradientFill?.Clone(),
        BorderTop = BorderTop,
        BorderRight = BorderRight,
        BorderBottom = BorderBottom,
        BorderLeft = BorderLeft,
        BorderDiagonalDown = BorderDiagonalDown,
        BorderDiagonalUp = BorderDiagonalUp,
        NumberFormat = NumberFormat,
        HorizontalAlignment = HorizontalAlignment,
        VerticalAlignment = VerticalAlignment,
        ReadingOrder = ReadingOrder,
        WrapText = WrapText,
        ShrinkToFit = ShrinkToFit,
        DoubleUnderline = DoubleUnderline,
        IndentLevel = IndentLevel,
        TextRotation = TextRotation,
        Locked = Locked,
        Hidden = Hidden,
        FontScheme = FontScheme,
        Charset = Charset,
        FontFamily = FontFamily,
        NativeDifferentialAttributes = NativeDifferentialAttributes,
        NativeDifferentialChildXmls = NativeDifferentialChildXmls,
        NativeDifferentialElementXmls = NativeDifferentialElementXmls,
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CellStyle other && Equals(other);

    /// <summary>Structural equality across all properties.</summary>
    public bool Equals(CellStyle? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return FontName == other.FontName
            && FontSize == other.FontSize
            && Bold == other.Bold
            && Italic == other.Italic
            && Underline == other.Underline
            && Strikethrough == other.Strikethrough
            && Superscript == other.Superscript
            && Subscript == other.Subscript
            && FontColor == other.FontColor
            && FontThemeColor == other.FontThemeColor
            && FillColor == other.FillColor
            && FillThemeColor == other.FillThemeColor
            && FillPatternStyle == other.FillPatternStyle
            && FillPatternColor == other.FillPatternColor
            && FillPatternThemeColor == other.FillPatternThemeColor
            && GradientFillEquals(GradientFill, other.GradientFill)
            && BorderTop == other.BorderTop
            && BorderRight == other.BorderRight
            && BorderBottom == other.BorderBottom
            && BorderLeft == other.BorderLeft
            && BorderDiagonalDown == other.BorderDiagonalDown
            && BorderDiagonalUp == other.BorderDiagonalUp
            && NumberFormat == other.NumberFormat
            && HorizontalAlignment == other.HorizontalAlignment
            && VerticalAlignment == other.VerticalAlignment
            && ReadingOrder == other.ReadingOrder
            && WrapText == other.WrapText
            && ShrinkToFit == other.ShrinkToFit
            && DoubleUnderline == other.DoubleUnderline
            && IndentLevel == other.IndentLevel
            && TextRotation == other.TextRotation
            && Locked == other.Locked
            && Hidden == other.Hidden
            && FontScheme == other.FontScheme
            && Charset == other.Charset
            && FontFamily == other.FontFamily
            && DictionaryEquals(NativeDifferentialAttributes, other.NativeDifferentialAttributes)
            && ListEquals(NativeDifferentialChildXmls, other.NativeDifferentialChildXmls)
            && DictionaryEquals(NativeDifferentialElementXmls, other.NativeDifferentialElementXmls);
    }

    private static bool GradientFillEquals(CellGradientFill? a, CellGradientFill? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }

    private static bool DictionaryEquals(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var bValue) || value != bValue)
                return false;
        }
        return true;
    }

    private static bool ListEquals(IReadOnlyList<string>? a, IReadOnlyList<string>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(FontName);
        h.Add(FontSize);
        h.Add(Bold);
        h.Add(Italic);
        h.Add(Underline);
        h.Add(Strikethrough);
        h.Add(Superscript);
        h.Add(Subscript);
        h.Add(FontColor);
        h.Add(FontThemeColor);
        h.Add(FillColor);
        h.Add(FillThemeColor);
        h.Add(FillPatternStyle);
        h.Add(FillPatternColor);
        h.Add(FillPatternThemeColor);
        h.Add(GradientFill?.GetHashCode() ?? 0);
        h.Add(BorderTop);
        h.Add(BorderRight);
        h.Add(BorderBottom);
        h.Add(BorderLeft);
        h.Add(BorderDiagonalDown);
        h.Add(BorderDiagonalUp);
        h.Add(NumberFormat);
        h.Add(HorizontalAlignment);
        h.Add(VerticalAlignment);
        h.Add(ReadingOrder);
        h.Add(WrapText);
        h.Add(ShrinkToFit);
        h.Add(DoubleUnderline);
        h.Add(IndentLevel);
        h.Add(TextRotation);
        h.Add(Locked);
        h.Add(Hidden);
        h.Add(FontScheme);
        h.Add(Charset);
        h.Add(FontFamily);
        h.Add(GetDictionaryHashCode(NativeDifferentialAttributes));
        h.Add(GetListHashCode(NativeDifferentialChildXmls));
        h.Add(GetDictionaryHashCode(NativeDifferentialElementXmls));
        return h.ToHashCode();
    }

    /// <summary>
    /// Returns the effective font name that should be displayed, consulting the workbook theme when
    /// <see cref="FontScheme"/> is not None. Falls back to <see cref="FontName"/> when the scheme
    /// resolves to null or when the scheme is None.
    /// </summary>
    public string ResolveEffectiveFontName(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return theme.ResolveSchemeFontName(FontScheme) ?? FontName;
    }

    /// <summary>Resolves the effective font color against <paramref name="theme"/>.</summary>
    public CellColor ResolveFontColor(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return FontThemeColor?.Resolve(theme) ?? FontColor;
    }

    /// <summary>Resolves the effective fill color against <paramref name="theme"/>.</summary>
    public CellColor? ResolveFillColor(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return FillThemeColor?.Resolve(theme) ?? FillColor;
    }

    /// <summary>Resolves the effective pattern foreground color against <paramref name="theme"/>.</summary>
    public CellColor? ResolveFillPatternColor(WorkbookTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return FillPatternThemeColor?.Resolve(theme) ?? FillPatternColor;
    }

    private static int GetDictionaryHashCode(IReadOnlyDictionary<string, string>? dict)
    {
        if (dict is null) return 0;
        // XOR each entry's hash so order doesn't matter
        int code = 0;
        foreach (var (key, value) in dict)
            code ^= HashCode.Combine(key, value);
        return code;
    }

    private static int GetListHashCode(IReadOnlyList<string>? list)
    {
        if (list is null) return 0;
        var h = new HashCode();
        foreach (var item in list)
            h.Add(item);
        return h.ToHashCode();
    }
}

/// <summary>
/// A partial style override. Null fields mean "leave unchanged".
/// Apply via ApplyStyleCommand to avoid resetting unrelated properties.
/// </summary>
public record StyleDiff(
    bool? Bold                  = null,
    bool? Italic                = null,
    bool? Underline             = null,
    bool? Strikethrough         = null,
    bool? Superscript           = null,
    bool? Subscript             = null,
    string? FontName            = null,
    double? FontSize            = null,
    CellColor? FontColor        = null,
    WorkbookThemeColorReference? FontThemeColor = null,
    CellColor? FillColor        = null,
    WorkbookThemeColorReference? FillThemeColor = null,
    HorizontalAlignment? HAlign = null,
    VerticalAlignment? VAlign   = null,
    bool? WrapText              = null,
    bool? ShrinkToFit           = null,
    string? NumberFormat        = null,
    bool? DoubleUnderline       = null,
    int? IndentLevel            = null,
    int? TextRotation           = null,
    CellBorder? BorderTop            = null,
    CellBorder? BorderRight          = null,
    CellBorder? BorderBottom         = null,
    CellBorder? BorderLeft           = null,
    CellBorder? BorderDiagonalDown   = null,
    CellBorder? BorderDiagonalUp     = null,
    bool? Locked                = null,
    bool? Hidden                = null,
    bool? ClearFill             = null,
    CellFillPatternStyle? FillPatternStyle = null,
    CellColor? FillPatternColor = null,
    WorkbookThemeColorReference? FillPatternThemeColor = null,
    CellFontScheme? FontScheme  = null,
    CellGradientFill? GradientFill = null
)
{
    /// <summary>Create a StyleDiff that captures all properties of <paramref name="style"/> as explicit overrides.</summary>
    public static StyleDiff FromStyle(CellStyle style)
    {
        // When the source style has no fill at all (no flat/theme color, no pattern, no gradient),
        // FillColor/FillThemeColor/GradientFill would all serialize as null in the diff, which
        // ApplyTo() interprets as "leave the target's fill untouched" rather than "the source has
        // no fill". Force an explicit ClearFill so painting a fill-less source actually removes
        // any existing fill (color/theme/pattern/gradient) from the target, matching Excel's
        // Format Painter behavior and keeping FillPatternStyle/FillColor internally consistent.
        bool isFillLess = style.FillColor is null
            && style.FillThemeColor is null
            && style.FillPatternStyle == CellFillPatternStyle.None
            && style.GradientFill is null;

        return new(
            Bold:            style.Bold,
            Italic:          style.Italic,
            Underline:       style.Underline,
            Strikethrough:   style.Strikethrough,
            Superscript:     style.Superscript,
            Subscript:       style.Subscript,
            FontName:        style.FontName,
            FontSize:        style.FontSize,
            FontColor:       style.FontColor,
            FontThemeColor:  style.FontThemeColor,
            FillColor:       style.FillColor,
            FillThemeColor:  style.FillThemeColor,
            FillPatternStyle: style.FillPatternStyle,
            FillPatternColor: style.FillPatternColor,
            FillPatternThemeColor: style.FillPatternThemeColor,
            HAlign:          style.HorizontalAlignment,
            VAlign:          style.VerticalAlignment,
            WrapText:        style.WrapText,
            ShrinkToFit:     style.ShrinkToFit,
            NumberFormat:    style.NumberFormat,
            DoubleUnderline: style.DoubleUnderline,
            IndentLevel:     style.IndentLevel,
            TextRotation:    style.TextRotation,
            BorderTop:            style.BorderTop,
            BorderRight:          style.BorderRight,
            BorderBottom:         style.BorderBottom,
            BorderLeft:           style.BorderLeft,
            BorderDiagonalDown:   style.BorderDiagonalDown,
            BorderDiagonalUp:     style.BorderDiagonalUp,
            Locked:          style.Locked,
            Hidden:          style.Hidden,
            ClearFill:       isFillLess ? true : null,
            FontScheme:      style.FontScheme,
            GradientFill:    style.GradientFill?.Clone()
        );
    }

    /// <summary>Apply this diff to a base style, returning a new style with only non-null fields overridden.</summary>
    public CellStyle ApplyTo(CellStyle base_)
    {
        var s = base_.Clone();
        if (Bold           is not null) s.Bold          = Bold.Value;
        if (Italic         is not null) s.Italic        = Italic.Value;
        if (Underline      is not null) s.Underline     = Underline.Value;
        if (Strikethrough  is not null) s.Strikethrough = Strikethrough.Value;
        if (Superscript    is not null)
        {
            s.Superscript = Superscript.Value;
            if (Superscript.Value)
                s.Subscript = false;
        }
        if (Subscript      is not null)
        {
            s.Subscript = Subscript.Value;
            if (Subscript.Value)
                s.Superscript = false;
        }
        if (FontName       is not null)
        {
            s.FontName    = FontName;
            // When FontScheme is not explicitly specified in the diff, a FontName assignment
            // represents an explicit user font pick and pins the scheme to None.
            // When FontScheme IS specified (e.g., FormatPainter copying a themed cell),
            // the explicit scheme value is honored below.
            if (FontScheme is null)
                s.FontScheme = CellFontScheme.None;
        }
        if (FontScheme     is not null) s.FontScheme   = FontScheme.Value;
        if (FontSize       is not null) s.FontSize      = FontSize.Value;
        if (FontColor      is not null)
        {
            s.FontColor = FontColor.Value;
            s.FontThemeColor = null;
        }
        if (FontThemeColor is not null) s.FontThemeColor = FontThemeColor.Value;
        if (FillColor      is not null)
        {
            s.FillColor = FillColor.Value;
            s.FillThemeColor = null;
            // A new flat fill color supersedes any stale gradient fill from the base style,
            // matching Excel's behavior of replacing a gradient with a flat fill when a new
            // fill is applied (e.g. Format Painter or a Cell Style preset), unless this same
            // diff explicitly carries its own GradientFill override (e.g. Format Painter
            // copying a gradient-filled source cell).
            if (GradientFill is null) s.GradientFill = null;
        }
        if (FillThemeColor is not null)
        {
            s.FillThemeColor = FillThemeColor.Value;
            if (GradientFill is null) s.GradientFill = null;
        }
        if (ClearFill      == true)
        {
            s.FillColor = null;
            s.FillThemeColor = null;
            s.FillPatternStyle = CellFillPatternStyle.None;
            s.FillPatternColor = null;
            s.FillPatternThemeColor = null;
            s.GradientFill = null;
        }
        if (GradientFill   is not null) s.GradientFill = GradientFill.Clone();
        if (FillPatternStyle is not null) s.FillPatternStyle = FillPatternStyle.Value;
        if (FillPatternColor is not null)
        {
            s.FillPatternColor = FillPatternColor.Value;
            s.FillPatternThemeColor = null;
        }
        if (FillPatternThemeColor is not null) s.FillPatternThemeColor = FillPatternThemeColor.Value;
        if (HAlign         is not null) s.HorizontalAlignment = HAlign.Value;
        if (VAlign         is not null) s.VerticalAlignment   = VAlign.Value;
        if (WrapText       is not null) s.WrapText      = WrapText.Value;
        if (ShrinkToFit    is not null) s.ShrinkToFit   = ShrinkToFit.Value;
        if (NumberFormat   is not null) s.NumberFormat  = NumberFormat;
        if (DoubleUnderline is not null) s.DoubleUnderline = DoubleUnderline.Value;
        if (IndentLevel    is not null) s.IndentLevel   = Math.Clamp(IndentLevel.Value, 0, 15);
        if (TextRotation   is not null) s.TextRotation  = TextRotation.Value;
        if (BorderTop          is not null) s.BorderTop          = BorderTop.Value;
        if (BorderRight        is not null) s.BorderRight        = BorderRight.Value;
        if (BorderBottom       is not null) s.BorderBottom       = BorderBottom.Value;
        if (BorderLeft         is not null) s.BorderLeft         = BorderLeft.Value;
        if (BorderDiagonalDown is not null) s.BorderDiagonalDown = BorderDiagonalDown.Value;
        if (BorderDiagonalUp   is not null) s.BorderDiagonalUp   = BorderDiagonalUp.Value;
        if (Locked             is not null) s.Locked             = Locked.Value;
        if (Hidden         is not null) s.Hidden        = Hidden.Value;
        return s;
    }
}
