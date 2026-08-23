using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Opc;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

public static class XlsxWorkbookThemeReader
{
    private static readonly (WorkbookThemeColorSlot Slot, string ElementName)[] ThemeColorElements =
        XlsxDrawingThemeColorSlots.ColorSchemeElements.ToArray();

    public static IReadOnlyList<(WorkbookThemeColorSlot Slot, string ElementName)> ColorElements => ThemeColorElements;

    public static WorkbookTheme Load(Stream xlsxStream)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch
        {
            // The stream isn't even a readable zip container, so there is no theme part to speak
            // of -- this is the same as "workbook has no custom theme".
            return WorkbookTheme.Office;
        }

        using (archive)
        {
            return Load(archive);
        }
    }

    // R145-io-theme-corrupt-fallback (default-masks-missing F1): a theme part that is PRESENT but
    // fails to resolve/read/parse (truncated zip entry, malformed XML, unreadable relationships,
    // ...) must never be treated the same as "workbook legitimately has no custom theme". The two
    // used to collapse onto the identical WorkbookTheme.Office fallback here, and
    // XlsxWorkbookThemeWriter.Save then permanently overwrote the still-present, still-corrupt
    // xl/theme/theme1.xml with a synthesized default on the very next save -- silently destroying
    // the workbook's real theme (Accent1-6/Dark/Light colors etc.) with no warning to the user.
    // Only "themePath is null" (no theme relationship AND no xl/theme/theme1.xml entry at all) is
    // the legitimate empty case below; any exception encountered while a part is actually being
    // resolved/read is surfaced via XlsxThemePartCorruptException instead of swallowed, so it
    // propagates out of the load pipeline (production callers already fail the whole file open
    // with a clear message on an unexpected exception here -- see WorkbookFileWorkflow.OpenAsync)
    // rather than silently continuing on to a save that clobbers the original bytes.
    internal static WorkbookTheme Load(ZipArchive archive)
    {
        string? themePath;
        XDocument themeXml;
        try
        {
            themePath = DrawingMlThemeReader.ResolveThemePartPath(archive, "xl/workbook.xml", "xl/theme/theme1.xml");
            if (themePath is null)
                return WorkbookTheme.Office;

            themeXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry(themePath)!);
        }
        catch (Exception ex)
        {
            throw new XlsxThemePartCorruptException(ex);
        }

        try
        {
            return Read(themeXml);
        }
        catch (Exception ex)
        {
            throw new XlsxThemePartCorruptException(ex);
        }
    }

    private static WorkbookTheme Read(XDocument themeXml)
    {
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var sharedTheme = DrawingMlThemeReader.Read(themeXml);
        var theme = WorkbookTheme.Office
            .WithName(sharedTheme.Name ?? WorkbookTheme.Office.Name);

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
                sharedTheme.FontScheme.MajorLatinTypeface ?? theme.MajorFontName,
                sharedTheme.FontScheme.MinorLatinTypeface ?? theme.MinorFontName);
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
            if (sharedTheme.ColorScheme[ToSharedSlot(slot)] is { } color)
                theme = theme.WithColor(slot, ToCellColor(color.BaseColor ?? color.ResolvedColor));
        }

        return theme.WithNativeColorSchemeXml(colorScheme.ToString(SaveOptions.DisableFormatting));
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
        var sharedColors = DrawingMlThemeReader.ReadColorScheme(colorScheme);
        foreach (var (slot, elementName) in ThemeColorElements)
        {
            if (sharedColors[ToSharedSlot(slot)] is { } color)
                colors[slot] = ToCellColor(color.BaseColor ?? color.ResolvedColor);
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
            ? Math.Round(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 3)
            : null;
    }

    private static DrawingMlThemeColorSlot ToSharedSlot(WorkbookThemeColorSlot slot) =>
        XlsxDrawingThemeColorSlots.ToSharedSlot(slot);

    private static CellColor ToCellColor(DrawingMlRgbColor color) =>
        new(color.R, color.G, color.B);
}

/// <summary>
/// Thrown by <see cref="XlsxWorkbookThemeReader"/> when an xlsx package's theme part
/// (xl/theme/theme1.xml, or whatever xl/workbook.xml's theme relationship points at) is PRESENT
/// but could not be resolved, read, or parsed. This is deliberately a distinct failure mode from
/// "the workbook has no custom theme" (which resolves cleanly to <see cref="WorkbookTheme.Office"/>
/// with no exception): collapsing the two let a corrupted-but-present theme part be silently
/// replaced with the stock Office theme, after which <c>XlsxWorkbookThemeWriter.Save</c> would
/// permanently overwrite the original, still-corrupt part with a synthesized default on the very
/// next save -- destroying the workbook's real theme colors/fonts with no warning. Callers that
/// want the file to still open in a degraded state must catch this exception explicitly and decide
/// how to warn the user and/or avoid re-saving over the original part; letting it propagate (the
/// default for every current production caller) fails the whole file open instead of silently
/// corrupting it.
/// </summary>
public sealed class XlsxThemePartCorruptException : Exception
{
    public XlsxThemePartCorruptException(Exception innerException)
        : base("The workbook's theme part (xl/theme/theme1.xml) is present but could not be read.", innerException)
    {
    }
}
