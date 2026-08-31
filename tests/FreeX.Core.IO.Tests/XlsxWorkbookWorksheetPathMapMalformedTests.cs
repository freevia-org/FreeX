using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorkbookWorksheetPathMapMalformedTests
{
    [Fact]
    public void TryCreate_StrictDuplicateRelationshipIds_ReturnsNullInsteadOfThrowing()
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace documentRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        using var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteXml(archive, "xl/workbook.xml", new XDocument(
                new XElement(workbookNs + "workbook",
                    new XElement(workbookNs + "sheets",
                        new XElement(workbookNs + "sheet",
                            new XAttribute("name", "Sheet1"),
                            new XAttribute(documentRelNs + "id", "rId1"))))));
            WriteXml(archive, "xl/_rels/workbook.xml.rels", new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Target", "worksheets/sheet1.xml")),
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Target", "worksheets/sheet2.xml")))));
        }

        package.Position = 0;
        using var readArchive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);

        var act = () => XlsxWorkbookWorksheetPathMap.TryCreate(
            readArchive,
            rejectDuplicateRelationshipIds: true);

        act.Should().NotThrow();
        act().Should().BeNull();
    }

    private static void WriteXml(ZipArchive archive, string path, XDocument document)
    {
        using var stream = archive.CreateEntry(path).Open();
        document.Save(stream);
    }
}
