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
    public void SetChartQuickLayoutCommand_AppliesEveryCatalogEntryWithoutChangingChartContentOrStyle()
    {
        foreach (var layout in ChartQuickLayout.Catalog)
        {
            var (_, bus, chart) = NewChartDoc(showLegend: true, quickLayoutId: 2);
            chart.Title = "Revenue";
            chart.CategoryAxisTitle = "Quarter";
            chart.ValueAxisTitle = "USD";
            chart.StyleId = 7;
            chart.ColorSchemeId = "mono-blue";
            chart.WidthPt = 420;
            chart.HeightPt = 260;
            var categories = chart.Categories.ToArray();
            var series = chart.Series.Select(item => (item.Name, Values: item.Values.ToArray())).ToArray();

            bus.Execute(new SetChartQuickLayoutCommand(0, 0, layout));

            chart.QuickLayoutId.Should().Be(layout.Id);
            chart.Title.Should().Be("Revenue");
            chart.ShowLegend.Should().BeTrue();
            chart.CategoryAxisTitle.Should().Be("Quarter");
            chart.ValueAxisTitle.Should().Be("USD");
            chart.StyleId.Should().Be(7);
            chart.ColorSchemeId.Should().Be("mono-blue");
            chart.WidthPt.Should().Be(420);
            chart.HeightPt.Should().Be(260);
            chart.Categories.Should().Equal(categories);
            chart.Series.Select(item => (item.Name, Values: item.Values.ToArray()))
                .Should().BeEquivalentTo(series, options => options.WithStrictOrdering());

            bus.Undo().Should().BeTrue();
            chart.QuickLayoutId.Should().Be(2);
            bus.Redo().Should().BeTrue();
            chart.QuickLayoutId.Should().Be(layout.Id);
        }
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

    [Fact]
    public void ReplaceChartDataCommand_AppliesAndRevertsEditableChartData()
    {
        var (_, bus, chart) = NewChartDoc();
        chart.Title = "Old";
        chart.WidthPt = 420;
        chart.HeightPt = 260;
        chart.RotationAngle = 25;
        chart.FlipH = true;
        chart.StyleId = 7;
        chart.ColorSchemeId = "mono-blue";
        chart.QuickLayoutId = 4;
        chart.Placement = new FloatingPlacement
        {
            Wrapping = ImageWrapping.InFront,
            HorizontalOffsetPt = 18,
            VerticalOffsetPt = 24,
            ZOrderIndex = 5,
        };
        var placement = chart.Placement;
        var replacement = Chart.Create(
            ChartKind.Line,
            ["Jan", "Feb", "Mar"],
            [5.0, 6.0, 7.0],
            seriesName: "Revenue",
            title: "Monthly Revenue");
        replacement.WidthPt = 0;
        replacement.HeightPt = 0;
        replacement.RotationAngle = 90;
        replacement.StyleId = 2;
        replacement.ColorSchemeId = "colorful-2";
        replacement.QuickLayoutId = 8;
        replacement.Placement = new FloatingPlacement { Wrapping = ImageWrapping.Behind };

        bus.Execute(new ReplaceChartDataCommand(0, 0, replacement));

        chart.Kind.Should().Be(ChartKind.Line);
        chart.Title.Should().Be("Monthly Revenue");
        chart.Categories.Should().Equal("Jan", "Feb", "Mar");
        chart.Series.Should().ContainSingle();
        chart.Series[0].Name.Should().Be("Revenue");
        chart.Series[0].Values.Should().Equal(5.0, 6.0, 7.0);
        chart.WidthPt.Should().Be(420);
        chart.HeightPt.Should().Be(260);
        chart.RotationAngle.Should().Be(25);
        chart.FlipH.Should().BeTrue();
        chart.StyleId.Should().Be(7);
        chart.ColorSchemeId.Should().Be("mono-blue");
        chart.QuickLayoutId.Should().Be(4);
        chart.Placement.Should().BeSameAs(placement);

        bus.Undo().Should().BeTrue();
        chart.Kind.Should().Be(ChartKind.Column);
        chart.Title.Should().Be("Old");
        chart.Categories.Should().Equal("Q1", "Q2");
        chart.Series[0].Name.Should().Be("Sales");
        chart.Series[0].Values.Should().Equal(1.0, 2.0);
        chart.StyleId.Should().Be(7);
        chart.Placement.Should().BeSameAs(placement);

        bus.Redo().Should().BeTrue();
        chart.Kind.Should().Be(ChartKind.Line);
        chart.Categories.Should().Equal("Jan", "Feb", "Mar");
    }
}
