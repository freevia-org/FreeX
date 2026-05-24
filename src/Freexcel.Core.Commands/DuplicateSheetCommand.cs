using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Command to duplicate a worksheet immediately after the source sheet.</summary>
public sealed class DuplicateSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sourceSheetId;
    private readonly string? _requestedName;
    private SheetId? _copySheetId;
    private int _insertIndex;

    public string Label => "Duplicate Sheet";

    public DuplicateSheetCommand(SheetId sourceSheetId, string? name = null)
    {
        _sourceSheetId = sourceSheetId;
        _requestedName = name;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var source = ctx.GetSheet(_sourceSheetId);
        var sourceIndex = ctx.Workbook.Sheets.ToList().FindIndex(s => s.Id == _sourceSheetId);
        if (sourceIndex < 0)
            return new CommandOutcome(false, "Source sheet was not found.");

        var name = _requestedName ?? GenerateCopyName(ctx.Workbook, source.Name);
        var validationError = ctx.Workbook.ValidateSheetName(name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        var copyId = SheetId.New();
        var copy = source.Clone(copyId, name);

        // Copy drawing collections that live only in the Commands layer.
        foreach (var chart in source.Charts)
            copy.Charts.Add(CloneChart(chart, copyId));
        foreach (var textBox in source.TextBoxes)
            copy.TextBoxes.Add(new TextBoxModel
            {
                Name = textBox.Name,
                Anchor = RemapAddress(textBox.Anchor, copyId),
                Text = textBox.Text,
                Width = textBox.Width,
                Height = textBox.Height,
                RotationDegrees = textBox.RotationDegrees,
                IsVisible = textBox.IsVisible,
                FillColor = textBox.FillColor,
                OutlineColor = textBox.OutlineColor,
                Title = textBox.Title,
                AltText = textBox.AltText
            });
        foreach (var shape in source.DrawingShapes)
            copy.DrawingShapes.Add(new DrawingShapeModel
            {
                Name = shape.Name,
                Anchor = RemapAddress(shape.Anchor, copyId),
                Kind = shape.Kind,
                Width = shape.Width,
                Height = shape.Height,
                RotationDegrees = shape.RotationDegrees,
                IsVisible = shape.IsVisible,
                FillColor = shape.FillColor,
                OutlineColor = shape.OutlineColor,
                Title = shape.Title,
                AltText = shape.AltText
            });
        foreach (var picture in source.Pictures)
        {
            var copiedPicture = new PictureModel
            {
                Name = picture.Name,
                Anchor = RemapAddress(picture.Anchor, copyId),
                Kind = picture.Kind,
                SourceRowCount = picture.SourceRowCount,
                SourceColumnCount = picture.SourceColumnCount,
                ImageBytes = picture.ImageBytes?.ToArray(),
                ContentType = picture.ContentType,
                Width = picture.Width,
                Height = picture.Height,
                LockAspectRatio = picture.LockAspectRatio,
                RotationDegrees = picture.RotationDegrees,
                IsVisible = picture.IsVisible,
                Title = picture.Title,
                AltText = picture.AltText
            };
            foreach (var cell in picture.Cells)
                copiedPicture.Cells.Add(cell);
            copy.Pictures.Add(copiedPicture);
        }
        foreach (var sparkline in source.Sparklines)
            copy.Sparklines.Add(new SparklineModel
            {
                DataRange = RemapRange(sparkline.DataRange, copyId),
                Location = RemapAddress(sparkline.Location, copyId),
                Kind = sparkline.Kind
            });

        _insertIndex = sourceIndex + 1;
        _copySheetId = copyId;
        ctx.Workbook.InsertSheet(_insertIndex, copy);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_copySheetId.HasValue)
            ctx.Workbook.RemoveSheet(_copySheetId.Value);
    }

    private static string GenerateCopyName(Workbook workbook, string sourceName)
    {
        for (int n = 2; n < 10_000; n++)
        {
            var suffix = $" ({n})";
            var baseName = sourceName.Length + suffix.Length <= 31
                ? sourceName
                : sourceName[..(31 - suffix.Length)];
            var candidate = baseName + suffix;
            if (workbook.ValidateSheetName(candidate) is null)
                return candidate;
        }

        return $"Sheet{Guid.NewGuid():N}"[..31];
    }

    private static ChartModel CloneChart(ChartModel chart, SheetId copyId) =>
        new()
        {
            Name = chart.Name,
            Type = chart.Type,
            DataRange = RemapRange(chart.DataRange, copyId),
            IsVisible = chart.IsVisible,
            FirstRowIsHeader = chart.FirstRowIsHeader,
            FirstColIsCategories = chart.FirstColIsCategories,
            IsPivotChart = chart.IsPivotChart,
            PivotSourceSheetName = chart.PivotSourceSheetName,
            PivotTableName = chart.PivotTableName,
            PivotSourceFormatId = chart.PivotSourceFormatId,
            PivotCacheId = chart.PivotCacheId,
            PivotFormatsXml = chart.PivotFormatsXml,
            Title = chart.Title,
            TitleLayout = chart.TitleLayout,
            TitleOverlay = chart.TitleOverlay,
            XAxisTitle = chart.XAxisTitle,
            XAxisTitleLayout = chart.XAxisTitleLayout,
            YAxisTitle = chart.YAxisTitle,
            YAxisTitleLayout = chart.YAxisTitleLayout,
            HideXAxis = chart.HideXAxis,
            HideYAxis = chart.HideYAxis,
            XAxisPosition = chart.XAxisPosition,
            YAxisPosition = chart.YAxisPosition,
            ChartTitleTextColor = chart.ChartTitleTextColor,
            ChartTitleTextThemeColor = chart.ChartTitleTextThemeColor,
            ChartTitleFontSize = chart.ChartTitleFontSize,
            AxisTitleTextColor = chart.AxisTitleTextColor,
            AxisTitleTextThemeColor = chart.AxisTitleTextThemeColor,
            AxisTitleFontSize = chart.AxisTitleFontSize,
            ChartAreaFillColor = chart.ChartAreaFillColor,
            ChartAreaFillThemeColor = chart.ChartAreaFillThemeColor,
            ChartAreaBorderColor = chart.ChartAreaBorderColor,
            ChartAreaBorderThemeColor = chart.ChartAreaBorderThemeColor,
            ChartAreaBorderThickness = chart.ChartAreaBorderThickness,
            PlotAreaFillColor = chart.PlotAreaFillColor,
            PlotAreaFillThemeColor = chart.PlotAreaFillThemeColor,
            PlotAreaBorderColor = chart.PlotAreaBorderColor,
            PlotAreaBorderThemeColor = chart.PlotAreaBorderThemeColor,
            PlotAreaBorderThickness = chart.PlotAreaBorderThickness,
            LegendTextColor = chart.LegendTextColor,
            LegendTextThemeColor = chart.LegendTextThemeColor,
            LegendFillColor = chart.LegendFillColor,
            LegendFillThemeColor = chart.LegendFillThemeColor,
            LegendBorderColor = chart.LegendBorderColor,
            LegendBorderThemeColor = chart.LegendBorderThemeColor,
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
            XAxisLogBase = chart.XAxisLogBase,
            XAxisReverseOrder = chart.XAxisReverseOrder,
            XAxisNumberFormat = chart.XAxisNumberFormat,
            XAxisNumberFormatCode = chart.XAxisNumberFormatCode,
            XAxisNumberFormatSourceLinked = chart.XAxisNumberFormatSourceLinked,
            ShowXAxisMajorGridlines = chart.ShowXAxisMajorGridlines,
            ShowXAxisMinorGridlines = chart.ShowXAxisMinorGridlines,
            XAxisIsDateAxis = chart.XAxisIsDateAxis,
            XAxisMajorGridlineColor = chart.XAxisMajorGridlineColor,
            XAxisMinorGridlineColor = chart.XAxisMinorGridlineColor,
            XAxisGridlineThickness = chart.XAxisGridlineThickness,
            XAxisMajorTickStyle = chart.XAxisMajorTickStyle,
            XAxisMinorTickStyle = chart.XAxisMinorTickStyle,
            ShowXAxisLabels = chart.ShowXAxisLabels,
            XAxisTickLabelPosition = chart.XAxisTickLabelPosition,
            XAxisLabelTextColor = chart.XAxisLabelTextColor,
            XAxisLabelTextThemeColor = chart.XAxisLabelTextThemeColor,
            XAxisLabelFontSize = chart.XAxisLabelFontSize,
            XAxisLabelAngle = chart.XAxisLabelAngle,
            XAxisLabelSkip = chart.XAxisLabelSkip,
            XAxisTickMarkSkip = chart.XAxisTickMarkSkip,
            XAxisLabelOffset = chart.XAxisLabelOffset,
            XAxisNoMultiLevelLabels = chart.XAxisNoMultiLevelLabels,
            XAxisLabelAlignment = chart.XAxisLabelAlignment,
            XAxisBaseTimeUnit = chart.XAxisBaseTimeUnit,
            XAxisMajorTimeUnit = chart.XAxisMajorTimeUnit,
            XAxisMinorTimeUnit = chart.XAxisMinorTimeUnit,
            XAxisLineColor = chart.XAxisLineColor,
            XAxisLineThickness = chart.XAxisLineThickness,
            XAxisCrosses = chart.XAxisCrosses,
            XAxisCrossesAt = chart.XAxisCrossesAt,
            XAxisCrossBetween = chart.XAxisCrossBetween,
            XAxisDisplayUnit = chart.XAxisDisplayUnit,
            XAxisCustomDisplayUnit = chart.XAxisCustomDisplayUnit,
            YAxisMinimum = chart.YAxisMinimum,
            YAxisMaximum = chart.YAxisMaximum,
            YAxisMajorUnit = chart.YAxisMajorUnit,
            YAxisMinorUnit = chart.YAxisMinorUnit,
            YAxisLogScale = chart.YAxisLogScale,
            YAxisLogBase = chart.YAxisLogBase,
            YAxisReverseOrder = chart.YAxisReverseOrder,
            YAxisNumberFormat = chart.YAxisNumberFormat,
            YAxisNumberFormatCode = chart.YAxisNumberFormatCode,
            YAxisNumberFormatSourceLinked = chart.YAxisNumberFormatSourceLinked,
            ShowYAxisMajorGridlines = chart.ShowYAxisMajorGridlines,
            ShowYAxisMinorGridlines = chart.ShowYAxisMinorGridlines,
            YAxisMajorGridlineColor = chart.YAxisMajorGridlineColor,
            YAxisMinorGridlineColor = chart.YAxisMinorGridlineColor,
            YAxisGridlineThickness = chart.YAxisGridlineThickness,
            YAxisMajorTickStyle = chart.YAxisMajorTickStyle,
            YAxisMinorTickStyle = chart.YAxisMinorTickStyle,
            ShowYAxisLabels = chart.ShowYAxisLabels,
            YAxisTickLabelPosition = chart.YAxisTickLabelPosition,
            YAxisLabelTextColor = chart.YAxisLabelTextColor,
            YAxisLabelTextThemeColor = chart.YAxisLabelTextThemeColor,
            YAxisLabelFontSize = chart.YAxisLabelFontSize,
            YAxisLabelAngle = chart.YAxisLabelAngle,
            YAxisLineColor = chart.YAxisLineColor,
            YAxisLineThickness = chart.YAxisLineThickness,
            YAxisCrosses = chart.YAxisCrosses,
            YAxisCrossesAt = chart.YAxisCrossesAt,
            YAxisCrossBetween = chart.YAxisCrossBetween,
            YAxisDisplayUnit = chart.YAxisDisplayUnit,
            YAxisCustomDisplayUnit = chart.YAxisCustomDisplayUnit,
            DataTable = chart.DataTable is null
                ? null
                : new ChartDataTableModel
                {
                    ShowHorizontalBorder = chart.DataTable.ShowHorizontalBorder,
                    ShowVerticalBorder = chart.DataTable.ShowVerticalBorder,
                    ShowOutline = chart.DataTable.ShowOutline,
                    ShowLegendKeys = chart.DataTable.ShowLegendKeys
                },
            FloorFormat = CloneSurfaceFormat(chart.FloorFormat),
            SideWallFormat = CloneSurfaceFormat(chart.SideWallFormat),
            BackWallFormat = CloneSurfaceFormat(chart.BackWallFormat),
            BarGapWidth = chart.BarGapWidth,
            BarOverlap = chart.BarOverlap,
            VaryColorsByPoint = chart.VaryColorsByPoint,
            BubbleScale = chart.BubbleScale,
            ShowNegativeBubbles = chart.ShowNegativeBubbles,
            BubbleSizeRepresents = chart.BubbleSizeRepresents,
            StockSubtype = chart.StockSubtype,
            LegendPosition = chart.LegendPosition,
            LegendOverlay = chart.LegendOverlay,
            ShowLegend = chart.ShowLegend,
            ShowDataLabels = chart.ShowDataLabels,
            DataLabelPosition = chart.DataLabelPosition,
            ShowDataLabelValue = chart.ShowDataLabelValue,
            ShowDataLabelLegendKey = chart.ShowDataLabelLegendKey,
            ShowDataLabelBubbleSize = chart.ShowDataLabelBubbleSize,
            ShowDataLabelCategoryName = chart.ShowDataLabelCategoryName,
            ShowDataLabelSeriesName = chart.ShowDataLabelSeriesName,
            ShowDataLabelPercentage = chart.ShowDataLabelPercentage,
            DataLabelSeparator = chart.DataLabelSeparator,
            DataLabelNumberFormat = chart.DataLabelNumberFormat,
            DataLabelNumberFormatCode = chart.DataLabelNumberFormatCode,
            DataLabelNumberFormatSourceLinked = chart.DataLabelNumberFormatSourceLinked,
            ShowDataLabelCallouts = chart.ShowDataLabelCallouts,
            DataLabelFillColor = chart.DataLabelFillColor,
            DataLabelFillThemeColor = chart.DataLabelFillThemeColor,
            DataLabelBorderColor = chart.DataLabelBorderColor,
            DataLabelBorderThemeColor = chart.DataLabelBorderThemeColor,
            DataLabelTextColor = chart.DataLabelTextColor,
            DataLabelTextThemeColor = chart.DataLabelTextThemeColor,
            DataLabelBorderThickness = chart.DataLabelBorderThickness,
            DataLabelFontSize = chart.DataLabelFontSize,
            DataLabelAngle = chart.DataLabelAngle,
            ShowLinearTrendline = chart.ShowLinearTrendline,
            TrendlineName = chart.TrendlineName,
            TrendlineType = chart.TrendlineType,
            TrendlinePeriod = chart.TrendlinePeriod,
            TrendlineOrder = chart.TrendlineOrder,
            TrendlineForward = chart.TrendlineForward,
            TrendlineBackward = chart.TrendlineBackward,
            TrendlineIntercept = chart.TrendlineIntercept,
            ShowTrendlineEquation = chart.ShowTrendlineEquation,
            ShowTrendlineRSquared = chart.ShowTrendlineRSquared,
            TrendlineLabelNumberFormatCode = chart.TrendlineLabelNumberFormatCode,
            TrendlineLabelNumberFormatSourceLinked = chart.TrendlineLabelNumberFormatSourceLinked,
            TrendlineColor = chart.TrendlineColor,
            TrendlineThemeColor = chart.TrendlineThemeColor,
            TrendlineThickness = chart.TrendlineThickness,
            TrendlineDashStyle = chart.TrendlineDashStyle,
            ShowErrorBars = chart.ShowErrorBars,
            ErrorBarKind = chart.ErrorBarKind,
            ErrorBarAxisDirection = chart.ErrorBarAxisDirection,
            ErrorBarDirection = chart.ErrorBarDirection,
            ErrorBarValue = chart.ErrorBarValue,
            ErrorBarPlusRangeFormula = chart.ErrorBarPlusRangeFormula,
            ErrorBarMinusRangeFormula = chart.ErrorBarMinusRangeFormula,
            ErrorBarPlusRangeCacheXml = chart.ErrorBarPlusRangeCacheXml,
            ErrorBarMinusRangeCacheXml = chart.ErrorBarMinusRangeCacheXml,
            ErrorBarEndCaps = chart.ErrorBarEndCaps,
            ErrorBarColor = chart.ErrorBarColor,
            ErrorBarThemeColor = chart.ErrorBarThemeColor,
            ErrorBarThickness = chart.ErrorBarThickness,
            ErrorBarDashStyle = chart.ErrorBarDashStyle,
            ShowDropLines = chart.ShowDropLines,
            DropLineColor = chart.DropLineColor,
            DropLineThemeColor = chart.DropLineThemeColor,
            DropLineThickness = chart.DropLineThickness,
            DropLineDashStyle = chart.DropLineDashStyle,
            ShowHighLowLines = chart.ShowHighLowLines,
            HighLowLineColor = chart.HighLowLineColor,
            HighLowLineThemeColor = chart.HighLowLineThemeColor,
            HighLowLineThickness = chart.HighLowLineThickness,
            HighLowLineDashStyle = chart.HighLowLineDashStyle,
            ShowSeriesLines = chart.ShowSeriesLines,
            SeriesLineColor = chart.SeriesLineColor,
            SeriesLineThemeColor = chart.SeriesLineThemeColor,
            SeriesLineThickness = chart.SeriesLineThickness,
            SeriesLineDashStyle = chart.SeriesLineDashStyle,
            ShowUpDownBars = chart.ShowUpDownBars,
            UpDownBarGapWidth = chart.UpDownBarGapWidth,
            UpBarFillColor = chart.UpBarFillColor,
            UpBarFillThemeColor = chart.UpBarFillThemeColor,
            UpBarBorderColor = chart.UpBarBorderColor,
            UpBarBorderThemeColor = chart.UpBarBorderThemeColor,
            UpBarBorderThickness = chart.UpBarBorderThickness,
            DownBarFillColor = chart.DownBarFillColor,
            DownBarFillThemeColor = chart.DownBarFillThemeColor,
            DownBarBorderColor = chart.DownBarBorderColor,
            DownBarBorderThemeColor = chart.DownBarBorderThemeColor,
            DownBarBorderThickness = chart.DownBarBorderThickness,
            ShowSecondaryAxis = chart.ShowSecondaryAxis,
            SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes.ToList(),
            ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes.ToList(),
            SeriesFormats = chart.SeriesFormats.ToList(),
            PointDataLabelFormats = chart.PointDataLabelFormats.ToList(),
            UseComboLineForSecondarySeries = chart.UseComboLineForSecondarySeries,
            Left = chart.Left,
            Top = chart.Top,
            Width = chart.Width,
            Height = chart.Height,
            DrawingAnchorKind = chart.DrawingAnchorKind
        };

    private static CellAddress RemapAddress(CellAddress address, SheetId sheetId) =>
        new(sheetId, address.Row, address.Col);

    private static GridRange RemapRange(GridRange range, SheetId sheetId) =>
        new(RemapAddress(range.Start, sheetId), RemapAddress(range.End, sheetId));

    private static GridRange? RemapRange(GridRange? range, SheetId sheetId) =>
        range.HasValue ? RemapRange(range.Value, sheetId) : null;

    private static ChartSurfaceFormatModel? CloneSurfaceFormat(ChartSurfaceFormatModel? format) =>
        format is null
            ? null
            : new ChartSurfaceFormatModel
            {
                FillColor = format.FillColor,
                FillThemeColor = format.FillThemeColor,
                BorderColor = format.BorderColor,
                BorderThemeColor = format.BorderThemeColor,
                BorderThickness = format.BorderThickness
            };
}

