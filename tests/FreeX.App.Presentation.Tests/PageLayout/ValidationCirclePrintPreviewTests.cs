using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class ValidationCirclePrintPreviewTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void BuilderAndInstructionPlan_PreserveEmptyCircledCellAsEllipse()
    {
        var workbook = new Workbook("Preview");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));
        var circled = new CellAddress(sheet.Id, 1, 2);
        sheet.PrintArea = new GridRange(new CellAddress(sheet.Id, 1, 1), circled);
        sheet.ValidationCircleCells = [circled];
        var pagination = PagePaginationPlanner.Paginate(
            sheet.PrintArea.Value,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);

        var layout = PageContentRenderModelBuilder.Build(
            workbook,
            sheet,
            pagination,
            pageIndex: 0,
            Measurer,
            new DateTime(2026, 8, 15));

        layout.Should().NotBeNull();
        var cell = layout!.Cells.Single(block => block.Row == 1 && block.Column == 2);
        cell.Text.Should().BeEmpty();
        cell.HasValidationCircle.Should().BeTrue();

        var painting = PrintPreviewInstructionBuilder.Build(layout);
        var ellipse = painting.Instructions
            .Single(instruction => instruction.Kind == PrintPreviewPaintKind.Ellipse);
        ellipse.Fill.Should().BeNull();
        ellipse.Stroke.Should().Be(ValidationCircleLayoutPlanner.StrokeColor);
        ellipse.StrokeThickness.Should().Be(ValidationCircleLayoutPlanner.StrokeThickness);
        new LayoutRect(ellipse.Left, ellipse.Top, ellipse.Width, ellipse.Height)
            .Should().Be(ValidationCircleLayoutPlanner.CalculateEllipseBounds(cell.Bounds));
    }

    [Fact]
    public void GeometryPlanner_UsesWpfAuthorityProportionsAndMinimumDiameter()
    {
        ValidationCircleLayoutPlanner.CalculateEllipseBounds(new LayoutRect(10, 20, 100, 50))
            .Should().Be(new LayoutRect(22, 29, 76, 32));
        ValidationCircleLayoutPlanner.CalculateEllipseBounds(new LayoutRect(10, 20, 2, 2))
            .Should().Be(new LayoutRect(9, 19, 4, 4));
    }
}
