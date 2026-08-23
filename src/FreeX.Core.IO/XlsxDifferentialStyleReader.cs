using FreeX.Core.Model;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxDifferentialStyleReader
{
    public static IReadOnlyList<CellStyle> ReadAll(
        ZipArchive archive,
        XNamespace workbookNs,
        WorkbookTheme? theme = null,
        WorkbookIndexedColorPalette? indexedColors = null)
    {
        var stylesEntry = archive.GetEntry("xl/styles.xml");
        if (stylesEntry is null)
            return [];

        try
        {
            var stylesXml = LoadXml(stylesEntry);
            return ReadAll(stylesXml, workbookNs, theme, indexedColors);
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<CellStyle> ReadAll(
        XDocument? stylesXml,
        XNamespace workbookNs,
        WorkbookTheme? theme = null,
        WorkbookIndexedColorPalette? indexedColors = null)
    {
        try
        {
            return stylesXml?.Root?
                .Element(workbookNs + "dxfs")?
                .Elements(workbookNs + "dxf")
                .Select(dxf => Read(dxf, workbookNs, theme, indexedColors))
                .ToList()
                ?? [];
        }
        catch
        {
            return [];
        }
    }

    // Reads a single <dxf> into its modeled CellStyle (including captured native metadata). Exposed so
    // the stylesheet metadata preserver can compare two dxfs' visible styles before merging them. Theme
    // and indexed-color resolution is optional here: the preserver only compares two dxfs read the same
    // way for equivalence, so a missing theme/palette does not affect that comparison.
    internal static CellStyle ReadDifferentialStyle(
        XElement dxf,
        XNamespace workbookNs,
        WorkbookTheme? theme = null,
        WorkbookIndexedColorPalette? indexedColors = null) =>
        Read(dxf, workbookNs, theme, indexedColors);

    private static CellStyle Read(
        XElement dxf,
        XNamespace workbookNs,
        WorkbookTheme? theme,
        WorkbookIndexedColorPalette? indexedColors)
    {
        var style = new CellStyle();
        var font = dxf.Element(workbookNs + "font");
        if (font is not null)
        {
            // ECMA-376 CT_BooleanProperty semantics (b/i/strike): the element's mere presence means
            // "on" only when it carries no val attribute; an explicit val="0"/"false" means the dxf
            // is turning that toggle OFF (e.g. a conditional-format rule that un-bolds matching
            // cells). Mirrors XlsxStructuredTableStyleMetadataReader.ReadDifferentialStyleDiff, which
            // reads the same dxf font shape correctly via ReadBoolAttribute(defaultValue: true).
            if (font.Element(workbookNs + "b") is { } boldElement)
            {
                style.Bold = XlsxXmlAttributeReader.ReadBoolAttribute(boldElement, "val", defaultValue: true);
                // Record the dxf's explicit on/off decision separately from the plain bool above so a
                // conditional-format merge can tell "this dxf turns bold off" apart from "this dxf never
                // mentions bold" (both of which read as Bold=false). See CellStyle.DxfBold.
                style.DxfBold = style.Bold;
            }
            if (font.Element(workbookNs + "i") is { } italicElement)
            {
                style.Italic = XlsxXmlAttributeReader.ReadBoolAttribute(italicElement, "val", defaultValue: true);
                style.DxfItalic = style.Italic;
            }
            var underlineElement = font.Element(workbookNs + "u");
            var underlineVal = underlineElement?.Attribute("val")?.Value;
            // CT_UnderlineProperty's val is an enum (single/double/.../none), not a plain boolean --
            // val="none" is the explicit "turn underline off" form, so it must not be read as "on".
            style.Underline = underlineElement is not null
                && !string.Equals(underlineVal, "none", StringComparison.OrdinalIgnoreCase);
            if (underlineElement is not null)
                style.DxfUnderline = style.Underline;
            style.DoubleUnderline = string.Equals(underlineVal, "double", StringComparison.OrdinalIgnoreCase)
                || string.Equals(underlineVal, "doubleAccounting", StringComparison.OrdinalIgnoreCase);
            if (font.Element(workbookNs + "strike") is { } strikeElement)
            {
                style.Strikethrough = XlsxXmlAttributeReader.ReadBoolAttribute(strikeElement, "val", defaultValue: true);
                style.DxfStrikethrough = style.Strikethrough;
            }
            var verticalAlignment = font.Element(workbookNs + "vertAlign")?.Attribute("val")?.Value;
            style.Superscript = string.Equals(verticalAlignment, "superscript", StringComparison.OrdinalIgnoreCase);
            style.Subscript = string.Equals(verticalAlignment, "subscript", StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(font.Element(workbookNs + "sz")?.Attribute("val")?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) &&
                IsSupportedFontSize(size))
            {
                style.FontSize = size;
            }

            var fontName = font.Element(workbookNs + "name")?.Attribute("val")?.Value;
            if (!string.IsNullOrWhiteSpace(fontName))
                style.FontName = fontName;

            if (TryReadColor(font.Element(workbookNs + "color"), theme, indexedColors, out var fontColor, out var fontThemeColor))
            {
                style.FontColor = fontColor;
                // Record that this dxf explicitly specified a font color (even black) separately
                // from the plain FontColor above, which cannot distinguish an explicit black choice
                // from "never mentioned". See CellStyle.DxfFontColor.
                style.DxfFontColor = fontColor;
                // Preserve the theme link (R120-cf-theme-color-1: a CF rule's font color picked from
                // the theme gallery must keep following theme changes, not just the RGB Excel baked
                // in at author time) so ViewportConditionalFormatEvaluator/writer can re-resolve or
                // round-trip it, mirroring FontThemeColor on the plain (non-CF) style path.
                style.FontThemeColor = fontThemeColor;
            }
        }

        var patternFill = dxf
            .Element(workbookNs + "fill")?
            .Element(workbookNs + "patternFill");
        if (patternFill is not null)
        {
            style.FillPatternStyle = XlsxFillPatternCodec.FromToken(patternFill.Attribute("patternType")?.Value);
            if (TryReadColor(patternFill.Element(workbookNs + "bgColor"), theme, indexedColors, out var backgroundColor, out var backgroundThemeColor))
            {
                style.FillColor = backgroundColor;
                style.FillThemeColor = backgroundThemeColor;
            }
            if (TryReadColor(patternFill.Element(workbookNs + "fgColor"), theme, indexedColors, out var foregroundColor, out var foregroundThemeColor))
            {
                if (style.FillPatternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
                {
                    style.FillColor = foregroundColor;
                    style.FillThemeColor = foregroundThemeColor;
                }
                else
                {
                    style.FillPatternColor = foregroundColor;
                    style.FillPatternThemeColor = foregroundThemeColor;
                }
            }
        }

        var border = dxf.Element(workbookNs + "border");
        if (border is not null)
        {
            style.BorderTop = ReadBorder(border.Element(workbookNs + "top"), workbookNs, theme, indexedColors);
            style.BorderRight = ReadBorder(border.Element(workbookNs + "right"), workbookNs, theme, indexedColors);
            style.BorderBottom = ReadBorder(border.Element(workbookNs + "bottom"), workbookNs, theme, indexedColors);
            style.BorderLeft = ReadBorder(border.Element(workbookNs + "left"), workbookNs, theme, indexedColors);
            var diagBorder = ReadBorder(border.Element(workbookNs + "diagonal"), workbookNs, theme, indexedColors);
            if (diagBorder.Style != BorderStyle.None)
            {
                style.BorderDiagonalDown = border.Attribute("diagonalDown")?.Value is "1" or "true" ? diagBorder : default;
                style.BorderDiagonalUp = border.Attribute("diagonalUp")?.Value is "1" or "true" ? diagBorder : default;
            }
        }

        var numberFormat = dxf.Element(workbookNs + "numFmt")?.Attribute("formatCode")?.Value;
        if (!string.IsNullOrWhiteSpace(numberFormat))
            style.NumberFormat = numberFormat;

        var nativeAttributes = ReadNativeAttributes(dxf);
        if (nativeAttributes.Count > 0)
            style.NativeDifferentialAttributes = nativeAttributes;

        var nativeChildXmls = ReadNativeChildXmls(dxf, workbookNs);
        if (nativeChildXmls.Count > 0)
            style.NativeDifferentialChildXmls = nativeChildXmls;

        var nativeElementXmls = ReadNativeElementXmls(dxf, workbookNs);
        if (nativeElementXmls.Count > 0)
            style.NativeDifferentialElementXmls = nativeElementXmls;

        return style;
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        return XlsxPackageXmlEditor.LoadXml(entry);
    }

    private static Dictionary<string, string> ReadNativeAttributes(XElement dxf) =>
        dxf.Attributes()
            .Where(attribute => attribute.Name.NamespaceName.Length == 0)
            .ToDictionary(attribute => attribute.Name.LocalName, attribute => attribute.Value, StringComparer.Ordinal);

    private static List<string> ReadNativeChildXmls(XElement dxf, XNamespace workbookNs)
    {
        var modeledChildren = ModeledChildren(workbookNs);
        return dxf.Elements()
            .Where(element => !modeledChildren.Contains(element.Name))
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToList();
    }

    private static Dictionary<string, string> ReadNativeElementXmls(XElement dxf, XNamespace workbookNs)
    {
        var modeledChildren = ModeledChildren(workbookNs);
        return dxf.Elements()
            .Where(element => modeledChildren.Contains(element.Name))
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().ToString(SaveOptions.DisableFormatting),
                StringComparer.Ordinal);
    }

    private static XName[] ModeledChildren(XNamespace workbookNs) =>
    [
        workbookNs + "font",
        workbookNs + "numFmt",
        workbookNs + "fill",
        workbookNs + "alignment",
        workbookNs + "border",
        workbookNs + "protection"
    ];

    private static CellBorder ReadBorder(
        XElement? edge,
        XNamespace workbookNs,
        WorkbookTheme? theme,
        WorkbookIndexedColorPalette? indexedColors)
    {
        if (edge is null)
            return default;

        var style = edge.Attribute("style")?.Value switch
        {
            "thin" => BorderStyle.Thin,
            "medium" => BorderStyle.Medium,
            "thick" => BorderStyle.Thick,
            "dashed" => BorderStyle.Dashed,
            "dotted" => BorderStyle.Dotted,
            "double" => BorderStyle.Double,
            "hair" => BorderStyle.Hair,
            "slantDashDot" => BorderStyle.SlantDashDot,
            "mediumDashed" => BorderStyle.MediumDashed,
            "dashDot" => BorderStyle.DashDot,
            "mediumDashDot" => BorderStyle.MediumDashDot,
            "dashDotDot" => BorderStyle.DashDotDot,
            "mediumDashDotDot" => BorderStyle.MediumDashDotDot,
            _ => BorderStyle.None
        };
        if (style == BorderStyle.None)
            return default;

        var hasColor = TryReadColor(edge.Element(workbookNs + "color"), theme, indexedColors, out var color, out var themeColor);
        return new CellBorder(style, hasColor ? color : CellColor.Black, themeColor);
    }

    // Resolves a dxf color element the same way the normal (non-conditional-format) style path does:
    // literal rgb first, then theme (with tint) when a theme/indexedColors context is supplied, then the
    // legacy indexed palette. Falls back to the rgb-only 2-arg reader when no theme context is available
    // (e.g. the stylesheet metadata preserver's dxf-equivalence comparison, which doesn't need resolved
    // colors — only that both sides are read identically). Also returns the theme slot+tint via
    // themeColorReference (null for sRGB/indexed colors) so callers can preserve the theme link instead of
    // only keeping the baked RGB -- see CellStyle.FontThemeColor/FillThemeColor/FillPatternThemeColor and
    // CellBorder.ThemeColor (R120-cf-theme-color-1).
    private static bool TryReadColor(
        XElement? element,
        WorkbookTheme? theme,
        WorkbookIndexedColorPalette? indexedColors,
        out CellColor color,
        out WorkbookThemeColorReference? themeColorReference)
    {
        if (theme is not null && indexedColors is not null)
            return XlsxColorReader.TryReadCellColorWithThemeReference(element, theme, indexedColors, out color, out themeColorReference);

        themeColorReference = null;
        return XlsxColorReader.TryReadCellColor(element, out color);
    }

    private static bool IsSupportedFontSize(double fontSize) =>
        fontSize >= 1 && fontSize <= 409;

}
