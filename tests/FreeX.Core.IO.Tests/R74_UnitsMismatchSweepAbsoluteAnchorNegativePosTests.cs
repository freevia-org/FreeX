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
/// R74-units-mismatch-sweep-1 regression: <see cref="XlsxSourceDrawingGeometryRewriter"/>'s absoluteAnchor
/// path rewrote <c>xdr:pos</c> x/y through the same clamped pixel-&gt;EMU helper used for sizes/offsets
/// (<c>Math.Max(0, pixels)</c>), so a legitimately-NEGATIVE absoluteAnchor position (a picture positioned
/// left of/above the sheet origin, e.g. <c>xdr:pos x="-95250"</c> = 10px left of column A) silently snapped
/// back to x="0" on the very next save of that sheet, even when the picture itself was untouched. The fix
/// uses the signed pixel-&gt;EMU conversion (<see cref="Free.Shared.Drawing.DrawingMlCoordinateUnits.PixelsToEmuSigned"/>)
/// for xdr:pos specifically, while xdr:ext (a size) and oneCell/twoCellAnchor colOff/rowOff (which are
/// genuinely non-negative in the schema) keep clamping.
/// </summary>
public sealed class R74_UnitsMismatchSweepAbsoluteAnchorNegativePosTests
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

    [Fact]
    public void NegativeAbsoluteAnchorPosX_SurvivesAnUnrelatedEditOnTheSameSheet_AndPositivePosStillRoundTrips()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("NegativeAbsoluteAnchorPos");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "AbsPic", 2);
        AddPicture(sheet, "NormalPic", 6);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        // Convert "AbsPic" into an absoluteAnchor positioned 10px to the left of (and level with) the
        // sheet origin -- a legitimate negative EMU position ("xdr:pos x=-95250 y=0") that Excel itself
        // can produce for a picture dragged partially off the left edge.
        RewriteDrawingXml(initialSave, drawingXml =>
        {
            var root = drawingXml.Root!;
            var absAnchorSource = root.Elements(Xdr + "oneCellAnchor")
                .Single(anchor => anchor.Descendants(Xdr + "cNvPr").Any(c => c.Attribute("name")?.Value == "AbsPic"));
            var pic = absAnchorSource.Element(Xdr + "pic")!;
            pic.Remove();
            absAnchorSource.Remove();

            root.Add(new XElement(Xdr + "absoluteAnchor",
                new XElement(Xdr + "pos", new XAttribute("x", -95250), new XAttribute("y", 0)),
                new XElement(Xdr + "ext", new XAttribute("cx", 914400), new XAttribute("cy", 609600)),
                pic,
                new XElement(Xdr + "clientData")));
        });

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var reloadedSheet = reloaded.GetSheet("Sheet1")!;

        var absPic = reloadedSheet.Pictures.Single(picture => picture.Name == "AbsPic");
        absPic.IsSourceLoaded.Should().BeTrue();
        absPic.AnchorOffsetX.Should().BeApproximately(-10, 0.01,
            "the loader must preserve the negative EMU position as a negative pixel offset, not clamp it on read");

        // An UNRELATED edit on the same sheet (resizing the picture's height, and the sibling picture's
        // width) -- AbsPic's AnchorOffsetX/Y are left exactly as loaded (still negative/zero).
        absPic.Height = 200;
        var normalPic = reloadedSheet.Pictures.Single(picture => picture.Name == "NormalPic");
        normalPic.Width = 321;

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        var savedDrawingXml = ReadDrawingXml(secondSave);
        var savedAbsoluteAnchor = savedDrawingXml.Descendants(Xdr + "absoluteAnchor").Single();
        var pos = savedAbsoluteAnchor.Element(Xdr + "pos")!;
        var ext = savedAbsoluteAnchor.Element(Xdr + "ext")!;

        pos.Attribute("x")!.Value.Should().Be("-95250",
            "a legitimately-negative absoluteAnchor pos x must not be clamped to 0 on an unrelated save");
        pos.Attribute("y")!.Value.Should().Be("0");
        ext.Attribute("cy")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(200).ToString(),
            "the unrelated height edit must still be written to xdr:ext as before");
    }

    [Fact]
    public void PositiveAbsoluteAnchorPos_StillRoundTripsThroughTheSignedConversion()
    {
        // No-regression sibling: a positive absoluteAnchor pos must still write and round-trip
        // correctly now that xdr:pos x/y goes through the signed conversion instead of the clamped one.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("PositiveAbsoluteAnchorPos");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "AbsPic", 2);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

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
                new XElement(Xdr + "ext", new XAttribute("cx", 914400), new XAttribute("cy", 609600)),
                pic,
                new XElement(Xdr + "clientData")));
        });

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var absPic = reloaded.GetSheet("Sheet1")!.Pictures.Single(picture => picture.Name == "AbsPic");
        absPic.AnchorOffsetX = 50;
        absPic.AnchorOffsetY = 25;
        absPic.Height = 300; // also touch a modeled property so the save's fingerprint check sees a change.

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        var pos = ReadDrawingXml(secondSave).Descendants(Xdr + "absoluteAnchor").Single().Element(Xdr + "pos")!;
        pos.Attribute("x")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(50).ToString());
        pos.Attribute("y")!.Value.Should().Be(DrawingMlCoordinateUnits.PixelsToEmu(25).ToString());
    }

    [Fact]
    public void OneCellAnchorColOffRowOff_StillClampsToNonNegative_WhenModelOffsetIsNegative()
    {
        // No-regression sibling: colOff/rowOff are genuinely non-negative in the OOXML schema (unlike
        // absoluteAnchor's pos), so an out-of-range negative model offset must still clamp to 0, exactly
        // as before this fix -- only the absoluteAnchor xdr:pos path gained signed handling.
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("OneCellAnchorClampRegression");
        var sheet = workbook.AddSheet("Sheet1");
        AddPicture(sheet, "Pic", 2);

        using var initialSave = new MemoryStream();
        adapter.Save(workbook, initialSave);

        initialSave.Position = 0;
        var reloaded = adapter.Load(initialSave);
        var pic = reloaded.GetSheet("Sheet1")!.Pictures.Single();
        pic.AnchorOffsetX = -50;
        pic.AnchorOffsetY = -25;

        using var secondSave = new MemoryStream();
        adapter.Save(reloaded, secondSave);

        var savedAnchor = ReadDrawingXml(secondSave).Root!.Elements(Xdr + "oneCellAnchor").Single();
        var from = savedAnchor.Element(Xdr + "from")!;
        from.Element(Xdr + "colOff")!.Value.Should().Be("0",
            "a negative oneCellAnchor colOff must still clamp to 0, unlike absoluteAnchor's signed pos");
        from.Element(Xdr + "rowOff")!.Value.Should().Be("0",
            "a negative oneCellAnchor rowOff must still clamp to 0, unlike absoluteAnchor's signed pos");
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
