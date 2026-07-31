using System;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R110-io-drawing-anchor-write-exhaustion: <see cref="XlsxWorksheetChartWriter"/>'s ToMarkerIndex pixel-to-
/// cell walk (used for every OneCell/TwoCell chart anchor's from/to markers) starts at index 0 and steps
/// forward, skipping hidden/zero-size columns/rows, until the remaining pixel distance fits inside the next
/// visible one. If it never fits -- trivially true once most of the sheet's columns/rows are hidden, since a
/// hidden column/row contributes zero pixels and never reduces the remaining distance -- the loop falls
/// through having incremented `index` all the way to `maxIndex` (16384 columns / 1,048,576 rows), one past
/// Excel's real zero-based ceiling (16383 / 1,048,575 -- the exact ceiling
/// <see cref="XlsxDrawingAnchorApplier"/>'s read-side MaxColumnIndexZeroBased/MaxRowIndexZeroBased already
/// enforces). Before the fix that exhausted value was written verbatim as &lt;xdr:col&gt;16384&lt;/xdr:col&gt;
/// or &lt;xdr:row&gt;1048576&lt;/xdr:row&gt;, an out-of-range reference real Excel repairs/discards the
/// drawing for on next open. Real product entry point: <see cref="XlsxWorksheetChartWriter.Save"/>, the same
/// call FileFormats/XlsxFileAdapter uses to rebuild every chart drawing on save.
/// </summary>
public sealed class R110_DrawingAnchorWriteExhaustionClampTests
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private const string ChartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const uint MaxColumnIndexZeroBased = 16383;
    private const uint MaxRowIndexZeroBased = 1048575;

    [Fact]
    public void Save_TwoCellChartAnchor_WithAllColumnsHidden_ClampsToColumnIndex_InsteadOfWritingOneOffTheEnd()
    {
        var workbook = new Workbook("HiddenColumnsChart");
        var sheet = workbook.AddSheet("Charted");

        // Hide every column on the sheet (an ordinary "hide unused columns"/dashboard action that never
        // touches the chart object itself). With nothing visible, the from/to pixel-to-cell walk can never
        // find a column whose width absorbs the remaining distance, so it runs to exhaustion.
        for (var col = 1u; col <= 16384u; col++)
            sheet.HiddenCols.Add(col);

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            DrawingAnchorKind = ChartDrawingAnchorKind.TwoCell,
            Left = 50,
            Top = 50,
            Width = 400,
            Height = 300,
        });

        using var package = CreatePackage();
        Save(package, workbook);

        var anchor = ReadSoleTwoCellAnchor(package);
        var toCol = (uint)anchor.Element(Xdr + "to")!.Element(Xdr + "col")!;
        var fromCol = (uint)anchor.Element(Xdr + "from")!.Element(Xdr + "col")!;

        toCol.Should().BeLessThanOrEqualTo(MaxColumnIndexZeroBased,
            "an exhausted column walk must clamp to Excel's real zero-based ceiling (16383 = XFD), never write " +
            "16384 (one past the last real column) into <xdr:col> where Excel would reject/repair the drawing");
        fromCol.Should().BeLessThanOrEqualTo(MaxColumnIndexZeroBased,
            "the from-marker's column walk is exhausted identically and must clamp the same way");
    }

    [Fact]
    public void Save_TwoCellChartAnchor_WithAllRowsHidden_ClampsToRowIndex_InsteadOfWritingOneOffTheEnd()
    {
        var workbook = new Workbook("HiddenRowsChart");
        var sheet = workbook.AddSheet("Charted");

        for (var row = 1u; row <= 1_048_576u; row++)
            sheet.HiddenRows.Add(row);

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            DrawingAnchorKind = ChartDrawingAnchorKind.TwoCell,
            Left = 50,
            Top = 50,
            Width = 400,
            Height = 300,
        });

        using var package = CreatePackage();
        Save(package, workbook);

        var anchor = ReadSoleTwoCellAnchor(package);
        var toRow = (uint)anchor.Element(Xdr + "to")!.Element(Xdr + "row")!;
        var fromRow = (uint)anchor.Element(Xdr + "from")!.Element(Xdr + "row")!;

        toRow.Should().BeLessThanOrEqualTo(MaxRowIndexZeroBased,
            "an exhausted row walk must clamp to Excel's real zero-based ceiling (1,048,575), never write " +
            "1,048,576 (one past the last real row) into <xdr:row>");
        fromRow.Should().BeLessThanOrEqualTo(MaxRowIndexZeroBased,
            "the from-marker's row walk is exhausted identically and must clamp the same way");
    }

    // Sibling no-regression: an ordinary sheet with nothing hidden must keep producing small, sane anchor
    // indices (the walk finds room in the very first visible column/row and returns early) -- the clamp
    // added for the exhaustion case must never fire for the overwhelming common case.
    [Fact]
    public void Save_TwoCellChartAnchor_WithNothingHidden_StillProducesSmallOrdinaryIndices()
    {
        var workbook = new Workbook("OrdinaryChart");
        var sheet = workbook.AddSheet("Charted");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            DrawingAnchorKind = ChartDrawingAnchorKind.TwoCell,
            Left = 50,
            Top = 50,
            Width = 400,
            Height = 300,
        });

        using var package = CreatePackage();
        Save(package, workbook);

        var anchor = ReadSoleTwoCellAnchor(package);
        var toCol = (uint)anchor.Element(Xdr + "to")!.Element(Xdr + "col")!;
        var toRow = (uint)anchor.Element(Xdr + "to")!.Element(Xdr + "row")!;

        // Default column width ~67px, row height 20px: a 400x300 chart at Left=50/Top=50 spans well under
        // 20 columns/rows -- nowhere near the 16383/1048575 ceiling the exhaustion case clamps to.
        toCol.Should().BeLessThan(20,
            "an ordinary unhidden sheet must find room within the first handful of columns, not clamp at the ceiling");
        toRow.Should().BeLessThan(30,
            "an ordinary unhidden sheet must find room within the first handful of rows, not clamp at the ceiling");
    }

    private static void Save(MemoryStream package, Workbook workbook) =>
        XlsxWorksheetChartWriter.Save(
            package,
            workbook,
            _ => true,
            (_, _, _) => new XDocument(new XElement(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/chart") + "chartSpace")),
            _ => "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
            _ => ChartRelationshipType,
            null);

    private static XElement ReadSoleTwoCellAnchor(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var drawingEntry = archive.Entries.Single(e => e.FullName.StartsWith("xl/drawings/drawing", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal));
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(drawingEntry);
        return drawingXml.Root!.Elements(Xdr + "twoCellAnchor").Single();
    }

    private static MemoryStream CreatePackage() =>
        XlsxPackageTestFixtures.CreatePackage(
            ("[Content_Types].xml",
                """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                </Types>
                """),
            ("xl/workbook.xml",
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="Charted" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """),
            ("xl/_rels/workbook.xml.rels",
                """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """),
            ("xl/worksheets/sheet1.xml",
                """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData/></worksheet>
                """));
}
