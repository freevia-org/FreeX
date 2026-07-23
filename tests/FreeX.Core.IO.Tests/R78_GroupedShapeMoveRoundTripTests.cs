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
/// R78-io-drawing-grpsp-move regressions for <see cref="XlsxSourceDrawingGeometryRewriter"/>: a
/// picture/shape nested inside an <c>xdr:grpSp</c> group has no anchor of its own -- its position/size
/// live in its own local <c>&lt;a:xfrm&gt;&lt;a:off&gt;</c>/<c>&lt;a:ext&gt;</c>, expressed in the
/// group's <c>chOff</c>/<c>chExt</c> child coordinate space. R72-io-drawing-anchors-4-1 correctly
/// stopped this rewriter from ever matching such an element against the group's SHARED anchor (doing
/// so would corrupt the whole group), but it did so by skipping the element entirely -- so a
/// move/resize applied to the in-memory model (accepted, rendered, undoable) was silently discarded on
/// every save: after save+reload the shape snapped back to its original pre-move geometry with no
/// error. These tests assert the shape's own local off/ext IS now rewritten (recomputed through the
/// inverse of the same chOff/chExt/rot/flip transform the reader composes), while the group's shared
/// anchor and an untouched sibling's own geometry remain byte-for-byte unchanged.
/// <para>
/// The group scenario is hand-assembled by restructuring a normally-written drawing part (the writer
/// never emits <c>xdr:grpSp</c> itself), mirroring
/// <see cref="XlsxSourceDrawingGeometryRewriterGroupAndAbsoluteAnchorTests"/>. The group here uses a
/// non-trivial 2x scale (<c>ext</c> = 2x <c>chExt</c>) so the tests also exercise the scale-aware
/// inversion, not just a pass-through 1:1 mapping.
/// </para>
/// </summary>
public sealed class R78_GroupedShapeMoveRoundTripTests
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    // Group xfrm: off=(0,0) ext=(1828800,1828800) [2in x 2in worksheet space], chOff=(0,0)
    // chExt=(914400,914400) [1in x 1in child space] -> scale factor 2 on both axes, no rotation/flip.
    private static XElement BuildGroup(XElement picOrSpA, XElement picOrSpB) =>
        new(Xdr + "grpSp",
            new XElement(Xdr + "nvGrpSpPr",
                new XElement(Xdr + "cNvPr", new XAttribute("id", 100), new XAttribute("name", "Group 1")),
                new XElement(Xdr + "cNvGrpSpPr")),
            new XElement(Xdr + "grpSpPr",
                new XElement(A + "xfrm",
                    new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(A + "ext", new XAttribute("cx", 1828800), new XAttribute("cy", 1828800)),
                    new XElement(A + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(A + "chExt", new XAttribute("cx", 914400), new XAttribute("cy", 914400)))),
            picOrSpA,
            picOrSpB);

    private static XElement BuildTwoCellAnchor(XElement grpSp) =>
        new(Xdr + "twoCellAnchor",
            new XElement(Xdr + "from",
                new XElement(Xdr + "col", 1), new XElement(Xdr + "colOff", 0),
                new XElement(Xdr + "row", 20), new XElement(Xdr + "rowOff", 0)),
            new XElement(Xdr + "to",
                new XElement(Xdr + "col", 10), new XElement(Xdr + "colOff", 0),
                new XElement(Xdr + "row", 40), new XElement(Xdr + "rowOff", 0)),
            grpSp,
            new XElement(Xdr + "clientData"));

    [Fact]
    public void MovingAndResizingAGroupedPicture_RoundTripsTheEdit_InsteadOfSilentlyReverting()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("GroupedPictureMove");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "GroupChildA", 6);
        AddPicture(sheet, "GroupChildB", 10);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

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

            // GroupChildA: local off=(0,0), ext=(457200,457200) [48x48 EMU-inches/100 local] -> at
            // scale 2 this is 96x96px in worksheet space, positioned at the group's own origin.
            picA.Element(Xdr + "spPr")!.Element(A + "xfrm")!.Add(
                new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(A + "ext", new XAttribute("cx", 457200), new XAttribute("cy", 457200)));
            // GroupChildB: local off=(457200,457200), ext=(228600,228600) -- left untouched by the
            // test's edit below, so its own local off/ext must round-trip byte-for-byte.
            picB.Element(Xdr + "spPr")!.Element(A + "xfrm")!.Add(
                new XElement(A + "off", new XAttribute("x", 457200), new XAttribute("y", 457200)),
                new XElement(A + "ext", new XAttribute("cx", 228600), new XAttribute("cy", 228600)));

            root.Add(BuildTwoCellAnchor(BuildGroup(picA, picB)));
        });

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;
        reloadedSheet.Pictures.Should().HaveCount(2);
        reloadedSheet.Pictures.Should().OnlyContain(picture => picture.IsSourceLoaded);

        var groupChildA = reloadedSheet.Pictures.Single(picture => picture.Name == "GroupChildA");
        // Sanity: the loader correctly composed the group's 2x scale before the edit.
        groupChildA.Width.Should().BeApproximately(96, 0.01);
        groupChildA.Height.Should().BeApproximately(96, 0.01);
        groupChildA.AnchorOffsetX.Should().BeApproximately(0, 0.01);
        groupChildA.AnchorOffsetY.Should().BeApproximately(0, 0.01);

        // The user drags GroupChildA to a new position and resizes it -- exactly the finding's
        // failure scenario (accepted in-memory, undoable, rendered) that used to silently revert.
        groupChildA.AnchorOffsetX = 50;
        groupChildA.AnchorOffsetY = 30;
        groupChildA.Width = 200;
        groupChildA.Height = 150;

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        secondSave.Position = 0;
        var finalSheet = adapter.Load(secondSave).GetSheet("Sheet1")!;
        var finalGroupChildA = finalSheet.Pictures.Single(picture => picture.Name == "GroupChildA");
        finalGroupChildA.AnchorOffsetX.Should().BeApproximately(50, 0.01,
            "the finding: a grouped picture's move must survive a save+reload instead of snapping back to its original position");
        finalGroupChildA.AnchorOffsetY.Should().BeApproximately(30, 0.01);
        finalGroupChildA.Width.Should().BeApproximately(200, 0.01,
            "the finding: a grouped picture's resize must survive a save+reload instead of snapping back to its original size");
        finalGroupChildA.Height.Should().BeApproximately(150, 0.01);
    }

    [Fact]
    public void MovingOneGroupedPicture_LeavesTheSharedGroupAnchorAndTheUneditedSiblingByteUnchanged()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("GroupedPictureMoveNoRegression");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "GroupChildA", 6);
        AddPicture(sheet, "GroupChildB", 10);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

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
                new XElement(A + "ext", new XAttribute("cx", 457200), new XAttribute("cy", 457200)));
            picB.Element(Xdr + "spPr")!.Element(A + "xfrm")!.Add(
                new XElement(A + "off", new XAttribute("x", 457200), new XAttribute("y", 457200)),
                new XElement(A + "ext", new XAttribute("cx", 228600), new XAttribute("cy", 228600)));

            root.Add(BuildTwoCellAnchor(BuildGroup(picA, picB)));
        });

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;

        var originalGroupAnchorXml = SharedGroupMarkersXml(ReadDrawingXml(initialSave).Descendants(Xdr + "twoCellAnchor").Single());
        var originalGroupChildBXml = ReadDrawingXml(initialSave).Descendants(Xdr + "pic")
            .Single(pic => pic.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "GroupChildB"))
            .ToString(SaveOptions.DisableFormatting);

        var groupChildA = reloadedSheet.Pictures.Single(picture => picture.Name == "GroupChildA");
        groupChildA.AnchorOffsetX = 50;
        groupChildA.AnchorOffsetY = 30;
        groupChildA.Width = 200;
        groupChildA.Height = 150;
        // GroupChildB is deliberately left untouched.

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        var savedDrawingXml = ReadDrawingXml(secondSave);
        var savedGroupAnchor = savedDrawingXml.Descendants(Xdr + "twoCellAnchor").Single();
        // Compare only the SHARED markers (from/to + grpSpPr/xfrm), not the whole subtree: GroupChildA's
        // own local off/ext (a descendant of this same twoCellAnchor) is expected to change -- that's
        // the fix under test, verified separately in
        // MovingAndResizingAGroupedPicture_RoundTripsTheEdit_InsteadOfSilentlyReverting.
        SharedGroupMarkersXml(savedGroupAnchor).Should().Be(originalGroupAnchorXml,
            "a grouped child's own model edit must never be written into the group's shared twoCellAnchor (R72-io-drawing-anchors-4-1)");

        var savedGroupChildB = savedDrawingXml.Descendants(Xdr + "pic")
            .Single(pic => pic.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "GroupChildB"));
        savedGroupChildB.ToString(SaveOptions.DisableFormatting).Should().Be(originalGroupChildBXml,
            "an unedited sibling inside the same group must round-trip byte-for-byte, not get scrambled by including group children in the matching pool");
    }

    /// <summary>
    /// Serializes only the group's SHARED markers -- the enclosing twoCellAnchor's from/to cell
    /// markers and the grpSp's own grpSpPr/xfrm (off/ext/chOff/chExt) -- rather than the whole subtree,
    /// which also contains the (now legitimately rewritable) descendant pic/sp elements.
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
