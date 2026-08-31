using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Accumulates the automatic-styles needed by a workbook's content.xml: per-<see cref="StyleId"/> cell
/// styles (font/fill/border/alignment + a data-style reference for number formats), and deduplicated
/// column-width and row-height styles. Styles are emitted in a stable order so output is deterministic.
/// </summary>
internal sealed class OdsStyleRegistry
{
    private readonly Workbook _workbook;

    // StyleId.Value -> emitted cell-style name (null when the style is the bare default, which needs none).
    private readonly Dictionary<int, string?> _cellStyleNames = new();
    private readonly List<(string Name, CellStyle Style, string? DataStyleName)> _cellStyles = new();

    // Excel format code -> data-style name (number/date/percentage/currency).
    private readonly Dictionary<string, string> _dataStyleNames = new(StringComparer.Ordinal);
    private readonly List<(string Name, string ExcelCode)> _dataStyles = new();

    // Column width (chars) -> column style name.
    private readonly Dictionary<double, string> _columnStyles = new();
    private readonly List<(string Name, double WidthChars)> _columnStyleList = new();

    // Row height (px) -> row style name.
    private readonly Dictionary<double, string> _rowStyles = new();
    private readonly List<(string Name, double HeightPx)> _rowStyleList = new();

    public OdsStyleRegistry(Workbook workbook) => _workbook = workbook;

    /// <summary>Returns the cell style name for a StyleId, registering it on first use. Null = default.</summary>
    public string? GetCellStyle(StyleId styleId)
    {
        if (_cellStyleNames.TryGetValue(styleId.Value, out var existing))
            return existing;

        var style = _workbook.GetStyle(styleId);
        if (IsEffectivelyDefault(style))
        {
            _cellStyleNames[styleId.Value] = null;
            return null;
        }

        var name = "ce" + _cellStyles.Count;
        string? dataStyleName = null;
        if (OdsNumberFormat.IsCustom(style.NumberFormat))
            dataStyleName = GetDataStyle(style.NumberFormat);

        _cellStyleNames[styleId.Value] = name;
        _cellStyles.Add((name, style, dataStyleName));
        return name;
    }

    private string GetDataStyle(string excelCode)
    {
        if (_dataStyleNames.TryGetValue(excelCode, out var existing))
            return existing;
        var name = "N" + _dataStyles.Count;
        _dataStyleNames[excelCode] = name;
        _dataStyles.Add((name, excelCode));
        return name;
    }

    public string GetColumnStyle(double widthChars)
    {
        if (_columnStyles.TryGetValue(widthChars, out var existing))
            return existing;
        var name = "co" + _columnStyleList.Count;
        _columnStyles[widthChars] = name;
        _columnStyleList.Add((name, widthChars));
        return name;
    }

    public string GetRowStyle(double heightPx)
    {
        if (_rowStyles.TryGetValue(heightPx, out var existing))
            return existing;
        var name = "ro" + _rowStyleList.Count;
        _rowStyles[heightPx] = name;
        _rowStyleList.Add((name, heightPx));
        return name;
    }

    /// <summary>Emits every accumulated automatic-style element (data styles first, then cell/col/row).</summary>
    public IEnumerable<XElement> EmitAutomaticStyles()
    {
        var number = OdsFileAdapter.NumberNs;
        var styleNs = OdsFileAdapter.StyleNs;

        // Data styles must precede the cell styles that reference them.
        foreach (var (name, excelCode) in _dataStyles)
            yield return OdsNumberFormat.BuildDataStyle(name, excelCode);

        foreach (var (name, style, dataStyleName) in _cellStyles)
            yield return BuildCellStyle(name, style, dataStyleName);

        foreach (var (name, widthChars) in _columnStyleList)
        {
            yield return new XElement(styleNs + "style",
                new XAttribute(styleNs + "name", name),
                new XAttribute(styleNs + "family", "table-column"),
                new XElement(styleNs + "table-column-properties",
                    new XAttribute(OdsFileAdapter.StyleNs + "column-width", ColumnWidthToCm(widthChars)),
                    // Carry the exact char width so we recover Excel's value precisely on read.
                    new XAttribute(OdsFileAdapter.StyleNs + "freex-width-chars", widthChars.ToString("R", CultureInfo.InvariantCulture))));
        }

        foreach (var (name, heightPx) in _rowStyleList)
        {
            yield return new XElement(styleNs + "style",
                new XAttribute(styleNs + "name", name),
                new XAttribute(styleNs + "family", "table-row"),
                new XElement(styleNs + "table-row-properties",
                    new XAttribute(OdsFileAdapter.StyleNs + "row-height", RowHeightToCm(heightPx)),
                    new XAttribute(OdsFileAdapter.StyleNs + "freex-height-px", heightPx.ToString("R", CultureInfo.InvariantCulture))));
        }
    }

