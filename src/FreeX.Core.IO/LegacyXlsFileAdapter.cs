using System.Text;
using ExcelDataReader;
using FreeX.Core.Model;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOICell = NPOI.SS.UserModel.ICell;
using NPOICellStyle = NPOI.SS.UserModel.ICellStyle;
using NPOIWorkbook = NPOI.SS.UserModel.IWorkbook;
using ModelBorderStyle = FreeX.Core.Model.BorderStyle;
using ModelCellAddress = FreeX.Core.Model.CellAddress;
using ModelCellStyle = FreeX.Core.Model.CellStyle;
using ModelHorizontalAlignment = FreeX.Core.Model.HorizontalAlignment;
using ModelVerticalAlignment = FreeX.Core.Model.VerticalAlignment;

namespace FreeX.Core.IO;

public sealed class LegacyXlsFileAdapter : IFileAdapter
{
    public string Extension => ".xls";
    public string FormatName => "XLS 97-2003 Workbook";
    public IReadOnlyList<FileFormatDescriptor> Formats { get; } =
    [
        new(".xls", "XLS 97-2003 Workbook", CanOpen: true, CanSave: false),
        new(".xlsb", "XLSB Binary Workbook", CanOpen: true, CanSave: false),
        new(".xlt", "XLT 97-2003 Template", CanOpen: true, CanSave: false, OpensAsTemplate: true)
    ];

    public Workbook Load(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (stream.CanSeek)
        {
            var start = stream.Position;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            try
            {
                using var hssfStream = new MemoryStream(bytes, writable: false);
                return LoadHssf(hssfStream);
            }
            catch
            {
                stream.Position = start;
                using var fallbackStream = new MemoryStream(bytes, writable: false);
                return LoadWithExcelDataReader(fallbackStream);
            }
        }

        return LoadWithExcelDataReader(stream);
    }

    public void Save(Workbook workbook, Stream stream) =>
        throw new NotSupportedException("Legacy .xls files are currently open-only. Use Save As XLSX Workbook instead.");

