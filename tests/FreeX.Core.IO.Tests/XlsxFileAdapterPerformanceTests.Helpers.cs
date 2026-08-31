using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxFileAdapterPerformanceTests
{
    private const int DenseSheetCount = 8;
    private const int DenseRowsPerSheet = 80;
    private const int DenseColumnsPerSheet = 24;
    private const int StyleOnlySaveSheetCount = 2;
    private const int StyleOnlySaveRowsPerSheet = 600;
    private const int StyleOnlySaveColumnsPerSheet = 72;
    private const int StyleOnlySaveRunWidth = 8;
    private const int WorksheetNativeMetadataSheetCount = 8;
    private const int WorksheetNativeMetadataRowsPerSheet = 40;
    private const int WorksheetReplayMetadataSheetCount = 8;
    private const int WorksheetReplayMetadataRowsPerSheet = 40;
    private const int AdvancedConditionalFormatRulesPerSheet = 40;
    private const int WorksheetSingleXmlCellsPerSheet = 40;
    private const int IgnoredErrorStyleOnlyRows = 800;
    private const int IgnoredErrorStyleOnlyValueColumns = 30;
    private const int IgnoredErrorStyleOnlyStyleColumns = 10;
    private const int IgnoredErrorStyleOnlyIgnoredRanges = 800;
    private const int IgnoredErrorSaveRows = 300;
    private const int IgnoredErrorSaveColumns = 40;
    private const int GeneratedStyleHeavySheetCount = 3;
    private const int GeneratedStyleHeavyRowsPerSheet = 400;
    private const int GeneratedStyleHeavyValueColumnsPerSheet = 12;
    private const int GeneratedStyleHeavyStyleOnlyColumnsPerSheet = 160;
    private const int GeneratedStyleHeavyStyleOnlyStartColumn = GeneratedStyleHeavyValueColumnsPerSheet + 2;
    private const int GeneratedStyleHeavyDrawingShapePairs = 24;
    private const int GeneratedStyleHeavyChartExSourceRows = 24;
    private const int GeneratedStyleHeavyPivotChartSourceRows = 24;
    private const int LargePatchBaselineRows = 260_000;

    private static byte[] CreateDenseXlsxPackage()
    {
        using var workbook = new XLWorkbook();
        for (var sheetIndex = 1; sheetIndex <= DenseSheetCount; sheetIndex++)
        {
            var sheet = workbook.Worksheets.Add($"Sheet {sheetIndex}");
            for (var row = 1; row <= DenseRowsPerSheet; row++)
            {
                for (var col = 1; col <= DenseColumnsPerSheet; col++)
                {
                    var cell = sheet.Cell(row, col);
                    cell.Value = row * col + sheetIndex;
                    if ((row + col) % 17 == 0)
                    {
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, 242, 204);
                    }
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateIgnoredErrorAndStyleOnlyMetadataPackage()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Metadata");
        for (var row = 1; row <= IgnoredErrorStyleOnlyRows; row++)
        {
            for (var col = 1; col <= IgnoredErrorStyleOnlyValueColumns; col++)
                sheet.Cell(row, col).Value = row * col;

            for (var col = IgnoredErrorStyleOnlyValueColumns + 2;
                 col < IgnoredErrorStyleOnlyValueColumns + 2 + IgnoredErrorStyleOnlyStyleColumns;
                 col++)
            {
                var styleOnlyCell = sheet.Cell(row, col);
                styleOnlyCell.Style.Fill.BackgroundColor = XLColor.FromArgb(221, 235, 247);
                styleOnlyCell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            var worksheetXml = LoadZipEntryXml(worksheetEntry);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            worksheetXml.Root!.Element(ns + "ignoredErrors")?.Remove();

            var ignoredErrors = new XElement(ns + "ignoredErrors");
            for (var rangeIndex = 1; rangeIndex <= IgnoredErrorStyleOnlyIgnoredRanges; rangeIndex++)
            {
                ignoredErrors.Add(new XElement(
                    ns + "ignoredError",
                    new XAttribute("sqref", $"A{rangeIndex}:AD{rangeIndex + 999}"),
                    new XAttribute("numberStoredAsText", "1")));
            }

            worksheetXml.Root.Add(ignoredErrors);
            ReplaceZipEntryXml(archive, worksheetEntry.FullName, worksheetXml);
        }

        return stream.ToArray();
    }

    private static byte[] CreateGeneratedStyleHeavyXlsxPackage(
        bool formulaMarker = false,
        bool internalHyperlinkMarker = false,
        bool legacyCommentMarker = false,
        bool structuredTableMarker = false,
        bool filteredStructuredTableMarker = false,
        bool sparklineMarker = false)
    {
        var hasStructuredTable = structuredTableMarker || filteredStructuredTableMarker;
        using var stream = new MemoryStream(capacity: 8 * 1024 * 1024);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(
                archive,
                "[Content_Types].xml",
                writer => WriteGeneratedStyleHeavyContentTypes(writer, legacyCommentMarker, hasStructuredTable));
            WriteTextEntry(archive, "_rels/.rels", WriteGeneratedStyleHeavyRootRelationships);
            WriteTextEntry(archive, "xl/workbook.xml", WriteGeneratedStyleHeavyWorkbook);
            WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", WriteGeneratedStyleHeavyWorkbookRelationships);
            WriteTextEntry(archive, "xl/styles.xml", WriteGeneratedStyleHeavyStyles);

            for (var sheetIndex = 1; sheetIndex <= GeneratedStyleHeavySheetCount; sheetIndex++)
            {
                var path = string.Create(
                    CultureInfo.InvariantCulture,
                    $"xl/worksheets/sheet{sheetIndex}.xml");
                WriteTextEntry(
                    archive,
                    path,
                    writer => WriteGeneratedStyleHeavyWorksheet(
                        writer,
                        sheetIndex,
                        formulaMarker,
                        internalHyperlinkMarker,
                        legacyCommentMarker,
                        hasStructuredTable,
                        sparklineMarker));
            }

            if (legacyCommentMarker || hasStructuredTable)
            {
                WriteTextEntry(
                    archive,
                    "xl/worksheets/_rels/sheet1.xml.rels",
                    writer => WriteGeneratedStyleHeavyWorksheetRelationships(
                        writer,
                        legacyCommentMarker,
                        hasStructuredTable));
                if (legacyCommentMarker)
                {
                    WriteTextEntry(archive, "xl/comments1.xml", WriteGeneratedStyleHeavyComments);
                    WriteTextEntry(archive, "xl/drawings/vmlDrawing1.vml", WriteGeneratedStyleHeavyVmlDrawing);
                }

                if (hasStructuredTable)
                    WriteTextEntry(
                        archive,
                        "xl/tables/table1.xml",
                        writer => WriteGeneratedStyleHeavyStructuredTable(writer, filteredStructuredTableMarker));
            }
        }

        return stream.ToArray();
    }

    private static byte[] AddGeneratedStyleHeavyHeaderFooterLegacyDrawingPackage(byte[] package)
    {
        using var stream = new MemoryStream(capacity: package.Length + 4096);
        stream.Write(package, 0, package.Length);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            archive.GetEntry("xl/drawings/vmlDrawing1.vml")?.Delete();
            WriteTextEntry(archive, "xl/drawings/vmlDrawing1.vml", writer => writer.Write(
                """
                <xml xmlns:v="urn:schemas-microsoft-com:vml"
                     xmlns:o="urn:schemas-microsoft-com:office:office"
                     xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="LH" type="#_x0000_t75">
                    <v:imagedata o:relid="rIdImage1" o:title="Header"/>
                  </v:shape>
                </xml>
                """));

            archive.GetEntry("xl/media/headerFooterImage1.png")?.Delete();
            var imageEntry = archive.CreateEntry("xl/media/headerFooterImage1.png");
            using (var imageStream = imageEntry.Open())
                imageStream.Write(MinimalPngBytes());

            ReplaceZipEntryXml(archive, "xl/drawings/_rels/vmlDrawing1.vml.rels", new XDocument(
                new XElement(
                    packageRelNs + "Relationships",
                    new XElement(
                        packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdImage1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                        new XAttribute("Target", "../media/headerFooterImage1.png")))));

            const string worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadZipEntryXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdHeaderFooterDrawing1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
                new XAttribute("Target", "../drawings/vmlDrawing1.vml")));
            ReplaceZipEntryXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadZipEntryXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "legacyDrawingHF",
                new XAttribute(relNs + "id", "rIdHeaderFooterDrawing1")));
            ReplaceZipEntryXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var contentTypesXml = LoadZipEntryXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/drawings/vmlDrawing1.vml",
                "application/vnd.openxmlformats-officedocument.vmlDrawing");
            AddContentTypeOverride(contentTypesXml, "/xl/media/headerFooterImage1.png", "image/png");
            ReplaceZipEntryXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddGeneratedStyleHeavyDrawingShapePackage(byte[] package)
    {
        using var stream = new MemoryStream(capacity: package.Length + 24 * 1024);
        stream.Write(package, 0, package.Length);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            archive.GetEntry("xl/drawings/drawing1.xml")?.Delete();
            WriteTextEntry(archive, "xl/drawings/drawing1.xml", WriteGeneratedStyleHeavyDrawingShapes);
            ReplaceZipEntryXml(
                archive,
                "xl/drawings/_rels/drawing1.xml.rels",
                new XDocument(new XElement(packageRelNs + "Relationships")));

            const string worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadZipEntryXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdGeneratedDrawingShapes1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            ReplaceZipEntryXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadZipEntryXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "drawing",
                new XAttribute(relNs + "id", "rIdGeneratedDrawingShapes1")));
            ReplaceZipEntryXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var contentTypesXml = LoadZipEntryXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/drawings/drawing1.xml",
                "application/vnd.openxmlformats-officedocument.drawing+xml");
            ReplaceZipEntryXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddGeneratedStyleHeavyChartExPackage(byte[] package)
    {
        using var stream = new MemoryStream(capacity: package.Length + 16 * 1024);
        stream.Write(package, 0, package.Length);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            archive.GetEntry("xl/drawings/drawing1.xml")?.Delete();
            WriteTextEntry(archive, "xl/drawings/drawing1.xml", WriteGeneratedStyleHeavyChartExDrawing);
            ReplaceZipEntryXml(archive, "xl/drawings/_rels/drawing1.xml.rels", new XDocument(
                new XElement(
                    packageRelNs + "Relationships",
                    new XElement(
                        packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdGeneratedChartEx1"),
                        new XAttribute("Type", "http://schemas.microsoft.com/office/2014/relationships/chartEx"),
                        new XAttribute("Target", "../charts/chart1.xml")))));

            archive.GetEntry("xl/charts/chart1.xml")?.Delete();
            WriteTextEntry(archive, "xl/charts/chart1.xml", WriteGeneratedStyleHeavyChartExChart);
            ReplaceZipEntryXml(archive, "xl/charts/_rels/chart1.xml.rels", new XDocument(
                new XElement(
                    packageRelNs + "Relationships",
                    new XElement(
                        packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdGeneratedChartExStyle1"),
                        new XAttribute("Type", "http://schemas.microsoft.com/office/2011/relationships/chartStyle"),
                        new XAttribute("Target", "style1.xml")),
                    new XElement(
                        packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdGeneratedChartExColors1"),
                        new XAttribute("Type", "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle"),
                        new XAttribute("Target", "colors1.xml")))));
            WriteTextEntry(archive, "xl/charts/style1.xml", WriteGeneratedStyleHeavyChartExStyle);
            WriteTextEntry(archive, "xl/charts/colors1.xml", WriteGeneratedStyleHeavyChartExColors);

            const string worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadZipEntryXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdGeneratedChartExDrawing1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            ReplaceZipEntryXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadZipEntryXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "drawing",
                new XAttribute(relNs + "id", "rIdGeneratedChartExDrawing1")));
            ReplaceZipEntryXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var contentTypesXml = LoadZipEntryXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/drawings/drawing1.xml",
                "application/vnd.openxmlformats-officedocument.drawing+xml");
            AddContentTypeOverride(contentTypesXml, "/xl/charts/chart1.xml", "application/vnd.ms-office.chartex+xml");
            AddContentTypeOverride(contentTypesXml, "/xl/charts/style1.xml", "application/vnd.ms-office.chartstyle+xml");
            AddContentTypeOverride(contentTypesXml, "/xl/charts/colors1.xml", "application/vnd.ms-office.chartcolorstyle+xml");
            ReplaceZipEntryXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        return stream.ToArray();
    }

    private static byte[] AddGeneratedStyleHeavyPivotChartPackage(byte[] package)
    {
        using var stream = new MemoryStream(capacity: package.Length + 20 * 1024);
        stream.Write(package, 0, package.Length);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            archive.GetEntry("xl/drawings/drawing1.xml")?.Delete();
            WriteTextEntry(archive, "xl/drawings/drawing1.xml", WriteGeneratedStyleHeavyPivotChartDrawing);
            ReplaceZipEntryXml(archive, "xl/drawings/_rels/drawing1.xml.rels", new XDocument(
                new XElement(
                    packageRelNs + "Relationships",
                    new XElement(
                        packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdGeneratedPivotChart1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"),
                        new XAttribute("Target", "../charts/chart1.xml")))));

            archive.GetEntry("xl/charts/chart1.xml")?.Delete();
            WriteTextEntry(archive, "xl/charts/chart1.xml", WriteGeneratedStyleHeavyPivotChart);

            ReplaceZipEntryXml(archive, "xl/pivotTables/_rels/pivotTable1.xml.rels", new XDocument(
                new XElement(
                    packageRelNs + "Relationships",
                    new XElement(
                        packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdGeneratedPivotCache1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition"),
                        new XAttribute("Target", "../pivotCache/pivotCacheDefinition1.xml")))));
            WriteTextEntry(archive, "xl/pivotTables/pivotTable1.xml", WriteGeneratedStyleHeavyPivotTable);
            WriteTextEntry(archive, "xl/pivotCache/pivotCacheDefinition1.xml", WriteGeneratedStyleHeavyPivotCacheDefinition);

            var workbookXml = LoadZipEntryXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Elements(workbookNs + "pivotCaches").Remove();
            var pivotCaches = new XElement(
                workbookNs + "pivotCaches",
                new XElement(
                    workbookNs + "pivotCache",
                    new XAttribute("cacheId", "1"),
                    new XAttribute(relNs + "id", "rIdGeneratedPivotCache1")));
            if (workbookXml.Root.Element(workbookNs + "sheets") is { } sheets)
                sheets.AddBeforeSelf(pivotCaches);
            else
                workbookXml.Root.Add(pivotCaches);
            ReplaceZipEntryXml(archive, "xl/workbook.xml", workbookXml);

            var workbookRelsXml = LoadZipEntryXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdGeneratedPivotCache1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition"),
                new XAttribute("Target", "pivotCache/pivotCacheDefinition1.xml")));
            ReplaceZipEntryXml(archive, "xl/_rels/workbook.xml.rels", workbookRelsXml);

            const string worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadZipEntryXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(
                new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", "rIdGeneratedPivotChartDrawing1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                    new XAttribute("Target", "../drawings/drawing1.xml")),
                new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", "rIdGeneratedPivotTable1"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable"),
                    new XAttribute("Target", "../pivotTables/pivotTable1.xml")));
            ReplaceZipEntryXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadZipEntryXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(
                new XElement(
                    workbookNs + "drawing",
                    new XAttribute(relNs + "id", "rIdGeneratedPivotChartDrawing1")),
                new XElement(
                    workbookNs + "pivotTableDefinition",
                    new XAttribute(relNs + "id", "rIdGeneratedPivotTable1")));
            ReplaceZipEntryXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var contentTypesXml = LoadZipEntryXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/drawings/drawing1.xml",
                "application/vnd.openxmlformats-officedocument.drawing+xml");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/charts/chart1.xml",
                "application/vnd.openxmlformats-officedocument.drawingml.chart+xml");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/pivotCache/pivotCacheDefinition1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml");
            AddContentTypeOverride(
                contentTypesXml,
                "/xl/pivotTables/pivotTable1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml");
            ReplaceZipEntryXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        return stream.ToArray();
    }

    private static void WriteGeneratedStyleHeavyDrawingShapes(TextWriter writer)
    {
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
            """);

        for (var index = 0; index < GeneratedStyleHeavyDrawingShapePairs; index++)
        {
            var row = 1 + index % 12;
            var textColumn = 20 + (index / 12) * 4;
            var shapeColumn = textColumn + 2;
            var textId = 2 + index * 2;
            var shapeId = textId + 1;
            writer.Write($"""
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>{textColumn}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>{textColumn + 2}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row + 3}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="{textId}" name="Benchmark TextBox {index + 1}"/>
                    <xdr:cNvSpPr txBox="1"/>
                  </xdr:nvSpPr>
                  <xdr:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></xdr:spPr>
                  <xdr:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Benchmark note {index + 1}</a:t></a:r></a:p></xdr:txBody>
                </xdr:sp>
                <xdr:clientData/>
              </xdr:twoCellAnchor>
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>{shapeColumn}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>{shapeColumn + 2}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{row + 3}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="{shapeId}" name="Benchmark Shape {index + 1}"/>
                    <xdr:cNvSpPr/>
                  </xdr:nvSpPr>
                  <xdr:spPr><a:prstGeom prst="ellipse"><a:avLst/></a:prstGeom></xdr:spPr>
                </xdr:sp>
                <xdr:clientData/>
              </xdr:twoCellAnchor>
            """);
        }

        writer.Write(
            """
            </xdr:wsDr>
            """);
    }

    private static void WriteGeneratedStyleHeavyChartExDrawing(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                      xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                      xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006">
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>14</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>22</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>18</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <mc:AlternateContent>
                  <mc:Choice Requires="cx1" xmlns:cx1="http://schemas.microsoft.com/office/drawing/2015/9/8/chartex">
                    <xdr:graphicFrame macro="">
                      <xdr:nvGraphicFramePr>
                        <xdr:cNvPr id="2" name="Generated ChartEx"/>
                        <xdr:cNvGraphicFramePr/>
                      </xdr:nvGraphicFramePr>
                      <xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm>
                      <a:graphic>
                        <a:graphicData uri="http://schemas.microsoft.com/office/drawing/2014/chartex">
                          <cx:chart r:id="rIdGeneratedChartEx1"/>
                        </a:graphicData>
                      </a:graphic>
                    </xdr:graphicFrame>
                  </mc:Choice>
                  <mc:Fallback>
                    <xdr:sp macro="" textlink="">
                      <xdr:nvSpPr><xdr:cNvPr id="0" name=""/><xdr:cNvSpPr><a:spLocks noTextEdit="1"/></xdr:cNvSpPr></xdr:nvSpPr>
                      <xdr:spPr><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/><a:ln><a:noFill/></a:ln></xdr:spPr>
                      <xdr:txBody><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>This chart isn't available.</a:t></a:r></a:p></xdr:txBody>
                    </xdr:sp>
                  </mc:Fallback>
                </mc:AlternateContent>
                <xdr:clientData/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """);

    private static void WriteGeneratedStyleHeavyChartExChart(TextWriter writer) =>
        writer.Write($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <cx:chartSpace xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex"
                           xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <cx:chartData>
                <cx:data id="data0">
                  <cx:strDim type="cat"><cx:f>'Generated 1'!$A$2:$A${GeneratedStyleHeavyChartExSourceRows}</cx:f></cx:strDim>
                  <cx:numDim type="val"><cx:f>'Generated 1'!$B$2:$B${GeneratedStyleHeavyChartExSourceRows}</cx:f><cx:nf>'Generated 1'!$B$1</cx:nf></cx:numDim>
                </cx:data>
              </cx:chartData>
              <cx:chart>
                <cx:title><cx:tx><cx:rich><a:bodyPr/><a:lstStyle/><a:p><a:r><a:t>Generated Histogram</a:t></a:r></a:p></cx:rich></cx:tx></cx:title>
                <cx:plotArea>
                  <cx:plotAreaRegion>
                    <cx:series layoutId="clusteredColumn"><cx:dataId val="data0"/><cx:layoutPr><cx:binning intervalClosed="r"/></cx:layoutPr></cx:series>
                  </cx:plotAreaRegion>
                  <cx:axis id="0"><cx:catScaling gapWidth="2.19000006"/><cx:tickLabels/></cx:axis>
                  <cx:axis id="1"><cx:valScaling/><cx:majorGridlines/><cx:tickLabels/></cx:axis>
                </cx:plotArea>
              </cx:chart>
            </cx:chartSpace>
            """);

    private static void WriteGeneratedStyleHeavyChartExStyle(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <cs:chartStyle xmlns:cs="http://schemas.microsoft.com/office/drawing/2012/chartStyle"
                           xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                           id="201"/>
            """);

    private static void WriteGeneratedStyleHeavyChartExColors(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <cs:colorStyle xmlns:cs="http://schemas.microsoft.com/office/drawing/2012/chartStyle"
                           xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                           meth="cycle"
                           id="10">
              <a:schemeClr val="accent1"/>
              <a:schemeClr val="accent2"/>
              <a:schemeClr val="accent3"/>
            </cs:colorStyle>
            """);

    private static void WriteGeneratedStyleHeavyPivotChartDrawing(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                      xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <xdr:twoCellAnchor>
                <xdr:from><xdr:col>14</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                <xdr:to><xdr:col>22</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>18</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                <xdr:graphicFrame macro="">
                  <xdr:nvGraphicFramePr>
                    <xdr:cNvPr id="2" name="Generated Pivot Chart"/>
                    <xdr:cNvGraphicFramePr/>
                  </xdr:nvGraphicFramePr>
                  <xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm>
                  <a:graphic>
                    <a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/chart">
                      <c:chart r:id="rIdGeneratedPivotChart1"/>
                    </a:graphicData>
                  </a:graphic>
                </xdr:graphicFrame>
                <xdr:clientData/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """);

    private static void WriteGeneratedStyleHeavyPivotChart(TextWriter writer) =>
        writer.Write($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:pivotSource>
                <c:name>'Generated 1'!PivotTable1</c:name>
                <c:fmtId val="7"/>
              </c:pivotSource>
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>Generated Pivot Chart</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>'Generated 1'!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>'Generated 1'!$A$2:$A${GeneratedStyleHeavyPivotChartSourceRows}</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>'Generated 1'!$B$2:$B${GeneratedStyleHeavyPivotChartSourceRows}</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

    private static void WriteGeneratedStyleHeavyPivotCacheDefinition(TextWriter writer) =>
        writer.Write($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                  refreshedBy="FreeX Benchmark"
                                  refreshOnLoad="0"
                                  recordCount="{GeneratedStyleHeavyPivotChartSourceRows - 1}">
              <cacheSource type="worksheet">
                <worksheetSource ref="A1:B{GeneratedStyleHeavyPivotChartSourceRows}" sheet="Generated 1"/>
              </cacheSource>
              <cacheFields count="2">
                <cacheField name="R1C1" numFmtId="0">
                  <sharedItems count="0"/>
                </cacheField>
                <cacheField name="R1C2" numFmtId="0">
                  <sharedItems containsNumber="1" count="0"/>
                </cacheField>
              </cacheFields>
            </pivotCacheDefinition>
            """);

    private static void WriteGeneratedStyleHeavyPivotTable(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                                  name="PivotTable1"
                                  cacheId="1"
                                  dataOnRows="0"
                                  applyNumberFormats="0"
                                  applyBorderFormats="0"
                                  applyFontFormats="0"
                                  applyPatternFormats="0"
                                  applyAlignmentFormats="0"
                                  applyWidthHeightFormats="1">
              <location ref="D30:E32" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
              <pivotFields count="2">
                <pivotField axis="axisRow" showAll="0"/>
                <pivotField dataField="1" showAll="0"/>
              </pivotFields>
              <rowFields count="1">
                <field x="0"/>
              </rowFields>
              <dataFields count="1">
                <dataField name="Sum of R1C2" fld="1" subtotal="sum" numFmtId="0"/>
              </dataFields>
            </pivotTableDefinition>
            """);

    private static void WriteTextEntry(ZipArchive archive, string path, Action<TextWriter> write)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        write(writer);
    }

    private static void WriteGeneratedStyleHeavyContentTypes(
        TextWriter writer,
        bool legacyCommentMarker,
        bool structuredTableMarker)
    {
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
            """);
        if (legacyCommentMarker)
        {
            writer.Write(
                """
                  <Default Extension="vml" ContentType="application/vnd.openxmlformats-officedocument.vmlDrawing"/>
                """);
        }

        writer.Write(
            """
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            """);
        if (legacyCommentMarker)
        {
            writer.Write(
                """
                  <Override PartName="/xl/comments1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml"/>
                """);
        }

        if (structuredTableMarker)
        {
            writer.Write(
                """
                  <Override PartName="/xl/tables/table1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml"/>
                """);
        }

        for (var sheetIndex = 1; sheetIndex <= GeneratedStyleHeavySheetCount; sheetIndex++)
        {
            writer.Write($"""
              <Override PartName="/xl/worksheets/sheet{sheetIndex}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            """);
        }

        writer.Write(
            """
            </Types>
            """);
    }

    private static void WriteGeneratedStyleHeavyRootRelationships(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);

    private static void WriteGeneratedStyleHeavyWorkbook(TextWriter writer)
    {
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <bookViews>
                <workbookView activeTab="0"/>
              </bookViews>
              <sheets>
            """);
        for (var sheetIndex = 1; sheetIndex <= GeneratedStyleHeavySheetCount; sheetIndex++)
        {
            writer.Write($"""
                <sheet name="Generated {sheetIndex}" sheetId="{sheetIndex}" r:id="rId{sheetIndex}"/>
            """);
        }

        writer.Write(
            """
              </sheets>
              <calcPr calcId="191029"/>
            </workbook>
            """);
    }

    private static void WriteGeneratedStyleHeavyWorkbookRelationships(TextWriter writer)
    {
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            """);
        for (var sheetIndex = 1; sheetIndex <= GeneratedStyleHeavySheetCount; sheetIndex++)
        {
            writer.Write($"""
              <Relationship Id="rId{sheetIndex}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{sheetIndex}.xml"/>
            """);
        }

        writer.Write($"""
              <Relationship Id="rId{GeneratedStyleHeavySheetCount + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
    }

    private static void WriteGeneratedStyleHeavyStyles(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="1">
                <font><sz val="11"/><color theme="1"/><name val="Calibri"/><family val="2"/><scheme val="minor"/></font>
              </fonts>
              <fills count="3">
                <fill><patternFill patternType="none"/></fill>
                <fill><patternFill patternType="gray125"/></fill>
                <fill><patternFill patternType="solid"><fgColor rgb="FFE2F0D9"/><bgColor indexed="64"/></patternFill></fill>
              </fills>
              <borders count="2">
                <border><left/><right/><top/><bottom/><diagonal/></border>
                <border><left/><right/><top/><bottom style="thin"><color rgb="FF70AD47"/></bottom><diagonal/></border>
              </borders>
              <cellStyleXfs count="1">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0"/>
              </cellStyleXfs>
              <cellXfs count="2">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="0" fillId="2" borderId="1" xfId="0" applyFill="1" applyBorder="1"/>
              </cellXfs>
              <cellStyles count="1">
                <cellStyle name="Normal" xfId="0" builtinId="0"/>
              </cellStyles>
              <dxfs count="0"/>
              <tableStyles count="0" defaultTableStyle="TableStyleMedium2" defaultPivotStyle="PivotStyleLight16"/>
            </styleSheet>
            """);

    private static void WriteGeneratedStyleHeavyWorksheet(
        TextWriter writer,
        int sheetIndex,
        bool formulaMarker,
        bool internalHyperlinkMarker,
        bool legacyCommentMarker,
        bool structuredTableMarker,
        bool sparklineMarker)
    {
        var lastColumn = GeneratedStyleHeavyStyleOnlyStartColumn + GeneratedStyleHeavyStyleOnlyColumnsPerSheet - 1;
        var lastReference = $"{CellAddress.NumberToColumnName((uint)lastColumn)}{GeneratedStyleHeavyRowsPerSheet}";
        var worksheetExtensionNamespaces = sparklineMarker && sheetIndex == 1
            ? " xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" xmlns:xm=\"http://schemas.microsoft.com/office/excel/2006/main\""
            : "";
        writer.Write($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"{worksheetExtensionNamespaces}>
              <dimension ref="A1:{lastReference}"/>
              <sheetViews><sheetView workbookViewId="0"/></sheetViews>
              <sheetFormatPr defaultRowHeight="15"/>
              <sheetData>
            """);

        for (var row = 1; row <= GeneratedStyleHeavyRowsPerSheet; row++)
        {
            writer.Write($"""    <row r="{row}">""");
            for (var col = 1; col <= GeneratedStyleHeavyValueColumnsPerSheet; col++)
            {
                var reference = $"{CellAddress.NumberToColumnName((uint)col)}{row}";
                if (formulaMarker && sheetIndex == 1 && row == 1 && col == 1)
                {
                    writer.Write($"""<c r="{reference}"><f>1+1</f><v>2</v></c>""");
                }
                else
                {
                    writer.Write($"""<c r="{reference}"><v>{sheetIndex * 1_000_000 + row * 1_000 + col}</v></c>""");
                }
            }

            for (var col = GeneratedStyleHeavyStyleOnlyStartColumn;
                 col < GeneratedStyleHeavyStyleOnlyStartColumn + GeneratedStyleHeavyStyleOnlyColumnsPerSheet;
                 col++)
            {
                writer.Write(
                    $"""<c r="{CellAddress.NumberToColumnName((uint)col)}{row}" s="1"/>""");
            }

            writer.WriteLine("</row>");
        }

        writer.Write(
            """
              </sheetData>
            """);
        if (internalHyperlinkMarker && sheetIndex == 1)
        {
            writer.Write(
                """
                  <hyperlinks>
                    <hyperlink ref="A1" location="B2" tooltip="Jump source"/>
                  </hyperlinks>
                """);
        }

        if (legacyCommentMarker && sheetIndex == 1)
        {
            writer.Write(
                """
                  <legacyDrawing r:id="rId2"/>
                """);
        }

        if (structuredTableMarker && sheetIndex == 1)
        {
            var tableRelId = legacyCommentMarker ? "rId3" : "rId1";
            writer.Write($"""
                  <tableParts count="1"><tablePart r:id="{tableRelId}"/></tableParts>
                """);
        }

        if (sparklineMarker && sheetIndex == 1)
        {
            writer.Write(
                """
                  <extLst>
                    <ext uri="{05C60535-1F16-4fd2-B633-F4F36F0B64E0}">
                      <x14:sparklineGroups>
                        <x14:sparklineGroup type="column">
                          <x14:sparklines>
                            <x14:sparkline>
                              <xm:f>'Generated 1'!A1:L1</xm:f>
                              <xm:sqref>M1</xm:sqref>
                            </x14:sparkline>
                          </x14:sparklines>
                        </x14:sparklineGroup>
                      </x14:sparklineGroups>
                    </ext>
                  </extLst>
                """);
        }

        writer.Write(
            """
            </worksheet>
            """);
    }

    private static void WriteGeneratedStyleHeavyWorksheetRelationships(
        TextWriter writer,
        bool legacyCommentMarker,
        bool structuredTableMarker)
    {
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
            """);
        if (legacyCommentMarker)
        {
            writer.Write(
                """
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments" Target="../comments1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing" Target="../drawings/vmlDrawing1.vml"/>
                """);
        }

        if (structuredTableMarker)
        {
            var tableRelId = legacyCommentMarker ? "rId3" : "rId1";
            writer.Write($"""
                  <Relationship Id="{tableRelId}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/table" Target="../tables/table1.xml"/>
                """);
        }

        writer.Write(
            """
            </Relationships>
            """);
    }

    private static void WriteGeneratedStyleHeavyComments(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <comments xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <authors><author>FreeX Benchmark</author></authors>
              <commentList>
                <comment ref="A1" authorId="0"><text><r><t>Comment 0</t></r></text></comment>
              </commentList>
            </comments>
            """);

    private static void WriteGeneratedStyleHeavyVmlDrawing(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <xml xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel">
              <v:shape id="_x0000_s1025" type="#_x0000_t202" style="position:absolute;margin-left:80pt;margin-top:6pt;width:108pt;height:59.25pt;z-index:1;visibility:hidden" fillcolor="#ffffe1" o:insetmode="auto">
                <v:fill color2="#ffffe1"/>
                <v:shadow color="black" obscured="t"/>
                <v:path o:connecttype="none"/>
                <v:textbox style="mso-direction-alt:auto"><div style="text-align:left"/></v:textbox>
                <x:ClientData ObjectType="Note">
                  <x:MoveWithCells/>
                  <x:SizeWithCells/>
                  <x:Anchor>1, 15, 0, 2, 3, 15, 4, 3</x:Anchor>
                  <x:AutoFill>False</x:AutoFill>
                  <x:Row>0</x:Row>
                  <x:Column>0</x:Column>
                </x:ClientData>
              </v:shape>
            </xml>
            """);

    private static void WriteGeneratedStyleHeavyStructuredTable(TextWriter writer, bool includeFilter)
    {
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" id="1" name="GeneratedTable1" displayName="GeneratedTable1" ref="A1:L400" totalsRowShown="0">
            """);
        writer.Write(
            includeFilter
                ? """
                    <autoFilter ref="A1:L400"><filterColumn colId="0"><filters><filter val="1001001"/></filters></filterColumn></autoFilter>
                """
                : """
                    <autoFilter ref="A1:L400"/>
                """);
        writer.Write(
            """
              <tableColumns count="12">
            """);
        for (var col = 1; col <= GeneratedStyleHeavyValueColumnsPerSheet; col++)
        {
            writer.Write($"""
                <tableColumn id="{col}" name="Field{col}"/>
            """);
        }

        writer.Write(
            """
              </tableColumns>
              <tableStyleInfo name="TableStyleMedium2" showFirstColumn="0" showLastColumn="0" showRowStripes="1" showColumnStripes="0"/>
            </table>
            """);
    }

    private static byte[] CreateLargePatchBaselineXlsxPackage()
    {
        using var stream = new MemoryStream(capacity: 8 * 1024 * 1024);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(archive, "[Content_Types].xml", WriteLargePatchBaselineContentTypes);
            WriteTextEntry(archive, "_rels/.rels", WriteGeneratedStyleHeavyRootRelationships);
            WriteTextEntry(archive, "xl/workbook.xml", WriteLargePatchBaselineWorkbook);
            WriteTextEntry(archive, "xl/_rels/workbook.xml.rels", WriteLargePatchBaselineWorkbookRelationships);
            WriteTextEntry(archive, "xl/styles.xml", WriteGeneratedStyleHeavyStyles);
            WriteTextEntry(archive, "xl/worksheets/sheet1.xml", WriteLargePatchBaselineWorksheet);
        }

        return stream.ToArray();
    }

    private static void WriteLargePatchBaselineContentTypes(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);

    private static void WriteLargePatchBaselineWorkbook(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <bookViews>
                <workbookView activeTab="0"/>
              </bookViews>
              <sheets>
                <sheet name="Large Data" sheetId="1" r:id="rId1"/>
              </sheets>
              <calcPr calcId="191029"/>
            </workbook>
            """);

    private static void WriteLargePatchBaselineWorkbookRelationships(TextWriter writer) =>
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);

    private static void WriteLargePatchBaselineWorksheet(TextWriter writer)
    {
        writer.Write($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <dimension ref="A1:A{LargePatchBaselineRows}"/>
              <sheetViews><sheetView workbookViewId="0"/></sheetViews>
              <sheetFormatPr defaultRowHeight="15"/>
              <sheetData>
            """);

        for (var row = 1; row <= LargePatchBaselineRows; row++)
            writer.WriteLine($"""    <row r="{row}"><c r="A{row}"><v>{row}</v></c></row>""");

        writer.Write(
            """
              </sheetData>
            </worksheet>
            """);
    }

    private static void AssertIgnoredErrorAndStyleOnlyMetadata(Workbook workbook)
    {
        workbook.SheetCount.Should().Be(1);
        var sheet = workbook.Sheets[0];
        sheet.EnumerateCells().Count(pair => pair.Cell.IgnoreFormulaError)
            .Should().Be(IgnoredErrorStyleOnlyRows * IgnoredErrorStyleOnlyValueColumns);
        sheet.GetStyleOnlyEntries().Count()
            .Should().Be(IgnoredErrorStyleOnlyRows * IgnoredErrorStyleOnlyStyleColumns);
    }

    private static void AssertGeneratedStyleHeavyWorkbook(Workbook workbook)
    {
        workbook.SheetCount.Should().Be(GeneratedStyleHeavySheetCount);
        workbook.Sheets.Sum(sheet => sheet.CellCount)
            .Should()
            .Be(GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyValueColumnsPerSheet);
        workbook.Sheets.Sum(sheet => sheet.GetStyleOnlyEntries().Count())
            .Should()
            .Be(GeneratedStyleHeavySheetCount * GeneratedStyleHeavyRowsPerSheet * GeneratedStyleHeavyStyleOnlyColumnsPerSheet);
    }

    private static void ApplyGeneratedStyleHeavyFallbackMutation(Workbook workbook, int iteration)
    {
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor((byte)(220 - iteration), 235, 247)
        });
        var sheet = workbook.Sheets[0];
        var cell = sheet.GetCell(1, 1);
        cell.Should().NotBeNull();
        cell!.StyleId = styleId;
    }

    private static void ApplyGeneratedStyleHeavyExistingStyleMutation(Workbook workbook)
    {
        var sheet = workbook.Sheets[0];
        var styleId = sheet.GetStyleOnlyEntries().Select(entry => entry.StyleId).First();
        var cell = sheet.GetCell(1, 1);
        cell.Should().NotBeNull();
        cell!.StyleId = styleId;
    }

    private static void ApplyGeneratedStyleHeavyDimensionMutation(Workbook workbook, int iteration)
    {
        var sheet = workbook.Sheets[0];
        var row = (uint)(1 + iteration);
        var column = (uint)(2 + iteration);
        sheet.RowHeights[row] = 24 + iteration;
        sheet.HiddenRows.Add((uint)(20 + iteration));
        sheet.ColumnWidths[column] = 14.25 + iteration;
        sheet.HiddenCols.Add((uint)(30 + iteration));
    }

    private static void ApplyGeneratedStyleHeavyMergeRegionMutation(Workbook workbook, int iteration)
    {
        var sheet = workbook.Sheets[0];
        var row = (uint)(1 + iteration * 2);
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, row, 1),
            new CellAddress(sheet.Id, row + 1, 2)));
    }

    private static void ApplyGeneratedStyleHeavyInternalHyperlinkMutation(Workbook workbook, int iteration)
    {
        var sheet = workbook.Sheets[0];
        var address = new CellAddress(sheet.Id, 1, 1);
        var target = $"C{2 + iteration}";
        sheet.Hyperlinks[address] = target;
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            $"Jump {iteration}",
            target);
    }

    private static void ApplyGeneratedStyleHeavyLegacyCommentMutation(Workbook workbook, int iteration)
    {
        var sheet = workbook.Sheets[0];
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] =
            string.Create(CultureInfo.InvariantCulture, $"Comment {iteration + 1}");
    }

    private static void ApplyGeneratedStyleHeavyStructuredTableMutation(Workbook workbook, int iteration)
    {
        var sheet = workbook.Sheets[0];
        sheet.SetCell(
            new CellAddress(sheet.Id, 2, 2),
            new NumberValue(1_000_000 + iteration));
    }

    private static void ApplyGeneratedStyleHeavySparklineMutation(Workbook workbook, int iteration)
    {
        var sheet = workbook.Sheets[0];
        sheet.SetCell(
            new CellAddress(sheet.Id, 2, 2),
            new NumberValue(2_000_000 + iteration));
    }

    private static void ReplaceZipEntryXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream, System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static XDocument LoadZipEntryXml(ZipArchiveEntry entry)
    {
        return XlsxPackageTestFixtures.LoadPackageXml(entry);
    }

    private static void AddContentTypeOverride(XDocument contentTypesXml, string partName, string contentType)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var root = contentTypesXml.Root!;
        var existing = root
            .Elements(contentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.Ordinal));

        if (existing is not null)
        {
            existing.SetAttributeValue("ContentType", contentType);
            return;
        }

        root.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private static Workbook CreateDenseModelWorkbook()
    {
        var workbook = new Workbook("Dense IO");
        for (var sheetIndex = 1; sheetIndex <= DenseSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Sheet {sheetIndex}");
            for (uint row = 1; row <= DenseRowsPerSheet; row++)
            {
                for (uint col = 1; col <= DenseColumnsPerSheet; col++)
                {
                    sheet.SetCell(
                        new CellAddress(sheet.Id, row, col),
                        new NumberValue(row * col + sheetIndex));
                }
            }
        }

        return workbook;
    }

    private static Workbook CreateDrawingPicturesWorkbook(int pictureCount)
    {
        var workbook = new Workbook("Drawing Pictures IO");
        var sheet = workbook.AddSheet("Sheet1");
        var imageBytes = MinimalPngBytes();
        for (var index = 0; index < pictureCount; index++)
        {
            var row = (uint)(1 + index / 18);
            var column = (uint)(1 + index % 18);
            sheet.Pictures.Add(new PictureModel
            {
                Name = $"Picture {index + 1}",
                Anchor = new CellAddress(sheet.Id, row, column),
                Kind = PictureKind.Image,
                ImageBytes = imageBytes,
                ContentType = "image/png",
                Width = 72,
                Height = 48,
                AltText = $"Drawing picture {index + 1}"
            });
        }

        return workbook;
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static Workbook CreateIgnoredErrorsSaveWorkbook()
    {
        var workbook = new Workbook("Ignored Errors Save IO");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= IgnoredErrorSaveRows; row++)
        {
            for (uint col = 1; col <= IgnoredErrorSaveColumns; col++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, col),
                    new TextValue($"{row:D4}{col:D2}"));
                sheet.GetCell(row, col)!.IgnoreFormulaError = true;
            }
        }

        return workbook;
    }

    private static MemoryStream CreateWritablePackageStream(byte[] package)
    {
        var stream = new MemoryStream(package.Length * 2);
        stream.Write(package, 0, package.Length);
        stream.Position = 0;
        return stream;
    }

    private static void InvokeSavePostProcessing(Workbook workbook, Stream stream)
    {
        XlsxFileAdapter.ApplyPackagePostProcessing(workbook, stream, null);
    }

    private static void MeasureExternalStage(string path, string stage, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            action();
            stopwatch.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Console.WriteLine(
                "PERF XLSX_LOAD_EXTERNAL_STAGE " +
                $"stage={stage} file=\"{Path.GetFileName(path)}\" bytes={new FileInfo(path).Length:N0} " +
                $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine(
                "PERF XLSX_LOAD_EXTERNAL_STAGE_FAILED " +
                $"stage={stage} file=\"{Path.GetFileName(path)}\" bytes={new FileInfo(path).Length:N0} " +
                $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} error=\"{ex.GetType().Name}: {ex.Message}\"");
        }
    }

    private static Workbook CreateStyleOnlyModelWorkbook()
    {
        var workbook = new Workbook("Style-only IO");
        var styleIds = new[]
        {
            workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(221, 235, 247),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(91, 155, 213))
            }),
            workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(226, 239, 218),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(112, 173, 71))
            }),
            workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(252, 228, 214),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(237, 125, 49))
            })
        };

        for (var sheetIndex = 1; sheetIndex <= StyleOnlySaveSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Styled blanks {sheetIndex}");
            for (uint row = 1; row <= StyleOnlySaveRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new NumberValue(row + (uint)sheetIndex));

                for (uint col = 3; col < 3 + StyleOnlySaveColumnsPerSheet; col++)
                {
                    var runIndex = (col - 3) / StyleOnlySaveRunWidth;
                    var styleIndex = (int)((runIndex + row + (uint)sheetIndex) % (uint)styleIds.Length);
                    sheet.SetStyleOnly(row, col, styleIds[styleIndex]);
                }
            }
        }

        return workbook;
    }

    private static Workbook CreateWorksheetNativeMetadataWorkbook()
    {
        var workbook = new Workbook("Worksheet native metadata IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Metadata {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new TextValue($"R{row}"));
            }

            sheet.IsProtected = true;
            sheet.ProtectionMetadata = MakeBag(
                "sheetProtection",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["algorithmName"] = "SHA-512",
                    ["hashValue"] = $"hash{sheetIndex}",
                    ["saltValue"] = $"salt{sheetIndex}",
                    ["spinCount"] = "100000",
                    ["objects"] = "1",
                    ["scenarios"] = "1"
                },
                [$"<fx:sheetProtectionNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.PrintOptionsMetadata = MakeBag(
                "printOptions",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["gridLinesSet"] = "1",
                    ["customAttr"] = $"print-{sheetIndex}"
                },
                [$"<fx:printOptionsNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.DimensionMetadata = MakeBag(
                "dimension",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nativeDimensionAttr"] = $"dimension-{sheetIndex}"
                });
            sheet.SheetPropertiesMetadata = MakeBag(
                "sheetPr",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["filterMode"] = "1",
                    ["customSheetPrAttr"] = $"sheetPr-{sheetIndex}"
                },
                [$"<fx:sheetPrNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.PrimaryViewMetadata = MakeBag(
                "sheetView",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["showZeros"] = "0",
                    ["rightToLeft"] = "1",
                    ["customViewAttr"] = $"view-{sheetIndex}"
                },
                [$"<pivotSelection xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" pane=\"topLeft\" />"]);
            sheet.PageMargins = new WorksheetPageMargins(0.7, 0.75, 0.8, 0.85);
            sheet.PageMarginsMetadata = MakeBag(
                "pageMargins",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customAttr"] = $"margins-{sheetIndex}"
                },
                [$"<fx:pageMarginsNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.RowPageBreaks.Add(20);
            sheet.RowPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
            {
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["manualBreakCount"] = "1"
                },
                BreakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>
                {
                    [20] = new(StringComparer.Ordinal)
                    {
                        ["pt"] = "1",
                        ["customAttr"] = $"row-break-{sheetIndex}"
                    }
                }
            };
            sheet.ColumnPageBreaks.Add(5);
            sheet.ColumnPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
            {
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["manualBreakCount"] = "1"
                },
                BreakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>
                {
                    [5] = new(StringComparer.Ordinal)
                    {
                        ["pt"] = "1",
                        ["customAttr"] = $"column-break-{sheetIndex}"
                    }
                }
            };
            sheet.PageHeader = new WorksheetHeaderFooter("L", "C", "R");
            sheet.PageFooter = new WorksheetHeaderFooter("FL", "FC", "FR");
            sheet.HeaderFooterMetadata = MakeBag(
                "headerFooter",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nativeHeaderFooterAttr"] = $"header-footer-{sheetIndex}"
                },
                [$"<fx:headerFooterNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
        }

        return workbook;
    }

    private static Workbook CreateWorksheetAutoFilterNativeMetadataWorkbook()
    {
        var workbook = CreateWorksheetNativeMetadataWorkbook();
        foreach (var sheet in workbook.Sheets)
        {
            sheet.AutoFilter = new WorksheetAutoFilterModel(
                $"A1:B{WorksheetNativeMetadataRowsPerSheet}",
                null)
            {
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customAutoFilterAttr"] = $"auto-filter-{sheet.Name}"
                }
            };
            sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
                0,
                [$"R{WorksheetNativeMetadataRowsPerSheet / 2}", $"R{WorksheetNativeMetadataRowsPerSheet}"],
                IncludeBlank: false));
            sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
                1,
                [],
                IncludeBlank: true,
                CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThanOrEqual", "10")],
                CustomFiltersAnd: false,
                NativeCustomFiltersAttributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customFiltersAttr"] = $"custom-filters-{sheet.Name}"
                },
                NativeFilterXmls: [],
                NativeAttributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customFilterColumnAttr"] = $"filter-column-{sheet.Name}"
                }));
        }

        return workbook;
    }

    private static Workbook CreateDataValidationNativeMetadataWorkbook()
    {
        var workbook = new Workbook("Data validation native metadata IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"DV Metadata {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new NumberValue(row));
                sheet.DataValidations.Add(new DataValidation
                {
                    AppliesTo = new GridRange(
                        new CellAddress(sheet.Id, row, 1),
                        new CellAddress(sheet.Id, row, 1)),
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "100",
                    NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["imeMode"] = "noControl",
                        ["customDvAttr"] = $"dv-{sheetIndex}-{row}"
                    },
                    NativeChildXmls =
                    [
                        $"<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{{FREEX-DV-{sheetIndex}-{row}}}\" /></extLst>"
                    ],
                    NativeContainerAttributes = row == 1
                        ? new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["disablePrompts"] = "0",
                            ["customDvContainerAttr"] = $"container-{sheetIndex}"
                        }
                        : null
                });
            }
        }

        return workbook;
    }

    private static Workbook CreateAdvancedConditionalFormattingWorkbook()
    {
        var workbook = new Workbook("Advanced conditional formatting IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"CF Metadata {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new NumberValue(row + sheetIndex));
            }

            for (uint row = 1; row <= AdvancedConditionalFormatRulesPerSheet; row++)
            {
                sheet.ConditionalFormats.Add(new ConditionalFormat
                {
                    AppliesTo = new GridRange(
                        new CellAddress(sheet.Id, row, 1),
                        new CellAddress(sheet.Id, row, 1)),
                    Priority = (int)row,
                    RuleType = CfRuleType.DataBar,
                    DataBarGradient = false,
                    DataBarBorder = true,
                    DataBarAxisPosition = "middle",
                    DataBarAxisColor = new RgbColor(0, 0, 0),
                    DataBarNegativeFillColor = new RgbColor(156, 0, 6),
                    DataBarNegativeBorderColor = new RgbColor(156, 0, 6),
                    NativePayloadChildXmls =
                    [
                        $"<x14:customPayload xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" id=\"{sheetIndex}-{row}\" />"
                    ],
                    FormatIfTrue = new CellStyle
                    {
                        FillColor = new CellColor(198, 239, 206),
                        FontColor = new CellColor(0, 97, 0),
                        BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(0, 97, 0))
                    }
                });
            }
        }

        return workbook;
    }

    private static Workbook CreateWorksheetSingleXmlCellsPostProcessingWorkbook()
    {
        var workbook = new Workbook("Worksheet singleXmlCells IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"SingleXml {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new TextValue($"R{row}"));
            }

            sheet.SmartTags = new WorksheetSmartTagsModel
            {
                NativeXml = "<smartTags xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    $"<cellSmartTags r=\"A{sheetIndex}\"><cellSmartTag type=\"{sheetIndex}\" deleted=\"0\">" +
                    $"<cellSmartTagPr key=\"place\" val=\"City{sheetIndex}\" /></cellSmartTag></cellSmartTags></smartTags>"
            };
            sheet.SingleXmlCells = new WorksheetSingleXmlCellsModel
            {
                NativeAttributes =
                {
                    ["nativeSingleXmlCellsAttr"] = $"single-xml-{sheetIndex}"
                }
            };
            for (var cellIndex = 1; cellIndex <= WorksheetSingleXmlCellsPerSheet; cellIndex++)
            {
                sheet.SingleXmlCells.Cells.Add(new WorksheetSingleXmlCellModel
                {
                    Id = cellIndex,
                    Reference = $"A{cellIndex}",
                    XmlCellPropertyId = 1000 + cellIndex,
                    NativeAttributes =
                    {
                        ["nativeSingleXmlCellAttr"] = $"single-cell-{sheetIndex}-{cellIndex}"
                    }
                });
            }
        }

        return workbook;
    }

    private static byte[] CreateWorksheetReplayMetadataSourcePackage()
    {
        using var workbook = new XLWorkbook();
        for (var sheetIndex = 1; sheetIndex <= WorksheetReplayMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.Worksheets.Add($"Replay {sheetIndex}");
            for (var row = 1; row <= WorksheetReplayMetadataRowsPerSheet; row++)
                sheet.Cell(row, 1).Value = $"R{row}";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void ApplyWorksheetReplayMetadata(Workbook workbook)
    {
        for (var i = 0; i < workbook.Sheets.Count; i++)
        {
            var sheet = workbook.Sheets[i];
            var sheetIndex = i + 1;
            sheet.SmartTags = new WorksheetSmartTagsModel
            {
                NativeXml = "<smartTags xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    $"<cellSmartTags r=\"A{sheetIndex}\"><cellSmartTag type=\"{sheetIndex}\" deleted=\"0\">" +
                    $"<cellSmartTagPr key=\"place\" val=\"City{sheetIndex}\" /></cellSmartTag></cellSmartTags></smartTags>"
            };
            sheet.SortState = new WorksheetSortStateModel
            {
                Reference = $"A1:A{WorksheetReplayMetadataRowsPerSheet}",
                CaseSensitive = true,
                Conditions =
                [
                    new WorksheetSortConditionModel
                    {
                        Reference = $"A1:A{WorksheetReplayMetadataRowsPerSheet}",
                        Descending = sheetIndex % 2 == 0,
                        SortBy = "value"
                    }
                ]
            };
            sheet.AdditionalViews = new WorksheetAdditionalViewsModel
            {
                NativeAttributes = { ["customSheetViewsAttr"] = $"views-{sheetIndex}" },
                Views =
                [
                    new WorksheetAdditionalViewModel
                    {
                        WorkbookViewId = (sheetIndex + 1).ToString(CultureInfo.InvariantCulture),
                        NativeAttributes = { ["customViewAttr"] = $"view-{sheetIndex}" }
                    }
                ]
            };
            sheet.DataConsolidation = new WorksheetDataConsolidationModel
            {
                Function = "sum",
                LeftLabels = true,
                TopLabels = true,
                Link = sheetIndex % 2 == 0,
                NativeAttributes = { ["customDataConsolidationFlag"] = $"data-{sheetIndex}" },
                References =
                [
                    new WorksheetDataConsolidationReferenceModel
                    {
                        Reference = "A1:A2",
                        Sheet = sheet.Name,
                        NativeAttributes = { ["customDataRefFlag"] = $"ref-{sheetIndex}" }
                    }
                ]
            };
            sheet.UsePrinterDefaults = false;
            sheet.PrintCopies = 2 + sheetIndex;
            sheet.PrintQualityVerticalDpi = 300 + sheetIndex;
            sheet.PageSetupMetadata = MakeBag(
                "pageSetup",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customPageSetupAttr"] = $"page-setup-{sheetIndex}"
                },
                [$"<fx:nativePageSetupChild xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
        }
    }

    private static NativeXmlPreserveBag MakeBag(
        string key,
        Dictionary<string, string>? attrs = null,
        IReadOnlyList<string>? children = null)
    {
        var wrapper = new XElement("e");
        foreach (var (name, value) in attrs ?? [])
            wrapper.SetAttributeValue(XName.Get(name), value);
        foreach (var childXml in children ?? [])
            wrapper.Add(XElement.Parse(childXml, System.Xml.Linq.LoadOptions.PreserveWhitespace));

        var bag = new NativeXmlPreserveBag();
        bag.Set(key, wrapper.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        return bag;
    }

    private static string[] ResolveExternalWorkbookPaths()
    {
        var configured = Environment.GetEnvironmentVariable("FREEX_IO_BENCHMARK_PATHS");
        if (string.IsNullOrWhiteSpace(configured))
            return [];

        var limit = 3;
        if (int.TryParse(Environment.GetEnvironmentVariable("FREEX_IO_BENCHMARK_LIMIT"), out var configuredLimit))
            limit = Math.Clamp(configuredLimit, 1, 20);

        return configured
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(EnumerateWorkbookPaths)
            .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.Length)
            .Take(limit)
            .Select(file => file.FullName)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateWorkbookPaths(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedBenchmarkWorkbook);
        }

        return File.Exists(path) && IsSupportedBenchmarkWorkbook(path)
            ? [path]
            : [];
    }

    private static bool IsSupportedBenchmarkWorkbook(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xltx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xltm", StringComparison.OrdinalIgnoreCase);
    }

}
