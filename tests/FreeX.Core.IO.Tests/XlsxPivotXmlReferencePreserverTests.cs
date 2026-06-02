using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxPivotXmlReferencePreserverTests
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // Per CT_Workbook the element order is ...sheets, ..., customWorkbookViews, pivotCaches,
    // smartTagPr, ..., extLst. Inserting <pivotCaches> before <sheets> is schema-invalid and makes
    // Excel reject the workbook and drop every PivotTable, which is what corrupted the user's file.
    [Fact]
    public void Preserve_InsertsPivotCachesAfterSheets_AndBeforeTrailingElements()
    {
        using var sourcePackage = new MemoryStream();
        using (var archive = new ZipArchive(sourcePackage, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "xl/workbook.xml",
                $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\">" +
                "<sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "<pivotCaches><pivotCache cacheId=\"1\" r:id=\"rId2\"/></pivotCaches>" +
                "</workbook>");
        }

        sourcePackage.Position = 0;
        using var targetPackage = new MemoryStream();
        using (var archive = new ZipArchive(targetPackage, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "xl/workbook.xml",
                $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Rel}\">" +
                "<sheets><sheet name=\"Data\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
                "<extLst/>" +
                "</workbook>");
        }

        targetPackage.Position = 0;
        using (var source = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true))
        {
            XlsxPivotXmlReferencePreserver.Preserve(source, target);
        }

        targetPackage.Position = 0;
        using var result = new ZipArchive(targetPackage, ZipArchiveMode.Read);
        XNamespace main = Main;
        var children = XDocument.Load(result.GetEntry("xl/workbook.xml")!.Open()).Root!
            .Elements()
            .Select(element => element.Name.LocalName)
            .ToList();

        children.Should().Contain("pivotCaches");
        children.IndexOf("pivotCaches").Should().BeGreaterThan(children.IndexOf("sheets"),
            "pivotCaches must come after sheets per CT_Workbook");
        children.IndexOf("pivotCaches").Should().BeLessThan(children.IndexOf("extLst"),
            "pivotCaches must come before extLst per CT_Workbook");
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }
}
