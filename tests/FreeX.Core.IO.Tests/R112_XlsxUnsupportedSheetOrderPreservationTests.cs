using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R112-io-preserver-order-1: XlsxUnsupportedSheetReferencePreserver.Preserve() must re-insert a
/// preserved macro/dialog sheet (a type FreeX never models as a live Sheet) at its ORIGINAL ordinal
/// position among &lt;sheets&gt;, not unconditionally append it to the end. FreeX itself never
/// writes xl/macroSheets or xl/dialogSheets parts (Sheet.Kind has no such case -- see
/// src/FreeX.Core.Model/Sheet.cs), so a genuine write-then-read-back round-trip fixture is
/// impossible for this defect: the only way a macro/dialog sheet ever appears is via a
/// foreign-authored (real Excel) package that FreeX loads and then re-saves. The fixture below
/// hand-authors that foreign package, mirroring the pattern already established and reviewed for
/// this exact preserver in XlsxPackagePreservingSaveValidationTests.CreatePackageWithUnsupportedSheetSidecars
/// (which itself places its unsupported sheets at the tail of &lt;sheets&gt; and therefore never
/// exercised the middle-of-strip case this defect is about).
/// </summary>
public sealed class R112_XlsxUnsupportedSheetOrderPreservationTests
{
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorkbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    [Fact]
    public void R112_LoadEditSave_PreservesMacroSheetAtOriginalMiddlePosition()
    {
        // Source tab order (as authored by Excel): [Data, Macro1, Summary].
        var sourceBytes = CreatePackageWithMiddleMacroSheet();

        AssertSourceSheetOrder(sourceBytes, "Data", "Macro1", "Summary");

        var savedBytes = SaveAfterLoadingAndEditing(sourceBytes, workbook =>
        {
            // FreeX never modeled Macro1 -- only the two ordinary worksheets are live Sheets.
            workbook.Sheets.Select(s => s.Name).Should().Equal("Data", "Summary");

            // An edit that is unrelated to sheet ordering, forcing SaveInternal's full rebuild path
            // (the same path the reported defect fires on for every such edit).
            var sheet = workbook.GetSheet("Data");
            sheet.Should().NotBeNull();
            sheet!.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("R112 edit"));
        });

        AssertRoundTripCellValue(savedBytes, "Data", 2, 2, new TextValue("R112 edit"));

