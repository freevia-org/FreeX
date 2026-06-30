using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public static class XlsxWorkbookThemeReader
{
    private static readonly (WorkbookThemeColorSlot Slot, string ElementName)[] ThemeColorElements =
        XlsxDrawingThemeColorSlots.ColorSchemeElements.ToArray();

    public static IReadOnlyList<(WorkbookThemeColorSlot Slot, string ElementName)> ColorElements => ThemeColorElements;

    public static WorkbookTheme Load(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            return Load(archive);
        }
        catch
        {
            return WorkbookTheme.Office;
        }
    }

    internal static WorkbookTheme Load(ZipArchive archive)
    {
        try
        {
            var themeEntry = archive.GetEntry("xl/theme/theme1.xml");
            if (themeEntry is null)
                return WorkbookTheme.Office;

            var themeXml = XlsxPackageXmlEditor.LoadXml(themeEntry);
            return Read(themeXml);
        }
        catch
        {
            return WorkbookTheme.Office;
        }
    }

    private static WorkbookTheme Read(XDocument themeXml)
    {
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var theme = WorkbookTheme.Office
            .WithName(themeXml.Root?.Attribute("name")?.Value ?? WorkbookTheme.Office.Name);

        var objectDefaults = ReadObjectDefaults(themeXml.Root?.Element(drawingNs + "objectDefaults"), drawingNs);

        theme = theme
            .WithNativeThemeSupplementXml(ReadThemeSupplementXml(themeXml.Root, drawingNs))
            .WithSupplementalMetadata(
                ReadAlternateColorSchemes(themeXml.Root, drawingNs),
                themeXml.Root?.Element(drawingNs + "objectDefaults") is not null,
                objectDefaults);

        var themeElements = themeXml.Root?.Element(drawingNs + "themeElements");
        if (themeElements is null)
            return theme;

        var fontScheme = themeElements.Element(drawingNs + "fontScheme");
        if (fontScheme is not null)
        {
            theme = theme.WithFonts(
                ReadThemeTypeface(fontScheme.Element(drawingNs + "majorFont"), drawingNs) ?? theme.MajorFontName,
                ReadThemeTypeface(fontScheme.Element(drawingNs + "minorFont"), drawingNs) ?? theme.MinorFontName);
            theme = theme.WithNativeFontSchemeXml(fontScheme.ToString(SaveOptions.DisableFormatting));
        }

        var formatScheme = themeElements.Element(drawingNs + "fmtScheme");
        var effectsName = formatScheme?.Attribute("name")?.Value;
        if (!string.IsNullOrWhiteSpace(effectsName))
            theme = theme.WithEffects(effectsName);
        if (formatScheme is not null)
            theme = theme.WithNativeFormatSchemeXml(formatScheme.ToString(SaveOptions.DisableFormatting));

        var colorScheme = themeElements.Element(drawingNs + "clrScheme");
        if (colorScheme is null)
            return theme;

        foreach (var (slot, elementName) in ThemeColorElements)
        {
            if (ReadThemeColor(colorScheme.Element(drawingNs + elementName), drawingNs) is { } color)
                theme = theme.WithColor(slot, color);
        }

        return theme.WithNativeColorSchemeXml(colorScheme.ToString(SaveOptions.DisableFormatting));
    }

    private static string? ReadThemeTypeface(XElement? fontElement, XNamespace drawingNs) =>
        fontElement?
            .Element(drawingNs + "latin")?
            .Attribute("typeface")?
            .Value;

    private static CellColor? ReadThemeColor(XElement? colorElement, XNamespace drawingNs)
    {
        var srgb = colorElement?.Element(drawingNs + "srgbClr")?.Attribute("val")?.Value;
        if (XlsxColorReader.TryParseHexColor(srgb, out var color))
            return color;

        var systemFallback = colorElement?.Element(drawingNs + "sysClr")?.Attribute("lastClr")?.Value;
        return XlsxColorReader.TryParseHexColor(systemFallback, out color)
            ? color
            : null;
    }

    private static string? ReadThemeSupplementXml(XElement? themeElement, XNamespace drawingNs)
    {
        if (themeElement is null)
            return null;

        var supplementElements = themeElement
            .Elements()
            .Where(element => element.Name != drawingNs + "themeElements")
            .Select(element => element.ToString(SaveOptions.DisableFormatting))
            .ToArray();

        return supplementElements.Length == 0
            ? null
            : string.Concat(supplementElements);
    }

    private static IReadOnlyList<WorkbookThemeAlternateColorScheme> ReadAlternateColorSchemes(
        XElement? themeElement,
        XNamespace drawingNs)
    {
        if (themeElement is null)
            return [];

        return themeElement
            .Element(drawingNs + "extraClrSchemeLst")?
            .Elements(drawingNs + "extraClrScheme")
            .Select(element => ReadAlternateColorScheme(element, drawingNs))
            .Where(scheme => scheme is not null)
            .Select(scheme => scheme!)
            .ToArray()
            ?? [];
    }

    private static WorkbookThemeAlternateColorScheme? ReadAlternateColorScheme(
        XElement extraColorScheme,
        XNamespace drawingNs)
    {
        var colorScheme = extraColorScheme.Element(drawingNs + "clrScheme");
        if (colorScheme is null)
            return null;

        var colors = new Dictionary<WorkbookThemeColorSlot, CellColor>();
        foreach (var (slot, elementName) in ThemeColorElements)
        {
            if (ReadThemeColor(colorScheme.Element(drawingNs + elementName), drawingNs) is { } color)
                colors[slot] = color;
        }

        var name = colorScheme.Attribute("name")?.Value;
        return new WorkbookThemeAlternateColorScheme(
            string.IsNullOrWhiteSpace(name) ? "Alternate Colors" : name.Trim(),
            colors,
            colorScheme.ToString(SaveOptions.DisableFormatting));
    }

    private static WorkbookThemeObjectDefaults? ReadObjectDefaults(
        XElement? objectDefaults,
        XNamespace drawingNs)
    {
        if (objectDefaults is null)
            return null;

        return new WorkbookThemeObjectDefaults(
            ReadShapeObjectDefault(objectDefaults.Element(drawingNs + "spDef"), drawingNs),
            ReadLineObjectDefault(objectDefaults.Element(drawingNs + "lnDef"), drawingNs),
            ReadTextObjectDefault(objectDefaults.Element(drawingNs + "txDef"), drawingNs),
            objectDefaults.ToString(SaveOptions.DisableFormatting));
    }

    private static WorkbookThemeShapeObjectDefault? ReadShapeObjectDefault(
        XElement? shapeDefault,
        XNamespace drawingNs)
    {
        var shapeProperties = shapeDefault?.Element(drawingNs + "spPr");
        if (shapeProperties is null)
            return null;

        ReadSolidFill(
            shapeProperties.Element(drawingNs + "solidFill"),
            drawingNs,
            out var fillThemeColor,
            out var fillColor);

        var line = shapeProperties.Element(drawingNs + "ln");
        ReadSolidFill(
            line?.Element(drawingNs + "solidFill"),
            drawingNs,
            out var outlineThemeColor,
            out var outlineColor);
        var width = ReadLineWidthPoints(line);

        return fillThemeColor is null &&
               fillColor is null &&
               outlineThemeColor is null &&
               outlineColor is null &&
               width is null
            ? null
            : new WorkbookThemeShapeObjectDefault(
                fillThemeColor,
                fillColor,
                outlineThemeColor,
                outlineColor,
                width);
    }

    private static WorkbookThemeLineObjectDefault? ReadLineObjectDefault(
        XElement? lineDefault,
        XNamespace drawingNs)
    {
        var line = FindFirstDescendant(lineDefault, drawingNs + "ln");
        if (line is null)
            return null;

        ReadSolidFill(
            line.Element(drawingNs + "solidFill"),
            drawingNs,
            out var strokeThemeColor,
            out var strokeColor);
        var width = ReadLineWidthPoints(line);

        return strokeThemeColor is null && strokeColor is null && width is null
            ? null
            : new WorkbookThemeLineObjectDefault(strokeThemeColor, strokeColor, width);
    }

    private static WorkbookThemeTextObjectDefault? ReadTextObjectDefault(
        XElement? textDefault,
        XNamespace drawingNs)
    {
        if (textDefault is null)
            return null;

        ReadSolidFill(
            FindFirstDescendant(textDefault, drawingNs + "solidFill"),
            drawingNs,
            out var textThemeColor,
            out var textColor);
        var typeface = FindFirstTypeface(textDefault, drawingNs);

        return textThemeColor is null && textColor is null && string.IsNullOrWhiteSpace(typeface)
            ? null
            : new WorkbookThemeTextObjectDefault(textThemeColor, textColor, typeface);
    }

    private static XElement? FindFirstDescendant(XElement? element, XName name)
    {
        if (element is null)
            return null;

        foreach (var descendant in element.Descendants(name))
            return descendant;

        return null;
    }

    private static string? FindFirstTypeface(XElement textDefault, XNamespace drawingNs)
    {
        foreach (var latin in textDefault.Descendants(drawingNs + "latin"))
        {
            var typeface = latin.Attribute("typeface")?.Value;
            if (!string.IsNullOrWhiteSpace(typeface))
                return typeface.Trim();
        }

        return null;
    }

    private static void ReadSolidFill(
        XElement? solidFill,
        XNamespace drawingNs,
        out WorkbookThemeColorReference? themeColor,
        out CellColor? color)
    {
        themeColor = null;
        color = null;
        if (solidFill is null)
            return;

        if (XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, drawingNs, out var readThemeColor))
        {
            themeColor = readThemeColor;
            return;
        }

        if (XlsxDrawingColorReader.TryReadConcreteColor(solidFill, drawingNs, out var readColor))
            color = readColor;
    }

    private static double? ReadLineWidthPoints(XElement? line)
    {
        var widthText = line?.Attribute("w")?.Value;
        return int.TryParse(widthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var emus) && emus > 0
            ? Math.Round(emus / (double)DrawingMlUnits.EmuPerPoint, 3)
            : null;
    }
}
