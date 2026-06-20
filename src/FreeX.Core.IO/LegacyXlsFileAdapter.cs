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
    private static readonly HashSet<string> ExcelReservedDefinedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Print_Area",
        "Print_Titles",
        "_FilterDatabase",
        "Criteria",
        "Database",
        "Extract",
        "Consolidate_Area"
    };

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

        LoadDefinedNames(hssf, workbook);

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
                LoadCellAnnotations(sourceCell, address, sheet);

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

    private static void LoadDefinedNames(NPOIWorkbook sourceWorkbook, Workbook workbook)
    {
        for (var index = 0; index < sourceWorkbook.NumberOfNames; index++)
        {
            var definedName = sourceWorkbook.GetNameAt(index);
            if (definedName is null ||
                definedName.IsDeleted ||
                definedName.IsFunctionName ||
                IsExcelReservedDefinedName(definedName.NameName) ||
                workbook.ValidateNamedRangeName(definedName.NameName) is not null)
            {
                continue;
            }

            var refersTo = NormalizeFormula(definedName.RefersToFormula ?? "");
            if (string.IsNullOrWhiteSpace(refersTo))
                continue;

            if (TryParseNamedRangeRefersTo(workbook, refersTo, out var range))
            {
                workbook.DefineNamedRange(
                    definedName.NameName,
                    range,
                    new NamedRangeMetadata(GetDefinedNameScope(sourceWorkbook, definedName), definedName.Comment ?? ""));
                continue;
            }

            workbook.NamedFormulas[definedName.NameName] = refersTo.Trim();
        }
    }

    private static void LoadCellAnnotations(NPOICell sourceCell, ModelCellAddress address, Sheet sheet)
    {
        var hyperlink = sourceCell.Hyperlink;
        if (hyperlink is not null)
        {
            var target = GetHyperlinkTarget(hyperlink);
            if (!string.IsNullOrWhiteSpace(target))
            {
                sheet.Hyperlinks[address] = target;
                sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
                    MapHyperlinkTargetKind(hyperlink.Type),
                    "",
                    hyperlink.Type == HyperlinkType.Document ? target : "");
            }
        }

        var comment = sourceCell.CellComment;
        var commentText = comment?.String?.String;
        if (!string.IsNullOrWhiteSpace(commentText))
        {
            sheet.Comments[address] = commentText;
            if (!string.IsNullOrWhiteSpace(comment!.Author))
                sheet.CommentAuthors[address] = comment.Author;
        }
    }

    private static string GetDefinedNameScope(NPOIWorkbook sourceWorkbook, IName definedName)
    {
        var sheetIndex = definedName.SheetIndex;
        return sheetIndex >= 0 && sheetIndex < sourceWorkbook.NumberOfSheets
            ? sourceWorkbook.GetSheetName(sheetIndex)
            : NamedRangeMetadata.WorkbookScope.Scope;
    }

    private static bool TryParseNamedRangeRefersTo(Workbook workbook, string? refersTo, out GridRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(refersTo))
            return false;

        var text = NormalizeFormula(refersTo).Trim();
        if (!TrySplitSheetQualifiedReference(text, out var sheetName, out var rangeText))
            return false;

        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return false;

        var parts = rangeText.Split(':');
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseA1Part(parts[0], sheet.Id, out var start))
            return false;

        var endText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseA1Part(endText, sheet.Id, out var end))
            return false;

        range = new GridRange(start, end);
        return true;
    }

    private static bool TrySplitSheetQualifiedReference(string text, out string sheetName, out string rangeText)
    {
        sheetName = "";
        rangeText = "";
        if (text.Length == 0)
            return false;

        if (text[0] == '\'')
        {
            var builder = new StringBuilder();
            for (var index = 1; index < text.Length; index++)
            {
                if (text[index] != '\'')
                {
                    builder.Append(text[index]);
                    continue;
                }

                if (index + 1 < text.Length && text[index + 1] == '\'')
                {
                    builder.Append('\'');
                    index++;
                    continue;
                }

                if (index + 1 >= text.Length || text[index + 1] != '!')
                    return false;

                sheetName = builder.ToString();
                rangeText = text[(index + 2)..].Trim();
                return rangeText.Length > 0;
            }

            return false;
        }

        var separator = text.IndexOf('!', StringComparison.Ordinal);
        if (separator <= 0 || separator == text.Length - 1)
            return false;

        sheetName = text[..separator].Trim();
        rangeText = text[(separator + 1)..].Trim();
        return sheetName.Length > 0 && rangeText.Length > 0;
    }

    private static bool TryParseA1Part(string text, SheetId sheetId, out ModelCellAddress address)
    {
        var normalized = text.Trim().Replace("$", "", StringComparison.Ordinal);
        return ModelCellAddress.TryParse(normalized, sheetId, out address);
    }

    private static string GetHyperlinkTarget(IHyperlink hyperlink)
    {
        var address = hyperlink.Address ?? "";
        if (hyperlink is HSSFHyperlink hssfHyperlink &&
            hyperlink.Type == HyperlinkType.Document &&
            !string.IsNullOrWhiteSpace(hssfHyperlink.TextMark))
        {
            return string.IsNullOrWhiteSpace(address) ? hssfHyperlink.TextMark : $"{address}#{hssfHyperlink.TextMark}";
        }

        return address;
    }

    private static HyperlinkTargetKind MapHyperlinkTargetKind(HyperlinkType type) =>
        type switch
        {
            HyperlinkType.Document => HyperlinkTargetKind.PlaceInThisDocument,
            HyperlinkType.Email => HyperlinkTargetKind.EmailAddress,
            _ => HyperlinkTargetKind.ExistingFileOrWebPage
        };

    private static bool IsExcelReservedDefinedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        var trimmedName = name.Trim();
        return trimmedName.StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase) ||
               trimmedName.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase) ||
               ExcelReservedDefinedNames.Contains(trimmedName);
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
