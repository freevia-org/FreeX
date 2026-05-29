using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxIndexedColorPaletteMapper
{
    private const int MaxNumberFormatColorIndex = 56;

    public static WorkbookIndexedColorPalette Load(Stream xlsxStream)
    {
        var palette = new WorkbookIndexedColorPalette();
        try
        {
            using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
            var stylesEntry = archive.GetEntry("xl/styles.xml");
            if (stylesEntry is null)
                return palette;

            var stylesXml = XlsxPackageXmlEditor.LoadXml(stylesEntry);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var index = 1;
            foreach (var rgbColor in stylesXml.Root?
                         .Element(workbookNs + "colors")?
                         .Element(workbookNs + "indexedColors")?
                         .Elements(workbookNs + "rgbColor") ?? [])
            {
                if (index > MaxNumberFormatColorIndex)
                    break;

                if (TryReadRgbColor(rgbColor.Attribute("rgb")?.Value, out var color))
                    palette.SetColor(index, color);

                index++;
            }
        }
        catch
        {
            // Indexed palette metadata is optional; malformed palette XML should not block workbook load.
        }

        return palette;
    }

    public static void Save(Stream xlsxStream, Workbook workbook)
    {
        if (workbook.IndexedColors.Colors.Count == 0)
            return;

        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Update, leaveOpen: true);
        var stylesEntry = archive.GetEntry("xl/styles.xml") ?? archive.CreateEntry("xl/styles.xml");
        var stylesXml = stylesEntry.Length == 0
            ? CreateEmptyStylesheet()
            : XlsxPackageXmlEditor.LoadXml(stylesEntry);

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = stylesXml.Root;
        if (root is null)
            return;

        var colors = root.Element(workbookNs + "colors");
        if (colors is null)
        {
            colors = new XElement(workbookNs + "colors");
            root.Add(colors);
        }

        colors.Element(workbookNs + "indexedColors")?.Remove();
        colors.Add(new XElement(
            workbookNs + "indexedColors",
            Enumerable.Range(1, MaxNumberFormatColorIndex)
                .Select(index =>
                {
                    workbook.IndexedColors.TryResolveColor(index, out var color);
                    return new XElement(workbookNs + "rgbColor", new XAttribute("rgb", ToArgb(color)));
                })));

        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/styles.xml", stylesXml);
    }

    private static XDocument CreateEmptyStylesheet()
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XDocument(new XElement(workbookNs + "styleSheet"));
    }

    private static bool TryReadRgbColor(string? rgb, out CellColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(rgb))
            return false;

        var value = rgb.Trim();
        if (value.Length == 8)
            value = value[2..];
        if (value.Length != 6 ||
            !byte.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = CellColor.FromArgb(r, g, b);
        return true;
    }

    private static string ToArgb(CellColor color) =>
        string.Create(CultureInfo.InvariantCulture, $"FF{color.R:X2}{color.G:X2}{color.B:X2}");
}
