using System.Globalization;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

/// <summary>
/// Read-side counterpart of <see cref="OdsStyleRegistry"/>: indexes ODF automatic-styles by name and
/// reconstructs FreeX <see cref="CellStyle"/> / column-width / row-height values. Reconstruction prefers
/// the private <c>freex-*</c> hint attributes the writer emits (so the exact Excel format code, border
/// style, alignment enum, indent, rotation, and char/px sizes recover precisely); the standard ODF
/// attributes are used as a fallback for files authored by other applications.
/// </summary>
internal sealed class OdsStyleTable
{
    /// <summary>Excel's maximum decimal places in a number format (r492).</summary>
    private const int MaxDecimalPlaces = 30;

    private static readonly XNamespace Style = OdsFileAdapter.StyleNs;
    private static readonly XNamespace Fo = OdsFileAdapter.FoNs;
    private static readonly XNamespace Number = OdsFileAdapter.NumberNs;

    private readonly Dictionary<string, XElement> _cellStyleElements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _columnWidths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _rowHeights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _dataStyleCodes = new(StringComparer.Ordinal);

    // Resolved cache: style name -> registered StyleId in the target workbook.
    private readonly Dictionary<string, StyleId?> _resolved = new(StringComparer.Ordinal);

    public void Load(XElement? container)
    {
        if (container is null)
            return;

        foreach (var element in container.Elements())
        {
            // Data styles (number/date/percentage/currency).
            if (IsDataStyle(element.Name))
            {
                var name = (string?)element.Attribute(Style + "name");
                if (name is null) continue;
                var code = OdsNumberFormat.ReadFreeXFormatCode(element) ?? InferFormatCode(element);
                if (code is not null)
                    _dataStyleCodes[name] = code;
                continue;
            }

            if (element.Name != Style + "style")
                continue;

            var family = (string?)element.Attribute(Style + "family");
            var styleName = (string?)element.Attribute(Style + "name");
            if (styleName is null)
                continue;

            switch (family)
            {
                case "table-cell":
                    _cellStyleElements[styleName] = element;
                    break;
                case "table-column":
                    var colProps = element.Element(Style + "table-column-properties");
                    if (colProps is not null && TryReadCharWidth(colProps, out var width))
                        _columnWidths[styleName] = width;
                    break;
                case "table-row":
                    var rowProps = element.Element(Style + "table-row-properties");
                    if (rowProps is not null && TryReadPxHeight(rowProps, out var height))
                        _rowHeights[styleName] = height;
                    break;
            }
        }
    }

    public double? GetColumnWidth(string styleName) =>
        _columnWidths.TryGetValue(styleName, out var w) ? w : null;

    public double? GetRowHeight(string styleName) =>
        _rowHeights.TryGetValue(styleName, out var h) ? h : null;

    /// <summary>Resolves a cell-style name to a registered StyleId, building the CellStyle on first use.</summary>
    public StyleId? GetCellStyle(Workbook workbook, string styleName)
    {
        if (_resolved.TryGetValue(styleName, out var cached))
            return cached;

        if (!_cellStyleElements.TryGetValue(styleName, out var element))
        {
            _resolved[styleName] = null;
            return null;
        }

        var style = BuildCellStyle(element);
        StyleId? id = style.Equals(CellStyle.Default) ? null : workbook.RegisterStyle(style);
        _resolved[styleName] = id;
        return id;
    }

    private CellStyle BuildCellStyle(XElement element)
    {
        var style = new CellStyle();

        // Number format from the referenced data style.
        var dataStyleName = (string?)element.Attribute(Style + "data-style-name");
        if (dataStyleName is not null && _dataStyleCodes.TryGetValue(dataStyleName, out var code))
            style.NumberFormat = code;

        var cellProps = element.Element(Style + "table-cell-properties");
        if (cellProps is not null)
            ApplyCellProperties(style, cellProps);

        var paraProps = element.Element(Style + "paragraph-properties");
        if (paraProps is not null)
            ApplyParagraphProperties(style, paraProps);

        var textProps = element.Element(Style + "text-properties");
        if (textProps is not null)
            ApplyTextProperties(style, textProps);

        return style;
    }

