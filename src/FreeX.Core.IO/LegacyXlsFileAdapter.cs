using System.Globalization;
using System.Text;
using ExcelDataReader;
using NPOI.HSSF.Record;
using FreeX.Core.Model;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
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
    private const int LegacyXlsMaxColumnIndex = 255;
    private const short LegacyPaperSizeLetter = 1;
    private const short LegacyPaperSizeLegal = 5;
    private const short LegacyPaperSizeA4 = 9;

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
        LoadWorkbookView(hssf, workbook);
        LoadWorkbookProtection(hssf, workbook);
        if (hssf.ActiveSheetIndex >= 0 && hssf.ActiveSheetIndex < hssf.NumberOfSheets)
            workbook.ActiveSheetIndex = hssf.ActiveSheetIndex;

        var styleCache = new Dictionary<short, StyleId>();
        var palette = hssf.GetCustomPalette();
        for (var sheetIndex = 0; sheetIndex < hssf.NumberOfSheets; sheetIndex++)
        {
            var sourceSheet = hssf.GetSheetAt(sheetIndex);
            var sheet = workbook.AddSheet(string.IsNullOrWhiteSpace(sourceSheet.SheetName)
                ? $"Sheet{sheetIndex + 1}"
                : sourceSheet.SheetName);

            var visibility = hssf.GetSheetVisibility(sheetIndex);
            sheet.IsHidden = visibility is SheetVisibility.Hidden or SheetVisibility.VeryHidden;
            sheet.IsVeryHidden = visibility is SheetVisibility.VeryHidden;

            LoadSheetLayout(sourceSheet, sheet, palette);
            LoadMergedRegions(sourceSheet, sheet);
            LoadCells(hssf, sourceSheet, workbook, sheet, styleCache);
            LoadPictures(sourceSheet, sheet);
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        LoadConditionalFormats(hssf, workbook);
        LoadDataValidations(hssf, workbook);
        LoadDefinedNames(hssf, workbook);

        return workbook;
    }

    private static void LoadWorkbookView(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        if (sourceWorkbook.FirstVisibleTab >= 0 && sourceWorkbook.FirstVisibleTab < sourceWorkbook.NumberOfSheets)
            workbook.FirstVisibleSheetIndex = sourceWorkbook.FirstVisibleTab;

        if (sourceWorkbook.Workbook.FindFirstRecordBySid(WindowOneRecord.sid) is not WindowOneRecord window)
            return;

        workbook.ShowSheetTabs = window.DisplayTabs;
        workbook.SheetTabRatio = Math.Clamp((int)window.TabWidthRatio, 0, 1000);
        if (window.FirstVisibleTab >= 0 && window.FirstVisibleTab < sourceWorkbook.NumberOfSheets)
            workbook.FirstVisibleSheetIndex = window.FirstVisibleTab;
    }

    private static void LoadWorkbookProtection(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        var isStructureProtected =
            sourceWorkbook.Workbook.FindFirstRecordBySid(ProtectRecord.sid) is ProtectRecord protect &&
            protect.Protect;
        var isWindowProtected =
            sourceWorkbook.Workbook.FindFirstRecordBySid(WindowProtectRecord.sid) is WindowProtectRecord windowProtect &&
            windowProtect.Protect;

        workbook.IsStructureProtected = isStructureProtected || isWindowProtected;
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(PasswordRecord.sid) is PasswordRecord { Password: not 0 } password)
            workbook.StructureProtectionPassword = ((ushort)password.Password).ToString("X4", CultureInfo.InvariantCulture);

        if (!isWindowProtected)
            return;

        var serializedMetadata = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["lockWindows"] = "1"
            });
        if (serializedMetadata is null)
            return;

        workbook.ProtectionMetadata = new NativeXmlPreserveBag();
        workbook.ProtectionMetadata.Set("workbookProtection", serializedMetadata);
    }

    private static Workbook LoadWithExcelDataReader(Stream stream)
    {
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var workbook = new Workbook("Untitled");
        var styleCache = new Dictionary<ExcelDataReaderStyleKey, StyleId>();

        do
        {
            var sheetIndex = workbook.Sheets.Count;
            var sheet = workbook.AddSheet(string.IsNullOrWhiteSpace(reader.Name) ? $"Sheet{workbook.Sheets.Count + 1}" : reader.Name);
            if (reader.IsActiveSheet)
                workbook.ActiveSheetIndex = sheetIndex;

            LoadExcelDataReaderSheetLayout(reader, sheet);
            var row = 1u;
            while (reader.Read())
            {
                if (reader.RowHeight > 0)
                    sheet.RowHeights[row] = PointsToPixels(reader.RowHeight);

                for (var col = 0; col < reader.FieldCount; col++)
                {
                    var value = MapValue(reader.GetValue(col));
                    if (value is BlankValue)
                        continue;

                    var cell = Cell.FromValue(value);
                    cell.StyleId = GetExcelDataReaderStyleId(reader, workbook, col, styleCache);
                    sheet.SetCell(new ModelCellAddress(sheet.Id, row, (uint)(col + 1)), cell);
                }

                row++;
            }
        }
        while (reader.NextResult());

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        return workbook;
    }

    private static void LoadExcelDataReaderSheetLayout(IExcelDataReader reader, Sheet sheet)
    {
        LoadExcelDataReaderSheetState(reader, sheet);

        foreach (var range in reader.MergeCells ?? [])
        {
            if (range.FromRow <= range.ToRow &&
                range.FromColumn <= range.ToColumn)
            {
                sheet.AddMergedRegion(ToGridRange(range, sheet.Id));
            }
        }

        for (var col = 0; col < reader.FieldCount; col++)
        {
            var width = reader.GetColumnWidth(col);
            if (width > 0)
                sheet.ColumnWidths[ToModelIndex(col)] = width;
        }
    }

    private static void LoadExcelDataReaderSheetState(IExcelDataReader reader, Sheet sheet)
    {
        sheet.IsVeryHidden = string.Equals(reader.VisibleState, "veryHidden", StringComparison.OrdinalIgnoreCase);
        sheet.IsHidden = sheet.IsVeryHidden ||
            string.Equals(reader.VisibleState, "hidden", StringComparison.OrdinalIgnoreCase);

        if (reader.HeaderFooter is { } headerFooter)
        {
            sheet.PageHeader = ParseHeaderFooterRawText(headerFooter.OddHeader);
            sheet.PageFooter = ParseHeaderFooterRawText(headerFooter.OddFooter);
        }
    }

    private static StyleId GetExcelDataReaderStyleId(
        IExcelDataReader reader,
        Workbook workbook,
        int column,
        Dictionary<ExcelDataReaderStyleKey, StyleId> styleCache)
    {
        var sourceStyle = reader.GetCellStyle(column);
        var numberFormat = reader.GetNumberFormatString(column);
        var styleKey = new ExcelDataReaderStyleKey(
            string.IsNullOrWhiteSpace(numberFormat)
                ? ModelCellStyle.Default.NumberFormat
                : numberFormat,
            sourceStyle.HorizontalAlignment,
            sourceStyle.VerticalAlignment,
            sourceStyle.IndentLevel,
            sourceStyle.Locked,
            sourceStyle.Hidden);

        if (IsDefaultExcelDataReaderStyle(styleKey))
            return StyleId.Default;

        if (styleCache.TryGetValue(styleKey, out var cached))
            return cached;

        var style = new ModelCellStyle
        {
            NumberFormat = styleKey.NumberFormat,
            HorizontalAlignment = MapExcelDataReaderHorizontalAlignment(styleKey.HorizontalAlignment),
            VerticalAlignment = MapExcelDataReaderVerticalAlignment(styleKey.VerticalAlignment),
            IndentLevel = styleKey.IndentLevel,
            Locked = styleKey.Locked,
            Hidden = styleKey.Hidden
        };

        var styleId = workbook.RegisterStyle(style);
        styleCache[styleKey] = styleId;
        return styleId;
    }

    private static bool IsDefaultExcelDataReaderStyle(ExcelDataReaderStyleKey styleKey) =>
        string.Equals(styleKey.NumberFormat, ModelCellStyle.Default.NumberFormat, StringComparison.Ordinal) &&
        MapExcelDataReaderHorizontalAlignment(styleKey.HorizontalAlignment) == ModelCellStyle.Default.HorizontalAlignment &&
        MapExcelDataReaderVerticalAlignment(styleKey.VerticalAlignment) == ModelCellStyle.Default.VerticalAlignment &&
        styleKey.IndentLevel == ModelCellStyle.Default.IndentLevel &&
        styleKey.Locked == ModelCellStyle.Default.Locked &&
        styleKey.Hidden == ModelCellStyle.Default.Hidden;

    private static void LoadSheetLayout(ISheet sourceSheet, Sheet sheet, HSSFPalette palette)
    {
        LoadPaneState(sourceSheet, sheet);
        LoadPrintTitles(sourceSheet, sheet);
        LoadPageLayout(sourceSheet, sheet);
        LoadSheetView(sourceSheet, sheet, palette);
        LoadSheetProtection(sourceSheet, sheet);

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
            if (sourceRow.OutlineLevel > 0)
                sheet.RowOutlineLevels[rowNumber] = sourceRow.OutlineLevel;
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

        LoadColumnOutlineLevels(sourceSheet, sheet);
    }

    private static void LoadSheetProtection(ISheet sourceSheet, Sheet sheet)
    {
        var isObjectProtected = sourceSheet is HSSFSheet hssfSheet && hssfSheet.ObjectProtect;
        var isScenarioProtected = sourceSheet.ScenarioProtect;
        sheet.IsProtected = sourceSheet.Protect || isObjectProtected || isScenarioProtected;

        if (sourceSheet is HSSFSheet { Password: not 0 } protectedSheet)
            sheet.ProtectionPassword = ((ushort)protectedSheet.Password).ToString("X4", CultureInfo.InvariantCulture);

        var nativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (isObjectProtected)
            nativeAttributes["objects"] = "1";
        if (isScenarioProtected)
            nativeAttributes["scenarios"] = "1";

        var serializedMetadata = XmlNativeBagSerializer.Serialize(nativeAttributes);
        if (serializedMetadata is not null)
        {
            var metadata = new NativeXmlPreserveBag();
            metadata.Set("sheetProtection", serializedMetadata);
            sheet.ProtectionMetadata = metadata;
        }
    }

    private static void LoadDataValidations(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        if (sourceWorkbook.NumberOfSheets == 0 || workbook.Sheets.Count == 0)
            return;

        for (var sheetIndex = 0; sheetIndex < sourceWorkbook.NumberOfSheets && sheetIndex < workbook.Sheets.Count; sheetIndex++)
        {
            if (sourceWorkbook.GetSheetAt(sheetIndex) is not HSSFSheet sourceSheet)
                continue;

            var sheet = workbook.GetSheetAt(sheetIndex);

            IReadOnlyList<IDataValidation> validations;
            try
            {
                validations = sourceSheet.GetDataValidations();
            }
            catch
            {
                continue;
            }

            foreach (var sourceValidation in validations)
            {
                if (TryCreateDataValidation(sourceValidation, sheet.Id, out var validation))
                    sheet.DataValidations.Add(validation);
            }
        }
    }

    private static bool TryCreateDataValidation(
        IDataValidation sourceValidation,
        SheetId sheetId,
        out DataValidation validation)
    {
        validation = new DataValidation();
        var regions = sourceValidation.Regions?.CellRangeAddresses;
        if (regions is null || regions.Length == 0)
            return false;

        validation.AppliesTo = ToGridRange(regions[0], sheetId);
        foreach (var region in regions.Skip(1))
            validation.AdditionalRanges.Add(ToGridRange(region, sheetId));

        var constraint = sourceValidation.ValidationConstraint;
        validation.Type = MapDataValidationType(constraint.GetValidationType());
        validation.Operator = MapDataValidationOperator(constraint.Operator);
        validation.AllowBlank = sourceValidation.EmptyCellAllowed;
        validation.ShowDropdown = !sourceValidation.SuppressDropDownArrow;
        validation.AlertStyle = MapDataValidationAlertStyle(sourceValidation.ErrorStyle);
        validation.ShowInputMessage = sourceValidation.ShowPromptBox;
        validation.ShowErrorMessage = sourceValidation.ShowErrorBox;
        validation.ErrorTitle = NullIfEmpty(sourceValidation.ErrorBoxTitle);
        validation.ErrorMessage = NullIfEmpty(sourceValidation.ErrorBoxText);
        validation.PromptTitle = NullIfEmpty(sourceValidation.PromptBoxTitle);
        validation.PromptMessage = NullIfEmpty(sourceValidation.PromptBoxText);

        if (validation.Type == DvType.List && constraint.ExplicitListValues is { Length: > 0 } explicitValues)
        {
            validation.Formula1 = string.Join(",", explicitValues);
        }
        else
        {
            validation.Formula1 = NullIfEmpty(constraint.Formula1);
            validation.Formula2 = NullIfEmpty(constraint.Formula2);
        }

        return true;
    }

    private static GridRange ToGridRange(CellRangeAddressBase range, SheetId sheetId) =>
        new(
            new ModelCellAddress(sheetId, ToModelIndex(range.FirstRow), ToModelIndex(range.FirstColumn)),
            new ModelCellAddress(sheetId, ToModelIndex(range.LastRow), ToModelIndex(range.LastColumn)));

    private static GridRange ToGridRange(ExcelDataReader.CellRange range, SheetId sheetId) =>
        new(
            new ModelCellAddress(sheetId, ToModelIndex(range.FromRow), ToModelIndex(range.FromColumn)),
            new ModelCellAddress(sheetId, ToModelIndex(range.ToRow), ToModelIndex(range.ToColumn)));

    private static DvType MapDataValidationType(int validationType) =>
        validationType switch
        {
            ValidationType.INTEGER => DvType.WholeNumber,
            ValidationType.DECIMAL => DvType.Decimal,
            ValidationType.LIST => DvType.List,
            ValidationType.DATE => DvType.Date,
            ValidationType.TIME => DvType.Time,
            ValidationType.TEXT_LENGTH => DvType.TextLength,
            ValidationType.FORMULA => DvType.Custom,
            _ => DvType.Any
        };

    private static DvOperator MapDataValidationOperator(int operatorType) =>
        operatorType switch
        {
            OperatorType.NOT_BETWEEN => DvOperator.NotBetween,
            OperatorType.EQUAL => DvOperator.Equal,
            OperatorType.NOT_EQUAL => DvOperator.NotEqual,
            OperatorType.GREATER_THAN => DvOperator.GreaterThan,
            OperatorType.LESS_THAN => DvOperator.LessThan,
            OperatorType.GREATER_OR_EQUAL => DvOperator.GreaterThanOrEqual,
            OperatorType.LESS_OR_EQUAL => DvOperator.LessThanOrEqual,
            _ => DvOperator.Between
        };

    private static DvAlertStyle MapDataValidationAlertStyle(int errorStyle) =>
        errorStyle switch
        {
            ERRORSTYLE.WARNING => DvAlertStyle.Warning,
            ERRORSTYLE.INFO => DvAlertStyle.Information,
            _ => DvAlertStyle.Stop
        };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static void LoadConditionalFormats(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        if (sourceWorkbook.NumberOfSheets == 0 || workbook.Sheets.Count == 0)
            return;

        for (var sheetIndex = 0; sheetIndex < sourceWorkbook.NumberOfSheets && sheetIndex < workbook.Sheets.Count; sheetIndex++)
        {
            if (sourceWorkbook.GetSheetAt(sheetIndex) is not HSSFSheet sourceSheet)
                continue;

            var sheet = workbook.GetSheetAt(sheetIndex);
            ISheetConditionalFormatting sourceFormats;
            try
            {
                sourceFormats = sourceSheet.SheetConditionalFormatting;
            }
            catch
            {
                continue;
            }

            for (var formatIndex = 0; formatIndex < sourceFormats.NumConditionalFormattings; formatIndex++)
            {
                IConditionalFormatting sourceFormat;
                try
                {
                    sourceFormat = sourceFormats.GetConditionalFormattingAt(formatIndex);
                }
                catch
                {
                    continue;
                }

                var ranges = sourceFormat.GetFormattingRanges();
                if (ranges.Length == 0)
                    continue;

                for (var ruleIndex = 0; ruleIndex < sourceFormat.NumberOfRules; ruleIndex++)
                {
                    var sourceRule = sourceFormat.GetRule(ruleIndex);
                    foreach (var range in ranges)
                    {
                        if (TryCreateConditionalFormat(sourceWorkbook, sourceRule, range, sheet.Id, out var conditionalFormat))
                            sheet.ConditionalFormats.Add(conditionalFormat);
                    }
                }
            }
        }
    }

    private static bool TryCreateConditionalFormat(
        HSSFWorkbook sourceWorkbook,
        IConditionalFormattingRule sourceRule,
        CellRangeAddressBase range,
        SheetId sheetId,
        out ConditionalFormat conditionalFormat)
    {
        conditionalFormat = new ConditionalFormat();
        if (sourceRule.ConditionType == ConditionType.CellValueIs)
        {
            conditionalFormat.RuleType = CfRuleType.CellValue;
            conditionalFormat.Operator = MapConditionalFormatOperator(sourceRule.ComparisonOperation);
            conditionalFormat.Value1 = NullIfEmpty(NormalizeFormula(sourceRule.Formula1 ?? ""));
            conditionalFormat.Value2 = NullIfEmpty(NormalizeFormula(sourceRule.Formula2 ?? ""));
        }
        else if (sourceRule.ConditionType == ConditionType.Formula)
        {
            conditionalFormat.RuleType = CfRuleType.Formula;
            conditionalFormat.FormulaText = NullIfEmpty(NormalizeFormula(sourceRule.Formula1 ?? ""));
        }
        else
        {
            return false;
        }

        conditionalFormat.AppliesTo = ToGridRange(range, sheetId);
        conditionalFormat.Priority = Math.Max(1, sourceRule.Priority);
        conditionalFormat.StopIfTrue = sourceRule.StopIfTrue;
        conditionalFormat.FormatIfTrue = MapConditionalFormatStyle(sourceWorkbook, sourceRule);
        return true;
    }

    private static CfOperator MapConditionalFormatOperator(ComparisonOperator op) =>
        op switch
        {
            ComparisonOperator.NotBetween => CfOperator.NotBetween,
            ComparisonOperator.Equal => CfOperator.Equal,
            ComparisonOperator.NotEqual => CfOperator.NotEqual,
            ComparisonOperator.GreaterThan => CfOperator.GreaterThan,
            ComparisonOperator.LessThan => CfOperator.LessThan,
            ComparisonOperator.GreaterThanOrEqual => CfOperator.GreaterThanOrEqual,
            ComparisonOperator.LessThanOrEqual => CfOperator.LessThanOrEqual,
            _ => CfOperator.Between
        };

    private static ModelCellStyle? MapConditionalFormatStyle(
        HSSFWorkbook sourceWorkbook,
        IConditionalFormattingRule sourceRule)
    {
        var hasStyle = false;
        var style = new ModelCellStyle();

        if (sourceRule.FontFormatting is { } font)
        {
            hasStyle = true;
            style.Bold = font.IsBold;
            style.Italic = font.IsItalic;
            style.Underline = font.UnderlineType != FontUnderlineType.None;
            if (font.FontHeight > 0)
                style.FontSize = font.FontHeight / 20.0;
            if (font.FontColorIndex != 0)
                style.FontColor = GetIndexedColor(sourceWorkbook, font.FontColorIndex);
        }

        if (sourceRule.PatternFormatting is { } pattern)
        {
            hasStyle = true;
            style.FillPatternStyle = MapFillPattern(pattern.FillPattern);
            if (pattern.FillForegroundColor != 0)
                style.FillColor = GetIndexedColor(sourceWorkbook, pattern.FillForegroundColor);
            if (pattern.FillBackgroundColor != 0)
                style.FillPatternColor = GetIndexedColor(sourceWorkbook, pattern.FillBackgroundColor);
        }

        if (sourceRule.BorderFormatting is { } border)
        {
            hasStyle = true;
            style.BorderTop = new CellBorder(MapBorderStyle(border.BorderTop), GetIndexedColor(sourceWorkbook, border.TopBorderColor));
            style.BorderRight = new CellBorder(MapBorderStyle(border.BorderRight), GetIndexedColor(sourceWorkbook, border.RightBorderColor));
            style.BorderBottom = new CellBorder(MapBorderStyle(border.BorderBottom), GetIndexedColor(sourceWorkbook, border.BottomBorderColor));
            style.BorderLeft = new CellBorder(MapBorderStyle(border.BorderLeft), GetIndexedColor(sourceWorkbook, border.LeftBorderColor));
        }

        return hasStyle ? style : null;
    }

    private static void LoadColumnOutlineLevels(ISheet sourceSheet, Sheet sheet)
    {
        for (var columnIndex = 0; columnIndex <= LegacyXlsMaxColumnIndex; columnIndex++)
        {
            var outlineLevel = sourceSheet.GetColumnOutlineLevel(columnIndex);
            if (outlineLevel > 0)
                sheet.ColOutlineLevels[ToModelIndex(columnIndex)] = outlineLevel;
        }
    }

    private static void LoadPaneState(ISheet sourceSheet, Sheet sheet)
    {
        var pane = sourceSheet.PaneInformation;
        if (pane is null)
            return;

        if (pane.IsFreezePane())
        {
            sheet.FrozenCols = (uint)Math.Max(0, (int)pane.VerticalSplitPosition);
            sheet.FrozenRows = (uint)Math.Max(0, (int)pane.HorizontalSplitPosition);
            sheet.SplitColumn = null;
            sheet.SplitRow = null;
            return;
        }

        if (pane.HorizontalSplitPosition > 0 && pane.HorizontalSplitTopRow >= 0)
            sheet.SplitRow = ToModelIndex(pane.HorizontalSplitTopRow);
        if (pane.VerticalSplitPosition > 0 && pane.VerticalSplitLeftColumn >= 0)
            sheet.SplitColumn = ToModelIndex(pane.VerticalSplitLeftColumn);
    }

    private static void LoadPrintTitles(ISheet sourceSheet, Sheet sheet)
    {
        if (TryCreateRepeatRows(sourceSheet.RepeatingRows, out var rows))
            sheet.PrintTitleRows = rows;
        if (TryCreateRepeatColumns(sourceSheet.RepeatingColumns, out var columns))
            sheet.PrintTitleColumns = columns;
    }

    private static void LoadPageLayout(ISheet sourceSheet, Sheet sheet)
    {
        sheet.PageMargins = new WorksheetPageMargins(
            ValidMarginOrDefault(sourceSheet.GetMargin(MarginType.LeftMargin), sheet.PageMargins.Left),
            ValidMarginOrDefault(sourceSheet.GetMargin(MarginType.RightMargin), sheet.PageMargins.Right),
            ValidMarginOrDefault(sourceSheet.GetMargin(MarginType.TopMargin), sheet.PageMargins.Top),
            ValidMarginOrDefault(sourceSheet.GetMargin(MarginType.BottomMargin), sheet.PageMargins.Bottom));

        sheet.PrintGridlines = sourceSheet.IsPrintGridlines;
        sheet.PrintHeadings = sourceSheet.IsPrintRowAndColumnHeadings;
        sheet.CenterHorizontallyOnPage = sourceSheet.HorizontallyCenter;
        sheet.CenterVerticallyOnPage = sourceSheet.VerticallyCenter;
        sheet.FitToPage = sourceSheet.FitToPage;
        sheet.AutoPageBreaks = sourceSheet.Autobreaks;
        sheet.PageHeader = ToWorksheetHeaderFooter(sourceSheet.Header);
        sheet.PageFooter = ToWorksheetHeaderFooter(sourceSheet.Footer);

        LoadManualPageBreaks(sourceSheet, sheet);
        LoadPrintSetup(sourceSheet.PrintSetup, sheet);
    }

    private static void LoadSheetView(ISheet sourceSheet, Sheet sheet, HSSFPalette palette)
    {
        sheet.ShowGridlines = sourceSheet.DisplayGridlines;
        sheet.ShowHeadings = sourceSheet.DisplayRowColHeadings;
        sheet.ShowFormulas = sourceSheet.DisplayFormulas;
        if (sourceSheet.TopRow > 0)
            sheet.ViewTopRow = ToModelIndex(sourceSheet.TopRow);
        if (TryGetTabColor(sourceSheet, palette, out var tabColor))
            sheet.TabColor = tabColor;
    }

    private static bool TryGetTabColor(ISheet sourceSheet, HSSFPalette palette, out CellColor tabColor)
    {
        tabColor = default;
        if (sourceSheet is not HSSFSheet hssfSheet)
            return false;

        try
        {
            if (hssfSheet.IsAutoTabColor)
                return false;

            var color = palette.GetColor(hssfSheet.TabColorIndex);
            if (color is null)
                return false;

            var triplet = color.GetTriplet();
            if (triplet.Length < 3)
                return false;

            tabColor = new CellColor(triplet[0], triplet[1], triplet[2]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void LoadManualPageBreaks(ISheet sourceSheet, Sheet sheet)
    {
        foreach (var rowBreak in sourceSheet.RowBreaks)
        {
            var modelRow = ToModelIndex(rowBreak);
            if (modelRow is >= 2 and <= ModelCellAddress.MaxRow)
                sheet.RowPageBreaks.Add(modelRow);
        }

        foreach (var columnBreak in sourceSheet.ColumnBreaks)
        {
            var modelColumn = ToModelIndex(columnBreak);
            if (modelColumn is >= 2 and <= ModelCellAddress.MaxCol)
                sheet.ColumnPageBreaks.Add(modelColumn);
        }
    }

    private static void LoadPrintSetup(IPrintSetup printSetup, Sheet sheet)
    {
        sheet.PageOrientation = printSetup.Landscape
            ? WorksheetPageOrientation.Landscape
            : WorksheetPageOrientation.Portrait;
        sheet.PaperSize = MapPaperSize(printSetup.PaperSize);
        sheet.HeaderMargin = ValidMarginOrDefault(printSetup.HeaderMargin, sheet.HeaderMargin);
        sheet.FooterMargin = ValidMarginOrDefault(printSetup.FooterMargin, sheet.FooterMargin);
        sheet.PageOrder = printSetup.LeftToRight
            ? WorksheetPageOrder.OverThenDown
            : WorksheetPageOrder.DownThenOver;
        sheet.FirstPageNumber = printSetup.UsePage && printSetup.PageStart > 0
            ? printSetup.PageStart
            : null;
        sheet.PrintCopies = printSetup.Copies > 0 ? printSetup.Copies : null;
        sheet.PrintBlackAndWhite = printSetup.NoColor;
        sheet.PrintDraftQuality = printSetup.Draft;
        sheet.PrintQualityDpi = printSetup.HResolution > 0 ? printSetup.HResolution : null;
        sheet.PrintQualityVerticalDpi = printSetup.VResolution > 0 && printSetup.VResolution != printSetup.HResolution
            ? printSetup.VResolution
            : null;
        sheet.PrintComments = printSetup.Notes
            ? WorksheetPrintComments.AtEnd
            : WorksheetPrintComments.None;

        sheet.ScaleToFit = printSetup.FitWidth > 0 || printSetup.FitHeight > 0
            ? new WorksheetScaleToFit(null, PositiveOrNull(printSetup.FitWidth), PositiveOrNull(printSetup.FitHeight))
            : new WorksheetScaleToFit(PositiveOrDefault(printSetup.Scale, 100), null, null);
    }

    private static WorksheetPaperSize MapPaperSize(short paperSize) =>
        paperSize switch
        {
            LegacyPaperSizeLetter => WorksheetPaperSize.Letter,
            LegacyPaperSizeLegal => WorksheetPaperSize.Legal,
            LegacyPaperSizeA4 => WorksheetPaperSize.A4,
            _ => WorksheetPaperSize.A4
        };

    private static int? PositiveOrNull(short value) =>
        value > 0 ? value : null;

    private static int PositiveOrDefault(short value, int defaultValue) =>
        value > 0 ? value : defaultValue;

    private static double ValidMarginOrDefault(double value, double defaultValue) =>
        double.IsFinite(value) && value >= 0 ? value : defaultValue;

    private static WorksheetHeaderFooter ToWorksheetHeaderFooter(IHeaderFooter headerFooter)
    {
        if (headerFooter is NPOI.HSSF.UserModel.HeaderFooter legacyHeaderFooter)
            return ParseHeaderFooterRawText(legacyHeaderFooter.RawText);

        return new(headerFooter.Left ?? "", headerFooter.Center ?? "", headerFooter.Right ?? "");
    }

    private static WorksheetHeaderFooter ParseHeaderFooterRawText(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
            return new WorksheetHeaderFooter("", "", "");

        var left = new StringBuilder();
        var center = new StringBuilder();
        var right = new StringBuilder();
        var current = center;

        for (var index = 0; index < rawText.Length; index++)
        {
            if (rawText[index] == '&' && index + 1 < rawText.Length)
            {
                current = rawText[index + 1] switch
                {
                    'L' => left,
                    'C' => center,
                    'R' => right,
                    _ => current
                };

                if (rawText[index + 1] is 'L' or 'C' or 'R')
                {
                    index++;
                    continue;
                }
            }

            current.Append(rawText[index]);
        }

        return new WorksheetHeaderFooter(left.ToString(), center.ToString(), right.ToString());
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

    private static void LoadPictures(ISheet sourceSheet, Sheet sheet)
    {
        if (sourceSheet is not HSSFSheet { DrawingPatriarch: HSSFPatriarch patriarch })
            return;

        foreach (var sourcePicture in EnumeratePictures(patriarch.Children))
        {
            if (TryCreatePicture(sourcePicture, sheet, out var picture))
                sheet.Pictures.Add(picture);
        }
    }

    private static IEnumerable<HSSFPicture> EnumeratePictures(IEnumerable<HSSFShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape is HSSFPicture picture)
                yield return picture;

            if (shape is HSSFShapeGroup group)
            {
                foreach (var nestedPicture in EnumeratePictures(group.Children))
                    yield return nestedPicture;
            }
        }
    }

    private static bool TryCreatePicture(HSSFPicture sourcePicture, Sheet sheet, out PictureModel picture)
    {
        picture = new PictureModel();
        var data = sourcePicture.PictureData;
        if (data?.Data is not { Length: > 0 } bytes ||
            sourcePicture.Anchor is not HSSFClientAnchor anchor ||
            anchor.Row1 < 0 ||
            anchor.Col1 < 0)
        {
            return false;
        }

        var anchorRow = ToModelIndex(Math.Min(anchor.Row1, anchor.Row2));
        var anchorCol = ToModelIndex(Math.Min(anchor.Col1, anchor.Col2));
        picture = new PictureModel
        {
            Anchor = new ModelCellAddress(sheet.Id, anchorRow, anchorCol),
            Kind = PictureKind.Image,
            Name = FirstNonBlank(sourcePicture.Name, sourcePicture.ShapeName, sourcePicture.FileName),
            ImageBytes = bytes.ToArray(),
            ContentType = NormalizePictureContentType(data.MimeType),
            AnchorOffsetX = HssfColumnOffsetToPixels(sheet, anchorCol, Math.Min(anchor.Dx1, anchor.Dx2)),
            AnchorOffsetY = HssfRowOffsetToPixels(sheet, anchorRow, Math.Min(anchor.Dy1, anchor.Dy2)),
            FlipHorizontal = anchor.IsHorizontallyFlipped,
            FlipVertical = anchor.IsVerticallyFlipped
        };

        var (width, height) = GetHssfAnchorSize(sheet, anchor);
        if (width > 0)
            picture.Width = width;
        if (height > 0)
            picture.Height = height;

        return true;
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string NormalizePictureContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? "image/png" : contentType;

    private static (double Width, double Height) GetHssfAnchorSize(Sheet sheet, HSSFClientAnchor anchor)
    {
        var fromColumn = ToModelIndex(Math.Min(anchor.Col1, anchor.Col2));
        var toColumn = ToModelIndex(Math.Max(anchor.Col1, anchor.Col2));
        var fromRow = ToModelIndex(Math.Min(anchor.Row1, anchor.Row2));
        var toRow = ToModelIndex(Math.Max(anchor.Row1, anchor.Row2));
        var fromColumnOffset = HssfColumnOffsetToPixels(sheet, fromColumn, Math.Min(anchor.Dx1, anchor.Dx2));
        var toColumnOffset = HssfColumnOffsetToPixels(sheet, toColumn, Math.Max(anchor.Dx1, anchor.Dx2));
        var fromRowOffset = HssfRowOffsetToPixels(sheet, fromRow, Math.Min(anchor.Dy1, anchor.Dy2));
        var toRowOffset = HssfRowOffsetToPixels(sheet, toRow, Math.Max(anchor.Dy1, anchor.Dy2));

        var width = SumColumnPixels(sheet, fromColumn, toColumn - fromColumn) + toColumnOffset - fromColumnOffset;
        var height = SumRowPixels(sheet, fromRow, toRow - fromRow) + toRowOffset - fromRowOffset;
        return (width, height);
    }

    private static double HssfColumnOffsetToPixels(Sheet sheet, uint column, int offset) =>
        Math.Clamp(offset, 0, 1023) / 1024.0 * GetColumnPixelWidth(sheet, column);

    private static double HssfRowOffsetToPixels(Sheet sheet, uint row, int offset) =>
        Math.Clamp(offset, 0, 255) / 256.0 * GetRowPixelHeight(sheet, row);

    private static double SumColumnPixels(Sheet sheet, uint firstColumn, uint count)
    {
        double width = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var column = firstColumn + offset;
            if (!sheet.IsColEffectivelyHidden(column))
                width += GetColumnPixelWidth(sheet, column);
        }

        return width;
    }

    private static double SumRowPixels(Sheet sheet, uint firstRow, uint count)
    {
        double height = 0;
        for (var offset = 0u; offset < count; offset++)
        {
            var row = firstRow + offset;
            if (!sheet.IsRowEffectivelyHidden(row))
                height += GetRowPixelHeight(sheet, row);
        }

        return height;
    }

    private static double GetColumnPixelWidth(Sheet sheet, uint column) =>
        sheet.ColumnWidths.GetValueOrDefault(column, sheet.DefaultColumnWidth) * 8;

    private static double GetRowPixelHeight(Sheet sheet, uint row) =>
        sheet.RowHeights.GetValueOrDefault(row, sheet.DefaultRowHeight);

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
                definedName.IsFunctionName)
            {
                continue;
            }

            if (TryLoadPrintDefinedName(workbook, definedName))
                continue;

            if (TryLoadAutoFilterDefinedName(workbook, definedName))
                continue;

            if (IsExcelReservedDefinedName(definedName.NameName) ||
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

    private static bool TryLoadAutoFilterDefinedName(Workbook workbook, IName definedName)
    {
        if (!IsAutoFilterDefinedName(definedName.NameName))
            return false;

        if (!TryParseNamedRangeRefersTo(workbook, definedName.RefersToFormula, out var range) ||
            range.Start.Sheet != range.End.Sheet ||
            workbook.GetSheet(range.Start.Sheet) is not { } sheet)
        {
            return true;
        }

        sheet.AutoFilter = new WorksheetAutoFilterModel(range.ToString(), null);
        return true;
    }

    private static bool TryLoadPrintDefinedName(Workbook workbook, IName definedName)
    {
        if (!IsPrintAreaDefinedName(definedName.NameName) &&
            !IsPrintTitlesDefinedName(definedName.NameName))
        {
            return false;
        }

        var refersTo = NormalizeFormula(definedName.RefersToFormula ?? "");
        if (string.IsNullOrWhiteSpace(refersTo))
            return true;

        if (IsPrintAreaDefinedName(definedName.NameName))
        {
            foreach (var reference in SplitFormulaReferences(refersTo))
            {
                if (TryParseNamedRangeRefersTo(workbook, reference, out var printArea) &&
                    workbook.GetSheet(printArea.Start.Sheet) is { } sheet)
                {
                    sheet.PrintArea = printArea;
                    break;
                }
            }

            return true;
        }

        foreach (var reference in SplitFormulaReferences(refersTo))
            TryLoadPrintTitleReference(workbook, reference);

        return true;
    }

    private static bool TryLoadPrintTitleReference(Workbook workbook, string reference)
    {
        if (!TrySplitSheetQualifiedReference(reference.Trim(), out var sheetName, out var rangeText))
            return false;

        var sheet = workbook.GetSheet(sheetName);
        if (sheet is null)
            return false;

        if (TryParseRepeatRows(rangeText, out var rows))
        {
            sheet.PrintTitleRows = rows;
            return true;
        }

        if (TryParseRepeatColumns(rangeText, out var columns))
        {
            sheet.PrintTitleColumns = columns;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> SplitFormulaReferences(string formula)
    {
        var start = 0;
        var inQuote = false;
        for (var index = 0; index < formula.Length; index++)
        {
            if (formula[index] == '\'')
            {
                if (inQuote && index + 1 < formula.Length && formula[index + 1] == '\'')
                {
                    index++;
                    continue;
                }

                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && formula[index] == ',')
            {
                var token = formula[start..index].Trim();
                if (token.Length > 0)
                    yield return token;
                start = index + 1;
            }
        }

        var lastToken = formula[start..].Trim();
        if (lastToken.Length > 0)
            yield return lastToken;
    }

    private static bool TryParseRepeatRows(string rangeText, out WorksheetRepeatRange rows)
    {
        rows = default;
        var parts = rangeText.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseRowReference(parts[0], out var start))
            return false;

        var endText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseRowReference(endText, out var end) ||
            start < 1 ||
            start > end ||
            end > ModelCellAddress.MaxRow)
        {
            return false;
        }

        rows = new WorksheetRepeatRange(start, end);
        return true;
    }

    private static bool TryCreateRepeatRows(CellRangeAddress? range, out WorksheetRepeatRange rows)
    {
        rows = default;
        if (range is null ||
            range.FirstRow < 0 ||
            range.LastRow < range.FirstRow)
        {
            return false;
        }

        rows = new WorksheetRepeatRange(ToModelIndex(range.FirstRow), ToModelIndex(range.LastRow));
        return true;
    }

    private static bool TryParseRepeatColumns(string rangeText, out WorksheetRepeatRange columns)
    {
        columns = default;
        var parts = rangeText.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2)
            return false;

        if (!TryParseColumnReference(parts[0], out var start))
            return false;

        var endText = parts.Length == 2 ? parts[1] : parts[0];
        if (!TryParseColumnReference(endText, out var end) ||
            start < 1 ||
            start > end ||
            end > ModelCellAddress.MaxCol)
        {
            return false;
        }

        columns = new WorksheetRepeatRange(start, end);
        return true;
    }

    private static bool TryCreateRepeatColumns(CellRangeAddress? range, out WorksheetRepeatRange columns)
    {
        columns = default;
        if (range is null ||
            range.FirstColumn < 0 ||
            range.LastColumn < range.FirstColumn)
        {
            return false;
        }

        columns = new WorksheetRepeatRange(ToModelIndex(range.FirstColumn), ToModelIndex(range.LastColumn));
        return true;
    }

    private static bool TryParseRowReference(string text, out uint row) =>
        uint.TryParse(text.Trim().Replace("$", "", StringComparison.Ordinal), out row);

    private static bool TryParseColumnReference(string text, out uint column)
    {
        column = default;
        var normalized = text.Trim().Replace("$", "", StringComparison.Ordinal);
        if (normalized.Length == 0 || normalized.Any(character => !IsAsciiLetter(character)))
            return false;

        try
        {
            column = ModelCellAddress.ColumnNameToNumber(normalized);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsAsciiLetter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

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

    private static bool IsPrintAreaDefinedName(string? name) =>
        IsBuiltInDefinedName(name, "Print_Area");

    private static bool IsPrintTitlesDefinedName(string? name) =>
        IsBuiltInDefinedName(name, "Print_Titles");

    private static bool IsAutoFilterDefinedName(string? name) =>
        IsBuiltInDefinedName(name, "_FilterDatabase") ||
        IsBuiltInDefinedName(name, "FilterDatabase");

    private static bool IsBuiltInDefinedName(string? name, string builtInName)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var trimmedName = name.Trim();
        return string.Equals(trimmedName, builtInName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(trimmedName, "_xlnm." + builtInName, StringComparison.OrdinalIgnoreCase);
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

    private static ModelHorizontalAlignment MapExcelDataReaderHorizontalAlignment(ExcelDataReader.HorizontalAlignment alignment) =>
        alignment switch
        {
            ExcelDataReader.HorizontalAlignment.Left => ModelHorizontalAlignment.Left,
            ExcelDataReader.HorizontalAlignment.Center or ExcelDataReader.HorizontalAlignment.Centered or ExcelDataReader.HorizontalAlignment.CenteredAcrossSelection => ModelHorizontalAlignment.Center,
            ExcelDataReader.HorizontalAlignment.Right => ModelHorizontalAlignment.Right,
            ExcelDataReader.HorizontalAlignment.Justified => ModelHorizontalAlignment.Justify,
            ExcelDataReader.HorizontalAlignment.Distributed => ModelHorizontalAlignment.Distributed,
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

    private static ModelVerticalAlignment MapExcelDataReaderVerticalAlignment(ExcelDataReader.VerticalAlignment alignment) =>
        alignment switch
        {
            ExcelDataReader.VerticalAlignment.Top => ModelVerticalAlignment.Top,
            ExcelDataReader.VerticalAlignment.Center => ModelVerticalAlignment.Center,
            ExcelDataReader.VerticalAlignment.Justify => ModelVerticalAlignment.Justify,
            ExcelDataReader.VerticalAlignment.Distributed => ModelVerticalAlignment.Distributed,
            _ => ModelVerticalAlignment.Bottom
        };

    private readonly record struct ExcelDataReaderStyleKey(
        string NumberFormat,
        ExcelDataReader.HorizontalAlignment HorizontalAlignment,
        ExcelDataReader.VerticalAlignment VerticalAlignment,
        int IndentLevel,
        bool Locked,
        bool Hidden);

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
