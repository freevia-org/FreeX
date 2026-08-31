using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxStructuredTableStyleMetadataWriter
{
    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        Save(archive, workbook);
    }

    private static void Save(ZipArchive archive, Workbook workbook)
    {
        if (workbook.StructuredTableStyles.Count == 0)
            return;

        var stylesEntry = archive.GetEntry("xl/styles.xml");
        if (stylesEntry is null)
            return;

        var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
        var targetRoot = stylesXml.Root;
        if (targetRoot is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var existingTableStyles = targetRoot.Element(workbookNs + "tableStyles");
        var tableStyles = existingTableStyles ?? new XElement(workbookNs + "tableStyles");

        // dxfId values baked into a table style's NativeXml are tied to the SOURCE file's <dxfs>
        // array. ClosedXML (plus XlsxAdvancedConditionalFormatWriter, which runs earlier in the save
        // pipeline) regenerates <dxfs> from scratch on a full save, containing only CF-tracked
        // differential styles -- the table style's original dxf entries are dropped. Re-emitting the
        // stale dxfId verbatim would silently repoint the table's color at an unrelated CF color, so
        // every table-style dxfId must be remapped against the CURRENT <dxfs> array before writing.
        var existingDxfs = targetRoot.Element(workbookNs + "dxfs");
        var dxfs = existingDxfs ?? new XElement(workbookNs + "dxfs");
        if (existingDxfs is null)
        {
            // CT_Stylesheet requires dxfs to precede tableStyles.
            if (existingTableStyles is not null)
                existingTableStyles.AddBeforeSelf(dxfs);
            else
                targetRoot.Add(dxfs);
        }

        if (existingTableStyles is null)
            targetRoot.Add(tableStyles);

        var existingStylesByName = tableStyles
            .Elements(workbookNs + "tableStyle")
            .Select(element => (Name: element.Attribute("name")?.Value, Element: element))
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Name))
            .ToDictionary(pair => pair.Name!, pair => pair.Element, StringComparer.OrdinalIgnoreCase);

        foreach (var style in workbook.StructuredTableStyles.Where(style => !string.IsNullOrWhiteSpace(style.Name)))
        {
            var styleXml = ToTableStyleXml(style, workbookNs, dxfs);
            if (styleXml is null)
                continue;

            if (existingStylesByName.TryGetValue(style.Name, out var existingStyle))
                existingStyle.ReplaceWith(styleXml);
            else
                tableStyles.Add(styleXml);
        }

        tableStyles.SetAttributeValue(
            "count",
            tableStyles.Elements(workbookNs + "tableStyle").Count().ToString(CultureInfo.InvariantCulture));
        dxfs.SetAttributeValue(
            "count",
            dxfs.Elements(workbookNs + "dxf").Count().ToString(CultureInfo.InvariantCulture));
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
    }

    private static XElement? ToTableStyleXml(StructuredTableStyleModel style, XNamespace workbookNs, XElement dxfs)
    {
        var nativeStyle = TryParseNativeTableStyleXml(style, workbookNs, dxfs);
        if (nativeStyle is not null)
            return nativeStyle;

        return new XElement(
            workbookNs + "tableStyle",
            new XAttribute("name", style.Name),
            new XAttribute("pivot", style.AppliesToPivotTables ? "1" : "0"),
            new XAttribute("table", style.AppliesToTables ? "1" : "0"),
            new XAttribute("count", "0"));
    }

    private static XElement? TryParseNativeTableStyleXml(StructuredTableStyleModel style, XNamespace workbookNs, XElement dxfs)
    {
        if (string.IsNullOrWhiteSpace(style.NativeXml))
            return null;

        try
        {
            var nativeStyle = XElement.Parse(style.NativeXml);
            if (nativeStyle.Name == workbookNs + "tableStyle" &&
                string.Equals(nativeStyle.Attribute("name")?.Value, style.Name, StringComparison.Ordinal))
            {
                var clone = new XElement(nativeStyle);
                RemapDifferentialFormatIds(clone, style, workbookNs, dxfs);
                return clone;
            }
        }
        catch
        {
            // Ignore malformed authored table-style payloads and fall back to a minimal style shell.
        }

        return null;
    }

    /// <summary>
    /// Remaps each &lt;tableStyleElement dxfId="..."/&gt; captured verbatim in the table style's
    /// NativeXml against the CURRENT (possibly freshly-regenerated) &lt;dxfs&gt; array, instead of
    /// trusting the stale index tied to the source file's dxfs. Mirrors the intent of
    /// <see cref="XlsxAdvancedConditionalFormatWriter"/>'s own dxfId map for conditional formats: a
    /// fresh dxf entry is appended (rebuilt from the <see cref="StyleDiff"/> the reader captured per
    /// element at load time) and the element's dxfId is repointed at it. When no StyleDiff was
    /// captured for an element (unsupported semantic type, or a malformed/out-of-range source dxfId),
    /// the stale dxfId is dropped rather than risk silently repointing the table's color at an
    /// unrelated dxf that now happens to occupy that index.
    /// </summary>
    private static void RemapDifferentialFormatIds(
        XElement tableStyleXml,
        StructuredTableStyleModel style,
        XNamespace workbookNs,
        XElement dxfs)
    {
        foreach (var element in tableStyleXml.Elements(workbookNs + "tableStyleElement"))
        {
            var dxfIdAttribute = element.Attribute("dxfId");
            if (dxfIdAttribute is null)
                continue;

            var type = element.Attribute("type")?.Value;
            var modelElement = string.IsNullOrWhiteSpace(type)
                ? null
                : style.Elements.FirstOrDefault(e => string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase));

            if (modelElement?.Format is not { } diff)
            {
                dxfIdAttribute.Remove();
                continue;
            }

            var newDxfId = dxfs.Elements(workbookNs + "dxf").Count();
            dxfs.Add(BuildDxfElement(diff, workbookNs));
            element.SetAttributeValue("dxfId", newDxfId.ToString(CultureInfo.InvariantCulture));
        }
    }

    // A dxf numFmt's numFmtId only needs to disambiguate format codes within that single dxf element
    // (it is never looked up against the workbook's shared <numFmts> collection), so a fixed
    // custom-range id is safe here -- mirrors XlsxAdvancedConditionalFormatWriter's MinCustomNumFmtId.
    private const int TableStyleDxfNumFmtId = 164;

    private static XElement BuildDxfElement(StyleDiff diff, XNamespace workbookNs)
    {
        XElement? font = null;
        if (diff.Bold is true || diff.Italic is true || diff.Underline is true ||
            diff.FontColor is not null || diff.FontName is not null || diff.FontSize is not null)
        {
            font = new XElement(
                workbookNs + "font",
                diff.Bold is true ? new XElement(workbookNs + "b") : null,
                diff.Italic is true ? new XElement(workbookNs + "i") : null,
                diff.Underline is true ? new XElement(workbookNs + "u") : null,
                diff.FontSize is { } fontSize
                    ? new XElement(workbookNs + "sz", new XAttribute("val", fontSize.ToString(CultureInfo.InvariantCulture)))
                    : null,
                diff.FontColor is { } fontColor
                    ? ToDxfColorElement(workbookNs, "color", fontColor, diff.FontThemeColor)
                    : null,
                diff.FontName is { } fontName
                    ? new XElement(workbookNs + "name", new XAttribute("val", fontName))
                    : null);
        }

        XElement? numFmt = diff.NumberFormat is { } numberFormat
            ? new XElement(
                workbookNs + "numFmt",
                new XAttribute("numFmtId", TableStyleDxfNumFmtId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("formatCode", numberFormat))
            : null;

        XElement? fill = null;
        if (diff.FillColor is not null || diff.FillPatternStyle is not null || diff.FillPatternColor is not null)
        {
            var patternStyle = diff.FillPatternStyle ?? CellFillPatternStyle.None;
            var patternFill = new XElement(
                workbookNs + "patternFill",
                new XAttribute("patternType", ToPatternType(patternStyle)));

            if (patternStyle is CellFillPatternStyle.None or CellFillPatternStyle.Solid)
            {
                if (diff.FillColor is { } fg)
                    patternFill.Add(ToDxfColorElement(workbookNs, "fgColor", fg, diff.FillThemeColor));
                patternFill.Add(new XElement(workbookNs + "bgColor", new XAttribute("indexed", "64")));
            }
            else
            {
                if (diff.FillPatternColor is { } fg)
                    patternFill.Add(ToDxfColorElement(workbookNs, "fgColor", fg, diff.FillPatternThemeColor));
                if (diff.FillColor is { } bg)
                    patternFill.Add(ToDxfColorElement(workbookNs, "bgColor", bg, diff.FillThemeColor));
            }

            fill = new XElement(workbookNs + "fill", patternFill);
        }

        XElement? border = null;
        if (diff.BorderTop is not null || diff.BorderRight is not null ||
            diff.BorderBottom is not null || diff.BorderLeft is not null)
        {
            border = new XElement(
                workbookNs + "border",
                BuildBorderEdgeElement("top", diff.BorderTop, workbookNs),
                BuildBorderEdgeElement("right", diff.BorderRight, workbookNs),
                BuildBorderEdgeElement("bottom", diff.BorderBottom, workbookNs),
                BuildBorderEdgeElement("left", diff.BorderLeft, workbookNs));
        }

        return new XElement(workbookNs + "dxf", font, numFmt, fill, border);
    }

    private static XElement? BuildBorderEdgeElement(string edgeName, CellBorder? edge, XNamespace workbookNs)
    {
        if (edge is not { Style: not BorderStyle.None } border)
            return null;

        return new XElement(
            workbookNs + edgeName,
            new XAttribute("style", ToBorderStyle(border.Style)),
            ToDxfColorElement(workbookNs, "color", border.Color, border.ThemeColor));
    }

    /// <summary>
    /// freex-table-style-theme-color-1: XLSX <em>does</em> carry theme colours, so a themed table-style
    /// dxf must re-emit its <c>&lt;color theme="n" tint="t"/&gt;</c> link rather than the RGB it happens
    /// to resolve to — otherwise every open→save permanently baked the style and its banding stopped
    /// following Theme Colors while the surrounding cells still did. Mirrors the conditional-formatting
    /// dxf writer's ToDifferentialColorXml, which this path had diverged from.
    /// </summary>
    private static XElement ToDxfColorElement(
        XNamespace workbookNs,
        string elementName,
        CellColor color,
        WorkbookThemeColorReference? themeColor)
    {
        if (themeColor is not { } link)
            return new XElement(workbookNs + elementName, new XAttribute("rgb", ToArgb(color)));

        var element = new XElement(
            workbookNs + elementName,
            new XAttribute(
                "theme",
                XlsxColorReader.ThemeColorIndex(link.Slot).ToString(CultureInfo.InvariantCulture)));
        if (Math.Abs(link.Tint) >= 0.000001)
            element.SetAttributeValue("tint", link.Tint.ToString("G17", CultureInfo.InvariantCulture));
        return element;
    }

    private static string ToBorderStyle(BorderStyle style) =>
        style switch
        {
            BorderStyle.Thin => "thin",
            BorderStyle.Medium => "medium",
            BorderStyle.Thick => "thick",
            BorderStyle.Dashed => "dashed",
            BorderStyle.Dotted => "dotted",
            BorderStyle.Double => "double",
            BorderStyle.Hair => "hair",
            BorderStyle.SlantDashDot => "slantDashDot",
            BorderStyle.MediumDashed => "mediumDashed",
            BorderStyle.DashDot => "dashDot",
            BorderStyle.MediumDashDot => "mediumDashDot",
            BorderStyle.DashDotDot => "dashDotDot",
            BorderStyle.MediumDashDotDot => "mediumDashDotDot",
            _ => "none"
        };

    private static string ToArgb(CellColor color) =>
        $"FF{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string ToPatternType(CellFillPatternStyle style) =>
        style switch
        {
            CellFillPatternStyle.Gray0625 => "gray0625",
            CellFillPatternStyle.Gray125 => "gray125",
            CellFillPatternStyle.LightGray => "lightGray",
            CellFillPatternStyle.MediumGray => "mediumGray",
            CellFillPatternStyle.DarkGray => "darkGray",
            CellFillPatternStyle.LightHorizontal => "lightHorizontal",
            CellFillPatternStyle.LightVertical => "lightVertical",
            CellFillPatternStyle.LightDown => "lightDown",
            CellFillPatternStyle.LightUp => "lightUp",
            CellFillPatternStyle.LightGrid => "lightGrid",
            CellFillPatternStyle.LightTrellis => "lightTrellis",
            CellFillPatternStyle.DarkHorizontal => "darkHorizontal",
            CellFillPatternStyle.DarkVertical => "darkVertical",
            CellFillPatternStyle.DarkDown => "darkDown",
            CellFillPatternStyle.DarkUp => "darkUp",
            CellFillPatternStyle.DarkGrid => "darkGrid",
            CellFillPatternStyle.DarkTrellis => "darkTrellis",
            _ => "solid"
        };
}
