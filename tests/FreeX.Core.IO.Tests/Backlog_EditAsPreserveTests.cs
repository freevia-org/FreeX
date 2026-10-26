using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Backlog item "editAs-preserve". <c>xdr:twoCellAnchor/@editAs</c> (values <c>twoCell</c> | <c>oneCell</c> |
/// <c>absolute</c>) controls how a drawing object moves/resizes when its spanned rows/columns are resized.
/// FreeX never models the attribute and never implements the actual resize-on-row/col-resize behavior (that
/// is a much larger, separately-scoped modeling change) -- this backlog item is scoped ONLY to proving the
/// attribute round-trips unchanged through a load+save cycle, or documenting precisely where it doesn't.
/// <para>
/// Investigation result: for pictures/shapes/text boxes loaded FROM an .xlsx
/// (<see cref="PictureModel.IsSourceLoaded"/> / <see cref="DrawingShapeModel.IsSourceLoaded"/> /
/// <see cref="TextBoxModel.IsSourceLoaded"/> == true), <c>XlsxWorksheetDrawingObjectWriter</c> never
/// re-emits their anchor XML at all -- per its own doc comment, the ORIGINAL <c>&lt;xdr:twoCellAnchor&gt;</c>
/// element (attributes included) is instead copied VERBATIM into the saved package by
/// <c>XlsxWorksheetDrawingPartMerger.MergeDrawingPart</c> (<c>new XElement(sourceAnchor)</c>), and
/// <c>XlsxSourceDrawingGeometryRewriter</c> (which patches a resized/moved object's geometry back into that
/// preserved XML) only ever rewrites specific CHILD elements (<c>from</c>/<c>to</c> offsets, <c>ext</c>) in
/// place -- it never touches the anchor element's own attributes. So <c>editAs</c> already survives a save
/// unchanged for these object kinds, with or without a resize, and <see cref="XlsxDrawingAnchorApplier"/>
/// (this backlog item's assigned file) never participates in that write path at all -- there is nothing for
/// it to read or preserve, because the attribute was never lost. The tests below pin that down.
/// </para>
/// <para>
/// The one place <c>editAs</c> is a REAL, PROVEN gap is chart drawing anchors: <c>XlsxWorksheetChartWriter</c>
/// always regenerates a fresh <c>&lt;xdr:twoCellAnchor&gt;</c>/<c>&lt;xdr:oneCellAnchor&gt;</c>/
/// <c>&lt;xdr:absoluteAnchor&gt;</c> from <see cref="ChartModel"/> -- there is no chart equivalent of the
/// picture/shape/text-box <c>IsSourceLoaded</c> verbatim-preservation path -- and it never writes an
/// <c>editAs</c> attribute at all. So a chart anchor authored with <c>editAs="oneCell"</c> in the source file
/// silently loses it on save (Excel then falls back to its implicit default, <c>"twoCell"</c>). Preserving it
/// would need a new <see cref="ChartModel"/> field (e.g. <c>ChartModel.EditAs</c>) read by
/// <see cref="XlsxDrawingAnchorApplier.ApplyToChart"/> and written by <c>XlsxWorksheetChartWriter</c> --
/// both out of this backlog item's file scope (only <c>XlsxDrawingAnchorApplier.cs</c> may be edited).
/// <see cref="XlsxAdapter_ChartTwoCellAnchorEditAs_IsCurrentlyDroppedOnSave_DeferredGap"/> pins the CURRENT
/// (gap) behavior down with a passing test, so a future fix targeting <c>ChartModel</c>/
/// <c>XlsxWorksheetChartWriter</c> has a clear, already-written assertion to flip instead of silently
/// regressing further. DEFERRED: chart editAs preservation needs a ChartModel field change out of scope here.
/// </para>
/// </summary>
public sealed class Backlog_editAs_preserve_Tests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace SpreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    [Theory]
    [InlineData("oneCell")]
    [InlineData("absolute")]
    [InlineData("twoCell")]
    public void XlsxAdapter_RoundTripsTwoCellAnchorEditAsAttribute_ForSourceLoadedPicture(string editAs)
    {
        using var package = BuildPackageWithPictureTwoCellAnchor(editAs);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.IsSourceLoaded.Should().BeTrue(
            "the picture was read from the source .xlsx package, not freshly authored in this session");

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var anchor = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").Should().ContainSingle().Subject;
        anchor.Attribute("editAs")!.Value.Should().Be(editAs,
            "editAs describes move/resize behavior and must survive a save unchanged even though FreeX " +
            "never models/implements the resize behavior itself (round-trip preservation only, per backlog scope)");
    }

    [Fact]
    public void XlsxAdapter_RoundTripsTwoCellAnchorWithoutEditAs_LeavesAttributeAbsent()
    {
        // editAs is optional on a real twoCellAnchor (Excel's implicit default when absent is "twoCell").
        // Confirm FreeX doesn't invent the attribute out of thin air for a source anchor that never had one.
        using var package = BuildPackageWithPictureTwoCellAnchor(editAs: null);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var anchor = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").Should().ContainSingle().Subject;
        anchor.Attribute("editAs").Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_RoundTripsTwoCellAnchorEditAs_EvenWhenPictureIsResized()
    {
        // A resize/move on a source-loaded picture is patched into the PRESERVED anchor XML in place by
        // XlsxSourceDrawingGeometryRewriter (only the from/to/ext child elements are touched). Confirm that
        // patching never disturbs the sibling editAs attribute living on the anchor element itself.
        using var package = BuildPackageWithPictureTwoCellAnchor("oneCell");

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.Width *= 2;
        picture.Height *= 2;

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/drawings/drawing1.xml");
        var anchor = drawingXml.Descendants(SpreadsheetDrawingNs + "twoCellAnchor").Should().ContainSingle().Subject;
        anchor.Attribute("editAs")!.Value.Should().Be("oneCell");
    }


    private static MemoryStream BuildPackageWithPictureTwoCellAnchor(string? editAs)
    {
        var workbook = new Workbook("EditAsPreserve");
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

            var editAsAttribute = editAs is null ? string.Empty : $" editAs=\"{editAs}\"";
            var drawingXml = XDocument.Parse($"""
                <xdr:wsDr xmlns:xdr="{SpreadsheetDrawingNs}" xmlns:a="{DrawingNs}" xmlns:r="{RelNs}">
                  <xdr:twoCellAnchor{editAsAttribute}>
                    <xdr:from><xdr:col>1</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>
                    <xdr:to><xdr:col>4</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>8</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>
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
