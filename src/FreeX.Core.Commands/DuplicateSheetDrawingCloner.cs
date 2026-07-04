using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Clones drawing-layer worksheet content for duplicated sheets.</summary>
internal static class DuplicateSheetDrawingCloner
{
    internal static void CopyDrawingCollections(Sheet source, Sheet copy, SheetId copyId)
    {
        var zOrderIdMap = new Dictionary<DrawingObjectZOrderEntry, DrawingObjectZOrderEntry>();
        foreach (var chart in source.Charts)
            copy.Charts.Add(CloneChart(chart, source.Id, copyId));
        foreach (var textBox in source.TextBoxes)
        {
            var cloned = CloneTextBox(textBox, copyId);
            copy.TextBoxes.Add(cloned);
            zOrderIdMap[new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, textBox.Id)] =
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, cloned.Id);
        }

        foreach (var shape in source.DrawingShapes)
        {
            var cloned = CloneDrawingShape(shape, copyId);
            copy.DrawingShapes.Add(cloned);
            zOrderIdMap[new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id)] =
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, cloned.Id);
        }

        foreach (var picture in source.Pictures)
        {
            var cloned = ClonePicture(picture, source.Id, source.Name, copy.Name, copyId);
            copy.Pictures.Add(cloned);
            zOrderIdMap[new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, picture.Id)] =
                new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, cloned.Id);
        }

        if (source.DrawingObjectZOrder.Count > 0)
        {
            foreach (var entry in DrawingObjectZOrder.GetNormalizedOrder(source))
            {
                if (zOrderIdMap.TryGetValue(entry, out var clonedEntry))
                    copy.DrawingObjectZOrder.Add(clonedEntry);
            }
        }

        foreach (var sparkline in source.Sparklines)
            copy.Sparklines.Add(new SparklineModel
            {
                DataRange = RemapRange(sparkline.DataRange, copyId),
                Location = RemapAddress(sparkline.Location, copyId),
                Kind = sparkline.Kind
            });
    }

    private static TextBoxModel CloneTextBox(TextBoxModel textBox, SheetId copyId) =>
        new()
        {
            Name = textBox.Name,
            Anchor = RemapAddress(textBox.Anchor, copyId),
            Text = textBox.Text,
            Title = textBox.Title,
            AltText = textBox.AltText,
            Width = textBox.Width,
            Height = textBox.Height,
            RotationDegrees = textBox.RotationDegrees,
            IsVisible = textBox.IsVisible,
            HasFill = textBox.HasFill,
            FillColor = textBox.FillColor,
            OutlineColor = textBox.OutlineColor,
            FillThemeColor = textBox.FillThemeColor,
            OutlineThemeColor = textBox.OutlineThemeColor,
            IsSourceLoaded = textBox.IsSourceLoaded
        };

    private static DrawingShapeModel CloneDrawingShape(DrawingShapeModel shape, SheetId copyId) =>
        new()
        {
            Name = shape.Name,
            Anchor = RemapAddress(shape.Anchor, copyId),
            Kind = shape.Kind,
            Width = shape.Width,
            Height = shape.Height,
            RotationDegrees = shape.RotationDegrees,
            IsVisible = shape.IsVisible,
            HasFill = shape.HasFill,
            Title = shape.Title,
            AltText = shape.AltText,
            FillColor = shape.FillColor,
            OutlineColor = shape.OutlineColor,
            GradientFillEndColor = shape.GradientFillEndColor,
            GradientFillDirection = shape.GradientFillDirection,
            FillThemeColor = shape.FillThemeColor,
            OutlineThemeColor = shape.OutlineThemeColor,
            HasShadowEffect = shape.HasShadowEffect,
            EffectPreset = shape.EffectPreset,
            UsesThemeEffects = shape.UsesThemeEffects,
            IsSourceLoaded = shape.IsSourceLoaded
        };

    private static PictureModel ClonePicture(
        PictureModel picture,
        SheetId sourceSheetId,
        string sourceSheetName,
        string copySheetName,
        SheetId copyId)
    {
        var copiedPicture = new PictureModel
        {
            Name = picture.Name,
            Anchor = RemapAddress(picture.Anchor, copyId),
            Kind = picture.Kind,
            SourceRowCount = picture.SourceRowCount,
            SourceColumnCount = picture.SourceColumnCount,
            IsLinkedToSourceRange = picture.IsLinkedToSourceRange,
            LinkedSourceRange = picture.LinkedSourceRange is { } linkedSourceRange &&
                linkedSourceRange.Start.Sheet == sourceSheetId
                    ? RemapRange(linkedSourceRange, copyId)
                    : picture.LinkedSourceRange,
            LinkedSourceSheetName = picture.LinkedSourceSheetName == sourceSheetName
                ? copySheetName
                : picture.LinkedSourceSheetName,
            ImageBytes = picture.ImageBytes?.ToArray(),
            ContentType = picture.ContentType,
            Title = picture.Title,
            AltText = picture.AltText,
            Width = picture.Width,
            Height = picture.Height,
            LockAspectRatio = picture.LockAspectRatio,
            RotationDegrees = picture.RotationDegrees,
            IsVisible = picture.IsVisible,
            CropLeft = picture.CropLeft,
            CropTop = picture.CropTop,
            CropRight = picture.CropRight,
            CropBottom = picture.CropBottom,
            IsSourceLoaded = picture.IsSourceLoaded
        };

        foreach (var cell in picture.Cells)
            copiedPicture.Cells.Add(cell);

        return copiedPicture;
    }

    private static ChartModel CloneChart(ChartModel chart, SheetId sourceSheetId, SheetId copyId) =>
        new()
        {
            Name = chart.Name,
            Type = chart.Type,
            // Only remap the DataRange onto the copy when it actually points at the sheet being
            // duplicated — a cross-sheet DataRange (e.g. a Dashboard chart plotting Data!A1:B10)
            // must keep pointing at the original source sheet, matching Excel's Duplicate Sheet
            // behavior (only same-sheet references travel with the copy).
            DataRange = chart.DataRange.Start.Sheet == sourceSheetId
                ? RemapRange(chart.DataRange, copyId)
                : chart.DataRange,
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
            ChartDefaultTextColor = chart.ChartDefaultTextColor,
            ChartDefaultTextThemeColor = chart.ChartDefaultTextThemeColor,
            ChartDefaultFontSize = chart.ChartDefaultFontSize,
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
            LegendEntries = chart.LegendEntries.ToList(),
            SeriesRangeDataLabels = chart.SeriesRangeDataLabels.ToList(),
            VerbatimSeriesFormulas = chart.VerbatimSeriesFormulas?.ToList(),
            EmbeddedSeriesData = chart.EmbeddedSeriesData?.ToList(),
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
                    ShowLegendKeys = chart.DataTable.ShowLegendKeys,
                    FillColor = chart.DataTable.FillColor,
                    FillThemeColor = chart.DataTable.FillThemeColor,
                    BorderColor = chart.DataTable.BorderColor,
                    BorderThemeColor = chart.DataTable.BorderThemeColor,
                    BorderThickness = chart.DataTable.BorderThickness,
                    TextColor = chart.DataTable.TextColor,
                    TextThemeColor = chart.DataTable.TextThemeColor,
                    FontSize = chart.DataTable.FontSize
            },
            FloorFormat = CloneSurfaceFormat(chart.FloorFormat),
            SideWallFormat = CloneSurfaceFormat(chart.SideWallFormat),
            BackWallFormat = CloneSurfaceFormat(chart.BackWallFormat),
            PrintSettings = ClonePrintSettings(chart.PrintSettings),
            UserShapes = CloneUserShapes(chart.UserShapes),
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
            DataLabelLeaderLineColor = chart.DataLabelLeaderLineColor,
            DataLabelLeaderLineThemeColor = chart.DataLabelLeaderLineThemeColor,
            DataLabelLeaderLineThickness = chart.DataLabelLeaderLineThickness,
            DataLabelLeaderLineDashStyle = chart.DataLabelLeaderLineDashStyle,
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
            TrendlineLabelLayout = chart.TrendlineLabelLayout,
            TrendlineLabelFillColor = chart.TrendlineLabelFillColor,
            TrendlineLabelFillThemeColor = chart.TrendlineLabelFillThemeColor,
            TrendlineLabelBorderColor = chart.TrendlineLabelBorderColor,
            TrendlineLabelBorderThemeColor = chart.TrendlineLabelBorderThemeColor,
            TrendlineLabelBorderThickness = chart.TrendlineLabelBorderThickness,
            TrendlineLabelTextColor = chart.TrendlineLabelTextColor,
            TrendlineLabelTextThemeColor = chart.TrendlineLabelTextThemeColor,
            TrendlineLabelFontSize = chart.TrendlineLabelFontSize,
            TrendlineLabelAngle = chart.TrendlineLabelAngle,
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
            SeriesDataLabelFormats = chart.SeriesDataLabelFormats.ToList(),
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

    private static ChartPrintSettingsModel? ClonePrintSettings(ChartPrintSettingsModel? printSettings) =>
        printSettings is null
            ? null
            : new ChartPrintSettingsModel
            {
                PageMargins = printSettings.PageMargins is null
                    ? null
                    : new ChartPageMarginsModel
                    {
                        Left = printSettings.PageMargins.Left,
                        Right = printSettings.PageMargins.Right,
                        Top = printSettings.PageMargins.Top,
                        Bottom = printSettings.PageMargins.Bottom,
                        Header = printSettings.PageMargins.Header,
                        Footer = printSettings.PageMargins.Footer
                    },
                PageSetup = printSettings.PageSetup is null
                    ? null
                    : new ChartPageSetupModel
                    {
                        PaperSize = printSettings.PageSetup.PaperSize,
                        Orientation = printSettings.PageSetup.Orientation,
                        Copies = printSettings.PageSetup.Copies,
                        UsePrinterDefaults = printSettings.PageSetup.UsePrinterDefaults,
                        FirstPageNumber = printSettings.PageSetup.FirstPageNumber,
                        HorizontalDpi = printSettings.PageSetup.HorizontalDpi,
                        VerticalDpi = printSettings.PageSetup.VerticalDpi,
                        BlackAndWhite = printSettings.PageSetup.BlackAndWhite,
                        Draft = printSettings.PageSetup.Draft
                    },
                HeaderFooter = printSettings.HeaderFooter is null
                    ? null
                    : new ChartHeaderFooterModel
                    {
                        DifferentOddEven = printSettings.HeaderFooter.DifferentOddEven,
                        DifferentFirst = printSettings.HeaderFooter.DifferentFirst,
                        AlignWithMargins = printSettings.HeaderFooter.AlignWithMargins,
                        OddHeader = printSettings.HeaderFooter.OddHeader,
                        OddFooter = printSettings.HeaderFooter.OddFooter,
                        EvenHeader = printSettings.HeaderFooter.EvenHeader,
                        EvenFooter = printSettings.HeaderFooter.EvenFooter,
                        FirstHeader = printSettings.HeaderFooter.FirstHeader,
                        FirstFooter = printSettings.HeaderFooter.FirstFooter
                    }
            };

    private static ChartUserShapesModel? CloneUserShapes(ChartUserShapesModel? userShapes) =>
        userShapes is null
            ? null
            : new ChartUserShapesModel
            {
                RelationshipId = userShapes.RelationshipId,
                RelationshipType = userShapes.RelationshipType,
                Target = userShapes.Target,
                TargetMode = userShapes.TargetMode
            };
}
