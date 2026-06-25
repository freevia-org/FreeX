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

    [Fact]
    public void XlsxDrawingPartReader_ResolvesTwoCellAnchorWhenFromAndToShareColumn()
    {
        // Regression: a shape narrower than one column has from/to in the SAME column, with the
        // sub-cell colOff EMU values expressing its width. The from/to row differs. Such an anchor is
        // valid and must NOT be dropped — dropping it snapped the object to A1 (over the sheet title).
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:twoCellAnchor>
                <xdr:from>
                  <xdr:col>3</xdr:col><xdr:colOff>438151</xdr:colOff>
                  <xdr:row>0</xdr:row><xdr:rowOff>228600</xdr:rowOff>
                </xdr:from>
                <xdr:to>
                  <xdr:col>3</xdr:col><xdr:colOff>1733551</xdr:colOff>
                  <xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff>
                </xdr:to>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="2" name="Rounded Rectangle 3"/>
                    <xdr:cNvSpPr/>
                  </xdr:nvSpPr>
                  <xdr:spPr>
                    <a:xfrm><a:off x="2266951" y="184150"/><a:ext cx="171450" cy="0"/></a:xfrm>
                    <a:prstGeom prst="roundRect"><a:avLst/></a:prstGeom>
                  </xdr:spPr>
                  <xdr:txBody><a:bodyPr/><a:p><a:r><a:t>Visit Chandoo.org</a:t></a:r></a:p></xdr:txBody>
                </xdr:sp>
                <xdr:clientData/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """);

        var textBox = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml)
            .TextBoxes
            .Should()
            .ContainSingle()
            .Subject;

        textBox.Anchor.Should().NotBeNull();
        textBox.Anchor!.Kind.Should().Be(ChartDrawingAnchorKind.TwoCell);
        textBox.Anchor.FromColumnZeroBased.Should().Be(3, "the box anchors to column D, not A");
        textBox.Anchor.FromRowZeroBased.Should().Be(0);

        // The from-cell sub-cell offsets are exposed in DIP pixels (EMU/9525) so the placement layer can
        // add them to the cell's left/top edge, keeping side-by-side objects within column D distinct.
        textBox.Anchor.FromColumnOffset.Should().BeApproximately(438151 / 9525.0, 0.001);
        textBox.Anchor.FromRowOffset.Should().BeApproximately(228600 / 9525.0, 0.001);
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

    // -----------------------------------------------------------------------
    // Outline width + dash round-trip tests
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1.5, DrawingShapeOutlineDash.Solid)]
    [InlineData(3.0, DrawingShapeOutlineDash.Dash)]
    [InlineData(0.5, DrawingShapeOutlineDash.Dot)]
    [InlineData(2.0, DrawingShapeOutlineDash.DashDot)]
    public void NativeJsonAdapter_RoundTripsOutlineWidthAndDash(double widthPt, DrawingShapeOutlineDash dash)
    {
        var workbook = CreateWorkbookWithOutlineShape(widthPt, dash, outlineHasNoFill: false);
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.OutlineWidthPoints.Should().BeApproximately(widthPt, 0.001);
        loaded.OutlineDash.Should().Be(dash);
        loaded.OutlineHasNoFill.Should().BeFalse();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTripsOutlineNoFill()
    {
        var workbook = CreateWorkbookWithOutlineShape(0, DrawingShapeOutlineDash.Solid, outlineHasNoFill: true);
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.OutlineHasNoFill.Should().BeTrue();
    }

    [Theory]
    [InlineData(1.5, DrawingShapeOutlineDash.Solid)]
    [InlineData(3.0, DrawingShapeOutlineDash.Dash)]
    [InlineData(0.75, DrawingShapeOutlineDash.Dot)]
    public void XlsxAdapter_RoundTripsOutlineWidthAndDash(double widthPt, DrawingShapeOutlineDash dash)
    {
        var workbook = CreateWorkbookWithOutlineShape(widthPt, dash, outlineHasNoFill: false);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        // Verify XML contains w attribute and prstDash when non-solid
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = LoadDrawingXml(archive);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var ln = drawingXml.Descendants(a + "ln").Should().ContainSingle().Subject;
            var expectedEmu = (long)Math.Round(widthPt * 12700);
            ln.Attribute("w")!.Value.Should().Be(expectedEmu.ToString());
            if (dash != DrawingShapeOutlineDash.Solid)
                drawingXml.Descendants(a + "prstDash").Should().ContainSingle();
        }

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.OutlineWidthPoints.Should().BeApproximately(widthPt, 0.01);
        loaded.OutlineDash.Should().Be(dash);
        loaded.OutlineHasNoFill.Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_RoundTripsOutlineNoFillAsLnNoFillElement()
    {
        var workbook = CreateWorkbookWithOutlineShape(0, DrawingShapeOutlineDash.Solid, outlineHasNoFill: true);
        using var stream = new MemoryStream();
        var adapter = new XlsxFileAdapter();

        adapter.Save(workbook, stream);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = LoadDrawingXml(archive);
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var ln = drawingXml.Descendants(a + "ln").Should().ContainSingle().Subject;
            ln.Element(a + "noFill").Should().NotBeNull("outline with noFill must be preserved");
        }

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        loaded.OutlineHasNoFill.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Pre-rotation dimension (xfrm ext) round-trip test
    // -----------------------------------------------------------------------

    [Fact]
    public void XlsxDrawingPartReader_ReadsPreRotationDimensionsFromXfrmExt()
    {
        // A twoCellAnchor shape where the anchor span is the bounding box, but the
        // <a:xfrm><a:ext> gives the pre-rotation shape size (100x60 px = 952500x571500 EMU).
        var drawingXml = XDocument.Parse("""
            <xdr:wsDr xmlns:xdr="http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <xdr:twoCellAnchor>
                <xdr:from>
                  <xdr:col>0</xdr:col><xdr:colOff>254000</xdr:colOff>
                  <xdr:row>3</xdr:row><xdr:rowOff>86360</xdr:rowOff>
                </xdr:from>
                <xdr:to>
                  <xdr:col>2</xdr:col><xdr:colOff>304800</xdr:colOff>
                  <xdr:row>7</xdr:row><xdr:rowOff>116840</xdr:rowOff>
                </xdr:to>
                <xdr:sp>
                  <xdr:nvSpPr>
                    <xdr:cNvPr id="2" name="Rot30"/>
                    <xdr:cNvSpPr/>
                  </xdr:nvSpPr>
                  <xdr:spPr>
                    <a:xfrm rot="1800000">
                      <a:off x="254000" y="635000"/>
                      <a:ext cx="952500" cy="571500"/>
                    </a:xfrm>
                    <a:prstGeom prst="rect"><a:avLst/></a:prstGeom>
                    <a:solidFill><a:srgbClr val="5B9BD5"/></a:solidFill>
                    <a:ln w="19050"><a:solidFill><a:srgbClr val="2F5597"/></a:solidFill></a:ln>
                  </xdr:spPr>
                </xdr:sp>
                <xdr:clientData/>
              </xdr:twoCellAnchor>
            </xdr:wsDr>
            """);

        var shapes = XlsxWorksheetDrawingPartReader.ReadShapeParts(drawingXml).Shapes;
        var shape = shapes.Should().ContainSingle().Subject;

        // Pre-rotation dimensions come from <a:xfrm><a:ext>
        shape.XfrmWidthPixels.Should().BeApproximately(952500 / 9525.0, 0.1,
            "width is taken from <a:xfrm><a:ext cx>");
        shape.XfrmHeightPixels.Should().BeApproximately(571500 / 9525.0, 0.1,
            "height is taken from <a:xfrm><a:ext cy>");

        // Outline width parsed from <a:ln w="19050">
        shape.OutlineWidthPoints.Should().BeApproximately(19050.0 / 12700.0, 0.001,
            "1.5 pt = 19050 EMU");
    }

    [Fact]
    public void XlsxAdapter_WritesXfrmExtForRotatedShape()
    {
        var workbook = new Workbook("XfrmExt");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 100,
            Height = 60,
            RotationDegrees = 30,
            FillColor = new CellColor(91, 155, 213),
            OutlineColor = new CellColor(47, 85, 151),
            OutlineWidthPoints = 1.5
        });

        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var drawingXml = LoadDrawingXml(archive);
        XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
        var xfrm = drawingXml.Descendants(a + "xfrm").Should().ContainSingle().Subject;
        xfrm.Attribute("rot")!.Value.Should().Be("1800000", "30° = 30 × 60000");
        var ext = xfrm.Element(a + "ext");
        ext.Should().NotBeNull("writer must include <a:ext cx cy> for pre-rotation size");
        ext!.Attribute("cx")!.Value.Should().Be("952500", "100 px × 9525 EMU/px");
        ext.Attribute("cy")!.Value.Should().Be("571500", "60 px × 9525 EMU/px");
    }

    private static Workbook CreateWorkbookWithOutlineShape(
        double outlineWidthPt,
        DrawingShapeOutlineDash outlineDash,
        bool outlineHasNoFill)
    {
        var workbook = new Workbook("Outline");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 120,
            Height = 70,
            FillColor = new CellColor(91, 155, 213),
            OutlineColor = outlineHasNoFill ? null : new CellColor(47, 85, 151),
            OutlineWidthPoints = outlineWidthPt,
            OutlineHasNoFill = outlineHasNoFill,
            OutlineDash = outlineDash
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