    private XElement BuildCellStyle(string name, CellStyle style, string? dataStyleName)
    {
        var styleNs = OdsFileAdapter.StyleNs;
        var foNs = OdsFileAdapter.FoNs;

        var styleElement = new XElement(styleNs + "style",
            new XAttribute(styleNs + "name", name),
            new XAttribute(styleNs + "family", "table-cell"));
        if (dataStyleName is not null)
            styleElement.SetAttributeValue(styleNs + "data-style-name", dataStyleName);

        // ---- table-cell-properties: fill, borders, vertical alignment, wrap, rotation ----
        var cellProps = new XElement(styleNs + "table-cell-properties");
        var hasCellProps = false;

        if (style.FillColor is { } fill)
        {
            cellProps.SetAttributeValue(foNs + "background-color", HexColor(fill));
            hasCellProps = true;
        }

        if (AddBorder(cellProps, foNs + "border-top", style.BorderTop)) hasCellProps = true;
        if (AddBorder(cellProps, foNs + "border-right", style.BorderRight)) hasCellProps = true;
        if (AddBorder(cellProps, foNs + "border-bottom", style.BorderBottom)) hasCellProps = true;
        if (AddBorder(cellProps, foNs + "border-left", style.BorderLeft)) hasCellProps = true;

        if (style.VerticalAlignment != VerticalAlignment.Bottom)
        {
            cellProps.SetAttributeValue(styleNs + "vertical-align", style.VerticalAlignment switch
            {
                VerticalAlignment.Top => "top",
                VerticalAlignment.Center => "middle",
                VerticalAlignment.Justify => "automatic",
                VerticalAlignment.Distributed => "automatic",
                _ => "bottom",
            });
            hasCellProps = true;
        }

        if (style.WrapText)
        {
            cellProps.SetAttributeValue(styleNs + "wrap-option", "wrap");
            hasCellProps = true;
        }

        if (style.TextRotation != 0)
        {
            // Excel 255 = vertical stacked; ODF expresses that via direction=ttb.
            if (style.TextRotation == 255)
                cellProps.SetAttributeValue(styleNs + "direction", "ttb");
            else
                cellProps.SetAttributeValue(styleNs + "rotation-angle", style.TextRotation.ToString(CultureInfo.InvariantCulture));
            hasCellProps = true;
        }

        // Always carry the exact Excel rotation + a vertical-align hint so read recovers them precisely.
        cellProps.SetAttributeValue(OdsFileAdapter.StyleNs + "freex-rotation", style.TextRotation.ToString(CultureInfo.InvariantCulture));
        cellProps.SetAttributeValue(OdsFileAdapter.StyleNs + "freex-valign", ((int)style.VerticalAlignment).ToString(CultureInfo.InvariantCulture));
        hasCellProps = true;

        styleElement.Add(cellProps);

        // ---- paragraph-properties: horizontal alignment + indent ----
        var paragraphProps = new XElement(styleNs + "paragraph-properties");
        var hasParaProps = false;
        if (style.HorizontalAlignment != HorizontalAlignment.General)
        {
            paragraphProps.SetAttributeValue(foNs + "text-align", style.HorizontalAlignment switch
            {
                HorizontalAlignment.Left => "start",
                HorizontalAlignment.Center => "center",
                HorizontalAlignment.Right => "end",
                HorizontalAlignment.Justify => "justify",
                HorizontalAlignment.Distributed => "justify",
                _ => "start",
            });
            hasParaProps = true;
        }
        if (style.IndentLevel > 0)
        {
            paragraphProps.SetAttributeValue(foNs + "margin-left",
                (style.IndentLevel * 0.25).ToString("0.###", CultureInfo.InvariantCulture) + "cm");
            hasParaProps = true;
        }
        // Hints so horizontal alignment + indent recover exactly regardless of the lossy cm mapping.
        paragraphProps.SetAttributeValue(OdsFileAdapter.StyleNs + "freex-halign", ((int)style.HorizontalAlignment).ToString(CultureInfo.InvariantCulture));
        paragraphProps.SetAttributeValue(OdsFileAdapter.StyleNs + "freex-indent", style.IndentLevel.ToString(CultureInfo.InvariantCulture));
        styleElement.Add(paragraphProps);
        _ = hasParaProps;

        // ---- text-properties: font name/size/weight/style/decoration/color ----
        var textProps = new XElement(styleNs + "text-properties");
        var effectiveFont = style.ResolveEffectiveFontName(_workbook.Theme);
        textProps.SetAttributeValue(OdsFileAdapter.StyleNs + "font-name", effectiveFont);
        textProps.SetAttributeValue(OdsFileAdapter.FoNs + "font-family", effectiveFont);
        textProps.SetAttributeValue(OdsFileAdapter.FoNs + "font-size",
            style.FontSize.ToString("R", CultureInfo.InvariantCulture) + "pt");
        if (style.Bold)
            textProps.SetAttributeValue(OdsFileAdapter.FoNs + "font-weight", "bold");
        if (style.Italic)
            textProps.SetAttributeValue(OdsFileAdapter.FoNs + "font-style", "italic");
        if (style.Underline || style.DoubleUnderline)
        {
            textProps.SetAttributeValue(OdsFileAdapter.StyleNs + "text-underline-style", "solid");
            textProps.SetAttributeValue(OdsFileAdapter.StyleNs + "text-underline-width", "auto");
            textProps.SetAttributeValue(OdsFileAdapter.StyleNs + "text-underline-color", "font-color");
            if (style.DoubleUnderline)
                textProps.SetAttributeValue(OdsFileAdapter.StyleNs + "text-underline-type", "double");
        }
        if (style.Strikethrough)
            textProps.SetAttributeValue(OdsFileAdapter.StyleNs + "text-line-through-style", "solid");
        textProps.SetAttributeValue(OdsFileAdapter.FoNs + "color", HexColor(style.ResolveFontColor(_workbook.Theme)));
        styleElement.Add(textProps);

        _ = hasCellProps;
        return styleElement;
    }

