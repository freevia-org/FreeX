using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Reads cell-level gradient fills (<c>&lt;gradientFill&gt;</c>) from styles.xml and builds
/// a per-xf lookup table that the XLSX loader overlays onto ClosedXML-mapped styles.
/// ClosedXML does not expose gradientFill on its IXLStyle, so we read the raw OOXML directly.
/// </summary>
internal static class XlsxCellGradientFillReader
{
    public static XlsxCellGradientFillTable Read(
        XDocument? stylesXml,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var root = stylesXml?.Root;
        if (root is null)
            return XlsxCellGradientFillTable.Empty;

        try
        {
            XNamespace ns = root.Name.Namespace;

            // Read the fills array
            var fills = root
                .Element(ns + "fills")?
                .Elements(ns + "fill")
                .Select(fill => ReadGradientFill(fill, ns, theme, indexedColors))
                .ToList();

            if (fills is not { Count: > 0 })
                return XlsxCellGradientFillTable.Empty;

            // Read cellXfs — each xf element references a fillId
            var cellXfs = root
                .Element(ns + "cellXfs")?
                .Elements(ns + "xf")
                .ToList();

            if (cellXfs is not { Count: > 0 })
                return XlsxCellGradientFillTable.Empty;

            var gradients = new CellGradientFill?[cellXfs.Count];
            bool hasAny = false;
            for (var i = 0; i < cellXfs.Count; i++)
            {
                if (int.TryParse(
                        cellXfs[i].Attribute("fillId")?.Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var fillId) &&
                    fillId >= 0 &&
                    fillId < fills.Count &&
                    fills[fillId] is { } gradient)
                {
                    gradients[i] = gradient;
                    hasAny = true;
                }
            }

            return hasAny ? new XlsxCellGradientFillTable(gradients) : XlsxCellGradientFillTable.Empty;
        }
        catch
        {
            return XlsxCellGradientFillTable.Empty;
        }
    }

    private static CellGradientFill? ReadGradientFill(
        XElement fill,
        XNamespace ns,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var gradientFillEl = fill.Element(ns + "gradientFill");
        if (gradientFillEl is null)
            return null;

        var typeAttr = gradientFillEl.Attribute("type")?.Value;
        var gradType = string.Equals(typeAttr, "path", StringComparison.OrdinalIgnoreCase)
            ? CellGradientFillType.Path
            : CellGradientFillType.Linear;

        var degree = ParseDouble(gradientFillEl.Attribute("degree")?.Value, 0.0);
        var left   = ParseDouble(gradientFillEl.Attribute("left")?.Value,   0.0);
        var right  = ParseDouble(gradientFillEl.Attribute("right")?.Value,  0.0);
        var top    = ParseDouble(gradientFillEl.Attribute("top")?.Value,    0.0);
        var bottom = ParseDouble(gradientFillEl.Attribute("bottom")?.Value, 0.0);

        var stops = gradientFillEl
            .Elements(ns + "stop")
            .Select(stop => ReadStop(stop, ns, theme, indexedColors))
            .Where(s => s.HasValue)
            .Select(s => s!.Value)
            .OrderBy(s => s.Position)
            .ToList();

        if (stops.Count < 2)
            return null; // degenerate — not a real gradient

        return new CellGradientFill
        {
            Type   = gradType,
            Degree = degree,
            Left   = left,
            Right  = right,
            Top    = top,
            Bottom = bottom,
            Stops  = stops,
        };
    }

    private static CellGradientStop? ReadStop(
        XElement stop,
        XNamespace ns,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        if (!double.TryParse(
                stop.Attribute("position")?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var position))
        {
            return null;
        }

        var colorEl = stop.Element(ns + "color");
        if (!XlsxColorReader.TryReadCellColor(colorEl, theme, indexedColors, out var color))
            return null;

        return new CellGradientStop(position, color);
    }

    private static double ParseDouble(string? text, double defaultValue)
    {
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : defaultValue;
    }
}

internal sealed class XlsxCellGradientFillTable
{
    public static readonly XlsxCellGradientFillTable Empty = new([]);

    private readonly IReadOnlyList<CellGradientFill?> _gradients;

    public XlsxCellGradientFillTable(IReadOnlyList<CellGradientFill?> gradients)
    {
        _gradients = gradients;
    }

    public bool HasAny => _gradients.Count > 0;

    /// <summary>
    /// Try to get a gradient fill for the given style index (cellXf index).
    /// Returns false when the index is out of range or the fill is not a gradient.
    /// </summary>
    public bool TryGet(int styleIndex, out CellGradientFill? gradient)
    {
        if (styleIndex >= 0 && styleIndex < _gradients.Count && _gradients[styleIndex] is { } g)
        {
            gradient = g;
            return true;
        }

        gradient = null;
        return false;
    }
}
