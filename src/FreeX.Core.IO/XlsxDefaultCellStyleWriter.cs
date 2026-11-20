using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxDefaultCellStyleWriter
{
    private const string StylesPartPath = "xl/styles.xml";
    private static readonly XNamespace SpreadsheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public static void Save(Stream packageStream, CellStyle defaultStyle)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        if (archive.GetEntry(StylesPartPath) is not { } stylesEntry)
            return;

        var styles = XlsxPackageXmlEditor.LoadXml(stylesEntry);
        var root = styles.Root;
        var fonts = root?.Element(SpreadsheetNs + "fonts");
        var styleXfs = root?.Element(SpreadsheetNs + "cellStyleXfs");
        var normalStyle = root?.Element(SpreadsheetNs + "cellStyles")?
            .Elements(SpreadsheetNs + "cellStyle")
            .FirstOrDefault(style => (string?)style.Attribute("name") == "Normal");
        if (fonts is null || styleXfs is null || normalStyle is null ||
            !int.TryParse(normalStyle.Attribute("xfId")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var normalXfId))
        {
            return;
        }

        var normalXf = styleXfs.Elements(SpreadsheetNs + "xf").ElementAtOrDefault(normalXfId);
        if (normalXf is null)
            return;

        var font = fonts.Elements(SpreadsheetNs + "font").FirstOrDefault(candidate =>
            string.Equals(
                (string?)candidate.Element(SpreadsheetNs + "name")?.Attribute("val"),
                defaultStyle.FontName,
                StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(
                candidate.Element(SpreadsheetNs + "sz")?.Attribute("val")?.Value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var size) &&
            size.Equals(defaultStyle.FontSize));
        if (font is null)
            return;

        var fontId = fonts.Elements(SpreadsheetNs + "font").TakeWhile(candidate => candidate != font).Count();
        normalXf.SetAttributeValue("fontId", fontId.ToString(CultureInfo.InvariantCulture));
        XlsxPackageXmlEditor.ReplaceXml(archive, StylesPartPath, styles);
    }
}
