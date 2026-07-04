namespace FreeW.Core.Model.Tests;

public sealed class ChartEditCommandTests
{
    private sealed class Context(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static (TextDocument Doc, DocumentCommandBus Bus, Chart Chart) NewChartDoc(bool showLegend = false, int quickLayoutId = 0)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0], seriesName: "Sales");
        chart.ShowLegend = showLegend;
        chart.QuickLayoutId = quickLayoutId;
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromChart(chart));
        doc.Blocks.Add(paragraph);
        return (doc, new DocumentCommandBus(new Context(doc)), chart);
    }

    [Fact]
    public void SetChartLegendCommand_AppliesAndRevertsLegendState()
    {
        var (_, bus, chart) = NewChartDoc();

        bus.Execute(new SetChartLegendCommand(0, 0, showLegend: true));

        chart.ShowLegend.Should().BeTrue();

        bus.Undo().Should().BeTrue();
        chart.ShowLegend.Should().BeFalse();

        bus.Redo().Should().BeTrue();
        chart.ShowLegend.Should().BeTrue();
    }

    [Fact]
    public void SetChartLegendCommand_ClearsQuickLayoutOverrideAndRestoresItOnUndo()
    {
        var (_, bus, chart) = NewChartDoc(showLegend: false, quickLayoutId: 3);

        bus.Execute(new SetChartLegendCommand(0, 0, showLegend: false));

        chart.ShowLegend.Should().BeFalse();
        chart.QuickLayoutId.Should().Be(0);

        bus.Undo().Should().BeTrue();
        chart.ShowLegend.Should().BeFalse();
        chart.QuickLayoutId.Should().Be(3);
    }

    [Fact]
    public void SetChartLegendCommand_NoopsWhenRunHasNoChart()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("plain"));
        var bus = new DocumentCommandBus(new Context(doc));

        bus.Execute(new SetChartLegendCommand(0, 0, showLegend: true));

        ((Paragraph)doc.Blocks[0]).Runs[0].Chart.Should().BeNull();
    }

    [Fact]
    public void SetChartTitleCommand_AppliesNormalizesClearsLayoutAndRevertsTitle()
    {
        var (_, bus, chart) = NewChartDoc(quickLayoutId: 2);
        chart.Title = "Old Title";

        bus.Execute(new SetChartTitleCommand(0, 0, "  Chart Title  "));

        chart.Title.Should().Be("Chart Title");
        chart.QuickLayoutId.Should().Be(0);

        bus.Undo().Should().BeTrue();
        chart.Title.Should().Be("Old Title");
        chart.QuickLayoutId.Should().Be(2);

        bus.Redo().Should().BeTrue();
        chart.Title.Should().Be("Chart Title");
    }

    [Fact]
    public void SetChartTitleCommand_ClearsTitleWhenBlank()
    {
        var (_, bus, chart) = NewChartDoc();
        chart.Title = "Old Title";

        bus.Execute(new SetChartTitleCommand(0, 0, "   "));

        chart.Title.Should().BeNull();

        bus.Undo().Should().BeTrue();
        chart.Title.Should().Be("Old Title");
    }

    [Fact]
    public void SetChartAxisTitlesCommand_AppliesNormalizesAndRevertsTitles()
    {
        var (_, bus, chart) = NewChartDoc(quickLayoutId: 9);
        chart.CategoryAxisTitle = "Old Category";
        chart.ValueAxisTitle = "Old Value";

        bus.Execute(new SetChartAxisTitlesCommand(0, 0, "  Quarter  ", "  Revenue  "));

        chart.CategoryAxisTitle.Should().Be("Quarter");
        chart.ValueAxisTitle.Should().Be("Revenue");
        chart.QuickLayoutId.Should().Be(0);

        bus.Undo().Should().BeTrue();
        chart.CategoryAxisTitle.Should().Be("Old Category");
        chart.ValueAxisTitle.Should().Be("Old Value");
        chart.QuickLayoutId.Should().Be(9);

        bus.Redo().Should().BeTrue();
        chart.CategoryAxisTitle.Should().Be("Quarter");
        chart.ValueAxisTitle.Should().Be("Revenue");
    }

    [Fact]
    public void SetChartAxisTitlesCommand_NoopsForAxislessCharts()
    {
        var (_, bus, chart) = NewChartDoc();
        chart.Kind = ChartKind.Pie;

        bus.Execute(new SetChartAxisTitlesCommand(0, 0, "Category", "Value"));

        chart.CategoryAxisTitle.Should().BeNull();
        chart.ValueAxisTitle.Should().BeNull();
        bus.Undo().Should().BeTrue();
        chart.CategoryAxisTitle.Should().BeNull();
        chart.ValueAxisTitle.Should().BeNull();
    }
}
