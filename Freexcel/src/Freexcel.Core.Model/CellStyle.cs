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
            && FontColor == other.FontColor
            && FillColor == other.FillColor
            && BorderTop == other.BorderTop
            && BorderRight == other.BorderRight
            && BorderBottom == other.BorderBottom
            && BorderLeft == other.BorderLeft
            && NumberFormat == other.NumberFormat
            && HorizontalAlignment == other.HorizontalAlignment
            && VerticalAlignment == other.VerticalAlignment
            && WrapText == other.WrapText;
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
        return h.ToHashCode();
    }
}