    private static Workbook LoadHssf(Stream stream)
    {
        using var hssf = new HSSFWorkbook(stream);
        var workbook = new Workbook("Untitled")
        {
            Uses1904DateSystem = hssf.IsDate1904()
        };

        var styleCache = new Dictionary<short, StyleId>();
        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
        {
            var sourceSheet = hssf.GetSheetAt(sheetIndex);
            var sheet = workbook.AddSheet(string.IsNullOrWhiteSpace(sourceSheet.SheetName)
                ? $"Sheet{sheetIndex + 1}"
                : sourceSheet.SheetName);

            var visibility = hssf.GetSheetVisibility(sheetIndex);
            sheet.IsHidden = visibility is SheetVisibility.Hidden or SheetVisibility.VeryHidden;
            sheet.IsVeryHidden = visibility is SheetVisibility.VeryHidden;

            LoadSheetLayout(sourceSheet, sheet);
            LoadMergedRegions(sourceSheet, sheet);
            LoadCells(hssf, sourceSheet, workbook, sheet, styleCache);
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        return workbook;
    }

    private static Workbook LoadWithExcelDataReader(Stream stream)
    {
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var workbook = new Workbook("Untitled");

        do
        {
            var sheet = workbook.AddSheet(string.IsNullOrWhiteSpace(reader.Name) ? $"Sheet{workbook.Sheets.Count + 1}" : reader.Name);
            var row = 1u;
            while (reader.Read())
            {
                for (var col = 0; col < reader.FieldCount; col++)
                {
                    var value = MapValue(reader.GetValue(col));
                    if (value is BlankValue)
                        continue;

                    sheet.SetCell(new ModelCellAddress(sheet.Id, row, (uint)(col + 1)), value);
                }

                row++;
            }
        }
        while (reader.NextResult());

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        return workbook;
    }

    private static void LoadSheetLayout(ISheet sourceSheet, Sheet sheet)
    {
        if (sourceSheet.DefaultColumnWidth > 0)
            sheet.DefaultColumnWidth = sourceSheet.DefaultColumnWidth;
        if (sourceSheet.DefaultRowHeightInPoints > 0)
            sheet.DefaultRowHeight = PointsToPixels(sourceSheet.DefaultRowHeightInPoints);

        for (var rowIndex = sourceSheet.FirstRowNum; rowIndex <= sourceSheet.LastRowNum; rowIndex++)
        {
            var sourceRow = sourceSheet.GetRow(rowIndex);
            if (sourceRow is null)
                continue;

            var rowNumber = ToModelIndex(rowIndex);
            if (sourceRow.ZeroHeight)
                sheet.HiddenRows.Add(rowNumber);
            if (sourceRow.HeightInPoints > 0)
                sheet.RowHeights[rowNumber] = PointsToPixels(sourceRow.HeightInPoints);
        }

        var maxColumn = FindLastColumn(sourceSheet);
        for (var columnIndex = 0; columnIndex <= maxColumn; columnIndex++)
        {
            var columnNumber = ToModelIndex(columnIndex);
            if (sourceSheet.IsColumnHidden(columnIndex))
                sheet.HiddenCols.Add(columnNumber);

            var width = sourceSheet.GetColumnWidth(columnIndex);
            if (width > 0)
                sheet.ColumnWidths[columnNumber] = width / 256.0;
        }
    }

    private static void LoadMergedRegions(ISheet sourceSheet, Sheet sheet)
    {
        for (var i = 0; i < sourceSheet.NumMergedRegions; i++)
        {
            var region = sourceSheet.GetMergedRegion(i);
            sheet.AddMergedRegion(new GridRange(
                new ModelCellAddress(sheet.Id, ToModelIndex(region.FirstRow), ToModelIndex(region.FirstColumn)),
                new ModelCellAddress(sheet.Id, ToModelIndex(region.LastRow), ToModelIndex(region.LastColumn))));
        }
    }

    private static void LoadCells(
        NPOIWorkbook sourceWorkbook,
        ISheet sourceSheet,
        Workbook workbook,
        Sheet sheet,
        Dictionary<short, StyleId> styleCache)
    {
        for (var rowIndex = sourceSheet.FirstRowNum; rowIndex <= sourceSheet.LastRowNum; rowIndex++)
        {
            var sourceRow = sourceSheet.GetRow(rowIndex);
            if (sourceRow is null)
                continue;

            foreach (var sourceCell in sourceRow.Cells)
            {
                var address = new ModelCellAddress(sheet.Id, ToModelIndex(sourceCell.RowIndex), ToModelIndex(sourceCell.ColumnIndex));
                var cell = MapCell(sourceCell);
                var styleId = GetStyleId(sourceWorkbook, workbook, sourceCell.CellStyle, styleCache);

                if (cell.Value is BlankValue && !cell.HasFormula)
                {
                    if (styleId != StyleId.Default)
                        sheet.SetStyleOnly(address.Row, address.Col, styleId);
                    continue;
                }

                cell.StyleId = styleId;
                sheet.SetCell(address, cell);
            }
        }
    }

    private static Cell MapCell(NPOICell sourceCell)
    {
        if (sourceCell.CellType == CellType.Formula)
        {
            var formulaText = NormalizeFormula(sourceCell.CellFormula);
            var cell = Cell.FromFormula(formulaText);
            cell.ArrayMode = FormulaArrayMode.Implicit;
            cell.Value = MapCachedFormulaValue(sourceCell);
            return cell;
        }

        return Cell.FromValue(MapNpoiValue(sourceCell, sourceCell.CellType));
    }

    private static ScalarValue MapCachedFormulaValue(NPOICell sourceCell) =>
        MapNpoiValue(sourceCell, sourceCell.CachedFormulaResultType);

    private static ScalarValue MapNpoiValue(NPOICell sourceCell, CellType cellType) =>
        cellType switch
        {
            CellType.Numeric when DateUtil.IsCellDateFormatted(sourceCell) && sourceCell.DateCellValue is { } date => DateTimeValue.FromDateTime(date),
            CellType.Numeric => new NumberValue(sourceCell.NumericCellValue),
            CellType.Boolean => new BoolValue(sourceCell.BooleanCellValue),
            CellType.String => string.IsNullOrEmpty(sourceCell.StringCellValue)
                ? BlankValue.Instance
                : new TextValue(sourceCell.StringCellValue),
            CellType.Error => MapErrorValue(sourceCell.ErrorCellValue),
            _ => BlankValue.Instance
        };

    private static StyleId GetStyleId(
        NPOIWorkbook sourceWorkbook,
        Workbook workbook,
        NPOICellStyle? sourceStyle,
        Dictionary<short, StyleId> styleCache)
    {
        if (sourceStyle is null)
            return StyleId.Default;

        var styleIndex = sourceStyle.Index;
        if (styleIndex == 0)
            return StyleId.Default;
        if (styleCache.TryGetValue(styleIndex, out var cached))
            return cached;

        var style = MapStyle(sourceWorkbook, sourceStyle);
        var styleId = workbook.RegisterStyle(style);
        styleCache[styleIndex] = styleId;
        return styleId;
    }

    private static ModelCellStyle MapStyle(NPOIWorkbook sourceWorkbook, NPOICellStyle sourceStyle)
    {
        var style = new ModelCellStyle
        {
            NumberFormat = sourceStyle.GetDataFormatString(),
            HorizontalAlignment = MapHorizontalAlignment(sourceStyle.Alignment),
            VerticalAlignment = MapVerticalAlignment(sourceStyle.VerticalAlignment),
            WrapText = sourceStyle.WrapText,
            ShrinkToFit = sourceStyle.ShrinkToFit,
            IndentLevel = sourceStyle.Indention,
            TextRotation = MapTextRotation(sourceStyle.Rotation),
            Locked = sourceStyle.IsLocked,
            Hidden = sourceStyle.IsHidden,
            FillPatternStyle = MapFillPattern(sourceStyle.FillPattern),
            BorderTop = new CellBorder(MapBorderStyle(sourceStyle.BorderTop), GetIndexedColor(sourceWorkbook, sourceStyle.TopBorderColor)),
            BorderRight = new CellBorder(MapBorderStyle(sourceStyle.BorderRight), GetIndexedColor(sourceWorkbook, sourceStyle.RightBorderColor)),
            BorderBottom = new CellBorder(MapBorderStyle(sourceStyle.BorderBottom), GetIndexedColor(sourceWorkbook, sourceStyle.BottomBorderColor)),
            BorderLeft = new CellBorder(MapBorderStyle(sourceStyle.BorderLeft), GetIndexedColor(sourceWorkbook, sourceStyle.LeftBorderColor))
        };

        if (sourceStyle.FillForegroundColor != 0)
            style.FillColor = GetIndexedColor(sourceWorkbook, sourceStyle.FillForegroundColor);

        var font = sourceWorkbook.GetFontAt(sourceStyle.FontIndex);
        if (font is not null)
        {
            style.FontName = string.IsNullOrWhiteSpace(font.FontName) ? style.FontName : font.FontName;
            if (font.FontHeightInPoints > 0)
                style.FontSize = font.FontHeightInPoints;
            style.Bold = font.IsBold;
            style.Italic = font.IsItalic;
            style.Strikethrough = font.IsStrikeout;
            style.Underline = font.Underline != FontUnderlineType.None;
            style.FontColor = GetIndexedColor(sourceWorkbook, font.Color);
        }

        return style;
    }

    private static CellColor GetIndexedColor(NPOIWorkbook sourceWorkbook, short colorIndex)
    {
        if (sourceWorkbook is HSSFWorkbook hssf)
        {
            var color = hssf.GetCustomPalette().GetColor(colorIndex);
            var triplet = color?.GetTriplet();
            if (triplet is { Length: >= 3 })
                return new CellColor(Convert.ToByte(triplet[0]), Convert.ToByte(triplet[1]), Convert.ToByte(triplet[2]));
        }

        return CellColor.Black;
    }

    private static ErrorValue MapErrorValue(byte errorCode) =>
        FormulaError.ForInt(errorCode).String switch
        {
            "#DIV/0!" => ErrorValue.DivByZero,
            "#VALUE!" => ErrorValue.Value,
            "#REF!" => ErrorValue.Ref,
            "#NAME?" => ErrorValue.Name,
            "#NULL!" => ErrorValue.Null,
            "#N/A" => ErrorValue.NA,
            "#NUM!" => ErrorValue.Num,
            var code => new ErrorValue(code)
        };

    private static string NormalizeFormula(string formula) =>
        formula.StartsWith('=') ? formula[1..] : formula;

    private static uint ToModelIndex(int zeroBasedIndex) => (uint)zeroBasedIndex + 1;

    private static int FindLastColumn(ISheet sourceSheet)
    {
        var maxColumn = 0;
        for (var rowIndex = sourceSheet.FirstRowNum; rowIndex <= sourceSheet.LastRowNum; rowIndex++)
        {
            var row = sourceSheet.GetRow(rowIndex);
            if (row is not null && row.LastCellNum > 0)
                maxColumn = Math.Max(maxColumn, row.LastCellNum - 1);
        }

        return maxColumn;
    }

    private static double PointsToPixels(double points) =>
        Math.Round(points * (96.0 / 72.0), MidpointRounding.AwayFromZero);

    private static ModelHorizontalAlignment MapHorizontalAlignment(NPOI.SS.UserModel.HorizontalAlignment alignment) =>
        alignment switch
        {
            NPOI.SS.UserModel.HorizontalAlignment.Left => ModelHorizontalAlignment.Left,
            NPOI.SS.UserModel.HorizontalAlignment.Center => ModelHorizontalAlignment.Center,
            NPOI.SS.UserModel.HorizontalAlignment.Right => ModelHorizontalAlignment.Right,
            NPOI.SS.UserModel.HorizontalAlignment.Justify => ModelHorizontalAlignment.Justify,
            NPOI.SS.UserModel.HorizontalAlignment.Distributed => ModelHorizontalAlignment.Distributed,
            _ => ModelHorizontalAlignment.General
        };

    private static ModelVerticalAlignment MapVerticalAlignment(NPOI.SS.UserModel.VerticalAlignment alignment) =>
        alignment switch
        {
            NPOI.SS.UserModel.VerticalAlignment.Top => ModelVerticalAlignment.Top,
            NPOI.SS.UserModel.VerticalAlignment.Center => ModelVerticalAlignment.Center,
            NPOI.SS.UserModel.VerticalAlignment.Justify => ModelVerticalAlignment.Justify,
            NPOI.SS.UserModel.VerticalAlignment.Distributed => ModelVerticalAlignment.Distributed,
            _ => ModelVerticalAlignment.Bottom
        };

    private static ModelBorderStyle MapBorderStyle(NPOI.SS.UserModel.BorderStyle borderStyle) =>
        borderStyle switch
        {
            NPOI.SS.UserModel.BorderStyle.Thin => ModelBorderStyle.Thin,
            NPOI.SS.UserModel.BorderStyle.Medium => ModelBorderStyle.Medium,
            NPOI.SS.UserModel.BorderStyle.Thick => ModelBorderStyle.Thick,
            NPOI.SS.UserModel.BorderStyle.Dashed => ModelBorderStyle.Dashed,
            NPOI.SS.UserModel.BorderStyle.Dotted => ModelBorderStyle.Dotted,
            NPOI.SS.UserModel.BorderStyle.Double => ModelBorderStyle.Double,
            _ => ModelBorderStyle.None
        };

    private static CellFillPatternStyle MapFillPattern(FillPattern fillPattern) =>
        fillPattern switch
        {
            FillPattern.SolidForeground => CellFillPatternStyle.Solid,
            FillPattern.FineDots => CellFillPatternStyle.Gray125,
            FillPattern.AltBars => CellFillPatternStyle.DarkHorizontal,
            FillPattern.SparseDots => CellFillPatternStyle.Gray0625,
            FillPattern.ThickHorizontalBands => CellFillPatternStyle.DarkHorizontal,
            FillPattern.ThickVerticalBands => CellFillPatternStyle.DarkVertical,
            FillPattern.ThickBackwardDiagonals => CellFillPatternStyle.DarkUp,
            FillPattern.ThickForwardDiagonals => CellFillPatternStyle.DarkDown,
            FillPattern.BigSpots => CellFillPatternStyle.LightGray,
            FillPattern.Bricks => CellFillPatternStyle.LightTrellis,
            FillPattern.ThinHorizontalBands => CellFillPatternStyle.LightHorizontal,
            FillPattern.ThinVerticalBands => CellFillPatternStyle.LightVertical,
            FillPattern.ThinBackwardDiagonals => CellFillPatternStyle.LightUp,
            FillPattern.ThinForwardDiagonals => CellFillPatternStyle.LightDown,
            FillPattern.Squares => CellFillPatternStyle.LightGrid,
            FillPattern.Diamonds => CellFillPatternStyle.LightTrellis,
            _ => CellFillPatternStyle.None
        };

    private static int MapTextRotation(short rotation) =>
        rotation switch
        {
            255 => 255,
            > 90 => 90 - rotation,
            _ => rotation
        };

    private static ScalarValue MapValue(object? value) =>
        value switch
        {
            null => BlankValue.Instance,
            double number => new NumberValue(number),
            float number => new NumberValue(number),
            long number => new NumberValue(number),
            int number => new NumberValue(number),
            short number => new NumberValue(number),
            byte number => new NumberValue(number),
            sbyte number => new NumberValue(number),
            uint number => new NumberValue(number),
            ushort number => new NumberValue(number),
            ulong number => new NumberValue(number),
            decimal number => new NumberValue((double)number),
            bool boolean => new BoolValue(boolean),
            DateTime date => DateTimeValue.FromDateTime(date),
            TimeSpan time => new DateTimeValue(time.TotalDays),
            string text when text.Length == 0 => BlankValue.Instance,
            string text => new TextValue(text),
            _ => new TextValue(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "")
        };
}
