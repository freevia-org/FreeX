using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

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
        sheet.BackgroundImage = layout.BackgroundImage;
        sheet.PageHeaderPictures = layout.HeaderFooterPictures.PageHeader;
        sheet.PageFooterPictures = layout.HeaderFooterPictures.PageFooter;
        sheet.FirstPageHeaderPictures = layout.HeaderFooterPictures.FirstPageHeader;
        sheet.FirstPageFooterPictures = layout.HeaderFooterPictures.FirstPageFooter;
        sheet.EvenPageHeaderPictures = layout.HeaderFooterPictures.EvenPageHeader;
        sheet.EvenPageFooterPictures = layout.HeaderFooterPictures.EvenPageFooter;
        sheet.CodeName = layout.CodeName;

        foreach (var (rowNum, level) in layout.RowOutlineLevels)
            sheet.RowOutlineLevels[rowNum] = level;
        foreach (var (colNum, level) in layout.ColOutlineLevels)
            sheet.ColOutlineLevels[colNum] = level;
        sheet.GroupHiddenRows.UnionWith(layout.GroupHiddenRows);
        sheet.GroupHiddenCols.UnionWith(layout.GroupHiddenCols);
        foreach (var chartPart in layout.ChartParts)
        {
            if (XlsxChartPartReader.TryReadSupportedChart(chartPart.Xml, sheet.Id, out var chart))
            {
                chart.Name = chartPart.Name;
                XlsxDrawingAnchorApplier.ApplyToChart(chart, chartPart.Anchor, sheet);
                ApplyChartExternalDataRelationshipMetadata(chart, chartPart);
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
                HasShadowEffect = shapePart.HasShadowEffect
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
        {
            foreach (var (address, cell) in sheet.GetUsedCells())
            {
                var comparableAddress = new CellAddress(
                    layout.IgnoredErrors.ExistingCellOnlyRanges[0].Start.Sheet,
                    address.Row,
                    address.Col);
                if (layout.IgnoredErrors.ExistingCellOnlyRanges.Any(range => range.Contains(comparableAddress)))
                    cell.IgnoreFormulaError = true;
            }
        }
        foreach (var watchedCell in layout.CellWatches)
        {
            var address = new CellAddress(sheet.Id, watchedCell.Row, watchedCell.Col);
            if (!workbook.WatchedCells.Contains(address))
                workbook.WatchedCells.Add(address);
        }
        foreach (var scenario in layout.Scenarios)
        {
            var remappedScenario = new WorkbookScenario(
                scenario.Name,
                scenario.ChangingCells
                    .Select(change => new ScenarioCellValue(
                        new CellAddress(sheet.Id, change.Address.Row, change.Address.Col),
                        change.Value))
                    .ToList());

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
        sheet.FullCalculationOnLoad = layout.FullCalculationOnLoad;
        sheet.PhoneticProperties = layout.PhoneticProperties;
    }
}
