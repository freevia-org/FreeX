using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R72-io-drawing-anchors-4-1/4-2 regressions for <see cref="XlsxSourceDrawingGeometryRewriter"/>:
/// <list type="bullet">
/// <item>a picture nested inside an <c>xdr:grpSp</c> group must never have its child geometry rewritten
/// into the group's own SHARED <c>xdr:twoCellAnchor</c> -- doing so silently shifts/resizes the whole
/// group from a single child's edit (and the next matched child in the group overwrites it again, so
/// the first child's edit is discarded).</item>
/// <item>an <c>xdr:absoluteAnchor</c> (<c>xdr:pos</c>/<c>xdr:ext</c>, no <c>xdr:from</c>/<c>xdr:to</c>)
/// must still have its position/size rewritten from the model -- the old code's from-null guard
/// short-circuited before ever reaching the position/size rewrite, silently discarding any
/// move/resize of an absolutely-anchored source-loaded picture.</item>
/// </list>
/// Both scenarios are hand-assembled by restructuring a normally-written drawing part (the writer never
/// emits <c>xdr:grpSp</c> or <c>xdr:absoluteAnchor</c> for pictures itself), mirroring the direct
/// zip/XML manipulation <c>XlsxSourceDrawingGeometryRewriterPictureIdentityTests</c> already uses.
/// </summary>
public sealed class XlsxSourceDrawingGeometryRewriterGroupAndAbsoluteAnchorTests
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void ResizingOneGroupedPicture_LeavesTheSharedGroupAnchorByteUnchanged_AndSiblingUngroupedPictureStillRewrites()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("GroupAnchorPreservation");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "Solo", 2);
        AddPicture(sheet, "GroupChildA", 6);
        AddPicture(sheet, "GroupChildB", 10);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        // Restructure the preserved drawing part: pull GroupChildA/GroupChildB's <xdr:pic> elements out
        // of their individual oneCellAnchors and wrap both inside a single <xdr:grpSp> under one shared
        // <xdr:twoCellAnchor> -- exactly the "two pictures inside one grpSp" shape the finding describes.
        // "Solo" is left as a normal, ungrouped oneCellAnchor picture (the no-regression control).
        RewriteDrawingXml(initialSave, drawingXml =>
        {
            var root = drawingXml.Root!;
            var anchors = root.Elements(Xdr + "oneCellAnchor").ToList();

            XElement FindAnchorFor(string name) =>
                anchors.Single(anchor => anchor.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == name));

            var anchorA = FindAnchorFor("GroupChildA");
            var anchorB = FindAnchorFor("GroupChildB");
            var picA = anchorA.Element(Xdr + "pic")!;
            var picB = anchorB.Element(Xdr + "pic")!;
            picA.Remove();
            picB.Remove();
            anchorA.Remove();
            anchorB.Remove();

            picA.Element(Xdr + "spPr")!.Element(A + "xfrm")!.Add(
                new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext", new XAttribute("cx", 914400), new XAttribute("cy", 914400)));
            picB.Element(Xdr + "spPr")!.Element(A + "xfrm")!.Add(
                new XElement(A + "off", new XAttribute("x", 914400), new XAttribute("y", 914400)),
                new XElement(A + "ext", new XAttribute("cx", 457200), new XAttribute("cy", 457200)));

            var grpSp = new XElement(Xdr + "grpSp",
                new XElement(Xdr + "nvGrpSpPr",
                    new XElement(Xdr + "cNvPr", new XAttribute("id", 100), new XAttribute("name", "Group 1")),
                    new XElement(Xdr + "cNvGrpSpPr")),
                new XElement(Xdr + "grpSpPr",
                    new XElement(A + "xfrm",
                        new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                        new XElement(A + "ext", new XAttribute("cx", 1828800), new XAttribute("cy", 1828800)),
                        new XElement(A + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)),
                        new XElement(A + "chExt", new XAttribute("cx", 1828800), new XAttribute("cy", 1828800)))),
                picA,
                picB);

            root.Add(new XElement(Xdr + "twoCellAnchor",
                new XElement(Xdr + "from",
                    new XElement(Xdr + "col", 1), new XElement(Xdr + "colOff", 0),
                    new XElement(Xdr + "row", 20), new XElement(Xdr + "rowOff", 0)),
                new XElement(Xdr + "to",
                    new XElement(Xdr + "col", 10), new XElement(Xdr + "colOff", 0),
                    new XElement(Xdr + "row", 40), new XElement(Xdr + "rowOff", 0)),
                grpSp,
                new XElement(Xdr + "clientData")));
        });

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        reloadedSheet.Pictures.Should().HaveCount(3);
        reloadedSheet.Pictures.Should().OnlyContain(picture => picture.IsSourceLoaded);

        var originalGroupAnchorXml = SharedGroupMarkersXml(ReadDrawingXml(initialSave).Descendants(Xdr + "twoCellAnchor").Single());

        var groupChildA = reloadedSheet.Pictures.Single(picture => picture.Name == "GroupChildA");
        groupChildA.Width = 500;
        groupChildA.Height = 400;
        var solo = reloadedSheet.Pictures.Single(picture => picture.Name == "Solo");
        solo.Width = 321;
        solo.Height = 111;

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        var savedDrawingXml = ReadDrawingXml(secondSave);
        var savedGroupAnchor = savedDrawingXml.Descendants(Xdr + "twoCellAnchor").Single();

        // The finding's core assertion: the group's SHARED anchor (from/to markers, and the group's own
        // xfrm/off/ext/chOff/chExt) must be completely untouched by GroupChildA's resize -- not shifted,
        // not resized, not overwritten by a subsequently-processed sibling's geometry either. This
        // compares only those shared markers (not the whole subtree): R78-io-drawing-grpsp-move made
        // GroupChildA's own local <a:off>/<a:ext> (a descendant of this same twoCellAnchor) legitimately
        // change to reflect its resize -- that is the fix, not a regression.
        SharedGroupMarkersXml(savedGroupAnchor).Should().Be(originalGroupAnchorXml,
            "a grouped child's own model edit must never be written into the group's shared twoCellAnchor");

        // R78-io-drawing-grpsp-move: GroupChildA's own local off/ext (inside the group's chOff/chExt
        // child space, scale 1 in this fixture since ext == chExt) must now reflect the resize instead
        // of silently reverting to the original 96x64 on save+reload.
        var savedGroupChildA = savedDrawingXml.Descendants(Xdr + "pic")
            .Single(pic => pic.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "GroupChildA"));
        var groupChildAExt = savedGroupChildA.Element(Xdr + "spPr")!.Element(A + "xfrm")!.Element(A + "ext")!;
        groupChildAExt.Attribute("cx")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(500).ToString(),
            "a grouped picture's resize must be written into its own local xfrm ext, not silently dropped");
        groupChildAExt.Attribute("cy")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(400).ToString());

        // No-regression: the sibling ungrouped picture's own oneCellAnchor must still be rewritten from
        // its model as before.
        var soloAnchor = savedDrawingXml.Root!.Elements(Xdr + "oneCellAnchor")
            .Single(anchor => anchor.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "Solo"));
        var soloExt = soloAnchor.Element(Xdr + "ext")!;
        soloExt.Attribute("cx")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(321).ToString(),
            "a non-grouped source-loaded picture must still have its width rewritten");
        soloExt.Attribute("cy")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(111).ToString(),
            "a non-grouped source-loaded picture must still have its height rewritten");
    }

    [Fact]
    public void MovingAndResizingASourceLoadedAbsoluteAnchorPicture_UpdatesPosAndExt_AndSiblingOneCellAnchorStillRewrites()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("AbsoluteAnchorRewrite");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "AbsPic", 2);
        AddPicture(sheet, "NormalPic", 6);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        // Convert "AbsPic"'s normal oneCellAnchor into an absoluteAnchor (xdr:pos/xdr:ext, no
        // xdr:from/xdr:to at all) -- the shape the writer itself never produces for pictures, but which
        // Excel's own absolutely-positioned pictures use.
        RewriteDrawingXml(initialSave, drawingXml =>
        {
            var root = drawingXml.Root!;
            var absAnchorSource = root.Elements(Xdr + "oneCellAnchor")
                .Single(anchor => anchor.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "AbsPic"));
            var pic = absAnchorSource.Element(Xdr + "pic")!;
            pic.Remove();
            absAnchorSource.Remove();

            root.Add(new XElement(Xdr + "absoluteAnchor",
                new XElement(Xdr + "pos", new XAttribute("x", 200000), new XAttribute("y", 100000)),
                new XElement(Xdr + "ext", new XAttribute("cx", 300000), new XAttribute("cy", 150000)),
                pic,
                new XElement(Xdr + "clientData")));
        });

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        reloadedSheet.Pictures.Should().HaveCount(2);

        var absPic = reloadedSheet.Pictures.Single(picture => picture.Name == "AbsPic");
        absPic.IsSourceLoaded.Should().BeTrue();
        absPic.Width = 500;
        absPic.Height = 250;
        absPic.AnchorOffsetX = 800;
        absPic.AnchorOffsetY = 400;

        var normalPic = reloadedSheet.Pictures.Single(picture => picture.Name == "NormalPic");
        normalPic.Width = 222;
        normalPic.Height = 333;

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        var savedDrawingXml = ReadDrawingXml(secondSave);
        var savedAbsoluteAnchor = savedDrawingXml.Descendants(Xdr + "absoluteAnchor").Single();
        var pos = savedAbsoluteAnchor.Element(Xdr + "pos")!;
        var ext = savedAbsoluteAnchor.Element(Xdr + "ext")!;

        pos.Attribute("x")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(800).ToString(),
            "the absoluteAnchor's xdr:pos x must reflect the model's new AnchorOffsetX, not the stale original");
        pos.Attribute("y")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(400).ToString(),
            "the absoluteAnchor's xdr:pos y must reflect the model's new AnchorOffsetY, not the stale original");
        ext.Attribute("cx")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(500).ToString(),
            "the absoluteAnchor's xdr:ext cx must reflect the model's new Width, not the stale original");
        ext.Attribute("cy")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(250).ToString(),
            "the absoluteAnchor's xdr:ext cy must reflect the model's new Height, not the stale original");

        // No-regression: an ordinary oneCellAnchor sibling picture must still rewrite its own ext.
        var normalAnchor = savedDrawingXml.Root!.Elements(Xdr + "oneCellAnchor")
            .Single(anchor => anchor.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "NormalPic"));
        var normalExt = normalAnchor.Element(Xdr + "ext")!;
        normalExt.Attribute("cx")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(222).ToString());
        normalExt.Attribute("cy")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(333).ToString());

        // Reload once more end-to-end: the model must come back with exactly the edited geometry.
        secondSave.Position = 0;
        var finalSheet = adapter.Load(secondSave).GetSheet("Sheet1")!;
        var finalAbsPic = finalSheet.Pictures.Single(picture => picture.Name == "AbsPic");
        finalAbsPic.Width.Should().BeApproximately(500, 0.01);
        finalAbsPic.Height.Should().BeApproximately(250, 0.01);
        finalAbsPic.AnchorOffsetX.Should().BeApproximately(800, 0.01);
        finalAbsPic.AnchorOffsetY.Should().BeApproximately(400, 0.01);
    }

    /// <summary>
    /// R78-io-drawing-grpsp-move: serializes only the group's SHARED markers -- the enclosing
    /// twoCellAnchor's from/to cell markers and the grpSp's own grpSpPr/xfrm (off/ext/chOff/chExt) --
    /// rather than the whole subtree. A grouped child's own local off/ext (nested inside the same
    /// twoCellAnchor as a descendant pic/sp) is now legitimately rewritten by the fix, so comparing
    /// full subtree bytes would flag that intentional change as if it were group corruption.
    /// </summary>
    private static string SharedGroupMarkersXml(XElement twoCellAnchor)
    {
        var grpSp = twoCellAnchor.Element(Xdr + "grpSp")!;
        return new XElement("shared",
            new XElement(twoCellAnchor.Element(Xdr + "from")!),
            new XElement(twoCellAnchor.Element(Xdr + "to")!),
            new XElement(grpSp.Element(Xdr + "grpSpPr")!)).ToString(SaveOptions.DisableFormatting);
    }

    private static void AddPicture(Sheet sheet, string name, uint row) =>
        sheet.Pictures.Add(new PictureModel
        {
            Name = name,
            Anchor = new CellAddress(sheet.Id, row, 2),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 96,
            Height = 64
        });

    private static void RewriteDrawingXml(MemoryStream packageStream, Action<XDocument> mutate)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        var entry = archive.GetEntry("xl/drawings/drawing1.xml")!;

        XDocument drawingXml;
        using (var reader = new StreamReader(entry.Open()))
            drawingXml = XDocument.Parse(reader.ReadToEnd());

        mutate(drawingXml);

        entry.Delete();
        var newEntry = archive.CreateEntry("xl/drawings/drawing1.xml");
        using var writer = new StreamWriter(newEntry.Open());
        writer.Write(drawingXml.ToString(SaveOptions.DisableFormatting));
    }

    private static XDocument ReadDrawingXml(MemoryStream packageStream)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry("xl/drawings/drawing1.xml")!;
        using var reader = new StreamReader(entry.Open());
        return XDocument.Parse(reader.ReadToEnd());
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
}
