using FluentAssertions;
using Free.Shared.Pdf;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class PortablePdfVectorDrawingTests
{
    [Fact]
    public void BuildWithPageSetup_EmitsChartAndTextBoxVectorOpsFromSharedPrintLayout()
    {
        var workbook = new Workbook { Name = "VectorEvidence.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        PopulateChartSource(sheet);
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B4", sheet.Id),
            Title = "Vector chart title",
            Left = 24,
            Top = 24,
            Width = 260,
            Height = 180,
            ChartAreaFillColor = new CellColor(230, 242, 255),
            ChartAreaBorderColor = new CellColor(30, 90, 160),
            ChartAreaBorderThickness = 2
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 6, 4),
            Text = "Vector text box",
            Width = 140,
            Height = 48,
            FillColor = new CellColor(255, 244, 204),
            OutlineColor = new CellColor(160, 110, 20)
        });
        var exportPlan = CreatePageSetupPdfPlan(workbook);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, exportPlan);
        var ops = document.Pages.Should().ContainSingle().Subject.Ops;

        ops.OfType<PdfFillRect>().Should().Contain(rect => rect.Color == new PdfColor(230, 242, 255));
        ops.OfType<PdfStrokeRect>().Should().Contain(rect =>
            rect.Color == new PdfColor(30, 90, 160) &&
            rect.LineWidth > 0);
        ops.OfType<PdfOpacityGroup>()
            .SelectMany(group => group.Ops.OfType<PdfFillRect>())
            .Should()
            .Contain(rect => rect.Color == new PdfColor(255, 244, 204));
        ops.OfType<PdfStrokeRect>().Should().Contain(rect => rect.Color == new PdfColor(160, 110, 20));
        ops.OfType<PdfText>().Should().Contain(text => text.Text == "Vector chart title");
        ops.OfType<PdfText>().Should().Contain(text => text.Text == "Vector text box");
    }

    private static PortablePdfExportPlan CreatePageSetupPdfPlan(Workbook workbook)
    {
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0));

        printPlan.IsReady.Should().BeTrue(printPlan.StatusText);
        return PortablePdfExportPlanner.CreatePlan(printPlan, workbook);
    }

    private static void PopulateChartSource(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(11));
    }
}
