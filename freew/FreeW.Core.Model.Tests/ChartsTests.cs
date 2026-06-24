namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for the <see cref="Chart"/> / <see cref="ChartSeries"/> / <see cref="Run.Chart"/> model
/// (roadmap item W3): the inline-run-mark API and the convenience factories.
/// </summary>
public class ChartsTests
{
    [Fact]
    public void Create_BuildsSingleSeriesChartWithCategoriesValuesNameAndTitle()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            categories: ["A", "B", "C"],
            values: [1.0, 2.0, 3.0],
            seriesName: "Sales",
            title: "Annual");

        chart.Kind.Should().Be(ChartKind.Column);
        chart.Title.Should().Be("Annual");
        chart.Categories.Should().Equal("A", "B", "C");
        var series = chart.Series.Should().ContainSingle().Subject;
        series.Name.Should().Be("Sales");
        series.Values.Should().Equal(1.0, 2.0, 3.0);
    }

    [Fact]
    public void Create_TitleAndSeriesName_DefaultToNull()
    {
        var chart = Chart.Create(ChartKind.Bar, ["A"], [1.0]);

        chart.Title.Should().BeNull();
        chart.Series.Single().Name.Should().BeNull();
    }

    [Fact]
    public void Chart_DefaultsToColumnWithWordTypicalSize()
    {
        var chart = new Chart();

        chart.Kind.Should().Be(ChartKind.Column);
        chart.WidthPt.Should().Be(360);
        chart.HeightPt.Should().Be(216);
        chart.Categories.Should().BeEmpty();
        chart.Series.Should().BeEmpty();
    }

    [Fact]
    public void ChartSeries_Constructor_CopiesNameAndValues()
    {
        var series = new ChartSeries("North", [4.0, 5.0]);

        series.Name.Should().Be("North");
        series.Values.Should().Equal(4.0, 5.0);
    }

    [Fact]
    public void FromChart_ProducesTextlessRunCarryingTheChart()
    {
        var chart = Chart.Create(ChartKind.Pie, ["X"], [1.0]);

        var run = Run.FromChart(chart);

        run.Chart.Should().BeSameAs(chart);
        run.Text.Should().BeEmpty();
        run.Image.Should().BeNull();
        run.Equation.Should().BeNull();
    }

    [Fact]
    public void Chart_SupportsMultipleSeries()
    {
        var chart = new Chart { Kind = ChartKind.Line };
        chart.Categories.AddRange(["Jan", "Feb"]);
        chart.Series.Add(new ChartSeries("A", [1.0, 2.0]));
        chart.Series.Add(new ChartSeries("B", [3.0, 4.0]));

        chart.Series.Should().HaveCount(2);
        chart.Series[1].Values.Should().Equal(3.0, 4.0);
    }

    [Theory]
    [InlineData(ChartKind.Scatter)]
    [InlineData(ChartKind.Area)]
    [InlineData(ChartKind.Doughnut)]
    public void Create_SupportsRicherChartKinds(ChartKind kind)
    {
        var chart = Chart.Create(kind, ["A", "B"], [1.0, 2.0]);

        chart.Kind.Should().Be(kind);
        chart.Series.Single().Values.Should().Equal(1.0, 2.0);
    }

    [Fact]
    public void Chart_LegendAndAxisTitles_DefaultToOff()
    {
        var chart = new Chart();

        chart.ShowLegend.Should().BeFalse();
        chart.CategoryAxisTitle.Should().BeNull();
        chart.ValueAxisTitle.Should().BeNull();
    }

    [Fact]
    public void Chart_LegendAndAxisTitles_AreSettable()
    {
        var chart = new Chart
        {
            ShowLegend = true,
            CategoryAxisTitle = "Quarter",
            ValueAxisTitle = "USD",
        };

        chart.ShowLegend.Should().BeTrue();
        chart.CategoryAxisTitle.Should().Be("Quarter");
        chart.ValueAxisTitle.Should().Be("USD");
    }

    // ── ChartStyle catalog ─────────────────────────────────────────────────────────────

    [Fact]
    public void ChartStyle_Catalog_IsNonEmptyAndCoherent()
    {
        ChartStyle.Catalog.Should().NotBeEmpty();
        // Every entry must have a unique, positive id.
        ChartStyle.Catalog.Select(s => s.Id).Should().OnlyHaveUniqueItems();
        ChartStyle.Catalog.Should().OnlyContain(s => s.Id > 0);
        // Every entry must have a non-empty name.
        ChartStyle.Catalog.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Name));
    }

    [Fact]
    public void ChartStyle_Default_IsFirstCatalogEntry()
    {
        ChartStyle.Default.Should().Be(ChartStyle.Catalog[0]);
    }

    [Fact]
    public void ChartStyle_FindById_ReturnsMatchOrNull()
    {
        var style = ChartStyle.Catalog[0];
        ChartStyle.FindById(style.Id).Should().Be(style);
        ChartStyle.FindById(-1).Should().BeNull();
    }

    // ── ChartColorScheme catalog ────────────────────────────────────────────────────────

    [Fact]
    public void ChartColorScheme_Catalog_IsNonEmptyAndCoherent()
    {
        ChartColorScheme.Catalog.Should().NotBeEmpty();
        // Every entry must have a unique, non-empty id.
        ChartColorScheme.Catalog.Select(s => s.Id).Should().OnlyHaveUniqueItems();
        ChartColorScheme.Catalog.Should().OnlyContain(s => !string.IsNullOrWhiteSpace(s.Id));
        // Every entry must have at least 4 colours.
        ChartColorScheme.Catalog.Should().OnlyContain(s => s.Colors.Count >= 4);
        // Every colour must look like a #RRGGBB hex.
        ChartColorScheme.Catalog.SelectMany(s => s.Colors)
            .Should().OnlyContain(hex => hex.StartsWith("#") && hex.Length == 7);
    }

    [Fact]
    public void ChartColorScheme_Default_IsFirstCatalogEntry()
    {
        ChartColorScheme.Default.Should().Be(ChartColorScheme.Catalog[0]);
    }

    [Fact]
    public void ChartColorScheme_FindById_ReturnsMatchOrNull()
    {
        var scheme = ChartColorScheme.Catalog[0];
        ChartColorScheme.FindById(scheme.Id).Should().Be(scheme);
        ChartColorScheme.FindById("nonexistent").Should().BeNull();
    }

    // ── ChartQuickLayout catalog ─────────────────────────────────────────────────────────

    [Fact]
    public void ChartQuickLayout_Catalog_IsNonEmptyAndCoherent()
    {
        ChartQuickLayout.Catalog.Should().NotBeEmpty();
        // Every entry must have a unique, positive id.
        ChartQuickLayout.Catalog.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        ChartQuickLayout.Catalog.Should().OnlyContain(l => l.Id > 0);
        // Every entry must have a non-empty name.
        ChartQuickLayout.Catalog.Should().OnlyContain(l => !string.IsNullOrWhiteSpace(l.Name));
    }

    [Fact]
    public void ChartQuickLayout_Default_IsFirstCatalogEntry()
    {
        ChartQuickLayout.Default.Should().Be(ChartQuickLayout.Catalog[0]);
    }

    [Fact]
    public void ChartQuickLayout_FindById_ReturnsMatchOrNull()
    {
        var layout = ChartQuickLayout.Catalog[0];
        ChartQuickLayout.FindById(layout.Id).Should().Be(layout);
        ChartQuickLayout.FindById(-99).Should().BeNull();
    }

    // ── Chart model fields ────────────────────────────────────────────────────────────────

    [Fact]
    public void Chart_StyleId_ColorSchemeId_QuickLayoutId_DefaultToUnset()
    {
        var chart = new Chart();
        chart.StyleId.Should().Be(0);
        chart.ColorSchemeId.Should().BeNull();
        chart.QuickLayoutId.Should().Be(0);
    }

    [Fact]
    public void Chart_StyleId_ColorSchemeId_QuickLayoutId_AreSettable()
    {
        var chart = new Chart
        {
            StyleId = 3,
            ColorSchemeId = "colorful2",
            QuickLayoutId = 5
        };
        chart.StyleId.Should().Be(3);
        chart.ColorSchemeId.Should().Be("colorful2");
        chart.QuickLayoutId.Should().Be(5);
    }
}
