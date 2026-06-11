using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void WorksheetDrawingObjects_ProducesSchemaValidWorkbook()
    {
        using var stream = Save(CreateWorksheetDrawingObjectsSourceWorkbook());

        SchemaErrors(stream).Should().BeEmpty();
        AssertWorksheetDrawingObjectsAuthored(stream);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithWorksheetDrawingObjects_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateWorksheetDrawingObjectsSourceWorkbook());
        var sourceWorksheetDrawing = ReadWorksheetChildElement(source, "drawing");
        var sourceWorksheetRelationships = ReadPackageRootElement(source, "xl/worksheets/_rels/sheet1.xml.rels");
        var sourceDrawing = ReadPackageRootElement(source, "xl/drawings/drawing1.xml");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        AssertWorksheetDrawingObjectsModel(sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetChildElement(saved, "drawing")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetDrawing.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/worksheets/_rels/sheet1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceWorksheetRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "xl/drawings/drawing1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceDrawing.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        AssertWorksheetDrawingObjectsModel(adapter.Load(saved).GetSheetAt(0));
    }

    [Fact]
    public void WorksheetDrawingShapes_RoundTripExpandedPresetKinds()
    {
        var workbook = new Workbook("ExpandedShapeKinds");
        var sheet = workbook.AddSheet("Shapes");
        var cases = new (DrawingShapeKind Kind, string Preset)[]
        {
            (DrawingShapeKind.RoundedRectangle, "roundRect"),
            (DrawingShapeKind.ElbowConnector, "bentConnector2"),
            (DrawingShapeKind.RightArrow, "rightArrow"),
            (DrawingShapeKind.NotEqualSign, "mathNotEqual"),
            (DrawingShapeKind.FlowchartDecision, "flowChartDecision"),
            (DrawingShapeKind.Star5, "star5"),
            (DrawingShapeKind.OvalCallout, "wedgeEllipseCallout")
        };

        for (var i = 0; i < cases.Length; i++)
        {
            sheet.DrawingShapes.Add(new DrawingShapeModel
            {
                Name = cases[i].Kind.ToString(),
                Anchor = new CellAddress(sheet.Id, (uint)(i + 1), 1),
                Kind = cases[i].Kind,
                Width = 96,
                Height = 56
            });
        }

        using var stream = Save(workbook);
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        ReadPackageRootElement(stream, "xl/drawings/drawing1.xml")
            .Descendants(drawingNs + "prstGeom")
            .Select(element => element.Attribute("prst")?.Value)
            .Should()
            .Contain(cases.Select(item => item.Preset));

        stream.Position = 0;
        var loadedKinds = new XlsxFileAdapter()
            .Load(stream)
            .GetSheetAt(0)
            .DrawingShapes
            .Select(shape => shape.Kind);

        loadedKinds.Should().Equal(cases.Select(item => item.Kind));
    }

    [Fact]
    public void WorksheetDrawingShapes_WriteConcreteDefaultFillAndOutline()
    {
        var workbook = new Workbook("DefaultShapeStyle");
        var sheet = workbook.AddSheet("Shapes");
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = DrawingShapeKind.Rectangle,
            FillColor = DrawingShapeModel.DefaultFillColor,
            OutlineColor = DrawingShapeModel.DefaultOutlineColor
        });

        using var stream = Save(workbook);
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var shapeProperties = ReadPackageRootElement(stream, "xl/drawings/drawing1.xml")
            .Descendants(spreadsheetDrawingNs + "spPr")
            .Should()
            .ContainSingle()
            .Subject;

        shapeProperties.Element(drawingNs + "solidFill")!
            .Element(drawingNs + "srgbClr")!
            .Attribute("val")!
            .Value
            .Should()
            .Be("5B9BD5");
        shapeProperties.Element(drawingNs + "ln")!
            .Element(drawingNs + "solidFill")!
            .Element(drawingNs + "srgbClr")!
            .Attribute("val")!
            .Value
            .Should()
            .Be("2F5597");
    }

    private static Workbook CreateWorksheetDrawingObjectsSourceWorkbook()
    {
        var workbook = new Workbook("WorksheetDrawingObjectsPatchSave");
        var sheet = workbook.AddSheet("Data");
        SeedNumericGrid(sheet);

        var textBox = new TextBoxModel
        {
            Name = "Review Note",
            Anchor = new CellAddress(sheet.Id, 2, 4),
            Text = "Review total before close",
            Title = "Review note",
            AltText = "Text box used for workbook review notes",
            Width = 210,
            Height = 70,
            RotationDegrees = 5,
            FillColor = new CellColor(255, 242, 204),
            OutlineColor = new CellColor(191, 144, 0)
        };
        var shape = new DrawingShapeModel
        {
            Name = "Variance Flag",
            Anchor = new CellAddress(sheet.Id, 4, 4),
            Kind = DrawingShapeKind.Ellipse,
            Title = "Variance flag",
            AltText = "Ellipse highlighting a variance",
            Width = 120,
            Height = 80,
            RotationDegrees = 15,
            FillColor = new CellColor(189, 215, 238),
            GradientFillEndColor = new CellColor(221, 235, 247),
            GradientFillDirection = DrawingShapeGradientDirection.Vertical,
            OutlineColor = new CellColor(31, 78, 121),
            EffectPreset = DrawingShapeEffectPreset.Glow
        };

        sheet.TextBoxes.Add(textBox);
        sheet.DrawingShapes.Add(shape);
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id));
        return workbook;
    }

    private static void AssertWorksheetDrawingObjectsAuthored(Stream stream)
    {
        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var drawing = ReadPackageRootElement(stream, "xl/drawings/drawing1.xml");
        drawing.Elements(spreadsheetDrawingNs + "oneCellAnchor").Should().HaveCount(2);
        drawing.Descendants(spreadsheetDrawingNs + "txBody")
            .Should()
            .ContainSingle()
            .Which
            .Descendants(drawingNs + "t")
            .Should()
            .ContainSingle()
            .Which
            .Value
            .Should()
            .Be("Review total before close");
        drawing.Descendants(drawingNs + "prstGeom")
            .Select(element => element.Attribute("prst")?.Value)
            .Should()
            .Contain("ellipse");
        drawing.Descendants(drawingNs + "glow").Should().ContainSingle();
        drawing.Descendants(drawingNs + "lin")
            .Should()
            .ContainSingle()
            .Which
            .Attribute("ang")!
            .Value
            .Should()
            .Be("16200000");
    }

    private static void AssertWorksheetDrawingObjectsModel(Sheet sheet)
    {
        var textBox = sheet.TextBoxes.Should().ContainSingle().Subject;
        textBox.Name.Should().Be("Review Note");
        textBox.Anchor.Should().Be(new CellAddress(sheet.Id, 2, 4));
        textBox.Text.Should().Be("Review total before close");
        textBox.Title.Should().Be("Review note");
        textBox.AltText.Should().Be("Text box used for workbook review notes");
        textBox.Width.Should().Be(210);
        textBox.Height.Should().Be(70);
        textBox.RotationDegrees.Should().Be(5);
        textBox.FillColor.Should().Be(new CellColor(255, 242, 204));
        textBox.OutlineColor.Should().Be(new CellColor(191, 144, 0));

        var shape = sheet.DrawingShapes.Should().ContainSingle().Subject;
        shape.Name.Should().Be("Variance Flag");
        shape.Anchor.Should().Be(new CellAddress(sheet.Id, 4, 4));
        shape.Kind.Should().Be(DrawingShapeKind.Ellipse);
        shape.Title.Should().Be("Variance flag");
        shape.AltText.Should().Be("Ellipse highlighting a variance");
        shape.Width.Should().Be(120);
        shape.Height.Should().Be(80);
        shape.RotationDegrees.Should().Be(15);
        shape.FillColor.Should().Be(new CellColor(189, 215, 238));
        shape.GradientFillEndColor.Should().Be(new CellColor(221, 235, 247));
        shape.GradientFillDirection.Should().Be(DrawingShapeGradientDirection.Vertical);
        shape.OutlineColor.Should().Be(new CellColor(31, 78, 121));
        shape.EffectPreset.Should().Be(DrawingShapeEffectPreset.Glow);
    }
}
