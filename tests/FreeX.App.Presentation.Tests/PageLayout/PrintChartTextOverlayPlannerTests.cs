using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

public sealed class PrintChartTextOverlayPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void Build_PlansTitleAxisLegendTicksAndDataLabelsFromChartDataCells()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = Range(30, 30, 33, 31),
            Title = "Printable chart label title",
            XAxisTitle = "Printable chart label axis",
            YAxisTitle = "Printable value axis",
            Left = 24,
            Top = 24,
            Width = 380,
            Height = 210,
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Right,
            YAxisMinimum = 0,
            YAxisMaximum = 20,
            YAxisMajorUnit = 10,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowDataLabels = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelValue = true
        };

        var overlays = Build(chart);
        var texts = overlays.Select(overlay => overlay.Text).ToList();

        texts.Should().Contain("Printable chart label title");
        texts.Should().Contain("Printable chart label axis");
        texts.Should().Contain("Printable value axis");
        texts.Should().Contain("PDF Rev");
        texts.Should().Contain("PDF tick Jan");
        texts.Should().Contain("$10.00");
        texts.Should().Contain("PDF tick Jan, 8");
        overlays.Single(overlay => overlay.Text == "Printable chart label title")
            .Role.Should().Be(PrintChartTextOverlayRole.ChartTitle);
        overlays.Single(overlay => overlay.Text == "Printable chart label axis")
            .Role.Should().Be(PrintChartTextOverlayRole.CategoryAxisTitle);
        overlays.Single(overlay => overlay.Text == "PDF Rev")
            .Role.Should().Be(PrintChartTextOverlayRole.LegendEntry);
        overlays.Single(overlay => overlay.Text == "PDF tick Jan")
            .Role.Should().Be(PrintChartTextOverlayRole.CategoryTickLabel);
        overlays.Single(overlay => overlay.Text == "$10.00")
            .Role.Should().Be(PrintChartTextOverlayRole.ValueTickLabel);
        overlays.Single(overlay => overlay.Text == "PDF tick Jan, 8")
            .Role.Should().Be(PrintChartTextOverlayRole.DataLabel);
        overlays.Single(overlay => overlay.Text == "Printable value axis")
            .RotationDegrees.Should().Be(-90);
        overlays.Single(overlay => overlay.Text == "Printable value axis")
            .Role.Should().Be(PrintChartTextOverlayRole.ValueAxisTitle);
    }

    [Theory]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.ThreeDPie)]
    [InlineData(ChartType.Doughnut)]
    public void Build_PlansPieFamilyLegendAndPercentageDataLabels(ChartType chartType)
    {
        var chart = new ChartModel
        {
            Type = chartType,
            DataRange = Range(30, 30, 33, 31),
            Title = "Printable pie label title",
            Width = 380,
            Height = 210,
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Right,
            ShowDataLabels = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelValue = true,
            ShowDataLabelPercentage = true
        };

        var overlays = Build(chart);
        var texts = overlays.Select(overlay => overlay.Text).ToList();

        texts.Should().Contain("PDF pie Jan");
        texts.Should().Contain("PDF pie Feb");
        texts.Should().Contain("PDF pie Jan, 24%");
        overlays.Where(overlay => overlay.Text is "PDF pie Jan" or "PDF pie Feb")
            .Should().OnlyContain(overlay => overlay.Role == PrintChartTextOverlayRole.LegendEntry);
        overlays.Single(overlay => overlay.Text == "PDF pie Jan, 24%")
            .Role.Should().Be(PrintChartTextOverlayRole.DataLabel);
    }

    [Fact]
    public void BoundOverlayText_UsesRendererMeasurementAndEllipsis()
    {
        PrintChartTextOverlayPlanner.BoundOverlayText(
                "abcdefghi",
                maxWidth: 8,
                fontSize: 12,
                MeasureByCharacterCount)
            .Should()
            .Be("abcdefg" + PrintChartTextOverlayPlanner.Ellipsis);
    }

    [Fact]
    public void SourceGuard_KeepsOverlayDecisionLogicInPresentationPlanner()
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

        var plannerSource = File.ReadAllText(Path.Combine(
            presentationRoot,
            "PageLayout",
            "PrintChartTextOverlayPlanner.cs"));
        var hostOverlaySource = File.ReadAllText(Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "PrintRenderer.ChartTextOverlays.cs"));

        plannerSource.Should().Contain("PrintChartTextOverlayPlan");
        plannerSource.Should().Contain("PrintChartTextMeasure");
        plannerSource.Should().Contain("ChartDataLabelTextPlanner.FormatDataLabel");
        plannerSource.Should().NotContain("System.Windows");
        plannerSource.Should().NotContain("FormattedText");
        plannerSource.Should().NotContain("PdfTextOverlay");

        hostOverlaySource.Should().Contain("PrintChartTextOverlayPlanner.Build");
        hostOverlaySource.Should().Contain("MeasurePrintedChartOverlayText");
        hostOverlaySource.Should().Contain("CreatePrintedChartTextOverlay");
        hostOverlaySource.Should().NotContain("BuildPrintedChartSeries");
        hostOverlaySource.Should().NotContain("EstimatePrintedChartPlotRect");
        hostOverlaySource.Should().NotContain("ChartDataLabelTextPlanner.FormatDataLabel");
    }

    private static IReadOnlyList<PrintChartTextOverlayPlan> Build(ChartModel chart) =>
        PrintChartTextOverlayPlanner.Build(
            chart,
            WorkbookTheme.Office,
            new LayoutRect(24, 24, 380, 210),
            ChartDataCellsFor(chart.Type),
            new Dictionary<(uint Row, uint Col), DisplayCell>(),
            MeasureByCharacterCount);

    private static IReadOnlyList<ChartDataCell> ChartDataCellsFor(ChartType chartType)
    {
        var isPie = chartType is ChartType.Pie or ChartType.ThreeDPie or ChartType.Doughnut;
        return
        [
            Cell(30, 30, "Month"),
            Cell(30, 31, isPie ? "PDF Share" : "PDF Rev"),
            Cell(31, 30, isPie ? "PDF pie Jan" : "PDF tick Jan"),
            Cell(31, 31, "8", new NumberValue(8)),
            Cell(32, 30, isPie ? "PDF pie Feb" : "PDF tick Feb"),
            Cell(32, 31, "14", new NumberValue(14)),
            Cell(33, 30, isPie ? "PDF pie Mar" : "PDF tick Mar"),
            Cell(33, 31, "11", new NumberValue(11))
        ];
    }

    private static ChartDataCell Cell(uint row, uint col, string text, ScalarValue? value = null) =>
        new(SheetId, row, col, text, value);

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(SheetId, startRow, startCol),
            new CellAddress(SheetId, endRow, endCol));

    private static PrintChartOverlayTextMetrics MeasureByCharacterCount(string text, double fontSize) =>
        new(text.Length, text.Length);
}
