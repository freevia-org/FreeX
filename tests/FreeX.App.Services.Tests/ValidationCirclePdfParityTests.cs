using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ValidationCirclePdfParityTests
{
    [Fact]
    public void PortablePlan_CarriesCircleForEmptyPrintedCell()
    {
        var (workbook, sheet, circled) = CreateWorkbookWithEmptyCircledCell();
        var pdfPlan = CreatePdfPlan(workbook);

        var content = PortablePdfPageContentPlanner.CreatePlan(
            workbook,
            pdfPlan.PageRequests.Single());

        content.Cells.Single(cell => cell.Row == circled.Row && cell.Column == circled.Col)
            .Should().Match<PortablePdfPageCell>(cell =>
                cell.DisplayText == "" && cell.HasValidationCircle);
    }

    [Fact]
    public void PageSetupAndLegacyPdfPaths_EmitSharedValidationCircleEllipse()
    {
        var (workbook, _, _) = CreateWorkbookWithEmptyCircledCell();
        var pdfPlan = CreatePdfPlan(workbook);

        var pageSetupDocument = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        var legacyDocument = WorkbookPdfContentBuilder.Build(
            workbook,
            pdfPlan,
            new PortablePdfDocumentOptions());

        AssertCircle(pageSetupDocument.Pages.Single().Ops);
        AssertCircle(legacyDocument.Pages.Single().Ops);
    }

    [Fact]
    public void PdfPaths_DoNotEmitValidationCircleWithoutSharedSheetState()
    {
        var (workbook, sheet, _) = CreateWorkbookWithEmptyCircledCell();
        sheet.ValidationCircleCells = null;
        var pdfPlan = CreatePdfPlan(workbook);

        WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan)
            .Pages.Single().Ops.OfType<PdfStrokeEllipse>()
            .Should().BeEmpty();
        WorkbookPdfContentBuilder.Build(workbook, pdfPlan, new PortablePdfDocumentOptions())
            .Pages.Single().Ops.OfType<PdfStrokeEllipse>()
            .Should().BeEmpty();
    }

    private static void AssertCircle(IReadOnlyList<PdfDrawOp> operations)
    {
        var circle = operations.OfType<PdfStrokeEllipse>().Should().ContainSingle().Subject;
        circle.Color.Should().Be(new PdfColor(
            ValidationCircleLayoutPlanner.StrokeColor.R,
            ValidationCircleLayoutPlanner.StrokeColor.G,
            ValidationCircleLayoutPlanner.StrokeColor.B));
        circle.LineWidth.Should().Be(ValidationCircleLayoutPlanner.StrokeThickness);
        circle.Width.Should().BeGreaterThan(0);
        circle.Height.Should().BeGreaterThan(0);
    }

    private static (Workbook Workbook, Sheet Sheet, CellAddress Circled) CreateWorkbookWithEmptyCircledCell()
    {
        var workbook = new Workbook("Circle PDF");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));
        var circled = new CellAddress(sheet.Id, 1, 2);
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            circled);
        sheet.ValidationCircleCells = [circled];
        return (workbook, sheet, circled);
    }

    private static PortablePdfExportPlan CreatePdfPlan(Workbook workbook)
    {
        var export = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0));
        export.IsReady.Should().BeTrue();
        var pdfPlan = PortablePdfExportPlanner.CreatePlan(export);
        pdfPlan.IsReady.Should().BeTrue();
        return pdfPlan;
    }
}
