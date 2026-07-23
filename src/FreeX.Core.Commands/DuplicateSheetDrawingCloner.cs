using FreeX.Core.Formula;
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

        // The DataRange remap above (in CloneChart) only handles the GridRange-typed series
        // range. Verbatim (multi-area/unparsable) series formulas, "value from cells" data-label
        // formulas, and custom error-bar range formulas are stored as raw sheet-qualified text
        // and are copied byte-for-byte by CloneChart, so they still literally say the SOURCE
        // sheet's name. Reuse the same RenameSheetOp-based rewriter that SheetCommands' actual
        // Rename Sheet path uses, so a same-sheet reference on the duplicate now points at the
        // copy sheet — matching Excel's Duplicate Sheet behavior for the GridRange DataRange case.
        if (copy.Charts.Count > 0 && !string.Equals(source.Name, copy.Name, StringComparison.Ordinal))
        {
            var renameOp = new RenameSheetOp(source.Name, copy.Name);
            RowColumnShiftHelpers.RewriteChartVerbatimFormulas(copy, renameOp, copy.Name);
            foreach (var chart in copy.Charts)
                RewriteErrorBarRangeFormulas(chart, renameOp, copy.Name);
        }

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
            copy.Sparklines.Add(CloneSparkline(sparkline, copyId));

        foreach (var control in source.FormControls)
            copy.FormControls.Add(CloneFormControl(control, copyId));
    }

    private static FormControlModel CloneFormControl(FormControlModel control, SheetId copyId) =>
        new()
        {
            Kind = control.Kind,
            Name = control.Name,
            Caption = control.Caption,
            ShapeId = control.ShapeId,
            Anchor = RemapRange(control.Anchor, copyId),
            AnchorOffsets = control.AnchorOffsets,
            // LinkedCell/ListFillRange are copied verbatim (not sheet-rewritten), mirroring how
            // Sheet.Clone leaves cell formulas unrewritten (DuplicateSheetCommand's named-range
            // copy): an unqualified reference (the common case, e.g. "$D$3") implicitly means
            // "this control's own hosting sheet" — see RowColumnShiftHelpers.ShiftFormControlRef —
            // so copying it verbatim onto the duplicate correctly follows the control to the copy,
            // matching Excel's Duplicate Sheet behavior for linked form controls.
            LinkedCell = control.LinkedCell,
            ListFillRange = control.ListFillRange,
            IsChecked = control.IsChecked,
            Value = control.Value,
            Min = control.Min,
            Max = control.Max,
            Increment = control.Increment,
            PageChange = control.PageChange,
            SelectedIndex = control.SelectedIndex,
            SelectedText = control.SelectedText
        };

    private static SparklineModel CloneSparkline(SparklineModel sparkline, SheetId copyId) =>
        new()
        {
            DataRange = RemapRange(sparkline.DataRange, copyId),
            Location = RemapAddress(sparkline.Location, copyId),
            Kind = sparkline.Kind,
            GroupId = sparkline.GroupId,
            ShowMarkers = sparkline.ShowMarkers,
            ShowHighPoint = sparkline.ShowHighPoint,
            ShowLowPoint = sparkline.ShowLowPoint,
            ShowFirstPoint = sparkline.ShowFirstPoint,
            ShowLastPoint = sparkline.ShowLastPoint,
            ShowNegativePoints = sparkline.ShowNegativePoints,
            ShowAxis = sparkline.ShowAxis,
            DisplayHidden = sparkline.DisplayHidden,
            RightToLeft = sparkline.RightToLeft,
            SeriesColor = sparkline.SeriesColor,
            NegativeColor = sparkline.NegativeColor,
            AxisColor = sparkline.AxisColor,
            MarkersColor = sparkline.MarkersColor,
            HighPointColor = sparkline.HighPointColor,
            LowPointColor = sparkline.LowPointColor,
            FirstPointColor = sparkline.FirstPointColor,
            LastPointColor = sparkline.LastPointColor,
            LineWeight = sparkline.LineWeight,
            MinAxisType = sparkline.MinAxisType,
            MaxAxisType = sparkline.MaxAxisType,
            ManualMin = sparkline.ManualMin,
            ManualMax = sparkline.ManualMax,
            DisplayEmptyCellsAs = sparkline.DisplayEmptyCellsAs
        };

    private static TextBoxModel CloneTextBox(TextBoxModel textBox, SheetId copyId) =>
        new()
        {
            Name = textBox.Name,
            Anchor = RemapAddress(textBox.Anchor, copyId),
            AnchorOffsetX = textBox.AnchorOffsetX,
            AnchorOffsetY = textBox.AnchorOffsetY,
            Text = textBox.Text,
            Title = textBox.Title,
            AltText = textBox.AltText,
            Width = textBox.Width,
            Height = textBox.Height,
            RotationDegrees = textBox.RotationDegrees,
            FlipHorizontal = textBox.FlipHorizontal,
            FlipVertical = textBox.FlipVertical,
            IsVisible = textBox.IsVisible,
            HasFill = textBox.HasFill,
            FillColor = textBox.FillColor,
            OutlineColor = textBox.OutlineColor,
            FillThemeColor = textBox.FillThemeColor,
            OutlineThemeColor = textBox.OutlineThemeColor,
            // backlog textbox-6-2: copy the txBody text-formatting fields too -- without this
            // Duplicate Sheet silently stripped a text box's font size/bold/italic/color/alignment
            // even though TextBoxModel now carries them.
            TextFontFamily = textBox.TextFontFamily,
            TextFontSizePoints = textBox.TextFontSizePoints,
            TextBold = textBox.TextBold,
            TextItalic = textBox.TextItalic,
            TextColor = textBox.TextColor,
            TextThemeColor = textBox.TextThemeColor,
            TextHAlign = textBox.TextHAlign,
            TextVAnchor = textBox.TextVAnchor,
            // A source-loaded text box's on-disk part is preserved by keying source drawing parts
            // by sheet NAME (XlsxFileAdapter.SavePostProcessing.GetSourceDrawingPathsBySheet); the
            // duplicate always gets a brand-new sheet name (e.g. "Sheet1 (2)") that is absent from
            // the source package, so no source part is ever mapped to it and the writer's
            // IsSourceLoaded-skipping emission drops it — the text box would be silently dropped on
            // save. Mark the clone as NOT source-loaded so it round-trips through the normal text
            // box writer like any other authored text box, mirroring ClonePicture below.
            IsSourceLoaded = false
        };

    private static DrawingShapeModel CloneDrawingShape(DrawingShapeModel shape, SheetId copyId) =>
        new()
        {
            Name = shape.Name,
            Anchor = RemapAddress(shape.Anchor, copyId),
            AnchorOffsetX = shape.AnchorOffsetX,
            AnchorOffsetY = shape.AnchorOffsetY,
            Kind = shape.Kind,
            Width = shape.Width,
            Height = shape.Height,
            RotationDegrees = shape.RotationDegrees,
            FlipHorizontal = shape.FlipHorizontal,
            FlipVertical = shape.FlipVertical,
            IsVisible = shape.IsVisible,
            HasFill = shape.HasFill,
            Locked = shape.Locked,
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
            // A source-loaded shape's on-disk part is preserved by keying source drawing parts by
            // sheet NAME (XlsxFileAdapter.SavePostProcessing.GetSourceDrawingPathsBySheet); the
            // duplicate always gets a brand-new sheet name (e.g. "Sheet1 (2)") that is absent from
            // the source package, so no source part is ever mapped to it and the writer's
            // IsSourceLoaded-skipping emission drops it — the shape would be silently dropped on
            // save. Mark the clone as NOT source-loaded so it round-trips through the normal shape
            // writer like any other authored shape, mirroring ClonePicture below.
            IsSourceLoaded = false,
            AdjustValues = shape.AdjustValues,
            OutlineWidthPoints = shape.OutlineWidthPoints,
            OutlineHasNoFill = shape.OutlineHasNoFill,
            OutlineDash = shape.OutlineDash,
            HeadArrowhead = shape.HeadArrowhead,
            TailArrowhead = shape.TailArrowhead,
            IsWordArt = shape.IsWordArt,
            WarpPreset = shape.WarpPreset,
            ShapeTextGradientEndColor = shape.ShapeTextGradientEndColor,
            ShapeTextGradientEndThemeColor = shape.ShapeTextGradientEndThemeColor,
            ShapeTextGradientAngle = shape.ShapeTextGradientAngle,
            ShapeTextOutlineColor = shape.ShapeTextOutlineColor,
            ShapeTextOutlineThemeColor = shape.ShapeTextOutlineThemeColor,
            ShapeTextOutlineWidthPoints = shape.ShapeTextOutlineWidthPoints,
            ShapeText = shape.ShapeText,
            ShapeTextFontSizePoints = shape.ShapeTextFontSizePoints,
            ShapeTextBold = shape.ShapeTextBold,
            ShapeTextItalic = shape.ShapeTextItalic,
            ShapeTextUnderline = shape.ShapeTextUnderline,
            ShapeTextColor = shape.ShapeTextColor,
            ShapeTextThemeColor = shape.ShapeTextThemeColor,
            ShapeTextHAlign = shape.ShapeTextHAlign,
            ShapeTextVAnchor = shape.ShapeTextVAnchor,
            ShapeTextWrap = shape.ShapeTextWrap
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
            AnchorOffsetX = picture.AnchorOffsetX,
            AnchorOffsetY = picture.AnchorOffsetY,
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
            // R80-io-drawing-image-5-3: an Insert > Icons/SVG picture keeps a PNG rasterization in
            // ImageBytes as the compatibility fallback but carries the editable vector original in
            // SvgImageBytes. Copying ImageBytes without this would silently downgrade the duplicated
            // picture to a flat PNG, re-introducing the same drop the round-80 fix addressed on the
            // original-picture path. Defensive-copy the array to match ImageBytes above so the
            // duplicate owns its own bytes rather than aliasing the source picture's.
            SvgImageBytes = picture.SvgImageBytes?.ToArray(),
            // R65-io-image-drawing-6-1: for a "Link to File" picture, ImageBytes is null and this
            // r:link external target is the picture's ONLY image reference — dropping it here would
            // leave the duplicate with no image at all.
            LinkedImageTarget = picture.LinkedImageTarget,
            Title = picture.Title,
            AltText = picture.AltText,
            Width = picture.Width,
            Height = picture.Height,
            LockAspectRatio = picture.LockAspectRatio,
            RotationDegrees = picture.RotationDegrees,
            FlipHorizontal = picture.FlipHorizontal,
            FlipVertical = picture.FlipVertical,
            IsVisible = picture.IsVisible,
            CropLeft = picture.CropLeft,
            CropTop = picture.CropTop,
            CropRight = picture.CropRight,
            CropBottom = picture.CropBottom,
            // A source-loaded picture's on-disk part is preserved by keying source drawing parts by
            // sheet NAME (XlsxFileAdapter.SavePostProcessing.GetSourceDrawingPathsBySheet); the
            // duplicate always gets a brand-new name (e.g. "Sheet1 (2)") that is absent from the
            // source package, so no source part is ever mapped to it and the writer's
            // !IsSourceLoaded-only emission (XlsxWorksheetDrawingObjectWriter.IsSupportedPicture)
            // skips it too — the picture would be silently dropped on save. The already-copied raw
            // ImageBytes/ContentType fully support authoring it fresh instead, so mark the clone as
            // NOT source-loaded so it round-trips through the normal picture writer like any other
            // authored picture, matching Excel (the pasted copy is a normal embedded picture).
            IsSourceLoaded = false
        };

        foreach (var cell in picture.Cells)
            copiedPicture.Cells.Add(cell);

        return copiedPicture;
    }

    private static ChartModel CloneChart(ChartModel chart, SheetId sourceSheetId, SheetId copyId) =>
        new()
        {
            Name = chart.Name,
            AltTextTitle = chart.AltTextTitle,
            AltTextDescription = chart.AltTextDescription,
            Type = chart.Type,
            Uses1904DateSystem = chart.Uses1904DateSystem,
            Language = chart.Language,
            ChartStyleId = chart.ChartStyleId,
            ColorMapOverride = chart.ColorMapOverride,
            ExternalData = chart.ExternalData,
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
            ShowPivotChartFieldButtons = chart.ShowPivotChartFieldButtons,
            ShowPivotChartReportFilterButtons = chart.ShowPivotChartReportFilterButtons,
            ShowPivotChartAxisFieldButtons = chart.ShowPivotChartAxisFieldButtons,
            ShowPivotChartValueFieldButtons = chart.ShowPivotChartValueFieldButtons,
            Title = chart.Title,
            TitleLayout = chart.TitleLayout,
            TitleOverlay = chart.TitleOverlay,
            XAxisTitle = chart.XAxisTitle,
            XAxisTitleLayout = chart.XAxisTitleLayout,
            XAxisTitleVerbatimXml = chart.XAxisTitleVerbatimXml,
            XAxisTitleRotation = chart.XAxisTitleRotation,
            YAxisTitle = chart.YAxisTitle,
            YAxisTitleLayout = chart.YAxisTitleLayout,
            YAxisTitleVerbatimXml = chart.YAxisTitleVerbatimXml,
            YAxisTitleRotation = chart.YAxisTitleRotation,
            PlotAreaLayout = chart.PlotAreaLayout,
            LegendLayout = chart.LegendLayout,
            RoundedCorners = chart.RoundedCorners,
            BlankDisplayMode = chart.BlankDisplayMode,
            ShowDataLabelsOverMaximum = chart.ShowDataLabelsOverMaximum,
            AutoTitleDeleted = chart.AutoTitleDeleted,
            ShowDataInHiddenRowsAndColumns = chart.ShowDataInHiddenRowsAndColumns,
            Protection = chart.Protection,
            SeriesInRows = chart.SeriesInRows,
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
            XAxisTitleFontSize = chart.XAxisTitleFontSize,
            XAxisTitleTextColor = chart.XAxisTitleTextColor,
            XAxisTitleTextThemeColor = chart.XAxisTitleTextThemeColor,
            YAxisTitleFontSize = chart.YAxisTitleFontSize,
            YAxisTitleTextColor = chart.YAxisTitleTextColor,
            YAxisTitleTextThemeColor = chart.YAxisTitleTextThemeColor,
            ChartAreaFillColor = chart.ChartAreaFillColor,
            ChartAreaFillThemeColor = chart.ChartAreaFillThemeColor,
            ChartAreaNoFill = chart.ChartAreaNoFill,
            ChartAreaBorderColor = chart.ChartAreaBorderColor,
            ChartAreaBorderThemeColor = chart.ChartAreaBorderThemeColor,
            ChartAreaBorderThickness = chart.ChartAreaBorderThickness,
            ChartAreaNoLine = chart.ChartAreaNoLine,
            PlotAreaFillColor = chart.PlotAreaFillColor,
            PlotAreaFillThemeColor = chart.PlotAreaFillThemeColor,
            PlotAreaNoFill = chart.PlotAreaNoFill,
            PlotAreaBorderColor = chart.PlotAreaBorderColor,
            PlotAreaBorderThemeColor = chart.PlotAreaBorderThemeColor,
            PlotAreaBorderThickness = chart.PlotAreaBorderThickness,
            PlotAreaNoLine = chart.PlotAreaNoLine,
            LegendTextColor = chart.LegendTextColor,
            LegendTextThemeColor = chart.LegendTextThemeColor,
            LegendFillColor = chart.LegendFillColor,
            LegendFillThemeColor = chart.LegendFillThemeColor,
            LegendBorderColor = chart.LegendBorderColor,
            LegendBorderThemeColor = chart.LegendBorderThemeColor,
            LegendBorderThickness = chart.LegendBorderThickness,
            LegendFontSize = chart.LegendFontSize,
            LegendEntries = chart.LegendEntries.ToList(),
            SeriesColumnMappings = chart.SeriesColumnMappings.ToList(),
            SeriesRangeDataLabels = chart.SeriesRangeDataLabels.ToList(),
            VerbatimSeriesFormulas = chart.VerbatimSeriesFormulas?.ToList(),
            EmbeddedSeriesData = chart.EmbeddedSeriesData?.ToList(),
            DoughnutHoleSize = chart.DoughnutHoleSize,
            FirstSliceAngle = chart.FirstSliceAngle,
            ExplodedSliceIndex = chart.ExplodedSliceIndex,
            ExplodedSliceDistance = chart.ExplodedSliceDistance,
            ExplodedSlices = chart.ExplodedSlices.ToList(),
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
            ShowXAxisDisplayUnitLabel = chart.ShowXAxisDisplayUnitLabel,
            ShowYAxisDisplayUnitLabel = chart.ShowYAxisDisplayUnitLabel,
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
            ThreeDView = chart.ThreeDView,
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
            DataLabelSeparatorText = chart.DataLabelSeparatorText,
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
            TrendlineSeriesIndex = chart.TrendlineSeriesIndex,
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
            ErrorBarSeriesIndex = chart.ErrorBarSeriesIndex,
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
            SecondaryAxisTitle = chart.SecondaryAxisTitle,
            SecondaryAxisMinimum = chart.SecondaryAxisMinimum,
            SecondaryAxisMaximum = chart.SecondaryAxisMaximum,
            SecondaryAxisNumberFormat = chart.SecondaryAxisNumberFormat,
            SecondaryAxisNumberFormatCode = chart.SecondaryAxisNumberFormatCode,
            SecondaryAxisNumberFormatSourceLinked = chart.SecondaryAxisNumberFormatSourceLinked,
            SecondaryAxisReverseOrder = chart.SecondaryAxisReverseOrder,
            SecondaryAxisLogScale = chart.SecondaryAxisLogScale,
            SecondaryAxisLogBase = chart.SecondaryAxisLogBase,
            SecondaryAxisMajorTickStyle = chart.SecondaryAxisMajorTickStyle,
            SecondaryAxisMinorTickStyle = chart.SecondaryAxisMinorTickStyle,
            SecondaryAxisCrosses = chart.SecondaryAxisCrosses,
            SecondaryAxisCrossesAt = chart.SecondaryAxisCrossesAt,
            SecondaryAxisCrossBetween = chart.SecondaryAxisCrossBetween,
            SecondaryAxisDisplayUnit = chart.SecondaryAxisDisplayUnit,
            SecondaryAxisCustomDisplayUnit = chart.SecondaryAxisCustomDisplayUnit,
            ShowSecondaryAxisDisplayUnitLabel = chart.ShowSecondaryAxisDisplayUnitLabel,
            ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes.ToList(),
            ComboScatterSeriesIndexes = chart.ComboScatterSeriesIndexes.ToList(),
            SeriesPlotOrder = chart.SeriesPlotOrder.ToList(),
            SeriesFormats = chart.SeriesFormats.ToList(),
            SeriesDataLabelFormats = chart.SeriesDataLabelFormats.ToList(),
            PointDataLabelFormats = chart.PointDataLabelFormats.ToList(),
            RangeDataLabels = chart.RangeDataLabels.ToList(),
            PointFillColors = chart.PointFillColors.ToList(),
            HistogramBinning = chart.HistogramBinning,
            WaterfallTotalPointIndices = chart.WaterfallTotalPointIndices?.ToList(),
            QuartileMethod = chart.QuartileMethod,
            AdditionalPlotGroupDataLabels = chart.AdditionalPlotGroupDataLabels.ToList(),
            AdditionalSeriesErrorBarsXml = chart.AdditionalSeriesErrorBarsXml.ToList(),
            AdditionalSeriesTrendlinesXml = chart.AdditionalSeriesTrendlinesXml.ToList(),
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

    /// <summary>
    /// Rewrites the custom error-bar range formulas (raw <c>&lt;c:f&gt;</c> text, same
    /// verbatim/multi-area-union form as <see cref="ChartModel.VerbatimSeriesFormulas"/>) so a
    /// same-sheet reference travels with the duplicate instead of still pointing at the source
    /// sheet by name.
    /// </summary>
    private static void RewriteErrorBarRangeFormulas(ChartModel chart, RenameSheetOp op, string hostSheetName)
    {
        if (chart.ErrorBarPlusRangeFormula is { } plus)
        {
            var rewritten = RewriteVerbatimRangeText(plus, op, hostSheetName);
            if (rewritten is not null)
                chart.ErrorBarPlusRangeFormula = rewritten;
        }

        if (chart.ErrorBarMinusRangeFormula is { } minus)
        {
            var rewritten = RewriteVerbatimRangeText(minus, op, hostSheetName);
            if (rewritten is not null)
                chart.ErrorBarMinusRangeFormula = rewritten;
        }
    }

    /// <summary>
    /// Rewrites a raw (non-"="-prefixed) <c>&lt;c:f&gt;</c> range formula that may be a
    /// comma-separated multi-area union wrapped in parentheses, e.g.
    /// <c>(Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5)</c>. Mirrors
    /// <c>RowColumnShiftHelpers.RewriteVerbatimFormula</c>'s wrapper handling since that helper is
    /// private to its own file. Returns <see langword="null"/> when nothing changed.
    /// </summary>
    /// <remarks>
    /// Excel writes/accepts multi-area unions where only the FIRST area of the union is
    /// sheet-qualified and later areas omit the sheet name, e.g.
    /// <c>(Sheet1!$A$1:$A$5,$C$1:$C$5)</c> — the unqualified areas implicitly mean "same sheet
    /// as the union's first (or nearest preceding qualified) area", not "current/host sheet".
    /// <see cref="FormulaRewriter.Rewrite"/> only rewrites <see cref="RenameSheetOp"/> references
    /// that already carry an explicit sheet qualifier, so splitting the union and rewriting each
    /// area independently would silently leave later unqualified areas untouched — detached from
    /// the sheet being renamed. To avoid that, each area inherits the nearest preceding explicit
    /// sheet qualifier in the union before being rewritten, and the rewritten qualifier is made
    /// explicit on that area so it travels with the duplicate/rename unambiguously.
    /// </remarks>
    private static string? RewriteVerbatimRangeText(string text, RenameSheetOp op, string hostSheetName)
    {
        var hasPrefix = text.Length > 0 && text[0] == '=';
        var body = hasPrefix ? text[1..] : text;

        var hasParens = body.Length >= 2 && body[0] == '(' && body[^1] == ')';
        if (hasParens)
            body = body[1..^1];

        var areas = SplitOnUnquotedCommas(body);
        var anyChanged = false;
        var rewrittenAreas = new string[areas.Length];
        string? inheritedSheetQualifier = null;
        for (var i = 0; i < areas.Length; i++)
        {
            var area = areas[i];
            var qualifier = TryExtractLeadingSheetQualifier(area);
            if (qualifier is not null)
            {
                inheritedSheetQualifier = qualifier;
            }
            else if (inheritedSheetQualifier is not null)
            {
                // This area has no sheet qualifier of its own; it implicitly belongs to the
                // nearest preceding qualified area's sheet. Make that explicit before rewriting
                // so FormulaRewriter's RenameSheetOp match (which requires a non-null sheet name)
                // can see and rewrite it too.
                area = inheritedSheetQualifier + "!" + area;
            }

            var rewritten = FormulaRewriter.Rewrite(area, op, hostSheetName);
            if (rewritten is not null && rewritten != areas[i])
            {
                rewrittenAreas[i] = rewritten;
                anyChanged = true;
            }
            else
            {
                rewrittenAreas[i] = areas[i];
            }
        }

        if (!anyChanged)
            return null;

        var newBody = string.Join(",", rewrittenAreas);
        if (hasParens)
            newBody = "(" + newBody + ")";
        return hasPrefix ? "=" + newBody : newBody;
    }

    /// <summary>
    /// Detects a leading sheet-name qualifier (<c>Sheet1</c> from <c>Sheet1!...</c>, or the quoted
    /// form <c>'Sheet 1'</c> from <c>'Sheet 1'!...</c>, quotes included) at the start of a single
    /// range/cell reference area and returns it (without the trailing <c>!</c>), or
    /// <see langword="null"/> if the area has no sheet qualifier. Only looks at the very start of
    /// the string since a verbatim area is a bare reference, not a general formula expression.
    /// </summary>
    private static string? TryExtractLeadingSheetQualifier(string area)
    {
        if (area.Length == 0)
            return null;

        if (area[0] == '\'')
        {
            var i = 1;
            while (i < area.Length)
            {
                if (area[i] == '\'')
                {
                    if (i + 1 < area.Length && area[i + 1] == '\'')
                    {
                        i += 2; // escaped quote inside the sheet name
                        continue;
                    }

                    // Closing quote found; must be immediately followed by '!'.
                    return i + 1 < area.Length && area[i + 1] == '!'
                        ? area[..(i + 1)]
                        : null;
                }

                i++;
            }

            return null;
        }

        var bang = area.IndexOf('!');
        if (bang <= 0)
            return null;

        // Bare (unquoted) sheet name: must not itself contain characters that would mean this
        // '!' belongs to something other than a leading sheet qualifier (e.g. a colon inside the
        // prefix would mean this isn't a simple "Sheet!" prefix).
        var candidate = area[..bang];
        return candidate.IndexOfAny([':', '$', '\'']) < 0 ? candidate : null;
    }

    /// <summary>
    /// Splits a comma-separated area-union string on commas that are not inside single-quoted
    /// sheet names (e.g. <c>'Sheet, Name'!A1</c> must not be split on the comma inside the quotes).
    /// Mirrors <c>RowColumnShiftHelpers.SplitOnUnquotedCommas</c>.
    /// </summary>
    private static string[] SplitOnUnquotedCommas(string text)
    {
        var parts = new List<string>();
        var start = 0;
        var inQuote = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\'')
            {
                if (inQuote && i + 1 < text.Length && text[i + 1] == '\'')
                    i++; // skip escaped quote
                else
                    inQuote = !inQuote;
            }
            else if (c == ',' && !inQuote)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts.ToArray();
    }

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
                        UseFirstPageNumber = printSettings.PageSetup.UseFirstPageNumber,
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
