using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class ChartCommandTests
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
    [InlineData(ChartType.Map)]
    public void AdvancedChartTypes_AreRecognizedButNotRenderable(ChartType type)
    {
        ChartTypeSupport.IsKnown(type).Should().BeTrue();
        ChartTypeSupport.IsRenderable(type).Should().BeFalse();
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

    [Theory]
    [InlineData(ChartType.Column)]
    [InlineData(ChartType.StackedColumn)]
    [InlineData(ChartType.PercentStackedColumn)]
    [InlineData(ChartType.Line)]
    [InlineData(ChartType.ThreeDLine)]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.Doughnut)]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.PercentStackedBar)]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bubble)]
    [InlineData(ChartType.Area)]
    [InlineData(ChartType.ThreeDArea)]
    public void AddChartCommand_AddsRequestedChartTypeAndUndoRemovesIt(ChartType type)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 4));

        var command = new AddChartCommand(sheet.Id, range, type, "Sales");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts.Should().ContainSingle();
        sheet.Charts[0].Type.Should().Be(type);
        sheet.Charts[0].DataRange.Should().Be(range);
        sheet.Charts[0].Title.Should().Be("Sales");

        command.Revert(ctx);

        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsDataRangeOnDifferentSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 3, 2));

        var command = new AddChartCommand(sheet1.Id, range, ChartType.Column);

        command.Apply(ctx).Success.Should().BeFalse();
        sheet1.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        var ctx = new SimpleCtx(wb);
        var range = CreateChartRange(sheet);

        var outcome = new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("protected");
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_AllowsProtectedSheetWithEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        var ctx = new SimpleCtx(wb);
        var range = CreateChartRange(sheet);

        var outcome = new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Charts.Should().ContainSingle();
    }

    [Fact]
    public void AddChartCommand_ReplacesInvalidChartTypeWithColumn()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        var command = new AddChartCommand(sheet.Id, range, (ChartType)99, "Sales");

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts.Should().ContainSingle().Which.Type.Should().Be(ChartType.Column);
        sheet.Charts[0].FirstColIsCategories.Should().BeTrue();
    }

    [Theory]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    [InlineData(ChartType.Histogram)]
    [InlineData(ChartType.Pareto)]
    [InlineData(ChartType.BoxAndWhisker)]
    [InlineData(ChartType.Waterfall)]
    [InlineData(ChartType.Funnel)]
    [InlineData(ChartType.Map)]
    public void AddChartCommand_RejectsDeferredChartFamilies(ChartType type)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        var outcome = new AddChartCommand(sheet.Id, range, type, "Sales").Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("recognized for XLSX preservation");
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsInvalidInitialSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        new AddChartCommand(sheet.Id, range, ChartType.Column, width: double.NaN)
            .Apply(ctx).Success.Should().BeFalse();
        new AddChartCommand(sheet.Id, range, ChartType.Column, height: double.PositiveInfinity)
            .Apply(ctx).Success.Should().BeFalse();
        new AddChartCommand(sheet.Id, range, ChartType.Column, width: 0)
            .Apply(ctx).Success.Should().BeFalse();

        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsRangesWithoutDataPoints()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var headerOnlyRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 2));

        var outcome = new AddChartCommand(sheet.Id, headerOnlyRange, ChartType.Column).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.Charts.Should().BeEmpty();
    }

    [Fact]
    public void AddChartCommand_RejectsRangesWithoutDataSeries()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var incompleteBubbleRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));

        var outcome = new AddChartCommand(sheet.Id, incompleteBubbleRange, ChartType.Bubble).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.Charts.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bubble)]
    public void AddChartCommand_UsesNumericFirstColumnForXyCharts(ChartType type)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));

        var command = new AddChartCommand(sheet.Id, range, type);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts.Should().ContainSingle();
        sheet.Charts[0].FirstColIsCategories.Should().BeFalse();
    }

    [Fact]
    public void AddChartSheetCommand_CreatesDefaultChartSheetAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Sheet1");
        wb.AddSheet("Chart1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(source.Id, 1, 1),
            new CellAddress(source.Id, 4, 3));

        var command = new AddChartSheetCommand(source.Id, range, ChartType.Column, "Chart");

        command.Apply(ctx).Success.Should().BeTrue();

        command.CreatedSheetId.Should().NotBeNull();
        source.Charts.Should().BeEmpty();
        var chartSheet = wb.Sheets.Single(sheet => sheet.Name == "Chart2");
        chartSheet.Id.Should().Be(command.CreatedSheetId!.Value);
        chartSheet.Charts.Should().ContainSingle();
        chartSheet.Charts[0].Type.Should().Be(ChartType.Column);
        chartSheet.Charts[0].DataRange.Should().Be(range);

        command.Revert(ctx);

        wb.Sheets.Should().NotContain(sheet => sheet.Name == "Chart2");
        source.Charts.Should().BeEmpty();
    }

    [Fact]
    public void ChangeChartTypeCommand_UpdatesNormalChartAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];

        var command = new ChangeChartTypeCommand(sheet.Id, chart.Id, ChartType.Scatter);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.Type.Should().Be(ChartType.Scatter);
        chart.FirstColIsCategories.Should().BeFalse();

        command.Revert(ctx);

        chart.Type.Should().Be(ChartType.Column);
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void ChangeChartTypeCommand_RejectsPivotCharts()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            PivotTableName = "PivotTable1"
        });

        var outcome = new ChangeChartTypeCommand(sheet.Id, sheet.Charts[0].Id, ChartType.Line).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.Charts[0].Type.Should().Be(ChartType.Column);
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_UpdatesStyleAndFieldButtonsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ChartStyleId = 4,
            ShowPivotChartReportFilterButtons = true,
            ShowPivotChartAxisFieldButtons = true,
            ShowPivotChartValueFieldButtons = true
        };
        sheet.Charts.Add(chart);

        var command = new ConfigurePivotChartOptionsCommand(
            sheet.Id,
            chart.Id,
            99,
            showFieldButtons: false,
            showReportFilterButtons: false,
            showAxisFieldButtons: true,
            showValueFieldButtons: false);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.ChartStyleId.Should().Be(48);
        chart.ShowPivotChartFieldButtons.Should().BeFalse();
        chart.ShowPivotChartReportFilterButtons.Should().BeFalse();
        chart.ShowPivotChartAxisFieldButtons.Should().BeTrue();
        chart.ShowPivotChartValueFieldButtons.Should().BeFalse();

        command.Revert(ctx);

        chart.ChartStyleId.Should().Be(4);
        chart.ShowPivotChartFieldButtons.Should().BeTrue();
        chart.ShowPivotChartReportFilterButtons.Should().BeTrue();
        chart.ShowPivotChartAxisFieldButtons.Should().BeTrue();
        chart.ShowPivotChartValueFieldButtons.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_RejectsNormalCharts()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range
        };
        sheet.Charts.Add(chart);

        var outcome = new ConfigurePivotChartOptionsCommand(sheet.Id, chart.Id, 12, showFieldButtons: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.ChartStyleId.Should().BeNull();
        chart.ShowPivotChartFieldButtons.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_RejectsProtectedSheetWithoutUsePivotReportsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ChartStyleId = 4
        };
        sheet.Charts.Add(chart);
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);

        var outcome = new ConfigurePivotChartOptionsCommand(sheet.Id, chart.Id, 12, showFieldButtons: false).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.ChartStyleId.Should().Be(4);
        chart.ShowPivotChartFieldButtons.Should().BeTrue();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_PreservesIndividualButtonsWhenCallerOmitsThem()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            ShowPivotChartReportFilterButtons = false,
            ShowPivotChartAxisFieldButtons = true,
            ShowPivotChartValueFieldButtons = false
        };
        sheet.Charts.Add(chart);

        var command = new ConfigurePivotChartOptionsCommand(sheet.Id, chart.Id, 12, showFieldButtons: false);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.ShowPivotChartFieldButtons.Should().BeFalse();
        chart.ShowPivotChartReportFilterButtons.Should().BeFalse();
        chart.ShowPivotChartAxisFieldButtons.Should().BeTrue();
        chart.ShowPivotChartValueFieldButtons.Should().BeFalse();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_UpdatesDataTableAndUndoRestores()
    {
        var workbook = new Workbook("PivotChartOptionsDataTableCommandTest");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            DataTable = new ChartDataTableModel { ShowLegendKeys = false }
        };
        sheet.Charts.Add(chart);
        var ctx = new SimpleCtx(workbook);

        var command = new ConfigurePivotChartOptionsCommand(
            sheet.Id,
            chart.Id,
            12,
            showFieldButtons: true,
            showDataTable: true,
            showDataTableLegendKeys: true);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.ShowLegendKeys.Should().BeTrue();
        chart.DataTable.ShowHorizontalBorder.Should().BeTrue();
        chart.DataTable.ShowVerticalBorder.Should().BeTrue();
        chart.DataTable.ShowOutline.Should().BeTrue();

        command.Revert(ctx);

        chart.DataTable.Should().NotBeNull();
        chart.DataTable!.ShowLegendKeys.Should().BeFalse();
    }

    [Fact]
    public void ConfigurePivotChartOptionsCommand_UpdatesDesignFlagsAndUndoRestores()
    {
        var workbook = new Workbook("PivotChartOptionsDesignCommandTest");
        var sheet = workbook.AddSheet("Sheet1");
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            RoundedCorners = false,
            ShowDataInHiddenRowsAndColumns = false,
            BlankDisplayMode = ChartBlankDisplayMode.Gap
        };
        sheet.Charts.Add(chart);
        var ctx = new SimpleCtx(workbook);

        var command = new ConfigurePivotChartOptionsCommand(
            sheet.Id,
            chart.Id,
            12,
            showFieldButtons: true,
            roundedCorners: true,
            showHiddenData: true,
            blankDisplayMode: ChartBlankDisplayMode.Zero);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.RoundedCorners.Should().BeTrue();
        chart.ShowDataInHiddenRowsAndColumns.Should().BeTrue();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Zero);

        command.Revert(ctx);

        chart.RoundedCorners.Should().BeFalse();
        chart.ShowDataInHiddenRowsAndColumns.Should().BeFalse();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Gap);
    }

    [Fact]
    public void SetChartStyleCommand_UpdatesStyleAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            ChartStyleId = 4
        };
        sheet.Charts.Add(chart);

        var command = new SetChartStyleCommand(sheet.Id, chart.Id, 99);

        command.Apply(ctx).Success.Should().BeTrue();
        chart.ChartStyleId.Should().Be(48);

        command.Revert(ctx);
        chart.ChartStyleId.Should().Be(4);
    }

    [Fact]
    public void SetChartStyleCommand_AllowsClearingStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            ChartStyleId = 10
        };
        sheet.Charts.Add(chart);

        new SetChartStyleCommand(sheet.Id, chart.Id, null).Apply(ctx).Success.Should().BeTrue();

        chart.ChartStyleId.Should().BeNull();
    }

    [Fact]
    public void SetChartStyleCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new SetChartStyleCommand(sheet.Id, chart.Id, 5).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.ChartStyleId.Should().BeNull();
    }

    [Fact]
    public void ChangeChartSourceCommand_UpdatesNormalChartSourceAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var originalRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var newRange = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 6, 5));
        new AddChartCommand(sheet.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];

        var command = new ChangeChartSourceCommand(
            sheet.Id,
            chart.Id,
            newRange,
            firstRowIsHeader: false,
            firstColIsCategories: false);

        command.Apply(ctx).Success.Should().BeTrue();

        chart.DataRange.Should().Be(newRange);
        chart.FirstRowIsHeader.Should().BeFalse();
        chart.FirstColIsCategories.Should().BeFalse();

        command.Revert(ctx);

        chart.DataRange.Should().Be(originalRange);
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void ChangeChartSourceCommand_RejectsRangesOnDifferentSheet()
    {
        var wb = new Workbook("test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var ctx = new SimpleCtx(wb);
        var originalRange = new GridRange(
            new CellAddress(sheet1.Id, 1, 1),
            new CellAddress(sheet1.Id, 4, 3));
        var otherSheetRange = new GridRange(
            new CellAddress(sheet2.Id, 1, 1),
            new CellAddress(sheet2.Id, 4, 3));
        new AddChartCommand(sheet1.Id, originalRange, ChartType.Column, "Sales").Apply(ctx);

        var outcome = new ChangeChartSourceCommand(sheet1.Id, sheet1.Charts[0].Id, otherSheetRange).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet1.Charts[0].DataRange.Should().Be(originalRange);
    }

    [Fact]
    public void MoveChartCommand_MovesNormalChartToExistingSheetAndUndoRestores()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var target = wb.AddSheet("Dashboard");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(source.Id, 1, 1),
            new CellAddress(source.Id, 4, 3));
        new AddChartCommand(source.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = source.Charts[0];

        var command = new MoveChartCommand(source.Id, chart.Id, target.Id);

        command.Apply(ctx).Success.Should().BeTrue();

        source.Charts.Should().BeEmpty();
        target.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);

        command.Revert(ctx);

        source.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);
        target.Charts.Should().BeEmpty();
    }

    [Fact]
    public void MoveChartCommand_RejectsProtectedSourceWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var target = wb.AddSheet("Dashboard");
        var ctx = new SimpleCtx(wb);
        var range = CreateChartRange(source);
        new AddChartCommand(source.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = source.Charts[0];
        source.IsProtected = true;

        var outcome = new MoveChartCommand(source.Id, chart.Id, target.Id).Apply(ctx);

        outcome.Success.Should().BeFalse();
        source.Charts.Should().Contain(chart);
        target.Charts.Should().BeEmpty();
    }

    [Fact]
    public void MoveChartToNewSheetCommand_CreatesSheetAndUndoRemovesIt()
    {
        var wb = new Workbook("test");
        var source = wb.AddSheet("Source");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(source.Id, 1, 1),
            new CellAddress(source.Id, 4, 3));
        new AddChartCommand(source.Id, range, ChartType.Line, "Sales").Apply(ctx);
        var chart = source.Charts[0];

        var command = new MoveChartToNewSheetCommand(source.Id, chart.Id, "Sales Chart");

        command.Apply(ctx).Success.Should().BeTrue();

        source.Charts.Should().BeEmpty();
        var chartSheet = wb.Sheets.Single(sheet => sheet.Name == "Sales Chart");
        chartSheet.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);

        command.Revert(ctx);

        wb.Sheets.Should().NotContain(sheet => sheet.Name == "Sales Chart");
        source.Charts.Should().ContainSingle().Which.Id.Should().Be(chart.Id);
    }

    [Fact]
    public void SetChartLayoutCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(Title: "Blocked")).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.Title.Should().Be("Sales");
    }

    [Fact]
    public void SetChartLayoutCommand_UpdatesTitleAxesLegendAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 4));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Old").Apply(ctx);
        var chartId = sheet.Charts[0].Id;

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chartId,
            new ChartLayoutOptions(
                Title: "Revenue",
                XAxisTitle: "Quarter",
                YAxisTitle: "Amount",
                ChartTitleTextColor: new CellColor(31, 78, 121),
                ChartTitleFontSize: 18,
                AxisTitleTextColor: new CellColor(89, 89, 89),
                AxisTitleFontSize: 12,
                ChartAreaFillColor: new CellColor(245, 245, 245),
                PlotAreaFillColor: new CellColor(250, 252, 255),
                PlotAreaBorderColor: new CellColor(120, 120, 120),
                PlotAreaBorderThickness: 2.25,
                LegendTextColor: new CellColor(40, 40, 40),
                LegendFillColor: new CellColor(248, 248, 248),
                LegendBorderColor: new CellColor(180, 180, 180),
                LegendBorderThickness: 1.25,
                LegendFontSize: 11,
                DoughnutHoleSize: 0.72,
                FirstSliceAngle: 135,
                ExplodedSliceIndex: 1,
                ExplodedSliceDistance: 0.18,
                XAxisMinimum: 0,
                XAxisMaximum: 10,
                XAxisMajorUnit: 2,
                XAxisMinorUnit: 1,
                XAxisLogScale: true,
                XAxisNumberFormat: ChartDataLabelNumberFormat.Number,
                ShowXAxisMajorGridlines: true,
                ShowXAxisMinorGridlines: true,
                XAxisMajorGridlineColor: new CellColor(200, 200, 200),
                XAxisMinorGridlineColor: new CellColor(230, 230, 230),
                XAxisGridlineThickness: 1.5,
                XAxisMajorTickStyle: ChartAxisTickStyle.Outside,
                XAxisMinorTickStyle: ChartAxisTickStyle.Inside,
                ShowXAxisLabels: false,
                XAxisLabelTextColor: new CellColor(70, 70, 70),
                XAxisLabelFontSize: 10,
                XAxisLabelAngle: -45,
                XAxisLineColor: new CellColor(10, 20, 30),
                XAxisLineThickness: 2.5,
                YAxisMinimum: -5,
                YAxisMaximum: 25,
                YAxisMajorUnit: 5,
                YAxisMinorUnit: 2.5,
                YAxisLogScale: true,
                YAxisNumberFormat: ChartDataLabelNumberFormat.Currency,
                ShowYAxisMajorGridlines: true,
                ShowYAxisMinorGridlines: true,
                YAxisMajorGridlineColor: new CellColor(190, 190, 190),
                YAxisMinorGridlineColor: new CellColor(225, 225, 225),
                YAxisGridlineThickness: 2,
                YAxisMajorTickStyle: ChartAxisTickStyle.Cross,
                YAxisMinorTickStyle: ChartAxisTickStyle.None,
                ShowYAxisLabels: false,
                YAxisLabelTextColor: new CellColor(80, 80, 80),
                YAxisLabelFontSize: 11,
                YAxisLabelAngle: 90,
                YAxisLineColor: new CellColor(40, 50, 60),
                YAxisLineThickness: 3.5,
                LegendPosition: ChartLegendPosition.Bottom,
                LegendOverlay: true,
                ShowLegend: true,
                ShowDataLabels: true,
                DataLabelPosition: ChartDataLabelPosition.OutsideEnd,
                ShowDataLabelCategoryName: true,
                ShowDataLabelSeriesName: true,
                ShowDataLabelPercentage: true,
                DataLabelSeparator: ChartDataLabelSeparator.NewLine,
                DataLabelNumberFormat: ChartDataLabelNumberFormat.Currency,
                ShowDataLabelCallouts: true,
                DataLabelFillColor: new CellColor(255, 255, 225),
                DataLabelBorderColor: new CellColor(128, 128, 128),
                DataLabelTextColor: new CellColor(30, 30, 30),
                DataLabelBorderThickness: 1.5,
                DataLabelFontSize: 13,
                DataLabelAngle: -35,
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.Power,
                TrendlinePeriod: 3,
                TrendlineOrder: 4,
                ShowTrendlineEquation: true,
                ShowTrendlineRSquared: true,
                TrendlineColor: new CellColor(217, 83, 25),
                TrendlineThickness: 2.5,
                TrendlineDashStyle: ChartLineDashStyle.Solid,
                ShowSecondaryAxis: true,
                SecondaryAxisSeriesIndexes: [1],
                ComboLineSeriesIndexes: [2],
                SeriesFormats:
                [
                    new ChartSeriesFormat(
                        0,
                        FillColor: new CellColor(0, 114, 178),
                        StrokeColor: new CellColor(0, 0, 0),
                        StrokeThickness: 2.5,
                        DashStyle: ChartLineDashStyle.Dot,
                        MarkerStyle: ChartMarkerStyle.Diamond,
                        MarkerSize: 7)
                ],
                UseComboLineForSecondarySeries: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].Title.Should().Be("Revenue");
        sheet.Charts[0].XAxisTitle.Should().Be("Quarter");
        sheet.Charts[0].YAxisTitle.Should().Be("Amount");
        sheet.Charts[0].ChartTitleTextColor.Should().Be(new CellColor(31, 78, 121));
        sheet.Charts[0].ChartTitleFontSize.Should().Be(18);
        sheet.Charts[0].AxisTitleTextColor.Should().Be(new CellColor(89, 89, 89));
        sheet.Charts[0].AxisTitleFontSize.Should().Be(12);
        sheet.Charts[0].ChartAreaFillColor.Should().Be(new CellColor(245, 245, 245));
        sheet.Charts[0].PlotAreaFillColor.Should().Be(new CellColor(250, 252, 255));
        sheet.Charts[0].PlotAreaBorderColor.Should().Be(new CellColor(120, 120, 120));
        sheet.Charts[0].PlotAreaBorderThickness.Should().Be(2.25);
        sheet.Charts[0].LegendTextColor.Should().Be(new CellColor(40, 40, 40));
        sheet.Charts[0].LegendFillColor.Should().Be(new CellColor(248, 248, 248));
        sheet.Charts[0].LegendBorderColor.Should().Be(new CellColor(180, 180, 180));
        sheet.Charts[0].LegendBorderThickness.Should().Be(1.25);
        sheet.Charts[0].LegendFontSize.Should().Be(11);
        sheet.Charts[0].DoughnutHoleSize.Should().Be(0.55);
        sheet.Charts[0].FirstSliceAngle.Should().Be(0);
        sheet.Charts[0].ExplodedSliceIndex.Should().Be(-1);
        sheet.Charts[0].ExplodedSliceDistance.Should().Be(0.1);
        sheet.Charts[0].XAxisMinimum.Should().BeNull();
        sheet.Charts[0].XAxisMaximum.Should().BeNull();
        sheet.Charts[0].XAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].XAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].XAxisLogScale.Should().BeFalse();
        sheet.Charts[0].XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        sheet.Charts[0].ShowXAxisMajorGridlines.Should().BeFalse();
        sheet.Charts[0].ShowXAxisMinorGridlines.Should().BeFalse();
        sheet.Charts[0].XAxisMajorGridlineColor.Should().BeNull();
        sheet.Charts[0].XAxisMinorGridlineColor.Should().BeNull();
        sheet.Charts[0].XAxisGridlineThickness.Should().Be(1);
        sheet.Charts[0].XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        sheet.Charts[0].XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        sheet.Charts[0].ShowXAxisLabels.Should().BeTrue();
        sheet.Charts[0].XAxisLabelTextColor.Should().BeNull();
        sheet.Charts[0].XAxisLabelFontSize.Should().Be(11);
        sheet.Charts[0].XAxisLabelAngle.Should().Be(0);
        sheet.Charts[0].XAxisLineColor.Should().BeNull();
        sheet.Charts[0].XAxisLineThickness.Should().Be(1);
        sheet.Charts[0].YAxisMinimum.Should().Be(-5);
        sheet.Charts[0].YAxisMaximum.Should().Be(25);
        sheet.Charts[0].YAxisMajorUnit.Should().Be(5);
        sheet.Charts[0].YAxisMinorUnit.Should().Be(2.5);
        sheet.Charts[0].YAxisLogScale.Should().BeTrue();
        sheet.Charts[0].YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        sheet.Charts[0].ShowYAxisMajorGridlines.Should().BeTrue();
        sheet.Charts[0].ShowYAxisMinorGridlines.Should().BeTrue();
        sheet.Charts[0].YAxisMajorGridlineColor.Should().Be(new CellColor(190, 190, 190));
        sheet.Charts[0].YAxisMinorGridlineColor.Should().Be(new CellColor(225, 225, 225));
        sheet.Charts[0].YAxisGridlineThickness.Should().Be(2);
        sheet.Charts[0].YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        sheet.Charts[0].YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        sheet.Charts[0].ShowYAxisLabels.Should().BeFalse();
        sheet.Charts[0].YAxisLabelTextColor.Should().Be(new CellColor(80, 80, 80));
        sheet.Charts[0].YAxisLabelFontSize.Should().Be(11);
        sheet.Charts[0].YAxisLabelAngle.Should().Be(90);
        sheet.Charts[0].YAxisLineColor.Should().Be(new CellColor(40, 50, 60));
        sheet.Charts[0].YAxisLineThickness.Should().Be(3.5);
        sheet.Charts[0].LegendPosition.Should().Be(ChartLegendPosition.Bottom);
        sheet.Charts[0].LegendOverlay.Should().BeTrue();
        sheet.Charts[0].ShowLegend.Should().BeTrue();
        sheet.Charts[0].ShowDataLabels.Should().BeTrue();
        sheet.Charts[0].DataLabelPosition.Should().Be(ChartDataLabelPosition.OutsideEnd);
        sheet.Charts[0].ShowDataLabelCategoryName.Should().BeTrue();
        sheet.Charts[0].ShowDataLabelSeriesName.Should().BeTrue();
        sheet.Charts[0].ShowDataLabelPercentage.Should().BeFalse();
        sheet.Charts[0].DataLabelSeparator.Should().Be(ChartDataLabelSeparator.NewLine);
        sheet.Charts[0].DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        sheet.Charts[0].ShowDataLabelCallouts.Should().BeTrue();
        sheet.Charts[0].DataLabelFillColor.Should().Be(new CellColor(255, 255, 225));
        sheet.Charts[0].DataLabelBorderColor.Should().Be(new CellColor(128, 128, 128));
        sheet.Charts[0].DataLabelTextColor.Should().Be(new CellColor(30, 30, 30));
        sheet.Charts[0].DataLabelBorderThickness.Should().Be(1.5);
        sheet.Charts[0].DataLabelFontSize.Should().Be(13);
        sheet.Charts[0].DataLabelAngle.Should().Be(-35);
        sheet.Charts[0].ShowLinearTrendline.Should().BeTrue();
        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.Power);
        sheet.Charts[0].TrendlinePeriod.Should().Be(3);
        sheet.Charts[0].TrendlineOrder.Should().Be(4);
        sheet.Charts[0].ShowTrendlineEquation.Should().BeTrue();
        sheet.Charts[0].ShowTrendlineRSquared.Should().BeTrue();
        sheet.Charts[0].TrendlineColor.Should().Be(new CellColor(217, 83, 25));
        sheet.Charts[0].TrendlineThickness.Should().Be(2.5);
        sheet.Charts[0].TrendlineDashStyle.Should().Be(ChartLineDashStyle.Solid);
        sheet.Charts[0].ShowSecondaryAxis.Should().BeTrue();
        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().Equal(1);
        sheet.Charts[0].ComboLineSeriesIndexes.Should().Equal(2);
        sheet.Charts[0].SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(
                0,
                FillColor: new CellColor(0, 114, 178),
                StrokeColor: new CellColor(0, 0, 0),
                StrokeThickness: 2.5,
                DashStyle: ChartLineDashStyle.Dot));
        sheet.Charts[0].UseComboLineForSecondarySeries.Should().BeTrue();

        command.Revert(ctx);

        sheet.Charts[0].Title.Should().Be("Old");
        sheet.Charts[0].XAxisTitle.Should().BeNull();
        sheet.Charts[0].YAxisTitle.Should().BeNull();
        sheet.Charts[0].ChartTitleTextColor.Should().BeNull();
        sheet.Charts[0].ChartTitleFontSize.Should().Be(16);
        sheet.Charts[0].AxisTitleTextColor.Should().BeNull();
        sheet.Charts[0].AxisTitleFontSize.Should().Be(12);
        sheet.Charts[0].ChartAreaFillColor.Should().BeNull();
        sheet.Charts[0].PlotAreaFillColor.Should().BeNull();
        sheet.Charts[0].PlotAreaBorderColor.Should().BeNull();
        sheet.Charts[0].PlotAreaBorderThickness.Should().Be(1);
        sheet.Charts[0].LegendTextColor.Should().BeNull();
        sheet.Charts[0].LegendFillColor.Should().BeNull();
        sheet.Charts[0].LegendBorderColor.Should().BeNull();
        sheet.Charts[0].LegendBorderThickness.Should().Be(0);
        sheet.Charts[0].LegendFontSize.Should().Be(12);
        sheet.Charts[0].DoughnutHoleSize.Should().Be(0.55);
        sheet.Charts[0].FirstSliceAngle.Should().Be(0);
        sheet.Charts[0].ExplodedSliceIndex.Should().Be(-1);
        sheet.Charts[0].ExplodedSliceDistance.Should().Be(0.1);
        sheet.Charts[0].XAxisMinimum.Should().BeNull();
        sheet.Charts[0].XAxisMaximum.Should().BeNull();
        sheet.Charts[0].XAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].XAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].XAxisLogScale.Should().BeFalse();
        sheet.Charts[0].XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        sheet.Charts[0].ShowXAxisMajorGridlines.Should().BeFalse();
        sheet.Charts[0].ShowXAxisMinorGridlines.Should().BeFalse();
        sheet.Charts[0].XAxisMajorGridlineColor.Should().BeNull();
        sheet.Charts[0].XAxisMinorGridlineColor.Should().BeNull();
        sheet.Charts[0].XAxisGridlineThickness.Should().Be(1);
        sheet.Charts[0].XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        sheet.Charts[0].XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        sheet.Charts[0].ShowXAxisLabels.Should().BeTrue();
        sheet.Charts[0].XAxisLabelTextColor.Should().BeNull();
        sheet.Charts[0].XAxisLabelFontSize.Should().Be(11);
        sheet.Charts[0].XAxisLabelAngle.Should().Be(0);
        sheet.Charts[0].XAxisLineColor.Should().BeNull();
        sheet.Charts[0].XAxisLineThickness.Should().Be(1);
        sheet.Charts[0].YAxisMinimum.Should().BeNull();
        sheet.Charts[0].YAxisMaximum.Should().BeNull();
        sheet.Charts[0].YAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].YAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].YAxisLogScale.Should().BeFalse();
        sheet.Charts[0].YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        sheet.Charts[0].ShowYAxisMajorGridlines.Should().BeFalse();
        sheet.Charts[0].ShowYAxisMinorGridlines.Should().BeFalse();
        sheet.Charts[0].YAxisMajorGridlineColor.Should().BeNull();
        sheet.Charts[0].YAxisMinorGridlineColor.Should().BeNull();
        sheet.Charts[0].YAxisGridlineThickness.Should().Be(1);
        sheet.Charts[0].YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        sheet.Charts[0].YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        sheet.Charts[0].ShowYAxisLabels.Should().BeTrue();
        sheet.Charts[0].YAxisLabelTextColor.Should().BeNull();
        sheet.Charts[0].YAxisLabelFontSize.Should().Be(11);
        sheet.Charts[0].YAxisLabelAngle.Should().Be(0);
        sheet.Charts[0].YAxisLineColor.Should().BeNull();
        sheet.Charts[0].YAxisLineThickness.Should().Be(1);
        sheet.Charts[0].LegendPosition.Should().Be(ChartLegendPosition.Right);
        sheet.Charts[0].LegendOverlay.Should().BeFalse();
        sheet.Charts[0].ShowLegend.Should().BeTrue();
        sheet.Charts[0].ShowDataLabels.Should().BeFalse();
        sheet.Charts[0].DataLabelPosition.Should().Be(ChartDataLabelPosition.BestFit);
        sheet.Charts[0].ShowDataLabelCategoryName.Should().BeFalse();
        sheet.Charts[0].ShowDataLabelSeriesName.Should().BeFalse();
        sheet.Charts[0].ShowDataLabelPercentage.Should().BeFalse();
        sheet.Charts[0].DataLabelSeparator.Should().Be(ChartDataLabelSeparator.Comma);
        sheet.Charts[0].DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        sheet.Charts[0].ShowDataLabelCallouts.Should().BeFalse();
        sheet.Charts[0].DataLabelFillColor.Should().BeNull();
        sheet.Charts[0].DataLabelBorderColor.Should().BeNull();
        sheet.Charts[0].DataLabelTextColor.Should().BeNull();
        sheet.Charts[0].DataLabelBorderThickness.Should().Be(0);
        sheet.Charts[0].DataLabelFontSize.Should().Be(11);
        sheet.Charts[0].DataLabelAngle.Should().Be(0);
        sheet.Charts[0].ShowLinearTrendline.Should().BeFalse();
        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.Linear);
        sheet.Charts[0].TrendlinePeriod.Should().Be(2);
        sheet.Charts[0].TrendlineOrder.Should().Be(2);
        sheet.Charts[0].ShowTrendlineEquation.Should().BeFalse();
        sheet.Charts[0].ShowTrendlineRSquared.Should().BeFalse();
        sheet.Charts[0].TrendlineColor.Should().BeNull();
        sheet.Charts[0].TrendlineThickness.Should().Be(1.5);
        sheet.Charts[0].TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
        sheet.Charts[0].ShowSecondaryAxis.Should().BeFalse();
        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().BeEmpty();
        sheet.Charts[0].ComboLineSeriesIndexes.Should().BeEmpty();
        sheet.Charts[0].SeriesFormats.Should().BeEmpty();
        sheet.Charts[0].UseComboLineForSecondarySeries.Should().BeFalse();
    }

    [Fact]
    public void SetChartLayoutCommand_RgbColorsClearThemeRefsAndUndoRestoresThem()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column).Apply(ctx);
        var chart = sheet.Charts[0];
        chart.ChartAreaFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1);
        chart.LegendTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1);
        chart.DataLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark2);
        chart.TrendlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(
                ChartAreaFillColor: new CellColor(245, 245, 245),
                LegendTextColor: new CellColor(40, 40, 40),
                DataLabelTextColor: new CellColor(30, 30, 30),
                TrendlineColor: new CellColor(217, 83, 25)));

        command.Apply(ctx).Success.Should().BeTrue();

        chart.ChartAreaFillThemeColor.Should().BeNull();
        chart.LegendTextThemeColor.Should().BeNull();
        chart.DataLabelTextThemeColor.Should().BeNull();
        chart.TrendlineThemeColor.Should().BeNull();

        command.Revert(ctx);

        chart.ChartAreaFillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1));
        chart.LegendTextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1));
        chart.DataLabelTextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark2));
        chart.TrendlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2));
    }

    [Fact]
    public void SetChartLayoutCommand_RejectsMissingChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            Guid.NewGuid(),
            new ChartLayoutOptions(Title: "Revenue"));

        command.Apply(ctx).Success.Should().BeFalse();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsAxisBounds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Scatter, "Sales").Apply(ctx);

        new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                XAxisMinimum: 0,
                XAxisMaximum: 10,
                XAxisMajorUnit: 2,
                XAxisMinorUnit: 1,
                XAxisLogScale: true,
                YAxisMinimum: -5,
                YAxisMaximum: 25,
                YAxisMajorUnit: 5,
                YAxisMinorUnit: 2.5,
                YAxisLogScale: true)).Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(ClearXAxisBounds: true, ClearYAxisBounds: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].XAxisMinimum.Should().BeNull();
        sheet.Charts[0].XAxisMaximum.Should().BeNull();
        sheet.Charts[0].XAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].XAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].XAxisLogScale.Should().BeFalse();
        sheet.Charts[0].YAxisMinimum.Should().BeNull();
        sheet.Charts[0].YAxisMaximum.Should().BeNull();
        sheet.Charts[0].YAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].YAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].YAxisLogScale.Should().BeFalse();

        command.Revert(ctx);

        sheet.Charts[0].XAxisMinimum.Should().Be(0);
        sheet.Charts[0].XAxisMaximum.Should().Be(10);
        sheet.Charts[0].XAxisMajorUnit.Should().Be(2);
        sheet.Charts[0].XAxisMinorUnit.Should().Be(1);
        sheet.Charts[0].XAxisLogScale.Should().BeTrue();
        sheet.Charts[0].YAxisMinimum.Should().Be(-5);
        sheet.Charts[0].YAxisMaximum.Should().Be(25);
        sheet.Charts[0].YAxisMajorUnit.Should().Be(5);
        sheet.Charts[0].YAxisMinorUnit.Should().Be(2.5);
        sheet.Charts[0].YAxisLogScale.Should().BeTrue();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsUnsupportedAxisBounds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Pie, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                XAxisMinimum: 0,
                XAxisMaximum: 10,
                XAxisMajorUnit: 2,
                XAxisMinorUnit: 1,
                XAxisLogScale: true,
                XAxisNumberFormat: ChartDataLabelNumberFormat.Currency,
                ShowXAxisMajorGridlines: true,
                ShowXAxisMinorGridlines: true,
                XAxisMajorGridlineColor: new CellColor(200, 200, 200),
                XAxisMinorGridlineColor: new CellColor(230, 230, 230),
                XAxisGridlineThickness: 1.5,
                XAxisMajorTickStyle: ChartAxisTickStyle.Cross,
                XAxisMinorTickStyle: ChartAxisTickStyle.Inside,
                ShowXAxisLabels: false,
                XAxisLabelTextColor: new CellColor(70, 70, 70),
                XAxisLabelFontSize: 10,
                XAxisLabelAngle: -45,
                XAxisLineColor: new CellColor(10, 20, 30),
                XAxisLineThickness: 2.5,
                YAxisMinimum: -5,
                YAxisMaximum: 25,
                YAxisMajorUnit: 5,
                YAxisMinorUnit: 2.5,
                YAxisLogScale: true,
                YAxisNumberFormat: ChartDataLabelNumberFormat.Percent,
                ShowYAxisMajorGridlines: true,
                ShowYAxisMinorGridlines: true,
                YAxisMajorGridlineColor: new CellColor(190, 190, 190),
                YAxisMinorGridlineColor: new CellColor(225, 225, 225),
                YAxisGridlineThickness: 2,
                YAxisMajorTickStyle: ChartAxisTickStyle.Cross,
                YAxisMinorTickStyle: ChartAxisTickStyle.Inside,
                ShowYAxisLabels: false,
                YAxisLabelTextColor: new CellColor(80, 80, 80),
                YAxisLabelFontSize: 12,
                YAxisLabelAngle: 90,
                YAxisLineColor: new CellColor(40, 50, 60),
                YAxisLineThickness: 3.5));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].XAxisMinimum.Should().BeNull();
        sheet.Charts[0].XAxisMaximum.Should().BeNull();
        sheet.Charts[0].XAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].XAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].XAxisLogScale.Should().BeFalse();
        sheet.Charts[0].XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        sheet.Charts[0].ShowXAxisMajorGridlines.Should().BeFalse();
        sheet.Charts[0].ShowXAxisMinorGridlines.Should().BeFalse();
        sheet.Charts[0].XAxisMajorGridlineColor.Should().BeNull();
        sheet.Charts[0].XAxisMinorGridlineColor.Should().BeNull();
        sheet.Charts[0].XAxisGridlineThickness.Should().Be(1);
        sheet.Charts[0].XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        sheet.Charts[0].XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        sheet.Charts[0].ShowXAxisLabels.Should().BeTrue();
        sheet.Charts[0].XAxisLabelTextColor.Should().BeNull();
        sheet.Charts[0].XAxisLabelFontSize.Should().Be(11);
        sheet.Charts[0].XAxisLabelAngle.Should().Be(0);
        sheet.Charts[0].XAxisLineColor.Should().BeNull();
        sheet.Charts[0].XAxisLineThickness.Should().Be(1);
        sheet.Charts[0].YAxisMinimum.Should().BeNull();
        sheet.Charts[0].YAxisMaximum.Should().BeNull();
        sheet.Charts[0].YAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].YAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].YAxisLogScale.Should().BeFalse();
        sheet.Charts[0].YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        sheet.Charts[0].ShowYAxisMajorGridlines.Should().BeFalse();
        sheet.Charts[0].ShowYAxisMinorGridlines.Should().BeFalse();
        sheet.Charts[0].YAxisMajorGridlineColor.Should().BeNull();
        sheet.Charts[0].YAxisMinorGridlineColor.Should().BeNull();
        sheet.Charts[0].YAxisGridlineThickness.Should().Be(1);
        sheet.Charts[0].YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        sheet.Charts[0].YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        sheet.Charts[0].ShowYAxisLabels.Should().BeTrue();
        sheet.Charts[0].YAxisLabelTextColor.Should().BeNull();
        sheet.Charts[0].YAxisLabelFontSize.Should().Be(11);
        sheet.Charts[0].YAxisLabelAngle.Should().Be(0);
        sheet.Charts[0].YAxisLineColor.Should().BeNull();
        sheet.Charts[0].YAxisLineThickness.Should().Be(1);
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsAxisTitlesWhenChartHasNoAxes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Pie, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                XAxisTitle: "Quarter",
                YAxisTitle: "Amount",
                AxisTitleTextColor: new CellColor(89, 89, 89),
                AxisTitleFontSize: 18));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].XAxisTitle.Should().BeNull();
        sheet.Charts[0].YAxisTitle.Should().BeNull();
        sheet.Charts[0].AxisTitleTextColor.Should().BeNull();
        sheet.Charts[0].AxisTitleFontSize.Should().Be(12);
    }

    [Theory]
    [InlineData(ChartTrendlineType.Linear)]
    [InlineData(ChartTrendlineType.Exponential)]
    [InlineData(ChartTrendlineType.Logarithmic)]
    [InlineData(ChartTrendlineType.Power)]
    [InlineData(ChartTrendlineType.MovingAverage)]
    [InlineData(ChartTrendlineType.Polynomial)]
    public void SetChartLayoutCommand_UpdatesSupportedTrendlineTypes(ChartTrendlineType type)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(ShowLinearTrendline: true, TrendlineType: type));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowLinearTrendline.Should().BeTrue();
        sheet.Charts[0].TrendlineType.Should().Be(type);
    }

    [Fact]
    public void SetChartLayoutCommand_UpdatesMovingAveragePeriod()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 6, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.MovingAverage,
                TrendlinePeriod: 4));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.MovingAverage);
        sheet.Charts[0].TrendlinePeriod.Should().Be(4);
    }

    [Fact]
    public void SetChartLayoutCommand_UpdatesPolynomialOrder()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 6, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.Polynomial,
                TrendlineOrder: 5));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.Polynomial);
        sheet.Charts[0].TrendlineOrder.Should().Be(5);
    }

    [Fact]
    public void SetChartLayoutCommand_UpdatesErrorBarsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(
                ShowErrorBars: true,
                ErrorBarKind: ChartErrorBarKind.Percentage,
                ErrorBarDirection: ChartErrorBarDirection.Plus,
                ErrorBarValue: 12.5,
                ErrorBarEndCaps: false));

        command.Apply(ctx).Success.Should().BeTrue();

        chart.ShowErrorBars.Should().BeTrue();
        chart.ErrorBarKind.Should().Be(ChartErrorBarKind.Percentage);
        chart.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Plus);
        chart.ErrorBarValue.Should().Be(12.5);
        chart.ErrorBarEndCaps.Should().BeFalse();

        command.Revert(ctx);

        chart.ShowErrorBars.Should().BeFalse();
        chart.ErrorBarKind.Should().Be(ChartErrorBarKind.StandardError);
        chart.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Both);
        chart.ErrorBarValue.Should().Be(5);
        chart.ErrorBarEndCaps.Should().BeTrue();
    }

    [Fact]
    public void SetChartLayoutCommand_ClampsErrorBarValueAndDefaultsInvalidEnums()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ErrorBarKind: (ChartErrorBarKind)999,
                ErrorBarDirection: (ChartErrorBarDirection)999,
                ErrorBarValue: double.NaN));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ErrorBarKind.Should().Be(ChartErrorBarKind.StandardError);
        sheet.Charts[0].ErrorBarDirection.Should().Be(ChartErrorBarDirection.Both);
        sheet.Charts[0].ErrorBarValue.Should().Be(0);
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesSecondaryAxisSeriesIndexes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowSecondaryAxis: true,
                SecondaryAxisSeriesIndexes: [-1, 0, 1, 1, 2]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().Equal(1);
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsSecondaryAxisWhenNoSeriesTargetsRemain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowSecondaryAxis: true,
                SecondaryAxisSeriesIndexes: [-1, 0, 2]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowSecondaryAxis.Should().BeFalse();
        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsSecondaryAxisStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Pie, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowSecondaryAxis: true,
                SecondaryAxisSeriesIndexes: [1]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowSecondaryAxis.Should().BeFalse();
        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsTrendlineStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Pie, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.Polynomial,
                TrendlinePeriod: 5,
                TrendlineOrder: 4,
                ShowTrendlineEquation: true,
                ShowTrendlineRSquared: true,
                TrendlineColor: new CellColor(217, 83, 25),
                TrendlineThickness: 2.5,
                TrendlineDashStyle: ChartLineDashStyle.Dot));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowLinearTrendline.Should().BeFalse();
        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.Linear);
        sheet.Charts[0].TrendlinePeriod.Should().Be(2);
        sheet.Charts[0].TrendlineOrder.Should().Be(2);
        sheet.Charts[0].ShowTrendlineEquation.Should().BeFalse();
        sheet.Charts[0].ShowTrendlineRSquared.Should().BeFalse();
        sheet.Charts[0].TrendlineColor.Should().BeNull();
        sheet.Charts[0].TrendlineThemeColor.Should().BeNull();
        sheet.Charts[0].TrendlineThickness.Should().Be(1.5);
        sheet.Charts[0].TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsComboLineOverlayStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                UseComboLineForSecondarySeries: true,
                ComboLineSeriesIndexes: [1]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].UseComboLineForSecondarySeries.Should().BeFalse();
        sheet.Charts[0].ComboLineSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsComboLineOverlayWhenNoSeriesTargetsRemain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                UseComboLineForSecondarySeries: true,
                ComboLineSeriesIndexes: [-1, 0, 2]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].UseComboLineForSecondarySeries.Should().BeFalse();
        sheet.Charts[0].ComboLineSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesSeriesFormatsToExistingSeries()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                SeriesFormats:
                [
                    new ChartSeriesFormat(-1, FillColor: new CellColor(255, 0, 0)),
                    new ChartSeriesFormat(0, FillColor: new CellColor(0, 114, 178)),
                    new ChartSeriesFormat(2, FillColor: new CellColor(255, 192, 0))
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SeriesFormats.Should().ContainSingle().Which.SeriesIndex.Should().Be(0);
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsSeriesMarkerFormattingWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                SeriesFormats:
                [
                    new ChartSeriesFormat(
                        0,
                        FillColor: new CellColor(68, 114, 196),
                        StrokeColor: new CellColor(47, 82, 143),
                        MarkerStyle: ChartMarkerStyle.Diamond,
                        MarkerSize: 8)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SeriesFormats.Should().Equal(
            new ChartSeriesFormat(
                0,
                FillColor: new CellColor(68, 114, 196),
                StrokeColor: new CellColor(47, 82, 143)));
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsPercentageDataLabelStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowDataLabels: true,
                ShowDataLabelPercentage: true,
                ShowDataLabelCategoryName: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowDataLabels.Should().BeTrue();
        sheet.Charts[0].ShowDataLabelCategoryName.Should().BeTrue();
        sheet.Charts[0].ShowDataLabelPercentage.Should().BeFalse();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsPieAndDoughnutStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                DoughnutHoleSize: 0.72,
                FirstSliceAngle: 135,
                ExplodedSliceIndex: 1,
                ExplodedSliceDistance: 0.18));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].DoughnutHoleSize.Should().Be(0.55);
        sheet.Charts[0].FirstSliceAngle.Should().Be(0);
        sheet.Charts[0].ExplodedSliceIndex.Should().Be(-1);
        sheet.Charts[0].ExplodedSliceDistance.Should().Be(0.1);
    }

    [Fact]
    public void SetChartLayoutCommand_PreservesBubbleSeriesFormatsForEveryYAndSizePair()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 5));
        new AddChartCommand(sheet.Id, range, ChartType.Bubble, "Bubble").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                SeriesFormats:
                [
                    new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196)),
                    new ChartSeriesFormat(1, FillColor: new CellColor(112, 173, 71)),
                    new ChartSeriesFormat(2, FillColor: new CellColor(255, 192, 0))
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SeriesFormats.Should().Equal(
            new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196)),
            new ChartSeriesFormat(1, FillColor: new CellColor(112, 173, 71)));
    }

    [Fact]
    public void SetChartLayoutCommand_ClampsSeriesFormatStrokeAndMarkerSizes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                SeriesFormats:
                [
                    new ChartSeriesFormat(0, StrokeThickness: -1, MarkerSize: 99)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, StrokeThickness: 0.5, MarkerSize: 30));
    }

    [Fact]
    public void SetChartLayoutCommand_ReplacesInvalidChartChoicesWithDefaults()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                XAxisNumberFormat: (ChartDataLabelNumberFormat)99,
                XAxisMajorTickStyle: (ChartAxisTickStyle)99,
                XAxisMinorTickStyle: (ChartAxisTickStyle)99,
                YAxisNumberFormat: (ChartDataLabelNumberFormat)99,
                YAxisMajorTickStyle: (ChartAxisTickStyle)99,
                YAxisMinorTickStyle: (ChartAxisTickStyle)99,
                LegendPosition: (ChartLegendPosition)99,
                DataLabelPosition: (ChartDataLabelPosition)99,
                DataLabelSeparator: (ChartDataLabelSeparator)99,
                DataLabelNumberFormat: (ChartDataLabelNumberFormat)99,
                TrendlineType: (ChartTrendlineType)99,
                TrendlineDashStyle: (ChartLineDashStyle)99,
                SeriesFormats:
                [
                    new ChartSeriesFormat(
                        0,
                        DashStyle: (ChartLineDashStyle)99,
                        MarkerStyle: (ChartMarkerStyle)99)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        var chart = sheet.Charts[0];
        chart.XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.LegendPosition.Should().Be(ChartLegendPosition.Right);
        chart.DataLabelPosition.Should().Be(ChartDataLabelPosition.BestFit);
        chart.DataLabelSeparator.Should().Be(ChartDataLabelSeparator.Comma);
        chart.DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.TrendlineType.Should().Be(ChartTrendlineType.Linear);
        chart.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
        chart.SeriesFormats.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesPointDataLabelFormatsToExistingPoints()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                PointDataLabelFormats:
                [
                    new ChartPointDataLabelFormat(-1, 0, FillColor: new CellColor(255, 0, 0)),
                    new ChartPointDataLabelFormat(0, -1, FillColor: new CellColor(255, 0, 0)),
                    new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(0, 114, 178)),
                    new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(112, 48, 160)),
                    new ChartPointDataLabelFormat(1, 2, FillColor: new CellColor(255, 192, 0)),
                    new ChartPointDataLabelFormat(2, 0, FillColor: new CellColor(255, 0, 0))
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].PointDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(112, 48, 160)));
    }

    [Fact]
    public void SetChartLayoutCommand_DropsEmptyPointDataLabelFormats()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                PointDataLabelFormats:
                [
                    new ChartPointDataLabelFormat(0, 0)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].PointDataLabelFormats.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_ClampsPointDataLabelFormatWeightsAndFontSizes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                PointDataLabelFormats:
                [
                    new ChartPointDataLabelFormat(0, 0, BorderThickness: 25, FontSize: 2)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        var format = sheet.Charts[0].PointDataLabelFormats.Should().ContainSingle().Subject;
        format.BorderThickness.Should().Be(10);
        format.FontSize.Should().Be(6);
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesNonFiniteNumericOptions()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ChartTitleFontSize: double.NaN,
                XAxisMinimum: double.NaN,
                XAxisMaximum: double.PositiveInfinity,
                XAxisMajorUnit: double.NaN,
                XAxisGridlineThickness: double.NaN,
                DataLabelFontSize: double.NaN,
                TrendlineThickness: double.NaN,
                SeriesFormats:
                [
                    new ChartSeriesFormat(0, StrokeThickness: double.NaN, MarkerSize: double.PositiveInfinity)
                ],
                PointDataLabelFormats:
                [
                    new ChartPointDataLabelFormat(0, 0, BorderThickness: double.NaN, FontSize: double.NegativeInfinity)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        var chart = sheet.Charts[0];
        chart.ChartTitleFontSize.Should().Be(6);
        chart.XAxisMinimum.Should().BeNull();
        chart.XAxisMaximum.Should().BeNull();
        chart.XAxisMajorUnit.Should().BeNull();
        chart.XAxisGridlineThickness.Should().Be(1);
        chart.DataLabelFontSize.Should().Be(6);
        chart.TrendlineThickness.Should().Be(0.5);
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, StrokeThickness: 0.5));
        chart.PointDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointDataLabelFormat(0, 0, BorderThickness: 0, FontSize: 6));
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesExplodedSliceIndexToExistingDataPoints()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Pie, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(ExplodedSliceIndex: 5, ExplodedSliceDistance: 0.2));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ExplodedSliceIndex.Should().Be(-1);
        sheet.Charts[0].ExplodedSliceDistance.Should().Be(0.2);
    }

    private static GridRange CreateChartRange(Sheet sheet) =>
        new(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
