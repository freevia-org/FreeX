using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintExportDrawingEvidencePlannerTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_SummarizesPrintableChartTextOverlaysAndTextBoxes()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 3),
            Text = "Printable drawing note",
            Width = 120,
            Height = 48
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B4", sheet.Id),
            Title = "Printable chart title",
            XAxisTitle = "Printable month axis",
            YAxisTitle = "Printable sales axis",
            Left = 24,
            Top = 24,
            Width = 260,
            Height = 180,
            ShowLegend = true
        });

        var plan = PrintExportDrawingEvidencePlanner.Build(
            workbook,
            sheet,
            Paginate(sheet),
            Measurer,
            new DateTime(2026, 7, 14));

        plan.PageCount.Should().Be(1);
        plan.HasDrawingContent.Should().BeTrue();
        plan.ChartCount.Should().Be(1);
        plan.ChartTextOverlayCount.Should().BeGreaterThanOrEqualTo(3);
        plan.TextBoxCount.Should().Be(1);
        plan.TextBoxTextRunCount.Should().Be(1);
        plan.Pages.Should().ContainSingle(page =>
            page.HasSelectableChartText &&
            page.HasSelectableTextBoxText);
        plan.StatusText.Should().Contain("Print/export drawing evidence: 1 page, 1 chart");
        plan.StatusText.Should().Contain("1 text box");
    }

    [Fact]
    public void Build_UsesRenderedPageContentFilteringForHiddenAndOffPageDrawings()
    {
        var (workbook, sheet) = CreateWorkbook();
        PopulateChartSource(sheet);
        sheet.PrintArea = GridRange.Parse("A1:H20", sheet.Id);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 3),
            Text = "Visible note"
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 5, 4),
            Text = "Hidden note",
            IsVisible = false
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 40, 4),
            Text = "Off-page note"
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B4", sheet.Id),
            Title = "Visible chart",
            Left = 24,
            Top = 24,
            Width = 260,
            Height = 180
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B4", sheet.Id),
            Title = "Hidden chart",
            Left = 24,
            Top = 24,
            Width = 260,
            Height = 180,
            IsVisible = false
        });
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = GridRange.Parse("A1:B4", sheet.Id),
            Title = "Off-page chart",
            Left = 10000,
            Top = 10000,
            Width = 260,
            Height = 180
        });

        var plan = PrintExportDrawingEvidencePlanner.Build(
            workbook,
            sheet,
            Paginate(sheet),
            Measurer,
            new DateTime(2026, 7, 14));

        plan.ChartCount.Should().Be(1);
        plan.TextBoxCount.Should().Be(1);
        plan.TextBoxTextRunCount.Should().Be(1);
        plan.Pages.Should().ContainSingle(page => page.HasDrawingContent);
    }

    private static PagePaginationResult Paginate(Sheet sheet)
    {
        var printRange = sheet.PrintArea ?? sheet.GetUsedRange()
            ?? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        return PagePaginationPlanner.Paginate(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook()
    {
        var workbook = new Workbook { Name = "Book1.xlsx" };
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
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
