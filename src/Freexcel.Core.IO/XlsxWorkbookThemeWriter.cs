using System.IO.Compression;
using System.Xml.Linq;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
                    CreateFormatSchemeElement(theme, drawingNs))));
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
}
