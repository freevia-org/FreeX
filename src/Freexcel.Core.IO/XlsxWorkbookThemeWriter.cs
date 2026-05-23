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
                    new XElement(drawingNs + "clrScheme",
                        new XAttribute("name", $"{theme.Name} Colors"),
                        XlsxWorkbookThemeReader.ColorElements.Select(color =>
                            new XElement(drawingNs + color.ElementName,
                                new XElement(drawingNs + "srgbClr",
                                    new XAttribute("val", FormatColor(theme.GetColor(color.Slot))))))),
                    new XElement(drawingNs + "fontScheme",
                        new XAttribute("name", $"{theme.Name} Fonts"),
                        new XElement(drawingNs + "majorFont",
                            new XElement(drawingNs + "latin",
                                new XAttribute("typeface", theme.MajorFontName))),
                        new XElement(drawingNs + "minorFont",
                            new XElement(drawingNs + "latin",
                                new XAttribute("typeface", theme.MinorFontName)))),
                    new XElement(drawingNs + "fmtScheme",
                        new XAttribute("name", theme.EffectsName)))));
    }

    private static string FormatColor(CellColor color) =>
        $"{color.R:X2}{color.G:X2}{color.B:X2}";
}
