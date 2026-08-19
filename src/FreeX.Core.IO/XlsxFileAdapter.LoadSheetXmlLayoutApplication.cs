using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private static void ApplySheetXmlLayout(
        Workbook workbook,
        Sheet sheet,
        SheetXmlLayout layout,
        HashSet<string> loadedScenarioNames,
        Dictionary<string, List<WorksheetCustomViewState>> customViewStatesById)
    {
        sheet.HiddenRows.UnionWith(layout.HiddenRows);
        sheet.HiddenCols.UnionWith(layout.HiddenCols);
        sheet.IsProtected = layout.IsProtected;
        sheet.ProtectionPassword = layout.ProtectionPasswordHash;
        sheet.ProtectionMetadata = layout.ProtectionMetadata;
        sheet.ProtectionPermissions.Clear();
        sheet.ProtectionPermissions.AddRange(layout.ProtectionPermissions);
        foreach (var range in layout.AllowEditRanges)
            sheet.AllowEditRanges.Add(new GridRange(
                new CellAddress(sheet.Id, range.Start.Row, range.Start.Col),
                new CellAddress(sheet.Id, range.End.Row, range.End.Col)));
        foreach (var (range, password) in layout.AllowEditRangePasswords)
            sheet.AllowEditRangePasswords[new GridRange(
                new CellAddress(sheet.Id, range.Start.Row, range.Start.Col),
                new CellAddress(sheet.Id, range.End.Row, range.End.Col))] = password;
        foreach (var range in layout.MergedRegions)
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, range.Start.Row, range.Start.Col),
                new CellAddress(sheet.Id, range.End.Row, range.End.Col)));
        sheet.ViewMode = layout.ViewMode;
        sheet.ShowGridlines = layout.ShowGridlines;
        sheet.ShowHeadings = layout.ShowHeadings;
        sheet.ShowRulers = layout.ShowRulers;
        sheet.ZoomPercent = layout.ZoomPercent;
        sheet.ShowFormulas = layout.ShowFormulas;
        sheet.IsRightToLeft = layout.IsRightToLeft;
        if (layout.DefaultColumnWidth is { } defaultColumnWidth)
            sheet.DefaultColumnWidth = defaultColumnWidth;
        if (layout.DefaultRowHeight is { } defaultRowHeight)
            sheet.DefaultRowHeight = defaultRowHeight;
        sheet.SheetFormatMetadata = layout.SheetFormatMetadata;
        sheet.DimensionMetadata = layout.DimensionMetadata;
        sheet.SheetPropertiesMetadata = layout.SheetPropertiesMetadata;
        foreach (var (rowNum, height) in layout.RowHeights)
            sheet.RowHeights[rowNum] = height;
        foreach (var (colNum, width) in layout.ColumnWidths)
            sheet.ColumnWidths[colNum] = width;
        foreach (var (row, col, text, author) in layout.Comments)
        {
            var address = new CellAddress(sheet.Id, row, col);
            sheet.Comments[address] = text;
            if (!string.IsNullOrEmpty(author))
                sheet.CommentAuthors[address] = author;
        }
        foreach (var (row, col) in layout.ShownCommentAddresses)
            sheet.ShownComments.Add(new CellAddress(sheet.Id, row, col));
        foreach (var (row, col, comment) in layout.ThreadedComments)
            sheet.ThreadedComments[new CellAddress(sheet.Id, row, col)] = comment;
        sheet.BackgroundImage = layout.BackgroundImage;
        sheet.PageHeaderPictures = layout.HeaderFooterPictures.PageHeader;
        sheet.PageFooterPictures = layout.HeaderFooterPictures.PageFooter;
        sheet.FirstPageHeaderPictures = layout.HeaderFooterPictures.FirstPageHeader;
        sheet.FirstPageFooterPictures = layout.HeaderFooterPictures.FirstPageFooter;
        sheet.EvenPageHeaderPictures = layout.HeaderFooterPictures.EvenPageHeader;
        sheet.EvenPageFooterPictures = layout.HeaderFooterPictures.EvenPageFooter;
        sheet.CodeName = layout.CodeName;
        sheet.AutoFilter = layout.AutoFilter;
        XlsxWorksheetAutoFilterMapper.MaterializeFilters(sheet);
        sheet.UsePrinterDefaults = layout.UsePrinterDefaults;
        sheet.PrintCopies = layout.PrintCopies;
        sheet.FitToPage = layout.FitToPage;
        sheet.AutoPageBreaks = layout.AutoPageBreaks;
        if (layout.PrintQualityDpi is { } printQualityDpi)
            sheet.PrintQualityDpi = printQualityDpi;
        sheet.PrintQualityVerticalDpi = layout.PrintQualityVerticalDpi == sheet.PrintQualityDpi
            ? null
            : layout.PrintQualityVerticalDpi;
        sheet.PageMarginsMetadata = layout.PageMarginsMetadata;
        sheet.PrintOptionsMetadata = layout.PrintOptionsMetadata;
        sheet.PageSetupMetadata = layout.PageSetupMetadata;
        sheet.HeaderFooterMetadata = layout.HeaderFooterMetadata;
        sheet.RowPageBreaksMetadata = layout.RowPageBreaksMetadata;
        sheet.ColumnPageBreaksMetadata = layout.ColumnPageBreaksMetadata;

        foreach (var (rowNum, level) in layout.RowOutlineLevels)
            sheet.RowOutlineLevels[rowNum] = level;
        foreach (var (colNum, level) in layout.ColOutlineLevels)
            sheet.ColOutlineLevels[colNum] = level;
        sheet.OutlineSummaryBelow = layout.OutlineSummaryBelow;
        sheet.OutlineSummaryRight = layout.OutlineSummaryRight;
        sheet.ShowOutlineSymbols = layout.ShowOutlineSymbols;
        sheet.ApplyOutlineStyles = layout.ApplyOutlineStyles;
        sheet.GroupHiddenRows.UnionWith(layout.GroupHiddenRows);
        sheet.GroupHiddenCols.UnionWith(layout.GroupHiddenCols);
        sheet.CollapsedAnchorRows.UnionWith(layout.CollapsedAnchorRows);
        sheet.CollapsedAnchorCols.UnionWith(layout.CollapsedAnchorCols);
        var loadedDrawingObjectOrder = new List<(int OrderIndex, DrawingObjectZOrderEntry Entry)>();
        var fallbackChartDataRange = sheet.GetUsedRange();
        var sheetNameResolver = workbook.Sheets
            .ToDictionary(s => s.Name, s => s.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var chartPart in layout.ChartParts)
        {
            if (XlsxChartPartReader.TryReadSupportedChart(chartPart.Xml, sheet.Id, fallbackChartDataRange, sheetNameResolver, out var chart))
            {
                chart.Name = chartPart.Name;
                chart.AltTextTitle = chartPart.Title;
                chart.AltTextDescription = chartPart.AltText;
                // R98-io-chart-hyperlink-model-field: populate the model's own hyperlink field (resolved
                // per-chart from THIS chart's own graphicFrame at load time, not a sheet-name-keyed
                // guess) so a later move (MoveChartCommand/MoveChartToNewSheetCommand, which relocate
                // this SAME ChartModel instance) or clone/paste (DuplicateSheetDrawingCloner.CloneChart)
                // still has a hyperlink to carry forward -- mirrors PictureModel/DrawingShapeModel/
                // TextBoxModel.Hyperlink (R97-model-drawing-hyperlink-2-2).
                chart.Hyperlink = chartPart.Hyperlink;
                XlsxDrawingAnchorApplier.ApplyToChart(chart, chartPart.Anchor, sheet);
                ApplyChartExternalDataRelationshipMetadata(chart, chartPart);
                ApplyChartUserShapesRelationshipMetadata(chart, chartPart);
                sheet.Charts.Add(chart);
                AddLoadedDrawingObjectOrder(
                    loadedDrawingObjectOrder,
                    chartPart.DrawingOrderIndex,
                    SelectionPaneObjectKind.Chart,
                    chart.Id);
            }
        }
        foreach (var picturePart in layout.PictureParts)
        {
            // R119-io-camera-linked-picture-identity: a part built by
            // XlsxWorksheetDrawingPartReader.ReadPictureSnapshotGroupParts (Kind ==
            // CellRangeSnapshot) came from a marked <xdr:grpSp>, not a real <xdr:pic> -- reconstruct
            // it as the single linked/unlinked camera PictureModel it always was, instead of the
            // ordinary embedded-image path below (which would leave Cells empty and
            // IsLinkedToSourceRange false, i.e. exactly the identity/link loss this fixes).
            var picture = picturePart.Kind == PictureKind.CellRangeSnapshot
                ? BuildCellRangeSnapshotPicture(picturePart, sheet, sheetNameResolver)
                : new PictureModel
                {
                    Anchor = new CellAddress(
                        sheet.Id,
                        picturePart.Anchor?.FromRowZeroBased + 1 ?? 1,
                        picturePart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                    Kind = PictureKind.Image,
                    Name = picturePart.Name,
                    ImageBytes = picturePart.ImageBytes,
                    ContentType = picturePart.ContentType,
                    Title = picturePart.Title,
                    AltText = picturePart.AltText,
                    // R90-app-accessibility-checker-5-2: preserve Excel's "Mark as decorative" flag.
                    IsDecorative = picturePart.IsDecorative,
                    RotationDegrees = picturePart.RotationDegrees,
                    FlipHorizontal = picturePart.FlipHorizontal,
                    FlipVertical = picturePart.FlipVertical,
                    CropLeft = picturePart.CropLeft,
                    CropTop = picturePart.CropTop,
                    CropRight = picturePart.CropRight,
                    CropBottom = picturePart.CropBottom,
                    // R65-io-image-drawing-6-1: a "Link to File" picture part has LinkTarget set instead of
                    // ImageBytes -- carry it onto the model so the picture is materialized as a linked
                    // picture (with a marker other code can check) instead of silently vanishing.
                    LinkedImageTarget = picturePart.LinkTarget,
                    // R80-io-drawing-image-5-3: carry the vector SVG fallback (if any) onto the model so
                    // the writer can re-emit the asvg:svgBlip extension instead of permanently downgrading
                    // the picture to a flat PNG the first time it is edited.
                    SvgImageBytes = picturePart.SvgImageBytes,
                    // R97-model-drawing-hyperlink-2-2: populate the model's own hyperlink field so a later
                    // clone/paste of this picture (which clears IsSourceLoaded and so can't lean on the
                    // source-package hyperlink re-read in XlsxWorksheetDrawingObjectWriter) still has a
                    // hyperlink to carry forward.
                    Hyperlink = picturePart.Hyperlink
                };
            XlsxDrawingAnchorApplier.ApplyToPicture(picture, picturePart.Anchor, sheet);
            picture.IsSourceLoaded = true;
            sheet.Pictures.Add(picture);
            AddLoadedDrawingObjectOrder(
                loadedDrawingObjectOrder,
                picturePart.DrawingOrderIndex,
                SelectionPaneObjectKind.Picture,
                picture.Id);
        }
        foreach (var textBoxPart in layout.TextBoxParts)
        {
            var textBox = new TextBoxModel
            {
                Anchor = new CellAddress(
                    sheet.Id,
                    textBoxPart.Anchor?.FromRowZeroBased + 1 ?? 1,
                    textBoxPart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                Text = textBoxPart.Text,
                Name = textBoxPart.Name,
                Title = textBoxPart.Title,
                AltText = textBoxPart.AltText,
                // R149-app-accessibility-checker-decorative-shapes: preserve Excel's "Mark as
                // decorative" flag, mirroring the picture path above.
                IsDecorative = textBoxPart.IsDecorative,
                RotationDegrees = textBoxPart.RotationDegrees,
                FlipHorizontal = textBoxPart.FlipHorizontal,
                FlipVertical = textBoxPart.FlipVertical,
                HasFill = textBoxPart.HasFill,
                FillColor = textBoxPart.FillColor,
                OutlineColor = textBoxPart.OutlineColor,
                // R91-commands-insert-object-5-1: preserve an authored <a:ln><a:noFill/> so a loaded
                // borderless text box doesn't regain the fallback gray border on render or re-save.
                OutlineHasNoFill = textBoxPart.OutlineHasNoFill,
                FillThemeColor = textBoxPart.FillThemeColor,
                OutlineThemeColor = textBoxPart.OutlineThemeColor,
                // backlog textbox-6-2: populate the txBody text-formatting fields read in
                // XlsxWorksheetDrawingParts.ReadSpElement -- without this the fields added to
                // TextBoxModel stayed dead code and a real xlsx load never filled them in.
                TextFontFamily = textBoxPart.TextFontFamily,
                TextFontSizePoints = textBoxPart.TextFontSizePoints,
                TextBold = textBoxPart.TextBold,
                TextItalic = textBoxPart.TextItalic,
                TextColor = textBoxPart.TextColor,
                TextThemeColor = textBoxPart.TextThemeColor,
                TextHAlign = textBoxPart.TextHAlign,
                TextVAnchor = textBoxPart.TextVAnchor,
                // R97-model-drawing-hyperlink-2-2: see the matching comment on the picture path above.
                Hyperlink = textBoxPart.Hyperlink
            };
            XlsxDrawingAnchorApplier.ApplyToTextBox(textBox, textBoxPart.Anchor, sheet);
            textBox.IsSourceLoaded = true;
            sheet.TextBoxes.Add(textBox);
            AddLoadedDrawingObjectOrder(
                loadedDrawingObjectOrder,
                textBoxPart.DrawingOrderIndex,
                SelectionPaneObjectKind.TextBox,
                textBox.Id);
        }
        foreach (var shapePart in layout.ShapeParts)
        {
            var shape = new DrawingShapeModel
            {
                Anchor = new CellAddress(
                    sheet.Id,
                    shapePart.Anchor?.FromRowZeroBased + 1 ?? 1,
                    shapePart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                Kind = shapePart.Kind,
                Name = shapePart.Name,
                Title = shapePart.Title,
                AltText = shapePart.AltText,
                // R149-app-accessibility-checker-decorative-shapes: preserve Excel's "Mark as
                // decorative" flag, mirroring the picture path above.
                IsDecorative = shapePart.IsDecorative,
                RotationDegrees = shapePart.RotationDegrees,
                FlipHorizontal = shapePart.FlipHorizontal,
                FlipVertical = shapePart.FlipVertical,
                HasFill = shapePart.HasFill,
                FillColor = shapePart.FillColor,
                OutlineColor = shapePart.OutlineColor,
                GradientFillEndColor = shapePart.GradientFillEndColor,
                GradientFillDirection = shapePart.GradientFillDirection,
                FillThemeColor = shapePart.FillThemeColor,
                OutlineThemeColor = shapePart.OutlineThemeColor,
                HasShadowEffect = shapePart.HasShadowEffect,
                EffectPreset = shapePart.EffectPreset,
                UsesThemeEffects = shapePart.UsesThemeEffects,
                OutlineWidthPoints = shapePart.OutlineWidthPoints,
                OutlineHasNoFill = shapePart.OutlineHasNoFill,
                OutlineDash = shapePart.OutlineDash,
                HeadArrowhead = shapePart.HeadArrowhead,
                TailArrowhead = shapePart.TailArrowhead,
                StartConnectedShapeId = shapePart.StartConnectedShapeId,
                StartConnectedShapeConnectionIndex = shapePart.StartConnectedShapeConnectionIndex,
                EndConnectedShapeId = shapePart.EndConnectedShapeId,
                EndConnectedShapeConnectionIndex = shapePart.EndConnectedShapeConnectionIndex,
                ShapeText = shapePart.ShapeText,
                ShapeTextFontSizePoints = shapePart.ShapeTextFontSizePoints,
                ShapeTextBold = shapePart.ShapeTextBold,
                ShapeTextItalic = shapePart.ShapeTextItalic,
                ShapeTextUnderline = shapePart.ShapeTextUnderline,
                ShapeTextColor = shapePart.ShapeTextColor,
                ShapeTextThemeColor = shapePart.ShapeTextThemeColor,
                ShapeTextHAlign = shapePart.ShapeTextHAlign,
                ShapeTextVAnchor = shapePart.ShapeTextVAnchor,
                ShapeTextWrap = shapePart.ShapeTextWrap,
                IsWordArt = shapePart.IsWordArt,
                WarpPreset = shapePart.WarpPreset,
                ShapeTextGradientEndColor = shapePart.ShapeTextGradientEndColor,
                ShapeTextGradientEndThemeColor = shapePart.ShapeTextGradientEndThemeColor,
                ShapeTextGradientAngle = shapePart.ShapeTextGradientAngle,
                ShapeTextOutlineColor = shapePart.ShapeTextOutlineColor,
                ShapeTextOutlineThemeColor = shapePart.ShapeTextOutlineThemeColor,
                ShapeTextOutlineWidthPoints = shapePart.ShapeTextOutlineWidthPoints,
                AdjustValues = shapePart.AdjustValues,
                // R97-model-drawing-hyperlink-2-2: see the matching comment on the picture path above.
                Hyperlink = shapePart.Hyperlink
            };
            XlsxDrawingAnchorApplier.ApplyToShape(shape, shapePart.Anchor, sheet,
                shapePart.XfrmWidthPixels, shapePart.XfrmHeightPixels);
            shape.IsSourceLoaded = true;
            sheet.DrawingShapes.Add(shape);
            AddLoadedDrawingObjectOrder(
                loadedDrawingObjectOrder,
                shapePart.DrawingOrderIndex,
                SelectionPaneObjectKind.Shape,
                shape.Id);
        }
        ApplyLoadedDrawingObjectZOrder(sheet, loadedDrawingObjectOrder);
        foreach (var sparklineLayout in layout.Sparklines)
        {
            var sparkline = sparklineLayout.Sparkline;
            // A sparkline's data range (and optional date-axis range) may live on a DIFFERENT sheet
            // than its host: Excel's Sparkline "Edit Data" dialog allows a cross-sheet source range,
            // whose <xm:f> formula carries a "Sheet2!" qualifier. XlsxSparklineMapper.Read preserved
            // that qualifier's sheet NAME (it had no sheet-id map yet); resolve it to the real SheetId
            // now via sheetNameResolver. A null qualifier (the common same-sheet case) or a name that
            // no longer exists falls back to the host sheet — matching the pre-fix behaviour there.
            var dataRangeSheetId = ResolveSparklineRangeSheetId(
                sparklineLayout.DataRangeSheetName, sheetNameResolver, sheet.Id);
            var dateAxisSheetId = ResolveSparklineRangeSheetId(
                sparklineLayout.DateAxisSheetName, sheetNameResolver, sheet.Id);
            sheet.Sparklines.Add(new SparklineModel
            {
                DataRange = new GridRange(
                    new CellAddress(dataRangeSheetId, sparkline.DataRange.Start.Row, sparkline.DataRange.Start.Col),
                    new CellAddress(dataRangeSheetId, sparkline.DataRange.End.Row, sparkline.DataRange.End.Col)),
                Location = new CellAddress(sheet.Id, sparkline.Location.Row, sparkline.Location.Col),
                Kind                = sparkline.Kind,
                GroupId             = sparkline.GroupId,
                ShowMarkers         = sparkline.ShowMarkers,
                ShowHighPoint       = sparkline.ShowHighPoint,
                ShowLowPoint        = sparkline.ShowLowPoint,
                ShowFirstPoint      = sparkline.ShowFirstPoint,
                ShowLastPoint       = sparkline.ShowLastPoint,
                ShowNegativePoints  = sparkline.ShowNegativePoints,
                ShowAxis            = sparkline.ShowAxis,
                DisplayHidden       = sparkline.DisplayHidden,
                RightToLeft         = sparkline.RightToLeft,
                SeriesColor         = sparkline.SeriesColor,
                NegativeColor       = sparkline.NegativeColor,
                AxisColor           = sparkline.AxisColor,
                MarkersColor        = sparkline.MarkersColor,
                HighPointColor      = sparkline.HighPointColor,
                LowPointColor       = sparkline.LowPointColor,
                FirstPointColor     = sparkline.FirstPointColor,
                LastPointColor      = sparkline.LastPointColor,
                LineWeight          = sparkline.LineWeight,
                MinAxisType         = sparkline.MinAxisType,
                MaxAxisType         = sparkline.MaxAxisType,
                ManualMin           = sparkline.ManualMin,
                ManualMax           = sparkline.ManualMax,
                DisplayEmptyCellsAs = sparkline.DisplayEmptyCellsAs,
                DateAxisRange       = sparkline.DateAxisRange is { } dateAxisRange
                    ? new GridRange(
                        new CellAddress(dateAxisSheetId, dateAxisRange.Start.Row, dateAxisRange.Start.Col),
                        new CellAddress(dateAxisSheetId, dateAxisRange.End.Row, dateAxisRange.End.Col))
                    : null,
            });
        }
        foreach (var formControl in layout.FormControls)
        {
            if (formControl.Anchor is { } anchor)
            {
                formControl.Anchor = new GridRange(
                    new CellAddress(sheet.Id, anchor.Start.Row, anchor.Start.Col),
                    new CellAddress(sheet.Id, anchor.End.Row, anchor.End.Col));
            }

            sheet.FormControls.Add(formControl);
        }
        foreach (var conditionalFormat in layout.AdvancedConditionalFormats)
            sheet.ConditionalFormats.Add(RemapConditionalFormat(conditionalFormat, sheet.Id));
        foreach (var ignoredErrorAddress in layout.IgnoredErrors.ExpandedCells)
        {
            var address = new CellAddress(sheet.Id, ignoredErrorAddress.Row, ignoredErrorAddress.Col);
            var cell = sheet.GetCell(address);
            if (cell is null)
            {
                cell = Cell.FromValue(BlankValue.Instance);
                sheet.SetCell(address, cell);
            }

            cell.IgnoreFormulaError = true;
        }
        if (layout.IgnoredErrors.ExistingCellOnlyRanges.Count > 0)
            ApplyExistingCellOnlyIgnoredErrors(sheet, layout.IgnoredErrors.ExistingCellOnlyRanges);
        sheet.IgnoredErrorsMetadata = layout.IgnoredErrorsMetadata;
        foreach (var watchedCell in layout.CellWatches)
        {
            var address = new CellAddress(sheet.Id, watchedCell.Row, watchedCell.Col);
            if (!workbook.WatchedCells.Contains(address))
                workbook.WatchedCells.Add(address);
        }
        sheet.CellWatchesMetadata = layout.CellWatchesMetadata;
        foreach (var scenario in layout.Scenarios)
        {
            var remappedScenario = new WorkbookScenario(
                scenario.Name,
                scenario.ChangingCells
                    .Select(change => new ScenarioCellValue(
                        new CellAddress(sheet.Id, change.Address.Row, change.Address.Col),
                        change.Value))
                    .ToList(),
                scenario.Comment,
                scenario.Hidden,
                scenario.Locked,
                scenario.User);

            if (loadedScenarioNames.Add(remappedScenario.Name))
            {
                workbook.Scenarios.Add(remappedScenario);
                continue;
            }

            var existingIndex = -1;
            for (var index = 0; index < workbook.Scenarios.Count; index++)
            {
                if (!string.Equals(workbook.Scenarios[index].Name, remappedScenario.Name, StringComparison.OrdinalIgnoreCase))
                    continue;

                existingIndex = index;
                break;
            }

            if (existingIndex >= 0)
            {
                workbook.Scenarios[existingIndex] = workbook.Scenarios[existingIndex] with
                {
                    ChangingCells = workbook.Scenarios[existingIndex].ChangingCells
                        .Concat(remappedScenario.ChangingCells)
                        .Distinct()
                        .ToList()
                };
            }
        }
        foreach (var customView in layout.CustomViews)
        {
            if (!customViewStatesById.TryGetValue(customView.Id, out var states))
            {
                states = [];
                customViewStatesById[customView.Id] = states;
            }

            states.Add(customView.State with { SheetName = sheet.Name });
        }
        foreach (var property in layout.CustomProperties)
            sheet.CustomProperties.Add(property);
        sheet.SmartTags = layout.SmartTags;
        sheet.DataConsolidation = layout.DataConsolidation;
        sheet.SortState = layout.SortState;
        sheet.SingleXmlCells = layout.SingleXmlCells;
        sheet.AdditionalViews = layout.AdditionalViews;
        sheet.PrimaryViewMetadata = layout.PrimaryViewMetadata;
        sheet.FullCalculationOnLoad = layout.FullCalculationOnLoad;
        sheet.PhoneticProperties = layout.PhoneticProperties;
    }

    /// <summary>
    /// Resolves the sheet a sparkline data-range/date-axis reference lives on. When the source
    /// <c>&lt;xm:f&gt;</c> formula carried a sheet-name qualifier (a cross-sheet source range), that
    /// name is looked up in <paramref name="sheetNameResolver"/> to obtain the correct
    /// <see cref="SheetId"/>. When the qualifier is absent (the common same-sheet case) or names a
    /// sheet that no longer exists, the reference is anchored to <paramref name="hostSheetId"/>.
    /// </summary>
    /// <summary>
    /// R119-io-camera-linked-picture-identity: rebuilds a "camera" / Paste Special &gt; Linked
    /// Picture / Paste Picture object's <see cref="PictureModel"/> (Kind ==
    /// <see cref="PictureKind.CellRangeSnapshot"/>) from an <see cref="XlsxPicturePackagePart"/> that
    /// <c>XlsxWorksheetDrawingPartReader.ReadPictureSnapshotGroupParts</c> produced from a marked
    /// <c>&lt;xdr:grpSp&gt;</c> -- restoring IsLinkedToSourceRange/LinkedSourceRange/
    /// LinkedSourceSheetName and the per-cell Cells snapshot instead of leaving the picture
    /// permanently flattened into independent, disconnected shapes (the bug this fixes). Mirrors
    /// <see cref="ResolveSparklineRangeSheetId"/>'s sheet-name-qualifier resolution: the linked
    /// range's sheet id is looked up by name in <paramref name="sheetNameResolver"/>, falling back to
    /// the host sheet when the qualifier is absent or names a sheet that no longer exists.
    /// </summary>
    private static PictureModel BuildCellRangeSnapshotPicture(
        XlsxPicturePackagePart picturePart,
        Sheet sheet,
        IReadOnlyDictionary<string, SheetId> sheetNameResolver)
    {
        var picture = new PictureModel
        {
            Anchor = new CellAddress(
                sheet.Id,
                picturePart.Anchor?.FromRowZeroBased + 1 ?? 1,
                picturePart.Anchor?.FromColumnZeroBased + 1 ?? 1),
            Kind = PictureKind.CellRangeSnapshot,
            Name = picturePart.Name,
            Title = picturePart.Title,
            AltText = picturePart.AltText,
            IsDecorative = picturePart.IsDecorative,
            RotationDegrees = picturePart.RotationDegrees,
            FlipHorizontal = picturePart.FlipHorizontal,
            FlipVertical = picturePart.FlipVertical,
            SourceRowCount = picturePart.SnapshotSourceRowCount,
            SourceColumnCount = picturePart.SnapshotSourceColumnCount,
            IsLinkedToSourceRange = picturePart.IsLinkedToSourceRange,
            LinkedSourceSheetName = picturePart.LinkedSourceSheetName,
            Hyperlink = picturePart.Hyperlink
        };

        if (picturePart.IsLinkedToSourceRange &&
            picturePart.LinkedSourceStartRow is { } startRow &&
            picturePart.LinkedSourceStartCol is { } startCol &&
            picturePart.LinkedSourceEndRow is { } endRow &&
            picturePart.LinkedSourceEndCol is { } endCol)
        {
            var linkedSheetId = ResolveSparklineRangeSheetId(picturePart.LinkedSourceSheetName, sheetNameResolver, sheet.Id);
            picture.LinkedSourceRange = new GridRange(
                new CellAddress(linkedSheetId, (uint)startRow, (uint)startCol),
                new CellAddress(linkedSheetId, (uint)endRow, (uint)endCol));
        }

        if (picturePart.SnapshotCells is not null)
        {
            foreach (var cell in picturePart.SnapshotCells)
                picture.Cells.Add(cell);
        }

        return picture;
    }

    private static SheetId ResolveSparklineRangeSheetId(
        string? qualifyingSheetName,
        IReadOnlyDictionary<string, SheetId> sheetNameResolver,
        SheetId hostSheetId) =>
        qualifyingSheetName is not null &&
        sheetNameResolver.TryGetValue(qualifyingSheetName, out var resolved)
            ? resolved
            : hostSheetId;

    private static void AddLoadedDrawingObjectOrder(
        List<(int OrderIndex, DrawingObjectZOrderEntry Entry)> order,
        int orderIndex,
        SelectionPaneObjectKind kind,
        Guid id)
    {
        if (orderIndex < 0 || id == Guid.Empty || !DrawingObjectZOrder.IsSupportedKind(kind))
            return;

        order.Add((orderIndex, new DrawingObjectZOrderEntry(kind, id)));
    }

    private static void ApplyLoadedDrawingObjectZOrder(
        Sheet sheet,
        List<(int OrderIndex, DrawingObjectZOrderEntry Entry)> order)
    {
        if (order.Count == 0)
            return;

        var loadedOrder = order
            .OrderBy(item => item.OrderIndex)
            .Select(item => item.Entry)
            .Where(entry => DrawingObjectZOrder.ContainsObject(sheet, entry))
            .Distinct()
            .ToList();
        if (loadedOrder.Count == 0)
            return;

        var defaultOrder = DrawingObjectZOrder.GetNormalizedOrder(sheet);
        if (loadedOrder.SequenceEqual(defaultOrder))
            return;

        sheet.DrawingObjectZOrder.Clear();
        sheet.DrawingObjectZOrder.AddRange(loadedOrder);
        DrawingObjectZOrder.EnsureNormalizedOrder(sheet);
    }

    private static void ApplyExistingCellOnlyIgnoredErrors(Sheet sheet, IReadOnlyList<GridRange> ranges)
    {
        var occupiedCells = sheet.GetOccupiedCellMap();
        if (occupiedCells.Count == 0 || ranges.Count == 0)
            return;

        if (HasRangeContainingUsedCells(sheet, ranges))
        {
            foreach (var cell in occupiedCells.Values)
                cell.IgnoreFormulaError = true;
            return;
        }

        var orderedRanges = ranges
            .OrderBy(range => range.Start.Row)
            .ThenBy(range => range.End.Row)
            .ToArray();
        var orderedCells = occupiedCells
            .OrderBy(pair => pair.Key.Row)
            .ThenBy(pair => pair.Key.Col);
        var activeRanges = new List<GridRange>();
        var nextRangeIndex = 0;
        uint currentRow = 0;
        List<(uint StartCol, uint EndCol)> rowIntervals = [];

        foreach (var pair in orderedCells)
        {
            var row = pair.Key.Row;
            if (row != currentRow)
            {
                currentRow = row;
                while (nextRangeIndex < orderedRanges.Length &&
                       orderedRanges[nextRangeIndex].Start.Row <= row)
                {
                    activeRanges.Add(orderedRanges[nextRangeIndex]);
                    nextRangeIndex++;
                }

                for (var i = activeRanges.Count - 1; i >= 0; i--)
                {
                    if (activeRanges[i].End.Row < row)
                        activeRanges.RemoveAt(i);
                }

                rowIntervals = BuildMergedIgnoredErrorColumnIntervals(activeRanges, row);
            }

            if (ContainsColumn(rowIntervals, pair.Key.Col))
                pair.Value.IgnoreFormulaError = true;
        }
    }

    private static bool HasRangeContainingUsedCells(Sheet sheet, IReadOnlyList<GridRange> ranges)
    {
        if (sheet.GetUsedRange() is not { } usedRange)
            return false;

        foreach (var range in ranges)
        {
            if (range.Start.Row <= usedRange.Start.Row &&
                range.End.Row >= usedRange.End.Row &&
                range.Start.Col <= usedRange.Start.Col &&
                range.End.Col >= usedRange.End.Col)
            {
                return true;
            }
        }

        return false;
    }

    private static List<(uint StartCol, uint EndCol)> BuildMergedIgnoredErrorColumnIntervals(
        List<GridRange> activeRanges,
        uint row)
    {
        var intervals = new List<(uint StartCol, uint EndCol)>(activeRanges.Count);
        foreach (var range in activeRanges)
        {
            if (row >= range.Start.Row && row <= range.End.Row)
                intervals.Add((range.Start.Col, range.End.Col));
        }

        if (intervals.Count <= 1)
            return intervals;

        intervals.Sort(static (left, right) =>
        {
            var startCompare = left.StartCol.CompareTo(right.StartCol);
            return startCompare != 0
                ? startCompare
                : left.EndCol.CompareTo(right.EndCol);
        });

        var writeIndex = 0;
        for (var readIndex = 1; readIndex < intervals.Count; readIndex++)
        {
            var current = intervals[readIndex];
            var merged = intervals[writeIndex];
            if (current.StartCol <= merged.EndCol + 1)
            {
                intervals[writeIndex] = (merged.StartCol, Math.Max(merged.EndCol, current.EndCol));
                continue;
            }

            writeIndex++;
            intervals[writeIndex] = current;
        }

        intervals.RemoveRange(writeIndex + 1, intervals.Count - writeIndex - 1);
        return intervals;
    }

    private static bool ContainsColumn(IReadOnlyList<(uint StartCol, uint EndCol)> intervals, uint col)
    {
        var low = 0;
        var high = intervals.Count - 1;
        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var interval = intervals[mid];
            if (col < interval.StartCol)
            {
                high = mid - 1;
                continue;
            }

            if (col > interval.EndCol)
            {
                low = mid + 1;
                continue;
            }

            return true;
        }

        return false;
    }
}
