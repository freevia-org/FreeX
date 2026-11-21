namespace FreeX.Core.Model;

/// <summary>The attribute groups a custom cell style carries, as in Excel's New Cell Style dialog.</summary>
[Flags]
public enum CustomCellStyleParts
{
    None = 0,
    NumberFormat = 1,
    Alignment = 2,
    Font = 4,
    Border = 8,
    Fill = 16,
    Protection = 32,
    All = NumberFormat | Alignment | Font | Border | Fill | Protection,
}

/// <summary>A user-defined named cell style: a name, the formatting it captured, and which groups apply.</summary>
public sealed record CustomCellStyle(string Name, CellStyle Style, CustomCellStyleParts Parts)
{
    public const int MaxNameLength = 255;
}
