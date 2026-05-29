namespace FreeX.Core.Model;

/// <summary>
/// Workbook-level overrides for Excel indexed colors used by number-format [ColorN] directives.
/// </summary>
public sealed class WorkbookIndexedColorPalette
{
    private static readonly CellColor[] DefaultColors =
    [
        default,
        new(0x00, 0x00, 0x00),
        new(0xFF, 0xFF, 0xFF),
        new(0xFF, 0x00, 0x00),
        new(0x00, 0xB0, 0x50),
        new(0x00, 0x70, 0xC0),
        new(0xFF, 0xFF, 0x00),
        new(0xFF, 0x00, 0xFF),
        new(0x00, 0xFF, 0xFF),
        new(0x80, 0x00, 0x00),
        new(0x00, 0x80, 0x00),
        new(0x00, 0x00, 0x80),
        new(0x80, 0x80, 0x00),
        new(0x80, 0x00, 0x80),
        new(0x00, 0x80, 0x80),
        new(0xC0, 0xC0, 0xC0),
        new(0x80, 0x80, 0x80),
        new(0x99, 0x99, 0xFF),
        new(0x99, 0x33, 0x66),
        new(0xFF, 0xFF, 0xCC),
        new(0xCC, 0xFF, 0xFF),
        new(0x66, 0x00, 0x66),
        new(0xFF, 0x80, 0x80),
        new(0x00, 0x66, 0xCC),
        new(0xCC, 0xCC, 0xFF),
        new(0x00, 0x00, 0x80),
        new(0xFF, 0x00, 0xFF),
        new(0xFF, 0xFF, 0x00),
        new(0x00, 0xFF, 0xFF),
        new(0x80, 0x00, 0x80),
        new(0x80, 0x00, 0x00),
        new(0x00, 0x80, 0x80),
        new(0x00, 0x00, 0xFF),
        new(0x00, 0xCC, 0xFF),
        new(0xCC, 0xFF, 0xFF),
        new(0xCC, 0xFF, 0xCC),
        new(0xFF, 0xFF, 0x99),
        new(0x99, 0xCC, 0xFF),
        new(0xFF, 0x99, 0xCC),
        new(0xCC, 0x99, 0xFF),
        new(0xFF, 0xCC, 0x99),
        new(0x33, 0x66, 0xFF),
        new(0x33, 0xCC, 0xCC),
        new(0x99, 0xCC, 0x00),
        new(0xFF, 0xCC, 0x00),
        new(0xFF, 0x99, 0x00),
        new(0xFF, 0x66, 0x00),
        new(0x66, 0x66, 0x99),
        new(0x96, 0x96, 0x96),
        new(0x00, 0x33, 0x66),
        new(0x33, 0x99, 0x66),
        new(0x00, 0x33, 0x00),
        new(0x33, 0x33, 0x00),
        new(0x99, 0x33, 0x00),
        new(0x33, 0x33, 0x99),
        new(0x33, 0x33, 0x33),
        new(0x33, 0x33, 0x33)
    ];

    private readonly Dictionary<int, CellColor> _colors = [];

    /// <summary>Authored indexed-color overrides keyed by Excel 1-based color index.</summary>
    public IReadOnlyDictionary<int, CellColor> Colors => _colors;

    /// <summary>Set or replace a color override for an Excel indexed color.</summary>
    public void SetColor(int index, CellColor color)
    {
        ValidateIndex(index);
        _colors[index] = color;
    }

    /// <summary>Remove an authored color override.</summary>
    public bool RemoveColor(int index)
    {
        ValidateIndex(index);
        return _colors.Remove(index);
    }

    /// <summary>Try to resolve an authored color override.</summary>
    public bool TryGetColor(int index, out CellColor color) =>
        _colors.TryGetValue(index, out color);

    /// <summary>Try to resolve an authored override, falling back to Excel's built-in indexed palette.</summary>
    public bool TryResolveColor(int index, out CellColor color) =>
        TryGetColor(index, out color) || TryGetDefaultColor(index, out color);

    /// <summary>Try to resolve Excel's built-in indexed color for a number-format [ColorN] index.</summary>
    public static bool TryGetDefaultColor(int index, out CellColor color)
    {
        if (index is >= 1 && index < DefaultColors.Length)
        {
            color = DefaultColors[index];
            return true;
        }

        color = default;
        return false;
    }

    private static void ValidateIndex(int index)
    {
        if (index is < 1 or > 56)
            throw new ArgumentOutOfRangeException(nameof(index), "Excel number-format indexed colors must be in the range 1-56.");
    }
}