    private bool AddBorder(XElement cellProps, XName attr, CellBorder border)
    {
        // freex-theme-border-color-F1: ODF has no theme-color concept, so a theme-backed edge
        // (CellBorder.ThemeColor) must be flattened through the workbook's CURRENT theme — exactly like
        // the font color this registry already writes via style.ResolveFontColor(_workbook.Theme).
        // Reading border.Color raw exported the RGB baked in at load time, so a theme change recolored
        // the exported fonts but left the borders on the old palette. Resolved once and used for the
        // default test, the visible fo:border, and the freex round-trip hint alike, so all three agree.
        var resolvedColor = border.ResolveColor(_workbook.Theme);

        // An invisible border (Style=None) with the default black color carries no information and is
        // skipped. But Excel sometimes stores a colored border whose style is None; the model treats that
        // color as significant (CellBorder compares Style AND Color), so we must still persist it via the
        // freex hint to round-trip it exactly — without emitting a visible fo:border.
        var isDefault = border.Style == BorderStyle.None && resolvedColor == CellColor.Black;
        if (isDefault)
            return false;

        if (border.Style != BorderStyle.None)
            cellProps.SetAttributeValue(attr, OdsBorder.ToOdf(border, resolvedColor));
        // Carry an exact-style hint per edge so the border style enum + color recover precisely.
        cellProps.SetAttributeValue(OdsFileAdapter.StyleNs + ("freex-" + attr.LocalName),
            ((int)border.Style).ToString(CultureInfo.InvariantCulture) + ":" + HexColor(resolvedColor));
        return true;
    }

    private bool IsEffectivelyDefault(CellStyle style) => style.Equals(CellStyle.Default);

    // ---- unit + color helpers --------------------------------------------------------------------

    internal static string HexColor(CellColor c) =>
        $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // Excel column width is "characters of the default font"; ~7px per char at default. Map to cm so the
    // file renders, but the freex-width-chars hint is what we read back for exactness.
    private static string ColumnWidthToCm(double widthChars)
    {
        var px = widthChars * 7.0 + 5.0;
        var cm = px / 37.795275591; // 96 dpi: 1cm = 37.795px
        return cm.ToString("0.####", CultureInfo.InvariantCulture) + "cm";
    }

    private static string RowHeightToCm(double heightPx)
    {
        var cm = heightPx / 37.795275591;
        return cm.ToString("0.####", CultureInfo.InvariantCulture) + "cm";
    }
}
