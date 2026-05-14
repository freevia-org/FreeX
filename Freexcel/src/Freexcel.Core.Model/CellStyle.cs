namespace Freexcel.Core.Model;

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
    Double
}

/// <summary>
/// A single border edge on a cell.
/// </summary>
public readonly record struct CellBorder(BorderStyle Style = BorderStyle.None, CellColor Color = default);

/// <summary>
/// Horizontal alignment within a cell.
/// </summary>
public enum HorizontalAlignment
{
    General,
    Left,
    Center,
    Right
}

/// <summary>
/// Vertical alignment within a cell.
/// </summary>
public enum VerticalAlignment
{
    Top,
    Center,
    Bottom
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

    /// <summary>Font color.</summary>
    public CellColor FontColor { get; set; } = CellColor.Black;

    /// <summary>Background fill color. Null means transparent / no fill.</summary>
    public CellColor? FillColor { get; set; }

    /// <summary>Top border.</summary>
    public CellBorder BorderTop { get; set; }

    /// <summary>Right border.</summary>
    public CellBorder BorderRight { get; set; }

    /// <summary>Bottom border.</summary>
    public CellBorder BorderBottom { get; set; }

    /// <summary>Left border.</summary>
    public CellBorder BorderLeft { get; set; }

    /// <summary>Number format string (e.g. "General", "0.00", "#,##0").</summary>
    public string NumberFormat { get; set; } = "General";

    /// <summary>Horizontal alignment.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.General;

    /// <summary>Vertical alignment.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Bottom;

    /// <summary>Whether text wraps within the cell.</summary>
    public bool WrapText { get; set; }

    /// <summary>Double-underline (accounting style).</summary>
    public bool DoubleUnderline { get; set; }

    /// <summary>Left-indent level (0–15 steps, each ~8 px).</summary>
    public int IndentLevel { get; set; }

    /// <summary>Text rotation in degrees: 0 = normal, 90 = rotated up, -90 = rotated down, 255 = vertical stacked.</summary>
    public int TextRotation { get; set; }

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
        FontColor = FontColor,
        FillColor = FillColor,
        BorderTop = BorderTop,
        BorderRight = BorderRight,
        BorderBottom = BorderBottom,
        BorderLeft = BorderLeft,
        NumberFormat = NumberFormat,
        HorizontalAlignment = HorizontalAlignment,
        VerticalAlignment = VerticalAlignment,
        WrapText = WrapText,
        DoubleUnderline = DoubleUnderline,
        IndentLevel = IndentLevel,
        TextRotation = TextRotation,
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
            && FontColor == other.FontColor
            && FillColor == other.FillColor
            && BorderTop == other.BorderTop
            && BorderRight == other.BorderRight
            && BorderBottom == other.BorderBottom
            && BorderLeft == other.BorderLeft
            && NumberFormat == other.NumberFormat
            && HorizontalAlignment == other.HorizontalAlignment
            && VerticalAlignment == other.VerticalAlignment
            && WrapText == other.WrapText
            && DoubleUnderline == other.DoubleUnderline
            && IndentLevel == other.IndentLevel
            && TextRotation == other.TextRotation;
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
        h.Add(FontColor);
        h.Add(FillColor);
        h.Add(BorderTop);
        h.Add(BorderRight);
        h.Add(BorderBottom);
        h.Add(BorderLeft);
        h.Add(NumberFormat);
        h.Add(HorizontalAlignment);
        h.Add(VerticalAlignment);
        h.Add(WrapText);
        h.Add(DoubleUnderline);
        h.Add(IndentLevel);
        h.Add(TextRotation);
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
    string? FontName            = null,
    double? FontSize            = null,
    CellColor? FontColor        = null,
    CellColor? FillColor        = null,
    HorizontalAlignment? HAlign = null,
    VerticalAlignment? VAlign   = null,
    bool? WrapText              = null,
    string? NumberFormat        = null,
    bool? DoubleUnderline       = null,
    int? IndentLevel            = null,
    int? TextRotation           = null,
    CellBorder? BorderTop       = null,
    CellBorder? BorderRight     = null,
    CellBorder? BorderBottom    = null,
    CellBorder? BorderLeft      = null,
    bool? ClearFill             = null   // true → set FillColor to null
)
{
    /// <summary>Create a StyleDiff that captures all properties of <paramref name="style"/> as explicit overrides.</summary>
    public static StyleDiff FromStyle(CellStyle style) => new(
        Bold:            style.Bold,
        Italic:          style.Italic,
        Underline:       style.Underline,
        Strikethrough:   style.Strikethrough,
        FontName:        style.FontName,
        FontSize:        style.FontSize,
        FontColor:       style.FontColor,
        FillColor:       style.FillColor,
        HAlign:          style.HorizontalAlignment,
        VAlign:          style.VerticalAlignment,
        WrapText:        style.WrapText,
        NumberFormat:    style.NumberFormat,
        DoubleUnderline: style.DoubleUnderline,
        IndentLevel:     style.IndentLevel,
        TextRotation:    style.TextRotation,
        BorderTop:       style.BorderTop,
        BorderRight:     style.BorderRight,
        BorderBottom:    style.BorderBottom,
        BorderLeft:      style.BorderLeft
    );

    /// <summary>Apply this diff to a base style, returning a new style with only non-null fields overridden.</summary>
    public CellStyle ApplyTo(CellStyle base_)
    {
        var s = base_.Clone();
        if (Bold           is not null) s.Bold          = Bold.Value;
        if (Italic         is not null) s.Italic        = Italic.Value;
        if (Underline      is not null) s.Underline     = Underline.Value;
        if (Strikethrough  is not null) s.Strikethrough = Strikethrough.Value;
        if (FontName       is not null) s.FontName      = FontName;
        if (FontSize       is not null) s.FontSize      = FontSize.Value;
        if (FontColor      is not null) s.FontColor     = FontColor.Value;
        if (FillColor      is not null) s.FillColor     = FillColor.Value;
        if (ClearFill      == true)     s.FillColor     = null;
        if (HAlign         is not null) s.HorizontalAlignment = HAlign.Value;
        if (VAlign         is not null) s.VerticalAlignment   = VAlign.Value;
        if (WrapText       is not null) s.WrapText      = WrapText.Value;
        if (NumberFormat   is not null) s.NumberFormat  = NumberFormat;
        if (DoubleUnderline is not null) s.DoubleUnderline = DoubleUnderline.Value;
        if (IndentLevel    is not null) s.IndentLevel   = Math.Clamp(IndentLevel.Value, 0, 15);
        if (TextRotation   is not null) s.TextRotation  = TextRotation.Value;
        if (BorderTop      is not null) s.BorderTop     = BorderTop.Value;
        if (BorderRight    is not null) s.BorderRight   = BorderRight.Value;
        if (BorderBottom   is not null) s.BorderBottom  = BorderBottom.Value;
        if (BorderLeft     is not null) s.BorderLeft    = BorderLeft.Value;
        return s;
    }
}
