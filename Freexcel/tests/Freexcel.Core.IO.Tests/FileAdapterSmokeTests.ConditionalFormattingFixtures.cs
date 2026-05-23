using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO.Tests;

public partial class FileAdapterSmokeTests
{
    private static void AddUnknownConditionalFormatting(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "conditionalFormatting",
                new XAttribute("sqref", "A2:A10"),
                new XElement(
                    worksheetNs + "cfRule",
                    new XAttribute("type", "freexcelFutureRule"),
                    new XAttribute("priority", "1"),
                    new XElement(worksheetNs + "formula", "UNKNOWN_CF_SENTINEL"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddAdvancedConditionalFormatNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var conditionalFormatting = worksheetXml.Root!
                .Elements(worksheetNs + "conditionalFormatting")
                .First(element => element.Elements(worksheetNs + "cfRule").Any(rule => rule.Attribute("type")?.Value == "colorScale"));
            conditionalFormatting.SetAttributeValue("customBlockAttr", "cf-container");
            conditionalFormatting.Add(new XElement(
                worksheetNs + "extLst",
                new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEXCEL-CF-CONTAINER-EXT}"))));
            var rule = conditionalFormatting
                .Elements(worksheetNs + "cfRule")
                .First(element => element.Attribute("type")?.Value == "colorScale");
            rule.SetAttributeValue("customAttr", "cf-native");
            rule.Add(new XElement(
                worksheetNs + "extLst",
                new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEXCEL-CF-EXT}"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddAdvancedConditionalFormatPayloadNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var dataBar = worksheetXml.Root!
                .Descendants(worksheetNs + "dataBar")
                .First();
            dataBar.SetAttributeValue("border", "1");
            dataBar.SetAttributeValue("axisPosition", "middle");
            dataBar.Add(
                new XElement(worksheetNs + "negativeFillColor", new XAttribute("rgb", "FFFF0000")),
                new XElement(worksheetNs + "axisColor", new XAttribute("rgb", "FF000000")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddConditionalFormatDifferentialStyleNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var stylesXml = LoadPackageXml(archive.GetEntry("xl/styles.xml")!);
            var dxf = stylesXml.Root!
                .Element(workbookNs + "dxfs")!
                .Elements(workbookNs + "dxf")
                .Single();
            dxf.SetAttributeValue("customAttr", "dxf-native");
            var font = dxf.Element(workbookNs + "font")!;
            font.SetAttributeValue("customFontAttr", "font-native");
            font.Add(new XElement(workbookNs + "scheme", new XAttribute("val", "minor")));
            dxf.Add(new XElement(
                workbookNs + "extLst",
                new XElement(workbookNs + "ext", new XAttribute("uri", "{FREEXCEL-DXF-NATIVE}"))));
            ReplacePackageXml(archive, "xl/styles.xml", stylesXml);
        }

        packageStream.Position = 0;
    }

}
