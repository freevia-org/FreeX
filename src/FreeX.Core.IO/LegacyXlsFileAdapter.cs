using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using ExcelDataReader;
using Free.Shared.Opc;
using NPOI.HSSF.Model;
using NPOI.HSSF.Record;
using NPOI.HSSF.Record.Common;
using NPOI.HSSF.Record.PivotTable;
using FreeX.Core.Model;
using NPOI.HSSF.UserModel;
using NPOI.POIFS.FileSystem;
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

    // OADate (1900-epoch) serial for 1904-01-01 — the day-count offset between Excel's two date
    // systems. NPOI's HSSFCell.DateCellValue already resolves the workbook's 1904 windowing and
    // hands us a true calendar DateTime, but our formula layer's 1904-aware functions
    // (YEAR/MONTH/DAY/EDATE/DATEDIF/... — see BuiltInFunctions.DateTime.cs / ExcelDateSystem)
    // interpret a stored serial as day-count-since-1904-01-01 when Workbook.Uses1904DateSystem is
    // true. So for a 1904-system workbook the stored serial must be 1904-epoch-relative (not the
    // default 1900-epoch OADate), matching how XlsxClosedXmlCellMapper handles xlsx.
    private const double Date1904EpochOADate = 1462;

    private static readonly FieldInfo? LbsSelectedIndexField =
        typeof(LbsDataSubRecord).GetField("_iSel", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? UnknownRecordRawDataField =
        typeof(UnknownRecord).GetField("_rawData", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? TabIdRecordTabIdsField =
        typeof(TabIdRecord).GetField("_tabids", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo? UseSelFsRecordOptionsField =
        typeof(UseSelFSRecord).GetField("_options", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo? HssfGetObjRecordMethod =
        typeof(HSSFSimpleShape).GetMethod(
            "GetObjRecord",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionNameField =
        typeof(ViewDefinitionRecord).GetField("name", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionCacheField =
        typeof(ViewDefinitionRecord).GetField("iCache", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionFirstRowField =
        typeof(ViewDefinitionRecord).GetField("rwFirst", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionLastRowField =
        typeof(ViewDefinitionRecord).GetField("rwLast", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionFirstColumnField =
        typeof(ViewDefinitionRecord).GetField("colFirst", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionLastColumnField =
        typeof(ViewDefinitionRecord).GetField("colLast", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionFirstHeaderRowField =
        typeof(ViewDefinitionRecord).GetField("rwFirstHead", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionFirstDataRowField =
        typeof(ViewDefinitionRecord).GetField("rwFirstData", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionFirstDataColumnField =
        typeof(ViewDefinitionRecord).GetField("colFirstData", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewDefinitionDataCaptionField =
        typeof(ViewDefinitionRecord).GetField("dataField", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotViewFieldAxisField =
        typeof(ViewFieldsRecord).GetField("sxaxis", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotDataItemSourceField =
        typeof(DataItemRecord).GetField("isxvdData", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotDataItemFunctionField =
        typeof(DataItemRecord).GetField("df", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotDataItemNumberFormatField =
        typeof(DataItemRecord).GetField("ifmt", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? PivotDataItemNameField =
        typeof(DataItemRecord).GetField("name", BindingFlags.Instance | BindingFlags.NonPublic);

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

    // R92-io-legacy-format-read-5-1: message reported whenever the workbook could not be parsed
    // as a BIFF8 (.xls) stream and had to fall back to ExcelDataReader's values-only reader -- the
    // guaranteed path for every .xlsb (BIFF12/BRT) file, and also the path taken when NPOI's
    // HSSFWorkbook throws on a corrupt/unsupported .xls. That reader only reads computed cell
    // values (MapExcelDataReaderCellValue -> reader.GetValue), never formula text, and does not
    // read charts, conditional formatting, data validation, autofilter, or defined names at all,
    // so every one of those features is silently dropped without this warning.
    private const string LegacyBinaryFallbackWarning =
        "[legacy-binary-fallback]: This workbook could not be read as an Excel 97-2003 (BIFF8) file " +
        "and was loaded as static values only -- formulas, charts, conditional formatting, data " +
        "validation, autofilter, and defined names were not preserved.";

    // R92-io-legacy-format-read-5-2: .xls Save always throws (NotSupportedException below), forcing
    // every edit through Save As XLSX/XLSM -- which discards the VBA project bytes entirely since no
    // code path carries them into a newly-written OOXML package. Surface the loss at open time so
    // the user knows before they save over the macros.
    private const string MacroProjectNotPreservedWarning =
        "[macros]: This workbook contains a VBA macro project. FreeX cannot edit or preserve legacy " +
        ".xls macros; they will be permanently discarded when this file is saved (Save As is " +
        "required, since .xls itself cannot be saved back to).";

    public Workbook Load(Stream stream) => LoadWithWarnings(stream).Workbook;

    /// <summary>
    /// Loads a workbook the same way as <see cref="Load"/>, but also returns any non-fatal
    /// diagnostic messages describing legacy-format features that could not be preserved
    /// (see <see cref="XlsxLoadResult.Warnings"/>).
    /// </summary>
    public XlsxLoadResult LoadWithWarnings(Stream stream)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var warnings = new List<string>();

        if (stream.CanSeek)
        {
            var start = stream.Position;
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            try
            {
                using var hssfStream = new MemoryStream(bytes, writable: false);
                var workbook = LoadHssf(hssfStream, warnings);
                return new XlsxLoadResult(workbook, warnings);
            }
            catch (Exception primaryFailure)
            {
                stream.Position = start;
                using var fallbackStream = new MemoryStream(bytes, writable: false);
                var workbook = LoadWithExcelDataReaderOrThrowInvalidData(fallbackStream, primaryFailure);
                warnings.Add(LegacyBinaryFallbackWarning);
                return new XlsxLoadResult(workbook, warnings);
            }
        }

        var nonSeekableWorkbook = LoadWithExcelDataReaderOrThrowInvalidData(stream, primaryFailure: null);
        warnings.Add(LegacyBinaryFallbackWarning);
        return new XlsxLoadResult(nonSeekableWorkbook, warnings);
    }

    public void Save(Workbook workbook, Stream stream) =>
        throw new NotSupportedException("Legacy .xls files are currently open-only. Use Save As XLSX Workbook instead.");

    private static Workbook LoadHssf(Stream stream, List<string> warnings)
    {
        var hasVbaProjectPackage = TryHasVbaProjectPackage(stream);
        if (hasVbaProjectPackage)
            warnings.Add(MacroProjectNotPreservedWarning);

        using var hssf = new HSSFWorkbook(stream);
        var workbook = new Workbook("Untitled")
        {
            Uses1904DateSystem = hssf.IsDate1904(),
            HasVbaProjectPackage = hasVbaProjectPackage
        };
        LoadWorkbookView(hssf, workbook);
        LoadWorkbookCountrySettings(hssf, workbook);
        LoadWorkbookLegacyMenuSettings(hssf, workbook);
        LoadWorkbookLegacyWorkbookSettings(hssf, workbook);
        LoadWorkbookFunctionGroups(hssf, workbook);
        LoadWorkbookProperties(hssf, workbook);
        LoadWorkbookProtection(hssf, workbook);
        LoadFileSharing(hssf, workbook);
        LoadCalculationOptions(hssf, workbook);
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
            sheet.CodeName = ReadHssfSheetCodeName(sourceSheet);

            LoadSheetLayout(sourceSheet, sheet, palette);
            LoadMergedRegions(sourceSheet, sheet);
            LoadCells(hssf, sourceSheet, workbook, sheet, styleCache);
            LoadDrawingObjects(hssf, workbook, sourceSheet, sheet);
            LoadLegacyPivotTables(workbook, sourceSheet, sheet);
        }

        if (workbook.Sheets.Count == 0)
            workbook.AddSheet("Sheet1");

        LoadConditionalFormats(hssf, workbook);
        LoadDataValidations(hssf, workbook);
        LoadDefinedNames(hssf, workbook);

        // R92-io-legacy-format-read-5-3: legacy BIFF embedded charts are anchored via internal
        // "_xlchart.N" defined names (see IsExcelReservedDefinedName below, which exists only to
        // hide them from the user-visible Name Manager) but the chart sub-streams themselves are
        // never modeled -- LoadDrawingObjects only reads pictures/simple shapes. Report the loss so
        // it isn't silent, since no XlsxFeatureReport gate exists for a legacy .xls/.xlsb open.
        var embeddedChartCount = CountEmbeddedLegacyCharts(hssf);
        if (embeddedChartCount > 0)
        {
            warnings.Add(
                $"[charts]: This workbook contains {embeddedChartCount} embedded chart" +
                (embeddedChartCount == 1 ? "" : "s") +
                " that FreeX cannot yet read from legacy .xls/.xlsb files; " +
                (embeddedChartCount == 1 ? "it was" : "they were") +
                " dropped and will not appear after loading.");
        }

        return workbook;
    }

    private static int CountEmbeddedLegacyCharts(NPOIWorkbook sourceWorkbook)
    {
        var count = 0;
        for (var index = 0; index < sourceWorkbook.NumberOfNames; index++)
        {
            var definedName = sourceWorkbook.GetNameAt(index);
            if (definedName is { IsDeleted: false } &&
                definedName.NameName is { } name &&
                name.Trim().StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static bool TryHasVbaProjectPackage(Stream stream)
    {
        if (!stream.CanSeek)
            return false;

        var start = stream.Position;
        try
        {
            var poifs = new POIFSFileSystem(POIFSFileSystem.CreateNonClosingInputStream(stream));
            return DirectoryContainsVbaProject(poifs.Root);
        }
        catch
        {
            return false;
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static bool DirectoryContainsVbaProject(DirectoryEntry directory)
    {
        var entries = directory.Entries;
        while (entries.MoveNext())
        {
            var entry = entries.Current;
            if (string.Equals(entry.Name, "_VBA_PROJECT_CUR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Name, "VBA", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (entry is DirectoryEntry child && DirectoryContainsVbaProject(child))
                return true;
        }

        return false;
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

    private static void LoadWorkbookCountrySettings(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(CountryRecord.sid) is not CountryRecord country)
            return;

        workbook.CountrySettings = new WorkbookCountrySettingsModel
        {
            DefaultCountryId = PositiveOrNull(country.DefaultCountry),
            CurrentCountryId = PositiveOrNull(country.CurrentCountry)
        };
    }

    private static void LoadWorkbookLegacyMenuSettings(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(MMSRecord.sid) is not MMSRecord menuSettings)
            return;

        var addMenuCount = PositiveOrNull(menuSettings.AddMenuCount);
        var deleteMenuCount = PositiveOrNull(menuSettings.DelMenuCount);
        if (addMenuCount is null && deleteMenuCount is null)
            return;

        workbook.LegacyMenuSettings = new WorkbookLegacyMenuSettingsModel
        {
            AddMenuCount = addMenuCount,
            DeleteMenuCount = deleteMenuCount
        };
    }

    private static void LoadWorkbookLegacyWorkbookSettings(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        var sheetTabIds = ReadHssfSheetTabIds(sourceWorkbook);
        var useNaturalLanguageFormulas = ReadHssfUseNaturalLanguageFormulas(sourceWorkbook);
        if (sheetTabIds.Count == 0 && useNaturalLanguageFormulas is null)
            return;

        workbook.LegacyWorkbookSettings = new WorkbookLegacyWorkbookSettingsModel
        {
            SheetTabIds = sheetTabIds,
            UseNaturalLanguageFormulas = useNaturalLanguageFormulas
        };
    }

    private static List<int> ReadHssfSheetTabIds(HSSFWorkbook sourceWorkbook)
    {
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(TabIdRecord.sid) is not TabIdRecord tabIdRecord ||
            TabIdRecordTabIdsField?.GetValue(tabIdRecord) is not short[] tabIds)
        {
            return [];
        }

        return tabIds
            .Select(value => (int)value)
            .Where(value => value >= 0)
            .ToList();
    }

    private static bool? ReadHssfUseNaturalLanguageFormulas(HSSFWorkbook sourceWorkbook)
    {
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(UseSelFSRecord.sid) is not UseSelFSRecord useSelFs ||
            UseSelFsRecordOptionsField?.GetValue(useSelFs) is not { } options)
        {
            return null;
        }

        return Convert.ToInt32(options, CultureInfo.InvariantCulture) != 0;
    }

    private static void LoadWorkbookFunctionGroups(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(FnGroupCountRecord.sid) is not FnGroupCountRecord functionGroups ||
            PositiveOrNull(functionGroups.Count) is not { } builtInGroupCount)
        {
            return;
        }

        workbook.FunctionGroups ??= new WorkbookFunctionGroupsModel();
        workbook.FunctionGroups.BuiltInGroupCount = builtInGroupCount.ToString(CultureInfo.InvariantCulture);
    }

    private static void LoadWorkbookProperties(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        var nativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ReadHssfWorkbookCodeName(sourceWorkbook) is { } codeName)
            nativeAttributes["codeName"] = codeName;
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(BackupRecord.sid) is BackupRecord backup)
            nativeAttributes["backupFile"] = backup.Backup != 0 ? "1" : "0";
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(HideObjRecord.sid) is HideObjRecord hideObjects)
            nativeAttributes["showObjects"] = MapShowObjects(hideObjects.GetHideObj());
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(BookBoolRecord.sid) is BookBoolRecord bookBool)
            nativeAttributes["saveExternalLinkValues"] = bookBool.SaveLinkValues != 0 ? "1" : "0";
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(RefreshAllRecord.sid) is RefreshAllRecord refreshAll)
            nativeAttributes["refreshAllConnections"] = refreshAll.RefreshAll ? "1" : "0";
        if (nativeAttributes.Count == 0)
            return;

        var serializedMetadata = XmlNativeBagSerializer.Serialize(nativeAttributes);
        if (serializedMetadata is null)
            return;

        workbook.Properties ??= new NativeXmlPreserveBag();
        workbook.Properties.Set("workbookPr", serializedMetadata);
    }

    private static string MapShowObjects(short hideObjects) =>
        hideObjects switch
        {
            HideObjRecord.HIDE_ALL => "none",
            HideObjRecord.SHOW_PLACEHOLDERS => "placeholders",
            _ => "all"
        };

    private static void LoadWorkbookProtection(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        var isStructureProtected =
            sourceWorkbook.Workbook.FindFirstRecordBySid(ProtectRecord.sid) is ProtectRecord protect &&
            protect.Protect;
        var isWindowProtected =
            sourceWorkbook.Workbook.FindFirstRecordBySid(WindowProtectRecord.sid) is WindowProtectRecord windowProtect &&
            windowProtect.Protect;

        workbook.IsStructureProtected = isStructureProtected;
        if (isStructureProtected &&
            sourceWorkbook.Workbook.FindFirstRecordBySid(PasswordRecord.sid) is PasswordRecord { Password: not 0 } password)
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

    private static void LoadFileSharing(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        var writeAccessUser = GetWriteAccessUser(sourceWorkbook);
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(FileSharingRecord.sid) is not FileSharingRecord fileSharing)
        {
            if (writeAccessUser is not null)
            {
                workbook.FileSharing = new WorkbookFileSharingModel
                {
                    UserName = writeAccessUser
                };
            }

            return;
        }

        var readOnlyRecommended = fileSharing.ReadOnly != 0;
        var userName = string.IsNullOrWhiteSpace(fileSharing.Username) ? writeAccessUser : fileSharing.Username.Trim();
        var reservationPassword = fileSharing.Password != 0
            ? ((ushort)fileSharing.Password).ToString("X4", CultureInfo.InvariantCulture)
            : null;

        if (!readOnlyRecommended &&
            userName is null &&
            reservationPassword is null)
        {
            return;
        }

        workbook.FileSharing = new WorkbookFileSharingModel
        {
            ReadOnlyRecommended = readOnlyRecommended,
            UserName = userName,
            ReservationPassword = reservationPassword
        };
    }

    private static string? GetWriteAccessUser(HSSFWorkbook sourceWorkbook)
    {
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(WriteAccessRecord.sid) is not WriteAccessRecord writeAccess)
            return null;

        var userName = writeAccess.Username?.Trim();
        return string.IsNullOrWhiteSpace(userName) ? null : userName;
    }

    private static void LoadCalculationOptions(HSSFWorkbook sourceWorkbook, Workbook workbook)
    {
        workbook.FullCalculationOnLoad = sourceWorkbook.ForceFormulaRecalculation;

        if (FindCalculationRecord<CalcModeRecord>(sourceWorkbook, CalcModeRecord.sid) is { } calcMode)
        {
            workbook.CalculationMode = calcMode.GetCalcMode() == CalcModeRecord.MANUAL
                ? WorkbookCalculationMode.Manual
                : WorkbookCalculationMode.Automatic;
        }

        if (FindCalculationRecord<IterationRecord>(sourceWorkbook, IterationRecord.sid) is { } iteration)
            workbook.IterativeCalculation = iteration.Iteration;

        if (FindCalculationRecord<CalcCountRecord>(sourceWorkbook, CalcCountRecord.sid) is { } calcCount &&
            calcCount.Iterations is > 0 and not 100)
        {
            workbook.MaxCalculationIterations = calcCount.Iterations;
        }

        if (FindCalculationRecord<DeltaRecord>(sourceWorkbook, DeltaRecord.sid) is { } delta &&
            delta.MaxChange > 0 &&
            Math.Abs(delta.MaxChange - 0.001) > 0.0000000001)
        {
            workbook.MaxCalculationChange = delta.MaxChange;
        }
    }

    private static TRecord? FindCalculationRecord<TRecord>(HSSFWorkbook sourceWorkbook, short sid)
        where TRecord : class
    {
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(sid) is TRecord workbookRecord)
            return workbookRecord;

        if (sourceWorkbook.NumberOfSheets == 0 ||
            sourceWorkbook.GetSheetAt(0) is not HSSFSheet firstSheet)
        {
            return null;
        }

        return firstSheet.Sheet.FindFirstRecordBySid(sid) as TRecord;
    }

    /// <summary>
    /// Runs the ExcelDataReader fallback, converting any reader failure into
    /// <see cref="InvalidDataException"/>.
    /// <para>
    /// This is the last resort for a legacy binary workbook: the primary NPOI path has already
    /// failed (or the stream is not seekable). ExcelDataReader throws its own exception types for a
    /// corrupt/truncated file or one whose extension lies about its format, and those escaped
    /// uncaught — from inside a catch block, which also discarded the original NPOI failure. Callers
    /// such as <c>StartupWorkbookLoader</c> filter on the standard IO exception types, so normalise
    /// to <see cref="InvalidDataException"/> and keep the primary failure as the inner exception.
    /// </para>
    /// </summary>
    private static Workbook LoadWithExcelDataReaderOrThrowInvalidData(Stream stream, Exception? primaryFailure)
    {
        try
        {
            return LoadWithExcelDataReader(stream);
        }
        catch (Exception fallbackFailure)
        {
            throw new InvalidDataException(
                "The file could not be read as a legacy Excel workbook. It may be corrupt, truncated, " +
                "or not actually in the format its file extension indicates.",
                primaryFailure ?? fallbackFailure);
        }
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
                    var value = MapExcelDataReaderCellValue(reader, col);
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
        sheet.CodeName = NullIfWhiteSpace(reader.CodeName);

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
        LoadSheetKind(sourceSheet, sheet);
        LoadPaneState(sourceSheet, sheet);
        LoadPrintTitles(sourceSheet, sheet);
        LoadPageLayout(sourceSheet, sheet);
        LoadSheetView(sourceSheet, sheet, palette);
        LoadSheetProtection(sourceSheet, sheet);
        LoadAllowEditRanges(sourceSheet, sheet);
        sheet.FullCalculationOnLoad = sourceSheet.ForceFormulaRecalculation;

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
        LoadOutlineSettings(sourceSheet, sheet);
    }

    private static void LoadSheetKind(ISheet sourceSheet, Sheet sheet)
    {
        if (sourceSheet is HSSFSheet hssfSheet &&
            hssfSheet.Sheet.FindFirstRecordBySid(WSBoolRecord.sid) is WSBoolRecord { Dialog: true })
        {
            sheet.Kind = SheetKind.DialogSheet;
        }
    }

    private static void LoadSheetProtection(ISheet sourceSheet, Sheet sheet)
    {
        var isObjectProtected = sourceSheet is HSSFSheet hssfSheet && hssfSheet.ObjectProtect;
        var isScenarioProtected = sourceSheet.ScenarioProtect;
        sheet.IsProtected = sourceSheet.Protect || isObjectProtected || isScenarioProtected;

        if (sourceSheet is HSSFSheet { Password: not 0 } protectedSheet)
            sheet.ProtectionPassword = ((ushort)protectedSheet.Password).ToString("X4", CultureInfo.InvariantCulture);

        // Mirror XlsxSheetProtectionPermissionMapper.Read's polarity: "objects"/"scenarios" TRUE
        // means the action is denied while protected, so it's only added to Permissions (meaning
        // "allowed") when the source sheet did NOT protect it. Without this, a .xls whose Protect
        // Sheet dialog left Objects/Scenarios editable would always re-save as .xlsx with them
        // denied (Sheet.ProtectionPermissions defaults to just the two Select* entries).
        if (!isObjectProtected)
            sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        if (!isScenarioProtected)
            sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditScenarios);

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

    /// <summary>
    /// Reads Excel's binary "Allow Users to Edit Ranges" feature (a <c>FeatHdrRecord</c> +
    /// per-range <c>FeatRecord</c>s of shared-feature type <c>SHAREDFEATURES_ISFPROTECTION</c>,
    /// each carrying a <see cref="NPOI.HSSF.Record.Common.FeatProtection"/> payload with the
    /// range's own password verifier) into <see cref="Sheet.AllowEditRanges"/>/
    /// <see cref="Sheet.AllowEditRangePasswords"/>, mirroring how <c>XlsxAllowEditRangeMapper.Read</c>
    /// models the equivalent OOXML <c>&lt;protectedRanges&gt;</c> element. Without this, every
    /// range a .xls sheet left editable under protection would silently become fully locked the
    /// moment FreeX opens and re-saves it as .xlsx.
    /// </summary>
    private static void LoadAllowEditRanges(ISheet sourceSheet, Sheet sheet)
    {
        if (sourceSheet is not HSSFSheet hssfSheet)
            return;

        foreach (var featRecord in hssfSheet.Sheet.Records.OfType<FeatRecord>())
        {
            if (featRecord.Isf_sharedFeatureType != FeatHdrRecord.SHAREDFEATURES_ISFPROTECTION)
                continue;

            string? rangePassword = null;
            if (featRecord.SharedFeature is FeatProtection featProtection)
            {
                var verifier = featProtection.GetPasswordVerifier();
                if (verifier != 0)
                    rangePassword = ((ushort)verifier).ToString("X4", CultureInfo.InvariantCulture);
            }

            foreach (var cellRef in featRecord.CellRefs ?? [])
            {
                var range = ToGridRange(cellRef, sheet.Id);
                sheet.AllowEditRanges.Add(range);
                if (rangePassword is not null)
                    sheet.AllowEditRangePasswords[range] = rangePassword;
            }
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
            // Index 64 is the BIFF/HSSF automatic-color sentinel; index 0 is a real palette entry.
            if (pattern.FillPattern != FillPattern.NoFill && pattern.FillForegroundColor != 64)
                style.FillColor = GetIndexedColor(sourceWorkbook, pattern.FillForegroundColor);
            if (pattern.FillBackgroundColor != 0 && pattern.FillBackgroundColor != 64)
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

    private static void LoadOutlineSettings(ISheet sourceSheet, Sheet sheet)
    {
        var hasOutlineLevels = sheet.RowOutlineLevels.Count > 0 || sheet.ColOutlineLevels.Count > 0;
        if (!hasOutlineLevels &&
            sourceSheet.RowSumsBelow &&
            sourceSheet.RowSumsRight &&
            sourceSheet.DisplayGuts)
        {
            return;
        }

        sheet.OutlineSummaryBelow = sourceSheet.RowSumsBelow;
        sheet.OutlineSummaryRight = sourceSheet.RowSumsRight;
        sheet.ShowOutlineSymbols = sourceSheet.DisplayGuts;
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
        LoadPrintOptionsMetadata(sourceSheet, sheet);
        LoadLegacyPrintSize(sourceSheet, sheet);
    }

    private static void LoadPrintOptionsMetadata(ISheet sourceSheet, Sheet sheet)
    {
        if (sourceSheet is not HSSFSheet hssfSheet ||
            hssfSheet.Sheet.FindFirstRecordBySid(GridsetRecord.sid) is not GridsetRecord gridset)
        {
            return;
        }

        var serializedMetadata = XmlNativeBagSerializer.Serialize(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["gridLinesSet"] = gridset.Gridset ? "1" : "0"
            });
        if (serializedMetadata is null)
            return;

        sheet.PrintOptionsMetadata ??= new NativeXmlPreserveBag();
        sheet.PrintOptionsMetadata.Set("printOptions", serializedMetadata);
    }

    private static void LoadLegacyPrintSize(ISheet sourceSheet, Sheet sheet)
    {
        if (sourceSheet is not HSSFSheet hssfSheet ||
            hssfSheet.Sheet.FindFirstRecordBySid(PrintSizeRecord.sid) is not PrintSizeRecord printSize)
        {
            return;
        }

        sheet.LegacyPrintSize = PositiveOrNull(printSize.PrintSize);
    }

    private static void LoadSheetView(ISheet sourceSheet, Sheet sheet, HSSFPalette palette)
    {
        sheet.ShowGridlines = sourceSheet.DisplayGridlines;
        sheet.ShowHeadings = sourceSheet.DisplayRowColHeadings;
        sheet.ShowFormulas = sourceSheet.DisplayFormulas;
        sheet.ShowZeros = sourceSheet.DisplayZeros;
        if (sourceSheet.TopRow > 0)
            sheet.ViewTopRow = ToModelIndex(sourceSheet.TopRow);
        if (sourceSheet.LeftCol > 0)
            sheet.ViewLeftCol = ToModelIndex(sourceSheet.LeftCol);
        if (sourceSheet.ActiveCell is { } activeCell &&
            (activeCell.Row > 0 || activeCell.Column > 0))
        {
            sheet.ActiveRow = ToModelIndex(activeCell.Row);
            sheet.ActiveCol = ToModelIndex(activeCell.Column);
        }

        if (TryGetWindowTwoRecord(sourceSheet) is { } window)
        {
            LoadPrimaryViewMetadata(sourceSheet, window, sheet);

            if (window.SavedInPageBreakPreview)
                sheet.ViewMode = WorksheetViewMode.PageBreakPreview;

            if (GetValidWindowZoom(window) is { } zoom)
                sheet.ZoomPercent = zoom;
            else if (GetValidScaleZoom(sourceSheet) is { } scaleZoom)
                sheet.ZoomPercent = scaleZoom;
        }
        else if (GetValidScaleZoom(sourceSheet) is { } scaleZoom)
        {
            sheet.ZoomPercent = scaleZoom;
        }

        if (TryGetTabColor(sourceSheet, palette, out var tabColor))
            sheet.TabColor = tabColor;
    }

    private static int? GetValidWindowZoom(WindowTwoRecord window)
    {
        var zoom = window.SavedInPageBreakPreview && window.PageBreakZoom > 0
            ? window.PageBreakZoom
            : window.NormalZoom;
        return zoom is >= 10 and <= 400 ? zoom : null;
    }

    private static int? GetValidScaleZoom(ISheet sourceSheet)
    {
        if (sourceSheet is not HSSFSheet hssfSheet ||
            hssfSheet.Sheet.FindFirstRecordBySid(SCLRecord.sid) is not SCLRecord scale ||
            scale.Denominator <= 0)
        {
            return null;
        }

        var zoom = (int)Math.Round(scale.Numerator * 100d / scale.Denominator, MidpointRounding.AwayFromZero);
        return zoom is >= 10 and <= 400 ? zoom : null;
    }

    private static void LoadPrimaryViewMetadata(ISheet sourceSheet, WindowTwoRecord window, Sheet sheet)
    {
        var nativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal);
        var nativeChildren = new List<string>();
        if (window.IsSelected)
            nativeAttributes["tabSelected"] = "1";
        if (!window.DefaultHeader)
            nativeAttributes["defaultGridColor"] = "0";
        if (window.HeaderColor != 64)
            nativeAttributes["colorId"] = window.HeaderColor.ToString(CultureInfo.InvariantCulture);
        nativeChildren.AddRange(CreateSelectionMetadata(sourceSheet, sheet.Id));
        if (nativeAttributes.Count == 0 && nativeChildren.Count == 0)
            return;

        var serializedMetadata = XmlNativeBagSerializer.Serialize(nativeAttributes, nativeChildren);
        if (serializedMetadata is null)
            return;

        sheet.PrimaryViewMetadata ??= new NativeXmlPreserveBag();
        sheet.PrimaryViewMetadata.Set("sheetView", serializedMetadata);
    }

    // BIFF writes one SELECTION record per pane when the sheet has frozen/split panes (each
    // carrying its own Pane byte identifying which of the 4 possible panes it belongs to).
    // Building metadata from only the first record found (regardless of which pane it actually
    // came from) silently discards every other pane's selection extent/activeCellId and can
    // mislabel a non-topLeft pane's data as topLeft's own. Iterate every SelectionRecord and emit
    // one <selection> per pane, tagging each with its real pane name whenever more than one
    // pane's worth of selection state exists so downstream pane-matching attributes each fragment
    // to the correct pane instead of assuming "topLeft" for all of them.
    private static List<string> CreateSelectionMetadata(ISheet sourceSheet, SheetId sheetId)
    {
        var result = new List<string>();
        var selections = GetSelectionRecords(sourceSheet);
        var includePaneAttribute = selections.Count > 1;
        foreach (var selection in selections)
        {
            if (TryCreateSelectionElement(selection, sheetId, includePaneAttribute, out var metadata))
                result.Add(metadata);
        }

        return result;
    }

    private static bool TryCreateSelectionElement(
        SelectionRecord selection,
        SheetId sheetId,
        bool includePaneAttribute,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? metadata)
    {
        metadata = null;
        var activeCell = ToA1(sheetId, selection.ActiveCellRow, selection.ActiveCellCol);
        var selectedRange = ToSqref(sheetId, selection.CellReferences);
        var hasSelectedRange = !string.IsNullOrWhiteSpace(selectedRange) &&
            !string.Equals(selectedRange, activeCell, StringComparison.Ordinal);
        var hasActiveCellId = selection.ActiveCellRef > 0;
        if (!hasSelectedRange && !hasActiveCellId)
            return false;

        var element = new XElement(
            XName.Get("selection", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"),
            includePaneAttribute ? new XAttribute("pane", ToPaneName(selection.Pane)) : null,
            new XAttribute("activeCell", activeCell),
            new XAttribute("sqref", hasSelectedRange ? selectedRange : activeCell));
        if (hasActiveCellId)
            element.SetAttributeValue("activeCellId", selection.ActiveCellRef.ToString(CultureInfo.InvariantCulture));

        metadata = element.ToString(SaveOptions.DisableFormatting);
        return true;
    }

    // Mirrors NPOI.SS.Util.PaneInformation's PANE_LOWER_RIGHT/PANE_UPPER_RIGHT/PANE_LOWER_LEFT/
    // PANE_UPPER_LEFT constants (0/1/2/3), which is also how NPOI.HSSF.Record.SelectionRecord.Pane
    // is populated -- matching ECMA-376's CT_Selection/@pane ST_Pane enum names.
    private static string ToPaneName(byte pane) => pane switch
    {
        0 => "bottomRight",
        1 => "topRight",
        2 => "bottomLeft",
        _ => "topLeft"
    };

    private static string ToSqref(SheetId sheetId, IEnumerable<CellRangeAddress8Bit> ranges) =>
        string.Join(" ", ranges.Select(range => ToA1Range(sheetId, range)));

    private static string ToA1Range(SheetId sheetId, CellRangeAddress8Bit range)
    {
        var first = ToA1(sheetId, range.FirstRow, range.FirstColumn);
        var last = ToA1(sheetId, range.LastRow, range.LastColumn);
        return string.Equals(first, last, StringComparison.Ordinal) ? first : $"{first}:{last}";
    }

    private static string ToA1(SheetId sheetId, int rowIndex, int columnIndex) =>
        new ModelCellAddress(sheetId, ToModelIndex(rowIndex), ToModelIndex(columnIndex)).ToA1();

    private static WindowTwoRecord? TryGetWindowTwoRecord(ISheet sourceSheet) =>
        sourceSheet is HSSFSheet hssfSheet
            ? hssfSheet.Sheet.FindFirstRecordBySid(WindowTwoRecord.sid) as WindowTwoRecord
            : null;

    // FindFirstRecordBySid only ever returns the first SELECTION record in the BIFF stream, which
    // silently drops every other pane's selection when the sheet has frozen/split panes (BIFF
    // writes one SelectionRecord per pane). Read the full underlying record list instead so every
    // pane's selection state can be preserved (see CreateSelectionMetadata above).
    private static List<SelectionRecord> GetSelectionRecords(ISheet sourceSheet) =>
        sourceSheet is HSSFSheet hssfSheet
            ? hssfSheet.Sheet.Records.OfType<SelectionRecord>().ToList()
            : [];

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

    private static void LoadDrawingObjects(HSSFWorkbook sourceWorkbook, Workbook workbook, ISheet sourceSheet, Sheet sheet)
    {
        if (sourceSheet is not HSSFSheet { DrawingPatriarch: HSSFPatriarch patriarch })
            return;

        foreach (var sourcePicture in EnumeratePictures(patriarch.Children))
        {
            if (TryCreatePicture(sourcePicture, sheet, out var picture))
                sheet.Pictures.Add(picture);
        }

        foreach (var sourceTextBox in EnumerateTextBoxes(patriarch.Children))
        {
            if (TryCreateTextBox(sourceTextBox, sheet, out var textBox))
                sheet.TextBoxes.Add(textBox);
        }

        foreach (var sourceControl in EnumerateFormControls(patriarch.Children))
        {
            if (TryCreateFormControl(sourceWorkbook, workbook, sourceControl, sheet, out var control))
                sheet.FormControls.Add(control);
        }

        foreach (var sourceShape in EnumerateSimpleShapes(patriarch.Children))
        {
            if (TryCreateDrawingShape(sourceShape, sheet, out var shape))
                sheet.DrawingShapes.Add(shape);
        }
    }

    private static void LoadLegacyPivotTables(Workbook workbook, ISheet sourceSheet, Sheet sheet)
    {
        if (sourceSheet is not HSSFSheet hssfSheet)
            return;

        var records = hssfSheet.Sheet.Records.Cast<object>().ToArray();
        for (var recordIndex = 0; recordIndex < records.Length; recordIndex++)
        {
            if (records[recordIndex] is not ViewDefinitionRecord definition)
                continue;

            var viewFields = new List<ViewFieldsRecord>();
            var dataItems = new List<DataItemRecord>();
            for (var nextIndex = recordIndex + 1; nextIndex < records.Length; nextIndex++)
            {
                if (records[nextIndex] is ViewDefinitionRecord)
                    break;
                if (records[nextIndex] is ViewFieldsRecord viewField)
                    viewFields.Add(viewField);
                else if (records[nextIndex] is DataItemRecord dataItem)
                    dataItems.Add(dataItem);
            }

            if (CreateLegacyPivotTable(sheet, definition, viewFields, dataItems, sheet.PivotTables.Count + 1) is not { } pivot)
                continue;

            if (!workbook.PivotCaches.Any(cache => cache.CacheId == pivot.CacheId))
            {
                workbook.PivotCaches.Add(new PivotCacheModel
                {
                    CacheId = pivot.CacheId,
                    SourceType = PivotCacheSourceType.Unknown
                });
            }

            sheet.PivotTables.Add(pivot);
        }
    }

    internal static PivotTableModel? CreateLegacyPivotTable(
        Sheet sheet,
        ViewDefinitionRecord definition,
        IReadOnlyList<ViewFieldsRecord> viewFields,
        IReadOnlyList<DataItemRecord> dataItems,
        int ordinal)
    {
        if (!TryGetPivotRecordInt(definition, PivotViewDefinitionFirstRowField, out var firstRow) ||
            !TryGetPivotRecordInt(definition, PivotViewDefinitionLastRowField, out var lastRow) ||
            !TryGetPivotRecordInt(definition, PivotViewDefinitionFirstColumnField, out var firstColumn) ||
            !TryGetPivotRecordInt(definition, PivotViewDefinitionLastColumnField, out var lastColumn))
        {
            return null;
        }

        if (lastRow < firstRow || lastColumn < firstColumn)
            return null;

        var cacheId = TryGetPivotRecordInt(definition, PivotViewDefinitionCacheField, out var cacheIndex)
            ? Math.Max(0, cacheIndex)
            : 0;
        var targetRange = new GridRange(
            new ModelCellAddress(sheet.Id, ToModelIndex(firstRow), ToModelIndex(firstColumn)),
            new ModelCellAddress(sheet.Id, ToModelIndex(lastRow), ToModelIndex(lastColumn)));
        var sourceRange = sheet.GetUsedRange() ?? targetRange;
        var pivot = new PivotTableModel
        {
            Name = ReadPivotRecordString(definition, PivotViewDefinitionNameField) ?? $"PivotTable{ordinal}",
            CacheId = cacheId,
            SourceRange = sourceRange,
            TargetRange = targetRange,
            LastRenderedRange = targetRange,
            FirstHeaderRow = ToOneBasedOffset(firstRow, PivotViewDefinitionFirstHeaderRowField, definition),
            FirstDataRow = ToOneBasedOffset(firstRow, PivotViewDefinitionFirstDataRowField, definition),
            FirstDataColumn = ToOneBasedOffset(firstColumn, PivotViewDefinitionFirstDataColumnField, definition),
            DataCaption = ReadPivotRecordString(definition, PivotViewDefinitionDataCaptionField),
            StyleName = "PivotStyleLight16"
        };

        for (var fieldIndex = 0; fieldIndex < viewFields.Count; fieldIndex++)
        {
            if (!TryGetPivotRecordInt(viewFields[fieldIndex], PivotViewFieldAxisField, out var axis))
                continue;

            var field = new PivotFieldModel(fieldIndex);
            if ((axis & 0x1) != 0)
                pivot.RowFields.Add(field);
            if ((axis & 0x2) != 0)
                pivot.ColumnFields.Add(field);
            if ((axis & 0x4) != 0)
                pivot.PageFields.Add(field);
        }

        foreach (var dataItem in dataItems)
        {
            var sourceFieldIndex = TryGetPivotRecordInt(dataItem, PivotDataItemSourceField, out var dataSource)
                ? dataSource
                : Math.Max(0, viewFields.Count - 1);
            var numberFormatId = TryGetPivotRecordInt(dataItem, PivotDataItemNumberFormatField, out var formatId) && formatId >= 0
                ? formatId
                : null as int?;
            var name = ReadPivotRecordString(dataItem, PivotDataItemNameField);
            pivot.DataFields.Add(new PivotDataFieldModel(
                sourceFieldIndex,
                string.IsNullOrWhiteSpace(name) ? $"Data Field {pivot.DataFields.Count + 1}" : name,
                MapLegacyPivotSummaryFunction(TryGetPivotRecordInt(dataItem, PivotDataItemFunctionField, out var function) ? function : 0),
                numberFormatId));
        }

        if (pivot.DataFields.Count == 0 && viewFields.Count > 0)
            pivot.DataFields.Add(new PivotDataFieldModel(viewFields.Count - 1, "Data Field 1", "sum"));

        return pivot;
    }

    private static int ToOneBasedOffset(int firstZeroBased, FieldInfo? field, object record) =>
        TryGetPivotRecordInt(record, field, out var absoluteZeroBased) && absoluteZeroBased >= firstZeroBased
            ? absoluteZeroBased - firstZeroBased + 1
            : 1;

    private static bool TryGetPivotRecordInt(object record, FieldInfo? field, out int value)
    {
        value = 0;
        if (field?.GetValue(record) is not { } raw)
            return false;

        switch (raw)
        {
            case byte byteValue:
                value = byteValue;
                return true;
            case short shortValue:
                value = shortValue;
                return true;
            case ushort ushortValue:
                value = ushortValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case uint uintValue when uintValue <= int.MaxValue:
                value = (int)uintValue;
                return true;
            default:
                return false;
        }
    }

    private static string? ReadPivotRecordString(object record, FieldInfo? field) =>
        field?.GetValue(record) as string;

    private static string MapLegacyPivotSummaryFunction(int function) =>
        function switch
        {
            1 => "count",
            2 => "average",
            3 => "max",
            4 => "min",
            5 => "product",
            6 => "countNums",
            7 => "stdDev",
            8 => "stdDevP",
            9 => "var",
            10 => "varP",
            _ => "sum"
        };

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

    private static IEnumerable<HSSFTextbox> EnumerateTextBoxes(IEnumerable<HSSFShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape is HSSFTextbox textBox && shape is not HSSFComment)
                yield return textBox;

            if (shape is HSSFShapeGroup group)
            {
                foreach (var nestedTextBox in EnumerateTextBoxes(group.Children))
                    yield return nestedTextBox;
            }
        }
    }

    private static IEnumerable<HSSFSimpleShape> EnumerateSimpleShapes(IEnumerable<HSSFShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape is HSSFSimpleShape simpleShape &&
                shape is not HSSFTextbox &&
                shape is not HSSFComment &&
                shape is not HSSFPicture &&
                shape is not HSSFCombobox)
            {
                yield return simpleShape;
            }

            if (shape is HSSFShapeGroup group)
            {
                foreach (var nestedShape in EnumerateSimpleShapes(group.Children))
                    yield return nestedShape;
            }
        }
    }

    private static IEnumerable<HSSFSimpleShape> EnumerateFormControls(IEnumerable<HSSFShape> shapes)
    {
        foreach (var shape in shapes)
        {
            if (shape is HSSFCombobox comboBox)
            {
                yield return comboBox;
            }
            else if (shape is HSSFSimpleShape { ShapeType: HSSFSimpleShape.OBJECT_TYPE_COMBO_BOX } comboShape)
            {
                yield return comboShape;
            }

            if (shape is HSSFShapeGroup group)
            {
                foreach (var nestedControl in EnumerateFormControls(group.Children))
                    yield return nestedControl;
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

    private static bool TryCreateTextBox(HSSFTextbox sourceTextBox, Sheet sheet, out TextBoxModel textBox)
    {
        textBox = new TextBoxModel();
        if (sourceTextBox.Anchor is not HSSFClientAnchor anchor ||
            anchor.Row1 < 0 ||
            anchor.Col1 < 0)
        {
            return false;
        }

        var anchorRow = ToModelIndex(Math.Min(anchor.Row1, anchor.Row2));
        var anchorCol = ToModelIndex(Math.Min(anchor.Col1, anchor.Col2));
        textBox = new TextBoxModel
        {
            Anchor = new ModelCellAddress(sheet.Id, anchorRow, anchorCol),
            Name = FirstNonBlank(sourceTextBox.Name, sourceTextBox.ShapeName),
            Text = sourceTextBox.String?.String ?? "",
            AnchorOffsetX = HssfColumnOffsetToPixels(sheet, anchorCol, Math.Min(anchor.Dx1, anchor.Dx2)),
            AnchorOffsetY = HssfRowOffsetToPixels(sheet, anchorRow, Math.Min(anchor.Dy1, anchor.Dy2)),
            RotationDegrees = sourceTextBox.RotationDegree,
            FlipHorizontal = anchor.IsHorizontallyFlipped || sourceTextBox.FlipHorizontal || sourceTextBox.IsFlipHorizontal,
            FlipVertical = anchor.IsVerticallyFlipped || sourceTextBox.FlipVertical || sourceTextBox.IsFlipVertical,
            HasFill = !sourceTextBox.IsNoFill,
            IsSourceLoaded = true
        };

        if (TryGetHssfRgbColor(sourceTextBox.FillColor, out var fillColor))
            textBox.FillColor = fillColor;
        if (TryGetHssfRgbColor(sourceTextBox.LineStyleColor, out var outlineColor))
            textBox.OutlineColor = outlineColor;

        var (width, height) = GetHssfAnchorSize(sheet, anchor);
        if (width > 0)
            textBox.Width = width;
        if (height > 0)
            textBox.Height = height;

        return true;
    }

    private static bool TryCreateFormControl(
        HSSFWorkbook sourceWorkbook,
        Workbook workbook,
        HSSFSimpleShape sourceControl,
        Sheet sheet,
        out FormControlModel control)
    {
        control = new FormControlModel();
        if (MapHssfFormControlKind(sourceControl.ShapeType) is not { } kind ||
            sourceControl.Anchor is not HSSFClientAnchor anchor ||
            anchor.Row1 < 0 ||
            anchor.Col1 < 0 ||
            IsAutoFilterDropDown(sourceWorkbook, workbook, anchor))
        {
            return false;
        }

        var fromRow = Math.Min(anchor.Row1, anchor.Row2);
        var fromCol = Math.Min(anchor.Col1, anchor.Col2);
        var toRow = Math.Max(anchor.Row1, anchor.Row2);
        var toCol = Math.Max(anchor.Col1, anchor.Col2);
        control = new FormControlModel
        {
            Kind = kind,
            Name = FirstNonBlank(sourceControl.Name, sourceControl.ShapeName),
            ShapeId = sourceControl.ShapeId > 0 ? (uint)sourceControl.ShapeId : null,
            Anchor = new GridRange(
                new ModelCellAddress(sheet.Id, ToModelIndex(fromRow), ToModelIndex(fromCol)),
                new ModelCellAddress(sheet.Id, ToModelIndex(toRow), ToModelIndex(toCol))),
            AnchorOffsets = new DrawingAnchorRange(
                new DrawingAnchorPoint(
                    (uint)fromCol,
                    DrawingMlUnits.PixelsToEmu(HssfColumnOffsetToPixels(sheet, ToModelIndex(fromCol), Math.Min(anchor.Dx1, anchor.Dx2))),
                    (uint)fromRow,
                    DrawingMlUnits.PixelsToEmu(HssfRowOffsetToPixels(sheet, ToModelIndex(fromRow), Math.Min(anchor.Dy1, anchor.Dy2)))),
                new DrawingAnchorPoint(
                    (uint)toCol,
                    DrawingMlUnits.PixelsToEmu(HssfColumnOffsetToPixels(sheet, ToModelIndex(toCol), Math.Max(anchor.Dx1, anchor.Dx2))),
                    (uint)toRow,
                    DrawingMlUnits.PixelsToEmu(HssfRowOffsetToPixels(sheet, ToModelIndex(toRow), Math.Max(anchor.Dy1, anchor.Dy2)))))
        };

        TryPopulateFormControlListMetadata(sourceWorkbook, sourceControl, control);
        return true;
    }

    private static void TryPopulateFormControlListMetadata(
        HSSFWorkbook sourceWorkbook,
        HSSFSimpleShape sourceControl,
        FormControlModel control)
    {
        if (control.Kind is not (FormControlKind.DropDown or FormControlKind.ListBox) ||
            TryGetLbsDataSubRecord(sourceControl) is not { } lbsData)
        {
            return;
        }

        if (TryFormatLbsListFillRange(sourceWorkbook, lbsData, out var listFillRange))
            control.ListFillRange = listFillRange;

        if (TryGetLbsSelectedIndex(lbsData, out var selectedIndex))
            control.SelectedIndex = selectedIndex;
    }

    private static LbsDataSubRecord? TryGetLbsDataSubRecord(HSSFSimpleShape sourceControl)
    {
        try
        {
            return TryGetObjRecord(sourceControl)?.SubRecords
                    .OfType<LbsDataSubRecord>()
                    .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static ObjRecord? TryGetObjRecord(HSSFSimpleShape sourceControl) =>
        HssfGetObjRecordMethod?.Invoke(sourceControl, null) as ObjRecord;

    private static bool TryFormatLbsListFillRange(
        HSSFWorkbook sourceWorkbook,
        LbsDataSubRecord lbsData,
        out string listFillRange)
    {
        listFillRange = "";
        if (lbsData.Formula is not { } formula)
            return false;

        try
        {
            var text = NormalizeFormula(HSSFFormulaParser.ToFormulaString(sourceWorkbook, [formula])).Trim();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            listFillRange = text;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetLbsSelectedIndex(LbsDataSubRecord lbsData, out int selectedIndex)
    {
        selectedIndex = 0;
        if (LbsSelectedIndexField?.GetValue(lbsData) is not int raw || raw <= 0)
            return false;

        selectedIndex = raw;
        return true;
    }

    private static bool IsAutoFilterDropDown(HSSFWorkbook sourceWorkbook, Workbook workbook, HSSFClientAnchor anchor)
    {
        var anchorRow = ToModelIndex(Math.Min(anchor.Row1, anchor.Row2));
        var anchorCol = ToModelIndex(Math.Min(anchor.Col1, anchor.Col2));

        for (var index = 0; index < sourceWorkbook.NumberOfNames; index++)
        {
            var definedName = sourceWorkbook.GetNameAt(index);
            if (definedName is null ||
                definedName.IsDeleted ||
                !IsAutoFilterDefinedName(definedName.NameName) ||
                !TryParseNamedRangeRefersTo(workbook, definedName.RefersToFormula, out var range))
            {
                continue;
            }

            if (range.Start.Sheet == range.End.Sheet &&
                anchorRow == range.Start.Row &&
                anchorCol >= range.Start.Col &&
                anchorCol <= range.End.Col)
            {
                return true;
            }
        }

        return false;
    }

    private static FormControlKind? MapHssfFormControlKind(int shapeType) =>
        shapeType switch
        {
            HSSFSimpleShape.OBJECT_TYPE_COMBO_BOX => FormControlKind.DropDown,
            _ => null
        };

    private static bool TryCreateDrawingShape(HSSFSimpleShape sourceShape, Sheet sheet, out DrawingShapeModel shape)
    {
        shape = new DrawingShapeModel();
        if (MapHssfShapeKind(sourceShape.ShapeType) is not { } kind ||
            sourceShape.Anchor is not HSSFClientAnchor anchor ||
            anchor.Row1 < 0 ||
            anchor.Col1 < 0)
        {
            return false;
        }

        var anchorRow = ToModelIndex(Math.Min(anchor.Row1, anchor.Row2));
        var anchorCol = ToModelIndex(Math.Min(anchor.Col1, anchor.Col2));
        shape = new DrawingShapeModel
        {
            Anchor = new ModelCellAddress(sheet.Id, anchorRow, anchorCol),
            Kind = kind,
            Name = FirstNonBlank(sourceShape.Name, sourceShape.ShapeName),
            AnchorOffsetX = HssfColumnOffsetToPixels(sheet, anchorCol, Math.Min(anchor.Dx1, anchor.Dx2)),
            AnchorOffsetY = HssfRowOffsetToPixels(sheet, anchorRow, Math.Min(anchor.Dy1, anchor.Dy2)),
            RotationDegrees = sourceShape.RotationDegree,
            FlipHorizontal = anchor.IsHorizontallyFlipped || sourceShape.FlipHorizontal || sourceShape.IsFlipHorizontal,
            FlipVertical = anchor.IsVerticallyFlipped || sourceShape.FlipVertical || sourceShape.IsFlipVertical,
            HasFill = kind is not DrawingShapeKind.Line && !sourceShape.IsNoFill,
            IsSourceLoaded = true
        };

        if (TryGetHssfRgbColor(sourceShape.FillColor, out var fillColor))
            shape.FillColor = fillColor;
        if (TryGetHssfRgbColor(sourceShape.LineStyleColor, out var outlineColor))
            shape.OutlineColor = outlineColor;

        var (width, height) = GetHssfAnchorSize(sheet, anchor);
        if (width > 0)
            shape.Width = width;
        if (height > 0)
            shape.Height = height;

        return true;
    }

    private static DrawingShapeKind? MapHssfShapeKind(int shapeType) =>
        shapeType switch
        {
            HSSFSimpleShape.OBJECT_TYPE_RECTANGLE => DrawingShapeKind.Rectangle,
            HSSFSimpleShape.OBJECT_TYPE_OVAL => DrawingShapeKind.Ellipse,
            HSSFSimpleShape.OBJECT_TYPE_LINE => DrawingShapeKind.Line,
            _ => null
        };

    private static bool TryGetHssfRgbColor(int value, out CellColor color)
    {
        color = default;
        if (value < 0 || value > 0xFFFFFF)
            return false;

        color = new CellColor(
            (byte)(value & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)((value >> 16) & 0xFF));
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
        var uses1904DateSystem = workbook.Uses1904DateSystem;
        for (var rowIndex = sourceSheet.FirstRowNum; rowIndex <= sourceSheet.LastRowNum; rowIndex++)
        {
            var sourceRow = sourceSheet.GetRow(rowIndex);
            if (sourceRow is null)
                continue;

            foreach (var sourceCell in sourceRow.Cells)
            {
                var address = new ModelCellAddress(sheet.Id, ToModelIndex(sourceCell.RowIndex), ToModelIndex(sourceCell.ColumnIndex));
                var cell = MapCell(sourceCell, uses1904DateSystem);
                var styleId = GetStyleId(sourceWorkbook, workbook, sourceCell.CellStyle, styleCache);
                LoadCellAnnotations(sourceCell, address, sheet);

                if (cell.Value is BlankValue && !cell.HasFormula)
                {
                    if (styleId != StyleId.Default)
                        sheet.SetStyleOnly(address.Row, address.Col, styleId);
                    continue;
                }

                cell.StyleId = styleId;

                // Legacy CSE array formula (Ctrl+Shift+Enter): NPOI reports every cell physically
                // covered by the declared array range via IsPartOfArrayFormulaGroup, and the anchor
                // (top-left) cell of the group via ArrayFormulaRange. Mirror the XLSX loader's model:
                // only the anchor becomes an independent (Dynamic/array) formula cell; every other
                // covered cell is registered as a provisional spill/array member so Sheet.TryGetArrayExtent
                // recognises the whole declared range (CommandGuards.RejectIfSplitsArray then blocks
                // splitting it, matching Excel's "You cannot change part of an array" rule) and so the
                // range round-trips as a real array formula if the workbook is later saved as XLSX.
                if (cell.HasFormula && TryGetArrayFormulaRange(sourceCell, out var arrayRange))
                {
                    var anchorRow = ToModelIndex(arrayRange.FirstRow);
                    var anchorCol = ToModelIndex(arrayRange.FirstColumn);
                    var isAnchor = address.Row == anchorRow && address.Col == anchorCol;

                    if (isAnchor)
                    {
                        // A single-cell "array formula" (FirstRow==LastRow && FirstColumn==LastColumn)
                        // has no spill/member semantics beyond the anchor itself — Dynamic with a 1x1
                        // extent behaves identically to Implicit, so only mark it Dynamic when the
                        // declared range actually covers more than one cell.
                        var isMultiCell = arrayRange.LastRow > arrayRange.FirstRow || arrayRange.LastColumn > arrayRange.FirstColumn;
                        cell.ArrayMode = isMultiCell ? FormulaArrayMode.Dynamic : FormulaArrayMode.Implicit;
                        if (isMultiCell)
                        {
                            // Mirror XlsxFileAdapter's legacy-CSE handling (see Cell.LegacyArrayRows /
                            // RecalcEngine): confine this formula's result to the originally declared
                            // ref extent on every recalc instead of letting it free-spill like a modern
                            // dynamic-array formula. Without this, RecalcEngine's LegacyArrayRows > 0
                            // gate never fires for .xls-sourced CSE arrays and they fall through to the
                            // free-spilling / IsSpillBlocked path instead.
                            cell.LegacyArrayRows = (uint)(arrayRange.LastRow - arrayRange.FirstRow + 1);
                            cell.LegacyArrayCols = (uint)(arrayRange.LastColumn - arrayRange.FirstColumn + 1);
                        }
                        sheet.SetCell(address, cell);
                    }
                    else
                    {
                        // Non-anchor member: NPOI (BIFF8) physically stores a copy of the array
                        // formula's tokens on every covered cell, but the model must mirror the XLSX
                        // loader here — only the anchor is an independent formula cell. Registering
                        // this cell WITH its formula (cell.HasFormula == true) would add it to
                        // Sheet._formulaCells and make RecalcEngine evaluate it as its own standalone
                        // formula, fighting the anchor's array/spill write. Strip the formula and keep
                        // only the cached result as a plain provisional value, exactly like a
                        // non-anchor XLSX spill-continuation cell.
                        var anchorAddr = new ModelCellAddress(sheet.Id, anchorRow, anchorCol);
                        var memberCell = Cell.FromValue(cell.Value);
                        memberCell.StyleId = cell.StyleId;
                        sheet.SetProvisionalSpillCell(anchorAddr, address.Row, address.Col, memberCell);
                    }
                    continue;
                }

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

            var scopeSheetId = GetDefinedNameScopeSheetId(sourceWorkbook, workbook, definedName);

            if (TryParseNamedRangeRefersTo(workbook, refersTo, out var range))
            {
                var metadata = new NamedRangeMetadata(GetDefinedNameScope(sourceWorkbook, definedName), definedName.Comment ?? "");
                if (scopeSheetId is { } rangeSheetId)
                    workbook.DefineNamedRange(definedName.NameName, range, metadata, rangeSheetId);
                else
                    workbook.DefineNamedRange(definedName.NameName, range, metadata);
                continue;
            }

            if (scopeSheetId is { } formulaSheetId)
                workbook.DefineNamedFormula(definedName.NameName, refersTo.Trim(), formulaSheetId);
            else
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
            Sheet? printSheet = null;
            var printAreas = new List<GridRange>();
            foreach (var reference in SplitFormulaReferences(refersTo))
            {
                if (TryParseNamedRangeRefersTo(workbook, reference, out var printArea) &&
                    workbook.GetSheet(printArea.Start.Sheet) is { } sheet)
                {
                    printSheet ??= sheet;
                    printAreas.Add(printArea);
                }
            }

            if (printSheet is not null && printAreas.Count > 0)
                printSheet.SetPrintAreas(printAreas);

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

    /// <summary>
    /// Resolves the BIFF NAME record's itab (sheet index) to the corresponding loaded sheet's
    /// <see cref="SheetId"/> so sheet-scoped defined names are registered in
    /// <see cref="Workbook.ScopedNamedRanges"/> instead of collapsing into workbook-global scope.
    /// Returns null for workbook-global names (itab == 0 / unset).
    /// </summary>
    private static SheetId? GetDefinedNameScopeSheetId(NPOIWorkbook sourceWorkbook, Workbook workbook, IName definedName)
    {
        var sheetIndex = definedName.SheetIndex;
        if (sheetIndex < 0 || sheetIndex >= sourceWorkbook.NumberOfSheets || sheetIndex >= workbook.Sheets.Count)
            return null;

        return workbook.Sheets[sheetIndex].Id;
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

    /// <summary>
    /// Detects whether <paramref name="sourceCell"/> is part of a legacy CSE (Ctrl+Shift+Enter) array
    /// formula group and, if so, returns the declared array range (anchor + bounding box), matching
    /// NPOI's <see cref="ICell.ArrayFormulaRange"/> semantics (0-based, covers every physical cell of
    /// the group including the anchor).
    /// </summary>
    private static bool TryGetArrayFormulaRange(NPOICell sourceCell, out CellRangeAddress range)
    {
        if (sourceCell.IsPartOfArrayFormulaGroup)
        {
            try
            {
                range = sourceCell.ArrayFormulaRange;
                return range is not null;
            }
            catch (InvalidOperationException)
            {
                // Defensive: some NPOI cell implementations throw if queried on a cell whose sheet
                // is not (yet) fully loaded, or for edge cases outside the documented contract.
                // Fall back to treating the cell as a plain (non-array) formula rather than crashing
                // the whole load.
            }
        }

        range = null!;
        return false;
    }

    private static Cell MapCell(NPOICell sourceCell, bool uses1904DateSystem)
    {
        if (sourceCell.CellType == CellType.Formula)
        {
            var formulaText = NormalizeFormula(sourceCell.CellFormula);
            var cell = Cell.FromFormula(formulaText);
            cell.ArrayMode = FormulaArrayMode.Implicit;
            cell.Value = MapCachedFormulaValue(sourceCell, uses1904DateSystem);
            return cell;
        }

        return Cell.FromValue(MapNpoiValue(sourceCell, sourceCell.CellType, uses1904DateSystem));
    }

    private static ScalarValue MapCachedFormulaValue(NPOICell sourceCell, bool uses1904DateSystem) =>
        MapNpoiValue(sourceCell, sourceCell.CachedFormulaResultType, uses1904DateSystem);

    private static ScalarValue MapNpoiValue(NPOICell sourceCell, CellType cellType, bool uses1904DateSystem) =>
        cellType switch
        {
            CellType.Numeric when DateUtil.IsCellDateFormatted(sourceCell) && sourceCell.DateCellValue is { } date => MapDateTimeValue(date, uses1904DateSystem),
            CellType.Numeric => new NumberValue(sourceCell.NumericCellValue),
            CellType.Boolean => new BoolValue(sourceCell.BooleanCellValue),
            CellType.String => string.IsNullOrEmpty(sourceCell.StringCellValue)
                ? BlankValue.Instance
                : new TextValue(sourceCell.StringCellValue),
            CellType.Error => MapErrorValue(sourceCell.ErrorCellValue),
            _ => BlankValue.Instance
        };

    // Converts the true calendar DateTime NPOI surfaces (it has already resolved the workbook's 1904
    // windowing) into the internal ScalarValue serial. The internal convention must match how the
    // 1904-aware date functions interpret a stored serial: Excel 1900 serial when the workbook is not
    // 1904, 1904-epoch-relative (day-count since 1904-01-01) when it is. This is the read-side mirror of
    // XlsxClosedXmlCellMapper.MapDateTimeValue for the legacy .xls (NPOI) path — including its use of
    // DateTimeValue.FromDateTime for the 1900 branch: NPOI's DateUtil.GetJavaDate(15) returns the true
    // 1900-01-15, whose OADate (16) is one day past the Excel serial that cell actually holds.
    private static DateTimeValue MapDateTimeValue(DateTime date, bool uses1904DateSystem) =>
        uses1904DateSystem
            ? new DateTimeValue(date.ToOADate() - Date1904EpochOADate)
            : DateTimeValue.FromDateTime(date);

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

        // Index 64 is the BIFF/HSSF automatic-color sentinel (HSSFColor.Automatic.Index);
        // index 0 is a real palette entry (black) and must not be excluded.
        if (sourceStyle.FillPattern != FillPattern.NoFill && sourceStyle.FillForegroundColor != 64)
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

    private static string? ReadHssfWorkbookCodeName(HSSFWorkbook sourceWorkbook)
    {
        if (sourceWorkbook.Workbook.FindFirstRecordBySid(UnknownRecord.CODENAME_1BA) is not UnknownRecord codeNameRecord ||
            UnknownRecordRawDataField?.GetValue(codeNameRecord) is not byte[] rawData)
        {
            return null;
        }

        return DecodeBiffCodeName(rawData);
    }

    private static string? ReadHssfSheetCodeName(ISheet sourceSheet)
    {
        if (sourceSheet is not HSSFSheet hssfSheet ||
            hssfSheet.Sheet.FindFirstRecordBySid(UnknownRecord.CODENAME_1BA) is not UnknownRecord codeNameRecord ||
            UnknownRecordRawDataField?.GetValue(codeNameRecord) is not byte[] rawData)
        {
            return null;
        }

        return DecodeBiffCodeName(rawData);
    }

    private static string? DecodeBiffCodeName(byte[] rawData)
    {
        if (rawData.Length < 3)
            return null;

        var characterCount = rawData[0] | (rawData[1] << 8);
        var optionFlags = rawData[2];
        var isWide = (optionFlags & 0x01) != 0;
        var byteCount = isWide ? characterCount * 2 : characterCount;
        if (byteCount <= 0 || rawData.Length < 3 + byteCount)
            return null;

        var codeName = isWide
            ? Encoding.Unicode.GetString(rawData, 3, byteCount)
            : Encoding.Latin1.GetString(rawData, 3, byteCount);
        return NullIfWhiteSpace(codeName);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static ScalarValue MapExcelDataReaderCellValue(IExcelDataReader reader, int column) =>
        reader.GetCellError(column) is { } error
            ? MapExcelDataReaderErrorValue(error)
            : MapValue(reader.GetValue(column));

    private static ErrorValue MapExcelDataReaderErrorValue(ExcelDataReader.CellError error) =>
        error switch
        {
            ExcelDataReader.CellError.NULL => ErrorValue.Null,
            ExcelDataReader.CellError.DIV0 => ErrorValue.DivByZero,
            ExcelDataReader.CellError.VALUE => ErrorValue.Value,
            ExcelDataReader.CellError.REF => ErrorValue.Ref,
            ExcelDataReader.CellError.NAME => ErrorValue.Name,
            ExcelDataReader.CellError.NUM => ErrorValue.Num,
            ExcelDataReader.CellError.NA => ErrorValue.NA,
            ExcelDataReader.CellError.GETTING_DATA => new ErrorValue("#GETTING_DATA"),
            _ => new ErrorValue(error.ToString())
        };

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

    // ExcelDataReader fallback path (used only when NPOI cannot parse the workbook). It surfaces true
    // calendar DateTimes and this path never sets Workbook.Uses1904DateSystem, so a 1900-epoch OADate
    // serial is self-consistent here (no 1904 conversion needed — unlike the NPOI path above, which
    // does propagate the workbook's 1904 flag via MapDateTimeValue).
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
