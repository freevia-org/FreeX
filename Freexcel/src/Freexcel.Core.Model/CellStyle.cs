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

    /// <summary>True when this color equals Black (the default).</summary>
    public bool IsDefault => this == Black;
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
public sealed class CellBorder
{
    /// <summary>Line style. Default is <see cref="BorderStyle.None"/>.</summary>
    public BorderStyle Style { get; set; } = BorderStyle.None;

    /// <summary>Border color. Default is black.</summary>
    public CellColor Color { get; set; } = CellColor.Black;
}

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
public sealed class CellStyle
{
    // ── Font ──────────────────────────────────────────────────────────────────

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

    // ── Fill ──────────────────────────────────────────────────────────────────

    /// <summary>Background fill color. Null means transparent / no fill.</summary>
    public CellColor? FillColor { get; set; }

    // ── Borders ───────────────────────────────────────────────────────────────

    /// <summary>Top border.</summary>
    public CellBorder BorderTop { get; set; } = new();

    /// <summary>Right border.</summary>
    public CellBorder BorderRight { get; set; } = new();

    /// <summary>Bottom border.</summary>
    public CellBorder BorderBottom { get; set; } = new();

    /// <summary>Left border.</summary>
    public CellBorder BorderLeft { get; set; } = new();

    // ── Alignment ─────────────────────────────────────────────────────────────

    /// <summary>Number format string (e.g. "General", "0.00", "#,##0").</summary>
    public string NumberFormat { get; set; } = "General";

    /// <summary>Horizontal alignment.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.General;

    /// <summary>Vertical alignment.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Bottom;

    /// <summary>Whether text wraps within the cell.</summary>
    public bool WrapText { get; set; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns a fresh default-valued instance.</summary>
    public static CellStyle Default => new();

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
        BorderTop = new CellBorder { Style = BorderTop.Style, Color = BorderTop.Color },
        BorderRight = new CellBorder { Style = BorderRight.Style, Color = BorderRight.Color },
        BorderBottom = new CellBorder { Style = BorderBottom.Style, Color = BorderBottom.Color },
        BorderLeft = new CellBorder { Style = BorderLeft.Style, Color = BorderLeft.Color },
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
            && BorderTop.Style == other.BorderTop.Style && BorderTop.Color == other.BorderTop.Color
            && BorderRight.Style == other.BorderRight.Style && BorderRight.Color == other.BorderRight.Color
            && BorderBottom.Style == other.BorderBottom.Style && BorderBottom.Color == other.BorderBottom.Color
            && BorderLeft.Style == other.BorderLeft.Style && BorderLeft.Color == other.BorderLeft.Color
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
        h.Add(BorderTop.Style); h.Add(BorderTop.Color);
        h.Add(BorderRight.Style); h.Add(BorderRight.Color);
        h.Add(BorderBottom.Style); h.Add(BorderBottom.Color);
        h.Add(BorderLeft.Style); h.Add(BorderLeft.Color);
        h.Add(NumberFormat);
        h.Add(HorizontalAlignment);
        h.Add(VerticalAlignment);
        h.Add(WrapText);
        return h.ToHashCode();
    }
}
