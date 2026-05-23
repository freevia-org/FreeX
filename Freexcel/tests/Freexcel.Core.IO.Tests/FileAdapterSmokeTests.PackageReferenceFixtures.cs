using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Freexcel.Core.IO.Tests;

public partial class FileAdapterSmokeTests
{
    private static void AddMinimalExternalLinkPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/externalLinks/externalLink1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Elements(workbookNs + "externalReferences").Remove();
            workbookXml.Root!.Add(new XElement(
                workbookNs + "externalReferences",
                new XElement(workbookNs + "externalReference", new XAttribute(relNs + "id", "rIdFreexcelExternalLink"))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            var workbookRelsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelsXml = LoadPackageXml(archive.GetEntry(workbookRelsPath)!);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelExternalLink"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink"),
                new XAttribute("Target", "externalLinks/externalLink1.xml")));
            ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);

            ReplacePackageXml(archive, "xl/externalLinks/externalLink1.xml", new XDocument(
                new XElement(
                    workbookNs + "externalLink",
                    new XAttribute(XNamespace.Xmlns + "r", relNs),
                    new XElement(
                        workbookNs + "externalBook",
                        new XAttribute(relNs + "id", "rIdFreexcelExternalBook"),
                        new XElement(workbookNs + "sheetNames",
                            new XElement(workbookNs + "sheetName", new XAttribute("val", "Sheet1")))))));
            ReplacePackageXml(archive, "xl/externalLinks/_rels/externalLink1.xml.rels", new XDocument(
                new XElement(
                    packageRelNs + "Relationships",
                    new XElement(
                        packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdFreexcelExternalBook"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath"),
                        new XAttribute("Target", "linked-workbook.xlsx"),
                        new XAttribute("TargetMode", "External")))));
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalCalcChainPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace calcNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/calcChain.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            ReplacePackageXml(archive, "xl/calcChain.xml", new XDocument(
                new XElement(
                    calcNs + "calcChain",
                    new XElement(calcNs + "c", new XAttribute("r", "A1"), new XAttribute("i", "1")))));

            var workbookRelsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelsXml = LoadPackageXml(archive.GetEntry(workbookRelsPath)!);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelCalcChain"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain"),
                new XAttribute("Target", "calcChain.xml")));
            ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalPrinterSettingsPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/printerSettings/printerSettings1.bin",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var pageSetup = worksheetXml.Root!.Element(worksheetNs + "pageSetup");
            if (pageSetup is null)
            {
                pageSetup = new XElement(worksheetNs + "pageSetup", new XAttribute("paperSize", "1"), new XAttribute("orientation", "portrait"));
                worksheetXml.Root.Add(pageSetup);
            }

            pageSetup.SetAttributeValue(relNs + "id", "rIdPrinterSettings1");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdPrinterSettings1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings"),
                new XAttribute("Target", "../printerSettings/printerSettings1.bin")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            archive.GetEntry("xl/printerSettings/printerSettings1.bin")?.Delete();
            var settingsEntry = archive.CreateEntry("xl/printerSettings/printerSettings1.bin");
            using var settingsStream = settingsEntry.Open();
            settingsStream.Write([0x46, 0x58, 0x4C, 0x50, 0x52, 0x4E]);
        }

        packageStream.Position = 0;
    }

    private static void AddHeaderFooterLegacyDrawingPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            archive.GetEntry("xl/drawings/vmlDrawing1.vml")?.Delete();
            var vmlEntry = archive.CreateEntry("xl/drawings/vmlDrawing1.vml");
            using (var writer = new StreamWriter(vmlEntry.Open(), Encoding.UTF8))
            {
                writer.Write("""
                    <xml xmlns:v="urn:schemas-microsoft-com:vml"
                         xmlns:o="urn:schemas-microsoft-com:office:office"
                         xmlns:x="urn:schemas-microsoft-com:office:excel">
                      <v:shape id="LH" type="#_x0000_t75">
                        <v:imagedata o:relid="rIdImage1" o:title="Header"/>
                      </v:shape>
                    </xml>
                    """);
            }
            archive.GetEntry("xl/media/headerFooterImage1.png")?.Delete();
            var imageEntry = archive.CreateEntry("xl/media/headerFooterImage1.png");
            using (var imageStream = imageEntry.Open())
                imageStream.Write(MinimalPngBytes());

            ReplacePackageXml(archive, "xl/drawings/_rels/vmlDrawing1.vml.rels", new XDocument(
                new XElement(
                    packageRelNs + "Relationships",
                    new XElement(
                        packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdImage1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                        new XAttribute("Target", "../media/headerFooterImage1.png")))));

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdHeaderFooterDrawing1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
                new XAttribute("Target", "../drawings/vmlDrawing1.vml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "legacyDrawingHF",
                new XAttribute(relNs + "id", "rIdHeaderFooterDrawing1")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/drawings/vmlDrawing1.vml",
                "application/vnd.openxmlformats-officedocument.vmlDrawing");
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/media/headerFooterImage1.png",
                "image/png");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        packageStream.Position = 0;
    }

}
