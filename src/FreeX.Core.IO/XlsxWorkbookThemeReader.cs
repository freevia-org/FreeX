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
        if (colorElement is null)
            return null;

        var srgb = colorElement.Element(drawingNs + "srgbClr")?.Attribute("val")?.Value;
        if (XlsxColorReader.TryParseHexColor(srgb, out var color))
            return color;

        var systemFallback = colorElement.Element(drawingNs + "sysClr")?.Attribute("lastClr")?.Value;
        if (XlsxColorReader.TryParseHexColor(systemFallback, out color))
            return color;

        var hslElement = colorElement.Element(drawingNs + "hslClr");
        if (hslElement is not null && TryReadHslColor(hslElement, out color))
            return color;

        var scrgbElement = colorElement.Element(drawingNs + "scrgbClr");
        if (scrgbElement is not null && TryReadScRgbColor(scrgbElement, out color))
            return color;

        var presetName = colorElement.Element(drawingNs + "prstClr")?.Attribute("val")?.Value;
        return TryMapPresetColor(presetName, out color) ? color : null;
    }

    private static bool TryReadHslColor(XElement hslElement, out CellColor color)
    {
        color = default;

        if (!TryParsePercentAttribute(hslElement, "hue", out var hueRaw) ||
            !TryParsePercentAttribute(hslElement, "sat", out var satRaw) ||
            !TryParsePercentAttribute(hslElement, "lum", out var lumRaw))
            return false;

        // hue is ST_PositiveFixedAngle: 60,000ths of a degree.
        var hueDegrees = hueRaw / 60000.0 % 360.0;
        if (hueDegrees < 0)
            hueDegrees += 360.0;

        // sat/lum are ST_Percentage: 1000ths of a percent (100000 == 100%).
        var saturation = Math.Clamp(satRaw / 100000.0, 0.0, 1.0);
        var lightness = Math.Clamp(lumRaw / 100000.0, 0.0, 1.0);

        var (r, g, b) = HslToRgb(hueDegrees, saturation, lightness);
        color = new CellColor(r, g, b);
        return true;
    }

    private static (byte R, byte G, byte B) HslToRgb(double hueDegrees, double saturation, double lightness)
    {
        if (saturation <= 0.0)
        {
            var gray = ToByte(lightness);
            return (gray, gray, gray);
        }

        var chroma = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
        var hPrime = hueDegrees / 60.0;
        var secondLargest = chroma * (1.0 - Math.Abs(hPrime % 2.0 - 1.0));

        var (r1, g1, b1) = hPrime switch
        {
            < 1.0 => (chroma, secondLargest, 0.0),
            < 2.0 => (secondLargest, chroma, 0.0),
            < 3.0 => (0.0, chroma, secondLargest),
            < 4.0 => (0.0, secondLargest, chroma),
            < 5.0 => (secondLargest, 0.0, chroma),
            _ => (chroma, 0.0, secondLargest)
        };

        var lightnessMatch = lightness - chroma / 2.0;
        return (ToByte(r1 + lightnessMatch), ToByte(g1 + lightnessMatch), ToByte(b1 + lightnessMatch));
    }

    private static bool TryReadScRgbColor(XElement scrgbElement, out CellColor color)
    {
        color = default;

        if (!TryParsePercentAttribute(scrgbElement, "r", out var rRaw) ||
            !TryParsePercentAttribute(scrgbElement, "g", out var gRaw) ||
            !TryParsePercentAttribute(scrgbElement, "b", out var bRaw))
            return false;

        // scRGB components are linear-light percentages (1000ths of a percent);
        // apply the sRGB transfer function to get the displayed (gamma-encoded) color.
        var r = ScRgbLinearToSrgbByte(rRaw / 100000.0);
        var g = ScRgbLinearToSrgbByte(gRaw / 100000.0);
        var b = ScRgbLinearToSrgbByte(bRaw / 100000.0);
        color = new CellColor(r, g, b);
        return true;
    }

    private static byte ScRgbLinearToSrgbByte(double linear)
    {
        linear = Math.Clamp(linear, 0.0, 1.0);
        var srgb = linear <= 0.0031308
            ? linear * 12.92
            : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        return ToByte(srgb);
    }

    private static bool TryParsePercentAttribute(XElement element, string attributeName, out int value) =>
        int.TryParse(
            element.Attribute(attributeName)?.Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);

    private static byte ToByte(double unitValue) =>
        (byte)Math.Clamp(Math.Round(unitValue * 255.0, MidpointRounding.AwayFromZero), 0.0, 255.0);

    private static bool TryMapPresetColor(string? name, out CellColor color)
    {
        if (!string.IsNullOrWhiteSpace(name) && PresetColors.TryGetValue(name.Trim(), out var hex))
        {
            var parsed = XlsxColorReader.TryParseHexColor(hex, out color);
            return parsed;
        }

        color = default;
        return false;
    }

    // DrawingML preset color values (ECMA-376 ST_PresetColorVal), matching the
    // CSS3/X11 extended color keyword table. Keys are matched case-insensitively.
    private static readonly Dictionary<string, string> PresetColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceBlue"] = "F0F8FF",
        ["antiqueWhite"] = "FAEBD7",
        ["aqua"] = "00FFFF",
        ["aquamarine"] = "7FFFD4",
        ["azure"] = "F0FFFF",
        ["beige"] = "F5F5DC",
        ["bisque"] = "FFE4C4",
        ["black"] = "000000",
        ["blanchedAlmond"] = "FFEBCD",
        ["blue"] = "0000FF",
        ["blueViolet"] = "8A2BE2",
        ["brown"] = "A52A2A",
        ["burlyWood"] = "DEB887",
        ["cadetBlue"] = "5F9EA0",
        ["chartreuse"] = "7FFF00",
        ["chocolate"] = "D2691E",
        ["coral"] = "FF7F50",
        ["cornflowerBlue"] = "6495ED",
        ["cornsilk"] = "FFF8DC",
        ["crimson"] = "DC143C",
        ["cyan"] = "00FFFF",
        ["darkBlue"] = "00008B",
        ["darkCyan"] = "008B8B",
        ["darkGoldenrod"] = "B8860B",
        ["darkGray"] = "A9A9A9",
        ["darkGrey"] = "A9A9A9",
        ["darkGreen"] = "006400",
        ["darkKhaki"] = "BDB76B",
        ["darkMagenta"] = "8B008B",
        ["darkOliveGreen"] = "556B2F",
        ["darkOrange"] = "FF8C00",
        ["darkOrchid"] = "9932CC",
        ["darkRed"] = "8B0000",
        ["darkSalmon"] = "E9967A",
        ["darkSeaGreen"] = "8FBC8B",
        ["darkSlateBlue"] = "483D8B",
        ["darkSlateGray"] = "2F4F4F",
        ["darkSlateGrey"] = "2F4F4F",
        ["darkTurquoise"] = "00CED1",
        ["darkViolet"] = "9400D3",
        ["deepPink"] = "FF1493",
        ["deepSkyBlue"] = "00BFFF",
        ["dimGray"] = "696969",
        ["dimGrey"] = "696969",
        ["dodgerBlue"] = "1E90FF",
        ["firebrick"] = "B22222",
        ["floralWhite"] = "FFFAF0",
        ["forestGreen"] = "228B22",
        ["fuchsia"] = "FF00FF",
        ["gainsboro"] = "DCDCDC",
        ["ghostWhite"] = "F8F8FF",
        ["gold"] = "FFD700",
        ["goldenrod"] = "DAA520",
        ["gray"] = "808080",
        ["grey"] = "808080",
        ["green"] = "008000",
        ["greenYellow"] = "ADFF2F",
        ["honeydew"] = "F0FFF0",
        ["hotPink"] = "FF69B4",
        ["indianRed"] = "CD5C5C",
        ["indigo"] = "4B0082",
        ["ivory"] = "FFFFF0",
        ["khaki"] = "F0E68C",
        ["lavender"] = "E6E6FA",
        ["lavenderBlush"] = "FFF0F5",
        ["lawnGreen"] = "7CFC00",
        ["lemonChiffon"] = "FFFACD",
        ["lightBlue"] = "ADD8E6",
        ["lightCoral"] = "F08080",
        ["lightCyan"] = "E0FFFF",
        ["lightGoldenrodYellow"] = "FAFAD2",
        ["lightGray"] = "D3D3D3",
        ["lightGrey"] = "D3D3D3",
        ["lightGreen"] = "90EE90",
        ["lightPink"] = "FFB6C1",
        ["lightSalmon"] = "FFA07A",
        ["lightSeaGreen"] = "20B2AA",
        ["lightSkyBlue"] = "87CEFA",
        ["lightSlateGray"] = "778899",
        ["lightSlateGrey"] = "778899",
        ["lightSteelBlue"] = "B0C4DE",
        ["lightYellow"] = "FFFFE0",
        ["lime"] = "00FF00",
        ["limeGreen"] = "32CD32",
        ["linen"] = "FAF0E6",
        ["magenta"] = "FF00FF",
        ["maroon"] = "800000",
        ["medAquamarine"] = "66CDAA",
        ["mediumAquamarine"] = "66CDAA",
        ["mediumBlue"] = "0000CD",
        ["mediumOrchid"] = "BA55D3",
        ["mediumPurple"] = "9370DB",
        ["mediumSeaGreen"] = "3CB371",
        ["mediumSlateBlue"] = "7B68EE",
        ["mediumSpringGreen"] = "00FA9A",
        ["mediumTurquoise"] = "48D1CC",
        ["mediumVioletRed"] = "C71585",
        ["midnightBlue"] = "191970",
        ["mintCream"] = "F5FFFA",
        ["mistyRose"] = "FFE4E1",
        ["moccasin"] = "FFE4B5",
        ["navajoWhite"] = "FFDEAD",
        ["navy"] = "000080",
        ["oldLace"] = "FDF5E6",
        ["olive"] = "808000",
        ["oliveDrab"] = "6B8E23",
        ["orange"] = "FFA500",
        ["orangeRed"] = "FF4500",
        ["orchid"] = "DA70D6",
        ["paleGoldenrod"] = "EEE8AA",
        ["paleGreen"] = "98FB98",
        ["paleTurquoise"] = "AFEEEE",
        ["paleVioletRed"] = "DB7093",
        ["papayaWhip"] = "FFEFD5",
        ["peachPuff"] = "FFDAB9",
        ["peru"] = "CD853F",
        ["pink"] = "FFC0CB",
        ["plum"] = "DDA0DD",
        ["powderBlue"] = "B0E0E6",
        ["purple"] = "800080",
        ["red"] = "FF0000",
        ["rosyBrown"] = "BC8F8F",
        ["royalBlue"] = "4169E1",
        ["saddleBrown"] = "8B4513",
        ["salmon"] = "FA8072",
        ["sandyBrown"] = "F4A460",
        ["seaGreen"] = "2E8B57",
        ["seaShell"] = "FFF5EE",
        ["sienna"] = "A0522D",
        ["silver"] = "C0C0C0",
        ["skyBlue"] = "87CEEB",
        ["slateBlue"] = "6A5ACD",
        ["slateGray"] = "708090",
        ["slateGrey"] = "708090",
        ["snow"] = "FFFAFA",
        ["springGreen"] = "00FF7F",
        ["steelBlue"] = "4682B4",
        ["tan"] = "D2B48C",
        ["teal"] = "008080",
        ["thistle"] = "D8BFD8",
        ["tomato"] = "FF6347",
        ["turquoise"] = "40E0D0",
        ["violet"] = "EE82EE",
        ["wheat"] = "F5DEB3",
        ["white"] = "FFFFFF",
        ["whiteSmoke"] = "F5F5F5",
        ["yellow"] = "FFFF00",
        ["yellowGreen"] = "9ACD32"
    };

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
