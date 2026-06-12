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
    [InlineData(DrawingShapeEffectPreset.Reflection)]
    [InlineData(DrawingShapeEffectPreset.Glow)]
    [InlineData(DrawingShapeEffectPreset.SoftEdges)]
    [InlineData(DrawingShapeEffectPreset.Bevel)]
    [InlineData(DrawingShapeEffectPreset.ThreeDRotation)]
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
    [InlineData(DrawingShapeEffectPreset.Reflection, "reflection")]
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
            var drawingXml = LoadDrawingXml(archive);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            drawingXml.Descendants(a + effectElementName).Should().ContainSingle();
        }

        stream.Position = 0;
        var loadedShape = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.EffectPreset.Should().Be(effectPreset);
        loadedShape.HasShadowEffect.Should().BeFalse();
        loadedShape.GetEffectiveEffectPreset().Should().Be(effectPreset);
    }

    [Fact]
    public void XlsxAdapter_RoundTripsDrawingShapeBevelPresetAsShape3D()
    {
        var workbook = CreateWorkbookWithShape(DrawingShapeEffectPreset.Bevel);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = LoadDrawingXml(archive);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            drawingXml.Descendants(a + "effectLst").Should().BeEmpty("bevel is stored as DrawingML 3D formatting");
            drawingXml.Descendants(a + "sp3d").Should().ContainSingle();
            var bevel = drawingXml.Descendants(a + "bevelT").Should().ContainSingle().Subject;
            bevel.Attribute("w")!.Value.Should().Be("76200");
            bevel.Attribute("h")!.Value.Should().Be("25400");
        }

        stream.Position = 0;
        var loadedShape = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.EffectPreset.Should().Be(DrawingShapeEffectPreset.Bevel);
        loadedShape.HasShadowEffect.Should().BeFalse();
        loadedShape.GetEffectiveEffectPreset().Should().Be(DrawingShapeEffectPreset.Bevel);
    }

    [Fact]
    public void XlsxAdapter_RoundTripsDrawingShapeThreeDRotationPresetAsScene3D()
    {
        var workbook = CreateWorkbookWithShape(DrawingShapeEffectPreset.ThreeDRotation);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = LoadDrawingXml(archive);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            drawingXml.Descendants(a + "effectLst").Should().BeEmpty("3-D rotation is stored as DrawingML scene 3D");
            var scene = drawingXml.Descendants(a + "scene3d").Should().ContainSingle().Subject;
            scene.Element(a + "camera")!.Attribute("prst")!.Value.Should().Be("isometricOffAxis1Left");
            scene.Element(a + "lightRig").Should().NotBeNull();
            var lightRig = scene.Element(a + "lightRig")!;
            lightRig.Attribute("rig")!.Value.Should().Be("threePt");
            lightRig.Attribute("dir")!.Value.Should().Be("t");
        }

        stream.Position = 0;
        var loadedShape = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.EffectPreset.Should().Be(DrawingShapeEffectPreset.ThreeDRotation);
        loadedShape.HasShadowEffect.Should().BeFalse();
        loadedShape.GetEffectiveEffectPreset().Should().Be(DrawingShapeEffectPreset.ThreeDRotation);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("1", true)]
    public void XlsxDrawingPartReader_ReadsShapeThemeEffectStyleOptIn(string effectRefIndex, bool expected)
    {
        var drawingXml = XDocument.Parse($$"""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:oneCellAnchor>
                <xdr:from>
                  <xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff>
                  <xdr:row>0</xdr:row><xdr:rowOff>0</xdr:rowOff>
                </xdr:from>
                <xdr:ext cx="914400" cy="457200"/>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="2" name="Rectangle 1"/>
                    <xdr:cNvSpPr/>
                  </xdr:nvSpPr>
                  <xdr:style>
                    <a:lnRef idx="0"/>
                    <a:fillRef idx="0"/>
                    <a:effectRef idx="{{effectRefIndex}}"/>
                    <a:fontRef idx="minor"/>
                  </xdr:style>
                  <xdr:spPr>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                    <a:solidFill><a:srgbClr val="5B9BD5"/></a:solidFill>
                    <a:ln><a:solidFill><a:srgbClr val="2F5597"/></a:solidFill></a:ln>
                  </xdr:spPr>
                </xdr:sp>
                <xdr:clientData/>
              </xdr:oneCellAnchor>
            </xdr:wsDr>
            """);

        var shape = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml)
            .Shapes
            .Should()
            .ContainSingle()
            .Subject;

        shape.UsesThemeEffects.Should().Be(expected);
        shape.EffectPreset.Should().Be(DrawingShapeEffectPreset.None);
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
            var drawingXml = LoadDrawingXml(archive);
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

    [Fact]
    public void NativeJsonAdapter_RoundTripsDrawingObjectNoFillState()
    {
        var workbook = CreateWorkbookWithNoFillObjects();
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loadedSheet = adapter.Load(stream).GetSheetAt(0);
        var loadedShape = loadedSheet.DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.HasFill.Should().BeFalse();
        loadedShape.FillColor.Should().BeNull();
        loadedShape.GradientFillEndColor.Should().BeNull();

        var loadedTextBox = loadedSheet.TextBoxes.Should().ContainSingle().Subject;
        loadedTextBox.HasFill.Should().BeFalse();
        loadedTextBox.FillColor.Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_RoundTripsDrawingObjectNoFillState()
    {
        var workbook = CreateWorkbookWithNoFillObjects();
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = LoadDrawingXml(archive);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            drawingXml.Descendants(a + "noFill").Should().HaveCount(2);
        }

        stream.Position = 0;
        var loadedSheet = adapter.Load(stream).GetSheetAt(0);
        loadedSheet.DrawingShapes.Should().ContainSingle().Which.HasFill.Should().BeFalse();
        loadedSheet.TextBoxes.Should().ContainSingle().Which.HasFill.Should().BeFalse();
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

    private static Workbook CreateWorkbookWithNoFillObjects()
    {
        var workbook = new Workbook("No Fill");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 120,
            Height = 70,
            HasFill = false,
            FillColor = null,
            OutlineColor = new CellColor(30, 40, 50)
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 2),
            Text = "Note",
            Width = 160,
            Height = 60,
            HasFill = false,
            FillColor = null,
            OutlineColor = new CellColor(70, 80, 90)
        });

        return workbook;
    }

    private static XDocument LoadDrawingXml(ZipArchive archive) =>
        XlsxPackageTestFixtures.LoadPackageXml(
            archive,
            "xl/drawings/drawing1.xml",
            "the XLSX package should contain xl/drawings/drawing1.xml");

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
