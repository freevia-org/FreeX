using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 57 io-geo bucket findings:
///  - R57-io-theme-colors-5-1: legacy indexed color 65 ("System Background") must resolve to white, not
///    fall through to the default(CellColor) black used when TryResolveColor's 1..56 range check fails.
///  - R57-io-drawing-anchor-5-1: chart/picture anchor row-marker computation must use the sheet's real
///    row ceiling (1,048,576), not the 16,384 column cap, or any anchor at row &gt;= 16384 gets corrupted
///    to row ~16384 on save.
///  - R57-io-drawing-anchor-5-2: absoluteAnchor pos x/y is a signed OOXML coordinate and must not be
///    clamped to zero on save, or a legitimately negative chart position is silently snapped to 0.
/// </summary>
public sealed class R57_IoGeoBucketTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    // ---- R57-io-theme-colors-5-1 ----

    [Fact]
    public void TryReadCellColor_IndexedSystemBackground65_ResolvesToWhite()
    {
        var element = XElement.Parse("""<color indexed="65"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color)
            .Should().BeTrue("indexed=65 (System Background) is a recognized reserved OOXML color index");

        // Pre-fix: index+1=66 is out of WorkbookIndexedColorPalette's 1..56 range, TryResolveColor fails,
        // and the caller falls through to default(CellColor), which is (0,0,0) = black.
        color.Should().Be(CellColor.White,
            "OOXML reserved indexed color 65 is 'System Window Background', which Excel renders as white, " +
            "not black");
    }

    [Fact]
    public void TryReadCellColor_IndexedSystemForeground64_StillResolvesToBlack()
    {
        // Sibling/no-regression: indexed=64 ("System Foreground") must keep resolving to black. Before
        // this fix, index+1=65 was also out of WorkbookIndexedColorPalette's 1..56 range, so
        // TryReadCellColor returned false and `color` was left at its unset default -- which happens to
        // equal CellColor.Black (0,0,0) too, so the RESOLVED COLOR was already correct by coincidence
        // (per the finding's evidence). The fix must resolve indexed=64 to black explicitly (returning
        // true) without disturbing that resolved value.
        var element = XElement.Parse("""<color indexed="64"/>""");
        var indexedColors = new WorkbookIndexedColorPalette();

        XlsxColorReader.TryReadCellColor(element, WorkbookTheme.Office, indexedColors, out var color);
        color.Should().Be(CellColor.Black);
    }

    // ---- R57-io-drawing-anchor-5-1 (XlsxWorksheetChartWriter.ToAnchorMarker/ToMarkerIndex) ----

    [Fact]
    public void XlsxAdapter_OneCellChartAnchoredBeyondRow16384_RoundTripsTrueRowPosition()
    {
        var workbook = new Workbook("ChartRowOverflow");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        // sheet.DefaultRowHeight is 20px; 400000px lands the marker walk exactly at zero-based row
        // index 20000 (20000 * 20 = 400000) with zero leftover offset, when the walk isn't truncated.
        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell,
            Left = 50,
            Top = 400000,
            Width = 300,
            Height = 200,
        };
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var reloadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        // Pre-fix: ToMarkerIndex's row-axis walk shares the column axis's hardcoded `< 16384` bound, so
        // it truncates at zero-based row index 16384 (reloaded Top ~= 16384*20 + leftover = 327700),
        // corrupting the chart roughly 3,600+ rows up the sheet instead of preserving row 400000/20=20000.
        reloadedChart.Top.Should().Be(400000,
            "the row-axis marker walk must reach Excel's real row ceiling (1,048,576), not stop at the " +
            "16,384 column cap, or a chart anchored beyond row 16384 silently moves up the sheet on save");
    }

    [Fact]
    public void XlsxAdapter_OneCellChartAnchoredAtNormalRow_StillRoundTripsCorrectly()
    {
        // Sibling/no-regression: a chart anchored well within the pre-existing 16384 cap must keep
        // round-tripping exactly as before the axis-max parameterization.
        var workbook = new Workbook("ChartNormalRow");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DrawingAnchorKind = ChartDrawingAnchorKind.OneCell,
            Left = 50,
            Top = 100,
            Width = 300,
            Height = 200,
        };
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var reloadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        reloadedChart.Top.Should().Be(100);
        reloadedChart.Left.Should().Be(50);
    }

    // ---- R57-io-drawing-anchor-5-1 (XlsxSourceDrawingGeometryRewriter's duplicate ToMarkerIndex) ----

    [Fact]
    public void XlsxAdapter_ResizingSourcePictureAnchoredBeyondRow16384_ComputesCorrectToRow()
    {
        using var package = BuildPackageWithPictureTwoCellAnchor(
            fromRowZeroBased: 19999, toRowZeroBased: 20003);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.Height.Should().Be(80, "from-row 19999 to to-row 20003 spans 4 default-height (20px) rows");

        // Grow the picture so its to-row marker must be recomputed well past the from-row (19999).
        // fromTop (SumRowPixels(1,19999)) = 399980px; + heightPixels(5000) = 404980px, which lands
        // exactly at zero-based row 20249 (20249*20 = 404980) with zero leftover, when the walk isn't
        // truncated.
        picture.Height = 5000;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedPicture = reloaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;

        // Pre-fix: the rewriter's own ToMarkerIndex copy shares the same hardcoded `< 16384` bound, so
        // the to-row walk (which restarts from absolute row 1 each time) truncates at zero-based row
        // 16384 -- BELOW the from-row (19999) -- producing an inverted/corrupted twoCellAnchor and a
        // reloaded height far smaller than the resize actually requested.
        reloadedPicture.Height.Should().Be(5000,
            "the to-row marker walk must reach Excel's real row ceiling, not truncate at 16384, or " +
            "resizing a picture anchored deep in a large sheet silently corrupts its to-row anchor");
    }

    [Fact]
    public void XlsxAdapter_ResizingSourcePictureAtNormalRow_StillComputesCorrectToRow()
    {
        // Sibling/no-regression: a picture anchored well within the pre-existing 16384 cap must keep
        // resizing exactly as before the axis-max parameterization.
        using var package = BuildPackageWithPictureTwoCellAnchor(fromRowZeroBased: 1, toRowZeroBased: 5);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out var blockReason)
            .Should().BeTrue(blockReason);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.Height.Should().Be(80);

        picture.Height = 200;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedPicture = reloaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;

        reloadedPicture.Height.Should().Be(200);
    }

    // ---- R57-io-drawing-anchor-5-2 ----

    [Fact]
    public void XlsxAdapter_ChartWithNegativeAbsoluteAnchorPosition_RoundTripsNegativeValue()
    {
        var workbook = new Workbook("NegativeAbsoluteAnchor");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DrawingAnchorKind = ChartDrawingAnchorKind.Absolute,
            Left = -10,
            Top = -5,
            Width = 300,
            Height = 200,
        };
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var reloadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        // Pre-fix: PixelsToEmu clamps via Math.Max(0, pixels), so a negative absoluteAnchor pos is
        // silently floored to 0 EMU on save, and the chart reloads at (0,0) instead of (-10,-5).
        reloadedChart.Left.Should().Be(-10,
            "absoluteAnchor pos x uses a signed OOXML coordinate type and must not be clamped to zero");
        reloadedChart.Top.Should().Be(-5,
            "absoluteAnchor pos y uses a signed OOXML coordinate type and must not be clamped to zero");
    }

    [Fact]
    public void XlsxAdapter_ChartWithPositiveAbsoluteAnchorPosition_StillRoundTripsCorrectly()
    {
        // Sibling/no-regression: the ordinary (non-negative) absoluteAnchor case must be unaffected by
        // switching pos x/y to the unclamped signed conversion.
        var workbook = new Workbook("PositiveAbsoluteAnchor");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Line,
            DrawingAnchorKind = ChartDrawingAnchorKind.Absolute,
            Left = 50,
            Top = 75,
            Width = 300,
            Height = 200,
        };
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var reloadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        reloadedChart.Left.Should().Be(50);
        reloadedChart.Top.Should().Be(75);
    }

    private static MemoryStream BuildPackageWithPictureTwoCellAnchor(uint fromRowZeroBased, uint toRowZeroBased)
    {
        var workbook = new Workbook("PictureRowOverflow");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var adapter = new XlsxFileAdapter();
        var package = new MemoryStream();
        adapter.Save(workbook, package);

        package.Position = 0;
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            var mediaEntry = archive.CreateEntry("xl/media/image1.png", CompressionLevel.NoCompression);
            using (var mediaStream = mediaEntry.Open())
                mediaStream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

            var drawingXml = XDocument.Parse($"""
                <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNs}" xmlns:a="{DrawingNs}" xmlns:r="{RelNs}">
                  <xdr:twoCellAnchor>
                    <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{fromRowZeroBased}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{toRowZeroBased}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
                    <xdr:pic>
                      <xdr:nvPicPr>
                        <xdr:cNvPr id="2" name="Picture 1"/>
                        <xdr:cNvPicPr/>
                      </xdr:nvPicPr>
                      <xdr:blipFill>
                        <a:blip r:embed="rIdImage1"/>
                        <a:stretch><a:fillRect/></a:stretch>
                      </xdr:blipFill>
                      <xdr:spPr>
                        <a:xfrm><a:off x="0" y="0"/><a:ext cx="914400" cy="914400"/></a:xfrm>
                        <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                      </xdr:spPr>
                    </xdr:pic>
                    <xdr:clientData/>
                  </xdr:twoCellAnchor>
                </xdr:wsDr>
                """);
            WritePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);
            WritePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", XDocument.Parse($"""
                <Relationships xmlns="{PackageRelNs}">
                  <Relationship Id="rIdImage1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="../media/image1.png"/>
                </Relationships>
                """));

            var worksheetXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
            worksheetXml.Root!.Add(new XElement(WorksheetNs + "drawing", new XAttribute(RelNs + "id", "rIdDrawing1")));
            WritePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            const string worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } existingRelsEntry
                ? XlsxPackageTestFixtures.LoadPackageXml(existingRelsEntry)
                : new XDocument(new XElement(PackageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(PackageRelNs + "Relationship",
                new XAttribute("Id", "rIdDrawing1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            WritePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var contentTypesXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "[Content_Types].xml");
            if (!contentTypesXml.Root!.Elements(ContentTypeNs + "Default").Any(e => e.Attribute("Extension")?.Value == "png"))
            {
                contentTypesXml.Root!.Add(new XElement(ContentTypeNs + "Default",
                    new XAttribute("Extension", "png"),
                    new XAttribute("ContentType", "image/png")));
            }

            contentTypesXml.Root!.Add(new XElement(ContentTypeNs + "Override",
                new XAttribute("PartName", "/xl/drawings/drawing1.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.drawing+xml")));
            WritePackageXml(archive, "[Content_Types].xml", contentTypesXml);
        }

        package.Position = 0;
        return package;
    }

    private static void WritePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream, System.Xml.Linq.SaveOptions.DisableFormatting);
    }
}
