using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{

    [Fact]
    public void ChartTypeSupport_IdentifiesTrendlineChartTypes()
    {
        var supportedTypes = new[] { ChartType.Column, ChartType.Line, ChartType.ThreeDLine, ChartType.Bar, ChartType.Scatter, ChartType.Bubble, ChartType.Area, ChartType.ThreeDArea };
        var unsupportedTypes = Enum.GetValues<ChartType>().Except(supportedTypes);

        supportedTypes.Should().OnlyContain(type => ChartTypeSupport.SupportsTrendlines(type));
        unsupportedTypes.Should().OnlyContain(type => !ChartTypeSupport.SupportsTrendlines(type));
    }

    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.StackedColumn)]
    [InlineData(ChartType.PercentStackedColumn)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.ThreeDLine)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.ThreeDPie)]
    [InlineData(ChartType.Doughnut)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.PercentStackedBar)]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bubble)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.ThreeDArea)]
    [InlineData(ChartType.Radar)]
    [InlineData(ChartType.Stock)]
    [InlineData(ChartType.Surface)]
    [InlineData(ChartType.ThreeDSurface)]
    [InlineData(ChartType.ThreeDColumn)]
    [InlineData(ChartType.ThreeDBar)]
    public void RenderableChartTypes_AreKnownAndRenderable(ChartType type)
    {
        ChartTypeSupport.IsKnown(type).Should().BeTrue();
        ChartTypeSupport.IsRenderable(type).Should().BeTrue();
    }

    [Theory]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Pareto)]
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Funnel)]
    public void AdvancedChartFamilies_AreKnownRenderableAuthorableAndChartExBacked(ChartType type)
    {
        ChartTypeSupport.IsKnown(type).Should().BeTrue();
        ChartTypeSupport.IsAdvancedFamily(type).Should().BeTrue();
        ChartTypeSupport.IsChartExFamily(type).Should().BeTrue();
        ChartTypeSupport.IsRenderable(type).Should().BeTrue();
        ChartTypeSupport.IsAuthorable(type).Should().BeTrue();
        ChartAuthoringPlanner.CanAuthor(type).Should().BeTrue();
    }

    [Fact]
    public void MapChartType_IsKnownButDeferredForAuthoringAndRendering()
    {
        ChartTypeSupport.IsKnown(ChartType.Map).Should().BeTrue();
        ChartTypeSupport.IsAdvancedFamily(ChartType.Map).Should().BeTrue();
        ChartTypeSupport.IsChartExFamily(ChartType.Map).Should().BeFalse();
        ChartTypeSupport.IsRenderable(ChartType.Map).Should().BeFalse();
        ChartTypeSupport.IsAuthorable(ChartType.Map).Should().BeFalse();
        ChartTypeSupport.IsDeferredAuthoringFamily(ChartType.Map).Should().BeTrue();
        ChartAuthoringPlanner.CanAuthor(ChartType.Map).Should().BeFalse();
        ChartAuthoringPlanner.RejectIfUnsupported(ChartType.Map)!.ErrorMessage
            .Should().Contain("recognized for XLSX preservation");
    }

    [Fact]
    public void ChartTypeSupport_IdentifiesSecondaryAxisChartTypes()
    {
        var supportedTypes = new[] { ChartType.Column, ChartType.Line, ChartType.ThreeDLine, ChartType.Area, ChartType.ThreeDArea, ChartType.Scatter };
        var unsupportedTypes = Enum.GetValues<ChartType>().Except(supportedTypes);

        supportedTypes.Should().OnlyContain(type => ChartTypeSupport.SupportsSecondaryAxis(type));
        unsupportedTypes.Should().OnlyContain(type => !ChartTypeSupport.SupportsSecondaryAxis(type));
    }

    [Fact]
    public void ChartTypeSupport_IdentifiesComboLineOverlayChartTypes()
    {
        var supportedTypes = new[] { ChartType.Column, ChartType.StackedColumn, ChartType.PercentStackedColumn, ChartType.Area, ChartType.ThreeDArea };
        var unsupportedTypes = Enum.GetValues<ChartType>().Except(supportedTypes);

        supportedTypes.Should().OnlyContain(type => ChartTypeSupport.SupportsComboLineOverlay(type));
        unsupportedTypes.Should().OnlyContain(type => !ChartTypeSupport.SupportsComboLineOverlay(type));
    }

    [Fact]
    public void ChartTypeSupport_RequiresAssignableSeriesForComboLineOverlay()
    {
        var sheetId = SheetId.New();
        var singleSeriesColumn = new ChartModel
        {
            Type = ChartType.Column,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 2))
        };
        var twoSeriesColumn = new ChartModel
        {
            Type = ChartType.Column,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };
        var twoSeriesLine = new ChartModel
        {
            Type = ChartType.Line,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };

        ChartTypeSupport.SupportsComboLineOverlay(singleSeriesColumn).Should().BeFalse();
        ChartTypeSupport.SupportsComboLineOverlay(twoSeriesColumn).Should().BeTrue();
        ChartTypeSupport.SupportsComboLineOverlay(twoSeriesLine).Should().BeFalse();
    }

    [Fact]
    public void ChartTypeSupport_IdentifiesXAxisLogScaleChartTypes()
    {
        var supportedTypes = new[] { ChartType.Bar, ChartType.StackedBar, ChartType.PercentStackedBar, ChartType.ThreeDBar, ChartType.Scatter, ChartType.Bubble };
        var unsupportedTypes = Enum.GetValues<ChartType>().Except(supportedTypes);

        supportedTypes.Should().OnlyContain(type => ChartTypeSupport.SupportsXAxisLogScale(type));
        unsupportedTypes.Should().OnlyContain(type => !ChartTypeSupport.SupportsXAxisLogScale(type));
    }

    [Fact]
    public void ChartTypeSupport_IdentifiesYAxisLogScaleChartTypes()
    {
        var supportedTypes = new[]
        {
            ChartType.Column,
            ChartType.StackedColumn,
            ChartType.PercentStackedColumn,
            ChartType.Line,
            ChartType.ThreeDLine,
            ChartType.Scatter,
            ChartType.Bubble,
            ChartType.Area,
            ChartType.ThreeDArea
        };
        var unsupportedTypes = Enum.GetValues<ChartType>().Except(supportedTypes);

        supportedTypes.Should().OnlyContain(type => ChartTypeSupport.SupportsYAxisLogScale(type));
        unsupportedTypes.Should().OnlyContain(type => !ChartTypeSupport.SupportsYAxisLogScale(type));
    }

    [Fact]
    public void ChartTypeSupport_IdentifiesValueAxisBoundsChartTypes()
    {
        var xAxisSupportedTypes = new[] { ChartType.Bar, ChartType.StackedBar, ChartType.PercentStackedBar, ChartType.ThreeDBar, ChartType.Scatter, ChartType.Bubble };
        var yAxisSupportedTypes = new[]
        {
            ChartType.Column,
            ChartType.StackedColumn,
            ChartType.PercentStackedColumn,
            ChartType.Line,
            ChartType.ThreeDLine,
            ChartType.Scatter,
            ChartType.Bubble,
            ChartType.Area,
            ChartType.ThreeDArea
        };

        xAxisSupportedTypes.Should().OnlyContain(type => ChartTypeSupport.SupportsXAxisBounds(type));
        Enum.GetValues<ChartType>().Except(xAxisSupportedTypes).Should().OnlyContain(type => !ChartTypeSupport.SupportsXAxisBounds(type));
        yAxisSupportedTypes.Should().OnlyContain(type => ChartTypeSupport.SupportsYAxisBounds(type));
        Enum.GetValues<ChartType>().Except(yAxisSupportedTypes).Should().OnlyContain(type => !ChartTypeSupport.SupportsYAxisBounds(type));
    }

    [Fact]
    public void ChartTypeSupport_IdentifiesSeriesMarkerChartTypes()
    {
        var supportedTypes = new[] { ChartType.Line, ChartType.ThreeDLine, ChartType.Scatter };
        var unsupportedTypes = Enum.GetValues<ChartType>().Except(supportedTypes);

        supportedTypes.Should().OnlyContain(type => ChartTypeSupport.SupportsSeriesMarkers(type));
        unsupportedTypes.Should().OnlyContain(type => !ChartTypeSupport.SupportsSeriesMarkers(type));
    }

    [Fact]
    public void ChartTypeSupport_CountsDataSeriesWithoutScatterXColumn()
    {
        var sheetId = SheetId.New();
        var scatter = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };
        var column = new ChartModel
        {
            Type = ChartType.Column,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };
        var bubble = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };

        ChartTypeSupport.GetDataSeriesCount(scatter).Should().Be(2);
        ChartTypeSupport.GetDataSeriesCount(column).Should().Be(2);
        ChartTypeSupport.GetDataSeriesCount(bubble).Should().Be(1);
    }

    [Fact]
    public void ChartTypeSupport_CountsBubbleYAndSizePairsAsSeparateSeries()
    {
        var sheetId = SheetId.New();
        var bubble = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 5))
        };

        ChartTypeSupport.GetDataSeriesCount(bubble).Should().Be(2);
        ChartTypeSupport.GetYAxisValueColumns(bubble).Should().Equal(2u, 4u);
    }

    [Fact]
    public void ChartTypeSupport_CountsChartDataPointsWithoutHeaderRow()
    {
        var sheetId = SheetId.New();
        var withHeader = new ChartModel
        {
            Type = ChartType.Pie,
            FirstRowIsHeader = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 2))
        };
        var withoutHeader = new ChartModel
        {
            Type = ChartType.Pie,
            FirstRowIsHeader = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 5, 2))
        };

        ChartTypeSupport.GetDataPointCount(withHeader).Should().Be(4);
        ChartTypeSupport.GetDataPointCount(withoutHeader).Should().Be(5);
    }

    [Fact]
    public void ChartTypeSupport_SelectsAxisValueColumnsForXyCharts()
    {
        var sheetId = SheetId.New();
        var scatter = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };
        var bubble = new ChartModel
        {
            Type = ChartType.Bubble,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };
        var column = new ChartModel
        {
            Type = ChartType.Column,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };

        ChartTypeSupport.GetXAxisValueColumn(scatter).Should().Be(1);
        ChartTypeSupport.GetYAxisValueColumns(scatter).Should().Equal(2u, 3u);
        ChartTypeSupport.GetXAxisValueColumn(bubble).Should().Be(1);
        ChartTypeSupport.GetYAxisValueColumns(bubble).Should().Equal(2u);
        ChartTypeSupport.GetXAxisValueColumn(column).Should().Be(1);
        ChartTypeSupport.GetYAxisValueColumns(column).Should().Equal(2u, 3u);
    }

    [Fact]
    public void ChartTypeSupport_ReturnsNoSeriesWhenOnlyCategoryOrXAxisColumnIsAvailable()
    {
        var sheetId = SheetId.New();
        var column = new ChartModel
        {
            Type = ChartType.Column,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 1))
        };
        var scatter = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstColIsCategories = false,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 1))
        };

        ChartTypeSupport.GetDataSeriesCount(column).Should().Be(0);
        ChartTypeSupport.GetYAxisValueColumns(column).Should().BeEmpty();
        ChartTypeSupport.GetDataSeriesCount(scatter).Should().Be(0);
        ChartTypeSupport.GetYAxisValueColumns(scatter).Should().BeEmpty();
    }

    [Fact]
    public void ChartTypeSupport_SelectsBarXAxisValueColumnsFromSeriesData()
    {
        var sheetId = SheetId.New();
        var bar = new ChartModel
        {
            Type = ChartType.Bar,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 3))
        };

        ChartTypeSupport.GetXAxisValueColumns(bar).Should().Equal(2u, 3u);
    }

    [Fact]
    public void ChartTypeSupport_IdentifiesBarGapWidthChartTypes()
    {
        var supportedTypes = new[]
        {
            ChartType.Column, ChartType.StackedColumn, ChartType.PercentStackedColumn, ChartType.ThreeDColumn,
            ChartType.Bar, ChartType.StackedBar, ChartType.PercentStackedBar, ChartType.ThreeDBar
        };
        var unsupportedTypes = Enum.GetValues<ChartType>().Except(supportedTypes);

        supportedTypes.Should().OnlyContain(type => ChartTypeSupport.SupportsBarGapWidth(type));
        unsupportedTypes.Should().OnlyContain(type => !ChartTypeSupport.SupportsBarGapWidth(type));
    }
}
