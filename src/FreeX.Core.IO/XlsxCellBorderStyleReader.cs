using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxCellBorderStyleReader
{
    public static XlsxCellBorderStyleTable Read(
        XDocument? stylesXml,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var root = stylesXml?.Root;
        if (root is null)
            return XlsxCellBorderStyleTable.Empty;

        try
        {
            XNamespace workbookNs = root.Name.Namespace;
            var borders = root
                .Element(workbookNs + "borders")?
                .Elements(workbookNs + "border")
                .Select(border => ReadBorder(border, workbookNs, theme, indexedColors))
                .ToList();
            if (borders is not { Count: > 0 })
                return XlsxCellBorderStyleTable.Empty;

            var cellXfs = root
                .Element(workbookNs + "cellXfs")?
                .Elements(workbookNs + "xf")
                .ToList();
            if (cellXfs is not { Count: > 0 })
                return XlsxCellBorderStyleTable.Empty;

            var styles = new XlsxCellBorderStyle[cellXfs.Count];
            for (var i = 0; i < cellXfs.Count; i++)
            {
                if (int.TryParse(
                        cellXfs[i].Attribute("borderId")?.Value,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var borderId) &&
                    borderId >= 0 &&
                    borderId < borders.Count)
                {
                    styles[i] = borders[borderId];
                }
            }

            return new XlsxCellBorderStyleTable(styles);
        }
        catch
        {
            return XlsxCellBorderStyleTable.Empty;
        }
    }

    private static XlsxCellBorderStyle ReadBorder(
        XElement border,
        XNamespace workbookNs,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        var diagEdge = border.Element(workbookNs + "diagonal");
        var diagBorder = ReadBorderEdge(diagEdge, workbookNs, theme, indexedColors);
        var diagonalDown = border.Attribute("diagonalDown")?.Value is "1" or "true";
        var diagonalUp = border.Attribute("diagonalUp")?.Value is "1" or "true";
        return new XlsxCellBorderStyle(
            ReadBorderEdge(border.Element(workbookNs + "top"), workbookNs, theme, indexedColors),
            ReadBorderEdge(border.Element(workbookNs + "right"), workbookNs, theme, indexedColors),
            ReadBorderEdge(border.Element(workbookNs + "bottom"), workbookNs, theme, indexedColors),
            ReadBorderEdge(border.Element(workbookNs + "left"), workbookNs, theme, indexedColors),
            diagonalDown ? diagBorder : default,
            diagonalUp ? diagBorder : default);
    }

    private static CellBorder ReadBorderEdge(
        XElement? edge,
        XNamespace workbookNs,
        WorkbookTheme theme,
        WorkbookIndexedColorPalette indexedColors)
    {
        if (edge is null)
            return default;

        var style = XlsxBorderStyleCodec.Decode(edge.Attribute("style")?.Value);
        if (style == BorderStyle.None)
            return default;

        var hasColor = XlsxColorReader.TryReadCellColorWithThemeReference(
            edge.Element(workbookNs + "color"), theme, indexedColors, out var parsedColor, out var themeColorReference);
        var color = hasColor ? parsedColor : CellColor.Black;
        return new CellBorder(style, color, themeColorReference);
    }
}

internal sealed class XlsxCellBorderStyleTable
{
    public static readonly XlsxCellBorderStyleTable Empty = new([]);

    private readonly IReadOnlyList<XlsxCellBorderStyle> _styles;

    public XlsxCellBorderStyleTable(IReadOnlyList<XlsxCellBorderStyle> styles)
    {
        _styles = styles;
        for (var i = 0; i < styles.Count; i++)
        {
            if (styles[i].HasVisibleBorder)
            {
                HasVisibleBorders = true;
                break;
            }
        }
    }

    public bool HasVisibleBorders { get; }

    public bool TryGetVisibleBorders(int styleIndex, out XlsxCellBorderStyle borders)
    {
        if (styleIndex >= 0 && styleIndex < _styles.Count)
        {
            borders = _styles[styleIndex];
            return borders.HasVisibleBorder;
        }

        borders = default;
        return false;
    }
}

internal readonly record struct XlsxCellBorderStyle(
    CellBorder Top,
    CellBorder Right,
    CellBorder Bottom,
    CellBorder Left,
    CellBorder DiagonalDown = default,
    CellBorder DiagonalUp = default)
{
    public bool HasVisibleBorder =>
        Top.Style != BorderStyle.None ||
        Right.Style != BorderStyle.None ||
        Bottom.Style != BorderStyle.None ||
        Left.Style != BorderStyle.None ||
        DiagonalDown.Style != BorderStyle.None ||
        DiagonalUp.Style != BorderStyle.None;

    public void ApplyTo(CellStyle style)
    {
        style.BorderTop = Top;
        style.BorderRight = Right;
        style.BorderBottom = Bottom;
        style.BorderLeft = Left;
        style.BorderDiagonalDown = DiagonalDown;
        style.BorderDiagonalUp = DiagonalUp;
    }
}
