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
}
