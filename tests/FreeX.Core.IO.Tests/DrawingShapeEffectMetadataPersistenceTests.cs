using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class DrawingShapeEffectMetadataPersistenceTests
{
    [Theory]
    [InlineData(DrawingShapeEffectPreset.Shadow)]
    [InlineData(DrawingShapeEffectPreset.InnerShadow)]
    [InlineData(DrawingShapeEffectPreset.Glow)]
    [InlineData(DrawingShapeEffectPreset.SoftEdges)]
    public void NativeJsonAdapter_RoundTripsDrawingShapeEffectPreset(DrawingShapeEffectPreset effectPreset)
    {
        var workbook = CreateWorkbookWithShape(effectPreset);
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loadedShape = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.EffectPreset.Should().Be(effectPreset);
        loadedShape.HasShadowEffect.Should().Be(effectPreset == DrawingShapeEffectPreset.Shadow);
        loadedShape.GetEffectiveEffectPreset().Should().Be(effectPreset);
    }

    [Theory]
    [InlineData(DrawingShapeEffectPreset.InnerShadow, "innerShdw")]
    [InlineData(DrawingShapeEffectPreset.Glow, "glow")]
    public void XlsxAdapter_RoundTripsDrawingShapeEffectPreset(DrawingShapeEffectPreset effectPreset, string effectElementName)
    {
        var workbook = CreateWorkbookWithShape(effectPreset);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = XDocument.Load(archive.GetEntry("xl/drawings/drawing1.xml")!.Open());
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            drawingXml.Descendants(a + effectElementName).Should().ContainSingle();
        }

        stream.Position = 0;
        var loadedShape = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.EffectPreset.Should().Be(effectPreset);
        loadedShape.HasShadowEffect.Should().BeFalse();
        loadedShape.GetEffectiveEffectPreset().Should().Be(effectPreset);
    }

    [Theory]
    [InlineData(DrawingShapeGradientDirection.Horizontal)]
    [InlineData(DrawingShapeGradientDirection.Vertical)]
    [InlineData(DrawingShapeGradientDirection.DiagonalUp)]
    public void NativeJsonAdapter_RoundTripsDrawingShapeGradientDirection(DrawingShapeGradientDirection direction)
    {
        var workbook = CreateWorkbookWithGradientShape(direction);
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loadedShape = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.GradientFillEndColor.Should().Be(new CellColor(240, 245, 250));
        loadedShape.GradientFillDirection.Should().Be(direction);
        loadedShape.GetEffectiveGradientFillDirection().Should().Be(direction);
    }

    [Fact]
    public void XlsxAdapter_RoundTripsDrawingShapeVerticalGradientDirection()
    {
        var workbook = CreateWorkbookWithGradientShape(DrawingShapeGradientDirection.Vertical);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = XDocument.Load(archive.GetEntry("xl/drawings/drawing1.xml")!.Open());
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var gradientLine = drawingXml.Descendants(a + "lin").Should().ContainSingle().Subject;
            gradientLine.Attribute("ang")!.Value.Should().Be("16200000");
        }

        stream.Position = 0;
        var loadedShape = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.GradientFillEndColor.Should().Be(new CellColor(240, 245, 250));
        loadedShape.GradientFillDirection.Should().Be(DrawingShapeGradientDirection.Vertical);
        loadedShape.GetEffectiveGradientFillDirection().Should().Be(DrawingShapeGradientDirection.Vertical);
    }

    private static Workbook CreateWorkbookWithShape(DrawingShapeEffectPreset effectPreset)
    {
        var workbook = new Workbook("Effects");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 120,
            Height = 70,
            FillColor = new CellColor(200, 210, 220),
            OutlineColor = new CellColor(30, 40, 50),
            EffectPreset = effectPreset,
            HasShadowEffect = effectPreset == DrawingShapeEffectPreset.Shadow
        });

        return workbook;
    }

    private static Workbook CreateWorkbookWithGradientShape(DrawingShapeGradientDirection direction)
    {
        var workbook = new Workbook("Gradients");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 120,
            Height = 70,
            FillColor = new CellColor(200, 210, 220),
            OutlineColor = new CellColor(30, 40, 50),
            GradientFillEndColor = new CellColor(240, 245, 250),
            GradientFillDirection = direction
        });

        return workbook;
    }
}
