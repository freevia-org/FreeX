using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R78-io-shape-geometry-5-3: a preset shape's customized adjust-handle values (<c>&lt;a:avLst&gt;</c>
/// <c>&lt;a:gd&gt;</c> children -- e.g. a round-rect's dragged corner-radius handle) must survive a save
/// via <see cref="DrawingShapeModel.AdjustValues"/> instead of always being written as an empty
/// <c>&lt;a:avLst/&gt;</c>, which silently resets the handle to the preset's built-in default.
/// </summary>
public sealed class R78_ShapeAdjustValueRoundTripTests
{
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void XlsxAdapter_WritesCustomAdjustValue_AsGdInAvLst()
    {
        var workbook = CreateWorkbookWithShape(
            [new DrawingShapeAdjustValue("adj", "val 8000")]);
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = LoadDrawingXml(archive);

        var avLst = drawingXml.Descendants(DrawingNs + "prstGeom").Should().ContainSingle().Subject
            .Element(DrawingNs + "avLst");
        avLst.Should().NotBeNull();
        var gd = avLst!.Elements(DrawingNs + "gd").Should().ContainSingle().Subject;
        gd.Attribute("name")!.Value.Should().Be("adj");
        gd.Attribute("fmla")!.Value.Should().Be("val 8000");
    }

    [Fact]
    public void XlsxAdapter_RoundTripsCustomAdjustValue_ThroughReload()
    {
        var workbook = CreateWorkbookWithShape(
            [new DrawingShapeAdjustValue("adj", "val 8000")]);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.AdjustValues.Should().NotBeNull();
        loaded.AdjustValues.Should().ContainSingle(v => v.Name == "adj" && v.Formula == "val 8000");
    }

    [Fact]
    public void XlsxAdapter_RoundTripsMultipleAdjustValues()
    {
        var workbook = CreateWorkbookWithShape(
        [
            new DrawingShapeAdjustValue("adj1", "val 12500"),
            new DrawingShapeAdjustValue("adj2", "val 45000")
        ]);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.AdjustValues.Should().NotBeNull();
        loaded.AdjustValues.Should().HaveCount(2);
        loaded.AdjustValues.Should().Contain(v => v.Name == "adj1" && v.Formula == "val 12500");
        loaded.AdjustValues.Should().Contain(v => v.Name == "adj2" && v.Formula == "val 45000");
    }

    /// <summary>No-regression sibling: a shape with no customized adjust values must still emit an
    /// empty <c>&lt;a:avLst/&gt;</c> (the preset's built-in defaults apply), matching prior behavior.</summary>
    [Fact]
    public void XlsxAdapter_NoAdjustValues_WritesEmptyAvLst()
    {
        var workbook = CreateWorkbookWithShape(adjustValues: null);
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = LoadDrawingXml(archive);

        var avLst = drawingXml.Descendants(DrawingNs + "prstGeom").Should().ContainSingle().Subject
            .Element(DrawingNs + "avLst");
        avLst.Should().NotBeNull();
        avLst!.Elements(DrawingNs + "gd").Should().BeEmpty();

        stream.Position = 0;
        var loaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.AdjustValues.Should().BeNull();
    }

    private static Workbook CreateWorkbookWithShape(IReadOnlyList<DrawingShapeAdjustValue>? adjustValues)
    {
        var workbook = new Workbook("AdjustValues");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.RoundedRectangle,
            Width = 200,
            Height = 100,
            HasFill = true,
            FillColor = new CellColor(91, 155, 213),
            AdjustValues = adjustValues
        });
        return workbook;
    }

    private static XDocument LoadDrawingXml(ZipArchive archive)
    {
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        entry.Should().NotBeNull("a drawing XML entry must be present");
        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }
}
