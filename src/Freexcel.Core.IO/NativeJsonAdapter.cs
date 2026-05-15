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
        foreach (var sDto in dto.Sheets ?? [])
        {
            if (string.IsNullOrEmpty(sDto?.Name)) continue;
            var sheet = workbook.AddSheet(sDto.Name);
            sheet.IsHidden = sDto.IsHidden;
            sheet.TabColor = sDto.TabColor is { } tabColor ? ParseColor(tabColor) : null;
            sheet.ViewMode = Enum.IsDefined(sDto.ViewMode) ? sDto.ViewMode : WorksheetViewMode.Normal;
            sheet.SplitRow = sDto.SplitRow;
            sheet.SplitColumn = sDto.SplitColumn;
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
                sheet.PageMargins = new WorksheetPageMargins(margins.Left, margins.Right, margins.Top, margins.Bottom);
            sheet.HeaderMargin = sDto.HeaderMargin ?? 0.3;
            sheet.FooterMargin = sDto.FooterMargin ?? 0.3;
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
            sheet.FirstPageNumber = sDto.FirstPageNumber;
            sheet.PrintBlackAndWhite = sDto.PrintBlackAndWhite;
            sheet.PrintDraftQuality = sDto.PrintDraftQuality;
            sheet.PrintQualityDpi = sDto.PrintQualityDpi;
            if (sDto.PrintErrorValue is { } printErrorValue && Enum.IsDefined(printErrorValue))
                sheet.PrintErrorValue = printErrorValue;
            if (sDto.PrintComments is { } printComments && Enum.IsDefined(printComments))
                sheet.PrintComments = printComments;
            if (sDto.ScaleToFit is { } scaleToFit)
                sheet.ScaleToFit = new WorksheetScaleToFit(scaleToFit.ScalePercent, scaleToFit.FitToPagesWide, scaleToFit.FitToPagesTall);
            foreach (var rowBreak in sDto.RowPageBreaks ?? [])
                sheet.RowPageBreaks.Add(rowBreak);
            foreach (var columnBreak in sDto.ColumnPageBreaks ?? [])
                sheet.ColumnPageBreaks.Add(columnBreak);
            foreach (var pictureDto in sDto.Pictures ?? [])
            {
                if (pictureDto?.Anchor is null) continue;
                try
                {
                    var picture = new PictureModel
                    {
                        Anchor = CellAddress.Parse(pictureDto.Anchor, sheet.Id),
                        Kind = pictureDto.Kind,
                        SourceRowCount = pictureDto.SourceRowCount,
                        SourceColumnCount = pictureDto.SourceColumnCount,
                        ImageBytes = string.IsNullOrEmpty(pictureDto.ImageBase64) ? null : Convert.FromBase64String(pictureDto.ImageBase64),
                        ContentType = pictureDto.ContentType,
                        Width = pictureDto.Width,
                        Height = pictureDto.Height,
                        RotationDegrees = pictureDto.RotationDegrees
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
                        Width = textBoxDto.Width,
                        Height = textBoxDto.Height,
                        RotationDegrees = textBoxDto.RotationDegrees,
                        FillColor = textBoxDto.FillColor is { } textFill ? ParseColor(textFill) : null,
                        OutlineColor = textBoxDto.OutlineColor is { } textOutline ? ParseColor(textOutline) : null
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
                        Kind = shapeDto.Kind,
                        Width = shapeDto.Width,
                        Height = shapeDto.Height,
                        RotationDegrees = shapeDto.RotationDegrees,
                        FillColor = shapeDto.FillColor is { } shapeFill ? ParseColor(shapeFill) : null,
                        OutlineColor = shapeDto.OutlineColor is { } shapeOutline ? ParseColor(shapeOutline) : null
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
                    sheet.Charts.Add(new ChartModel
                    {
                        Type = chartDto.Type,
                        DataRange = range,
                        FirstRowIsHeader = chartDto.FirstRowIsHeader,
                        FirstColIsCategories = chartDto.FirstColIsCategories,
                        Title = chartDto.Title,
                        XAxisTitle = chartDto.XAxisTitle,
                        YAxisTitle = chartDto.YAxisTitle,
                        LegendPosition = chartDto.LegendPosition,
                        ShowLegend = chartDto.ShowLegend,
                        Left = chartDto.Left,
                        Top = chartDto.Top,
                        Width = chartDto.Width,
                        Height = chartDto.Height
                    });
                }
                catch (FormatException) { /* skip charts with unparseable ranges */ }
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
                (viewDto.Sheets ?? []).Select(sheetDto => new WorksheetCustomViewState(
                    sheetDto.SheetName,
                    Enum.IsDefined(sheetDto.ViewMode) ? sheetDto.ViewMode : WorksheetViewMode.Normal,
                    sheetDto.FrozenRows,
                    sheetDto.FrozenCols,
                    sheetDto.SplitRow,
                    sheetDto.SplitColumn)).ToList()));
        }

        return workbook;
    }

    public void Save(Workbook workbook, Stream stream)
    {
        var dto = new WorkbookDto
        {
            Name = workbook.Name,
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
                    SplitColumn = sheet.SplitColumn
                }).ToList()
            }).ToList(),
            Sheets = workbook.Sheets.Select(s => new SheetDto
            {
                Name = s.Name,
                IsHidden = s.IsHidden,
                TabColor = s.TabColor is { } color ? FormatColor(color) : null,
                ViewMode = s.ViewMode,
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
                    OutlineColor = textBox.OutlineColor is { } textOutline ? FormatColor(textOutline) : null
                }).ToList(),
                DrawingShapes = s.DrawingShapes.Select(shape => new DrawingShapeDto
                {
                    Anchor = shape.Anchor.ToA1(),
                    Kind = shape.Kind,
                    Width = shape.Width,
                    Height = shape.Height,
                    RotationDegrees = shape.RotationDegrees,
                    FillColor = shape.FillColor is { } shapeFill ? FormatColor(shapeFill) : null,
                    OutlineColor = shape.OutlineColor is { } shapeOutline ? FormatColor(shapeOutline) : null
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
                    LegendPosition = chart.LegendPosition,
                    ShowLegend = chart.ShowLegend,
                    Left = chart.Left,
                    Top = chart.Top,
                    Width = chart.Width,
                    Height = chart.Height
                }).ToList(),
                Cells = s.GetUsedCells().Select(pair => new CellDto
                {
                    Address   = pair.Key.ToA1(),
                    Value     = SerializeValue(pair.Value.Value),
                    ValueType = GetValueType(pair.Value.Value),
                    Formula   = pair.Value.HasFormula ? pair.Value.FormulaText : null
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
        public List<CustomViewDto> CustomViews { get; set; } = [];
        public List<SheetDto> Sheets { get; set; } = [];
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
    }

    private class SheetDto
    {
        public string Name { get; set; } = "";
        public bool IsHidden { get; set; }
        public string? TabColor { get; set; }
        public WorksheetViewMode ViewMode { get; set; } = WorksheetViewMode.Normal;
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
        public List<PictureDto> Pictures { get; set; } = [];
        public List<TextBoxDto> TextBoxes { get; set; } = [];
        public List<DrawingShapeDto> DrawingShapes { get; set; } = [];
        public List<ChartDto> Charts { get; set; } = [];
        public List<CellDto> Cells { get; set; } = [];
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
        public ChartLegendPosition LegendPosition { get; set; } = ChartLegendPosition.Right;
        public bool ShowLegend { get; set; } = true;
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
    }
}