    private void ApplyCellProperties(CellStyle style, XElement cellProps)
    {
        var bg = (string?)cellProps.Attribute(Fo + "background-color");
        if (bg is { Length: > 0 } && !bg.Equals("transparent", StringComparison.OrdinalIgnoreCase))
            style.FillColor = OdsBorder.ParseHexColor(bg);

        style.BorderTop = ReadBorder(cellProps, "border-top", Fo + "border-top");
        style.BorderRight = ReadBorder(cellProps, "border-right", Fo + "border-right");
        style.BorderBottom = ReadBorder(cellProps, "border-bottom", Fo + "border-bottom");
        style.BorderLeft = ReadBorder(cellProps, "border-left", Fo + "border-left");

        // Vertical alignment hint (exact enum) preferred.
        var valignHint = (string?)cellProps.Attribute(Style + "freex-valign");
        if (valignHint is not null && int.TryParse(valignHint, out var vi))
            style.VerticalAlignment = (VerticalAlignment)vi;
        else
        {
            var va = (string?)cellProps.Attribute(Style + "vertical-align");
            style.VerticalAlignment = va switch
            {
                "top" => VerticalAlignment.Top,
                "middle" => VerticalAlignment.Center,
                _ => VerticalAlignment.Bottom,
            };
        }

        var wrap = (string?)cellProps.Attribute(Style + "wrap-option");
        style.WrapText = string.Equals(wrap, "wrap", StringComparison.Ordinal);

        var rotationHint = (string?)cellProps.Attribute(Style + "freex-rotation");
        if (rotationHint is not null && int.TryParse(rotationHint, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rot))
            style.TextRotation = rot;
    }

    private void ApplyParagraphProperties(CellStyle style, XElement paraProps)
    {
        var halignHint = (string?)paraProps.Attribute(Style + "freex-halign");
        if (halignHint is not null && int.TryParse(halignHint, out var hi))
            style.HorizontalAlignment = (HorizontalAlignment)hi;
        else
        {
            var ha = (string?)paraProps.Attribute(Fo + "text-align");
            style.HorizontalAlignment = ha switch
            {
                "start" => HorizontalAlignment.Left,
                "center" => HorizontalAlignment.Center,
                "end" => HorizontalAlignment.Right,
                "justify" => HorizontalAlignment.Justify,
                _ => HorizontalAlignment.General,
            };
        }

        var indentHint = (string?)paraProps.Attribute(Style + "freex-indent");
        if (indentHint is not null && int.TryParse(indentHint, out var indent))
            style.IndentLevel = Math.Clamp(indent, 0, 15);
    }

    private void ApplyTextProperties(CellStyle style, XElement textProps)
    {
        var fontName = (string?)textProps.Attribute(Style + "font-name")
            ?? (string?)textProps.Attribute(Fo + "font-family");
        if (fontName is { Length: > 0 })
        {
            style.FontName = fontName;
            style.FontScheme = CellFontScheme.None;
        }

        var size = (string?)textProps.Attribute(Fo + "font-size");
        if (size is not null && TryParsePt(size, out var pt))
            style.FontSize = pt;

        var weight = (string?)textProps.Attribute(Fo + "font-weight");
        style.Bold = string.Equals(weight, "bold", StringComparison.Ordinal);

        var fontStyle = (string?)textProps.Attribute(Fo + "font-style");
        style.Italic = string.Equals(fontStyle, "italic", StringComparison.Ordinal);

        var underline = (string?)textProps.Attribute(Style + "text-underline-style");
        if (underline is { Length: > 0 } && !underline.Equals("none", StringComparison.Ordinal))
        {
            var underlineType = (string?)textProps.Attribute(Style + "text-underline-type");
            if (string.Equals(underlineType, "double", StringComparison.Ordinal))
                style.DoubleUnderline = true;
            else
                style.Underline = true;
        }

        var strike = (string?)textProps.Attribute(Style + "text-line-through-style");
        style.Strikethrough = strike is { Length: > 0 } && !strike.Equals("none", StringComparison.Ordinal);

        var color = (string?)textProps.Attribute(Fo + "color");
        if (color is { Length: > 0 } && color.StartsWith('#'))
            style.FontColor = OdsBorder.ParseHexColor(color);
    }

