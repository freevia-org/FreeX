using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R44-serialize-clone-completeness-sweep-1..6: DuplicateSheetDrawingCloner.CloneChart's object
/// initializer (used exclusively by Home &gt; Sheet &gt; Duplicate Sheet / "Create a copy") carried
/// only a subset of ChartModel's XLSX/.fxl-round-tripped fields. A batch of settings the user can
/// configure through the chart UI -- Switch Row/Column (SeriesInRows), 3-D rotation (ThreeDView),
/// "Rounded corners", the Chart Styles gallery selection (ChartStyleId), Waterfall "Set as Total"
/// points (WaterfallTotalPointIndices), deleted auto-title (AutoTitleDeleted), axis display-unit
/// captions (ShowXAxisDisplayUnitLabel/ShowYAxisDisplayUnitLabel), and Box &amp; Whisker quartile
/// method (QuartileMethod) -- silently reverted to their defaults on the duplicated chart even
/// though the source chart still had them set. Verifies each named field survives Duplicate Sheet,
/// plus a broad sweep asserting every ChartModel field the cloner is expected to copy.
/// </summary>
public sealed class R44_serialize_clone_completeness_sweep_Tests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    private static Sheet CreateChartSheet(Workbook workbook, out GridRange range)
    {
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        return sheet;
    }

    // R44-serialize-clone-completeness-sweep-1 (the bug case): "Switch Row/Column" must survive
    // Duplicate Sheet, not silently revert to the default column-series layout.
    [Fact]
    public void DuplicateSheet_ChartWithSeriesInRows_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneSeriesInRows");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            SeriesInRows = true
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.SeriesInRows.Should().BeTrue(
            "Switch Row/Column must not be dropped by Duplicate Sheet");
    }

    // R44-serialize-clone-completeness-sweep-2 (the bug case): a custom 3-D rotation/perspective
    // must survive Duplicate Sheet, not revert to Excel-native default view (null).
    [Fact]
    public void DuplicateSheet_ChartWithThreeDView_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneThreeDView");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDColumn,
            DataRange = range,
            ThreeDView = new Chart3DViewModel
            {
                RotationX = 15,
                RotationY = 20,
                HeightPercent = 100,
                DepthPercent = 100,
                RightAngleAxes = true,
                Perspective = 30
            }
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.ThreeDView.Should().NotBeNull(
            "a custom 3-D rotation must not be dropped by Duplicate Sheet");
        copiedChart.ThreeDView!.RotationX.Should().Be(15);
        copiedChart.ThreeDView!.RotationY.Should().Be(20);
        copiedChart.ThreeDView!.Perspective.Should().Be(30);
    }

    // R44-serialize-clone-completeness-sweep-3 (the bug case): "Rounded corners" must survive
    // Duplicate Sheet, not silently revert to square (default false) corners.
    [Fact]
    public void DuplicateSheet_ChartWithRoundedCorners_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneRoundedCorners");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = range,
            RoundedCorners = true
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.RoundedCorners.Should().BeTrue(
            "Rounded corners must not be dropped by Duplicate Sheet");
    }

    // R44-serialize-clone-completeness-sweep-4 (the bug case): the Chart Styles gallery selection
    // must survive Duplicate Sheet, not silently revert to null (no recorded style).
    [Fact]
    public void DuplicateSheet_ChartWithChartStyleId_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneChartStyleId");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            ChartStyleId = 201
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.ChartStyleId.Should().Be(201,
            "the Chart Styles gallery selection must not be dropped by Duplicate Sheet");
    }

    // R44-serialize-clone-completeness-sweep-5 (the bug case): Waterfall "Set as Total" points must
    // survive Duplicate Sheet, not silently revert to null (only-last-point-is-total default).
    [Fact]
    public void DuplicateSheet_WaterfallChartWithTotalPointIndices_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneWaterfallTotals");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Waterfall,
            DataRange = range,
            WaterfallTotalPointIndices = [0, 2, 4]
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.WaterfallTotalPointIndices.Should().BeEquivalentTo([0, 2, 4],
            "user-marked waterfall total points must not be dropped by Duplicate Sheet");
    }

    // R44-serialize-clone-completeness-sweep-6 (the bug case): AutoTitleDeleted,
    // ShowXAxisDisplayUnitLabel/ShowYAxisDisplayUnitLabel and QuartileMethod must all survive
    // Duplicate Sheet, not silently revert to their defaults.
    [Fact]
    public void DuplicateSheet_ChartWithAutoTitleDeletedDisplayUnitsAndQuartileMethod_PreservesOnCopy()
    {
        var workbook = new Workbook("ChartCloneMiscFlags");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.BoxAndWhisker,
            DataRange = range,
            AutoTitleDeleted = true,
            XAxisDisplayUnit = ChartAxisDisplayUnit.Thousands,
            ShowXAxisDisplayUnitLabel = true,
            YAxisDisplayUnit = ChartAxisDisplayUnit.Millions,
            ShowYAxisDisplayUnitLabel = true,
            QuartileMethod = "inclusive"
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.AutoTitleDeleted.Should().BeTrue(
            "a user-deleted auto title must not be dropped by Duplicate Sheet");
        copiedChart.ShowXAxisDisplayUnitLabel.Should().BeTrue(
            "the X axis display-unit caption must not be dropped by Duplicate Sheet");
        copiedChart.ShowYAxisDisplayUnitLabel.Should().BeTrue(
            "the Y axis display-unit caption must not be dropped by Duplicate Sheet");
        copiedChart.QuartileMethod.Should().Be("inclusive",
            "the Box & Whisker quartile method must not be dropped by Duplicate Sheet");
    }

    // Broad sweep (sibling no-regression + completeness case): a chart populating a wide spread of
    // additional ChartModel fields not individually named above -- pivot-chart button visibility,
    // language/1904-date-system metadata, plot-area/legend manual layouts, secondary-axis own
    // orientation/tick/crossing settings, combo-scatter series indexes, series plot order, per-point
    // fill colors, and the verbatim extra error-bar/trendline/plot-group-label XML passthrough lists
    // -- must all still be present (by value or by reference identity for list/record fields) after
    // Duplicate Sheet.
    [Fact]
    public void DuplicateSheet_ChartWithBroadFieldSpread_PreservesAllFields()
    {
        var workbook = new Workbook("ChartCloneBroadSweep");
        var sheet = CreateChartSheet(workbook, out var range);
        var layout = new ChartManualLayoutModel { X = 0.1, Y = 0.2, Width = 0.5, Height = 0.5 };
        var legendLayout = new ChartManualLayoutModel { X = 0.05, Y = 0.9 };
        var pointFill = new ChartPointFillFormat(0, 1, new CellColor(255, 0, 0));
        var comboScatter = new List<int> { 2 };
        var seriesPlotOrder = new List<int> { 1, 0 };
        var extraErrorBars = new ChartSeriesRawXmlEntry(1, "<c:errBars/>");
        var extraTrendlines = new ChartSeriesRawXmlEntry(1, "<c:trendline/>");
        var extraPlotGroupLabels = new ChartPlotGroupDataLabelsXml(1, "<c:dLbls/>");

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            ShowPivotChartFieldButtons = false,
            ShowPivotChartReportFilterButtons = false,
            ShowPivotChartAxisFieldButtons = false,
            ShowPivotChartValueFieldButtons = false,
            Uses1904DateSystem = true,
            Language = "en-US",
            PlotAreaLayout = layout,
            LegendLayout = legendLayout,
            BlankDisplayMode = ChartBlankDisplayMode.Zero,
            ShowDataLabelsOverMaximum = true,
            ShowDataInHiddenRowsAndColumns = true,
            XAxisTitleVerbatimXml = "<c:title>X</c:title>",
            YAxisTitleVerbatimXml = "<c:title>Y</c:title>",
            XAxisTitleFontSize = 14,
            YAxisTitleFontSize = 18,
            SecondaryAxisReverseOrder = true,
            SecondaryAxisLogScale = true,
            SecondaryAxisLogBase = 10,
            SecondaryAxisMajorTickStyle = ChartAxisTickStyle.Inside,
            SecondaryAxisMinorTickStyle = ChartAxisTickStyle.Cross,
            SecondaryAxisCrosses = ChartAxisCrosses.Maximum,
            SecondaryAxisCrossesAt = 5,
            SecondaryAxisCrossBetween = ChartAxisCrossBetween.MidCategory,
            ComboScatterSeriesIndexes = comboScatter,
            SeriesPlotOrder = seriesPlotOrder,
            PointFillColors = [pointFill],
            DataLabelSeparator = ChartDataLabelSeparator.Custom,
            DataLabelSeparatorText = "; ",
            AdditionalPlotGroupDataLabels = [extraPlotGroupLabels],
            AdditionalSeriesErrorBarsXml = [extraErrorBars],
            AdditionalSeriesTrendlinesXml = [extraTrendlines],
            HistogramBinning = new HistogramBinningModel(HistogramBinningMode.BinCount, null, 5),
            ColorMapOverride = new ChartColorMapOverrideModel { UseMasterColorMapping = false },
            ExternalData = new ChartExternalDataModel { RelationshipId = "rId9" },
            Protection = new ChartProtectionModel { Formatting = true }
        };
        sheet.Charts.Add(chart);
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copy = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;

        copy.IsPivotChart.Should().BeTrue();
        copy.ShowPivotChartFieldButtons.Should().BeFalse();
        copy.ShowPivotChartReportFilterButtons.Should().BeFalse();
        copy.ShowPivotChartAxisFieldButtons.Should().BeFalse();
        copy.ShowPivotChartValueFieldButtons.Should().BeFalse();
        copy.Uses1904DateSystem.Should().BeTrue();
        copy.Language.Should().Be("en-US");
        copy.PlotAreaLayout.Should().BeSameAs(layout);
        copy.LegendLayout.Should().BeSameAs(legendLayout);
        copy.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Zero);
        copy.ShowDataLabelsOverMaximum.Should().BeTrue();
        copy.ShowDataInHiddenRowsAndColumns.Should().BeTrue();
        copy.XAxisTitleVerbatimXml.Should().Be("<c:title>X</c:title>");
        copy.YAxisTitleVerbatimXml.Should().Be("<c:title>Y</c:title>");
        copy.XAxisTitleFontSize.Should().Be(14);
        copy.YAxisTitleFontSize.Should().Be(18);
        copy.SecondaryAxisReverseOrder.Should().BeTrue();
        copy.SecondaryAxisLogScale.Should().BeTrue();
        copy.SecondaryAxisLogBase.Should().Be(10);
        copy.SecondaryAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        copy.SecondaryAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        copy.SecondaryAxisCrosses.Should().Be(ChartAxisCrosses.Maximum);
        copy.SecondaryAxisCrossesAt.Should().Be(5);
        copy.SecondaryAxisCrossBetween.Should().Be(ChartAxisCrossBetween.MidCategory);
        copy.ComboScatterSeriesIndexes.Should().BeEquivalentTo(comboScatter);
        copy.SeriesPlotOrder.Should().BeEquivalentTo(seriesPlotOrder);
        copy.PointFillColors.Should().ContainSingle().Which.Should().BeEquivalentTo(pointFill);
        copy.DataLabelSeparator.Should().Be(ChartDataLabelSeparator.Custom);
        copy.DataLabelSeparatorText.Should().Be("; ");
        copy.AdditionalPlotGroupDataLabels.Should().ContainSingle().Which.Should().BeEquivalentTo(extraPlotGroupLabels);
        copy.AdditionalSeriesErrorBarsXml.Should().ContainSingle().Which.Should().BeEquivalentTo(extraErrorBars);
        copy.AdditionalSeriesTrendlinesXml.Should().ContainSingle().Which.Should().BeEquivalentTo(extraTrendlines);
        copy.HistogramBinning.Should().Be(chart.HistogramBinning);
        copy.ColorMapOverride.Should().BeSameAs(chart.ColorMapOverride);
        copy.ExternalData.Should().BeSameAs(chart.ExternalData);
        copy.Protection.Should().BeSameAs(chart.Protection);
    }

    // Sibling no-regression case: a plain chart with none of the sweep fields set must still
    // duplicate cleanly, leaving every new field at its default.
    [Fact]
    public void DuplicateSheet_PlainChart_LeavesNewFieldsAtDefault()
    {
        var workbook = new Workbook("ChartCloneSweepDefaults");
        var sheet = CreateChartSheet(workbook, out var range);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range
        });
        var ctx = new TestCommandContext(workbook);

        new DuplicateSheetCommand(sheet.Id).Apply(ctx).Success.Should().BeTrue();

        var copiedChart = workbook.Sheets[1].Charts.Should().ContainSingle().Subject;
        copiedChart.SeriesInRows.Should().BeFalse();
        copiedChart.ThreeDView.Should().BeNull();
        copiedChart.RoundedCorners.Should().BeFalse();
        copiedChart.ChartStyleId.Should().BeNull();
        copiedChart.WaterfallTotalPointIndices.Should().BeNull();
        copiedChart.AutoTitleDeleted.Should().BeFalse();
        copiedChart.ShowXAxisDisplayUnitLabel.Should().BeFalse();
        copiedChart.ShowYAxisDisplayUnitLabel.Should().BeFalse();
        copiedChart.QuartileMethod.Should().BeNull();
    }
}
