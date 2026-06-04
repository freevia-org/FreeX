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
            XDocument worksheetXml;
            using (var worksheetStream = worksheetEntry.Open())
                worksheetXml = XDocument.Load(worksheetStream);
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
        bool structuredTableMarker = false)
    {
        using var stream = new MemoryStream(capacity: 8 * 1024 * 1024);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(
                archive,
                "[Content_Types].xml",
                writer => WriteGeneratedStyleHeavyContentTypes(writer, legacyCommentMarker, structuredTableMarker));
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
                        structuredTableMarker));
            }

            if (legacyCommentMarker || structuredTableMarker)
            {
                WriteTextEntry(
                    archive,
                    "xl/worksheets/_rels/sheet1.xml.rels",
                    writer => WriteGeneratedStyleHeavyWorksheetRelationships(
                        writer,
                        legacyCommentMarker,
                        structuredTableMarker));
                if (legacyCommentMarker)
                {
                    WriteTextEntry(archive, "xl/comments1.xml", WriteGeneratedStyleHeavyComments);
                    WriteTextEntry(archive, "xl/drawings/vmlDrawing1.vml", WriteGeneratedStyleHeavyVmlDrawing);
                }

                if (structuredTableMarker)
                    WriteTextEntry(archive, "xl/tables/table1.xml", WriteGeneratedStyleHeavyStructuredTable);
            }
        }

        return stream.ToArray();
    }

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
        bool structuredTableMarker)
    {
        var lastColumn = GeneratedStyleHeavyStyleOnlyStartColumn + GeneratedStyleHeavyStyleOnlyColumnsPerSheet - 1;
        var lastReference = $"{CellAddress.NumberToColumnName((uint)lastColumn)}{GeneratedStyleHeavyRowsPerSheet}";
        writer.Write($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
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

    private static void WriteGeneratedStyleHeavyStructuredTable(TextWriter writer)
    {
        writer.Write(
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" id="1" name="GeneratedTable1" displayName="GeneratedTable1" ref="A1:L400" totalsRowShown="0">
              <autoFilter ref="A1:L400"/>
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

    private static void ReplaceZipEntryXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream, System.Xml.Linq.SaveOptions.DisableFormatting);
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
        var method = typeof(XlsxFileAdapter).GetMethod(
            "ApplyPackagePostProcessing",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, [workbook, stream, null]);
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

    private static string FindRepoFile(params string[] relativeParts) => TestWorkspaceFiles.FindRepoFile(relativeParts);
}
