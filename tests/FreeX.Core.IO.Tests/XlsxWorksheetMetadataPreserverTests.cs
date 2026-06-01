using System.IO.Compression;
using System.Text;
using System.Xml;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetMetadataPreserverTests
{
    [Fact]
    public void Preserve_PlainSourceWorksheetSkipsTargetWorksheetLoad()
    {
        using var sourcePackage = CreateWorkbookPackage(CreatePlainWorksheetXml());
        using var targetPackage = CreateWorkbookPackage("<not-valid-xml");
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
        var workbook = new Workbook("Plain worksheet");
        workbook.AddSheet("Sheet1");

        var act = () => XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook);

        act.Should().NotThrow("plain worksheet metadata should be rejected before the target worksheet XML is loaded");
    }

    [Fact]
    public void Preserve_SourceWithNativeCellMetadataLoadsTargetWorksheet()
    {
        using var sourcePackage = CreateWorkbookPackage(CreateWorksheetWithNativeCellMetadataXml());
        using var targetPackage = CreateWorkbookPackage("<not-valid-xml");
        using var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
        using var targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
        var workbook = new Workbook("Native cell metadata");
        workbook.AddSheet("Sheet1");

        var act = () => XlsxWorksheetMetadataPreserver.Preserve(sourceArchive, targetArchive, workbook);

        act.Should().Throw<XmlException>("native cell metadata still requires loading and merging the target worksheet XML");
    }

    private static MemoryStream CreateWorkbookPackage(string worksheetXml)
    {
        var package = new MemoryStream();
        using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Sheet1" sheetId="1" r:id="rId1" />
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1"
                                Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"
                                Target="worksheets/sheet1.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        package.Position = 0;
        return package;
    }

    private static string CreatePlainWorksheetXml() => """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                   xmlns:x14ac="http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac">
          <dimension ref="A1:B2" />
          <sheetViews>
            <sheetView workbookViewId="0" />
          </sheetViews>
          <sheetFormatPr defaultRowHeight="15" x14ac:dyDescent="0.25" />
          <sheetData>
            <row r="1" spans="1:2" x14ac:dyDescent="0.25">
              <c r="A1"><v>1</v></c>
              <c r="B1" t="str"><v>plain</v></c>
            </row>
            <row r="2" spans="1:2" x14ac:dyDescent="0.25">
              <c r="A2" s="1"><v>2</v></c>
              <c r="B2"><f>A1+A2</f><v>3</v></c>
            </row>
          </sheetData>
          <pageMargins left="0.7" right="0.7" top="0.75" bottom="0.75" header="0.3" footer="0.3" />
        </worksheet>
        """;

    private static string CreateWorksheetWithNativeCellMetadataXml() => """
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <sheetData>
            <row r="1">
              <c r="A1" cm="1"><v>1</v></c>
            </row>
          </sheetData>
        </worksheet>
        """;

    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(text);
    }
}
