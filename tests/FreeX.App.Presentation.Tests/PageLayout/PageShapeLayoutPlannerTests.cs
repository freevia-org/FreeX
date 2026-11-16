using FluentAssertions;
using Free.Shared.Drawing;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PageShapeLayoutPlannerTests
{
    [Fact]
    public void Build_ResolvesGeometryPaintAndTransform_AndFiltersUnprintableShapes()
    {
        var sheet = new Workbook("test").AddSheet("Sheet1");
        var shape = new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 4), Kind = DrawingShapeKind.Triangle,
            Width = 80, Height = 40, AnchorOffsetX = 6, AnchorOffsetY = 4,
            RotationDegrees = 30, FlipHorizontal = true, ShapeText = "Label",
            FillColor = new CellColor(100, 120, 140)
        };
        var nonprinting = new DrawingShapeModel { Anchor = shape.Anchor, PrintObject = false };
        var hidden = new DrawingShapeModel { Anchor = shape.Anchor, IsVisible = false };
        var offPage = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 8, 8) };
        var result = PageShapeLayoutPlanner.Build([shape, nonprinting, hidden, offPage], sheet, WorkbookTheme.Office,
            [2, 3], [3, 4], 10, 20, new PrintGridMeasurement(0, 0, 60, 18), 0.5);
        var block = result.Should().ContainSingle().Subject;
        block.Bounds.Left.Should().BeGreaterThan(10);
        block.Bounds.Top.Should().BeGreaterThan(20);
        block.Bounds.Width.Should().BeGreaterThan(0);
        block.Bounds.Height.Should().BeGreaterThan(0);
        block.Geometry.Contours.Should().NotBeEmpty();
        block.Appearance.Transform.RotationDegrees.Should().Be(30);
        block.Appearance.Transform.FlipHorizontal.Should().BeTrue();
        block.Appearance.Paint.Fill.Should().Be(new CellColor(100, 120, 140));
        block.Text.Should().Be("Label");
    }

    [Fact]
    public void Build_IncludesShapesSpanningPagesAndWithHiddenAnchors()
    {
        var sheet = new Workbook("test").AddSheet("Sheet1");
        var spanning = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = 200, Height = 100 };
        var measurement = new PrintGridMeasurement(0, 0, 64, 20);
        var secondPage = PageShapeLayoutPlanner.Build([spanning], sheet, WorkbookTheme.Office,
            [2], [2], 0, 0, measurement);
        secondPage.Should().ContainSingle();
        secondPage[0].Bounds.Left.Should().BeLessThan(0);
        secondPage[0].Bounds.Top.Should().BeLessThan(0);
        sheet.HiddenRows.Add(1);
        sheet.HiddenCols.Add(1);
        var hiddenAnchor = PageShapeLayoutPlanner.Build([spanning], sheet, WorkbookTheme.Office,
            [2], [2], 0, 0, measurement);
        hiddenAnchor.Should().ContainSingle();
        hiddenAnchor[0].Bounds.Left.Should().Be(0);
        hiddenAnchor[0].Bounds.Top.Should().Be(0);
    }

    [Fact]
    public void Build_RejectsNonFiniteGeometryAndScale()
    {
        var sheet = new Workbook("test").AddSheet("Sheet1");
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Width = double.PositiveInfinity };
        var measurement = new PrintGridMeasurement(0, 0, 64, 20);
        PageShapeLayoutPlanner.Build([shape], sheet, WorkbookTheme.Office, [1], [1], 0, 0, measurement).Should().BeEmpty();
        shape.Width = 10;
        PageShapeLayoutPlanner.Build([shape], sheet, WorkbookTheme.Office, [1], [1], 0, 0, measurement, double.PositiveInfinity).Should().BeEmpty();
    }

    [Fact]
    public void Build_ClipsDrawingToBodyRatherThanRepeatedTitleBands()
    {
        var sheet = new Workbook("test").AddSheet("Sheet1");
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 2, 2), Width = 100, Height = 40 };
        var result = PageShapeLayoutPlanner.Build([shape], sheet, WorkbookTheme.Office, [1, 2], [1, 2], 10, 20,
            new PrintGridMeasurement(0, 0, 64, 20), bodyRows: [2], bodyColumns: [2]);
        result.Should().ContainSingle().Which.BodyClip.Should().Be(new LayoutRect(74, 40, 64, 20));
    }

    [Fact]
    public void Build_UsesRawDipOffsetsAndExtentsAfterNonuniformColumnAnchoring()
    {
        var sheet = new Workbook("test").AddSheet("Sheet1");
        sheet.ColumnWidths[1] = 10;
        sheet.ColumnWidths[2] = 25;
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 3), AnchorOffsetX = 11, AnchorOffsetY = 7, Width = 80, Height = 40 };
        var result = PageShapeLayoutPlanner.Build([shape], sheet, WorkbookTheme.Office, [1, 2, 3], [1, 2, 3, 4], 0, 0,
            new PrintGridMeasurement(0, 0, 150, 30), 0.5);
        var bounds = result.Should().ContainSingle().Subject.Bounds;
        var expectedAnchor = ChartAnchorGeometry.ConvertColumnOffsetToGridSpace(sheet, ChartAnchorGeometry.SumColumnPixels(sheet, 1, 2));
        bounds.Left.Should().BeApproximately((expectedAnchor + 11) * 0.5, 0.001);
        bounds.Top.Should().BeApproximately(3.5, 0.001);
        bounds.Width.Should().Be(40);
        bounds.Height.Should().Be(20);
    }

    [Fact]
    public void Build_CarriesArrowheadsAndBlackAndWhitePolicy()
    {
        var sheet = new Workbook("test").AddSheet("Sheet1");
        sheet.PrintBlackAndWhite = true;
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Kind = DrawingShapeKind.Line,
            Width = 80, Height = 20, HeadArrowhead = new DrawingArrowhead(DrawingArrowheadType.Oval),
            TailArrowhead = new DrawingArrowhead(DrawingArrowheadType.Triangle) };
        var block = PageShapeLayoutPlanner.Build([shape], sheet, WorkbookTheme.Office, [1, 2], [1, 2], 0, 0,
            new PrintGridMeasurement(0, 0, 64, 20)).Should().ContainSingle().Subject;
        block.Arrowheads.Should().HaveCount(2);
        block.Arrowheads.Should().OnlyContain(arrow => arrow.Contours.Count > 0);
        block.BlackAndWhite.Should().BeTrue();
    }

    [Fact]
    public void Build_ResolvesExplicitTextInsetsAndKeepsMissingSides()
    {
        var sheet = new Workbook("test").AddSheet("Sheet1");
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), ShapeText = "text",
            ShapeTextInsets = new DrawingShapeTextInsets(0, 19050, null, 38100) };
        var block = PageShapeLayoutPlanner.Build([shape], sheet, WorkbookTheme.Office, [1], [1], 0, 0,
            new PrintGridMeasurement(0, 0, 100, 50), 0.5).Should().ContainSingle().Subject;
        block.TextInsets.Should().Be(new PageShapeTextInsets(0, 1, null, 2));
    }

    [Fact]
    public void Build_ArrowheadsScaleWithPageRatherThanClampingAtOnePixel()
    {
        var sheet = new Workbook("test").AddSheet("Sheet1");
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1), Kind = DrawingShapeKind.Line,
            Width = 80, Height = 0, TailArrowhead = new DrawingArrowhead(DrawingArrowheadType.Triangle) };
        var measurement = new PrintGridMeasurement(0, 0, 100, 50);
        var full = PageShapeLayoutPlanner.Build([shape], sheet, WorkbookTheme.Office, [1], [1], 0, 0, measurement).Single();
        var half = PageShapeLayoutPlanner.Build([shape], sheet, WorkbookTheme.Office, [1], [1], 0, 0, measurement, 0.5).Single();
        var fullBase = full.Arrowheads.Single().Contours.Single().Segments[0].End;
        var halfBase = half.Arrowheads.Single().Contours.Single().Segments[0].End;
        halfBase.X.Should().BeApproximately(fullBase.X * 0.5, 0.001);
        halfBase.Y.Should().BeApproximately(fullBase.Y * 0.5, 0.001);
    }

    [Fact]
    public void Build_UniformGridHandlesLastWorksheetRow()
    {
        var sheet = new Workbook("test").AddSheet("Sheet1");
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, CellAddress.MaxRow, 1), Width = 20, Height = 10 };
        var block = PageShapeLayoutPlanner.Build([shape], sheet, WorkbookTheme.Office, [CellAddress.MaxRow], [1], 0, 0,
            new PrintGridMeasurement(0, 0, 64, 20), uniformGrid: true).Should().ContainSingle().Subject;
        block.Bounds.Top.Should().Be(0);
    }
}
