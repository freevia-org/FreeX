using FluentAssertions;
using Free.Shared.Drawing;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ShapePdfExportTests
{
    [Fact]
    public void PageSetup_ExportsClippedTransformedShapeAndOmitsNonprintingShape()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 2, 2), Kind = DrawingShapeKind.Ellipse, Width = 100, Height = 40, RotationDegrees = 30, FlipHorizontal = true, ShapeText = "Shape label" });
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 2, 2), PrintObject = false, ShapeText = "Do not print" });
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.ActiveSheet, WorkbookExportPrintOutputKind.Pdf, ActiveSheetIndex: 0));
        printPlan.IsReady.Should().BeTrue();
        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, PortablePdfExportPlanner.CreatePlan(printPlan, workbook));
        var clip = document.Pages.Single().Ops.OfType<PdfClipGroup>().Should().ContainSingle().Subject;
        var transformed = clip.Ops.OfType<PdfRotationGroup>().First();
        transformed.RotationDegrees.Should().Be(30);
        transformed.FlipH.Should().BeTrue();
        transformed.Ops.OfType<PdfPath>().Should().ContainSingle().Which.Contours
            .SelectMany(contour => contour.Segments).Should().Contain(segment => segment.Kind == PdfPathSegmentKind.CubicBezier);
        var text = clip.Ops.OfType<PdfRotationGroup>().Last();
        text.FlipH.Should().BeFalse();
        text.Ops.OfType<PdfClipGroup>().Single().Ops.OfType<PdfText>().Should().ContainSingle().Which.Text.Should().Be("Shape label");
    }

    [Fact]
    public void PageSetup_DraftQualitySuppressesShape()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.PrintDraftQuality = true;
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) });
        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.ActiveSheet, WorkbookExportPrintOutputKind.Pdf, ActiveSheetIndex: 0));
        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, PortablePdfExportPlanner.CreatePlan(plan, workbook));
        document.Pages.Single().Ops.OfType<PdfClipGroup>().Should().BeEmpty();
    }

    [Fact]
    public void PageSetup_UsesDrawingOrderAndPreservesGradientAndDash()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1),
            GradientFillEndColor = new CellColor(10, 20, 30), OutlineDash = DrawingShapeOutlineDash.Dash };
        var text = new TextBoxModel { Anchor = shape.Anchor, Text = "First" };
        sheet.DrawingShapes.Add(shape);
        sheet.TextBoxes.Add(text);
        sheet.DrawingObjectZOrder.Add(new(SelectionPaneObjectKind.TextBox, text.Id));
        sheet.DrawingObjectZOrder.Add(new(SelectionPaneObjectKind.Shape, shape.Id));
        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.ActiveSheet, WorkbookExportPrintOutputKind.Pdf, ActiveSheetIndex: 0));
        var ops = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, PortablePdfExportPlanner.CreatePlan(plan, workbook)).Pages.Single().Ops.ToList();
        var textIndex = ops.FindIndex(op => op is PdfText item && item.Text == "First");
        var shapeIndex = ops.FindIndex(op => op is PdfClipGroup);
        textIndex.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(shapeIndex);
        var shapeClip = (PdfClipGroup)ops[shapeIndex];
        var path = shapeClip.Ops.OfType<PdfRotationGroup>().First().Ops.OfType<PdfPathLinearGradient>().Should().ContainSingle().Subject;
        path.FillGradient.Should().NotBeNull();
        path.StrokeDash.Should().NotBeNull();
    }

    [Fact]
    public void PageSetup_WrapsAndCentersShapeTextInsideClip()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 2, 2), Width = 85, Height = 110,
            ShapeText = "alpha beta gamma delta epsilon", ShapeTextHAlign = DrawingShapeTextHAlign.Center,
            ShapeTextVAnchor = DrawingShapeTextVAnchor.Middle, ShapeTextBold = true, ShapeTextWrap = true });
        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.ActiveSheet, WorkbookExportPrintOutputKind.Pdf, ActiveSheetIndex: 0));
        var ops = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, PortablePdfExportPlanner.CreatePlan(plan, workbook)).Pages.Single().Ops;
        var shapeClip = ops.OfType<PdfClipGroup>().Single();
        var textClip = shapeClip.Ops.OfType<PdfRotationGroup>().Last().Ops.OfType<PdfClipGroup>().Single();
        var lines = textClip.Ops.OfType<PdfText>().ToArray();
        lines.Length.Should().BeGreaterThan(1);
        lines.Should().OnlyContain(line => line.X > textClip.X && line.Y > textClip.Y && line.Y < textClip.Y + textClip.Height);
    }

    [Fact]
    public void LegacyPdf_EmitsShapeArrowheadsAndHonorsBlackAndWhite()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.PrintBlackAndWhite = true;
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Kind = DrawingShapeKind.Line,
            Width = 80, Height = 20, OutlineColor = new CellColor(255, 0, 0), TailArrowhead = new DrawingArrowhead(DrawingArrowheadType.Triangle) });
        var plan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.ActiveSheet, WorkbookExportPrintOutputKind.Pdf, ActiveSheetIndex: 0));
        var document = WorkbookPdfContentBuilder.Build(workbook, PortablePdfExportPlanner.CreatePlan(plan, workbook), new PortablePdfDocumentOptions());
        var group = document.Pages.Single().Ops.OfType<PdfClipGroup>().Single().Ops.OfType<PdfRotationGroup>().Single();
        var paths = group.Ops.OfType<PdfPath>().ToArray();
        paths.Should().HaveCountGreaterThan(1);
        paths.Should().Contain(path => path.FillColor == PdfColor.Black);
        paths.Should().Contain(path => path.StrokeColor == PdfColor.Black);
    }

    [Fact]
    public void LegacyPdf_PreflightsPrintableShapeUnicode()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), ShapeText = "\u732b" };
        sheet.DrawingShapes.Add(shape);
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.ActiveSheet, WorkbookExportPrintOutputKind.Pdf, ActiveSheetIndex: 0));
        var plan = PortablePdfExportPlanner.CreatePlan(printPlan, workbook);
        PortablePdfTextCapabilityPlanner.CreatePlan(workbook, plan).UnsupportedTextDiagnostics
            .Should().ContainSingle(item => item.Source == PortablePdfTextRunSource.Shape);
        shape.PrintObject = false;
        PortablePdfTextCapabilityPlanner.CreatePlan(workbook, plan).IsReady.Should().BeTrue();
    }

    [Fact]
    public void PageSetup_WrapsLongUnbrokenWords()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 55, Height = 120,
            ShapeText = "abcdefghijklmno", ShapeTextWrap = true });
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.ActiveSheet, WorkbookExportPrintOutputKind.Pdf, ActiveSheetIndex: 0));
        var ops = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, PortablePdfExportPlanner.CreatePlan(printPlan, workbook)).Pages.Single().Ops;
        var text = ops.OfType<PdfClipGroup>().Single().Ops.OfType<PdfRotationGroup>().Last().Ops.OfType<PdfClipGroup>().Single().Ops.OfType<PdfText>().ToArray();
        text.Length.Should().BeGreaterThan(1);
        string.Concat(text.Select(line => line.Text)).Should().Be("abcdefghijklmno");
    }

    [Fact]
    public void PageSetup_PreservesLongLineWithoutArbitraryTruncation()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A1"));
        sheet.PrintArea = GridRange.Parse("A1:AZ20", sheet.Id);
        var text = new string('W', 160);
        sheet.DrawingShapes.Add(new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 4000, Height = 80,
            ShapeText = text, ShapeTextWrap = false });
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.ActiveSheet, WorkbookExportPrintOutputKind.Pdf, ActiveSheetIndex: 0));
        var ops = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, PortablePdfExportPlanner.CreatePlan(printPlan, workbook)).Pages.First().Ops;
        var rendered = ops.OfType<PdfClipGroup>().Single().Ops.OfType<PdfRotationGroup>().Last()
            .Ops.OfType<PdfClipGroup>().Single().Ops.OfType<PdfText>().Should().ContainSingle().Subject;
        rendered.Text.Should().Be(text);
    }
}
