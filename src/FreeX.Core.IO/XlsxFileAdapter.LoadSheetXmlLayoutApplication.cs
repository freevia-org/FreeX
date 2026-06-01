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
        foreach (var range in layout.AllowEditRanges)
            sheet.AllowEditRanges.Add(new GridRange(
                new CellAddress(sheet.Id, range.Start.Row, range.Start.Col),
                new CellAddress(sheet.Id, range.End.Row, range.End.Col)));
        sheet.ViewMode = layout.ViewMode;
        sheet.ShowGridlines = layout.ShowGridlines;
        sheet.ShowHeadings = layout.ShowHeadings;
        sheet.ShowRulers = layout.ShowRulers;
        sheet.ZoomPercent = layout.ZoomPercent;
        sheet.ShowFormulas = layout.ShowFormulas;
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
        foreach (var (row, col, text) in layout.Comments)
            sheet.Comments[new CellAddress(sheet.Id, row, col)] = text;
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
        foreach (var chartPart in layout.ChartParts)
        {
            if (XlsxChartPartReader.TryReadSupportedChart(chartPart.Xml, sheet.Id, out var chart))
            {
                chart.Name = chartPart.Name;
                XlsxDrawingAnchorApplier.ApplyToChart(chart, chartPart.Anchor, sheet);
                ApplyChartExternalDataRelationshipMetadata(chart, chartPart);
                ApplyChartUserShapesRelationshipMetadata(chart, chartPart);
                sheet.Charts.Add(chart);
            }
        }
        foreach (var picturePart in layout.PictureParts)
        {
            var picture = new PictureModel
            {
                Anchor = new CellAddress(
                    sheet.Id,
                    picturePart.Anchor?.FromRowZeroBased + 1 ?? 1,
                    picturePart.Anchor?.FromColumnZeroBased + 1 ?? 1),
                Kind = PictureKind.Image,
                Name = picturePart.Name,
                ImageBytes = picturePart.ImageBytes.ToArray(),
                ContentType = picturePart.ContentType,
                Title = picturePart.Title,
                AltText = picturePart.AltText,
                RotationDegrees = picturePart.RotationDegrees,
                CropLeft = picturePart.CropLeft,
                CropTop = picturePart.CropTop,
                CropRight = picturePart.CropRight,
                CropBottom = picturePart.CropBottom
            };
            XlsxDrawingAnchorApplier.ApplyToPicture(picture, picturePart.Anchor, sheet);
            picture.IsSourceLoaded = true;
            sheet.Pictures.Add(picture);
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
                RotationDegrees = textBoxPart.RotationDegrees,
                FillColor = textBoxPart.FillColor,
                OutlineColor = textBoxPart.OutlineColor,
                FillThemeColor = textBoxPart.FillThemeColor,
                OutlineThemeColor = textBoxPart.OutlineThemeColor
            };
            XlsxDrawingAnchorApplier.ApplyToTextBox(textBox, textBoxPart.Anchor, sheet);
            textBox.IsSourceLoaded = true;
            sheet.TextBoxes.Add(textBox);
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
                RotationDegrees = shapePart.RotationDegrees,
                FillColor = shapePart.FillColor,
                OutlineColor = shapePart.OutlineColor,
                GradientFillEndColor = shapePart.GradientFillEndColor,
                FillThemeColor = shapePart.FillThemeColor,
                OutlineThemeColor = shapePart.OutlineThemeColor,
                HasShadowEffect = shapePart.HasShadowEffect,
                EffectPreset = shapePart.EffectPreset
            };
            XlsxDrawingAnchorApplier.ApplyToShape(shape, shapePart.Anchor, sheet);
            shape.IsSourceLoaded = true;
            sheet.DrawingShapes.Add(shape);
        }
        foreach (var sparkline in layout.Sparklines)
        {
            sheet.Sparklines.Add(new SparklineModel
            {
                DataRange = new GridRange(
                    new CellAddress(sheet.Id, sparkline.DataRange.Start.Row, sparkline.DataRange.Start.Col),
                    new CellAddress(sheet.Id, sparkline.DataRange.End.Row, sparkline.DataRange.End.Col)),
                Location = new CellAddress(sheet.Id, sparkline.Location.Row, sparkline.Location.Col),
                Kind = sparkline.Kind
            });
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

            var existingIndex = workbook.Scenarios.FindIndex(existing =>
                string.Equals(existing.Name, remappedScenario.Name, StringComparison.OrdinalIgnoreCase));
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
