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
        if (TryCreateNativeThemeElement(theme.NativeColorSchemeXml, drawingNs + "clrScheme") is { } colorScheme)
            return colorScheme;

        return new XElement(drawingNs + "clrScheme",
            new XAttribute("name", $"{theme.Name} Colors"),
            XlsxWorkbookThemeReader.ColorElements.Select(color =>
                new XElement(drawingNs + color.ElementName,
                    new XElement(drawingNs + "srgbClr",
                        new XAttribute("val", XlsxDrawingColorWriter.FormatRgb(theme.GetColor(color.Slot)))))));
    }

    private static XElement CreateFontSchemeElement(WorkbookTheme theme, XNamespace drawingNs)
    {
        if (TryCreateNativeThemeElement(
                theme.NativeFontSchemeXml,
                drawingNs + "fontScheme",
                XlsxThemeTypefaceNormalizer.SanitizeNonEmptyTypefaceAttributes) is { } fontScheme)
        {
            return fontScheme;
        }

        return new XElement(drawingNs + "fontScheme",
            new XAttribute("name", $"{theme.Name} Fonts"),
            CreateFontCollectionElement("majorFont", theme.MajorFontName, drawingNs),
            CreateFontCollectionElement("minorFont", theme.MinorFontName, drawingNs));
    }

    // CT_FontCollection requires latin, ea and cs children (in that order). The east-asian/complex-
    // script faces default to empty (inherit), matching Office's built-in theme; omitting them makes
    // the theme part schema-invalid and Excel refuses to open the whole workbook.
    private static XElement CreateFontCollectionElement(string collectionName, string latinTypeface, XNamespace drawingNs) =>
        new(drawingNs + collectionName,
            new XElement(drawingNs + "latin", new XAttribute("typeface", XlsxFontNameSanitizer.NormalizeFontName(latinTypeface))),
            new XElement(drawingNs + "ea", new XAttribute("typeface", string.Empty)),
            new XElement(drawingNs + "cs", new XAttribute("typeface", string.Empty)));

    private static XElement CreateFormatSchemeElement(WorkbookTheme theme, XNamespace drawingNs)
    {
        if (TryCreateNativeThemeElement(theme.NativeFormatSchemeXml, drawingNs + "fmtScheme") is { } formatScheme)
            return formatScheme;

        // CT_StyleMatrix (fmtScheme) requires fillStyleLst, lnStyleLst, effectStyleLst and
        // bgFillStyleLst, each with at least three entries. An empty fmtScheme is schema-invalid and
        // makes Excel reject the workbook, so emit a minimal complete style matrix using phClr
        // placeholder colours (the standard theme convention).
        XElement SolidPhClrFill() =>
            new(drawingNs + "solidFill", new XElement(drawingNs + "schemeClr", new XAttribute("val", "phClr")));

        XElement Line(int widthEmu) =>
            new(drawingNs + "ln",
                new XAttribute("w", widthEmu),
                new XAttribute("cap", "flat"),
                new XAttribute("cmpd", "sng"),
                new XAttribute("algn", "ctr"),
                SolidPhClrFill(),
                new XElement(drawingNs + "prstDash", new XAttribute("val", "solid")));

        XElement EffectStyle() =>
            new(drawingNs + "effectStyle", new XElement(drawingNs + "effectLst"));

        return new XElement(drawingNs + "fmtScheme",
            new XAttribute("name", theme.EffectsName),
            new XElement(drawingNs + "fillStyleLst", SolidPhClrFill(), SolidPhClrFill(), SolidPhClrFill()),
            new XElement(drawingNs + "lnStyleLst", Line((int)(DrawingMlCoordinateUnits.EmuPerPoint / 2)), Line((int)DrawingMlCoordinateUnits.EmuPerPoint), Line((int)(DrawingMlCoordinateUnits.EmuPerPoint * 3 / 2))),
            new XElement(drawingNs + "effectStyleLst", EffectStyle(), EffectStyle(), EffectStyle()),
            new XElement(drawingNs + "bgFillStyleLst", SolidPhClrFill(), SolidPhClrFill(), SolidPhClrFill()));
    }

    private static IEnumerable<XElement> CreateThemeSupplementElements(WorkbookTheme theme, XNamespace drawingNs)
    {
        if (TryCreateNativeThemeSupplementElements(theme.NativeThemeSupplementXml, drawingNs) is not { } elements)
            return CreateModeledThemeSupplementElements(theme, drawingNs);

        if (!elements.Any(element => element.Name == drawingNs + "extraClrSchemeLst"))
            elements.AddRange(CreateAlternateColorSchemeListElement(theme, drawingNs));
        if (!elements.Any(element => element.Name == drawingNs + "objectDefaults"))
            elements.AddRange(CreateObjectDefaultsElement(theme.ObjectDefaults, drawingNs));

        return elements;

        static List<XElement>? TryCreateNativeThemeSupplementElements(string? nativeThemeSupplementXml, XNamespace drawingNs)
        {
            if (string.IsNullOrWhiteSpace(nativeThemeSupplementXml))
                return null;

            var elements = new List<XElement>();
            try
            {
                using var stringReader = new StringReader($"<themeSupplement>{nativeThemeSupplementXml}</themeSupplement>");
                using var xmlReader = XmlReader.Create(
                    stringReader,
                    new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null
                    });
                var document = XDocument.Load(xmlReader);
                foreach (var element in document.Root!
                             .Elements()
                             .Where(element => IsSupportedThemeSupplementElement(element, drawingNs)))
                {
                    var clone = new XElement(element);
                    XlsxThemeTypefaceNormalizer.SanitizeNonEmptyTypefaceAttributes(clone);
                    elements.Add(clone);
                }
            }
            catch
            {
                return null;
            }

            return elements;
        }
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
        if (TryCreateNativeThemeElement(scheme.NativeColorSchemeXml, drawingNs + "clrScheme") is { } colorScheme)
            return colorScheme;

        return new XElement(drawingNs + "clrScheme",
            new XAttribute("name", string.IsNullOrWhiteSpace(scheme.Name) ? "Alternate Colors" : scheme.Name),
            XlsxWorkbookThemeReader.ColorElements
                .Where(color => scheme.Colors.ContainsKey(color.Slot))
                .Select(color =>
                    new XElement(drawingNs + color.ElementName,
                        new XElement(drawingNs + "srgbClr",
                            new XAttribute("val", XlsxDrawingColorWriter.FormatRgb(scheme.Colors[color.Slot]))))));
    }

    private static IEnumerable<XElement> CreateObjectDefaultsElement(
        WorkbookThemeObjectDefaults? defaults,
        XNamespace drawingNs)
    {
        if (defaults is null)
            return [];

        if (!string.IsNullOrWhiteSpace(defaults.NativeObjectDefaultsXml))
        {
            if (TryCreateNativeThemeElement(
                    defaults.NativeObjectDefaultsXml,
                    drawingNs + "objectDefaults",
                    XlsxThemeTypefaceNormalizer.SanitizeNonEmptyTypefaceAttributes) is { } objectDefaults)
            {
                return [objectDefaults];
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

    private static XElement? TryCreateNativeThemeElement(
        string? xml,
        XName expectedName,
        Func<XElement, bool>? sanitize = null)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var element = XElement.Parse(xml);
            if (element.Name != expectedName)
                return null;

            var clone = new XElement(element);
            _ = sanitize?.Invoke(clone);
            return clone;
        }
        catch
        {
            return null;
        }
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
            runPropertiesChildren.Add(new XElement(
                drawingNs + "latin",
                new XAttribute("typeface", XlsxFontNameSanitizer.NormalizeFontName(text.Typeface))));

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
            line.SetAttributeValue("w", (int)Math.Round(widthPoints.Value * DrawingMlCoordinateUnits.EmuPerPoint));
        return line;
    }

    private static XElement? ToSolidFill(
        WorkbookThemeColorReference? themeColor,
        CellColor? color,
        XNamespace drawingNs) =>
        XlsxDrawingColorWriter.ToSolidFill(themeColor, color, drawingNs);
}
