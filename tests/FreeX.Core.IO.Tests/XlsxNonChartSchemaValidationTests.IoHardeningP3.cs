using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    // -------------------------------------------------------------------------
    // Fix 1: Duplicate zip entry names defeat sanitizers
    // -------------------------------------------------------------------------

    [Fact]
    public void LoadWorkbook_WithDuplicateZipEntryNames_ThrowsWorkbookInvalidException()
    {
        using var package = CreatePackageWithDuplicateEntry();

        var adapter = new XlsxFileAdapter();
        Action act = () => adapter.Load(package);

        act.Should().Throw<WorkbookInvalidException>(
            "a package with duplicate entry names must be rejected before any sanitization is attempted");
    }

    [Fact]
    public void LoadWorkbook_ValidPackage_IsNotRejectedByDuplicateEntryCheck()
    {
        var workbook = new Workbook("Valid");
        workbook.AddSheet("Sheet1").SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 1, 1), new NumberValue(1));
        using var stream = Save(workbook);
        stream.Position = 0;

        var adapter = new XlsxFileAdapter();
        Action act = () => adapter.Load(stream);

        act.Should().NotThrow("a valid package must not be affected by the duplicate-entry guard");
    }

    // -------------------------------------------------------------------------
    // Fix 2: Patch-save mishandles rows without r attributes
    // -------------------------------------------------------------------------

    [Fact]
    public void LoadedWorkbookPatchSave_WithRLessRows_FallsBackToFullSaveAndProducesSchemaValidWorkbook()
    {
        // Build a source package whose first worksheet contains r-less <row> elements (schema-valid;
        // produced by streaming writers).  Editing a cell in that sheet must not corrupt the file.
        using var source = CreateRLessRowSourcePackage();
        SchemaErrors(source).Should().BeEmpty("the hand-crafted fixture must itself be schema-valid");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        // Insert a NEW cell in column 2 of row 1 — this cell did not exist in the source,
        // so it becomes an InsertedLiteralValue change that goes through ApplyChanges (not
        // the streaming path, which only handles existing-cell updates).  ApplyChanges must
        // detect r-less rows and bail out to the full-save fallback rather than duplicating rows.
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("inserted"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        // Patch-save must bail out to a full save when the worksheet contains r-less rows and
        // an InsertedLiteralValue change requires FindOrCreateRow.
        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave,
            "patch-save must fall back to full-save when the worksheet contains r-less rows");

        SchemaErrors(saved).Should().BeEmpty("the saved file must be schema-valid");

        // Reload and verify the inserted cell landed correctly with no duplicate rows.
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        reloaded.GetSheetAt(0).GetCell(1, 2)!.Value.Should().Be(new TextValue("inserted"));

        // Verify no duplicate row elements for any logical row.
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var rowNumbers = worksheetXml
            .Root!
            .Element(ns + "sheetData")!
            .Elements(ns + "row")
            .Select(row => row.Attribute("r")?.Value)
            .Where(r => r is not null)
            .ToList();
        rowNumbers.Should().OnlyHaveUniqueItems("there must be at most one <row> element per logical row number");
    }

    // -------------------------------------------------------------------------
    // Fix 3: Case-sensitive chart boolean parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void ChartAxisDeleteFlag_ParsedFromUpperCaseTrue_RoundTripsAsTrue()
    {
        // Use TryReadSupportedChart directly with chart XML that uses val="True" (capital T)
        // on the axis delete element — which is a valid OPC boolean but was rejected by the
        // case-sensitive literal match `value is "1" or "true"`.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="1"/>
                    <c:axId val="2"/>
                  </c:barChart>
                  <c:catAx>
                    <c:axId val="1"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="2"/>
                    <c:delete val="True"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="2"/>
                    <c:axPos val="l"/>
                    <c:crossAx val="1"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue("the chart XML must be parseable");

        chart.HideXAxis.Should().BeTrue(
            "an axis with delete val=\"True\" (capital T) must be parsed as hidden=true");
    }

    // -------------------------------------------------------------------------
    // Fixture helpers
    // -------------------------------------------------------------------------

    private static MemoryStream CreatePackageWithDuplicateEntry()
    {
        // Build a minimal xlsx-shaped zip with two entries sharing the same name.
        // This is the hostile-package pattern: a sanitizer deletes the first entry,
        // but a second same-named entry silently remains.
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Write [Content_Types].xml twice with the same name.
            void WriteEntry(string path, string content)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }

            const string contentTypes =
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\" />" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\" />" +
                "</Types>";

            WriteEntry("[Content_Types].xml", contentTypes);
            // Second entry with the same name — this is what a hostile package does.
            WriteEntry("[Content_Types].xml", contentTypes);
        }

        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateRLessRowSourcePackage()
    {
        // Start with a normal single-sheet workbook, then replace the worksheet XML
        // with a version whose <row> elements have no r attribute.
        var workbook = new Workbook("RLessRows");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        var stream = Save(workbook);
        ReplaceWorksheetWithRLessRows(stream);
        stream.Position = 0;
        return stream;
    }

    private static void ReplaceWorksheetWithRLessRows(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        // Hand-craft worksheet XML with r-less rows — schema-valid per ECMA-376.
        // The three rows contain cells A1=1, A2=2, A3=3 but omit the row r attribute.
        var rLessWorksheetXml = new XDocument(
            new XDeclaration("1.0", "UTF-8", "yes"),
            new XElement(
                ns + "worksheet",
                new XElement(
                    ns + "sheetData",
                    // Row 1: no r attribute, cell A1
                    new XElement(
                        ns + "row",
                        new XElement(ns + "c",
                            new XAttribute("r", "A1"),
                            new XAttribute("t", "n"),
                            new XElement(ns + "v", "1"))),
                    // Row 2: no r attribute, cell A2
                    new XElement(
                        ns + "row",
                        new XElement(ns + "c",
                            new XAttribute("r", "A2"),
                            new XAttribute("t", "n"),
                            new XElement(ns + "v", "2"))),
                    // Row 3: no r attribute, cell A3
                    new XElement(
                        ns + "row",
                        new XElement(ns + "c",
                            new XAttribute("r", "A3"),
                            new XAttribute("t", "n"),
                            new XElement(ns + "v", "3"))))));

        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", rLessWorksheetXml);
    }

}
