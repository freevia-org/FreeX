using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R119-io-camera-linked-picture-identity: <see cref="PasteRangeAsPictureCommand"/> (the real Paste
/// as Picture / Paste Special &gt; Linked Picture ("Camera") entry point) creates a
/// <see cref="PictureModel"/> with <see cref="PictureModel.Kind"/> ==
/// <see cref="PictureKind.CellRangeSnapshot"/> and, for the Linked Picture variant,
/// <see cref="PictureModel.IsLinkedToSourceRange"/>/<see cref="PictureModel.LinkedSourceRange"/>/
/// <see cref="PictureModel.LinkedSourceSheetName"/> set. The command never rasterizes the picture
/// (<see cref="PictureModel.ImageBytes"/> stays null forever), so
/// <c>XlsxWorksheetDrawingObjectWriter.ToOneCellPictureSnapshotAnchor</c> re-emits it as a vector
/// <c>&lt;xdr:grpSp&gt;</c> (a background rectangle plus one rectangle+text shape per cached cell).
/// Before this fix, that group carried NONE of the linked-identity metadata anywhere in its XML, and
/// <c>XlsxWorksheetDrawingParts.ReadShapeParts</c> walks every <c>&lt;xdr:sp&gt;</c> regardless of
/// grpSp nesting, so it flattened the group into independent, ungrouped
/// <see cref="DrawingShapeModel"/> objects with no way back to a <see cref="PictureModel"/> at all --
/// permanently destroying the picture's identity, and (for the linked variant) its live link, on
/// every single .xlsx save+reload. These tests drive the command itself (not a hand-built model)
/// through a real save+reload -- the real product entry point per this round's testing rule.
/// </summary>
public sealed class R119_CameraLinkedPictureIdentityTests
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Workbook SaveAndReload(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        return new XlsxFileAdapter().Load(stream);
    }

    /// <summary>
    /// THE fail-before/pass-after test for the defect: a Paste Special &gt; Linked Picture ("Camera")
    /// object must still be a single, identifiable, LINKED <see cref="PictureModel"/> after a
    /// save+reload -- not flattened into disconnected shapes with the link silently dropped.
    /// </summary>
    [Fact]
    public void R119_LinkedPicture_SurvivesSaveReload_AsASingleLinkedPictureNotFlattenedShapes()
    {
        var wb = new Workbook("CameraLinkedPicture");
        var sheet = wb.AddSheet("Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var sourceCells = new List<(CellAddress Address, string Text)>
        {
            (new CellAddress(sheet.Id, 1, 1), "Alpha"),
            (new CellAddress(sheet.Id, 2, 2), "Beta"),
        };
        var destination = new CellAddress(sheet.Id, 5, 5);
        var ctx = new TestCommandContext(wb);

        // The real product entry point: Paste Special > Linked Picture ("Camera").
        var command = new PasteRangeAsPictureCommand(
            sheet.Id, sourceRange, sourceCells, destination,
            isLinkedToSourceRange: true, sourceSheetName: "Sheet1");
        command.Apply(ctx).Success.Should().BeTrue();

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;

        reloadedSheet.DrawingShapes.Should().BeEmpty(
            "the linked picture's per-cell rectangles must not flatten into independent, disconnected shapes");
        var reloadedPicture = reloadedSheet.Pictures.Should().ContainSingle().Subject;
        reloadedPicture.Kind.Should().Be(PictureKind.CellRangeSnapshot);
        reloadedPicture.IsLinkedToSourceRange.Should().BeTrue(
            "the finding: a Linked Picture / Camera object must stay live-linked across a save+reload");
        reloadedPicture.LinkedSourceSheetName.Should().Be("Sheet1");
        reloadedPicture.LinkedSourceRange.Should().NotBeNull();
        var linkedRange = reloadedPicture.LinkedSourceRange!.Value;
        linkedRange.Start.Row.Should().Be(1);
        linkedRange.Start.Col.Should().Be(1);
        linkedRange.End.Row.Should().Be(2);
        linkedRange.End.Col.Should().Be(2);
        linkedRange.Start.Sheet.Should().Be(reloadedSheet.Id,
            "the re-resolved link must point at the reloaded Sheet1, not a stale/default sheet id");
        reloadedPicture.Cells.Select(c => c.Text).Should().Contain(["Alpha", "Beta"]);
    }

    /// <summary>
    /// No-regression sibling: the plain "Paste as Picture" variant (no live link) must still
    /// reconstruct as a single CellRangeSnapshot picture too -- the fix must not accidentally make
    /// every reconstructed camera picture look "linked".
    /// </summary>
    [Fact]
    public void R119_UnlinkedPasteAsPicture_StillReconstructsAsASinglePicture_ButNotLinked()
    {
        var wb = new Workbook("PasteAsPicture");
        var sheet = wb.AddSheet("Sheet1");
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        var sourceCells = new List<(CellAddress Address, string Text)> { (new CellAddress(sheet.Id, 1, 1), "Plain") };
        var destination = new CellAddress(sheet.Id, 5, 5);
        var ctx = new TestCommandContext(wb);

        // The real product entry point: Paste as Picture (no live link requested).
        var command = new PasteRangeAsPictureCommand(sheet.Id, sourceRange, sourceCells, destination);
        command.Apply(ctx).Success.Should().BeTrue();

        var reloaded = SaveAndReload(wb);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;

        reloadedSheet.DrawingShapes.Should().BeEmpty();
        var reloadedPicture = reloadedSheet.Pictures.Should().ContainSingle().Subject;
        reloadedPicture.Kind.Should().Be(PictureKind.CellRangeSnapshot);
        reloadedPicture.IsLinkedToSourceRange.Should().BeFalse();
        reloadedPicture.LinkedSourceRange.Should().BeNull();
        reloadedPicture.Cells.Should().Contain(c => c.Text == "Plain");
    }

    /// <summary>
    /// No-regression sibling for the neighbouring behaviour this fix must not break: an ORDINARY,
    /// user-authored group of shapes (Excel's own "Group" command) -- which carries no
    /// fx:linkedPictureSnapshot marker at all -- must keep flattening into independent
    /// <see cref="DrawingShapeModel"/> objects exactly as before (R14's accepted behaviour for a
    /// real multi-shape group), not get swept up into a phantom picture reconstruction.
    /// <para>
    /// FreeX's own writer never emits an ordinary (unmarked) &lt;xdr:grpSp&gt; -- the ONLY grpSp it
    /// ever writes is this fix's marked camera reconstruction -- so, per this round's round-trip
    /// fixture rule, a genuine round-trip fixture for "ordinary group" is impossible to obtain from
    /// our own writer. This hand-assembles the group by restructuring a normally-written drawing
    /// part, exactly mirroring the established precedent in R78_GroupedShapeMoveRoundTripTests/
    /// R42_DrawingGroupTransformTests/R36_DrawingAnchorGroupOleTests for the same reason.
    /// </para>
    /// </summary>
    [Fact]
    public void R119_OrdinaryUnmarkedShapeGroup_StillFlattensNormally_NoRegression()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("OrdinaryShapeGroup");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "PlainRect",
            Anchor = new CellAddress(sheet.Id, 3, 3),
            Kind = DrawingShapeKind.Rectangle,
            Width = 80,
            Height = 40,
            ShapeText = "GroupText"
        });

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        RewriteDrawingXml(initialSave, drawingXml =>
        {
            var root = drawingXml.Root!;
            var anchor = root.Elements(Xdr + "oneCellAnchor")
                .Single(a => a.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "PlainRect"));
            var sp = anchor.Element(Xdr + "sp")!;
            var ext = anchor.Element(Xdr + "ext")!;
            var widthEmu = (long)ext.Attribute("cx")!;
            var heightEmu = (long)ext.Attribute("cy")!;
            sp.Remove();
            anchor.Remove();

            // No fx:linkedPictureSnapshot marker anywhere -- an ordinary, unmarked group.
            var group = new XElement(Xdr + "grpSp",
                new XElement(Xdr + "nvGrpSpPr",
                    new XElement(Xdr + "cNvPr", new XAttribute("id", 500), new XAttribute("name", "Group 1")),
                    new XElement(Xdr + "cNvGrpSpPr")),
                new XElement(Xdr + "grpSpPr",
                    new XElement(A + "xfrm",
                        new XElement(A + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                        new XElement(A + "ext", new XAttribute("cx", widthEmu), new XAttribute("cy", heightEmu)),
                        new XElement(A + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)),
                        new XElement(A + "chExt", new XAttribute("cx", widthEmu), new XAttribute("cy", heightEmu)))),
                sp);
            root.Add(new XElement(Xdr + "oneCellAnchor",
                new XElement(anchor.Element(Xdr + "from")!),
                new XElement(Xdr + "ext", new XAttribute("cx", widthEmu), new XAttribute("cy", heightEmu)),
                group,
                new XElement(Xdr + "clientData")));
        });

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;

        reloadedSheet.Pictures.Should().BeEmpty(
            "an ordinary unmarked group must never be mistaken for a reconstructed camera picture");
        reloadedSheet.DrawingShapes.Should().ContainSingle(s => s.ShapeText == "GroupText",
            "an ordinary grouped shape must keep flattening into a DrawingShapeModel exactly as before this fix");
    }

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
}
