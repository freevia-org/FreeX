using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class DrawingShapeEffectMetadataPersistenceTests
{
    [Theory]
    [InlineData(DrawingShapeEffectPreset.Shadow)]
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

    [Fact]
    public void XlsxAdapter_RoundTripsDrawingShapeGlowEffectPreset()
    {
        var workbook = CreateWorkbookWithShape(DrawingShapeEffectPreset.Glow);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loadedShape = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.EffectPreset.Should().Be(DrawingShapeEffectPreset.Glow);
        loadedShape.HasShadowEffect.Should().BeFalse();
        loadedShape.GetEffectiveEffectPreset().Should().Be(DrawingShapeEffectPreset.Glow);
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
}
