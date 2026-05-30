using FreeX.Core.Model;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

public static class XlsxWorkbookThemeReader
{
    private static readonly (WorkbookThemeColorSlot Slot, string ElementName)[] ThemeColorElements =
    [
        (WorkbookThemeColorSlot.Dark1, "dk1"),
        (WorkbookThemeColorSlot.Light1, "lt1"),
        (WorkbookThemeColorSlot.Dark2, "dk2"),
        (WorkbookThemeColorSlot.Light2, "lt2"),
        (WorkbookThemeColorSlot.Accent1, "accent1"),
        (WorkbookThemeColorSlot.Accent2, "accent2"),
        (WorkbookThemeColorSlot.Accent3, "accent3"),
        (WorkbookThemeColorSlot.Accent4, "accent4"),
        (WorkbookThemeColorSlot.Accent5, "accent5"),
        (WorkbookThemeColorSlot.Accent6, "accent6"),
        (WorkbookThemeColorSlot.Hyperlink, "hlink"),
        (WorkbookThemeColorSlot.FollowedHyperlink, "folHlink")
    ];

    public static IReadOnlyList<(WorkbookThemeColorSlot Slot, string ElementName)> ColorElements => ThemeColorElements;

    public static WorkbookTheme Load(Stream xlsxStream)
    {
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var themeEntry = archive.GetEntry("xl/theme/theme1.xml");
            if (themeEntry is null)
                return WorkbookTheme.Office;

            using var entryStream = themeEntry.Open();
            var themeXml = XDocument.Load(entryStream);
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

        theme = theme
            .WithNativeThemeSupplementXml(ReadThemeSupplementXml(themeXml.Root, drawingNs))
            .WithSupplementalMetadata(
                ReadAlternateColorSchemes(themeXml.Root, drawingNs),
                themeXml.Root?.Element(drawingNs + "objectDefaults") is not null);

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
}
