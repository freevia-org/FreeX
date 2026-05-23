using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

public sealed partial class NativeJsonAdapter
{
    private static ChartModel? TryLoadChart(ChartDto? chartDto, SheetId sheetId)
    {
        if (chartDto?.DataRange is null)
            return null;

        try
        {
            var chart = new ChartModel
            {
                Name = chartDto.Name,
                Type = chartDto.Type,
                DataRange = GridRange.Parse(chartDto.DataRange, sheetId),
                IsVisible = chartDto.IsVisible,
                IsPivotChart = chartDto.IsPivotChart,
                PivotSourceSheetName = chartDto.PivotSourceSheetName,
                PivotTableName = chartDto.PivotTableName,
                PivotCacheId = chartDto.PivotCacheId,
                ChartStyleId = chartDto.ChartStyleId,
                PivotFormatsXml = chartDto.PivotFormatsXml,
                Uses1904DateSystem = chartDto.Uses1904DateSystem,
                Language = chartDto.Language,
                ColorMapOverride = chartDto.ColorMapOverride,
                ExternalData = chartDto.ExternalData,
                PlotAreaLayout = chartDto.PlotAreaLayout,
                LegendLayout = chartDto.LegendLayout,
                RoundedCorners = chartDto.RoundedCorners,
                BlankDisplayMode = chartDto.BlankDisplayMode,
                ShowDataLabelsOverMaximum = chartDto.ShowDataLabelsOverMaximum,
                AutoTitleDeleted = chartDto.AutoTitleDeleted,
                ShowDataInHiddenRowsAndColumns = chartDto.ShowDataInHiddenRowsAndColumns,
                Protection = chartDto.Protection,
                PrintSettings = chartDto.PrintSettings,
                ThreeDView = chartDto.ThreeDView,
                ShowPivotChartFieldButtons = chartDto.ShowPivotChartFieldButtons,
                ShowPivotChartReportFilterButtons = chartDto.ShowPivotChartReportFilterButtons,
                ShowPivotChartAxisFieldButtons = chartDto.ShowPivotChartAxisFieldButtons,
                ShowPivotChartValueFieldButtons = chartDto.ShowPivotChartValueFieldButtons,
                FirstRowIsHeader = chartDto.FirstRowIsHeader,
                FirstColIsCategories = chartDto.FirstColIsCategories,
                Title = chartDto.Title,
                XAxisTitle = chartDto.XAxisTitle,
                YAxisTitle = chartDto.YAxisTitle,
                ChartTitleTextColor = chartDto.ChartTitleTextColor,
                ChartTitleFontSize = chartDto.ChartTitleFontSize,
                AxisTitleTextColor = chartDto.AxisTitleTextColor,
                AxisTitleFontSize = chartDto.AxisTitleFontSize,
                ChartAreaFillColor = chartDto.ChartAreaFillColor,
                ChartAreaFillThemeColor = ToThemeColorReference(chartDto.ChartAreaFillThemeColor),
                PlotAreaFillColor = chartDto.PlotAreaFillColor,
                PlotAreaFillThemeColor = ToThemeColorReference(chartDto.PlotAreaFillThemeColor),
                PlotAreaBorderColor = chartDto.PlotAreaBorderColor,
                PlotAreaBorderThemeColor = ToThemeColorReference(chartDto.PlotAreaBorderThemeColor),
                PlotAreaBorderThickness = chartDto.PlotAreaBorderThickness,
                LegendTextColor = chartDto.LegendTextColor,
                LegendTextThemeColor = ToThemeColorReference(chartDto.LegendTextThemeColor),
                LegendFillColor = chartDto.LegendFillColor,
                LegendFillThemeColor = ToThemeColorReference(chartDto.LegendFillThemeColor),
                LegendBorderColor = chartDto.LegendBorderColor,
                LegendBorderThemeColor = ToThemeColorReference(chartDto.LegendBorderThemeColor),
                LegendBorderThickness = chartDto.LegendBorderThickness,
                LegendFontSize = chartDto.LegendFontSize,
                DoughnutHoleSize = chartDto.DoughnutHoleSize,
                FirstSliceAngle = chartDto.FirstSliceAngle,
                ExplodedSliceIndex = chartDto.ExplodedSliceIndex,
                ExplodedSliceDistance = chartDto.ExplodedSliceDistance,
                XAxisMinimum = chartDto.XAxisMinimum,
                XAxisMaximum = chartDto.XAxisMaximum,
                XAxisMajorUnit = chartDto.XAxisMajorUnit,
                XAxisMinorUnit = chartDto.XAxisMinorUnit,
                XAxisLogScale = chartDto.XAxisLogScale,
                XAxisNumberFormat = chartDto.XAxisNumberFormat,
                ShowXAxisMajorGridlines = chartDto.ShowXAxisMajorGridlines,
                ShowXAxisMinorGridlines = chartDto.ShowXAxisMinorGridlines,
                XAxisMajorGridlineColor = chartDto.XAxisMajorGridlineColor,
                XAxisMinorGridlineColor = chartDto.XAxisMinorGridlineColor,
                XAxisGridlineThickness = chartDto.XAxisGridlineThickness,
                XAxisMajorTickStyle = chartDto.XAxisMajorTickStyle,
                XAxisMinorTickStyle = chartDto.XAxisMinorTickStyle,
                ShowXAxisLabels = chartDto.ShowXAxisLabels,
                XAxisLabelTextColor = chartDto.XAxisLabelTextColor,
                XAxisLabelFontSize = chartDto.XAxisLabelFontSize,
                XAxisLabelAngle = chartDto.XAxisLabelAngle,
                XAxisLineColor = chartDto.XAxisLineColor,
                XAxisLineThickness = chartDto.XAxisLineThickness,
                YAxisMinimum = chartDto.YAxisMinimum,
                YAxisMaximum = chartDto.YAxisMaximum,
                YAxisMajorUnit = chartDto.YAxisMajorUnit,
                YAxisMinorUnit = chartDto.YAxisMinorUnit,
                YAxisLogScale = chartDto.YAxisLogScale,
                YAxisNumberFormat = chartDto.YAxisNumberFormat,
                ShowYAxisMajorGridlines = chartDto.ShowYAxisMajorGridlines,
                ShowYAxisMinorGridlines = chartDto.ShowYAxisMinorGridlines,
                YAxisMajorGridlineColor = chartDto.YAxisMajorGridlineColor,
                YAxisMinorGridlineColor = chartDto.YAxisMinorGridlineColor,
                YAxisGridlineThickness = chartDto.YAxisGridlineThickness,
                YAxisMajorTickStyle = chartDto.YAxisMajorTickStyle,
                YAxisMinorTickStyle = chartDto.YAxisMinorTickStyle,
                ShowYAxisLabels = chartDto.ShowYAxisLabels,
                YAxisLabelTextColor = chartDto.YAxisLabelTextColor,
                YAxisLabelFontSize = chartDto.YAxisLabelFontSize,
                YAxisLabelAngle = chartDto.YAxisLabelAngle,
                YAxisLineColor = chartDto.YAxisLineColor,
                YAxisLineThickness = chartDto.YAxisLineThickness,
                DataTable = chartDto.DataTable,
                BarGapWidth = chartDto.BarGapWidth,
                BarOverlap = chartDto.BarOverlap,
                VaryColorsByPoint = chartDto.VaryColorsByPoint,
                StockSubtype = chartDto.StockSubtype,
                LegendPosition = chartDto.LegendPosition,
                LegendOverlay = chartDto.LegendOverlay,
                ShowLegend = chartDto.ShowLegend,
                ShowDataLabels = chartDto.ShowDataLabels,
                DataLabelPosition = chartDto.DataLabelPosition,
                ShowDataLabelCategoryName = chartDto.ShowDataLabelCategoryName,
                ShowDataLabelSeriesName = chartDto.ShowDataLabelSeriesName,
                ShowDataLabelPercentage = chartDto.ShowDataLabelPercentage,
                DataLabelSeparator = chartDto.DataLabelSeparator,
                DataLabelNumberFormat = chartDto.DataLabelNumberFormat,
                ShowDataLabelCallouts = chartDto.ShowDataLabelCallouts,
                DataLabelFillColor = chartDto.DataLabelFillColor,
                DataLabelFillThemeColor = ToThemeColorReference(chartDto.DataLabelFillThemeColor),
                DataLabelBorderColor = chartDto.DataLabelBorderColor,
                DataLabelBorderThemeColor = ToThemeColorReference(chartDto.DataLabelBorderThemeColor),
                DataLabelTextColor = chartDto.DataLabelTextColor,
                DataLabelTextThemeColor = ToThemeColorReference(chartDto.DataLabelTextThemeColor),
                DataLabelBorderThickness = chartDto.DataLabelBorderThickness,
                DataLabelFontSize = chartDto.DataLabelFontSize,
                DataLabelAngle = chartDto.DataLabelAngle,
                ShowLinearTrendline = chartDto.ShowLinearTrendline,
                TrendlineType = chartDto.TrendlineType,
                TrendlinePeriod = chartDto.TrendlinePeriod,
                TrendlineOrder = chartDto.TrendlineOrder,
                ShowTrendlineEquation = chartDto.ShowTrendlineEquation,
                ShowTrendlineRSquared = chartDto.ShowTrendlineRSquared,
                TrendlineColor = chartDto.TrendlineColor,
                TrendlineThemeColor = ToThemeColorReference(chartDto.TrendlineThemeColor),
                TrendlineThickness = chartDto.TrendlineThickness,
                TrendlineDashStyle = chartDto.TrendlineDashStyle,
                ShowErrorBars = chartDto.ShowErrorBars,
                ErrorBarKind = chartDto.ErrorBarKind,
                ErrorBarDirection = chartDto.ErrorBarDirection,
                ErrorBarValue = chartDto.ErrorBarValue,
                ErrorBarEndCaps = chartDto.ErrorBarEndCaps,
                ShowDropLines = chartDto.ShowDropLines,
                ShowHighLowLines = chartDto.ShowHighLowLines,
                ShowUpDownBars = chartDto.ShowUpDownBars,
                ShowSecondaryAxis = chartDto.ShowSecondaryAxis,
                SecondaryAxisSeriesIndexes = chartDto.SecondaryAxisSeriesIndexes ?? [],
                ComboLineSeriesIndexes = chartDto.ComboLineSeriesIndexes ?? [],
                SeriesFormats = chartDto.SeriesFormats ?? [],
                PointDataLabelFormats = chartDto.PointDataLabelFormats ?? [],
                UseComboLineForSecondarySeries = chartDto.UseComboLineForSecondarySeries,
                Left = chartDto.Left,
                Top = chartDto.Top,
                Width = NativeJsonValueSanitizer.PositiveFiniteOrDefault(chartDto.Width, 400),
                Height = NativeJsonValueSanitizer.PositiveFiniteOrDefault(chartDto.Height, 300)
            };
            SanitizeLoadedChart(chart);
            return chart;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static ChartDto ToChartDto(ChartModel chart) => new()
    {
        Type = chart.Type,
        Name = chart.Name,
        DataRange = chart.DataRange.ToString(),
        IsVisible = chart.IsVisible,
        IsPivotChart = chart.IsPivotChart,
        PivotSourceSheetName = chart.PivotSourceSheetName,
        PivotTableName = chart.PivotTableName,
        PivotCacheId = chart.PivotCacheId,
        ChartStyleId = chart.ChartStyleId,
        PivotFormatsXml = chart.PivotFormatsXml,
        Uses1904DateSystem = chart.Uses1904DateSystem,
        Language = chart.Language,
        ColorMapOverride = chart.ColorMapOverride,
        ExternalData = chart.ExternalData,
        PlotAreaLayout = chart.PlotAreaLayout,
        LegendLayout = chart.LegendLayout,
        RoundedCorners = chart.RoundedCorners,
        BlankDisplayMode = chart.BlankDisplayMode,
        ShowDataLabelsOverMaximum = chart.ShowDataLabelsOverMaximum,
        AutoTitleDeleted = chart.AutoTitleDeleted,
        ShowDataInHiddenRowsAndColumns = chart.ShowDataInHiddenRowsAndColumns,
        Protection = chart.Protection,
        PrintSettings = chart.PrintSettings,
        ThreeDView = chart.ThreeDView,
        ShowPivotChartFieldButtons = chart.ShowPivotChartFieldButtons,
        ShowPivotChartReportFilterButtons = chart.ShowPivotChartReportFilterButtons,
        ShowPivotChartAxisFieldButtons = chart.ShowPivotChartAxisFieldButtons,
        ShowPivotChartValueFieldButtons = chart.ShowPivotChartValueFieldButtons,
        FirstRowIsHeader = chart.FirstRowIsHeader,
        FirstColIsCategories = chart.FirstColIsCategories,
        Title = chart.Title,
        XAxisTitle = chart.XAxisTitle,
        YAxisTitle = chart.YAxisTitle,
        ChartTitleTextColor = chart.ChartTitleTextColor,
        ChartTitleFontSize = chart.ChartTitleFontSize,
        AxisTitleTextColor = chart.AxisTitleTextColor,
        AxisTitleFontSize = chart.AxisTitleFontSize,
        ChartAreaFillColor = chart.ChartAreaFillColor,
        ChartAreaFillThemeColor = FromThemeColorReference(chart.ChartAreaFillThemeColor),
        PlotAreaFillColor = chart.PlotAreaFillColor,
        PlotAreaFillThemeColor = FromThemeColorReference(chart.PlotAreaFillThemeColor),
        PlotAreaBorderColor = chart.PlotAreaBorderColor,
        PlotAreaBorderThemeColor = FromThemeColorReference(chart.PlotAreaBorderThemeColor),
        PlotAreaBorderThickness = chart.PlotAreaBorderThickness,
        LegendTextColor = chart.LegendTextColor,
        LegendTextThemeColor = FromThemeColorReference(chart.LegendTextThemeColor),
        LegendFillColor = chart.LegendFillColor,
        LegendFillThemeColor = FromThemeColorReference(chart.LegendFillThemeColor),
        LegendBorderColor = chart.LegendBorderColor,
        LegendBorderThemeColor = FromThemeColorReference(chart.LegendBorderThemeColor),
        LegendBorderThickness = chart.LegendBorderThickness,
        LegendFontSize = chart.LegendFontSize,
        DoughnutHoleSize = chart.DoughnutHoleSize,
        FirstSliceAngle = chart.FirstSliceAngle,
        ExplodedSliceIndex = chart.ExplodedSliceIndex,
        ExplodedSliceDistance = chart.ExplodedSliceDistance,
        XAxisMinimum = chart.XAxisMinimum,
        XAxisMaximum = chart.XAxisMaximum,
        XAxisMajorUnit = chart.XAxisMajorUnit,
        XAxisMinorUnit = chart.XAxisMinorUnit,
        XAxisLogScale = chart.XAxisLogScale,
        XAxisNumberFormat = chart.XAxisNumberFormat,
        ShowXAxisMajorGridlines = chart.ShowXAxisMajorGridlines,
        ShowXAxisMinorGridlines = chart.ShowXAxisMinorGridlines,
        XAxisMajorGridlineColor = chart.XAxisMajorGridlineColor,
        XAxisMinorGridlineColor = chart.XAxisMinorGridlineColor,
        XAxisGridlineThickness = chart.XAxisGridlineThickness,
        XAxisMajorTickStyle = chart.XAxisMajorTickStyle,
        XAxisMinorTickStyle = chart.XAxisMinorTickStyle,
        ShowXAxisLabels = chart.ShowXAxisLabels,
        XAxisLabelTextColor = chart.XAxisLabelTextColor,
        XAxisLabelFontSize = chart.XAxisLabelFontSize,
        XAxisLabelAngle = chart.XAxisLabelAngle,
        XAxisLineColor = chart.XAxisLineColor,
        XAxisLineThickness = chart.XAxisLineThickness,
        YAxisMinimum = chart.YAxisMinimum,
        YAxisMaximum = chart.YAxisMaximum,
        YAxisMajorUnit = chart.YAxisMajorUnit,
        YAxisMinorUnit = chart.YAxisMinorUnit,
        YAxisLogScale = chart.YAxisLogScale,
        YAxisNumberFormat = chart.YAxisNumberFormat,
        ShowYAxisMajorGridlines = chart.ShowYAxisMajorGridlines,
        ShowYAxisMinorGridlines = chart.ShowYAxisMinorGridlines,
        YAxisMajorGridlineColor = chart.YAxisMajorGridlineColor,
        YAxisMinorGridlineColor = chart.YAxisMinorGridlineColor,
        YAxisGridlineThickness = chart.YAxisGridlineThickness,
        YAxisMajorTickStyle = chart.YAxisMajorTickStyle,
        YAxisMinorTickStyle = chart.YAxisMinorTickStyle,
        ShowYAxisLabels = chart.ShowYAxisLabels,
        YAxisLabelTextColor = chart.YAxisLabelTextColor,
        YAxisLabelFontSize = chart.YAxisLabelFontSize,
        YAxisLabelAngle = chart.YAxisLabelAngle,
        YAxisLineColor = chart.YAxisLineColor,
        YAxisLineThickness = chart.YAxisLineThickness,
        DataTable = chart.DataTable,
        BarGapWidth = chart.BarGapWidth,
        BarOverlap = chart.BarOverlap,
        VaryColorsByPoint = chart.VaryColorsByPoint,
        StockSubtype = chart.StockSubtype,
        LegendPosition = chart.LegendPosition,
        LegendOverlay = chart.LegendOverlay,
        ShowLegend = chart.ShowLegend,
        ShowDataLabels = chart.ShowDataLabels,
        DataLabelPosition = chart.DataLabelPosition,
        ShowDataLabelCategoryName = chart.ShowDataLabelCategoryName,
        ShowDataLabelSeriesName = chart.ShowDataLabelSeriesName,
        ShowDataLabelPercentage = chart.ShowDataLabelPercentage,
        DataLabelSeparator = chart.DataLabelSeparator,
        DataLabelNumberFormat = chart.DataLabelNumberFormat,
        ShowDataLabelCallouts = chart.ShowDataLabelCallouts,
        DataLabelFillColor = chart.DataLabelFillColor,
        DataLabelFillThemeColor = FromThemeColorReference(chart.DataLabelFillThemeColor),
        DataLabelBorderColor = chart.DataLabelBorderColor,
        DataLabelBorderThemeColor = FromThemeColorReference(chart.DataLabelBorderThemeColor),
        DataLabelTextColor = chart.DataLabelTextColor,
        DataLabelTextThemeColor = FromThemeColorReference(chart.DataLabelTextThemeColor),
        DataLabelBorderThickness = chart.DataLabelBorderThickness,
        DataLabelFontSize = chart.DataLabelFontSize,
        DataLabelAngle = chart.DataLabelAngle,
        ShowLinearTrendline = chart.ShowLinearTrendline,
        TrendlineType = chart.TrendlineType,
        TrendlinePeriod = chart.TrendlinePeriod,
        TrendlineOrder = chart.TrendlineOrder,
        ShowTrendlineEquation = chart.ShowTrendlineEquation,
        ShowTrendlineRSquared = chart.ShowTrendlineRSquared,
        TrendlineColor = chart.TrendlineColor,
        TrendlineThemeColor = FromThemeColorReference(chart.TrendlineThemeColor),
        TrendlineThickness = chart.TrendlineThickness,
        TrendlineDashStyle = chart.TrendlineDashStyle,
        ShowErrorBars = chart.ShowErrorBars,
        ErrorBarKind = chart.ErrorBarKind,
        ErrorBarDirection = chart.ErrorBarDirection,
        ErrorBarValue = chart.ErrorBarValue,
        ErrorBarEndCaps = chart.ErrorBarEndCaps,
        ShowDropLines = chart.ShowDropLines,
        ShowHighLowLines = chart.ShowHighLowLines,
        ShowUpDownBars = chart.ShowUpDownBars,
        ShowSecondaryAxis = chart.ShowSecondaryAxis,
        SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes.ToList(),
        ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes.ToList(),
        SeriesFormats = chart.SeriesFormats.ToList(),
        PointDataLabelFormats = chart.PointDataLabelFormats.ToList(),
        UseComboLineForSecondarySeries = chart.UseComboLineForSecondarySeries,
        Left = chart.Left,
        Top = chart.Top,
        Width = chart.Width,
        Height = chart.Height
    };

    private static void SanitizeLoadedChart(ChartModel chart)
    {
        chart.Type = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.Type, ChartType.Column);
        chart.ChartTitleFontSize = Math.Clamp(chart.ChartTitleFontSize, 6, 72);
        chart.AxisTitleFontSize = Math.Clamp(chart.AxisTitleFontSize, 6, 72);
        if (!ChartTypeSupport.SupportsAxes(chart.Type))
        {
            chart.XAxisTitle = null;
            chart.YAxisTitle = null;
            chart.AxisTitleTextColor = null;
            chart.AxisTitleFontSize = 12;
        }
        chart.PlotAreaBorderThickness = Math.Clamp(chart.PlotAreaBorderThickness, 0, 10);
        chart.LegendBorderThickness = Math.Clamp(chart.LegendBorderThickness, 0, 10);
        chart.LegendFontSize = Math.Clamp(chart.LegendFontSize, 6, 72);
        chart.DoughnutHoleSize = Math.Clamp(chart.DoughnutHoleSize, 0.1, 0.9);
        chart.FirstSliceAngle = NormalizeChartAngle(chart.FirstSliceAngle);
        chart.ExplodedSliceDistance = Math.Clamp(chart.ExplodedSliceDistance, 0, 0.5);
        if (!ChartTypeSupport.SupportsDoughnutHoleSize(chart.Type))
            chart.DoughnutHoleSize = 0.55;
        if (!ChartTypeSupport.SupportsFirstSliceAngle(chart.Type))
            chart.FirstSliceAngle = 0;
        if (!ChartTypeSupport.SupportsExplodedSlices(chart.Type))
        {
            chart.ExplodedSliceIndex = -1;
            chart.ExplodedSliceDistance = 0.1;
        }
        chart.XAxisMajorUnit = ClampPositiveAxisUnit(chart.XAxisMajorUnit);
        chart.XAxisMinorUnit = ClampPositiveAxisUnit(chart.XAxisMinorUnit);
        chart.XAxisNumberFormat = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.XAxisNumberFormat, ChartDataLabelNumberFormat.General);
        chart.XAxisGridlineThickness = Math.Clamp(chart.XAxisGridlineThickness, 0.25, 10);
        chart.XAxisMajorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.XAxisMajorTickStyle, ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.XAxisMinorTickStyle, ChartAxisTickStyle.None);
        chart.XAxisLabelFontSize = Math.Clamp(chart.XAxisLabelFontSize, 6, 72);
        chart.XAxisLabelAngle = Math.Clamp(chart.XAxisLabelAngle, -90, 90);
        chart.XAxisLineThickness = Math.Clamp(chart.XAxisLineThickness, 0.5, 10);
        chart.YAxisMajorUnit = ClampPositiveAxisUnit(chart.YAxisMajorUnit);
        chart.YAxisMinorUnit = ClampPositiveAxisUnit(chart.YAxisMinorUnit);
        chart.YAxisNumberFormat = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.YAxisNumberFormat, ChartDataLabelNumberFormat.General);
        chart.YAxisGridlineThickness = Math.Clamp(chart.YAxisGridlineThickness, 0.25, 10);
        chart.YAxisMajorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.YAxisMajorTickStyle, ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.YAxisMinorTickStyle, ChartAxisTickStyle.None);
        chart.YAxisLabelFontSize = Math.Clamp(chart.YAxisLabelFontSize, 6, 72);
        chart.YAxisLabelAngle = Math.Clamp(chart.YAxisLabelAngle, -90, 90);
        chart.YAxisLineThickness = Math.Clamp(chart.YAxisLineThickness, 0.5, 10);
        if (!ChartTypeSupport.SupportsXAxisBounds(chart.Type))
            ClearXAxisBounds(chart);
        if (!ChartTypeSupport.SupportsYAxisBounds(chart.Type))
            ClearYAxisBounds(chart);
        chart.LegendPosition = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.LegendPosition, ChartLegendPosition.Right);
        chart.DataLabelPosition = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DataLabelPosition, ChartDataLabelPosition.BestFit);
        chart.DataLabelSeparator = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DataLabelSeparator, ChartDataLabelSeparator.Comma);
        chart.DataLabelNumberFormat = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.DataLabelNumberFormat, ChartDataLabelNumberFormat.General);
        chart.StockSubtype = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.StockSubtype, StockChartSubtype.HighLowClose);
        if (chart.Type != ChartType.Stock)
            chart.StockSubtype = StockChartSubtype.HighLowClose;
        if (chart.ThreeDView is { } threeDView)
        {
            threeDView.RotationX = ClampNullableInt(threeDView.RotationX, -90, 90);
            threeDView.HeightPercent = ClampNullableInt(threeDView.HeightPercent, 5, 500);
            threeDView.RotationY = ClampNullableInt(threeDView.RotationY, 0, 360);
            threeDView.DepthPercent = ClampNullableInt(threeDView.DepthPercent, 20, 2000);
            threeDView.Perspective = ClampNullableInt(threeDView.Perspective, 0, 240);
            if (threeDView.RotationX is null
                && threeDView.HeightPercent is null
                && threeDView.RotationY is null
                && threeDView.DepthPercent is null
                && threeDView.RightAngleAxes is null
                && threeDView.Perspective is null)
            {
                chart.ThreeDView = null;
            }
        }
        chart.DataLabelBorderThickness = Math.Clamp(chart.DataLabelBorderThickness, 0, 10);
        chart.DataLabelFontSize = Math.Clamp(chart.DataLabelFontSize, 6, 72);
        chart.DataLabelAngle = Math.Clamp(chart.DataLabelAngle, -90, 90);
        if (!ChartTypeSupport.SupportsPercentageDataLabels(chart.Type))
            chart.ShowDataLabelPercentage = false;
        chart.TrendlineType = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.TrendlineType, ChartTrendlineType.Linear);
        chart.TrendlinePeriod = Math.Max(2, chart.TrendlinePeriod);
        chart.TrendlineOrder = Math.Clamp(chart.TrendlineOrder, 2, 6);
        chart.TrendlineThickness = Math.Clamp(chart.TrendlineThickness, 0.5, 10);
        chart.TrendlineDashStyle = NativeJsonValueSanitizer.ValidEnumOrDefault(chart.TrendlineDashStyle, ChartLineDashStyle.Dash);
        if (!ChartTypeSupport.SupportsTrendlines(chart.Type))
        {
            chart.ShowLinearTrendline = false;
            chart.TrendlineType = ChartTrendlineType.Linear;
            chart.TrendlinePeriod = 2;
            chart.TrendlineOrder = 2;
            chart.ShowTrendlineEquation = false;
            chart.ShowTrendlineRSquared = false;
            chart.TrendlineColor = null;
            chart.TrendlineThemeColor = null;
            chart.TrendlineThickness = 1.5;
            chart.TrendlineDashStyle = ChartLineDashStyle.Dash;
        }

        var dataPointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.ExplodedSliceIndex < 0 || chart.ExplodedSliceIndex >= dataPointCount)
            chart.ExplodedSliceIndex = -1;

        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        chart.SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();
        if (!ChartTypeSupport.SupportsSecondaryAxis(chart.Type)
            || (chart.ShowSecondaryAxis && chart.SecondaryAxisSeriesIndexes.Count == 0))
        {
            chart.ShowSecondaryAxis = false;
            chart.SecondaryAxisSeriesIndexes = [];
        }
        chart.ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart)
            || (chart.UseComboLineForSecondarySeries && chart.ComboLineSeriesIndexes.Count == 0))
        {
            chart.UseComboLineForSecondarySeries = false;
            chart.ComboLineSeriesIndexes = [];
        }
        chart.SeriesFormats = chart.SeriesFormats
            .Where(format => format.SeriesIndex >= 0 && format.SeriesIndex < seriesCount)
            .GroupBy(format => format.SeriesIndex)
            .Select(group => ClampSeriesFormat(chart.Type, group.Last()))
            .Where(HasSeriesFormatting)
            .OrderBy(format => format.SeriesIndex)
            .ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Where(format => format.SeriesIndex >= 0
                && format.SeriesIndex < seriesCount
                && format.PointIndex >= 0
                && format.PointIndex < dataPointCount)
            .GroupBy(format => (format.SeriesIndex, format.PointIndex))
            .Select(group => ClampPointDataLabelFormat(group.Last()))
            .Where(HasPointDataLabelFormatting)
            .OrderBy(format => format.SeriesIndex)
            .ThenBy(format => format.PointIndex)
            .ToList();
    }

    private static double? ClampPositiveAxisUnit(double? value) =>
        value is null ? null : Math.Max(double.Epsilon, value.Value);

    private static int? ClampNullableInt(int? value, int min, int max) =>
        value is { } intValue ? Math.Clamp(intValue, min, max) : null;

    private static double NormalizeChartAngle(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static void ClearXAxisBounds(ChartModel chart)
    {
        chart.XAxisMinimum = null;
        chart.XAxisMaximum = null;
        chart.XAxisMajorUnit = null;
        chart.XAxisMinorUnit = null;
        chart.XAxisLogScale = false;
        chart.XAxisNumberFormat = ChartDataLabelNumberFormat.General;
        chart.ShowXAxisMajorGridlines = false;
        chart.ShowXAxisMinorGridlines = false;
        chart.XAxisMajorGridlineColor = null;
        chart.XAxisMinorGridlineColor = null;
        chart.XAxisGridlineThickness = 1;
        chart.XAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.XAxisMinorTickStyle = ChartAxisTickStyle.None;
        chart.ShowXAxisLabels = true;
        chart.XAxisLabelTextColor = null;
        chart.XAxisLabelFontSize = 11;
        chart.XAxisLabelAngle = 0;
        chart.XAxisLineColor = null;
        chart.XAxisLineThickness = 1;
    }

    private static void ClearYAxisBounds(ChartModel chart)
    {
        chart.YAxisMinimum = null;
        chart.YAxisMaximum = null;
        chart.YAxisMajorUnit = null;
        chart.YAxisMinorUnit = null;
        chart.YAxisLogScale = false;
        chart.YAxisNumberFormat = ChartDataLabelNumberFormat.General;
        chart.ShowYAxisMajorGridlines = false;
        chart.ShowYAxisMinorGridlines = false;
        chart.YAxisMajorGridlineColor = null;
        chart.YAxisMinorGridlineColor = null;
        chart.YAxisGridlineThickness = 1;
        chart.YAxisMajorTickStyle = ChartAxisTickStyle.Outside;
        chart.YAxisMinorTickStyle = ChartAxisTickStyle.None;
        chart.ShowYAxisLabels = true;
        chart.YAxisLabelTextColor = null;
        chart.YAxisLabelFontSize = 11;
        chart.YAxisLabelAngle = 0;
        chart.YAxisLineColor = null;
        chart.YAxisLineThickness = 1;
    }

    private static ChartSeriesFormat ClampSeriesFormat(ChartType chartType, ChartSeriesFormat format)
    {
        var supportsMarkers = ChartTypeSupport.SupportsSeriesMarkers(chartType);
        return format with
        {
            StrokeThickness = format.StrokeThickness is { } strokeThickness
                ? Math.Clamp(strokeThickness, 0.5, 10)
                : null,
            MarkerSize = supportsMarkers && format.MarkerSize is { } markerSize
                ? Math.Clamp(markerSize, 1, 30)
                : null,
            DashStyle = NativeJsonValueSanitizer.ValidNullableEnumOrNull(format.DashStyle),
            MarkerStyle = supportsMarkers ? NativeJsonValueSanitizer.ValidNullableEnumOrNull(format.MarkerStyle) : null
        };
    }

    private static bool HasSeriesFormatting(ChartSeriesFormat format) =>
        format.FillColor is not null
        || format.StrokeColor is not null
        || format.StrokeThickness is not null
        || format.DashStyle is not null
        || format.MarkerStyle is not null
        || format.MarkerSize is not null
        || format.FillThemeColor is not null
        || format.StrokeThemeColor is not null;

    private static ChartPointDataLabelFormat ClampPointDataLabelFormat(ChartPointDataLabelFormat format) =>
        format with
        {
            BorderThickness = format.BorderThickness is { } borderThickness
                ? Math.Clamp(borderThickness, 0, 10)
                : null,
            FontSize = format.FontSize is { } fontSize
                ? Math.Clamp(fontSize, 6, 72)
                : null
        };

    private static bool HasPointDataLabelFormatting(ChartPointDataLabelFormat format) =>
        format.FillColor is not null
        || format.BorderColor is not null
        || format.BorderThickness is not null
        || format.TextColor is not null
        || format.FontSize is not null
        || format.FillThemeColor is not null
        || format.BorderThemeColor is not null
        || format.TextThemeColor is not null;
}