        // The defect: Preserve() used to unconditionally Add() the preserved <sheet> to the END of
        // <sheets>, producing [Data, Summary, Macro1] instead of the original [Data, Macro1, Summary].
        AssertSourceSheetOrder(savedBytes, "Data", "Macro1", "Summary");
    }

    [Fact]
    public void R112_LoadEditSave_NoRegression_TrailingUnsupportedSheetsStillAppendInSourceOrder()
    {
        // Sibling/no-regression coverage: when the unsupported sheets already sit at the END of the
        // source tab order (the case XlsxPackagePreservingSaveValidationTests already covers), the
        // fix must not disturb that existing, correct behaviour -- including preserving the RELATIVE
        // order between multiple trailing preserved sheets (chartsheet, dialog sheet, macro sheet).
        var sourceBytes = CreatePackageWithTrailingUnsupportedSheets();

        AssertSourceSheetOrder(sourceBytes, "Data", "Chart Sidecar", "Dialog Sidecar", "Macro Sidecar");

        var savedBytes = SaveAfterLoadingAndEditing(sourceBytes, workbook =>
        {
            var sheet = workbook.GetSheet("Data");
            sheet.Should().NotBeNull();
            sheet!.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("R112 sibling edit"));
        });

        AssertRoundTripCellValue(savedBytes, "Data", 3, 1, new TextValue("R112 sibling edit"));
        AssertSourceSheetOrder(savedBytes, "Data", "Chart Sidecar", "Dialog Sidecar", "Macro Sidecar");
    }

    private static byte[] CreatePackageWithMiddleMacroSheet()
    {
        var packageBytes = CreateClosedXmlWorkbook("Data", "Summary");
        using var package = CreateExpandablePackage(packageBytes);
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml", "[Content_Types].xml");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/macroSheets/sheet1.xml",
                "application/vnd.ms-excel.macrosheet+xml");
            ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);

            AppendRelationship(
                archive,
                "xl/_rels/workbook.xml.rels",
                "rIdMacro1",
                "http://schemas.microsoft.com/office/2006/relationships/xlMacrosheet",
                "macroSheets/sheet1.xml");

            WriteTextEntry(
                archive,
                "xl/macroSheets/sheet1.xml",
                """
                <macrosheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheetData/>
                </macrosheet>
                """);

            // Insert the macro sheet's <sheet> element BETWEEN "Data" and "Summary" -- this is the
            // ordinal position the fix must reproduce on save.
            var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml", "xl/workbook.xml");
            var sheetsElement = workbookXml.Root!.Element(WorkbookNs + "sheets")!;
            var dataSheet = sheetsElement.Elements(WorkbookNs + "sheet")
                .Single(element => string.Equals((string?)element.Attribute("name"), "Data", StringComparison.Ordinal));
            dataSheet.AddAfterSelf(new XElement(
                WorkbookNs + "sheet",
                new XAttribute("name", "Macro1"),
                new XAttribute("sheetId", "99"),
                new XAttribute(RelNs + "id", "rIdMacro1")));
            ReplaceXml(archive, "xl/workbook.xml", workbookXml);
        }

        return package.ToArray();
    }

    private static byte[] CreatePackageWithTrailingUnsupportedSheets()
    {
        var packageBytes = CreateClosedXmlWorkbook("Data");
        using var package = CreateExpandablePackage(packageBytes);
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml", "[Content_Types].xml");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/chartsheets/sheet1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/dialogSheets/sheet2.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.dialogsheet+xml");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/macroSheets/sheet3.xml",
                "application/vnd.ms-excel.macrosheet+xml");
            ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);

            AppendWorkbookSheet(archive, "Chart Sidecar", "42", "rIdChartSidecar");
            AppendWorkbookSheet(archive, "Dialog Sidecar", "43", "rIdDialogSidecar");
            AppendWorkbookSheet(archive, "Macro Sidecar", "44", "rIdMacroSidecar");
            AppendRelationship(
                archive,
                "xl/_rels/workbook.xml.rels",
                "rIdChartSidecar",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chartsheet",
                "chartsheets/sheet1.xml");
            AppendRelationship(
                archive,
                "xl/_rels/workbook.xml.rels",
                "rIdDialogSidecar",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/dialogsheet",
                "dialogSheets/sheet2.xml");
            AppendRelationship(
                archive,
                "xl/_rels/workbook.xml.rels",
                "rIdMacroSidecar",
                "http://schemas.microsoft.com/office/2006/relationships/xlMacrosheet",
                "macroSheets/sheet3.xml");

            WriteTextEntry(
                archive,
                "xl/chartsheets/sheet1.xml",
                """
                <chartsheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheetViews>
                    <sheetView workbookViewId="0"/>
                  </sheetViews>
                </chartsheet>
                """);
            WriteTextEntry(
                archive,
                "xl/dialogSheets/sheet2.xml",
                """
                <dialogsheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                             xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheetViews>
                    <sheetView workbookViewId="0"/>
                  </sheetViews>
                </dialogsheet>
                """);
            WriteTextEntry(
                archive,
                "xl/macroSheets/sheet3.xml",
                """
                <macrosheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheetData/>
                </macrosheet>
                """);
        }

        return package.ToArray();
    }

    private static void AssertSourceSheetOrder(byte[] packageBytes, params string[] expectedNames)
    {
        using var package = new MemoryStream(packageBytes, writable: false);
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml", "xl/workbook.xml");
        var actualNames = workbookXml.Root!
            .Element(WorkbookNs + "sheets")!
            .Elements(WorkbookNs + "sheet")
            .Select(element => (string?)element.Attribute("name"))
            .ToArray();
        actualNames.Should().Equal(expectedNames);
    }

    private static byte[] SaveAfterLoadingAndEditing(byte[] sourceBytes, Action<Workbook> edit)
    {
        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream(sourceBytes, writable: false);
        var workbook = adapter.Load(source);
        edit(workbook);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        return saved.ToArray();
    }

    private static void AssertRoundTripCellValue(byte[] packageBytes, string sheetName, uint row, uint col, ScalarValue value)
    {
        using var package = new MemoryStream(packageBytes, writable: false);
        var workbook = new XlsxFileAdapter().Load(package);
        var sheet = workbook.GetSheet(sheetName);
        sheet.Should().NotBeNull();
        sheet!.GetValue(row, col).Should().Be(value);
    }

    private static MemoryStream CreateExpandablePackage(byte[] packageBytes)
    {
        var package = new MemoryStream(packageBytes.Length + 4096);
        package.Write(packageBytes);
        package.Position = 0;
        return package;
    }

    private static byte[] CreateClosedXmlWorkbook(params string[] sheetNames)
    {
        using var package = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            for (var index = 0; index < sheetNames.Length; index++)
            {
                var worksheet = workbook.Worksheets.Add(sheetNames[index]);
                worksheet.Cell(1, 1).Value = sheetNames[index];
                worksheet.Cell(2, 1).Value = index + 1;
            }

            workbook.SaveAs(package);
        }

        return package.ToArray();
    }

    private static void AddContentTypeOverride(XDocument contentTypesXml, string partName, string contentType)
    {
        contentTypesXml.Root!.Elements(ContentTypeNs + "Override")
            .Where(element => string.Equals((string?)element.Attribute("PartName"), partName, StringComparison.OrdinalIgnoreCase))
            .Remove();
        contentTypesXml.Root.Add(new XElement(
            ContentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private static void AppendRelationship(
        ZipArchive archive,
        string relationshipPartPath,
        string id,
        string type,
        string target,
        string? targetMode = null)
    {
        var relationshipsXml = archive.GetEntry(relationshipPartPath) is { } entry
            ? XlsxPackageTestFixtures.LoadPackageXml(entry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        var relationship = new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target));
        if (targetMode is not null)
            relationship.SetAttributeValue("TargetMode", targetMode);

        relationshipsXml.Root!.Add(relationship);
        ReplaceXml(archive, relationshipPartPath, relationshipsXml);
    }

    private static void AppendWorkbookSheet(ZipArchive archive, string name, string sheetId, string relationshipId)
    {
        var workbookXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/workbook.xml", "xl/workbook.xml");
        workbookXml.Root!
            .Element(WorkbookNs + "sheets")!
            .Add(new XElement(
                WorkbookNs + "sheet",
                new XAttribute("name", name),
                new XAttribute("sheetId", sheetId),
                new XAttribute(RelNs + "id", relationshipId)));
        ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void ReplaceXml(ZipArchive archive, string path, XDocument xml)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        xml.Save(writer, System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static void WriteTextEntry(ZipArchive archive, string path, string content)
    {
        archive.GetEntry(path)?.Delete();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }
}