    private static CellBorder ReadBorder(XElement cellProps, string hintLocalName, XName foAttr)
    {
        var hint = (string?)cellProps.Attribute(Style + ("freex-" + hintLocalName));
        if (hint is { Length: > 0 })
            return OdsBorder.FromHint(hint);
        var fo = (string?)cellProps.Attribute(foAttr);
        if (fo is { Length: > 0 } && !fo.Equals("none", StringComparison.Ordinal))
            return OdsBorder.FromOdf(fo);
        return default;
    }

    // ---- unit parsing ----------------------------------------------------------------------------

    private static bool TryReadCharWidth(XElement colProps, out double width)
    {
        width = 0;
        var hint = (string?)colProps.Attribute(Style + "freex-width-chars");
        if (hint is not null && double.TryParse(hint, NumberStyles.Float, CultureInfo.InvariantCulture, out width))
            return true;
        var cm = (string?)colProps.Attribute(Style + "column-width");
        if (cm is not null && TryParseCm(cm, out var px))
        {
            width = Math.Max(0, (px - 5.0) / 7.0);
            return true;
        }
        return false;
    }

    private static bool TryReadPxHeight(XElement rowProps, out double height)
    {
        height = 0;
        var hint = (string?)rowProps.Attribute(Style + "freex-height-px");
        if (hint is not null && double.TryParse(hint, NumberStyles.Float, CultureInfo.InvariantCulture, out height))
            return true;
        var cm = (string?)rowProps.Attribute(Style + "row-height");
        if (cm is not null && TryParseCm(cm, out var px))
        {
            height = px;
            return true;
        }
        return false;
    }

    private static bool TryParsePt(string value, out double pt)
    {
        pt = 0;
        if (value.EndsWith("pt", StringComparison.Ordinal))
            return double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out pt);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out pt);
    }

    private static bool TryParseCm(string value, out double px)
    {
        px = 0;
        double cm;
        if (value.EndsWith("cm", StringComparison.Ordinal))
        {
            if (!double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out cm))
                return false;
        }
        else if (value.EndsWith("mm", StringComparison.Ordinal))
        {
            if (!double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var mm))
                return false;
            cm = mm / 10.0;
        }
        else if (value.EndsWith("in", StringComparison.Ordinal))
        {
            if (!double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var inch))
                return false;
            cm = inch * 2.54;
        }
        else
        {
            return false;
        }
        px = cm * 37.795275591;
        return true;
    }

    private static bool IsDataStyle(XName name) =>
        name == Number + "number-style" ||
        name == Number + "percentage-style" ||
        name == Number + "currency-style" ||
        name == Number + "date-style" ||
        name == Number + "time-style" ||
        name == Number + "boolean-style" ||
        name == Number + "text-style";

    /// <summary>Best-effort code inference when no freex hint exists (foreign files).</summary>
    private static string? InferFormatCode(XElement dataStyle)
    {
        if (dataStyle.Name == Number + "percentage-style")
            return "0%";
        if (dataStyle.Name == Number + "currency-style")
            return "$#,##0.00";
        if (dataStyle.Name == Number + "date-style")
            return "yyyy-mm-dd";
        var numberElement = dataStyle.Element(Number + "number");
        if (numberElement is not null)
        {
            var decimals = (string?)numberElement.Attribute(Number + "decimal-places");

            // r492: `d > 0` is a lower bound only, and this value is chosen by the FILE, so
            // decimal-places="2000000000" asked for a two-billion-character string -- 4 GB, thrown
            // as an OutOfMemoryException while merely opening the document. Clamp to the 30 places
            // Excel's number formats allow, which is the same bound FormatCellsNumberFormatPlanner
            // already applies to the value coming from the Format Cells dialog.
            if (decimals is not null && int.TryParse(decimals, out var d) && d > 0)
                return "0." + new string('0', Math.Min(d, MaxDecimalPlaces));
            return "0";
        }
        return null;
    }
}
