using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using FreeX.Core.Model;
using Free.Shared.Opc;

namespace FreeX.Core.IO;

internal static class XlsxWorkbookThemeWriter
{
    public static void Save(Stream xlsxStream, WorkbookTheme theme)
    {
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        const string themePath = "xl/theme/theme1.xml";
        archive.GetEntry(themePath)?.Delete();

        // Like XlsxWorksheetChartWriter, this creates its entry directly rather than through
        // XlsxPackageXmlEditor/OpcXml, so it needs its own pass of the sanitizing those apply. The
        // theme part is built from model text -- the theme name (which reaches the a:theme/@name,
        // a:clrScheme/@name and a:fontScheme/@name attributes) and the major/minor font names -- and
        // WorkbookTheme.WithName takes arbitrary text, so a control code or lone surrogate that
        // arrived with a .fxl import would otherwise abort the ENTIRE workbook save here.
        var themeXml = ToThemeXml(theme);
        XmlTextSanitizer.SanitizeInPlace(themeXml);

        var themeEntry = archive.CreateEntry(themePath);
        using var stream = themeEntry.Open();
        themeXml.Save(stream);
    }

    private static XDocument ToThemeXml(WorkbookTheme theme)
    {
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        // The a:theme scaffold is the shared DrawingMlThemeXml; the native-preserving choices (verbatim
        // native colour/font/format scheme, supplement elements) stay this writer's.
        return DrawingMlThemeXml.CreateThemeDocument(
            theme.Name,
            CreateColorSchemeElement(theme, drawingNs),
            CreateFontSchemeElement(theme, drawingNs),
            CreateFormatSchemeElement(theme, drawingNs),
            CreateThemeSupplementElements(theme, drawingNs),
            new XDeclaration("1.0", "UTF-8", "yes"));
    }

    private static XElement CreateColorSchemeElement(WorkbookTheme theme, XNamespace drawingNs)
    {
        if (TryCreateNativeThemeElement(theme.NativeColorSchemeXml, drawingNs + "clrScheme") is { } colorScheme)
            return colorScheme;

        return DrawingMlThemeXml.CreateColorScheme(
            $"{theme.Name} Colors",
            XlsxWorkbookThemeReader.ColorElements.Select(color =>
                new KeyValuePair<string, string>(
                    color.ElementName,
                    XlsxDrawingColorWriter.FormatRgb(theme.GetColor(color.Slot)))));
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

        // CT_FontCollection requires latin, ea and cs children (in that order); the shared generated
        // font scheme writes the east-asian/complex-script faces empty (inherit), matching Office's
        // built-in theme. Omitting them makes the part schema-invalid and Excel refuses the workbook.
        return DrawingMlThemeXml.CreateFontScheme(
            $"{theme.Name} Fonts",
            XlsxFontNameSanitizer.NormalizeFontName(theme.MajorFontName),
            XlsxFontNameSanitizer.NormalizeFontName(theme.MinorFontName));
    }

    private static XElement CreateFormatSchemeElement(WorkbookTheme theme, XNamespace drawingNs)
    {
        if (TryCreateNativeThemeElement(theme.NativeFormatSchemeXml, drawingNs + "fmtScheme") is { } formatScheme)
            return formatScheme;

        // CT_StyleMatrix (fmtScheme) requires fillStyleLst, lnStyleLst, effectStyleLst and
        // bgFillStyleLst, each with at least three entries. An empty fmtScheme is schema-invalid and
        // makes Excel reject the workbook, so emit a minimal complete style matrix using phClr
        // placeholder colours (the standard theme convention).
        return DrawingMlThemeXml.CreateFormatScheme(
            theme.EffectsName,
            [DrawingMlThemeXml.CreateSolidPhClrFill(), DrawingMlThemeXml.CreateSolidPhClrFill(), DrawingMlThemeXml.CreateSolidPhClrFill()],
            [
                DrawingMlThemeXml.CreateLineStyle((int)(DrawingMlCoordinateUnits.EmuPerPoint / 2), includeCapCompoundAlign: true),
                DrawingMlThemeXml.CreateLineStyle((int)DrawingMlCoordinateUnits.EmuPerPoint, includeCapCompoundAlign: true),
                DrawingMlThemeXml.CreateLineStyle((int)(DrawingMlCoordinateUnits.EmuPerPoint * 3 / 2), includeCapCompoundAlign: true),
            ],
            [DrawingMlThemeXml.CreateEmptyEffectStyle(), DrawingMlThemeXml.CreateEmptyEffectStyle(), DrawingMlThemeXml.CreateEmptyEffectStyle()],
            [DrawingMlThemeXml.CreateSolidPhClrFill(), DrawingMlThemeXml.CreateSolidPhClrFill(), DrawingMlThemeXml.CreateSolidPhClrFill()]);
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
                using var xmlReader = XmlReader.Create(stringReader, SecureXmlReaderSettings.Create());
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

        return DrawingMlThemeXml.CreateColorScheme(
            string.IsNullOrWhiteSpace(scheme.Name) ? "Alternate Colors" : scheme.Name,
            XlsxWorkbookThemeReader.ColorElements
                .Where(color => scheme.Colors.ContainsKey(color.Slot))
                .Select(color =>
                    new KeyValuePair<string, string>(
                        color.ElementName,
                        XlsxDrawingColorWriter.FormatRgb(scheme.Colors[color.Slot]))));
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
