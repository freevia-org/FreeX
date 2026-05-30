using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookThemeWriter
{
    public static void Save(Stream xlsxStream, WorkbookTheme theme)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        const string themePath = "xl/theme/theme1.xml";
        archive.GetEntry(themePath)?.Delete();
        var themeEntry = archive.CreateEntry(themePath);
        using var stream = themeEntry.Open();
        ToThemeXml(theme).Save(stream);
    }

    private static XDocument ToThemeXml(WorkbookTheme theme)
    {
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(drawingNs + "theme",
                new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                new XAttribute("name", theme.Name),
                new XElement(drawingNs + "themeElements",
                    CreateColorSchemeElement(theme, drawingNs),
                    CreateFontSchemeElement(theme, drawingNs),
                    CreateFormatSchemeElement(theme, drawingNs)),
                CreateThemeSupplementElements(theme, drawingNs)));
    }

    private static XElement CreateColorSchemeElement(WorkbookTheme theme, XNamespace drawingNs)
    {
        if (!string.IsNullOrWhiteSpace(theme.NativeColorSchemeXml))
        {
            try
            {
                var colorScheme = XElement.Parse(theme.NativeColorSchemeXml);
                if (colorScheme.Name == drawingNs + "clrScheme")
                    return new XElement(colorScheme);
            }
            catch
            {
                // Fall back to modeled colors when native theme XML is malformed.
            }
        }

        return new XElement(drawingNs + "clrScheme",
            new XAttribute("name", $"{theme.Name} Colors"),
            XlsxWorkbookThemeReader.ColorElements.Select(color =>
                new XElement(drawingNs + color.ElementName,
                    new XElement(drawingNs + "srgbClr",
                        new XAttribute("val", FormatColor(theme.GetColor(color.Slot)))))));
    }

    private static XElement CreateFontSchemeElement(WorkbookTheme theme, XNamespace drawingNs)
    {
        if (!string.IsNullOrWhiteSpace(theme.NativeFontSchemeXml))
        {
            try
            {
                var fontScheme = XElement.Parse(theme.NativeFontSchemeXml);
                if (fontScheme.Name == drawingNs + "fontScheme")
                    return new XElement(fontScheme);
            }
            catch
            {
                // Fall back to modeled fonts when native theme XML is malformed.
            }
        }

        return new XElement(drawingNs + "fontScheme",
            new XAttribute("name", $"{theme.Name} Fonts"),
            new XElement(drawingNs + "majorFont",
                new XElement(drawingNs + "latin",
                    new XAttribute("typeface", theme.MajorFontName))),
            new XElement(drawingNs + "minorFont",
                new XElement(drawingNs + "latin",
                    new XAttribute("typeface", theme.MinorFontName))));
    }

    private static XElement CreateFormatSchemeElement(WorkbookTheme theme, XNamespace drawingNs)
    {
        if (!string.IsNullOrWhiteSpace(theme.NativeFormatSchemeXml))
        {
            try
            {
                var formatScheme = XElement.Parse(theme.NativeFormatSchemeXml);
                if (formatScheme.Name == drawingNs + "fmtScheme")
                    return new XElement(formatScheme);
            }
            catch
            {
                // Fall back to the modeled effect name when native theme XML is malformed.
            }
        }

        return new XElement(drawingNs + "fmtScheme",
            new XAttribute("name", theme.EffectsName));
    }

    private static string FormatColor(CellColor color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}";

    private static IEnumerable<XElement> CreateThemeSupplementElements(WorkbookTheme theme, XNamespace drawingNs)
    {
        var elements = new List<XElement>();
        if (string.IsNullOrWhiteSpace(theme.NativeThemeSupplementXml))
            return CreateModeledThemeSupplementElements(theme, drawingNs);

        try
        {
            using var stringReader = new StringReader($"<themeSupplement>{theme.NativeThemeSupplementXml}</themeSupplement>");
            using var xmlReader = XmlReader.Create(
                stringReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            var document = XDocument.Load(xmlReader);
            elements.AddRange(document.Root!
                .Elements()
                .Where(element => IsSupportedThemeSupplementElement(element, drawingNs))
                .Select(element => new XElement(element)));
        }
        catch
        {
            return CreateModeledThemeSupplementElements(theme, drawingNs);
        }

        if (!elements.Any(element => element.Name == drawingNs + "extraClrSchemeLst"))
            elements.AddRange(CreateAlternateColorSchemeListElement(theme, drawingNs));
        if (!elements.Any(element => element.Name == drawingNs + "objectDefaults"))
            elements.AddRange(CreateObjectDefaultsElement(theme.ObjectDefaults, drawingNs));

        return elements;
    }

    private static bool IsSupportedThemeSupplementElement(XElement element, XNamespace drawingNs) =>
        element.Name.Namespace == drawingNs
        && element.Name != drawingNs + "themeElements";

    private static IEnumerable<XElement> CreateModeledThemeSupplementElements(WorkbookTheme theme, XNamespace drawingNs) =>
        CreateObjectDefaultsElement(theme.ObjectDefaults, drawingNs)
            .Concat(CreateAlternateColorSchemeListElement(theme, drawingNs));

    private static IEnumerable<XElement> CreateAlternateColorSchemeListElement(WorkbookTheme theme, XNamespace drawingNs)
    {
        if (theme.AlternateColorSchemes is not { Count: > 0 })
            return [];

        return
        [
            new XElement(drawingNs + "extraClrSchemeLst",
                theme.AlternateColorSchemes.Select(scheme =>
                    new XElement(drawingNs + "extraClrScheme",
                        CreateAlternateColorSchemeElement(scheme, drawingNs))))
        ];
    }

    private static XElement CreateAlternateColorSchemeElement(
        WorkbookThemeAlternateColorScheme scheme,
        XNamespace drawingNs)
    {
        if (!string.IsNullOrWhiteSpace(scheme.NativeColorSchemeXml))
        {
            try
            {
                var colorScheme = XElement.Parse(scheme.NativeColorSchemeXml);
                if (colorScheme.Name == drawingNs + "clrScheme")
                    return new XElement(colorScheme);
            }
            catch
            {
                // Fall back to the parsed alternate colors when native XML is malformed.
            }
        }

        return new XElement(drawingNs + "clrScheme",
            new XAttribute("name", string.IsNullOrWhiteSpace(scheme.Name) ? "Alternate Colors" : scheme.Name),
            XlsxWorkbookThemeReader.ColorElements
                .Where(color => scheme.Colors.ContainsKey(color.Slot))
                .Select(color =>
                    new XElement(drawingNs + color.ElementName,
                        new XElement(drawingNs + "srgbClr",
                            new XAttribute("val", FormatColor(scheme.Colors[color.Slot]))))));
    }

    private static IEnumerable<XElement> CreateObjectDefaultsElement(
        WorkbookThemeObjectDefaults? defaults,
        XNamespace drawingNs)
    {
        if (defaults is null)
            return [];

        if (!string.IsNullOrWhiteSpace(defaults.NativeObjectDefaultsXml))
        {
            try
            {
                var objectDefaults = XElement.Parse(defaults.NativeObjectDefaultsXml);
                if (objectDefaults.Name == drawingNs + "objectDefaults")
                    return [new XElement(objectDefaults)];
            }
            catch
            {
                // Fall back to modeled defaults when native object defaults XML is malformed.
            }
        }

        if (!defaults.HasModeledDefaults)
            return [new XElement(drawingNs + "objectDefaults")];

        return
        [
            new XElement(drawingNs + "objectDefaults",
                CreateShapeDefaultElement(defaults.Shape, drawingNs),
                CreateLineDefaultElement(defaults.Line, drawingNs),
                CreateTextDefaultElement(defaults.Text, drawingNs))
        ];
    }

    private static XElement? CreateShapeDefaultElement(
        WorkbookThemeShapeObjectDefault? shape,
        XNamespace drawingNs)
    {
        if (shape is null)
            return null;

        var shapeProperties = new XElement(drawingNs + "spPr",
            ToSolidFill(shape.FillThemeColor, shape.FillColor, drawingNs),
            ToLineProperties(shape.OutlineThemeColor, shape.OutlineColor, shape.OutlineWidthPoints, drawingNs));

        return shapeProperties.HasElements
            ? new XElement(drawingNs + "spDef", shapeProperties)
            : null;
    }

    private static XElement? CreateLineDefaultElement(
        WorkbookThemeLineObjectDefault? line,
        XNamespace drawingNs)
    {
        if (line is null)
            return null;

        var lineProperties = ToLineProperties(line.StrokeThemeColor, line.StrokeColor, line.StrokeWidthPoints, drawingNs);
        return lineProperties is null
            ? null
            : new XElement(drawingNs + "lnDef",
                new XElement(drawingNs + "spPr", lineProperties));
    }

    private static XElement? CreateTextDefaultElement(
        WorkbookThemeTextObjectDefault? text,
        XNamespace drawingNs)
    {
        if (text is null)
            return null;

        var runPropertiesChildren = new List<object>();
        var fill = ToSolidFill(text.TextThemeColor, text.TextColor, drawingNs);
        if (fill is not null)
            runPropertiesChildren.Add(fill);
        if (!string.IsNullOrWhiteSpace(text.Typeface))
            runPropertiesChildren.Add(new XElement(drawingNs + "latin", new XAttribute("typeface", text.Typeface)));

        return runPropertiesChildren.Count == 0
            ? null
            : new XElement(drawingNs + "txDef",
                new XElement(drawingNs + "spPr"),
                new XElement(drawingNs + "bodyPr"),
                new XElement(drawingNs + "lstStyle",
                    new XElement(drawingNs + "defRPr", runPropertiesChildren)));
    }

    private static XElement? ToLineProperties(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        double? widthPoints,
        XNamespace drawingNs)
    {
        var fill = ToSolidFill(themeColor, color, drawingNs);
        if (fill is null && widthPoints is null)
            return null;

        var line = new XElement(drawingNs + "ln", fill);
        if (widthPoints is > 0)
            line.SetAttributeValue("w", (int)Math.Round(widthPoints.Value * 12700.0));
        return line;
    }

    private static XElement? ToSolidFill(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs)
    {
        XElement? colorElement = null;
        if (themeColor is { } theme)
        {
            colorElement = new XElement(drawingNs + "schemeClr",
                new XAttribute("val", ToDrawingSchemeColor(theme.Slot)));
            ApplyTint(colorElement, theme.Tint, drawingNs);
        }
        else if (color is { } concrete)
        {
            colorElement = new XElement(drawingNs + "srgbClr",
                new XAttribute("val", FormatColor(concrete)));
        }

        return colorElement is null
            ? null
            : new XElement(drawingNs + "solidFill", colorElement);
    }

    private static void ApplyTint(XElement colorElement, double tint, XNamespace drawingNs)
    {
        if (tint > 0)
        {
            colorElement.Add(
                new XElement(drawingNs + "lumMod", new XAttribute("val", Math.Clamp((int)Math.Round((1 - tint) * 100000), 0, 100000))),
                new XElement(drawingNs + "lumOff", new XAttribute("val", Math.Clamp((int)Math.Round(tint * 100000), 0, 100000))));
        }
        else if (tint < 0)
        {
            colorElement.Add(new XElement(drawingNs + "lumMod",
                new XAttribute("val", Math.Clamp((int)Math.Round((1 + tint) * 100000), 0, 100000))));
        }
    }

    private static string ToDrawingSchemeColor(WorkbookThemeColorSlot slot) =>
        slot switch
        {
            WorkbookThemeColorSlot.Dark1 => "dk1",
            WorkbookThemeColorSlot.Light1 => "lt1",
            WorkbookThemeColorSlot.Dark2 => "dk2",
            WorkbookThemeColorSlot.Light2 => "lt2",
            WorkbookThemeColorSlot.Accent1 => "accent1",
            WorkbookThemeColorSlot.Accent2 => "accent2",
            WorkbookThemeColorSlot.Accent3 => "accent3",
            WorkbookThemeColorSlot.Accent4 => "accent4",
            WorkbookThemeColorSlot.Accent5 => "accent5",
            WorkbookThemeColorSlot.Accent6 => "accent6",
            WorkbookThemeColorSlot.Hyperlink => "hlink",
            WorkbookThemeColorSlot.FollowedHyperlink => "folHlink",
            _ => "accent1"
        };
}
