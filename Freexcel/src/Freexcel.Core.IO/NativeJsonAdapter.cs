using System.Text.Json;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO;

/// <summary>
/// Native JSON adapter for Freexcel.
/// Serializes the workbook to a simple, human-readable JSON format.
/// </summary>
public sealed class NativeJsonAdapter : IFileAdapter
{
    public string Extension => ".fxl";
    public string FormatName => "Freexcel Workbook";

    public Workbook Load(Stream stream)
    {
        var dto = JsonSerializer.Deserialize<WorkbookDto>(stream)
            ?? throw new InvalidDataException("Invalid Freexcel file");

        var workbook = new Workbook(dto.Name);
        if (dto.WindowArrangement is { } arrangement && Enum.IsDefined(arrangement))
            workbook.WindowArrangement = arrangement;
        foreach (var errorCode in dto.DisabledFormulaErrorCodes ?? [])
            if (!string.IsNullOrWhiteSpace(errorCode))
                workbook.DisabledFormulaErrorCodes.Add(errorCode);

        foreach (var sDto in dto.Sheets ?? [])
        {
            if (string.IsNullOrEmpty(sDto?.Name)) continue;
            var sheet = workbook.AddSheet(sDto.Name);
            sheet.IsHidden = sDto.IsHidden;
            sheet.TabColor = sDto.TabColor is { } tabColor ? ParseColor(tabColor) : null;
            sheet.ViewMode = Enum.IsDefined(sDto.ViewMode) ? sDto.ViewMode : WorksheetViewMode.Normal;
            sheet.ShowGridlines = sDto.ShowGridlines ?? true;
            sheet.ShowHeadings = sDto.ShowHeadings ?? true;
            sheet.ShowRulers = sDto.ShowRulers ?? true;
            sheet.ZoomPercent = ValidZoomPercentOrDefault(sDto.ZoomPercent);
            sheet.ShowFormulas = sDto.ShowFormulas ?? false;
            sheet.FrozenRows = ValidFrozenRowsOrZero(sDto.FrozenRows);
            sheet.FrozenCols = ValidFrozenColumnsOrZero(sDto.FrozenCols);
            sheet.SplitRow = sheet.FrozenRows > 0 || sheet.FrozenCols > 0
                ? null
                : ValidRowPaneOrNull(sDto.SplitRow);
            sheet.SplitColumn = sheet.FrozenRows > 0 || sheet.FrozenCols > 0
                ? null
                : ValidColumnPaneOrNull(sDto.SplitColumn);
            if (!string.IsNullOrWhiteSpace(sDto.PrintArea))
            {
                try { sheet.PrintArea = GridRange.Parse(sDto.PrintArea, sheet.Id); }
                catch (FormatException) { /* skip unparseable print areas */ }
            }
            if (sDto.PageOrientation is { } orientation && Enum.IsDefined(orientation))
                sheet.PageOrientation = orientation;
            if (sDto.PaperSize is { } paperSize && Enum.IsDefined(paperSize))
                sheet.PaperSize = paperSize;
            if (sDto.PageMargins is { } margins)
                sheet.PageMargins = ValidPageMarginsOrDefault(
                    new WorksheetPageMargins(margins.Left, margins.Right, margins.Top, margins.Bottom),
                    WorksheetPageMargins.Narrow);
            sheet.HeaderMargin = NonNegativeFiniteOrDefault(sDto.HeaderMargin, 0.3);
            sheet.FooterMargin = NonNegativeFiniteOrDefault(sDto.FooterMargin, 0.3);
            sheet.PrintGridlines = sDto.PrintGridlines;
            sheet.PrintHeadings = sDto.PrintHeadings;
            sheet.PrintTitleRows = ToRepeatRange(sDto.PrintTitleRows);
            sheet.PrintTitleColumns = ToRepeatRange(sDto.PrintTitleColumns);
            sheet.PageHeader = ToHeaderFooter(sDto.PageHeader);
            sheet.PageFooter = ToHeaderFooter(sDto.PageFooter);
            sheet.FirstPageHeader = ToHeaderFooter(sDto.FirstPageHeader);
            sheet.FirstPageFooter = ToHeaderFooter(sDto.FirstPageFooter);
            sheet.EvenPageHeader = ToHeaderFooter(sDto.EvenPageHeader);
            sheet.EvenPageFooter = ToHeaderFooter(sDto.EvenPageFooter);
            sheet.DifferentFirstPageHeaderFooter = sDto.DifferentFirstPageHeaderFooter;
            sheet.DifferentOddEvenHeaderFooter = sDto.DifferentOddEvenHeaderFooter;
            sheet.HeaderFooterScaleWithDocument = sDto.HeaderFooterScaleWithDocument ?? true;
            sheet.HeaderFooterAlignWithMargins = sDto.HeaderFooterAlignWithMargins ?? true;
            sheet.CenterHorizontallyOnPage = sDto.CenterHorizontallyOnPage;
            sheet.CenterVerticallyOnPage = sDto.CenterVerticallyOnPage;
            if (sDto.PageOrder is { } pageOrder && Enum.IsDefined(pageOrder))
                sheet.PageOrder = pageOrder;
            sheet.FirstPageNumber = sDto.FirstPageNumber is > 0 ? sDto.FirstPageNumber : null;
            sheet.PrintBlackAndWhite = sDto.PrintBlackAndWhite;
            sheet.PrintDraftQuality = sDto.PrintDraftQuality;
            sheet.PrintQualityDpi = sDto.PrintQualityDpi is > 0 ? sDto.PrintQualityDpi : null;
            if (sDto.PrintErrorValue is { } printErrorValue && Enum.IsDefined(printErrorValue))
                sheet.PrintErrorValue = printErrorValue;
            if (sDto.PrintComments is { } printComments && Enum.IsDefined(printComments))
                sheet.PrintComments = printComments;
            if (sDto.ScaleToFit is { } scaleToFit)
                sheet.ScaleToFit = ValidScaleToFitOrDefault(
                    new WorksheetScaleToFit(scaleToFit.ScalePercent, scaleToFit.FitToPagesWide, scaleToFit.FitToPagesTall),
                    WorksheetScaleToFit.Default);
            foreach (var rowBreak in sDto.RowPageBreaks ?? [])
                if (rowBreak is >= 2 and <= CellAddress.MaxRow)
                    sheet.RowPageBreaks.Add(rowBreak);
            foreach (var columnBreak in sDto.ColumnPageBreaks ?? [])
                if (columnBreak is >= 2 and <= CellAddress.MaxCol)
                    sheet.ColumnPageBreaks.Add(columnBreak);
            if (sDto.BackgroundImage is { ImageBase64.Length: > 0 } backgroundDto)
            {
                try
                {
                    sheet.BackgroundImage = new WorksheetBackgroundImage(
                        Convert.FromBase64String(backgroundDto.ImageBase64),
                        string.IsNullOrWhiteSpace(backgroundDto.ContentType) ? "image/png" : backgroundDto.ContentType,
                        backgroundDto.FileName);
                }
                catch (FormatException) { /* skip invalid background image payloads */ }
            }
            foreach (var pictureDto in sDto.Pictures ?? [])
            {
                if (pictureDto?.Anchor is null) continue;
                try
                {
                    var picture = new PictureModel
                    {
                        Anchor = CellAddress.Parse(pictureDto.Anchor, sheet.Id),
                        Kind = ValidEnumOrDefault(pictureDto.Kind, PictureKind.CellRangeSnapshot),
                        SourceRowCount = pictureDto.SourceRowCount,
                        SourceColumnCount = pictureDto.SourceColumnCount,
                        ImageBytes = string.IsNullOrEmpty(pictureDto.ImageBase64) ? null : Convert.FromBase64String(pictureDto.ImageBase64),
                        ContentType = pictureDto.ContentType,
                        Width = PositiveFiniteOrDefault(pictureDto.Width, 240),
                        Height = PositiveFiniteOrDefault(pictureDto.Height, 140),
                        RotationDegrees = NormalizeRotation(pictureDto.RotationDegrees),
                        AltText = pictureDto.AltText
                    };
                    foreach (var cellDto in pictureDto.Cells ?? [])
                        picture.Cells.Add(new PictureCellSnapshot(cellDto.RowOffset, cellDto.ColumnOffset, cellDto.Text ?? ""));
                    sheet.Pictures.Add(picture);
                }
                catch (FormatException) { /* skip pictures with unparseable anchors */ }
            }
            foreach (var textBoxDto in sDto.TextBoxes ?? [])
            {
                if (textBoxDto?.Anchor is null) continue;
                try
                {
                    sheet.TextBoxes.Add(new TextBoxModel
                    {
                        Anchor = CellAddress.Parse(textBoxDto.Anchor, sheet.Id),
                        Text = textBoxDto.Text ?? "",
                        Width = PositiveFiniteOrDefault(textBoxDto.Width, 180),
                        Height = PositiveFiniteOrDefault(textBoxDto.Height, 80),
                        RotationDegrees = NormalizeRotation(textBoxDto.RotationDegrees),
                        FillColor = textBoxDto.FillColor is { } textFill ? ParseColor(textFill) : null,
                        OutlineColor = textBoxDto.OutlineColor is { } textOutline ? ParseColor(textOutline) : null,
                        AltText = textBoxDto.AltText
                    });
                }
                catch (FormatException) { /* skip text boxes with unparseable anchors */ }
            }
            foreach (var shapeDto in sDto.DrawingShapes ?? [])
            {
                if (shapeDto?.Anchor is null) continue;
                try
                {
                    sheet.DrawingShapes.Add(new DrawingShapeModel
                    {
                        Anchor = CellAddress.Parse(shapeDto.Anchor, sheet.Id),
                        Kind = ValidEnumOrDefault(shapeDto.Kind, DrawingShapeKind.Rectangle),
                        Width = PositiveFiniteOrDefault(shapeDto.Width, 120),
                        Height = PositiveFiniteOrDefault(shapeDto.Height, 70),
                        RotationDegrees = NormalizeRotation(shapeDto.RotationDegrees),
                        FillColor = shapeDto.FillColor is { } shapeFill ? ParseColor(shapeFill) : null,
                        OutlineColor = shapeDto.OutlineColor is { } shapeOutline ? ParseColor(shapeOutline) : null,
                        AltText = shapeDto.AltText
                    });
                }
                catch (FormatException) { /* skip shapes with unparseable anchors */ }
            }
            foreach (var chartDto in sDto.Charts ?? [])
            {
                if (chartDto?.DataRange is null)
                    continue;

                try
                {
                    var range = GridRange.Parse(chartDto.DataRange, sheet.Id);
                    var chart = new ChartModel
                    {
                        Type = chartDto.Type,
                        DataRange = range,
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
                        PlotAreaFillColor = chartDto.PlotAreaFillColor,
                        PlotAreaBorderColor = chartDto.PlotAreaBorderColor,
                        PlotAreaBorderThickness = chartDto.PlotAreaBorderThickness,
                        LegendTextColor = chartDto.LegendTextColor,
                        LegendFillColor = chartDto.LegendFillColor,
                        LegendBorderColor = chartDto.LegendBorderColor,
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
                        DataLabelBorderColor = chartDto.DataLabelBorderColor,
                        DataLabelTextColor = chartDto.DataLabelTextColor,
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
                        TrendlineThickness = chartDto.TrendlineThickness,
                        TrendlineDashStyle = chartDto.TrendlineDashStyle,
                        ShowSecondaryAxis = chartDto.ShowSecondaryAxis,
                        SecondaryAxisSeriesIndexes = chartDto.SecondaryAxisSeriesIndexes ?? [],
                        ComboLineSeriesIndexes = chartDto.ComboLineSeriesIndexes ?? [],
                        SeriesFormats = chartDto.SeriesFormats ?? [],
                        PointDataLabelFormats = chartDto.PointDataLabelFormats ?? [],
                        UseComboLineForSecondarySeries = chartDto.UseComboLineForSecondarySeries,
                        Left = chartDto.Left,
                        Top = chartDto.Top,
                        Width = PositiveFiniteOrDefault(chartDto.Width, 400),
                        Height = PositiveFiniteOrDefault(chartDto.Height, 300)
                    };
                    SanitizeLoadedChart(chart);
                    sheet.Charts.Add(chart);
                }
                catch (FormatException) { /* skip charts with unparseable ranges */ }
            }

            foreach (var validationDto in sDto.DataValidations ?? [])
            {
                if (string.IsNullOrWhiteSpace(validationDto?.AppliesTo))
                    continue;

                try
                {
                    sheet.DataValidations.Add(new DataValidation
                    {
                        AppliesTo = GridRange.Parse(validationDto.AppliesTo, sheet.Id),
                        Type = Enum.IsDefined(validationDto.Type) ? validationDto.Type : DvType.Any,
                        Operator = Enum.IsDefined(validationDto.Operator) ? validationDto.Operator : DvOperator.Between,
                        Formula1 = validationDto.Formula1,
                        Formula2 = validationDto.Formula2,
                        AllowBlank = validationDto.AllowBlank,
                        ShowDropdown = validationDto.ShowDropdown,
                        AlertStyle = Enum.IsDefined(validationDto.AlertStyle) ? validationDto.AlertStyle : DvAlertStyle.Stop,
                        ShowInputMessage = validationDto.ShowInputMessage,
                        ShowErrorMessage = validationDto.ShowErrorMessage,
                        ErrorTitle = validationDto.ErrorTitle,
                        ErrorMessage = validationDto.ErrorMessage,
                        PromptTitle = validationDto.PromptTitle,
                        PromptMessage = validationDto.PromptMessage
                    });
                }
                catch (FormatException) { /* skip validations with unparseable ranges */ }
            }

            foreach (var cDto in sDto.Cells ?? [])
            {
                if (string.IsNullOrEmpty(cDto?.Address)) continue;
                try
                {
                    var addr = CellAddress.Parse(cDto.Address, sheet.Id);
                    var cell = cDto.Formula != null
                        ? Cell.FromFormula(cDto.Formula)
                        : Cell.FromValue(DeserializeValue(cDto.Value, cDto.ValueType));
                    cell.IgnoreFormulaError = cDto.IgnoreFormulaError;
                    sheet.SetCell(addr, cell);
                }
                catch (FormatException) { /* skip cells with unparseable addresses */ }
            }
        }

        foreach (var viewDto in dto.CustomViews ?? [])
        {
            if (string.IsNullOrWhiteSpace(viewDto?.Name)) continue;
            workbook.CustomViews.Add(new WorkbookCustomView(
                viewDto.Name,
                (viewDto.Sheets ?? []).Select(ToWorksheetCustomViewState).ToList()));
        }

        foreach (var watchDto in dto.WatchedCells ?? [])
        {
            if (string.IsNullOrWhiteSpace(watchDto?.SheetName) ||
                string.IsNullOrWhiteSpace(watchDto.Address))
            {
                continue;
            }

            var sheet = workbook.Sheets.FirstOrDefault(s => s.Name == watchDto.SheetName);
            if (sheet is null)
                continue;

            try
            {
                workbook.WatchedCells.Add(CellAddress.Parse(watchDto.Address, sheet.Id));
            }
            catch (FormatException) { /* skip watches with unparseable addresses */ }
        }

        foreach (var scenarioDto in dto.Scenarios ?? [])
        {
            if (string.IsNullOrWhiteSpace(scenarioDto?.Name))
                continue;

            var changes = new List<ScenarioCellValue>();
            foreach (var changeDto in scenarioDto.ChangingCells ?? [])
            {
                if (string.IsNullOrWhiteSpace(changeDto?.SheetName) ||
                    string.IsNullOrWhiteSpace(changeDto.Address))
                {
                    continue;
                }

                var sheet = workbook.Sheets.FirstOrDefault(s => s.Name == changeDto.SheetName);
                if (sheet is null)
                    continue;

                try
                {
                    changes.Add(new ScenarioCellValue(
                        CellAddress.Parse(changeDto.Address, sheet.Id),
                        DeserializeValue(changeDto.Value, changeDto.ValueType)));
                }
                catch (FormatException) { /* skip scenarios with unparseable addresses */ }
            }

            if (changes.Count > 0)
                workbook.Scenarios.Add(new WorkbookScenario(scenarioDto.Name, changes));
        }

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        var dto = new WorkbookDto
        {
            Name = workbook.Name,
            WindowArrangement = workbook.WindowArrangement,
            DisabledFormulaErrorCodes = workbook.DisabledFormulaErrorCodes.OrderBy(code => code).ToList(),
            CustomViews = workbook.CustomViews.Select(view => new CustomViewDto
            {
                Name = view.Name,
                Sheets = view.Sheets.Select(sheet => new CustomViewSheetDto
                {
                    SheetName = sheet.SheetName,
                    ViewMode = sheet.ViewMode,
                    FrozenRows = sheet.FrozenRows,
                    FrozenCols = sheet.FrozenCols,
                    SplitRow = sheet.SplitRow,
                    SplitColumn = sheet.SplitColumn,
                    ShowGridlines = sheet.ShowGridlines,
                    ShowHeadings = sheet.ShowHeadings,
                    ShowRulers = sheet.ShowRulers,
                    ZoomPercent = sheet.ZoomPercent,
                    ShowFormulas = sheet.ShowFormulas
                }).ToList()
            }).ToList(),
            WatchedCells = workbook.WatchedCells.Select(address =>
            {
                var sheet = workbook.Sheets.FirstOrDefault(s => s.Id.Equals(address.Sheet));
                return sheet is null
                    ? null
                    : new WatchedCellDto { SheetName = sheet.Name, Address = address.ToA1() };
            }).OfType<WatchedCellDto>().ToList(),
            Scenarios = workbook.Scenarios.Select(scenario => new ScenarioDto
            {
                Name = scenario.Name,
                ChangingCells = scenario.ChangingCells.Select(change =>
                {
                    var sheet = workbook.Sheets.FirstOrDefault(s => s.Id.Equals(change.Address.Sheet));
                    return sheet is null
                        ? null
                        : new ScenarioCellDto
                        {
                            SheetName = sheet.Name,
                            Address = change.Address.ToA1(),
                            Value = SerializeValue(change.Value),
                            ValueType = GetValueType(change.Value)
                        };
                }).OfType<ScenarioCellDto>().ToList()
            }).Where(scenario => scenario.ChangingCells.Count > 0).ToList(),
            Sheets = workbook.Sheets.Select(s => new SheetDto
            {
                Name = s.Name,
                IsHidden = s.IsHidden,
                TabColor = s.TabColor is { } color ? FormatColor(color) : null,
                ViewMode = s.ViewMode,
                ShowGridlines = s.ShowGridlines,
                ShowHeadings = s.ShowHeadings,
                ShowRulers = s.ShowRulers,
                ZoomPercent = s.ZoomPercent,
                ShowFormulas = s.ShowFormulas,
                FrozenRows = s.FrozenRows,
                FrozenCols = s.FrozenCols,
                SplitRow = s.SplitRow,
                SplitColumn = s.SplitColumn,
                PrintArea = s.PrintArea?.ToString(),
                PageOrientation = s.PageOrientation,
                PaperSize = s.PaperSize,
                PageMargins = new PageMarginsDto
                {
                    Left = s.PageMargins.Left,
                    Right = s.PageMargins.Right,
                    Top = s.PageMargins.Top,
                    Bottom = s.PageMargins.Bottom
                },
                HeaderMargin = s.HeaderMargin,
                FooterMargin = s.FooterMargin,
                PrintGridlines = s.PrintGridlines,
                PrintHeadings = s.PrintHeadings,
                PrintTitleRows = FromRepeatRange(s.PrintTitleRows),
                PrintTitleColumns = FromRepeatRange(s.PrintTitleColumns),
                PageHeader = FromHeaderFooter(s.PageHeader),
                PageFooter = FromHeaderFooter(s.PageFooter),
                FirstPageHeader = FromHeaderFooter(s.FirstPageHeader),
                FirstPageFooter = FromHeaderFooter(s.FirstPageFooter),
                EvenPageHeader = FromHeaderFooter(s.EvenPageHeader),
                EvenPageFooter = FromHeaderFooter(s.EvenPageFooter),
                DifferentFirstPageHeaderFooter = s.DifferentFirstPageHeaderFooter,
                DifferentOddEvenHeaderFooter = s.DifferentOddEvenHeaderFooter,
                HeaderFooterScaleWithDocument = s.HeaderFooterScaleWithDocument,
                HeaderFooterAlignWithMargins = s.HeaderFooterAlignWithMargins,
                CenterHorizontallyOnPage = s.CenterHorizontallyOnPage,
                CenterVerticallyOnPage = s.CenterVerticallyOnPage,
                PageOrder = s.PageOrder,
                FirstPageNumber = s.FirstPageNumber,
                PrintBlackAndWhite = s.PrintBlackAndWhite,
                PrintDraftQuality = s.PrintDraftQuality,
                PrintQualityDpi = s.PrintQualityDpi,
                PrintErrorValue = s.PrintErrorValue,
                PrintComments = s.PrintComments,
                ScaleToFit = new ScaleToFitDto
                {
                    ScalePercent = s.ScaleToFit.ScalePercent,
                    FitToPagesWide = s.ScaleToFit.FitToPagesWide,
                    FitToPagesTall = s.ScaleToFit.FitToPagesTall
                },
                RowPageBreaks = s.RowPageBreaks.ToList(),
                ColumnPageBreaks = s.ColumnPageBreaks.ToList(),
                BackgroundImage = s.BackgroundImage is { } background
                    ? new WorksheetBackgroundDto
                    {
                        ImageBase64 = Convert.ToBase64String(background.ImageBytes),
                        ContentType = background.ContentType,
                        FileName = background.FileName
                    }
                    : null,
                Pictures = s.Pictures.Select(picture => new PictureDto
                {
                    Anchor = picture.Anchor.ToA1(),
                    Kind = picture.Kind,
                    SourceRowCount = picture.SourceRowCount,
                    SourceColumnCount = picture.SourceColumnCount,
                    ImageBase64 = picture.ImageBytes is { Length: > 0 } bytes ? Convert.ToBase64String(bytes) : null,
                    ContentType = picture.ContentType,
                    Width = picture.Width,
                    Height = picture.Height,
                    RotationDegrees = picture.RotationDegrees,
                    AltText = picture.AltText,
                    Cells = picture.Cells.Select(cell => new PictureCellDto
                    {
                        RowOffset = cell.RowOffset,
                        ColumnOffset = cell.ColumnOffset,
                        Text = cell.Text
                    }).ToList()
                }).ToList(),
                TextBoxes = s.TextBoxes.Select(textBox => new TextBoxDto
                {
                    Anchor = textBox.Anchor.ToA1(),
                    Text = textBox.Text,
                    Width = textBox.Width,
                    Height = textBox.Height,
                    RotationDegrees = textBox.RotationDegrees,
                    FillColor = textBox.FillColor is { } textFill ? FormatColor(textFill) : null,
                    OutlineColor = textBox.OutlineColor is { } textOutline ? FormatColor(textOutline) : null,
                    AltText = textBox.AltText
                }).ToList(),
                DrawingShapes = s.DrawingShapes.Select(shape => new DrawingShapeDto
                {
                    Anchor = shape.Anchor.ToA1(),
                    Kind = shape.Kind,
                    Width = shape.Width,
                    Height = shape.Height,
                    RotationDegrees = shape.RotationDegrees,
                    FillColor = shape.FillColor is { } shapeFill ? FormatColor(shapeFill) : null,
                    OutlineColor = shape.OutlineColor is { } shapeOutline ? FormatColor(shapeOutline) : null,
                    AltText = shape.AltText
                }).ToList(),
                Charts = s.Charts.Select(chart => new ChartDto
                {
                    Type = chart.Type,
                    DataRange = chart.DataRange.ToString(),
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
                    PlotAreaFillColor = chart.PlotAreaFillColor,
                    PlotAreaBorderColor = chart.PlotAreaBorderColor,
                    PlotAreaBorderThickness = chart.PlotAreaBorderThickness,
                    LegendTextColor = chart.LegendTextColor,
                    LegendFillColor = chart.LegendFillColor,
                    LegendBorderColor = chart.LegendBorderColor,
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
                    DataLabelBorderColor = chart.DataLabelBorderColor,
                    DataLabelTextColor = chart.DataLabelTextColor,
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
                    TrendlineThickness = chart.TrendlineThickness,
                    TrendlineDashStyle = chart.TrendlineDashStyle,
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
                }).ToList(),
                DataValidations = s.DataValidations.Select(validation => new DataValidationDto
                {
                    AppliesTo = validation.AppliesTo.ToString(),
                    Type = validation.Type,
                    Operator = validation.Operator,
                    Formula1 = validation.Formula1,
                    Formula2 = validation.Formula2,
                    AllowBlank = validation.AllowBlank,
                    ShowDropdown = validation.ShowDropdown,
                    AlertStyle = validation.AlertStyle,
                    ShowInputMessage = validation.ShowInputMessage,
                    ShowErrorMessage = validation.ShowErrorMessage,
                    ErrorTitle = validation.ErrorTitle,
                    ErrorMessage = validation.ErrorMessage,
                    PromptTitle = validation.PromptTitle,
                    PromptMessage = validation.PromptMessage
                }).ToList(),
                Cells = s.GetUsedCells().Select(pair => new CellDto
                {
                    Address   = pair.Key.ToA1(),
                    Value     = SerializeValue(pair.Value.Value),
                    ValueType = GetValueType(pair.Value.Value),
                    Formula   = pair.Value.HasFormula ? pair.Value.FormulaText : null,
                    IgnoreFormulaError = pair.Value.IgnoreFormulaError
                }).ToList()
            }).ToList()
        };

        JsonSerializer.Serialize(stream, dto, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string? SerializeValue(ScalarValue value) => value switch
    {
        BlankValue  => null,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b   => b.Value ? "TRUE" : "FALSE",
        TextValue t   => t.Value,
        ErrorValue e  => e.Code,
        _             => null,
    };

    private static string? GetValueType(ScalarValue value) => value switch
    {
        NumberValue => "n",
        BoolValue   => "b",
        TextValue   => "t",
        ErrorValue  => "e",
        _           => null,
    };

    private static ScalarValue DeserializeValue(string? val, string? type)
    {
        if (val == null) return BlankValue.Instance;
        return type switch
        {
            "n" => double.TryParse(val, System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture, out var d)
                   ? new NumberValue(d) : new TextValue(val),
            "b" => new BoolValue(val == "TRUE"),
            "t" => new TextValue(val),
            "e" => val switch {
                       "#DIV/0!" => ErrorValue.DivByZero,
                       "#VALUE!" => ErrorValue.Value,
                       "#REF!"   => ErrorValue.Ref,
                       "#NAME?"  => ErrorValue.Name,
                       "#NULL!"  => ErrorValue.Null,
                       "#N/A"    => ErrorValue.NA,
                       "#NUM!"   => ErrorValue.Num,
                       _         => new ErrorValue(val)
                   },
            // Legacy files without ValueType: sniff the value
            _   => double.TryParse(val, System.Globalization.NumberStyles.Any,
                       System.Globalization.CultureInfo.InvariantCulture, out var dn)
                   ? new NumberValue(dn)
                   : bool.TryParse(val, out var db) ? new BoolValue(db)
                   : new TextValue(val)
        };
    }

    private static void SanitizeLoadedChart(ChartModel chart)
    {
        chart.Type = ValidEnumOrDefault(chart.Type, ChartType.Column);
        chart.ChartTitleFontSize = Math.Clamp(chart.ChartTitleFontSize, 6, 72);
        chart.AxisTitleFontSize = Math.Clamp(chart.AxisTitleFontSize, 6, 72);
        chart.PlotAreaBorderThickness = Math.Clamp(chart.PlotAreaBorderThickness, 0, 10);
        chart.LegendBorderThickness = Math.Clamp(chart.LegendBorderThickness, 0, 10);
        chart.LegendFontSize = Math.Clamp(chart.LegendFontSize, 6, 72);
        chart.DoughnutHoleSize = Math.Clamp(chart.DoughnutHoleSize, 0.1, 0.9);
        chart.FirstSliceAngle = NormalizeChartAngle(chart.FirstSliceAngle);
        chart.ExplodedSliceDistance = Math.Clamp(chart.ExplodedSliceDistance, 0, 0.5);
        chart.XAxisMajorUnit = ClampPositiveAxisUnit(chart.XAxisMajorUnit);
        chart.XAxisMinorUnit = ClampPositiveAxisUnit(chart.XAxisMinorUnit);
        chart.XAxisNumberFormat = ValidEnumOrDefault(chart.XAxisNumberFormat, ChartDataLabelNumberFormat.General);
        chart.XAxisGridlineThickness = Math.Clamp(chart.XAxisGridlineThickness, 0.25, 10);
        chart.XAxisMajorTickStyle = ValidEnumOrDefault(chart.XAxisMajorTickStyle, ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle = ValidEnumOrDefault(chart.XAxisMinorTickStyle, ChartAxisTickStyle.None);
        chart.XAxisLabelFontSize = Math.Clamp(chart.XAxisLabelFontSize, 6, 72);
        chart.XAxisLabelAngle = Math.Clamp(chart.XAxisLabelAngle, -90, 90);
        chart.XAxisLineThickness = Math.Clamp(chart.XAxisLineThickness, 0.5, 10);
        chart.YAxisMajorUnit = ClampPositiveAxisUnit(chart.YAxisMajorUnit);
        chart.YAxisMinorUnit = ClampPositiveAxisUnit(chart.YAxisMinorUnit);
        chart.YAxisNumberFormat = ValidEnumOrDefault(chart.YAxisNumberFormat, ChartDataLabelNumberFormat.General);
        chart.YAxisGridlineThickness = Math.Clamp(chart.YAxisGridlineThickness, 0.25, 10);
        chart.YAxisMajorTickStyle = ValidEnumOrDefault(chart.YAxisMajorTickStyle, ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle = ValidEnumOrDefault(chart.YAxisMinorTickStyle, ChartAxisTickStyle.None);
        chart.YAxisLabelFontSize = Math.Clamp(chart.YAxisLabelFontSize, 6, 72);
        chart.YAxisLabelAngle = Math.Clamp(chart.YAxisLabelAngle, -90, 90);
        chart.YAxisLineThickness = Math.Clamp(chart.YAxisLineThickness, 0.5, 10);
        chart.LegendPosition = ValidEnumOrDefault(chart.LegendPosition, ChartLegendPosition.Right);
        chart.DataLabelPosition = ValidEnumOrDefault(chart.DataLabelPosition, ChartDataLabelPosition.BestFit);
        chart.DataLabelSeparator = ValidEnumOrDefault(chart.DataLabelSeparator, ChartDataLabelSeparator.Comma);
        chart.DataLabelNumberFormat = ValidEnumOrDefault(chart.DataLabelNumberFormat, ChartDataLabelNumberFormat.General);
        chart.DataLabelBorderThickness = Math.Clamp(chart.DataLabelBorderThickness, 0, 10);
        chart.DataLabelFontSize = Math.Clamp(chart.DataLabelFontSize, 6, 72);
        chart.DataLabelAngle = Math.Clamp(chart.DataLabelAngle, -90, 90);
        chart.TrendlineType = ValidEnumOrDefault(chart.TrendlineType, ChartTrendlineType.Linear);
        chart.TrendlinePeriod = Math.Max(2, chart.TrendlinePeriod);
        chart.TrendlineOrder = Math.Clamp(chart.TrendlineOrder, 2, 6);
        chart.TrendlineThickness = Math.Clamp(chart.TrendlineThickness, 0.5, 10);
        chart.TrendlineDashStyle = ValidEnumOrDefault(chart.TrendlineDashStyle, ChartLineDashStyle.Dash);

        var dataPointCount = ChartTypeSupport.GetDataPointCount(chart);
        if (chart.ExplodedSliceIndex < 0 || chart.ExplodedSliceIndex >= dataPointCount)
            chart.ExplodedSliceIndex = -1;

        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        chart.SecondaryAxisSeriesIndexes = chart.SecondaryAxisSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();
        chart.ComboLineSeriesIndexes = chart.ComboLineSeriesIndexes
            .Where(index => index > 0 && index < seriesCount)
            .Distinct()
            .Order()
            .ToList();
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart))
        {
            chart.UseComboLineForSecondarySeries = false;
            chart.ComboLineSeriesIndexes = [];
        }
        chart.SeriesFormats = chart.SeriesFormats
            .Where(format => format.SeriesIndex >= 0 && format.SeriesIndex < seriesCount)
            .GroupBy(format => format.SeriesIndex)
            .Select(group => ClampSeriesFormat(group.Last()))
            .OrderBy(format => format.SeriesIndex)
            .ToList();
        chart.PointDataLabelFormats = chart.PointDataLabelFormats
            .Where(format => format.SeriesIndex >= 0
                && format.SeriesIndex < seriesCount
                && format.PointIndex >= 0
                && format.PointIndex < dataPointCount)
            .GroupBy(format => (format.SeriesIndex, format.PointIndex))
            .Select(group => ClampPointDataLabelFormat(group.Last()))
            .OrderBy(format => format.SeriesIndex)
            .ThenBy(format => format.PointIndex)
            .ToList();
    }

    private static double? ClampPositiveAxisUnit(double? value) =>
        value is null ? null : Math.Max(double.Epsilon, value.Value);

    private static double NormalizeChartAngle(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static ChartSeriesFormat ClampSeriesFormat(ChartSeriesFormat format) =>
        format with
        {
            StrokeThickness = format.StrokeThickness is { } strokeThickness
                ? Math.Clamp(strokeThickness, 0.5, 10)
                : null,
            MarkerSize = format.MarkerSize is { } markerSize
                ? Math.Clamp(markerSize, 1, 30)
                : null,
            DashStyle = ValidNullableEnumOrNull(format.DashStyle),
            MarkerStyle = ValidNullableEnumOrNull(format.MarkerStyle)
        };

    private static TEnum ValidEnumOrDefault<TEnum>(TEnum value, TEnum defaultValue)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : defaultValue;

    private static TEnum? ValidNullableEnumOrNull<TEnum>(TEnum? value)
        where TEnum : struct, Enum =>
        value is { } enumValue && Enum.IsDefined(enumValue) ? enumValue : null;

    private static double PositiveFiniteOrDefault(double value, double defaultValue) =>
        double.IsFinite(value) && value > 0 ? value : defaultValue;

    private static double NonNegativeFiniteOrDefault(double? value, double defaultValue) =>
        value is { } number && double.IsFinite(number) && number >= 0 ? number : defaultValue;

    private static WorksheetPageMargins ValidPageMarginsOrDefault(WorksheetPageMargins margins, WorksheetPageMargins defaultValue) =>
        IsNonNegativeFinite(margins.Left) &&
        IsNonNegativeFinite(margins.Right) &&
        IsNonNegativeFinite(margins.Top) &&
        IsNonNegativeFinite(margins.Bottom)
            ? margins
            : defaultValue;

    private static WorksheetScaleToFit ValidScaleToFitOrDefault(WorksheetScaleToFit scaleToFit, WorksheetScaleToFit defaultValue) =>
        scaleToFit.ScalePercent is < 10 or > 400 ||
        scaleToFit.FitToPagesWide is < 1 ||
        scaleToFit.FitToPagesTall is < 1
            ? defaultValue
            : scaleToFit;

    private static uint? ValidRowPaneOrNull(uint? row) =>
        row is >= 1 and <= CellAddress.MaxRow ? row : null;

    private static uint? ValidColumnPaneOrNull(uint? column) =>
        column is >= 1 and <= CellAddress.MaxCol ? column : null;

    private static uint ValidFrozenRowsOrZero(uint row) =>
        row <= CellAddress.MaxRow ? row : 0;

    private static uint ValidFrozenColumnsOrZero(uint column) =>
        column <= CellAddress.MaxCol ? column : 0;

    private static WorksheetCustomViewState ToWorksheetCustomViewState(CustomViewSheetDto sheetDto)
    {
        var frozenRows = ValidFrozenRowsOrZero(sheetDto.FrozenRows);
        var frozenCols = ValidFrozenColumnsOrZero(sheetDto.FrozenCols);
        var hasFrozenPanes = frozenRows > 0 || frozenCols > 0;
        return new WorksheetCustomViewState(
            sheetDto.SheetName,
            Enum.IsDefined(sheetDto.ViewMode) ? sheetDto.ViewMode : WorksheetViewMode.Normal,
            frozenRows,
            frozenCols,
            hasFrozenPanes ? null : ValidRowPaneOrNull(sheetDto.SplitRow),
            hasFrozenPanes ? null : ValidColumnPaneOrNull(sheetDto.SplitColumn),
            sheetDto.ShowGridlines ?? true,
            sheetDto.ShowHeadings ?? true,
            sheetDto.ShowRulers ?? true,
            ValidZoomPercentOrDefault(sheetDto.ZoomPercent),
            sheetDto.ShowFormulas ?? false);
    }

    private static int ValidZoomPercentOrDefault(int? zoomPercent) =>
        zoomPercent is >= 10 and <= 400 ? zoomPercent.Value : 100;

    private static bool IsNonNegativeFinite(double value) =>
        double.IsFinite(value) && value >= 0;

    private static double NormalizeRotation(double value)
    {
        var normalized = value % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

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

    private static string FormatColor(CellColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static CellColor? ParseColor(string text)
    {
        var normalized = text.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];
        if (normalized.Length != 6 ||
            !byte.TryParse(normalized[..2], System.Globalization.NumberStyles.HexNumber, null, out var r) ||
            !byte.TryParse(normalized[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) ||
            !byte.TryParse(normalized[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return null;
        }

        return new CellColor(r, g, b);
    }

    private static WorksheetRepeatRange? ToRepeatRange(RepeatRangeDto? dto) =>
        dto is null ? null : new WorksheetRepeatRange(dto.Start, dto.End);

    private static RepeatRangeDto? FromRepeatRange(WorksheetRepeatRange? range) =>
        range is null ? null : new RepeatRangeDto { Start = range.Value.Start, End = range.Value.End };

    private static WorksheetHeaderFooter ToHeaderFooter(HeaderFooterDto? dto) =>
        dto is null
            ? new WorksheetHeaderFooter("", "", "")
            : new WorksheetHeaderFooter(dto.Left ?? "", dto.Center ?? "", dto.Right ?? "");

    private static HeaderFooterDto FromHeaderFooter(WorksheetHeaderFooter value) =>
        new() { Left = value.Left, Center = value.Center, Right = value.Right };

    private class WorkbookDto
    {
        public string Name { get; set; } = "";
        public WorkbookWindowArrangement? WindowArrangement { get; set; }
        public List<string> DisabledFormulaErrorCodes { get; set; } = [];
        public List<CustomViewDto> CustomViews { get; set; } = [];
        public List<WatchedCellDto> WatchedCells { get; set; } = [];
        public List<ScenarioDto> Scenarios { get; set; } = [];
        public List<SheetDto> Sheets { get; set; } = [];
    }

    private class WatchedCellDto
    {
        public string SheetName { get; set; } = "";
        public string Address { get; set; } = "";
    }

    private class ScenarioDto
    {
        public string Name { get; set; } = "";
        public List<ScenarioCellDto> ChangingCells { get; set; } = [];
    }

    private class ScenarioCellDto
    {
        public string SheetName { get; set; } = "";
        public string Address { get; set; } = "";
        public string? Value { get; set; }
        public string? ValueType { get; set; }
    }

    private class CustomViewDto
    {
        public string Name { get; set; } = "";
        public List<CustomViewSheetDto> Sheets { get; set; } = [];
    }

    private class CustomViewSheetDto
    {
        public string SheetName { get; set; } = "";
        public WorksheetViewMode ViewMode { get; set; } = WorksheetViewMode.Normal;
        public uint FrozenRows { get; set; }
        public uint FrozenCols { get; set; }
        public uint? SplitRow { get; set; }
        public uint? SplitColumn { get; set; }
        public bool? ShowGridlines { get; set; }
        public bool? ShowHeadings { get; set; }
        public bool? ShowRulers { get; set; }
        public int? ZoomPercent { get; set; }
        public bool? ShowFormulas { get; set; }
    }

    private class SheetDto
    {
        public string Name { get; set; } = "";
        public bool IsHidden { get; set; }
        public string? TabColor { get; set; }
        public WorksheetViewMode ViewMode { get; set; } = WorksheetViewMode.Normal;
        public bool? ShowGridlines { get; set; }
        public bool? ShowHeadings { get; set; }
        public bool? ShowRulers { get; set; }
        public int? ZoomPercent { get; set; }
        public bool? ShowFormulas { get; set; }
        public uint FrozenRows { get; set; }
        public uint FrozenCols { get; set; }
        public uint? SplitRow { get; set; }
        public uint? SplitColumn { get; set; }
        public string? PrintArea { get; set; }
        public WorksheetPageOrientation? PageOrientation { get; set; }
        public WorksheetPaperSize? PaperSize { get; set; }
        public PageMarginsDto? PageMargins { get; set; }
        public double? HeaderMargin { get; set; }
        public double? FooterMargin { get; set; }
        public bool PrintGridlines { get; set; }
        public bool PrintHeadings { get; set; }
        public RepeatRangeDto? PrintTitleRows { get; set; }
        public RepeatRangeDto? PrintTitleColumns { get; set; }
        public HeaderFooterDto? PageHeader { get; set; }
        public HeaderFooterDto? PageFooter { get; set; }
        public HeaderFooterDto? FirstPageHeader { get; set; }
        public HeaderFooterDto? FirstPageFooter { get; set; }
        public HeaderFooterDto? EvenPageHeader { get; set; }
        public HeaderFooterDto? EvenPageFooter { get; set; }
        public bool DifferentFirstPageHeaderFooter { get; set; }
        public bool DifferentOddEvenHeaderFooter { get; set; }
        public bool? HeaderFooterScaleWithDocument { get; set; }
        public bool? HeaderFooterAlignWithMargins { get; set; }
        public bool CenterHorizontallyOnPage { get; set; }
        public bool CenterVerticallyOnPage { get; set; }
        public WorksheetPageOrder? PageOrder { get; set; }
        public int? FirstPageNumber { get; set; }
        public bool PrintBlackAndWhite { get; set; }
        public bool PrintDraftQuality { get; set; }
        public int? PrintQualityDpi { get; set; }
        public WorksheetPrintErrorValue? PrintErrorValue { get; set; }
        public WorksheetPrintComments? PrintComments { get; set; }
        public ScaleToFitDto? ScaleToFit { get; set; }
        public List<uint> RowPageBreaks { get; set; } = [];
        public List<uint> ColumnPageBreaks { get; set; } = [];
        public WorksheetBackgroundDto? BackgroundImage { get; set; }
        public List<PictureDto> Pictures { get; set; } = [];
        public List<TextBoxDto> TextBoxes { get; set; } = [];
        public List<DrawingShapeDto> DrawingShapes { get; set; } = [];
        public List<ChartDto> Charts { get; set; } = [];
        public List<DataValidationDto> DataValidations { get; set; } = [];
        public List<CellDto> Cells { get; set; } = [];
    }

    private class DataValidationDto
    {
        public string? AppliesTo { get; set; }
        public DvType Type { get; set; } = DvType.Any;
        public DvOperator Operator { get; set; } = DvOperator.Between;
        public string? Formula1 { get; set; }
        public string? Formula2 { get; set; }
        public bool AllowBlank { get; set; } = true;
        public bool ShowDropdown { get; set; } = true;
        public DvAlertStyle AlertStyle { get; set; } = DvAlertStyle.Stop;
        public bool ShowInputMessage { get; set; } = true;
        public bool ShowErrorMessage { get; set; } = true;
        public string? ErrorTitle { get; set; }
        public string? ErrorMessage { get; set; }
        public string? PromptTitle { get; set; }
        public string? PromptMessage { get; set; }
    }

    private class PageMarginsDto
    {
        public double Left { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
    }

    private class RepeatRangeDto
    {
        public uint Start { get; set; }
        public uint End { get; set; }
    }

    private class ScaleToFitDto
    {
        public int? ScalePercent { get; set; }
        public int? FitToPagesWide { get; set; }
        public int? FitToPagesTall { get; set; }
    }

    private class HeaderFooterDto
    {
        public string? Left { get; set; }
        public string? Center { get; set; }
        public string? Right { get; set; }
    }

    private class WorksheetBackgroundDto
    {
        public string ImageBase64 { get; set; } = "";
        public string ContentType { get; set; } = "image/png";
        public string? FileName { get; set; }
    }

    private class PictureDto
    {
        public string? Anchor { get; set; }
        public PictureKind Kind { get; set; } = PictureKind.CellRangeSnapshot;
        public uint SourceRowCount { get; set; }
        public uint SourceColumnCount { get; set; }
        public string? ImageBase64 { get; set; }
        public string? ContentType { get; set; }
        public double Width { get; set; } = 240;
        public double Height { get; set; } = 140;
        public double RotationDegrees { get; set; }
        public string? AltText { get; set; }
        public List<PictureCellDto> Cells { get; set; } = [];
    }

    private class TextBoxDto
    {
        public string? Anchor { get; set; }
        public string? Text { get; set; }
        public double Width { get; set; } = 180;
        public double Height { get; set; } = 80;
        public double RotationDegrees { get; set; }
        public string? FillColor { get; set; }
        public string? OutlineColor { get; set; }
        public string? AltText { get; set; }
    }

    private class DrawingShapeDto
    {
        public string? Anchor { get; set; }
        public DrawingShapeKind Kind { get; set; } = DrawingShapeKind.Rectangle;
        public double Width { get; set; } = 120;
        public double Height { get; set; } = 70;
        public double RotationDegrees { get; set; }
        public string? FillColor { get; set; }
        public string? OutlineColor { get; set; }
        public string? AltText { get; set; }
    }

    private class PictureCellDto
    {
        public uint RowOffset { get; set; }
        public uint ColumnOffset { get; set; }
        public string? Text { get; set; }
    }

    private class ChartDto
    {
        public ChartType Type { get; set; } = ChartType.Column;
        public string? DataRange { get; set; }
        public bool FirstRowIsHeader { get; set; } = true;
        public bool FirstColIsCategories { get; set; } = true;
        public string? Title { get; set; }
        public string? XAxisTitle { get; set; }
        public string? YAxisTitle { get; set; }
        public CellColor? ChartTitleTextColor { get; set; }
        public double ChartTitleFontSize { get; set; } = 16;
        public CellColor? AxisTitleTextColor { get; set; }
        public double AxisTitleFontSize { get; set; } = 12;
        public CellColor? ChartAreaFillColor { get; set; }
        public CellColor? PlotAreaFillColor { get; set; }
        public CellColor? PlotAreaBorderColor { get; set; }
        public double PlotAreaBorderThickness { get; set; } = 1;
        public CellColor? LegendTextColor { get; set; }
        public CellColor? LegendFillColor { get; set; }
        public CellColor? LegendBorderColor { get; set; }
        public double LegendBorderThickness { get; set; }
        public double LegendFontSize { get; set; } = 12;
        public double DoughnutHoleSize { get; set; } = 0.55;
        public double FirstSliceAngle { get; set; }
        public int ExplodedSliceIndex { get; set; } = -1;
        public double ExplodedSliceDistance { get; set; } = 0.1;
        public double? XAxisMinimum { get; set; }
        public double? XAxisMaximum { get; set; }
        public double? XAxisMajorUnit { get; set; }
        public double? XAxisMinorUnit { get; set; }
        public bool XAxisLogScale { get; set; }
        public ChartDataLabelNumberFormat XAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
        public bool ShowXAxisMajorGridlines { get; set; }
        public bool ShowXAxisMinorGridlines { get; set; }
        public CellColor? XAxisMajorGridlineColor { get; set; }
        public CellColor? XAxisMinorGridlineColor { get; set; }
        public double XAxisGridlineThickness { get; set; } = 1;
        public ChartAxisTickStyle XAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
        public ChartAxisTickStyle XAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
        public bool ShowXAxisLabels { get; set; } = true;
        public CellColor? XAxisLabelTextColor { get; set; }
        public double XAxisLabelFontSize { get; set; } = 11;
        public double XAxisLabelAngle { get; set; }
        public CellColor? XAxisLineColor { get; set; }
        public double XAxisLineThickness { get; set; } = 1;
        public double? YAxisMinimum { get; set; }
        public double? YAxisMaximum { get; set; }
        public double? YAxisMajorUnit { get; set; }
        public double? YAxisMinorUnit { get; set; }
        public bool YAxisLogScale { get; set; }
        public ChartDataLabelNumberFormat YAxisNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
        public bool ShowYAxisMajorGridlines { get; set; }
        public bool ShowYAxisMinorGridlines { get; set; }
        public CellColor? YAxisMajorGridlineColor { get; set; }
        public CellColor? YAxisMinorGridlineColor { get; set; }
        public double YAxisGridlineThickness { get; set; } = 1;
        public ChartAxisTickStyle YAxisMajorTickStyle { get; set; } = ChartAxisTickStyle.Outside;
        public ChartAxisTickStyle YAxisMinorTickStyle { get; set; } = ChartAxisTickStyle.None;
        public bool ShowYAxisLabels { get; set; } = true;
        public CellColor? YAxisLabelTextColor { get; set; }
        public double YAxisLabelFontSize { get; set; } = 11;
        public double YAxisLabelAngle { get; set; }
        public CellColor? YAxisLineColor { get; set; }
        public double YAxisLineThickness { get; set; } = 1;
        public ChartLegendPosition LegendPosition { get; set; } = ChartLegendPosition.Right;
        public bool LegendOverlay { get; set; }
        public bool ShowLegend { get; set; } = true;
        public bool ShowDataLabels { get; set; }
        public ChartDataLabelPosition DataLabelPosition { get; set; } = ChartDataLabelPosition.BestFit;
        public bool ShowDataLabelCategoryName { get; set; }
        public bool ShowDataLabelSeriesName { get; set; }
        public bool ShowDataLabelPercentage { get; set; }
        public ChartDataLabelSeparator DataLabelSeparator { get; set; } = ChartDataLabelSeparator.Comma;
        public ChartDataLabelNumberFormat DataLabelNumberFormat { get; set; } = ChartDataLabelNumberFormat.General;
        public bool ShowDataLabelCallouts { get; set; }
        public CellColor? DataLabelFillColor { get; set; }
        public CellColor? DataLabelBorderColor { get; set; }
        public CellColor? DataLabelTextColor { get; set; }
        public double DataLabelBorderThickness { get; set; }
        public double DataLabelFontSize { get; set; } = 11;
        public double DataLabelAngle { get; set; }
        public bool ShowLinearTrendline { get; set; }
        public ChartTrendlineType TrendlineType { get; set; } = ChartTrendlineType.Linear;
        public int TrendlinePeriod { get; set; } = 2;
        public int TrendlineOrder { get; set; } = 2;
        public bool ShowTrendlineEquation { get; set; }
        public bool ShowTrendlineRSquared { get; set; }
        public CellColor? TrendlineColor { get; set; }
        public double TrendlineThickness { get; set; } = 1.5;
        public ChartLineDashStyle TrendlineDashStyle { get; set; } = ChartLineDashStyle.Dash;
        public bool ShowSecondaryAxis { get; set; }
        public List<int>? SecondaryAxisSeriesIndexes { get; set; }
        public List<int>? ComboLineSeriesIndexes { get; set; }
        public List<ChartSeriesFormat>? SeriesFormats { get; set; }
        public List<ChartPointDataLabelFormat>? PointDataLabelFormats { get; set; }
        public bool UseComboLineForSecondarySeries { get; set; }
        public double Left { get; set; } = 50;
        public double Top { get; set; } = 50;
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 300;
    }

    private class CellDto
    {
        public string Address { get; set; } = "";
        public string? Value { get; set; }
        public string? ValueType { get; set; }
        public string? Formula { get; set; }
        public bool IgnoreFormulaError { get; set; }
    }
}
