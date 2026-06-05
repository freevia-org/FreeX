using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static ExcelSmokeCom;
using static ExcelSmokeFixtures;
using static SmokeUsage;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        return ExcelOpenSmoke.Run(args);
    }
}

internal static class ExcelOpenSmoke
{
    private const uint ExcelOpenRejectedHResult = 0x800A03ECu;
    private const int ExcelCellTypeFormulas = -4123;
    private const int ExcelCellTypeAllValidation = -4174;
    private const int MsoShapeTypeAutoShape = 1;
    private const int MsoShapeTypeFreeform = 5;
    private const int MsoShapeTypeGroup = 6;
    private const int MsoShapeTypeLine = 9;
    private const int MsoShapeTypeLinkedPicture = 11;
    private const int MsoShapeTypePicture = 13;
    private const int MsoShapeTypeTextBox = 17;
    private const int MsoShapeTypeGraphic = 28;
    private const int MsoShapeTypeLinkedGraphic = 29;
    private const int XlLandscape = 2;
    private const int XlPageBreakManual = -4135;
    private const int XlColorIndexNone = -4142;
    private const int XlLineStyleNone = -4142;
    private const int XlBorderIndexLeft = 7;
    private const int XlBorderIndexTop = 8;
    private const int XlBorderIndexBottom = 9;
    private const int XlBorderIndexRight = 10;
    private const int XlHAlignGeneral = 1;
    private const int XlVAlignBottom = -4107;
    private const int MaxDataValidationProbeCells = 20000;
    private const int MaxMergedAreaProbeCells = 20000;
    private const int MaxFormattingProbeCells = 20000;
    private const int MaxStructureProbeRows = 200;
    private const int MaxStructureProbeColumns = 80;
    private const int MaxOpenXmlValidationErrorsToReport = 20;
    private const int MaxPackageEntryIssuesToReport = 20;
    private const int MaxPackageContentTypeIssuesToReport = 20;
    private const int MaxPackageRelationshipIssuesToReport = 20;
    private const double ExcelMeasurementTolerance = 0.01;
    private const string ChartRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
    private const string ChartSheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml";
    private const string ChartSheetRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chartsheet";
    private const string CalcChainContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml";
    private const string CalcChainRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain";
    private const string DialogSheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.dialogsheet+xml";
    private const string DialogSheetRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/dialogsheet";
    private const string DrawingContentType =
        "application/vnd.openxmlformats-officedocument.drawing+xml";
    private const string DrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing";
    private const string DrawingMlChartContentType =
        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
    private const string CommentsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.comments+xml";
    private const string CommentsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string CustomXmlContentType =
        "application/xml";
    private const string CustomXmlPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.customXmlProperties+xml";
    private const string CustomXmlRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml";
    private const string CustomXmlPropertiesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps";
    private const string ExternalLinkContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";
    private const string ExternalLinkRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink";
    private const string ExternalLinkPathRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath";
    private const string HyperlinkRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private const string ImageRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string MacroSheetContentType =
        "application/vnd.ms-excel.macrosheet+xml";
    private const string MacroSheetRelationshipType =
        "http://schemas.microsoft.com/office/2006/relationships/xlMacrosheet";
    private const string OfficeDocumentRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    private const string PackageRootRelationshipPart = "_rels/.rels";
    private const string PrinterSettingsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings";
    private const string PrinterSettingsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings";
    private const string RelationshipPartContentType =
        "application/vnd.openxmlformats-package.relationships+xml";
    private const string WorkbookContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";
    private const string WorkbookPart = "xl/workbook.xml";
    private const string SharedStringsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
    private const string SharedStringsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";
    private const string SlicerContentType =
        "application/vnd.ms-excel.slicer+xml";
    private const string SlicerCacheContentType =
        "application/vnd.ms-excel.slicerCache+xml";
    private const string SlicerRelationshipType =
        "http://schemas.microsoft.com/office/2007/relationships/slicer";
    private const string SlicerCacheRelationshipType =
        "http://schemas.microsoft.com/office/2007/relationships/slicerCache";
    private const string StylesContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml";
    private const string StylesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    private const string TableContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml";
    private const string TableRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table";
    private const string TimelineContentType =
        "application/vnd.ms-excel.timeline+xml";
    private const string TimelineCacheContentType =
        "application/vnd.ms-excel.timelineCache+xml";
    private const string TimelineRelationshipType =
        "http://schemas.microsoft.com/office/2010/relationships/Timeline";
    private const string TimelineCacheRelationshipType =
        "http://schemas.microsoft.com/office/2010/relationships/TimelineCache";
    private const string PivotCacheDefinitionContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml";
    private const string PivotCacheDefinitionRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition";
    private const string PivotCacheRecordsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml";
    private const string PivotCacheRecordsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords";
    private const string PivotTableContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml";
    private const string PivotTableRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable";
    private const string VmlDrawingContentType =
        "application/vnd.openxmlformats-officedocument.vmlDrawing";
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";
    private const string WorkbookRelationshipPart = "xl/_rels/workbook.xml.rels";
    private const string WorksheetContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml";
    private const string WorksheetRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    private static readonly XNamespace PackageContentTypeNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationshipNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeRelationshipNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace CustomXmlNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
    private static readonly XNamespace SpreadsheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace SlicerNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace TimelineNs =
        "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";
    private static readonly XNamespace DrawingNs =
        "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace DrawingChartNs =
        "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace SpreadsheetDrawingNs =
        "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace VmlNs =
        "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace VmlOfficeNs =
        "urn:schemas-microsoft-com:office:office";

    public static int Run(string[] args)
    {
        try
        {
            var options = SmokeOptions.Parse(args);
            if (options.ShowHelp)
            {
                WriteUsage();
                return 0;
            }

            if (!options.HasRequestedInputs)
            {
                Console.Error.WriteLine("No XLSX inputs or generated fixtures were requested.");
                WriteUsage();
                return 2;
            }

            var userProfile = GetUserProfile();
            var runDirectory = options.OutputDirectory ?? CreateDefaultRunDirectory(userProfile);
            EnsureUnderUserProfile(runDirectory, userProfile);
            Directory.CreateDirectory(runDirectory);

            var smokeInputs = new List<WorkbookSmokeInput>();
            CorpusManifestSelection? corpusSelection = null;
            var generatedWorkflow = options.FreeXResaveBeforeExcel
                ? WorkbookValidationWorkflow.FreeXSaveThenExcel
                : WorkbookValidationWorkflow.DirectExcel;
            if (options.GenerateChartFixtures)
            {
                foreach (var generatedFile in GenerateChartFixtures(Path.Combine(runDirectory, "generated")))
                {
                    AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                        generatedFile,
                        generatedWorkflow,
                        DescribeGeneratedFixture("FreeX chart fixture", generatedWorkflow),
                        Expectations: ChartExpectations(options.SaveReopen, generatedWorkflow == WorkbookValidationWorkflow.FreeXSaveThenExcel)));
                }
            }

            if (options.GenerateFreexFixture)
            {
                var generatedFile = GenerateFreeXNonChartFixture(Path.Combine(runDirectory, "generated"));
                AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                    generatedFile,
                    generatedWorkflow,
                    DescribeGeneratedFixture("FreeX non-chart fixture", generatedWorkflow)));
            }

            if (options.GenerateFreexFeatureFixtures)
            {
                foreach (var generatedFile in GenerateFreeXFeatureFixtures(Path.Combine(runDirectory, "generated")))
                {
                    AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                        generatedFile,
                        generatedWorkflow,
                        DescribeGeneratedFixture("FreeX feature fixture", generatedWorkflow),
                        Expectations: ExpectationsForGeneratedFixture(generatedFile, options.SaveReopen, generatedWorkflow)));
                }
            }

            if (options.GenerateSupportedCorpusFixtures)
            {
                corpusSelection = CorpusManifestResolver.GenerateSupportedFixtures(
                    options,
                    generatedWorkflow,
                    Path.Combine(runDirectory, "generated-corpus"));
                foreach (var input in corpusSelection.Inputs)
                    AddUniqueInput(smokeInputs, WithCorpusExpectations(input, options.SaveReopen));
            }

            var inputWorkflow = options.FreeXResaveBeforeExcel
                ? WorkbookValidationWorkflow.FreeXSaveThenExcel
                : WorkbookValidationWorkflow.DirectExcel;
            if (!options.GenerateSupportedCorpusFixtures && options.CorpusManifestPath is not null)
            {
                corpusSelection = CorpusManifestResolver.Resolve(options, inputWorkflow);
                foreach (var input in corpusSelection.Inputs)
                    AddUniqueInput(smokeInputs, WithCorpusExpectations(input, options.SaveReopen));
            }

            var inputFiles = ResolveInputFiles(options.Inputs, options.Pattern);
            foreach (var inputFile in inputFiles)
            {
                AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                    inputFile,
                    inputWorkflow,
                    options.FreeXResaveBeforeExcel ? "User input via FreeX save" : "User input"));
            }

            if (options.GenerateExcelFixture)
            {
                AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                    Path.Combine(runDirectory, "generated", "Excel_authored_smoke.xlsx"),
                    WorkbookValidationWorkflow.FreeXSaveThenExcel,
                    "Excel-authored fixture",
                    GenerateWithExcel: true,
                    Expectations: ExcelAuthoredFixtureExpectations(options.SaveReopen)));
            }

            if (smokeInputs.Count == 0)
            {
                Console.Error.WriteLine("No XLSX files matched the requested inputs.");
                return 2;
            }

            Console.WriteLine(options.SaveReopen ? "Excel save/reopen smoke" : "Excel open smoke");
            Console.WriteLine($"Run directory: {runDirectory}");
            Console.WriteLine($"Input count: {smokeInputs.Count}");
            Console.WriteLine($"Validation mode: {(options.SaveReopen ? "open -> SaveCopyAs -> close -> reopen" : "open only")}");
            if (corpusSelection is not null)
            {
                Console.WriteLine($"Corpus manifest: {corpusSelection.ManifestPath}");
                Console.WriteLine($"Corpus selected: {corpusSelection.Inputs.Count}; skipped: {corpusSelection.Skipped.Count}");
            }

            var result = RunExcelSmoke(smokeInputs, runDirectory, options.SaveReopen);
            WriteMachineReadableReport(runDirectory, options, result, corpusSelection);
            Console.WriteLine(result.Failed == 0
                ? $"PASS: Excel validated {result.Passed}/{result.Total} workbook(s)."
                : $"FAIL: Excel validated {result.Passed}/{result.Total} workbook(s); {result.Failed} failed.");

            return result.Failed == 0 ? 0 : 1;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    private static ExcelSmokeSummary RunExcelSmoke(
        IReadOnlyList<WorkbookSmokeInput> inputFiles,
        string runDirectory,
        bool saveReopen)
    {
        var stagingDirectory = Path.Combine(runDirectory, "staged");
        var freeXSavedDirectory = Path.Combine(runDirectory, "freex-saved");
        var excelSavedDirectory = Path.Combine(runDirectory, "excel-saved");
        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(freeXSavedDirectory);
        Directory.CreateDirectory(excelSavedDirectory);

        var baselineExcelPids = GetExcelProcessIds();
        object? excel = null;
        object? workbooks = null;
        int? excelPid = null;
        var results = new List<WorkbookSmokeResult>(inputFiles.Count);

        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application")
                ?? throw new InvalidOperationException("Excel.Application COM registration was not found. Install Microsoft Excel desktop before running this smoke check.");

            using var messageFilter = RegisterExcelBusyMessageFilter();
            excel = Activator.CreateInstance(excelType)
                ?? throw new InvalidOperationException("Excel.Application COM activation returned null.");

            excelPid = TryGetExcelProcessId(excel);
            dynamic excelApp = excel;
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;
            TrySetAutomationSecurity(excelApp);
            workbooks = excelApp.Workbooks;

            foreach (var inputFile in inputFiles)
            {
                var result = ValidateWorkbook(
                    (dynamic)workbooks,
                    inputFile,
                    stagingDirectory,
                    freeXSavedDirectory,
                    excelSavedDirectory,
                    saveReopen);
                results.Add(result);
                WriteWorkbookReport(result, saveReopen);
            }
        }
        finally
        {
            try
            {
                if (excel is not null)
                    ((dynamic)excel).Quit();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Excel.Quit failed during cleanup: {ex.Message}");
            }

            ReleaseComObject(workbooks);
            ReleaseComObject(excel);
            CollectComReferences();
            KillOrphanExcelProcesses(baselineExcelPids, excelPid);
        }

        return new ExcelSmokeSummary(
            results.Count,
            results.Count(result => result.Success),
            results.Count(result => !result.Success),
            results);
    }

    private static WorkbookSmokeResult ValidateWorkbook(
        dynamic workbooks,
        WorkbookSmokeInput input,
        string stagingDirectory,
        string freeXSavedDirectory,
        string excelSavedDirectory,
        bool saveReopen)
    {
        string sourceForExcel = input.SourcePath;
        string? freeXSavedPath = null;
        FreeXWorkbookSummary? freeXPreSave = null;
        IReadOnlyList<string> freeXPreSaveWarnings = [];

        try
        {
            if (input.GenerateWithExcel)
                GenerateExcelAuthoredFixture(workbooks, input.SourcePath);

            if (input.Workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel)
            {
                var freeXSave = SaveThroughFreeX(input.SourcePath, freeXSavedDirectory);
                AssertFreeXLoadWarnings(input, "FreeX source load", freeXSave.LoadWarnings);
                AssertPackageEntriesCanonical(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertNoExcelRecoveryLog(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertOpenXmlValid(freeXSave.SavedPath, "FreeX-saved workbook");
                AssertPackageContentTypesComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertPackageRelationshipsComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookPackageRoot(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookSheetRelationshipsComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertSharedStringTableComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertStylesPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetHyperlinkPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetDrawingPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetBackgroundImagePackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetPrinterSettingsPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertLegacyCommentPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetTablePackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertPivotPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertExternalLinkPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertCalcChainPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertCustomXmlPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertSlicerTimelinePackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertRequiredFreeXSavedPackageParts(freeXSave.SavedPath, input.Expectations, input.SourcePath);
                AssertRequiredFreeXSavedPackageRelationships(freeXSave.SavedPath, input.Expectations, input.SourcePath);
                AssertRequiredFreeXSavedPackageContentTypes(freeXSave.SavedPath, input.Expectations, input.SourcePath);
                AssertPublicPackageTagExpectations(
                    freeXSave.SavedPath,
                    input.CorpusRow,
                    "FreeX-saved workbook",
                    input.SourcePath,
                    allowExcelNormalization: false);
                sourceForExcel = freeXSave.SavedPath;
                freeXSavedPath = freeXSave.SavedPath;
                freeXPreSave = freeXSave.Summary;
                freeXPreSaveWarnings = freeXSave.LoadWarnings;
            }

            var stagedPath = CopyToStagingDirectory(sourceForExcel, stagingDirectory);
            if (!saveReopen)
            {
                var opened = OpenWorkbook(workbooks, stagedPath, true, input.Expectations);
                AssertSmokeExpectations(input, freeXPreSave, opened, null, null);
                return WorkbookSmokeResult.Pass(
                    input,
                    stagedPath,
                    freeXSavedPath,
                    null,
                    opened,
                    null,
                    freeXPreSave,
                    freeXPreSaveWarnings,
                    null,
                    Array.Empty<string>());
            }

            var excelSavedPath = CreateDerivedOutputPath(excelSavedDirectory, stagedPath, "excel-saved");
            var saveReopenResult = OpenSaveCloseReopenWorkbook(
                workbooks,
                stagedPath,
                excelSavedPath,
                input.Expectations,
                input.CorpusRow);
            var freeXReopenedExcelSave = LoadWorkbookSummary(saveReopenResult.ExcelSavedPath);
            AssertFreeXLoadWarnings(input, "FreeX reopened Excel save", freeXReopenedExcelSave.Warnings);
            AssertSmokeExpectations(input, freeXPreSave, saveReopenResult.Opened, saveReopenResult.Reopened, freeXReopenedExcelSave.Summary);

            return WorkbookSmokeResult.Pass(
                input,
                stagedPath,
                freeXSavedPath,
                saveReopenResult.ExcelSavedPath,
                saveReopenResult.Opened,
                saveReopenResult.Reopened,
                freeXPreSave,
                freeXPreSaveWarnings,
                freeXReopenedExcelSave.Summary,
                freeXReopenedExcelSave.Warnings);
        }
        catch (Exception ex)
        {
            return WorkbookSmokeResult.Fail(
                input,
                freeXSavedPath,
                FormatFailure(ex));
        }
    }

    private static ExcelWorkbookSummary OpenWorkbook(
        dynamic workbooks,
        string stagedPath,
        bool readOnly,
        WorkbookSmokeExpectations? expectations)
    {
        object? workbook = null;
        var closed = false;
        try
        {
            workbook = OpenExcelWorkbook(workbooks, stagedPath, readOnly);
            ExcelWorkbookSummary contents;
            try
            {
                contents = WithExcelBusyRetry(
                    () => CountWorkbookContents(workbook, expectations),
                    "Excel content count");
            }
            catch (COMException ex)
            {
                throw new InvalidDataException($"Excel content count failed for '{stagedPath}'", ex);
            }

            WithExcelBusyRetry(
                () =>
                {
                    ((dynamic)workbook).Close(false);
                    return true;
                },
                "Excel workbook close");
            closed = true;
            return contents;
        }
        catch (COMException ex)
        {
            throw new InvalidDataException($"Excel open failed for '{stagedPath}'", ex);
        }
        finally
        {
            try
            {
                if (workbook is not null && !closed)
                {
                    WithExcelBusyRetry(
                        () =>
                        {
                            ((dynamic)workbook).Close(false);
                            return true;
                        },
                        "Excel workbook cleanup close");
                }
            }
            catch
            {
                // The workbook may already be closed, or Excel may have rejected it before creating one.
            }

            ReleaseComObject(workbook);
        }
    }

    private static ExcelSaveReopenResult OpenSaveCloseReopenWorkbook(
        dynamic workbooks,
        string stagedPath,
        string excelSavedPath,
        WorkbookSmokeExpectations? expectations,
        CorpusManifestRow? corpusRow)
    {
        object? workbook = null;
        object? reopenedWorkbook = null;
        var workbookClosed = false;
        var reopenedClosed = false;

        try
        {
            workbook = OpenExcelWorkbook(workbooks, stagedPath, readOnly: false);
            ExcelWorkbookSummary opened;
            try
            {
                opened = WithExcelBusyRetry(
                    () => CountWorkbookContents(workbook, expectations),
                    "Excel content count after open");
            }
            catch (COMException ex)
            {
                throw new InvalidDataException($"Excel content count failed after opening '{stagedPath}'", ex);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(excelSavedPath)!);
            if (File.Exists(excelSavedPath))
                File.Delete(excelSavedPath);

            try
            {
                WithExcelBusyRetry(
                    () =>
                    {
                        ((dynamic)workbook).SaveCopyAs(excelSavedPath);
                        return true;
                    },
                    "Excel SaveCopyAs");
            }
            catch (COMException ex)
            {
                throw new InvalidDataException($"Excel SaveCopyAs failed for '{stagedPath}'", ex);
            }

            AssertNoExcelRecoveryLog(excelSavedPath, "Excel-saved workbook", stagedPath);
            WithExcelBusyRetry(
                () =>
                {
                    ((dynamic)workbook).Close(false);
                    return true;
                },
                "Excel workbook close after SaveCopyAs");
            workbookClosed = true;
            ReleaseComObject(workbook);
            workbook = null;
            CollectComReferences();
            AssertPackageEntriesCanonical(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertOpenXmlValid(excelSavedPath, "Excel-saved workbook");
            AssertPackageContentTypesComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertPackageRelationshipsComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookPackageRoot(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookSheetRelationshipsComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertSharedStringTableComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertStylesPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetHyperlinkPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetDrawingPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetBackgroundImagePackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetPrinterSettingsPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertLegacyCommentPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetTablePackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertPivotPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertExternalLinkPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertCalcChainPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertCustomXmlPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertSlicerTimelinePackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertRequiredExcelSavedPackageParts(excelSavedPath, expectations, stagedPath);
            AssertRequiredExcelSavedPackageRelationships(excelSavedPath, expectations, stagedPath);
            AssertRequiredExcelSavedPackageContentTypes(excelSavedPath, expectations, stagedPath);
            AssertPublicPackageTagExpectations(
                excelSavedPath,
                corpusRow,
                "Excel-saved workbook",
                stagedPath,
                allowExcelNormalization: true);

            reopenedWorkbook = OpenExcelWorkbook(workbooks, excelSavedPath, readOnly: true);
            ExcelWorkbookSummary reopened;
            try
            {
                reopened = WithExcelBusyRetry(
                    () => CountWorkbookContents(reopenedWorkbook, expectations),
                    "Excel content count after reopen");
            }
            catch (COMException ex)
            {
                throw new InvalidDataException($"Excel content count failed after reopening '{excelSavedPath}'", ex);
            }

            WithExcelBusyRetry(
                () =>
                {
                    ((dynamic)reopenedWorkbook).Close(false);
                    return true;
                },
                "Excel reopened workbook close");
            reopenedClosed = true;

            return new ExcelSaveReopenResult(excelSavedPath, opened, reopened);
        }
        catch (COMException ex)
        {
            throw new InvalidDataException($"Excel open failed for '{stagedPath}'", ex);
        }
        finally
        {
            try
            {
                if (workbook is not null && !workbookClosed)
                {
                    WithExcelBusyRetry(
                        () =>
                        {
                            ((dynamic)workbook).Close(false);
                            return true;
                        },
                        "Excel workbook cleanup close");
                }
            }
            catch
            {
                // Best-effort cleanup; orphaned Excel processes are handled separately.
            }

            try
            {
                if (reopenedWorkbook is not null && !reopenedClosed)
                {
                    WithExcelBusyRetry(
                        () =>
                        {
                            ((dynamic)reopenedWorkbook).Close(false);
                            return true;
                        },
                        "Excel reopened workbook cleanup close");
                }
            }
            catch
            {
                // Best-effort cleanup; orphaned Excel processes are handled separately.
            }

            ReleaseComObject(reopenedWorkbook);
            ReleaseComObject(workbook);
        }
    }

    private static object OpenExcelWorkbook(dynamic workbooks, string path, bool readOnly)
    {
        var workbook = WithExcelBusyRetry<object>(
            () => workbooks.Open(
                path,
                0,
                readOnly),
            "Excel workbook open");
        WaitForExcelReady(((dynamic)workbook).Application);
        return workbook;
    }

    private static void AssertNoExcelRecoveryLog(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var recoveryLogs = archive.Entries
            .Where(entry =>
                entry.FullName.Contains("recovery", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.FullName)
            .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (recoveryLogs.Length > 0)
        {
            throw new InvalidDataException(
                $"{label} for '{sourcePath}' contains repair/recovery log parts: {string.Join(", ", recoveryLogs)}");
        }
    }

    private static void AssertPublicPackageTagExpectations(
        string xlsxPath,
        CorpusManifestRow? row,
        string label,
        string sourcePath,
        bool allowExcelNormalization)
    {
        if (row is null ||
            !string.Equals(row.SourceType, "public", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tags = row.FeatureTags
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!HasExpectedPublicPackageTags(tags))
            return;

        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var worksheetXmlDocuments = LoadPublicWorkbookWorksheetXmlDocuments(archive, tags, issues);

        if ((tags.Contains("styles") || tags.Contains("formatting")) &&
            !PackageEntryExists(archive, "xl/styles.xml"))
        {
            issues.Add("missing xl/styles.xml for public styles/formatting tag");
        }

        if ((tags.Contains("styles") || tags.Contains("formatting")) &&
            !PackageRelationshipExists(
                archive,
                new PackageRelationshipExpectation(
                    WorkbookRelationshipPart,
                    StylesRelationshipType,
                    "xl/styles.xml")))
        {
            issues.Add("missing workbook relationship to xl/styles.xml for public styles/formatting tag");
        }

        if (tags.Contains("styles") || tags.Contains("formatting"))
        {
            var stylesContentTypeIssue = FindPackageContentTypeIssue(
                archive,
                "xl/styles.xml",
                StylesContentType);
            if (stylesContentTypeIssue is not null)
                issues.Add(stylesContentTypeIssue);
        }

        if (tags.Contains("shared-strings") &&
            !PackageEntryExists(archive, "xl/sharedStrings.xml"))
        {
            issues.Add("missing xl/sharedStrings.xml for public shared-strings tag");
        }

        if (tags.Contains("shared-strings") &&
            !PackageRelationshipExists(
                archive,
                new PackageRelationshipExpectation(
                    WorkbookRelationshipPart,
                    SharedStringsRelationshipType,
                    "xl/sharedStrings.xml")))
        {
            issues.Add("missing workbook relationship to xl/sharedStrings.xml for public shared-strings tag");
        }

        if (tags.Contains("shared-strings"))
        {
            var sharedStringsContentTypeIssue = FindPackageContentTypeIssue(
                archive,
                "xl/sharedStrings.xml",
                SharedStringsContentType);
            if (sharedStringsContentTypeIssue is not null)
                issues.Add(sharedStringsContentTypeIssue);
        }

        if (tags.Contains("hyperlinks") &&
            !PublicWorksheetElements(worksheetXmlDocuments, "hyperlink").Any())
        {
            issues.Add("missing worksheet hyperlink elements for public hyperlinks tag");
        }

        if (tags.Contains("hyperlinks") &&
            !tags.Contains("malformed-links"))
        {
            issues.AddRange(FindPublicHyperlinkRelationshipIssues(archive));
        }

        if (tags.Contains("merged-cells") &&
            !PublicWorksheetElements(worksheetXmlDocuments, "mergeCell").Any())
        {
            issues.Add("missing worksheet mergeCell elements for public merged-cells tag");
        }

        if (!allowExcelNormalization &&
            tags.Contains("inline-strings") &&
            !PublicWorksheetCells(worksheetXmlDocuments).Any(IsInlineStringCell))
        {
            issues.Add("missing inline-string cells for public inline-strings tag");
        }

        if (tags.Contains("cell-types"))
        {
            var distinctCellTypes = PublicWorksheetCells(worksheetXmlDocuments)
                .Select(cell => cell.Attribute("t")?.Value ?? "n")
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (distinctCellTypes < 3)
                issues.Add($"expected at least 3 worksheet cell types for public cell-types tag, observed {distinctCellTypes}");
        }

        if (tags.Contains("sheet-names") &&
            tags.Contains("boundary") &&
            !PublicWorkbookSheetNames(archive).Any(name => name.Length == 31))
        {
            issues.Add("missing 31-character workbook sheet name for public sheet-names boundary tags");
        }

        if (tags.Contains("chartsheet"))
        {
            issues.AddRange(FindPublicChartsheetPackageIssues(archive));
        }
        else if (tags.Contains("unsupported-sheet-types") &&
            !archive.Entries.Any(entry =>
                NormalizePackagePart(entry.FullName).StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("missing chartsheet package parts for public unsupported-sheet-types tag");
        }

        if (issues.Count == 0)
            return;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' failed public package-tag assertions for corpus row '{row.Id}': {string.Join("; ", issues)}");
    }

    private static bool HasExpectedPublicPackageTags(IReadOnlySet<string> tags) =>
        tags.Contains("styles") ||
        tags.Contains("formatting") ||
        tags.Contains("shared-strings") ||
        tags.Contains("hyperlinks") ||
        tags.Contains("merged-cells") ||
        tags.Contains("inline-strings") ||
        tags.Contains("cell-types") ||
        (tags.Contains("sheet-names") && tags.Contains("boundary")) ||
        tags.Contains("unsupported-sheet-types");

    private static bool HasExpectedPublicWorksheetPackageTags(IReadOnlySet<string> tags) =>
        tags.Contains("styles") ||
        tags.Contains("formatting") ||
        tags.Contains("shared-strings") ||
        tags.Contains("hyperlinks") ||
        tags.Contains("merged-cells") ||
        tags.Contains("inline-strings") ||
        tags.Contains("cell-types") ||
        (tags.Contains("sheet-names") && tags.Contains("boundary"));

    private static IReadOnlyList<XDocument> LoadPublicWorkbookWorksheetXmlDocuments(
        ZipArchive archive,
        IReadOnlySet<string> tags,
        ICollection<string> issues)
    {
        if (!HasExpectedPublicWorksheetPackageTags(tags))
            return [];

        var documents = new List<XDocument>();
        foreach (var worksheetPart in FindPublicWorkbookWorksheetParts(archive, issues))
        {
            var entry = FindPackageEntry(archive, worksheetPart);
            if (entry is not null)
                documents.Add(LoadPackageXml(entry));
        }

        return documents;
    }

    private static IReadOnlyList<string> FindPublicWorkbookWorksheetParts(
        ZipArchive archive,
        ICollection<string> issues)
    {
        var workbookEntry = FindPackageEntry(archive, "xl/workbook.xml");
        if (workbookEntry is null)
        {
            issues.Add("missing xl/workbook.xml for public workbook worksheet graph");
            return [];
        }

        var relationshipEntry = FindPackageEntry(archive, WorkbookRelationshipPart);
        if (relationshipEntry is null)
        {
            issues.Add($"missing {WorkbookRelationshipPart} for public workbook worksheet graph");
            return [];
        }

        var relationships = LoadPackageXml(relationshipEntry)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .ToArray() ?? [];
        var worksheetParts = new List<string>();
        var seenWorksheetParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in LoadPackageXml(workbookEntry).Descendants(SpreadsheetNs + "sheet"))
        {
            var sheetName = sheet.Attribute("name")?.Value ?? "(unnamed sheet)";
            var relationshipId = sheet.Attribute(OfficeRelationshipNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                issues.Add($"workbook sheet '{sheetName}' has no relationship id for public worksheet package graph");
                continue;
            }

            var relationship = relationships.FirstOrDefault(relationship =>
                string.Equals(
                    relationship.Attribute("Id")?.Value,
                    relationshipId,
                    StringComparison.OrdinalIgnoreCase));
            if (relationship is null)
            {
                issues.Add($"workbook sheet '{sheetName}' targets missing relationship {relationshipId} in {WorkbookRelationshipPart}");
                continue;
            }

            var relationshipType = relationship.Attribute("Type")?.Value;
            if (string.Equals(relationshipType, ChartSheetRelationshipType, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.Equals(relationshipType, WorksheetRelationshipType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} has Type={relationshipType}; expected worksheet or chartsheet relationship");
                continue;
            }

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
            {
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} has no Target");
                continue;
            }

            if (!TryResolvePackageRelationshipTarget(WorkbookRelationshipPart, target, out var worksheetPart, out var targetError))
            {
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} has invalid Target {target}: {targetError}");
                continue;
            }

            if (!PackageEntryExists(archive, worksheetPart))
            {
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} targets missing package part {worksheetPart}");
                continue;
            }

            var worksheetContentTypeIssue = FindPackageContentTypeIssue(archive, worksheetPart, WorksheetContentType);
            if (worksheetContentTypeIssue is not null)
                issues.Add(worksheetContentTypeIssue);

            if (seenWorksheetParts.Add(worksheetPart))
                worksheetParts.Add(worksheetPart);
        }

        if (worksheetParts.Count == 0)
            issues.Add("missing workbook worksheet relationships for public worksheet package tags");

        return worksheetParts;
    }

    private static IEnumerable<XElement> PublicWorksheetElements(
        IReadOnlyList<XDocument> worksheetXmlDocuments,
        string localName) =>
        worksheetXmlDocuments.SelectMany(document => document.Descendants(SpreadsheetNs + localName));

    private static IEnumerable<XElement> PublicWorksheetCells(IReadOnlyList<XDocument> worksheetXmlDocuments) =>
        PublicWorksheetElements(worksheetXmlDocuments, "c");

    private static IEnumerable<string> FindPublicChartsheetPackageIssues(ZipArchive archive)
    {
        var chartsheetEntries = archive.Entries
            .Where(entry =>
                NormalizePackagePart(entry.FullName).StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (chartsheetEntries.Length == 0)
        {
            yield return "missing chartsheet package parts for public chartsheet tag";
            yield break;
        }

        foreach (var chartsheetEntry in chartsheetEntries)
        {
            var chartsheetPart = NormalizePackagePart(chartsheetEntry.FullName);
            var chartsheetContentTypeIssue = FindPackageContentTypeIssue(
                archive,
                chartsheetPart,
                ChartSheetContentType);
            if (chartsheetContentTypeIssue is not null)
                yield return chartsheetContentTypeIssue;

            if (!PackageRelationshipExists(
                    archive,
                    new PackageRelationshipExpectation(
                        "xl/_rels/workbook.xml.rels",
                        ChartSheetRelationshipType,
                        chartsheetPart)))
            {
                yield return $"missing workbook relationship to {chartsheetPart} for public chartsheet tag";
            }

            var drawingIds = LoadPackageXml(chartsheetEntry)
                .Descendants(SpreadsheetNs + "drawing")
                .Select(drawing => drawing.Attribute(OfficeRelationshipNs + "id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToArray();
            if (drawingIds.Length == 0)
            {
                yield return $"{chartsheetPart} has no drawing relationship reference for public chartsheet tag";
                continue;
            }

            var chartsheetRelationshipPart = GetRelationshipPartForPackagePart(chartsheetPart);
            foreach (var drawingId in drawingIds)
            {
                if (!TryGetPackageRelationshipTarget(
                        archive,
                        chartsheetRelationshipPart,
                        drawingId,
                        DrawingRelationshipType,
                        out var drawingTarget,
                        out var drawingRelationshipIssue))
                {
                    yield return $"{chartsheetPart} drawing reference {drawingId}: {drawingRelationshipIssue}";
                    continue;
                }

                var drawingPart = ResolvePackageRelationshipTarget(chartsheetRelationshipPart, drawingTarget!);
                var drawingContentTypeIssue = FindPackageContentTypeIssue(
                    archive,
                    drawingPart,
                    DrawingContentType);
                if (drawingContentTypeIssue is not null)
                    yield return drawingContentTypeIssue;

                var drawingEntry = FindPackageEntry(archive, drawingPart);
                if (drawingEntry is null)
                {
                    yield return $"{chartsheetPart} drawing reference {drawingId} targets missing package part {drawingPart}";
                    continue;
                }

                var chartIds = LoadPackageXml(drawingEntry)
                    .Descendants(DrawingChartNs + "chart")
                    .Select(chart => chart.Attribute(OfficeRelationshipNs + "id")?.Value)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!)
                    .ToArray();
                if (chartIds.Length == 0)
                {
                    yield return $"{drawingPart} has no chart relationship reference for public chartsheet tag";
                    continue;
                }

                var drawingRelationshipPart = GetRelationshipPartForPackagePart(drawingPart);
                foreach (var chartId in chartIds)
                {
                    if (!TryGetPackageRelationshipTarget(
                            archive,
                            drawingRelationshipPart,
                            chartId,
                            ChartRelationshipType,
                            out var chartTarget,
                            out var chartRelationshipIssue))
                    {
                        yield return $"{drawingPart} chart reference {chartId}: {chartRelationshipIssue}";
                        continue;
                    }

                    var chartPart = ResolvePackageRelationshipTarget(drawingRelationshipPart, chartTarget!);
                    var chartContentTypeIssue = FindPackageContentTypeIssue(
                        archive,
                        chartPart,
                        DrawingMlChartContentType);
                    if (chartContentTypeIssue is not null)
                        yield return chartContentTypeIssue;
                }
            }
        }
    }

    private static IEnumerable<string> FindPublicHyperlinkRelationshipIssues(ZipArchive archive) =>
        FindWorksheetHyperlinkPackageIssues(archive);

    private static bool IsInlineStringCell(XElement cell) =>
        string.Equals(cell.Attribute("t")?.Value, "inlineStr", StringComparison.Ordinal) ||
        cell.Element(SpreadsheetNs + "is") is not null;

    private static IReadOnlyList<string> PublicWorkbookSheetNames(ZipArchive archive)
    {
        var workbookEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(NormalizePackagePart(entry.FullName), "xl/workbook.xml", StringComparison.OrdinalIgnoreCase));
        if (workbookEntry is null)
            return [];

        return LoadPackageXml(workbookEntry)
            .Descendants(SpreadsheetNs + "sheet")
            .Select(sheet => sheet.Attribute("name")?.Value ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();
    }

    private static bool PackageEntryExists(ZipArchive archive, string packagePart) =>
        FindPackageEntry(archive, packagePart) is not null;

    private static ZipArchiveEntry? FindPackageEntry(ZipArchive archive, string packagePart) =>
        archive.Entries.FirstOrDefault(entry =>
            string.Equals(
                NormalizePackagePart(entry.FullName),
                NormalizePackagePart(packagePart),
                StringComparison.OrdinalIgnoreCase));

    private static bool PackageRelationshipExists(
        ZipArchive archive,
        PackageRelationshipExpectation expectation)
    {
        var relationshipPart = NormalizePackagePart(expectation.RelationshipPart);
        var entry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(NormalizePackagePart(entry.FullName), relationshipPart, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return false;

        var relationshipsXml = LoadPackageXml(entry);
        return relationshipsXml.Root?.Elements(PackageRelationshipNs + "Relationship")
            .Any(relationship => PackageRelationshipMatches(relationshipPart, relationship, expectation)) == true;
    }

    private static string? FindPackageContentTypeIssue(
        ZipArchive archive,
        string packagePart,
        string expectedContentType)
    {
        var contentTypesEntry = FindPackageEntry(archive, "[Content_Types].xml");
        if (contentTypesEntry is null)
            return $"missing [Content_Types].xml for package content type assertion on {packagePart}";

        var actualContentType = GetEffectivePackageContentType(LoadPackageXml(contentTypesEntry), packagePart);
        if (actualContentType is null)
            return $"{packagePart} has no effective package content type";

        return string.Equals(actualContentType, expectedContentType, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{packagePart} has ContentType={actualContentType}; expected {expectedContentType}";
    }

    private static bool TryGetPackageRelationshipTarget(
        ZipArchive archive,
        string relationshipPart,
        string relationshipId,
        string expectedRelationshipType,
        out string? target,
        out string? issue)
    {
        target = null;
        relationshipPart = NormalizePackagePart(relationshipPart);
        var entry = FindPackageEntry(archive, relationshipPart);
        if (entry is null)
        {
            issue = $"missing relationship part {relationshipPart}";
            return false;
        }

        var relationship = LoadPackageXml(entry)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .FirstOrDefault(relationship =>
                string.Equals(
                    relationship.Attribute("Id")?.Value,
                    relationshipId,
                    StringComparison.OrdinalIgnoreCase));
        if (relationship is null)
        {
            issue = $"targets missing relationship {relationshipId} in {relationshipPart}";
            return false;
        }

        if (!string.Equals(relationship.Attribute("Type")?.Value, expectedRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issue = $"relationship {relationshipId} in {relationshipPart} has Type={relationship.Attribute("Type")?.Value}; expected {expectedRelationshipType}";
            return false;
        }

        target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issue = $"relationship {relationshipId} in {relationshipPart} has no Target";
            return false;
        }

        issue = null;
        return true;
    }

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void AssertRequiredExcelSavedPackageParts(
        string xlsxPath,
        WorkbookSmokeExpectations? expectations,
        string sourcePath)
    {
        AssertRequiredPackageParts(
            xlsxPath,
            expectations?.RequiredExcelSavedPackageParts,
            "Excel-saved workbook",
            sourcePath);
    }

    private static void AssertRequiredFreeXSavedPackageParts(
        string xlsxPath,
        WorkbookSmokeExpectations? expectations,
        string sourcePath)
    {
        AssertRequiredPackageParts(
            xlsxPath,
            expectations?.RequiredFreeXSavedPackageParts,
            "FreeX-saved workbook",
            sourcePath);
    }

    private static void AssertRequiredPackageParts(
        string xlsxPath,
        IReadOnlyList<string>? requiredParts,
        string label,
        string sourcePath)
    {
        if (requiredParts is null || requiredParts.Count == 0)
            return;

        using var archive = ZipFile.OpenRead(xlsxPath);
        var entries = archive.Entries
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requiredParts
            .Select(NormalizePackagePart)
            .Where(part => !entries.Contains(part))
            .ToArray();

        if (missing.Length == 0)
            return;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' is missing required package part(s): {string.Join(", ", missing)}");
    }

    private static void AssertRequiredExcelSavedPackageContentTypes(
        string xlsxPath,
        WorkbookSmokeExpectations? expectations,
        string sourcePath)
    {
        AssertRequiredPackageContentTypes(
            xlsxPath,
            expectations?.RequiredExcelSavedPackageContentTypes,
            "Excel-saved workbook",
            sourcePath);
    }

    private static void AssertRequiredFreeXSavedPackageContentTypes(
        string xlsxPath,
        WorkbookSmokeExpectations? expectations,
        string sourcePath)
    {
        AssertRequiredPackageContentTypes(
            xlsxPath,
            expectations?.RequiredFreeXSavedPackageContentTypes,
            "FreeX-saved workbook",
            sourcePath);
    }

    private static void AssertRequiredPackageContentTypes(
        string xlsxPath,
        IReadOnlyList<PackageContentTypeExpectation>? requiredContentTypes,
        string label,
        string sourcePath)
    {
        if (requiredContentTypes is null || requiredContentTypes.Count == 0)
            return;

        using var archive = ZipFile.OpenRead(xlsxPath);
        var entryNames = archive.Entries
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
        {
            throw new InvalidDataException(
                $"{label} for '{sourcePath}' is missing [Content_Types].xml.");
        }

        XDocument contentTypesXml;
        using (var stream = contentTypesEntry.Open())
            contentTypesXml = XDocument.Load(stream);

        var missing = new List<string>();
        foreach (var expectation in requiredContentTypes)
        {
            var partName = NormalizePackagePart(expectation.PartName);
            if (!entryNames.Contains(partName))
            {
                missing.Add($"{FormatPackageContentTypeExpectation(expectation)} (missing package part)");
                continue;
            }

            var actualContentType = GetEffectivePackageContentType(contentTypesXml, partName);
            if (actualContentType is null)
            {
                missing.Add($"{FormatPackageContentTypeExpectation(expectation)} (missing content type)");
                continue;
            }

            if (!string.Equals(actualContentType, expectation.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                missing.Add(
                    $"{FormatPackageContentTypeExpectation(expectation)} (observed ContentType={actualContentType})");
            }
        }

        if (missing.Count == 0)
            return;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' is missing required package content type(s): {string.Join("; ", missing)}");
    }

    private static void AssertPackageEntriesCanonical(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var exactNames = new HashSet<string>(StringComparer.Ordinal);
        var packagePartNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            var rawName = entry.FullName;
            var normalizedName = rawName.Replace('\\', '/');

            if (rawName.Contains('\\', StringComparison.Ordinal))
                issues.Add($"{rawName} uses a backslash in the package part name");
            if (normalizedName.StartsWith("/", StringComparison.Ordinal))
                issues.Add($"{rawName} starts with '/'");
            if (normalizedName.Contains("//", StringComparison.Ordinal))
                issues.Add($"{rawName} has an empty path segment");

            var segments = normalizedName.Split('/', StringSplitOptions.None);
            if (segments.Any(segment => segment is "." or ".."))
                issues.Add($"{rawName} has a relative path segment");

            if (!exactNames.Add(normalizedName))
            {
                issues.Add($"{rawName} duplicates package part {normalizedName}");
                continue;
            }

            if (packagePartNames.TryGetValue(normalizedName, out var existingName))
            {
                issues.Add($"{rawName} collides with package part {existingName} when compared case-insensitively");
            }
            else
            {
                packagePartNames.Add(normalizedName, normalizedName);
            }
        }

        if (issues.Count == 0)
            return;

        var sample = string.Join("; ", issues.Take(MaxPackageEntryIssuesToReport));
        var suffix = issues.Count > MaxPackageEntryIssuesToReport
            ? $"; ... {issues.Count - MaxPackageEntryIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid package ZIP entries: {sample}{suffix}");
    }

    private static void AssertPackageContentTypesComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
        {
            throw new InvalidDataException(
                $"{label} for '{sourcePath}' is missing [Content_Types].xml.");
        }

        XDocument contentTypesXml;
        using (var stream = contentTypesEntry.Open())
            contentTypesXml = XDocument.Load(stream);

        if (contentTypesXml.Root?.Name != PackageContentTypeNs + "Types")
        {
            throw new InvalidDataException(
                $"{label} for '{sourcePath}' has an invalid [Content_Types].xml root element.");
        }

        var packageParts = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var declarationIssues = FindPackageContentTypeDeclarationIssues(contentTypesXml, packageParts);
        if (declarationIssues.Count > 0)
        {
            var declarationSample = string.Join("; ", declarationIssues.Take(MaxPackageContentTypeIssuesToReport));
            var declarationSuffix = declarationIssues.Count > MaxPackageContentTypeIssuesToReport
                ? $"; ... {declarationIssues.Count - MaxPackageContentTypeIssuesToReport} more"
                : string.Empty;

            throw new InvalidDataException(
                $"{label} for '{sourcePath}' has invalid [Content_Types].xml declarations: {declarationSample}{declarationSuffix}");
        }

        var missing = packageParts
            .Where(part => !string.Equals(part, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
            .Where(part => string.IsNullOrWhiteSpace(GetEffectivePackageContentType(contentTypesXml, part)))
            .ToArray();

        if (missing.Length == 0)
        {
            var consistencyIssues = FindPackageContentTypeConsistencyIssues(contentTypesXml, packageParts);
            if (consistencyIssues.Count == 0)
                return;

            var consistencySample = string.Join("; ", consistencyIssues.Take(MaxPackageContentTypeIssuesToReport));
            var consistencySuffix = consistencyIssues.Count > MaxPackageContentTypeIssuesToReport
                ? $"; ... {consistencyIssues.Count - MaxPackageContentTypeIssuesToReport} more"
                : string.Empty;

            throw new InvalidDataException(
                $"{label} for '{sourcePath}' has inconsistent package content types: {consistencySample}{consistencySuffix}");
        }

        var sample = string.Join(", ", missing.Take(MaxPackageContentTypeIssuesToReport));
        var suffix = missing.Length > MaxPackageContentTypeIssuesToReport
            ? $", ... {missing.Length - MaxPackageContentTypeIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has package part(s) without effective content types: {sample}{suffix}");
    }

    private static List<string> FindPackageContentTypeConsistencyIssues(
        XDocument contentTypesXml,
        IReadOnlySet<string> packageParts)
    {
        var issues = new List<string>();
        foreach (var part in packageParts
                     .Where(part => !string.Equals(part, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(part => part, StringComparer.OrdinalIgnoreCase))
        {
            var contentType = GetEffectivePackageContentType(contentTypesXml, part);
            if (string.IsNullOrWhiteSpace(contentType))
                continue;

            var isRelationshipPart = IsPackageRelationshipPart(part);
            var hasRelationshipExtension = part.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);
            var hasRelationshipContentType = string.Equals(
                contentType,
                RelationshipPartContentType,
                StringComparison.OrdinalIgnoreCase);

            if (isRelationshipPart && !hasRelationshipContentType)
            {
                issues.Add($"{part} must use relationship content type {RelationshipPartContentType}; actual {contentType}");
            }
            else if (!isRelationshipPart && hasRelationshipContentType)
            {
                issues.Add($"{part} uses relationship content type but is not a valid relationship part");
            }

            if (hasRelationshipExtension && !isRelationshipPart)
                issues.Add($"{part} has .rels extension outside a valid relationship part location");
        }

        return issues;
    }

    private static List<string> FindPackageContentTypeDeclarationIssues(
        XDocument contentTypesXml,
        HashSet<string> packageParts)
    {
        var issues = new List<string>();
        var root = contentTypesXml.Root;
        if (root is null)
            return issues;

        foreach (var element in root.Elements())
        {
            if (element.Name != PackageContentTypeNs + "Default" &&
                element.Name != PackageContentTypeNs + "Override")
            {
                issues.Add($"unexpected child element '{element.Name}'");
            }
        }

        var defaultExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in root.Elements(PackageContentTypeNs + "Default"))
        {
            var extension = element.Attribute("Extension")?.Value;
            var declarationLabel = string.IsNullOrWhiteSpace(extension)
                ? "Default declaration"
                : $"Default extension '{extension}'";

            if (string.IsNullOrWhiteSpace(extension))
            {
                issues.Add("Default declaration missing Extension");
            }
            else
            {
                var trimmedExtension = extension.Trim();
                declarationLabel = $"Default extension '{trimmedExtension}'";

                if (!string.Equals(extension, trimmedExtension, StringComparison.Ordinal))
                    issues.Add($"Default extension '{extension}' has leading or trailing whitespace");

                if (trimmedExtension.IndexOf('/') >= 0 ||
                    trimmedExtension.IndexOf('\\') >= 0 ||
                    trimmedExtension.IndexOf('.') >= 0 ||
                    trimmedExtension.Any(char.IsWhiteSpace))
                {
                    issues.Add($"Default extension '{trimmedExtension}' is not a bare package extension");
                }

                if (!defaultExtensions.Add(trimmedExtension))
                    issues.Add($"duplicate Default extension '{trimmedExtension}'");
            }

            AddContentTypeAttributeIssues(issues, element, declarationLabel);
        }

        var overridePartNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in root.Elements(PackageContentTypeNs + "Override"))
        {
            var partName = element.Attribute("PartName")?.Value;
            var declarationLabel = string.IsNullOrWhiteSpace(partName)
                ? "Override declaration"
                : $"Override PartName '{partName}'";

            if (string.IsNullOrWhiteSpace(partName))
            {
                issues.Add("Override declaration missing PartName");
            }
            else
            {
                var trimmedPartName = partName.Trim();

                if (!string.Equals(partName, trimmedPartName, StringComparison.Ordinal))
                    issues.Add($"Override PartName '{partName}' has leading or trailing whitespace");

                if (!trimmedPartName.StartsWith("/", StringComparison.Ordinal))
                    issues.Add($"Override PartName '{partName}' must start with '/'");

                if (trimmedPartName.IndexOf('\\') >= 0)
                    issues.Add($"Override PartName '{partName}' must use forward slashes");

                if (trimmedPartName.IndexOf('?') >= 0 || trimmedPartName.IndexOf('#') >= 0)
                    issues.Add($"Override PartName '{partName}' must not include query or fragment text");

                var pathWithoutRootSlash = trimmedPartName.TrimStart('/');
                if (!TryNormalizePackagePathSegments(pathWithoutRootSlash, out var overridePart))
                {
                    issues.Add($"Override PartName '{partName}' escapes the package root");
                }
                else if (string.IsNullOrWhiteSpace(overridePart))
                {
                    issues.Add($"Override PartName '{partName}' does not reference a package part");
                }
                else
                {
                    declarationLabel = $"Override PartName '/{overridePart}'";
                    var rawNormalizedPart = NormalizePackagePart(trimmedPartName);
                    if (!string.Equals(overridePart, rawNormalizedPart, StringComparison.Ordinal))
                        issues.Add($"Override PartName '{partName}' is not canonical");

                    if (!overridePartNames.Add(overridePart))
                        issues.Add($"duplicate Override PartName '/{overridePart}'");

                    if (!packageParts.Contains(overridePart))
                        issues.Add($"Override PartName '/{overridePart}' references missing package part");
                }
            }

            AddContentTypeAttributeIssues(issues, element, declarationLabel);
        }

        return issues;
    }

    private static void AddContentTypeAttributeIssues(
        List<string> issues,
        XElement element,
        string declarationLabel)
    {
        var contentType = element.Attribute("ContentType")?.Value;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            issues.Add($"{declarationLabel} missing ContentType");
            return;
        }

        if (!string.Equals(contentType, contentType.Trim(), StringComparison.Ordinal))
            issues.Add($"{declarationLabel} ContentType has leading or trailing whitespace");

        if (!contentType.Contains("/", StringComparison.Ordinal))
            issues.Add($"{declarationLabel} ContentType '{contentType}' is not a media type");
    }

    private static string? GetEffectivePackageContentType(XDocument contentTypesXml, string normalizedPartName)
    {
        var normalizedContentTypePartName = $"/{NormalizePackagePart(normalizedPartName)}";
        var overrideContentType = contentTypesXml.Root?
            .Elements(PackageContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(
                NormalizeContentTypePartName(element.Attribute("PartName")?.Value),
                normalizedContentTypePartName,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;

        if (!string.IsNullOrWhiteSpace(overrideContentType))
            return overrideContentType;

        var extension = GetPackagePartExtension(normalizedPartName);
        if (string.IsNullOrWhiteSpace(extension))
            return null;

        return contentTypesXml.Root?
            .Elements(PackageContentTypeNs + "Default")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Extension")?.Value,
                extension,
                StringComparison.OrdinalIgnoreCase))
            ?.Attribute("ContentType")
            ?.Value;
    }

    private static string NormalizeContentTypePartName(string? partName) =>
        $"/{NormalizePackagePart(partName ?? string.Empty)}";

    private static string GetPackagePartExtension(string partName)
    {
        var fileName = NormalizePackagePart(partName);
        var slashIndex = fileName.LastIndexOf('/');
        if (slashIndex >= 0)
            fileName = fileName[(slashIndex + 1)..];

        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex >= 0 && dotIndex < fileName.Length - 1
            ? fileName[(dotIndex + 1)..]
            : string.Empty;
    }

    private static void AssertRequiredExcelSavedPackageRelationships(
        string xlsxPath,
        WorkbookSmokeExpectations? expectations,
        string sourcePath)
    {
        AssertRequiredPackageRelationships(
            xlsxPath,
            expectations?.RequiredExcelSavedPackageRelationships,
            "Excel-saved workbook",
            sourcePath);
    }

    private static void AssertRequiredFreeXSavedPackageRelationships(
        string xlsxPath,
        WorkbookSmokeExpectations? expectations,
        string sourcePath)
    {
        AssertRequiredPackageRelationships(
            xlsxPath,
            expectations?.RequiredFreeXSavedPackageRelationships,
            "FreeX-saved workbook",
            sourcePath);
    }

    private static void AssertRequiredPackageRelationships(
        string xlsxPath,
        IReadOnlyList<PackageRelationshipExpectation>? requiredRelationships,
        string label,
        string sourcePath)
    {
        if (requiredRelationships is null || requiredRelationships.Count == 0)
            return;

        using var archive = ZipFile.OpenRead(xlsxPath);
        var missing = new List<string>();
        foreach (var expectation in requiredRelationships)
        {
            var relationshipPart = NormalizePackagePart(expectation.RelationshipPart);
            var entry = archive.GetEntry(relationshipPart);
            if (entry is null)
            {
                missing.Add($"{FormatPackageRelationshipExpectation(expectation)} (missing relationship part)");
                continue;
            }

            XDocument relationshipsXml;
            using (var stream = entry.Open())
                relationshipsXml = XDocument.Load(stream);

            if (relationshipsXml.Root?.Elements(PackageRelationshipNs + "Relationship")
                    .Any(relationship => PackageRelationshipMatches(relationshipPart, relationship, expectation)) != true)
            {
                missing.Add(FormatPackageRelationshipExpectation(expectation));
            }
        }

        if (missing.Count == 0)
            return;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' is missing required package relationship(s): {string.Join("; ", missing)}");
    }

    private static bool PackageRelationshipMatches(
        string relationshipPart,
        XElement relationship,
        PackageRelationshipExpectation expectation)
    {
        if (!string.Equals(
                relationship.Attribute("Type")?.Value,
                expectation.RelationshipType,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectation.Id is not null &&
            !string.Equals(relationship.Attribute("Id")?.Value, expectation.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectation.TargetMode is not null &&
            !string.Equals(relationship.Attribute("TargetMode")?.Value, expectation.TargetMode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var actualTarget = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(actualTarget))
            return false;

        if (string.Equals(actualTarget, expectation.Target, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            return false;

        var actualPackageTarget = ResolvePackageRelationshipTarget(relationshipPart, actualTarget);
        var expectedPackageTarget = NormalizePackagePart(expectation.Target);
        return string.Equals(actualPackageTarget, expectedPackageTarget, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPackageRelationshipExpectation(PackageRelationshipExpectation expectation)
    {
        var id = expectation.Id is null ? string.Empty : $" Id={expectation.Id}";
        var mode = expectation.TargetMode is null ? string.Empty : $" TargetMode={expectation.TargetMode}";
        return $"{expectation.RelationshipPart} Type={expectation.RelationshipType} Target={expectation.Target}{mode}{id}";
    }

    private static string FormatPackageContentTypeExpectation(PackageContentTypeExpectation expectation) =>
        $"{expectation.PartName} ContentType={expectation.ContentType}";

    private static void AssertWorkbookPackageRoot(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        if (!PackageEntryExists(archive, WorkbookPart))
            issues.Add($"missing {WorkbookPart}");

        if (!PackageRelationshipExists(
                archive,
                new PackageRelationshipExpectation(
                    PackageRootRelationshipPart,
                    OfficeDocumentRelationshipType,
                    WorkbookPart)))
        {
            issues.Add($"missing package root officeDocument relationship to {WorkbookPart}");
        }

        var workbookContentTypeIssue = FindPackageContentTypeIssue(archive, WorkbookPart, WorkbookContentType);
        if (workbookContentTypeIssue is not null)
            issues.Add(workbookContentTypeIssue);

        if (issues.Count == 0)
            return;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid XLSX workbook package root: {string.Join("; ", issues)}");
    }

    private static void AssertWorkbookSheetRelationshipsComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is null)
            issues.Add($"missing {WorkbookPart} for workbook sheet graph");

        var relationshipEntry = FindPackageEntry(archive, WorkbookRelationshipPart);
        if (relationshipEntry is null)
            issues.Add($"missing {WorkbookRelationshipPart} for workbook sheet graph");

        if (workbookEntry is null || relationshipEntry is null)
        {
            ThrowInvalidWorkbookSheetGraph(label, sourcePath, issues);
            return;
        }

        var relationships = LoadPackageXml(relationshipEntry)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .ToArray() ?? [];
        var sheets = LoadPackageXml(workbookEntry)
            .Descendants(SpreadsheetNs + "sheet")
            .ToArray();
        if (sheets.Length == 0)
            issues.Add("workbook has no sheet elements");

        var seenRelationshipIds = new HashSet<string>(StringComparer.Ordinal);
        var seenSheetParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            var sheetName = sheet.Attribute("name")?.Value ?? "(unnamed sheet)";
            var relationshipId = sheet.Attribute(OfficeRelationshipNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                issues.Add($"workbook sheet '{sheetName}' has no relationship id");
                continue;
            }

            if (!seenRelationshipIds.Add(relationshipId))
            {
                issues.Add($"workbook sheet '{sheetName}' reuses relationship id {relationshipId}");
                continue;
            }

            var relationship = relationships.FirstOrDefault(relationship =>
                string.Equals(
                    relationship.Attribute("Id")?.Value,
                    relationshipId,
                    StringComparison.Ordinal));
            if (relationship is null)
            {
                issues.Add($"workbook sheet '{sheetName}' targets missing relationship {relationshipId} in {WorkbookRelationshipPart}");
                continue;
            }

            var relationshipType = relationship.Attribute("Type")?.Value;
            if (!TryGetWorkbookSheetExpectedContentType(relationshipType, out var expectedContentType))
            {
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} has unsupported Type={relationshipType}");
                continue;
            }

            var targetMode = relationship.Attribute("TargetMode")?.Value;
            if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} must not target an external sheet package part");
                continue;
            }

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
            {
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} has no Target");
                continue;
            }

            if (!TryResolvePackageRelationshipTarget(WorkbookRelationshipPart, target, out var sheetPart, out var targetError))
            {
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} has invalid Target {target}: {targetError}");
                continue;
            }

            if (!PackageEntryExists(archive, sheetPart))
            {
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} targets missing package part {sheetPart}");
                continue;
            }

            if (!seenSheetParts.Add(sheetPart))
                issues.Add($"workbook sheet '{sheetName}' relationship {relationshipId} reuses package part {sheetPart}");

            var contentTypeIssue = FindPackageContentTypeIssue(archive, sheetPart, expectedContentType);
            if (contentTypeIssue is not null)
                issues.Add(contentTypeIssue);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookSheetGraph(label, sourcePath, issues);
    }

    private static bool TryGetWorkbookSheetExpectedContentType(string? relationshipType, out string expectedContentType)
    {
        expectedContentType = string.Empty;
        if (string.Equals(relationshipType, WorksheetRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            expectedContentType = WorksheetContentType;
            return true;
        }

        if (string.Equals(relationshipType, ChartSheetRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            expectedContentType = ChartSheetContentType;
            return true;
        }

        if (string.Equals(relationshipType, DialogSheetRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            expectedContentType = DialogSheetContentType;
            return true;
        }

        if (string.Equals(relationshipType, MacroSheetRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            expectedContentType = MacroSheetContentType;
            return true;
        }

        return false;
    }

    private static void ThrowInvalidWorkbookSheetGraph(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook sheet package graph: {sample}{suffix}");
    }

    private static void AssertSharedStringTableComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var sharedStringCells = FindSharedStringCells(archive);
        var sharedStringsEntry = FindPackageEntry(archive, "xl/sharedStrings.xml");
        if (sharedStringCells.Count == 0 && sharedStringsEntry is null)
            return;

        var issues = new List<string>();
        if (sharedStringsEntry is null)
        {
            issues.Add("missing xl/sharedStrings.xml for shared-string cells");
            ThrowInvalidSharedStringTable(label, sourcePath, issues);
            return;
        }

        if (!PackageRelationshipExists(
                archive,
                new PackageRelationshipExpectation(
                    WorkbookRelationshipPart,
                    SharedStringsRelationshipType,
                    "xl/sharedStrings.xml")))
        {
            issues.Add("missing workbook relationship to xl/sharedStrings.xml");
        }

        var contentTypeIssue = FindPackageContentTypeIssue(archive, "xl/sharedStrings.xml", SharedStringsContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var sharedStringsXml = LoadPackageXml(sharedStringsEntry);
        if (sharedStringsXml.Root?.Name != SpreadsheetNs + "sst")
        {
            issues.Add("xl/sharedStrings.xml has an invalid shared-string table root element");
            ThrowInvalidSharedStringTable(label, sourcePath, issues);
            return;
        }

        var sharedStringCount = sharedStringsXml.Root.Elements(SpreadsheetNs + "si").Count();
        foreach (var sharedStringCell in sharedStringCells)
        {
            if (string.IsNullOrWhiteSpace(sharedStringCell.ValueText))
            {
                issues.Add($"{sharedStringCell.WorksheetPart} cell {sharedStringCell.CellReference} has no shared-string index");
                continue;
            }

            if (!int.TryParse(sharedStringCell.ValueText, NumberStyles.None, CultureInfo.InvariantCulture, out var sharedStringIndex))
            {
                issues.Add($"{sharedStringCell.WorksheetPart} cell {sharedStringCell.CellReference} has invalid shared-string index '{sharedStringCell.ValueText}'");
                continue;
            }

            if (sharedStringIndex < 0 || sharedStringIndex >= sharedStringCount)
            {
                issues.Add(
                    $"{sharedStringCell.WorksheetPart} cell {sharedStringCell.CellReference} references shared-string index {sharedStringIndex}, but xl/sharedStrings.xml contains {sharedStringCount} entries");
            }
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidSharedStringTable(label, sourcePath, issues);
    }

    private static List<SharedStringCellReference> FindSharedStringCells(ZipArchive archive)
    {
        var cells = new List<SharedStringCellReference>();
        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            foreach (var cell in LoadPackageXml(worksheetEntry).Descendants(SpreadsheetNs + "c"))
            {
                if (!string.Equals(cell.Attribute("t")?.Value, "s", StringComparison.Ordinal))
                    continue;

                cells.Add(new SharedStringCellReference(
                    worksheetPart,
                    cell.Attribute("r")?.Value ?? "(unknown ref)",
                    cell.Element(SpreadsheetNs + "v")?.Value));
            }
        }

        return cells;
    }

    private static void ThrowInvalidSharedStringTable(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid shared-string package graph: {sample}{suffix}");
    }

    private static void AssertStylesPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var styleReferences = FindStyleReferences(archive);
        var stylesEntry = FindPackageEntry(archive, "xl/styles.xml");
        if (styleReferences.Count == 0 && stylesEntry is null)
            return;

        var issues = new List<string>();
        if (stylesEntry is null)
        {
            issues.Add("missing xl/styles.xml for style references");
            ThrowInvalidStylesPackage(label, sourcePath, issues);
            return;
        }

        if (!PackageRelationshipExists(
                archive,
                new PackageRelationshipExpectation(
                    WorkbookRelationshipPart,
                    StylesRelationshipType,
                    "xl/styles.xml")))
        {
            issues.Add("missing workbook relationship to xl/styles.xml");
        }

        var contentTypeIssue = FindPackageContentTypeIssue(archive, "xl/styles.xml", StylesContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var stylesXml = LoadPackageXml(stylesEntry);
        if (stylesXml.Root?.Name != SpreadsheetNs + "styleSheet")
        {
            issues.Add("xl/styles.xml has an invalid stylesheet root element");
            ThrowInvalidStylesPackage(label, sourcePath, issues);
            return;
        }

        var cellXfs = stylesXml.Root.Element(SpreadsheetNs + "cellXfs");
        var cellFormatCount = cellXfs?.Elements(SpreadsheetNs + "xf").Count() ?? 0;
        if (cellFormatCount == 0)
            issues.Add("xl/styles.xml has no cellXfs xf entries");

        AddStyleCountAttributeIssues(issues, "cellXfs", cellXfs, cellFormatCount);
        foreach (var styleReference in styleReferences)
        {
            if (string.IsNullOrWhiteSpace(styleReference.ValueText))
            {
                issues.Add($"{styleReference.WorksheetPart} {styleReference.Description} has no style index");
                continue;
            }

            if (!int.TryParse(styleReference.ValueText, NumberStyles.None, CultureInfo.InvariantCulture, out var styleIndex))
            {
                issues.Add($"{styleReference.WorksheetPart} {styleReference.Description} has invalid style index '{styleReference.ValueText}'");
                continue;
            }

            if (styleIndex < 0 || styleIndex >= cellFormatCount)
            {
                issues.Add(
                    $"{styleReference.WorksheetPart} {styleReference.Description} references style index {styleIndex}, but xl/styles.xml cellXfs contains {cellFormatCount} entries");
            }
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidStylesPackage(label, sourcePath, issues);
    }

    private static List<StyleReference> FindStyleReferences(ZipArchive archive)
    {
        var styleReferences = new List<StyleReference>();
        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetXml = LoadPackageXml(worksheetEntry);
            foreach (var cell in worksheetXml.Descendants(SpreadsheetNs + "c"))
            {
                var styleIndex = cell.Attribute("s")?.Value;
                if (styleIndex is not null)
                {
                    styleReferences.Add(new StyleReference(
                        worksheetPart,
                        $"cell {cell.Attribute("r")?.Value ?? "(unknown ref)"}",
                        styleIndex));
                }
            }

            foreach (var row in worksheetXml.Descendants(SpreadsheetNs + "row"))
            {
                var styleIndex = row.Attribute("s")?.Value;
                if (styleIndex is not null)
                {
                    styleReferences.Add(new StyleReference(
                        worksheetPart,
                        $"row {row.Attribute("r")?.Value ?? "(unknown row)"}",
                        styleIndex));
                }
            }

            foreach (var column in worksheetXml.Descendants(SpreadsheetNs + "col"))
            {
                var styleIndex = column.Attribute("style")?.Value;
                if (styleIndex is not null)
                {
                    var min = column.Attribute("min")?.Value ?? "?";
                    var max = column.Attribute("max")?.Value ?? "?";
                    styleReferences.Add(new StyleReference(
                        worksheetPart,
                        $"column span {min}:{max}",
                        styleIndex));
                }
            }
        }

        return styleReferences;
    }

    private static void AddStyleCountAttributeIssues(
        List<string> issues,
        string elementName,
        XElement? element,
        int actualCount)
    {
        var countText = element?.Attribute("count")?.Value;
        if (string.IsNullOrWhiteSpace(countText))
            return;

        if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredCount))
        {
            issues.Add($"xl/styles.xml {elementName} has invalid count '{countText}'");
            return;
        }

        if (declaredCount != actualCount)
            issues.Add($"xl/styles.xml {elementName} count is {declaredCount}, but contains {actualCount} child entries");
    }

    private static void ThrowInvalidStylesPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid styles package graph: {sample}{suffix}");
    }

    private static void AssertWorksheetHyperlinkPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = FindWorksheetHyperlinkPackageIssues(archive).ToArray();
        if (issues.Length == 0)
            return;

        ThrowInvalidWorksheetHyperlinkPackage(label, sourcePath, issues);
    }

    private static IEnumerable<string> FindWorksheetHyperlinkPackageIssues(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var hyperlinks = LoadPackageXml(worksheetEntry)
                .Descendants(SpreadsheetNs + "hyperlink")
                .Select((hyperlink, index) => new WorksheetHyperlinkReference(
                    index + 1,
                    hyperlink.Attribute("ref")?.Value,
                    hyperlink.Attribute(OfficeRelationshipNs + "id")?.Value,
                    hyperlink.Attribute("location")?.Value))
                .ToArray();
            if (hyperlinks.Length == 0)
                continue;

            foreach (var hyperlink in hyperlinks)
            {
                if (string.IsNullOrWhiteSpace(hyperlink.Reference))
                {
                    yield return $"{worksheetPart} hyperlink #{hyperlink.Ordinal} has no cell reference";
                }

                if (string.IsNullOrWhiteSpace(hyperlink.RelationshipId) &&
                    string.IsNullOrWhiteSpace(hyperlink.Location))
                {
                    yield return $"{worksheetPart} {FormatWorksheetHyperlinkReference(hyperlink)} has neither a relationship id nor an internal location";
                }
            }

            var linkedHyperlinks = hyperlinks
                .Where(hyperlink => !string.IsNullOrWhiteSpace(hyperlink.RelationshipId))
                .ToArray();
            if (linkedHyperlinks.Length == 0)
                continue;

            var relationshipPart = GetRelationshipPartForPackagePart(worksheetPart);
            var relationshipEntry = FindPackageEntry(archive, relationshipPart);
            if (relationshipEntry is null)
            {
                yield return $"missing {relationshipPart} for worksheet hyperlink relationships in {worksheetPart}";
                continue;
            }

            var relationships = LoadPackageXml(relationshipEntry)
                .Root?
                .Elements(PackageRelationshipNs + "Relationship")
                .ToArray() ?? [];
            foreach (var linkedHyperlink in linkedHyperlinks)
            {
                var relationship = relationships.FirstOrDefault(relationship =>
                    string.Equals(
                        relationship.Attribute("Id")?.Value,
                        linkedHyperlink.RelationshipId,
                        StringComparison.OrdinalIgnoreCase));
                if (relationship is null)
                {
                    yield return $"{worksheetPart} {FormatWorksheetHyperlinkReference(linkedHyperlink)} targets missing relationship {linkedHyperlink.RelationshipId}";
                    continue;
                }

                if (!string.Equals(relationship.Attribute("Type")?.Value, HyperlinkRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{worksheetPart} {FormatWorksheetHyperlinkReference(linkedHyperlink)} relationship {linkedHyperlink.RelationshipId} is not a hyperlink relationship";
                    continue;
                }

                if (!string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                {
                    yield return $"{worksheetPart} {FormatWorksheetHyperlinkReference(linkedHyperlink)} relationship {linkedHyperlink.RelationshipId} is not external";
                    continue;
                }

                if (string.IsNullOrWhiteSpace(relationship.Attribute("Target")?.Value))
                {
                    yield return $"{worksheetPart} {FormatWorksheetHyperlinkReference(linkedHyperlink)} relationship {linkedHyperlink.RelationshipId} has no Target";
                }
            }
        }
    }

    private static string FormatWorksheetHyperlinkReference(WorksheetHyperlinkReference hyperlink) =>
        string.IsNullOrWhiteSpace(hyperlink.Reference)
            ? $"hyperlink #{hyperlink.Ordinal}"
            : $"hyperlink {hyperlink.Reference}";

    private static void ThrowInvalidWorksheetHyperlinkPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet hyperlink package graph: {sample}{suffix}");
    }

    private static void AssertWorksheetDrawingPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetRelationshipPart = GetRelationshipPartForPackagePart(worksheetPart);
            var drawingReferences = LoadPackageXml(worksheetEntry)
                .Descendants(SpreadsheetNs + "drawing")
                .Select(drawing => drawing.Attribute(OfficeRelationshipNs + "id")?.Value)
                .ToArray();

            foreach (var drawingRelationshipId in drawingReferences)
            {
                if (string.IsNullOrWhiteSpace(drawingRelationshipId))
                {
                    issues.Add($"{worksheetPart} has a drawing element without a relationship id");
                    continue;
                }

                if (!TryGetPackageRelationshipTarget(
                        archive,
                        worksheetRelationshipPart,
                        drawingRelationshipId,
                        DrawingRelationshipType,
                        out var drawingTarget,
                        out var drawingRelationshipIssue))
                {
                    issues.Add($"{worksheetPart} drawing reference {drawingRelationshipId}: {drawingRelationshipIssue}");
                    continue;
                }

                if (!TryResolvePackageRelationshipTarget(
                        worksheetRelationshipPart,
                        drawingTarget!,
                        out var drawingPart,
                        out var drawingTargetIssue))
                {
                    issues.Add($"{worksheetPart} drawing reference {drawingRelationshipId} has invalid Target {drawingTarget}: {drawingTargetIssue}");
                    continue;
                }

                var drawingContentTypeIssue = FindPackageContentTypeIssue(archive, drawingPart, DrawingContentType);
                if (drawingContentTypeIssue is not null)
                    issues.Add(drawingContentTypeIssue);

                var drawingEntry = FindPackageEntry(archive, drawingPart);
                if (drawingEntry is null)
                {
                    issues.Add($"{worksheetPart} drawing reference {drawingRelationshipId} targets missing package part {drawingPart}");
                    continue;
                }

                var drawingXml = LoadPackageXml(drawingEntry);
                if (drawingXml.Root?.Name != SpreadsheetDrawingNs + "wsDr")
                {
                    issues.Add($"{drawingPart} has an invalid worksheet drawing root element");
                    continue;
                }

                AddDrawingPartReferenceIssues(archive, drawingPart, drawingXml, issues);
            }
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetDrawingPackage(label, sourcePath, issues);
    }

    private static void AddDrawingPartReferenceIssues(
        ZipArchive archive,
        string drawingPart,
        XDocument drawingXml,
        List<string> issues)
    {
        var drawingRelationshipPart = GetRelationshipPartForPackagePart(drawingPart);
        foreach (var chartRelationshipId in drawingXml
                     .Descendants(DrawingChartNs + "chart")
                     .Select(chart => chart.Attribute(OfficeRelationshipNs + "id")?.Value)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Select(id => id!))
        {
            AddDrawingRelationshipPartIssue(
                archive,
                drawingPart,
                drawingRelationshipPart,
                chartRelationshipId,
                "chart",
                ChartRelationshipType,
                DrawingMlChartContentType,
                issues);
        }

        foreach (var imageRelationshipId in drawingXml
                     .Descendants(DrawingNs + "blip")
                     .Select(blip => blip.Attribute(OfficeRelationshipNs + "embed")?.Value)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Select(id => id!))
        {
            AddDrawingRelationshipPartIssue(
                archive,
                drawingPart,
                drawingRelationshipPart,
                imageRelationshipId,
                "embedded image",
                ImageRelationshipType,
                expectedContentType: null,
                issues);
        }
    }

    private static void AddDrawingRelationshipPartIssue(
        ZipArchive archive,
        string drawingPart,
        string drawingRelationshipPart,
        string relationshipId,
        string description,
        string expectedRelationshipType,
        string? expectedContentType,
        List<string> issues)
    {
        if (!TryGetPackageRelationshipTarget(
                archive,
                drawingRelationshipPart,
                relationshipId,
                expectedRelationshipType,
                out var target,
                out var relationshipIssue))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId}: {relationshipIssue}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                drawingRelationshipPart,
                target!,
                out var packagePart,
                out var targetIssue))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!PackageEntryExists(archive, packagePart))
        {
            issues.Add($"{drawingPart} {description} reference {relationshipId} targets missing package part {packagePart}");
            return;
        }

        var contentTypeIssue = expectedContentType is null
            ? FindPackageContentTypePrefixIssue(archive, packagePart, "image/", "an image/* content type")
            : FindPackageContentTypeIssue(archive, packagePart, expectedContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);
    }

    private static string? FindPackageContentTypePrefixIssue(
        ZipArchive archive,
        string packagePart,
        string expectedContentTypePrefix,
        string expectedDescription)
    {
        var contentTypesEntry = FindPackageEntry(archive, "[Content_Types].xml");
        if (contentTypesEntry is null)
            return $"missing [Content_Types].xml for package content type assertion on {packagePart}";

        var actualContentType = GetEffectivePackageContentType(LoadPackageXml(contentTypesEntry), packagePart);
        if (actualContentType is null)
            return $"{packagePart} has no effective package content type";

        return actualContentType.StartsWith(expectedContentTypePrefix, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"{packagePart} has ContentType={actualContentType}; expected {expectedDescription}";
    }

    private static void ThrowInvalidWorksheetDrawingPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet drawing package graph: {sample}{suffix}");
    }

    private static void AssertWorksheetBackgroundImagePackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetRelationshipPart = GetRelationshipPartForPackagePart(worksheetPart);
            var backgroundPictureReferences = LoadPackageXml(worksheetEntry)
                .Descendants(SpreadsheetNs + "picture")
                .Select((picture, index) => new WorksheetPictureReference(
                    index + 1,
                    picture.Attribute(OfficeRelationshipNs + "id")?.Value))
                .ToArray();

            foreach (var backgroundPictureReference in backgroundPictureReferences)
            {
                if (string.IsNullOrWhiteSpace(backgroundPictureReference.RelationshipId))
                {
                    issues.Add($"{worksheetPart} background picture #{backgroundPictureReference.Ordinal} has no relationship id");
                    continue;
                }

                AddDrawingRelationshipPartIssue(
                    archive,
                    worksheetPart,
                    worksheetRelationshipPart,
                    backgroundPictureReference.RelationshipId,
                    $"background picture #{backgroundPictureReference.Ordinal}",
                    ImageRelationshipType,
                    expectedContentType: null,
                    issues);
            }
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetBackgroundImagePackage(label, sourcePath, issues);
    }

    private static void ThrowInvalidWorksheetBackgroundImagePackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet background image package graph: {sample}{suffix}");
    }

    private static void AssertWorksheetPrinterSettingsPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var validatedRelationships = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validatedPackageParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetRelationshipPart = GetRelationshipPartForPackagePart(worksheetPart);
            var worksheetXml = LoadPackageXml(worksheetEntry);
            var referencedRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var pageSetupReferences = worksheetXml
                .Descendants(SpreadsheetNs + "pageSetup")
                .Select((pageSetup, index) => new WorksheetPrinterSettingsReference(
                    index + 1,
                    pageSetup.Attribute(OfficeRelationshipNs + "id")?.Value))
                .Where(reference => reference.RelationshipId is not null)
                .ToArray();
            foreach (var pageSetupReference in pageSetupReferences)
            {
                if (string.IsNullOrWhiteSpace(pageSetupReference.RelationshipId))
                {
                    issues.Add($"{worksheetPart} pageSetup #{pageSetupReference.Ordinal} has an empty printer settings relationship id");
                    continue;
                }

                referencedRelationshipIds.Add(pageSetupReference.RelationshipId);
                AddWorksheetPrinterSettingsRelationshipIssues(
                    archive,
                    worksheetPart,
                    worksheetRelationshipPart,
                    $"pageSetup #{pageSetupReference.Ordinal}",
                    pageSetupReference.RelationshipId,
                    validatedRelationships,
                    validatedPackageParts,
                    issues);
            }

            foreach (var printerSettingsRelationship in FindPackageRelationshipsByType(
                         archive,
                         worksheetRelationshipPart,
                         PrinterSettingsRelationshipType))
            {
                var relationshipId = printerSettingsRelationship.Attribute("Id")?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    issues.Add($"{worksheetRelationshipPart} has a printerSettings relationship without Id");
                    continue;
                }

                if (referencedRelationshipIds.Contains(relationshipId))
                    continue;

                AddWorksheetPrinterSettingsRelationshipIssues(
                    archive,
                    worksheetPart,
                    worksheetRelationshipPart,
                    "printerSettings relationship",
                    relationshipId,
                    validatedRelationships,
                    validatedPackageParts,
                    issues);
            }
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetPrinterSettingsPackage(label, sourcePath, issues);
    }

    private static void AddWorksheetPrinterSettingsRelationshipIssues(
        ZipArchive archive,
        string worksheetPart,
        string worksheetRelationshipPart,
        string referenceDescription,
        string relationshipId,
        HashSet<string> validatedRelationships,
        HashSet<string> validatedPackageParts,
        List<string> issues)
    {
        var relationshipKey = $"{NormalizePackagePart(worksheetRelationshipPart)}|{relationshipId}";
        if (!validatedRelationships.Add(relationshipKey))
            return;

        var relationship = FindPackageRelationshipById(
            archive,
            worksheetRelationshipPart,
            relationshipId,
            out var relationshipIssue);
        if (relationship is null)
        {
            issues.Add($"{worksheetPart} {referenceDescription} reference {relationshipId}: {relationshipIssue}");
            return;
        }

        if (!string.Equals(relationship.Attribute("Type")?.Value, PrinterSettingsRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} in {worksheetRelationshipPart} has Type={relationship.Attribute("Type")?.Value}; expected {PrinterSettingsRelationshipType}");
            return;
        }

        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} in {worksheetRelationshipPart} is external");
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} in {worksheetRelationshipPart} has invalid TargetMode {targetMode}");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} in {worksheetRelationshipPart} has no Target");
            return;
        }

        target = target.Trim();
        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} in {worksheetRelationshipPart} targets external URI without TargetMode=External: {target}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                worksheetRelationshipPart,
                target,
                out var printerSettingsPart,
                out var targetIssue))
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} in {worksheetRelationshipPart} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!IsPrinterSettingsPart(printerSettingsPart))
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} targets {printerSettingsPart}, which is not an xl/printerSettings binary part");

        var contentTypeIssue = FindPackageContentTypeIssue(archive, printerSettingsPart, PrinterSettingsContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var printerSettingsEntry = FindPackageEntry(archive, printerSettingsPart);
        if (printerSettingsEntry is null)
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} targets missing package part {printerSettingsPart}");
            return;
        }

        if (!validatedPackageParts.Add(printerSettingsPart))
            return;

        if (printerSettingsEntry.Length == 0)
            issues.Add($"{printerSettingsPart} is an empty printer settings binary part");
    }

    private static bool IsPrinterSettingsPart(string packagePart)
    {
        packagePart = NormalizePackagePart(packagePart);
        return packagePart.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase) &&
            packagePart.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowInvalidWorksheetPrinterSettingsPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet printer settings package graph: {sample}{suffix}");
    }

    private static void AssertLegacyCommentPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var validatedCommentParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validatedVmlDrawingParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetRelationshipPart = GetRelationshipPartForPackagePart(worksheetPart);
            var worksheetXml = LoadPackageXml(worksheetEntry);

            AddWorksheetCommentRelationshipIssues(
                archive,
                worksheetPart,
                worksheetRelationshipPart,
                validatedCommentParts,
                issues);

            AddWorksheetLegacyDrawingRelationshipIssues(
                archive,
                worksheetPart,
                worksheetRelationshipPart,
                worksheetXml,
                validatedVmlDrawingParts,
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidLegacyCommentPackage(label, sourcePath, issues);
    }

    private static void AddWorksheetCommentRelationshipIssues(
        ZipArchive archive,
        string worksheetPart,
        string worksheetRelationshipPart,
        HashSet<string> validatedCommentParts,
        List<string> issues)
    {
        foreach (var commentsRelationship in FindPackageRelationshipsByType(
                     archive,
                     worksheetRelationshipPart,
                     CommentsRelationshipType))
        {
            var relationshipId = commentsRelationship.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                issues.Add($"{worksheetRelationshipPart} has a comments relationship without Id");
                continue;
            }

            if (!TryGetPackageRelationshipTarget(
                    archive,
                    worksheetRelationshipPart,
                    relationshipId,
                    CommentsRelationshipType,
                    out var commentsTarget,
                    out var commentsRelationshipIssue))
            {
                issues.Add($"{worksheetPart} comments relationship {relationshipId}: {commentsRelationshipIssue}");
                continue;
            }

            if (!TryResolvePackageRelationshipTarget(
                    worksheetRelationshipPart,
                    commentsTarget!,
                    out var commentsPart,
                    out var commentsTargetIssue))
            {
                issues.Add($"{worksheetPart} comments relationship {relationshipId} has invalid Target {commentsTarget}: {commentsTargetIssue}");
                continue;
            }

            AddCommentsPartPackageIssues(archive, worksheetPart, commentsPart, validatedCommentParts, issues);
        }
    }

    private static void AddCommentsPartPackageIssues(
        ZipArchive archive,
        string worksheetPart,
        string commentsPart,
        HashSet<string> validatedCommentParts,
        List<string> issues)
    {
        var contentTypeIssue = FindPackageContentTypeIssue(archive, commentsPart, CommentsContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var commentsEntry = FindPackageEntry(archive, commentsPart);
        if (commentsEntry is null)
        {
            issues.Add($"{worksheetPart} comments relationship targets missing package part {commentsPart}");
            return;
        }

        if (!validatedCommentParts.Add(commentsPart))
            return;

        var commentsXml = LoadPackageXml(commentsEntry);
        if (commentsXml.Root?.Name != SpreadsheetNs + "comments")
        {
            issues.Add($"{commentsPart} has an invalid comments root element");
            return;
        }

        AddCommentListIssues(commentsPart, commentsXml, issues);
    }

    private static void AddCommentListIssues(
        string commentsPart,
        XDocument commentsXml,
        List<string> issues)
    {
        var authors = commentsXml.Root?
            .Element(SpreadsheetNs + "authors")?
            .Elements(SpreadsheetNs + "author")
            .ToArray() ?? [];
        var comments = commentsXml.Root?
            .Element(SpreadsheetNs + "commentList")?
            .Elements(SpreadsheetNs + "comment")
            .ToArray() ?? [];

        if (comments.Length == 0)
            return;

        if (authors.Length == 0)
        {
            issues.Add($"{commentsPart} has comments but no authors");
        }

        var seenCommentRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in comments)
        {
            var reference = comment.Attribute("ref")?.Value;
            if (string.IsNullOrWhiteSpace(reference))
            {
                issues.Add($"{commentsPart} has a comment without ref");
            }
            else if (!seenCommentRefs.Add(reference))
            {
                issues.Add($"{commentsPart} has duplicate comment ref {reference}");
            }

            var authorIdText = comment.Attribute("authorId")?.Value;
            if (!TryParseNonNegativePackageInt(authorIdText, out var authorId))
            {
                issues.Add($"{commentsPart} comment {reference ?? "(no ref)"} has invalid authorId '{authorIdText}'");
                continue;
            }

            if (authors.Length > 0 && authorId >= authors.Length)
                issues.Add($"{commentsPart} comment {reference ?? "(no ref)"} references authorId {authorId}, but only {authors.Length} author(s) exist");
        }
    }

    private static void AddWorksheetLegacyDrawingRelationshipIssues(
        ZipArchive archive,
        string worksheetPart,
        string worksheetRelationshipPart,
        XDocument worksheetXml,
        HashSet<string> validatedVmlDrawingParts,
        List<string> issues)
    {
        var referencedRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var legacyDrawingReferences = worksheetXml
            .Descendants()
            .Where(element =>
                element.Name == SpreadsheetNs + "legacyDrawing" ||
                element.Name == SpreadsheetNs + "legacyDrawingHF")
            .Select((legacyDrawing, index) => new LegacyDrawingReference(
                legacyDrawing.Name.LocalName,
                index + 1,
                legacyDrawing.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();

        foreach (var legacyDrawingReference in legacyDrawingReferences)
        {
            if (string.IsNullOrWhiteSpace(legacyDrawingReference.RelationshipId))
            {
                issues.Add($"{worksheetPart} {legacyDrawingReference.ElementName} #{legacyDrawingReference.Ordinal} has no relationship id");
                continue;
            }

            referencedRelationshipIds.Add(legacyDrawingReference.RelationshipId);
            AddVmlDrawingReferenceIssues(
                archive,
                worksheetPart,
                worksheetRelationshipPart,
                $"{legacyDrawingReference.ElementName} #{legacyDrawingReference.Ordinal}",
                legacyDrawingReference.RelationshipId,
                validatedVmlDrawingParts,
                issues);
        }

        foreach (var vmlDrawingRelationship in FindPackageRelationshipsByType(
                     archive,
                     worksheetRelationshipPart,
                     VmlDrawingRelationshipType))
        {
            var relationshipId = vmlDrawingRelationship.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId) || referencedRelationshipIds.Contains(relationshipId))
                continue;

            AddVmlDrawingReferenceIssues(
                archive,
                worksheetPart,
                worksheetRelationshipPart,
                "vmlDrawing relationship",
                relationshipId,
                validatedVmlDrawingParts,
                issues);
        }
    }

    private static void AddVmlDrawingReferenceIssues(
        ZipArchive archive,
        string worksheetPart,
        string worksheetRelationshipPart,
        string referenceDescription,
        string relationshipId,
        HashSet<string> validatedVmlDrawingParts,
        List<string> issues)
    {
        if (!TryGetPackageRelationshipTarget(
                archive,
                worksheetRelationshipPart,
                relationshipId,
                VmlDrawingRelationshipType,
                out var vmlDrawingTarget,
                out var vmlDrawingRelationshipIssue))
        {
            issues.Add($"{worksheetPart} {referenceDescription} reference {relationshipId}: {vmlDrawingRelationshipIssue}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                worksheetRelationshipPart,
                vmlDrawingTarget!,
                out var vmlDrawingPart,
                out var vmlDrawingTargetIssue))
        {
            issues.Add($"{worksheetPart} {referenceDescription} reference {relationshipId} has invalid Target {vmlDrawingTarget}: {vmlDrawingTargetIssue}");
            return;
        }

        var contentTypeIssue = FindPackageContentTypeIssue(archive, vmlDrawingPart, VmlDrawingContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var vmlDrawingEntry = FindPackageEntry(archive, vmlDrawingPart);
        if (vmlDrawingEntry is null)
        {
            issues.Add($"{worksheetPart} {referenceDescription} reference {relationshipId} targets missing package part {vmlDrawingPart}");
            return;
        }

        if (!validatedVmlDrawingParts.Add(vmlDrawingPart))
            return;

        var vmlDrawingXml = LoadPackageXml(vmlDrawingEntry);
        if (!string.Equals(vmlDrawingXml.Root?.Name.LocalName, "xml", StringComparison.Ordinal))
        {
            issues.Add($"{vmlDrawingPart} has an invalid VML drawing root element");
            return;
        }

        AddVmlImageRelationshipIssues(archive, vmlDrawingPart, vmlDrawingXml, issues);
    }

    private static void AddVmlImageRelationshipIssues(
        ZipArchive archive,
        string vmlDrawingPart,
        XDocument vmlDrawingXml,
        List<string> issues)
    {
        var relationshipPart = GetRelationshipPartForPackagePart(vmlDrawingPart);
        foreach (var imageRelationshipId in vmlDrawingXml
                     .Descendants(VmlNs + "imagedata")
                     .SelectMany(GetVmlImageRelationshipIds)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddDrawingRelationshipPartIssue(
                archive,
                vmlDrawingPart,
                relationshipPart,
                imageRelationshipId,
                "VML image",
                ImageRelationshipType,
                expectedContentType: null,
                issues);
        }
    }

    private static IEnumerable<string> GetVmlImageRelationshipIds(XElement imageData)
    {
        var relationshipId = imageData.Attribute(OfficeRelationshipNs + "id")?.Value;
        if (!string.IsNullOrWhiteSpace(relationshipId))
            yield return relationshipId;

        var officeRelationshipId = imageData.Attribute(VmlOfficeNs + "relid")?.Value;
        if (!string.IsNullOrWhiteSpace(officeRelationshipId))
            yield return officeRelationshipId;
    }

    private static void ThrowInvalidLegacyCommentPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid legacy comments/VML package graph: {sample}{suffix}");
    }

    private static void AssertWorksheetTablePackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetRelationshipPart = GetRelationshipPartForPackagePart(worksheetPart);
            var worksheetXml = LoadPackageXml(worksheetEntry);

            foreach (var tableParts in worksheetXml.Descendants(SpreadsheetNs + "tableParts"))
            {
                var tablePartReferences = tableParts
                    .Elements(SpreadsheetNs + "tablePart")
                    .Select((tablePart, index) => new TablePartReference(
                        index + 1,
                        tablePart.Attribute(OfficeRelationshipNs + "id")?.Value))
                    .ToArray();

                AddTablePartsCountAttributeIssues(issues, worksheetPart, tableParts, tablePartReferences.Length);
                if (tablePartReferences.Length == 0)
                    continue;

                foreach (var tablePartReference in tablePartReferences)
                {
                    if (string.IsNullOrWhiteSpace(tablePartReference.RelationshipId))
                    {
                        issues.Add($"{worksheetPart} tablePart #{tablePartReference.Ordinal} has no relationship id");
                        continue;
                    }

                    if (!TryGetPackageRelationshipTarget(
                            archive,
                            worksheetRelationshipPart,
                            tablePartReference.RelationshipId,
                            TableRelationshipType,
                            out var tableTarget,
                            out var tableRelationshipIssue))
                    {
                        issues.Add($"{worksheetPart} tablePart #{tablePartReference.Ordinal} reference {tablePartReference.RelationshipId}: {tableRelationshipIssue}");
                        continue;
                    }

                    if (!TryResolvePackageRelationshipTarget(
                            worksheetRelationshipPart,
                            tableTarget!,
                            out var tablePart,
                            out var tableTargetIssue))
                    {
                        issues.Add($"{worksheetPart} tablePart #{tablePartReference.Ordinal} reference {tablePartReference.RelationshipId} has invalid Target {tableTarget}: {tableTargetIssue}");
                        continue;
                    }

                    var contentTypeIssue = FindPackageContentTypeIssue(archive, tablePart, TableContentType);
                    if (contentTypeIssue is not null)
                        issues.Add(contentTypeIssue);

                    var tableEntry = FindPackageEntry(archive, tablePart);
                    if (tableEntry is null)
                    {
                        issues.Add($"{worksheetPart} tablePart #{tablePartReference.Ordinal} reference {tablePartReference.RelationshipId} targets missing package part {tablePart}");
                        continue;
                    }

                    var tableXml = LoadPackageXml(tableEntry);
                    if (tableXml.Root?.Name != SpreadsheetNs + "table")
                        issues.Add($"{tablePart} has an invalid table root element");
                }
            }
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetTablePackage(label, sourcePath, issues);
    }

    private static void AddTablePartsCountAttributeIssues(
        List<string> issues,
        string worksheetPart,
        XElement tableParts,
        int actualCount)
    {
        var countText = tableParts.Attribute("count")?.Value;
        if (string.IsNullOrWhiteSpace(countText))
            return;

        if (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredCount))
        {
            issues.Add($"{worksheetPart} tableParts has invalid count '{countText}'");
            return;
        }

        if (declaredCount != actualCount)
            issues.Add($"{worksheetPart} tableParts count is {declaredCount}, but contains {actualCount} tablePart entries");
    }

    private static void ThrowInvalidWorksheetTablePackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet table package graph: {sample}{suffix}");
    }

    private static void AssertPivotPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var pivotCacheReferences = FindWorkbookPivotCacheReferences(archive, issues);

        AddWorksheetPivotTablePackageIssues(archive, pivotCacheReferences, issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidPivotPackage(label, sourcePath, issues);
    }

    private static Dictionary<int, PivotCacheReference> FindWorkbookPivotCacheReferences(
        ZipArchive archive,
        List<string> issues)
    {
        var references = new Dictionary<int, PivotCacheReference>();
        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is null)
            return references;

        var pivotCaches = LoadPackageXml(workbookEntry)
            .Descendants(SpreadsheetNs + "pivotCache")
            .Select((pivotCache, index) => new
            {
                Ordinal = index + 1,
                CacheId = pivotCache.Attribute("cacheId")?.Value,
                RelationshipId = pivotCache.Attribute(OfficeRelationshipNs + "id")?.Value
            })
            .ToArray();
        if (pivotCaches.Length == 0)
            return references;

        foreach (var pivotCache in pivotCaches)
        {
            if (!TryParseNonNegativePackageInt(pivotCache.CacheId, out var cacheId))
            {
                issues.Add($"workbook pivotCache #{pivotCache.Ordinal} has invalid cacheId '{pivotCache.CacheId}'");
                continue;
            }

            if (references.ContainsKey(cacheId))
            {
                issues.Add($"workbook pivotCache #{pivotCache.Ordinal} duplicates cacheId {cacheId}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(pivotCache.RelationshipId))
            {
                issues.Add($"workbook pivotCache #{pivotCache.Ordinal} has no relationship id");
                continue;
            }

            if (!TryGetPackageRelationshipTarget(
                    archive,
                    WorkbookRelationshipPart,
                    pivotCache.RelationshipId,
                    PivotCacheDefinitionRelationshipType,
                    out var cacheDefinitionTarget,
                    out var cacheRelationshipIssue))
            {
                issues.Add($"workbook pivotCache #{pivotCache.Ordinal} reference {pivotCache.RelationshipId}: {cacheRelationshipIssue}");
                continue;
            }

            if (!TryResolvePackageRelationshipTarget(
                    WorkbookRelationshipPart,
                    cacheDefinitionTarget!,
                    out var cacheDefinitionPart,
                    out var cacheTargetIssue))
            {
                issues.Add($"workbook pivotCache #{pivotCache.Ordinal} reference {pivotCache.RelationshipId} has invalid Target {cacheDefinitionTarget}: {cacheTargetIssue}");
                continue;
            }

            AddPivotCacheDefinitionPackageIssues(archive, cacheDefinitionPart, issues);
            references[cacheId] = new PivotCacheReference(pivotCache.Ordinal, cacheId, cacheDefinitionPart);
        }

        return references;
    }

    private static void AddPivotCacheDefinitionPackageIssues(
        ZipArchive archive,
        string cacheDefinitionPart,
        List<string> issues)
    {
        var contentTypeIssue = FindPackageContentTypeIssue(archive, cacheDefinitionPart, PivotCacheDefinitionContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var cacheDefinitionEntry = FindPackageEntry(archive, cacheDefinitionPart);
        if (cacheDefinitionEntry is null)
        {
            issues.Add($"workbook pivot cache targets missing package part {cacheDefinitionPart}");
            return;
        }

        var cacheDefinitionXml = LoadPackageXml(cacheDefinitionEntry);
        if (cacheDefinitionXml.Root?.Name != SpreadsheetNs + "pivotCacheDefinition")
        {
            issues.Add($"{cacheDefinitionPart} has an invalid pivot cache definition root element");
            return;
        }

        AddPivotCacheRecordsPackageIssues(archive, cacheDefinitionPart, cacheDefinitionXml, issues);
    }

    private static void AddPivotCacheRecordsPackageIssues(
        ZipArchive archive,
        string cacheDefinitionPart,
        XDocument cacheDefinitionXml,
        List<string> issues)
    {
        var recordsRelationshipId = cacheDefinitionXml.Root?.Attribute(OfficeRelationshipNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(recordsRelationshipId))
            return;

        var cacheDefinitionRelationshipPart = GetRelationshipPartForPackagePart(cacheDefinitionPart);
        if (!TryGetPackageRelationshipTarget(
                archive,
                cacheDefinitionRelationshipPart,
                recordsRelationshipId,
                PivotCacheRecordsRelationshipType,
                out var recordsTarget,
                out var recordsRelationshipIssue))
        {
            issues.Add($"{cacheDefinitionPart} pivot cache records reference {recordsRelationshipId}: {recordsRelationshipIssue}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                cacheDefinitionRelationshipPart,
                recordsTarget!,
                out var recordsPart,
                out var recordsTargetIssue))
        {
            issues.Add($"{cacheDefinitionPart} pivot cache records reference {recordsRelationshipId} has invalid Target {recordsTarget}: {recordsTargetIssue}");
            return;
        }

        var contentTypeIssue = FindPackageContentTypeIssue(archive, recordsPart, PivotCacheRecordsContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var recordsEntry = FindPackageEntry(archive, recordsPart);
        if (recordsEntry is null)
        {
            issues.Add($"{cacheDefinitionPart} pivot cache records reference {recordsRelationshipId} targets missing package part {recordsPart}");
            return;
        }

        var recordsXml = LoadPackageXml(recordsEntry);
        if (recordsXml.Root?.Name != SpreadsheetNs + "pivotCacheRecords")
            issues.Add($"{recordsPart} has an invalid pivot cache records root element");
    }

    private static void AddWorksheetPivotTablePackageIssues(
        ZipArchive archive,
        IReadOnlyDictionary<int, PivotCacheReference> pivotCacheReferences,
        List<string> issues)
    {
        var validatedPivotTableParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetRelationshipPart = GetRelationshipPartForPackagePart(worksheetPart);
            var worksheetXml = LoadPackageXml(worksheetEntry);
            var referencedRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pivotTableReferences = worksheetXml
                .Descendants(SpreadsheetNs + "pivotTableDefinition")
                .Select((pivotTableDefinition, index) => new PivotTablePartReference(
                    index + 1,
                    pivotTableDefinition.Attribute(OfficeRelationshipNs + "id")?.Value))
                .ToArray();

            foreach (var pivotTableReference in pivotTableReferences)
            {
                if (string.IsNullOrWhiteSpace(pivotTableReference.RelationshipId))
                {
                    issues.Add($"{worksheetPart} pivotTableDefinition #{pivotTableReference.Ordinal} has no relationship id");
                    continue;
                }

                referencedRelationshipIds.Add(pivotTableReference.RelationshipId);
                AddWorksheetPivotTableReferenceIssues(
                    archive,
                    worksheetPart,
                    worksheetRelationshipPart,
                    $"pivotTableDefinition #{pivotTableReference.Ordinal}",
                    pivotTableReference.RelationshipId,
                    pivotCacheReferences,
                    validatedPivotTableParts,
                    issues);
            }

            foreach (var pivotTableRelationship in FindPackageRelationshipsByType(
                         archive,
                         worksheetRelationshipPart,
                         PivotTableRelationshipType))
            {
                var relationshipId = pivotTableRelationship.Attribute("Id")?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId) || referencedRelationshipIds.Contains(relationshipId))
                    continue;

                AddWorksheetPivotTableReferenceIssues(
                    archive,
                    worksheetPart,
                    worksheetRelationshipPart,
                    "pivotTable relationship",
                    relationshipId,
                    pivotCacheReferences,
                    validatedPivotTableParts,
                    issues);
            }
        }
    }

    private static void AddWorksheetPivotTableReferenceIssues(
        ZipArchive archive,
        string worksheetPart,
        string worksheetRelationshipPart,
        string referenceDescription,
        string relationshipId,
        IReadOnlyDictionary<int, PivotCacheReference> pivotCacheReferences,
        HashSet<string> validatedPivotTableParts,
        List<string> issues)
    {
        if (!TryGetPackageRelationshipTarget(
                archive,
                worksheetRelationshipPart,
                relationshipId,
                PivotTableRelationshipType,
                out var pivotTableTarget,
                out var pivotTableRelationshipIssue))
        {
            issues.Add($"{worksheetPart} {referenceDescription} reference {relationshipId}: {pivotTableRelationshipIssue}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                worksheetRelationshipPart,
                pivotTableTarget!,
                out var pivotTablePart,
                out var pivotTableTargetIssue))
        {
            issues.Add($"{worksheetPart} {referenceDescription} reference {relationshipId} has invalid Target {pivotTableTarget}: {pivotTableTargetIssue}");
            return;
        }

        var contentTypeIssue = FindPackageContentTypeIssue(archive, pivotTablePart, PivotTableContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var pivotTableEntry = FindPackageEntry(archive, pivotTablePart);
        if (pivotTableEntry is null)
        {
            issues.Add($"{worksheetPart} {referenceDescription} reference {relationshipId} targets missing package part {pivotTablePart}");
            return;
        }

        if (!validatedPivotTableParts.Add(pivotTablePart))
            return;

        var pivotTableXml = LoadPackageXml(pivotTableEntry);
        if (pivotTableXml.Root?.Name != SpreadsheetNs + "pivotTableDefinition")
        {
            issues.Add($"{pivotTablePart} has an invalid pivot table definition root element");
            return;
        }

        AddPivotTableCachePackageIssues(archive, pivotTablePart, pivotTableXml, pivotCacheReferences, issues);
    }

    private static void AddPivotTableCachePackageIssues(
        ZipArchive archive,
        string pivotTablePart,
        XDocument pivotTableXml,
        IReadOnlyDictionary<int, PivotCacheReference> pivotCacheReferences,
        List<string> issues)
    {
        var cacheIdText = pivotTableXml.Root?.Attribute("cacheId")?.Value;
        if (!TryParseNonNegativePackageInt(cacheIdText, out var cacheId))
        {
            issues.Add($"{pivotTablePart} has invalid cacheId '{cacheIdText}'");
            return;
        }

        if (!pivotCacheReferences.TryGetValue(cacheId, out var pivotCacheReference))
        {
            issues.Add($"{pivotTablePart} references cacheId {cacheId}, but workbook has no matching pivotCache");
            return;
        }

        AddPivotTableCacheDefinitionRelationshipIssues(archive, pivotTablePart, pivotCacheReference, issues);
    }

    private static void AddPivotTableCacheDefinitionRelationshipIssues(
        ZipArchive archive,
        string pivotTablePart,
        PivotCacheReference pivotCacheReference,
        List<string> issues)
    {
        var pivotTableRelationshipPart = GetRelationshipPartForPackagePart(pivotTablePart);
        var relationshipEntry = FindPackageEntry(archive, pivotTableRelationshipPart);
        if (relationshipEntry is null)
        {
            issues.Add($"{pivotTablePart} has no relationship part for pivot cache definition");
            return;
        }

        var cacheDefinitionRelationships = LoadPackageXml(relationshipEntry)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => string.Equals(
                relationship.Attribute("Type")?.Value,
                PivotCacheDefinitionRelationshipType,
                StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        if (cacheDefinitionRelationships.Length == 0)
        {
            issues.Add($"{pivotTablePart} has no pivot cache definition relationship");
            return;
        }

        foreach (var cacheDefinitionRelationship in cacheDefinitionRelationships)
        {
            var relationshipId = cacheDefinitionRelationship.Attribute("Id")?.Value ?? "(no Id)";
            var target = cacheDefinitionRelationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
            {
                issues.Add($"{pivotTablePart} pivot cache definition relationship {relationshipId} has no Target");
                continue;
            }

            if (!TryResolvePackageRelationshipTarget(
                    pivotTableRelationshipPart,
                    target,
                    out var cacheDefinitionPart,
                    out var targetIssue))
            {
                issues.Add($"{pivotTablePart} pivot cache definition relationship {relationshipId} has invalid Target {target}: {targetIssue}");
                continue;
            }

            if (!string.Equals(cacheDefinitionPart, pivotCacheReference.PackagePart, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"{pivotTablePart} pivot cache definition relationship {relationshipId} targets {cacheDefinitionPart}, but cacheId {pivotCacheReference.CacheId} resolves to {pivotCacheReference.PackagePart}");
            }
        }
    }

    private static XElement[] FindPackageRelationshipsByType(
        ZipArchive archive,
        string relationshipPart,
        string relationshipType)
    {
        var relationshipEntry = FindPackageEntry(archive, relationshipPart);
        if (relationshipEntry is null)
            return [];

        return LoadPackageXml(relationshipEntry)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => string.Equals(
                relationship.Attribute("Type")?.Value,
                relationshipType,
                StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
    }

    private static bool TryParseNonNegativePackageInt(string? text, out int value)
    {
        value = -1;
        return !string.IsNullOrWhiteSpace(text) &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
            value >= 0;
    }

    private static void ThrowInvalidPivotPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid pivot package graph: {sample}{suffix}");
    }

    private static void AssertExternalLinkPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is null)
            return;

        var externalReferences = LoadPackageXml(workbookEntry)
            .Descendants(SpreadsheetNs + "externalReference")
            .Select((externalReference, index) => new WorkbookExternalReference(
                index + 1,
                externalReference.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();
        if (externalReferences.Length == 0)
            return;

        var issues = new List<string>();
        var workbookRelationshipEntry = FindPackageEntry(archive, WorkbookRelationshipPart);
        if (workbookRelationshipEntry is null)
        {
            issues.Add($"missing {WorkbookRelationshipPart} for workbook external link graph");
            ThrowInvalidExternalLinkPackage(label, sourcePath, issues);
            return;
        }

        var workbookRelationships = LoadPackageXml(workbookRelationshipEntry)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .ToArray() ?? [];
        var validatedExternalLinkParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var externalReference in externalReferences)
        {
            if (string.IsNullOrWhiteSpace(externalReference.RelationshipId))
            {
                issues.Add($"workbook externalReference #{externalReference.Ordinal} has no relationship id");
                continue;
            }

            var relationship = workbookRelationships.FirstOrDefault(relationship =>
                string.Equals(
                    relationship.Attribute("Id")?.Value,
                    externalReference.RelationshipId,
                    StringComparison.OrdinalIgnoreCase));
            if (relationship is null)
            {
                issues.Add($"workbook externalReference #{externalReference.Ordinal} targets missing relationship {externalReference.RelationshipId} in {WorkbookRelationshipPart}");
                continue;
            }

            AddWorkbookExternalReferencePackageIssues(
                archive,
                externalReference,
                relationship,
                validatedExternalLinkParts,
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidExternalLinkPackage(label, sourcePath, issues);
    }

    private static void AddWorkbookExternalReferencePackageIssues(
        ZipArchive archive,
        WorkbookExternalReference externalReference,
        XElement relationship,
        HashSet<string> validatedExternalLinkParts,
        List<string> issues)
    {
        if (!string.Equals(relationship.Attribute("Type")?.Value, ExternalLinkRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} has Type={relationship.Attribute("Type")?.Value}; expected {ExternalLinkRelationshipType}");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} has no Target");
            return;
        }

        target = target.Trim();
        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} has invalid TargetMode {targetMode}");
            return;
        }

        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} targets external URI without TargetMode=External: {target}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                WorkbookRelationshipPart,
                target,
                out var externalLinkPart,
                out var targetIssue))
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} has invalid Target {target}: {targetIssue}");
            return;
        }

        var contentTypeIssue = FindPackageContentTypeIssue(archive, externalLinkPart, ExternalLinkContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var externalLinkEntry = FindPackageEntry(archive, externalLinkPart);
        if (externalLinkEntry is null)
        {
            issues.Add($"workbook externalReference #{externalReference.Ordinal} relationship {externalReference.RelationshipId} targets missing package part {externalLinkPart}");
            return;
        }

        if (!validatedExternalLinkParts.Add(externalLinkPart))
            return;

        var externalLinkXml = LoadPackageXml(externalLinkEntry);
        if (externalLinkXml.Root?.Name != SpreadsheetNs + "externalLink")
        {
            issues.Add($"{externalLinkPart} has an invalid external link root element");
            return;
        }

        AddExternalBookPackageIssues(archive, externalLinkPart, externalLinkXml, issues);
    }

    private static void AddExternalBookPackageIssues(
        ZipArchive archive,
        string externalLinkPart,
        XDocument externalLinkXml,
        List<string> issues)
    {
        var externalBooks = externalLinkXml
            .Descendants(SpreadsheetNs + "externalBook")
            .Select((externalBook, index) => new ExternalBookReference(
                index + 1,
                externalBook.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();
        if (externalBooks.Length == 0)
            return;

        var relationshipPart = GetRelationshipPartForPackagePart(externalLinkPart);
        var relationshipEntry = FindPackageEntry(archive, relationshipPart);
        if (relationshipEntry is null)
        {
            issues.Add($"{externalLinkPart} has no relationship part for externalBook references");
            return;
        }

        var relationships = LoadPackageXml(relationshipEntry)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .ToArray() ?? [];
        foreach (var externalBook in externalBooks)
        {
            if (string.IsNullOrWhiteSpace(externalBook.RelationshipId))
            {
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} has no relationship id");
                continue;
            }

            var relationship = relationships.FirstOrDefault(relationship =>
                string.Equals(
                    relationship.Attribute("Id")?.Value,
                    externalBook.RelationshipId,
                    StringComparison.OrdinalIgnoreCase));
            if (relationship is null)
            {
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} targets missing relationship {externalBook.RelationshipId} in {relationshipPart}");
                continue;
            }

            if (!string.Equals(relationship.Attribute("Type")?.Value, ExternalLinkPathRelationshipType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} relationship {externalBook.RelationshipId} has Type={relationship.Attribute("Type")?.Value}; expected {ExternalLinkPathRelationshipType}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(relationship.Attribute("Target")?.Value))
            {
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} relationship {externalBook.RelationshipId} has no Target");
                continue;
            }

            if (!string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{externalLinkPart} externalBook #{externalBook.Ordinal} relationship {externalBook.RelationshipId} is not external");
            }
        }
    }

    private static void ThrowInvalidExternalLinkPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid external link package graph: {sample}{suffix}");
    }

    private static void AssertCalcChainPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var calcChainRelationships = FindPackageRelationshipsByType(
            archive,
            WorkbookRelationshipPart,
            CalcChainRelationshipType);
        var standardCalcChainEntry = FindPackageEntry(archive, "xl/calcChain.xml");
        if (calcChainRelationships.Length == 0 && standardCalcChainEntry is null)
            return;

        var issues = new List<string>();
        var workbookSheetIds = FindWorkbookSheetIds(archive);
        var validatedCalcChainParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var calcChainRelationship in calcChainRelationships)
        {
            AddWorkbookCalcChainRelationshipIssues(
                archive,
                calcChainRelationship,
                workbookSheetIds,
                validatedCalcChainParts,
                issues);
        }

        if (standardCalcChainEntry is not null &&
            !validatedCalcChainParts.Contains("xl/calcChain.xml"))
        {
            issues.Add("xl/calcChain.xml is present without a workbook calcChain relationship");
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidCalcChainPackage(label, sourcePath, issues);
    }

    private static HashSet<int> FindWorkbookSheetIds(ZipArchive archive)
    {
        var sheetIds = new HashSet<int>();
        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is null)
            return sheetIds;

        foreach (var sheet in LoadPackageXml(workbookEntry).Descendants(SpreadsheetNs + "sheet"))
        {
            if (TryParseNonNegativePackageInt(sheet.Attribute("sheetId")?.Value, out var sheetId))
                sheetIds.Add(sheetId);
        }

        return sheetIds;
    }

    private static void AddWorkbookCalcChainRelationshipIssues(
        ZipArchive archive,
        XElement relationship,
        IReadOnlySet<int> workbookSheetIds,
        HashSet<string> validatedCalcChainParts,
        List<string> issues)
    {
        var relationshipId = relationship.Attribute("Id")?.Value;
        var relationshipLabel = $"workbook calcChain relationship {FormatRelationshipIssueId(relationshipId)}";
        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipLabel} is external");
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipLabel} has invalid TargetMode {targetMode}");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{relationshipLabel} has no Target");
            return;
        }

        target = target.Trim();
        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add($"{relationshipLabel} targets external URI without TargetMode=External: {target}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                WorkbookRelationshipPart,
                target,
                out var calcChainPart,
                out var targetIssue))
        {
            issues.Add($"{relationshipLabel} has invalid Target {target}: {targetIssue}");
            return;
        }

        var contentTypeIssue = FindPackageContentTypeIssue(archive, calcChainPart, CalcChainContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var calcChainEntry = FindPackageEntry(archive, calcChainPart);
        if (calcChainEntry is null)
        {
            issues.Add($"{relationshipLabel} targets missing package part {calcChainPart}");
            return;
        }

        if (!validatedCalcChainParts.Add(calcChainPart))
            return;

        var calcChainXml = LoadPackageXml(calcChainEntry);
        if (calcChainXml.Root?.Name != SpreadsheetNs + "calcChain")
        {
            issues.Add($"{calcChainPart} has an invalid calc-chain root element");
            return;
        }

        AddCalcChainCellIssues(calcChainPart, calcChainXml, workbookSheetIds, issues);
    }

    private static void AddCalcChainCellIssues(
        string calcChainPart,
        XDocument calcChainXml,
        IReadOnlySet<int> workbookSheetIds,
        List<string> issues)
    {
        var calcChainCells = calcChainXml.Root?
            .Elements(SpreadsheetNs + "c")
            .Select((cell, index) => (Cell: cell, Ordinal: index + 1)) ?? [];
        foreach (var (cell, ordinal) in calcChainCells)
        {
            var cellReference = cell.Attribute("r")?.Value;
            if (string.IsNullOrWhiteSpace(cellReference))
                issues.Add($"{calcChainPart} calc-chain cell #{ordinal} has no cell reference");

            var sheetIdText = cell.Attribute("i")?.Value;
            if (sheetIdText is null)
                continue;

            if (!TryParseNonNegativePackageInt(sheetIdText, out var sheetId))
            {
                issues.Add($"{calcChainPart} calc-chain cell {FormatCalcChainCellReference(cellReference, ordinal)} has invalid sheet id '{sheetIdText}'");
                continue;
            }

            if (workbookSheetIds.Count > 0 && !workbookSheetIds.Contains(sheetId))
            {
                issues.Add(
                    $"{calcChainPart} calc-chain cell {FormatCalcChainCellReference(cellReference, ordinal)} references sheet id {sheetId}, but the workbook has no matching sheet");
            }
        }
    }

    private static string FormatCalcChainCellReference(string? cellReference, int ordinal) =>
        string.IsNullOrWhiteSpace(cellReference)
            ? $"#{ordinal}"
            : cellReference;

    private static void ThrowInvalidCalcChainPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid calc-chain package graph: {sample}{suffix}");
    }

    private static void AssertCustomXmlPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var customXmlRelationships = FindPackageRelationshipsByType(archive, CustomXmlRelationshipType);
        var customXmlItemParts = FindCustomXmlItemParts(archive);
        if (customXmlRelationships.Count == 0 && customXmlItemParts.Count == 0)
            return;

        var issues = new List<string>();
        var validatedCustomXmlParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencedPropertiesParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationshipReference in customXmlRelationships)
        {
            AddCustomXmlRelationshipIssues(
                archive,
                relationshipReference,
                validatedCustomXmlParts,
                referencedPropertiesParts,
                issues);
        }

        foreach (var customXmlItemPart in customXmlItemParts)
        {
            if (!validatedCustomXmlParts.Contains(customXmlItemPart))
                issues.Add($"{customXmlItemPart} is present without a customXml relationship");
        }

        foreach (var propertiesPart in FindCustomXmlPropertiesParts(archive))
        {
            if (!referencedPropertiesParts.Contains(propertiesPart))
                issues.Add($"{propertiesPart} is present without a customXmlProps relationship");
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidCustomXmlPackage(label, sourcePath, issues);
    }

    private static List<PackageRelationshipReference> FindPackageRelationshipsByType(
        ZipArchive archive,
        string relationshipType)
    {
        var relationships = new List<PackageRelationshipReference>();
        foreach (var relationshipEntry in archive.Entries.Where(entry => IsPackageRelationshipPart(entry.FullName)))
        {
            var relationshipPart = NormalizePackagePart(relationshipEntry.FullName);
            foreach (var relationship in LoadPackageXml(relationshipEntry).Root?.Elements(PackageRelationshipNs + "Relationship") ?? [])
            {
                if (string.Equals(relationship.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase))
                    relationships.Add(new PackageRelationshipReference(relationshipPart, relationship));
            }
        }

        return relationships;
    }

    private static HashSet<string> FindCustomXmlItemParts(ZipArchive archive) =>
        archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .Where(IsCustomXmlItemPart)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> FindCustomXmlPropertiesParts(ZipArchive archive) =>
        archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .Where(IsCustomXmlPropertiesPart)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsCustomXmlItemPart(string part)
    {
        part = NormalizePackagePart(part);
        if (!part.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase) ||
            part.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
            !part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = part[(part.LastIndexOf('/') + 1)..];
        return !fileName.StartsWith("itemProps", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCustomXmlPropertiesPart(string part)
    {
        part = NormalizePackagePart(part);
        if (!part.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase) ||
            part.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) ||
            !part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = part[(part.LastIndexOf('/') + 1)..];
        return fileName.StartsWith("itemProps", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCustomXmlRelationshipIssues(
        ZipArchive archive,
        PackageRelationshipReference relationshipReference,
        HashSet<string> validatedCustomXmlParts,
        HashSet<string> referencedPropertiesParts,
        List<string> issues)
    {
        var relationship = relationshipReference.Relationship;
        var relationshipId = relationship.Attribute("Id")?.Value;
        var relationshipLabel =
            $"{relationshipReference.RelationshipPart} customXml relationship {FormatRelationshipIssueId(relationshipId)}";
        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipLabel} is external");
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipLabel} has invalid TargetMode {targetMode}");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{relationshipLabel} has no Target");
            return;
        }

        target = target.Trim();
        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add($"{relationshipLabel} targets external URI without TargetMode=External: {target}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                relationshipReference.RelationshipPart,
                target,
                out var customXmlPart,
                out var targetIssue))
        {
            issues.Add($"{relationshipLabel} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!IsCustomXmlItemPart(customXmlPart))
            issues.Add($"{relationshipLabel} targets {customXmlPart}, which is not a custom XML item part");

        var contentTypeIssue = FindPackageContentTypeIssue(archive, customXmlPart, CustomXmlContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var customXmlEntry = FindPackageEntry(archive, customXmlPart);
        if (customXmlEntry is null)
        {
            issues.Add($"{relationshipLabel} targets missing package part {customXmlPart}");
            return;
        }

        if (!validatedCustomXmlParts.Add(customXmlPart))
            return;

        var customXml = LoadPackageXml(customXmlEntry);
        if (customXml.Root is null)
            issues.Add($"{customXmlPart} has no custom XML root element");

        AddCustomXmlPropertiesRelationshipIssues(
            archive,
            customXmlPart,
            referencedPropertiesParts,
            issues);
    }

    private static void AddCustomXmlPropertiesRelationshipIssues(
        ZipArchive archive,
        string customXmlPart,
        HashSet<string> referencedPropertiesParts,
        List<string> issues)
    {
        var relationshipPart = GetRelationshipPartForPackagePart(customXmlPart);
        var relationshipEntry = FindPackageEntry(archive, relationshipPart);
        if (relationshipEntry is null)
        {
            issues.Add($"{customXmlPart} has no relationship part for custom XML properties");
            return;
        }

        var propertiesRelationships = LoadPackageXml(relationshipEntry)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(relationship => string.Equals(
                relationship.Attribute("Type")?.Value,
                CustomXmlPropertiesRelationshipType,
                StringComparison.OrdinalIgnoreCase))
            .ToArray() ?? [];
        if (propertiesRelationships.Length == 0)
        {
            issues.Add($"{customXmlPart} has no customXmlProps relationship in {relationshipPart}");
            return;
        }

        foreach (var propertiesRelationship in propertiesRelationships)
        {
            AddCustomXmlPropertiesPartIssues(
                archive,
                customXmlPart,
                relationshipPart,
                propertiesRelationship,
                referencedPropertiesParts,
                issues);
        }
    }

    private static void AddCustomXmlPropertiesPartIssues(
        ZipArchive archive,
        string customXmlPart,
        string relationshipPart,
        XElement relationship,
        HashSet<string> referencedPropertiesParts,
        List<string> issues)
    {
        var relationshipId = relationship.Attribute("Id")?.Value;
        var relationshipLabel =
            $"{customXmlPart} customXmlProps relationship {FormatRelationshipIssueId(relationshipId)}";
        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipLabel} is external");
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipLabel} has invalid TargetMode {targetMode}");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{relationshipLabel} has no Target");
            return;
        }

        target = target.Trim();
        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add($"{relationshipLabel} targets external URI without TargetMode=External: {target}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                relationshipPart,
                target,
                out var propertiesPart,
                out var targetIssue))
        {
            issues.Add($"{relationshipLabel} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!IsCustomXmlPropertiesPart(propertiesPart))
            issues.Add($"{relationshipLabel} targets {propertiesPart}, which is not a custom XML properties part");

        var contentTypeIssue = FindPackageContentTypeIssue(archive, propertiesPart, CustomXmlPropertiesContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var propertiesEntry = FindPackageEntry(archive, propertiesPart);
        if (propertiesEntry is null)
        {
            issues.Add($"{relationshipLabel} targets missing package part {propertiesPart}");
            return;
        }

        referencedPropertiesParts.Add(propertiesPart);
        var propertiesXml = LoadPackageXml(propertiesEntry);
        if (propertiesXml.Root?.Name != CustomXmlNs + "datastoreItem")
        {
            issues.Add($"{propertiesPart} has an invalid custom XML properties root element");
            return;
        }

        if (string.IsNullOrWhiteSpace(propertiesXml.Root.Attribute(CustomXmlNs + "itemID")?.Value))
            issues.Add($"{propertiesPart} datastoreItem has no itemID");
    }

    private static void ThrowInvalidCustomXmlPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid custom XML package graph: {sample}{suffix}");
    }

    private static void AssertSlicerTimelinePackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var validatedRelationships = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validatedPackageParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddWorkbookSlicerTimelineCacheIssues(archive, validatedRelationships, validatedPackageParts, issues);
        AddWorksheetSlicerTimelineReferenceIssues(archive, validatedRelationships, validatedPackageParts, issues);
        AddSlicerTimelineRelationshipPartIssues(archive, validatedRelationships, validatedPackageParts, issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidSlicerTimelinePackage(label, sourcePath, issues);
    }

    private static void AddWorkbookSlicerTimelineCacheIssues(
        ZipArchive archive,
        HashSet<string> validatedRelationships,
        HashSet<string> validatedPackageParts,
        List<string> issues)
    {
        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is null)
            return;

        var workbookXml = LoadPackageXml(workbookEntry);
        AddSlicerTimelineReferenceIssues(
            archive,
            WorkbookRelationshipPart,
            workbookXml.Descendants(SlicerNs + "slicerCache")
                .Select((element, index) => new SlicerTimelineRelationshipReference(
                    index + 1,
                    "workbook slicerCache",
                    element.Attribute(OfficeRelationshipNs + "id")?.Value)),
            SlicerCacheRelationshipType,
            SlicerCacheContentType,
            SlicerNs + "slicerCacheDefinition",
            "slicer cache",
            validatedRelationships,
            validatedPackageParts,
            issues);
        AddSlicerTimelineReferenceIssues(
            archive,
            WorkbookRelationshipPart,
            workbookXml.Descendants(TimelineNs + "timelineCacheRef")
                .Select((element, index) => new SlicerTimelineRelationshipReference(
                    index + 1,
                    "workbook timelineCacheRef",
                    element.Attribute(OfficeRelationshipNs + "id")?.Value)),
            TimelineCacheRelationshipType,
            TimelineCacheContentType,
            TimelineNs + "timelineCacheDefinition",
            "timeline cache",
            validatedRelationships,
            validatedPackageParts,
            issues);
    }

    private static void AddWorksheetSlicerTimelineReferenceIssues(
        ZipArchive archive,
        HashSet<string> validatedRelationships,
        HashSet<string> validatedPackageParts,
        List<string> issues)
    {
        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetRelationshipPart = GetRelationshipPartForPackagePart(worksheetPart);
            var worksheetXml = LoadPackageXml(worksheetEntry);
            AddSlicerTimelineReferenceIssues(
                archive,
                worksheetRelationshipPart,
                worksheetXml.Descendants(SlicerNs + "slicer")
                    .Select((element, index) => new SlicerTimelineRelationshipReference(
                        index + 1,
                        $"{worksheetPart} slicer",
                        element.Attribute(OfficeRelationshipNs + "id")?.Value)),
                SlicerRelationshipType,
                SlicerContentType,
                SlicerNs + "slicers",
                "slicer",
                validatedRelationships,
                validatedPackageParts,
                issues);
            AddSlicerTimelineReferenceIssues(
                archive,
                worksheetRelationshipPart,
                worksheetXml.Descendants(TimelineNs + "timelineRef")
                    .Select((element, index) => new SlicerTimelineRelationshipReference(
                        index + 1,
                        $"{worksheetPart} timelineRef",
                        element.Attribute(OfficeRelationshipNs + "id")?.Value)),
                TimelineRelationshipType,
                TimelineContentType,
                TimelineNs + "timelines",
                "timeline",
                validatedRelationships,
                validatedPackageParts,
                issues);
        }
    }

    private static void AddSlicerTimelineReferenceIssues(
        ZipArchive archive,
        string relationshipPart,
        IEnumerable<SlicerTimelineRelationshipReference> references,
        string expectedRelationshipType,
        string expectedContentType,
        XName expectedRootElement,
        string description,
        HashSet<string> validatedRelationships,
        HashSet<string> validatedPackageParts,
        List<string> issues)
    {
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.RelationshipId))
            {
                issues.Add($"{reference.Description} #{reference.Ordinal} has no relationship id");
                continue;
            }

            var relationship = FindPackageRelationshipById(
                archive,
                relationshipPart,
                reference.RelationshipId,
                out var relationshipIssue);
            if (relationship is null)
            {
                issues.Add($"{reference.Description} #{reference.Ordinal} reference {reference.RelationshipId}: {relationshipIssue}");
                continue;
            }

            if (!string.Equals(relationship.Attribute("Type")?.Value, expectedRelationshipType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{reference.Description} #{reference.Ordinal} relationship {reference.RelationshipId} in {relationshipPart} has Type={relationship.Attribute("Type")?.Value}; expected {expectedRelationshipType}");
                continue;
            }

            AddSlicerTimelineRelationshipIssues(
                archive,
                relationshipPart,
                relationship,
                $"{reference.Description} #{reference.Ordinal}",
                expectedContentType,
                expectedRootElement,
                description,
                validatedRelationships,
                validatedPackageParts,
                issues);
        }
    }

    private static XElement? FindPackageRelationshipById(
        ZipArchive archive,
        string relationshipPart,
        string relationshipId,
        out string? issue)
    {
        relationshipPart = NormalizePackagePart(relationshipPart);
        var entry = FindPackageEntry(archive, relationshipPart);
        if (entry is null)
        {
            issue = $"missing relationship part {relationshipPart}";
            return null;
        }

        var relationship = LoadPackageXml(entry)
            .Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .FirstOrDefault(relationship =>
                string.Equals(
                    relationship.Attribute("Id")?.Value,
                    relationshipId,
                    StringComparison.OrdinalIgnoreCase));
        issue = relationship is null
            ? $"targets missing relationship {relationshipId} in {relationshipPart}"
            : null;
        return relationship;
    }

    private static void AddSlicerTimelineRelationshipPartIssues(
        ZipArchive archive,
        HashSet<string> validatedRelationships,
        HashSet<string> validatedPackageParts,
        List<string> issues)
    {
        foreach (var relationshipEntry in archive.Entries.Where(entry => IsPackageRelationshipPart(entry.FullName)))
        {
            var relationshipPart = NormalizePackagePart(relationshipEntry.FullName);
            foreach (var relationship in LoadPackageXml(relationshipEntry).Root?.Elements(PackageRelationshipNs + "Relationship") ?? [])
            {
                var relationshipType = relationship.Attribute("Type")?.Value;
                if (string.Equals(relationshipType, SlicerRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    AddSlicerTimelineRelationshipIssues(
                        archive,
                        relationshipPart,
                        relationship,
                        $"{relationshipPart} slicer relationship",
                        SlicerContentType,
                        SlicerNs + "slicers",
                        "slicer",
                        validatedRelationships,
                        validatedPackageParts,
                        issues);
                }
                else if (string.Equals(relationshipType, SlicerCacheRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    AddSlicerTimelineRelationshipIssues(
                        archive,
                        relationshipPart,
                        relationship,
                        $"{relationshipPart} slicer cache relationship",
                        SlicerCacheContentType,
                        SlicerNs + "slicerCacheDefinition",
                        "slicer cache",
                        validatedRelationships,
                        validatedPackageParts,
                        issues);
                }
                else if (string.Equals(relationshipType, TimelineRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    AddSlicerTimelineRelationshipIssues(
                        archive,
                        relationshipPart,
                        relationship,
                        $"{relationshipPart} timeline relationship",
                        TimelineContentType,
                        TimelineNs + "timelines",
                        "timeline",
                        validatedRelationships,
                        validatedPackageParts,
                        issues);
                }
                else if (string.Equals(relationshipType, TimelineCacheRelationshipType, StringComparison.OrdinalIgnoreCase))
                {
                    AddSlicerTimelineRelationshipIssues(
                        archive,
                        relationshipPart,
                        relationship,
                        $"{relationshipPart} timeline cache relationship",
                        TimelineCacheContentType,
                        TimelineNs + "timelineCacheDefinition",
                        "timeline cache",
                        validatedRelationships,
                        validatedPackageParts,
                        issues);
                }
            }
        }
    }

    private static void AddSlicerTimelineRelationshipIssues(
        ZipArchive archive,
        string relationshipPart,
        XElement relationship,
        string referenceDescription,
        string expectedContentType,
        XName expectedRootElement,
        string packageDescription,
        HashSet<string> validatedRelationships,
        HashSet<string> validatedPackageParts,
        List<string> issues)
    {
        var relationshipId = relationship.Attribute("Id")?.Value ?? "(no Id)";
        var relationshipKey = $"{NormalizePackagePart(relationshipPart)}|{relationshipId}";
        if (!validatedRelationships.Add(relationshipKey))
            return;

        if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{referenceDescription} relationship {relationshipId} in {relationshipPart} is external");
            return;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{referenceDescription} relationship {relationshipId} in {relationshipPart} has no Target");
            return;
        }

        target = target.Trim();
        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add($"{referenceDescription} relationship {relationshipId} in {relationshipPart} targets external URI without TargetMode=External: {target}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(
                relationshipPart,
                target,
                out var packagePart,
                out var targetIssue))
        {
            issues.Add($"{referenceDescription} relationship {relationshipId} in {relationshipPart} has invalid Target {target}: {targetIssue}");
            return;
        }

        var contentTypeIssue = FindPackageContentTypeIssue(archive, packagePart, expectedContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var packageEntry = FindPackageEntry(archive, packagePart);
        if (packageEntry is null)
        {
            issues.Add($"{referenceDescription} relationship {relationshipId} in {relationshipPart} targets missing package part {packagePart}");
            return;
        }

        if (!validatedPackageParts.Add(packagePart))
            return;

        var packageXml = LoadPackageXml(packageEntry);
        if (packageXml.Root?.Name != expectedRootElement)
            issues.Add($"{packagePart} has an invalid {packageDescription} root element");
    }

    private static void ThrowInvalidSlicerTimelinePackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid slicer/timeline package graph: {sample}{suffix}");
    }

    private static void AssertPackageRelationshipsComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var entryNames = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var issues = new List<string>();

        foreach (var entry in archive.Entries.Where(entry => IsPackageRelationshipPart(entry.FullName)))
        {
            var relationshipPart = NormalizePackagePart(entry.FullName);
            if (!string.Equals(relationshipPart, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            {
                var ownerPart = GetRelationshipOwnerPart(relationshipPart);
                if (string.IsNullOrWhiteSpace(ownerPart) || !entryNames.Contains(ownerPart))
                {
                    issues.Add($"{relationshipPart} has no owning package part {ownerPart}");
                }
            }

            XDocument relationshipsXml;
            try
            {
                using var stream = entry.Open();
                relationshipsXml = XDocument.Load(stream);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException)
            {
                issues.Add($"{relationshipPart} is not parseable relationship XML: {ex.Message}");
                continue;
            }

            if (relationshipsXml.Root?.Name != PackageRelationshipNs + "Relationships")
            {
                issues.Add($"{relationshipPart} has an invalid Relationships root element");
                continue;
            }

            foreach (var element in relationshipsXml.Root.Elements())
            {
                if (element.Name != PackageRelationshipNs + "Relationship")
                    issues.Add($"{relationshipPart} has unexpected child element '{element.Name}'");
            }

            var relationships = relationshipsXml.Root
                .Elements(PackageRelationshipNs + "Relationship")
                .ToArray();
            if (relationships.Length == 0)
            {
                issues.Add($"{relationshipPart} has no Relationship elements");
                continue;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relationship in relationships)
            {
                ValidatePackageRelationship(relationshipPart, relationship, entryNames, ids, issues);
            }
        }

        if (issues.Count == 0)
            return;

        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid package relationship(s): {sample}{suffix}");
    }

    private static bool IsPackageRelationshipPart(string part)
    {
        var normalizedPart = NormalizePackagePart(part);
        return normalizedPart.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(normalizedPart, "_rels/.rels", StringComparison.OrdinalIgnoreCase) ||
                normalizedPart.Contains("/_rels/", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidatePackageRelationship(
        string relationshipPart,
        XElement relationship,
        IReadOnlySet<string> entryNames,
        HashSet<string> ids,
        List<string> issues)
    {
        var id = relationship.Attribute("Id")?.Value;
        var relationshipLabel = $"{relationshipPart} Relationship {FormatRelationshipIssueId(id)}";
        if (relationship.Elements().Any())
            issues.Add($"{relationshipLabel} must not contain child elements");

        if (string.IsNullOrWhiteSpace(id))
        {
            issues.Add($"{relationshipPart} has a Relationship without Id");
        }
        else if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
        {
            issues.Add($"{relationshipPart} Relationship Id '{id}' has leading or trailing whitespace");
        }
        else if (!ids.Add(id))
        {
            issues.Add($"{relationshipPart} has duplicate Relationship Id {id}");
        }

        var type = relationship.Attribute("Type")?.Value;
        if (string.IsNullOrWhiteSpace(type))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has no Type");
        }
        else
        {
            if (!string.Equals(type, type.Trim(), StringComparison.Ordinal))
                issues.Add($"{relationshipLabel} Type has leading or trailing whitespace");

            if (!Uri.TryCreate(type.Trim(), UriKind.Absolute, out var typeUri) ||
                string.IsNullOrWhiteSpace(typeUri.Scheme))
            {
                issues.Add($"{relationshipLabel} Type '{type}' is not an absolute URI");
            }
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has no Target");
            return;
        }

        if (!string.Equals(target, target.Trim(), StringComparison.Ordinal))
            issues.Add($"{relationshipLabel} Target has leading or trailing whitespace");
        target = target.Trim();

        var targetMode = relationship.Attribute("TargetMode")?.Value;
        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, targetMode.Trim(), StringComparison.Ordinal))
        {
            issues.Add($"{relationshipLabel} TargetMode has leading or trailing whitespace");
        }

        targetMode = targetMode?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has invalid TargetMode {targetMode}");
            return;
        }

        if (target.IndexOf('\\') >= 0)
            issues.Add($"{relationshipLabel} Target uses backslashes instead of package URI separators");

        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add(
                $"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} targets external URI without TargetMode=External: {target}");
            return;
        }

        if (!TryResolvePackageRelationshipTarget(relationshipPart, target, out var resolvedTarget, out var error))
        {
            issues.Add(
                $"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} has invalid Target {target}: {error}");
            return;
        }

        if (!entryNames.Contains(resolvedTarget))
        {
            issues.Add(
                $"{relationshipPart} Relationship {FormatRelationshipIssueId(id)} targets missing package part {resolvedTarget}");
        }
    }

    private static string FormatRelationshipIssueId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? "(no Id)" : id;

    private static bool IsAbsoluteRelationshipTarget(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
        !string.IsNullOrWhiteSpace(uri.Scheme);

    private static bool TryResolvePackageRelationshipTarget(
        string relationshipPart,
        string target,
        out string resolvedTarget,
        out string error)
    {
        resolvedTarget = string.Empty;
        error = string.Empty;

        target = StripRelationshipTargetFragment(target.Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "empty internal target";
            return false;
        }

        try
        {
            target = Uri.UnescapeDataString(target);
        }
        catch (UriFormatException ex)
        {
            error = ex.Message;
            return false;
        }

        string combined;
        if (target.StartsWith("/", StringComparison.Ordinal))
        {
            combined = target.TrimStart('/');
        }
        else
        {
            var ownerPart = GetRelationshipOwnerPart(relationshipPart);
            var ownerDirectory = ownerPart.Contains('/', StringComparison.Ordinal)
                ? ownerPart[..ownerPart.LastIndexOf('/')]
                : string.Empty;
            combined = string.IsNullOrWhiteSpace(ownerDirectory)
                ? target
                : $"{ownerDirectory}/{target}";
        }

        if (!TryNormalizePackagePathSegments(combined, out resolvedTarget))
        {
            error = "target escapes the package root";
            return false;
        }

        return !string.IsNullOrWhiteSpace(resolvedTarget);
    }

    private static string StripRelationshipTargetFragment(string target)
    {
        var fragmentIndex = target.IndexOf('#', StringComparison.Ordinal);
        var queryIndex = target.IndexOf('?', StringComparison.Ordinal);
        var endIndex = fragmentIndex < 0
            ? queryIndex
            : queryIndex < 0
                ? fragmentIndex
                : Math.Min(fragmentIndex, queryIndex);
        return endIndex < 0 ? target : target[..endIndex];
    }

    private static bool TryNormalizePackagePathSegments(string path, out string normalizedPath)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    normalizedPath = string.Empty;
                    return false;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        normalizedPath = NormalizePackagePart(string.Join("/", segments));
        return true;
    }

    private static string ResolvePackageRelationshipTarget(string relationshipPart, string target)
    {
        target = target.Replace('\\', '/');
        if (target.StartsWith("/", StringComparison.Ordinal))
            return NormalizePackagePart(target);

        var ownerPart = GetRelationshipOwnerPart(relationshipPart);
        var ownerDirectory = ownerPart.Contains('/', StringComparison.Ordinal)
            ? ownerPart[..ownerPart.LastIndexOf('/')]
            : string.Empty;
        var combined = string.IsNullOrWhiteSpace(ownerDirectory)
            ? target
            : $"{ownerDirectory}/{target}";
        return NormalizePackagePart(NormalizePackagePathSegments(combined));
    }

    private static string GetRelationshipOwnerPart(string relationshipPart)
    {
        relationshipPart = NormalizePackagePart(relationshipPart);
        if (string.Equals(relationshipPart, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        const string relationshipMarker = "/_rels/";
        var markerIndex = relationshipPart.LastIndexOf(relationshipMarker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return relationshipPart.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
                ? relationshipPart[..^".rels".Length]
                : relationshipPart;

        var directory = relationshipPart[..markerIndex];
        var fileName = relationshipPart[(markerIndex + relationshipMarker.Length)..];
        if (fileName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            fileName = fileName[..^".rels".Length];
        return string.IsNullOrWhiteSpace(directory)
            ? fileName
            : $"{directory}/{fileName}";
    }

    private static string GetRelationshipPartForPackagePart(string packagePart)
    {
        packagePart = NormalizePackagePart(packagePart);
        var directorySeparator = packagePart.LastIndexOf('/');
        return directorySeparator < 0
            ? $"_rels/{packagePart}.rels"
            : $"{packagePart[..directorySeparator]}/_rels/{packagePart[(directorySeparator + 1)..]}.rels";
    }

    private static string NormalizePackagePathSegments(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join("/", segments);
    }

    private static string NormalizePackagePart(string part) =>
        part.Replace('\\', '/').TrimStart('/');

    private static void AssertOpenXmlValid(string xlsxPath, string label)
    {
        try
        {
            using var document = SpreadsheetDocument.Open(xlsxPath, false);
            var errors = new OpenXmlValidator(FileFormatVersions.Microsoft365)
                .Validate(document)
                .Where(error => !IsIgnoredOpenXmlValidationError(error, label))
                .ToArray();

            if (errors.Length == 0)
                return;

            var sample = string.Join(
                "; ",
                errors
                    .Take(MaxOpenXmlValidationErrorsToReport)
                    .Select(FormatOpenXmlValidationError));
            var suffix = errors.Length > MaxOpenXmlValidationErrorsToReport
                ? $"; ... {errors.Length - MaxOpenXmlValidationErrorsToReport} more"
                : string.Empty;

            throw new InvalidDataException(
                $"{label} failed Open XML SDK validation with {errors.Length} error(s): {sample}{suffix}");
        }
        catch (OpenXmlPackageException ex)
        {
            throw new InvalidDataException(
                $"{label} could not be opened by Open XML SDK validation: {ex.Message}",
                ex);
        }
    }

    private static string FormatOpenXmlValidationError(ValidationErrorInfo error)
    {
        var path = string.IsNullOrWhiteSpace(error.Path?.XPath)
            ? "<unknown path>"
            : error.Path.XPath;
        return $"{path}: {error.Description}";
    }

    private static bool IsIgnoredOpenXmlValidationError(ValidationErrorInfo error, string label)
    {
        if (IsIgnoredLegacyMetadataValidationError(error))
            return true;

        return IsIgnoredExcelSavedValidationError(error, label);
    }

    private static bool IsIgnoredLegacyMetadataValidationError(ValidationErrorInfo error)
    {
        var description = error.Description ?? "";
        return description.Contains("invalid child element", StringComparison.OrdinalIgnoreCase) &&
               (description.Contains(":smartTagPr", StringComparison.OrdinalIgnoreCase) ||
                description.Contains(":smartTags", StringComparison.OrdinalIgnoreCase) ||
                description.Contains(":singleXmlCells", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIgnoredExcelSavedValidationError(ValidationErrorInfo error, string label)
    {
        if (!string.Equals(label, "Excel-saved workbook", StringComparison.Ordinal))
            return false;

        var path = error.Path?.XPath ?? "";
        var description = error.Description ?? "";
        if (path.StartsWith("/x:calcChain", StringComparison.Ordinal) &&
            description.Contains("referenced by 'c@", StringComparison.OrdinalIgnoreCase) &&
            description.Contains("/xl/styles.xml", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.Contains("/x:pageSetup", StringComparison.Ordinal) &&
               description.Contains("Dpi", StringComparison.OrdinalIgnoreCase) &&
               description.Contains("MinInclusive", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct ExcelShapeSummary(
        int TotalCount,
        int PictureCount,
        int TextBoxCount,
        int DrawingShapeCount);

    private readonly record struct ExcelPageSetupSummary(
        int PrintAreaSheetCount,
        int PrintTitleSheetCount,
        int LandscapeSheetCount,
        int ScaleToFitSheetCount,
        int PrintOptionsSheetCount,
        int HeaderFooterSheetCount,
        int ManualPageBreakCount,
        int AllowEditRangeCount);

    private readonly record struct ExcelStructureSummary(
        int MergedAreaCount,
        int FreezePaneSheetCount,
        int HiddenRowCount,
        int HiddenColumnCount,
        int CustomRowHeightCount,
        int CustomColumnWidthCount,
        int OutlineRowCount,
        int OutlineColumnCount);

    private readonly record struct ExcelFormattingSummary(
        int StyledCellCount,
        int NumberFormatCellCount,
        int BoldCellCount,
        int FilledCellCount,
        int BorderedCellCount,
        int AlignedCellCount,
        int WrappedCellCount);

    private readonly record struct ExcelContentProbePlan(
        bool NamedRanges,
        bool Charts,
        bool DataValidations,
        bool ConditionalFormats,
        bool Hyperlinks,
        bool Comments,
        bool ProtectedSheets,
        bool StructureProtection,
        bool Shapes,
        bool Sparklines,
        bool PageSetup,
        bool Structure,
        bool Formatting,
        bool Formulas,
        bool StructuredTables,
        bool AutoFilters,
        bool PivotTables)
    {
        public static ExcelContentProbePlan OpenabilityOnly { get; } = new(
            NamedRanges: false,
            Charts: false,
            DataValidations: false,
            ConditionalFormats: false,
            Hyperlinks: false,
            Comments: false,
            ProtectedSheets: false,
            StructureProtection: false,
            Shapes: false,
            Sparklines: false,
            PageSetup: false,
            Structure: false,
            Formatting: false,
            Formulas: false,
            StructuredTables: false,
            AutoFilters: false,
            PivotTables: false);

        public static ExcelContentProbePlan From(WorkbookSmokeExpectations? expectations)
        {
            if (expectations is null)
                return OpenabilityOnly;

            static bool Any(params int[] values) => values.Any(value => value > 0);

            return new ExcelContentProbePlan(
                NamedRanges: Any(expectations.MinExcelOpenedNamedRanges, expectations.MinExcelReopenedNamedRanges),
                Charts: Any(expectations.MinExcelOpenedCharts, expectations.MinExcelReopenedCharts),
                DataValidations: Any(expectations.MinExcelOpenedDataValidationCells, expectations.MinExcelReopenedDataValidationCells),
                ConditionalFormats: Any(expectations.MinExcelOpenedConditionalFormats, expectations.MinExcelReopenedConditionalFormats),
                Hyperlinks: Any(expectations.MinExcelOpenedHyperlinks, expectations.MinExcelReopenedHyperlinks),
                Comments: Any(expectations.MinExcelOpenedComments, expectations.MinExcelReopenedComments),
                ProtectedSheets: Any(expectations.MinExcelOpenedProtectedSheets, expectations.MinExcelReopenedProtectedSheets),
                StructureProtection: Any(expectations.MinExcelOpenedStructureProtection, expectations.MinExcelReopenedStructureProtection),
                Shapes: Any(
                    expectations.MinExcelOpenedPictures,
                    expectations.MinExcelReopenedPictures,
                    expectations.MinExcelOpenedTextBoxes,
                    expectations.MinExcelReopenedTextBoxes,
                    expectations.MinExcelOpenedDrawingShapes,
                    expectations.MinExcelReopenedDrawingShapes,
                    expectations.MinExcelOpenedShapes,
                    expectations.MinExcelReopenedShapes),
                Sparklines: Any(expectations.MinExcelOpenedSparklines, expectations.MinExcelReopenedSparklines),
                PageSetup: Any(
                    expectations.MinExcelOpenedPrintAreaSheets,
                    expectations.MinExcelReopenedPrintAreaSheets,
                    expectations.MinExcelOpenedPrintTitleSheets,
                    expectations.MinExcelReopenedPrintTitleSheets,
                    expectations.MinExcelOpenedLandscapeSheets,
                    expectations.MinExcelReopenedLandscapeSheets,
                    expectations.MinExcelOpenedScaleToFitSheets,
                    expectations.MinExcelReopenedScaleToFitSheets,
                    expectations.MinExcelOpenedPrintOptionsSheets,
                    expectations.MinExcelReopenedPrintOptionsSheets,
                    expectations.MinExcelOpenedHeaderFooterSheets,
                    expectations.MinExcelReopenedHeaderFooterSheets,
                    expectations.MinExcelOpenedManualPageBreaks,
                    expectations.MinExcelReopenedManualPageBreaks,
                    expectations.MinExcelOpenedAllowEditRanges,
                    expectations.MinExcelReopenedAllowEditRanges),
                Structure: Any(
                    expectations.MinExcelOpenedMergedAreas,
                    expectations.MinExcelReopenedMergedAreas,
                    expectations.MinExcelOpenedFreezePaneSheets,
                    expectations.MinExcelReopenedFreezePaneSheets,
                    expectations.MinExcelOpenedHiddenRows,
                    expectations.MinExcelReopenedHiddenRows,
                    expectations.MinExcelOpenedHiddenColumns,
                    expectations.MinExcelReopenedHiddenColumns,
                    expectations.MinExcelOpenedCustomRowHeights,
                    expectations.MinExcelReopenedCustomRowHeights,
                    expectations.MinExcelOpenedCustomColumnWidths,
                    expectations.MinExcelReopenedCustomColumnWidths,
                    expectations.MinExcelOpenedOutlineRows,
                    expectations.MinExcelReopenedOutlineRows,
                    expectations.MinExcelOpenedOutlineColumns,
                    expectations.MinExcelReopenedOutlineColumns),
                Formatting: Any(
                    expectations.MinExcelOpenedStyledCells,
                    expectations.MinExcelReopenedStyledCells,
                    expectations.MinExcelOpenedNumberFormatCells,
                    expectations.MinExcelReopenedNumberFormatCells,
                    expectations.MinExcelOpenedBoldCells,
                    expectations.MinExcelReopenedBoldCells,
                    expectations.MinExcelOpenedFilledCells,
                    expectations.MinExcelReopenedFilledCells,
                    expectations.MinExcelOpenedBorderedCells,
                    expectations.MinExcelReopenedBorderedCells,
                    expectations.MinExcelOpenedAlignedCells,
                    expectations.MinExcelReopenedAlignedCells,
                    expectations.MinExcelOpenedWrappedCells,
                    expectations.MinExcelReopenedWrappedCells),
                Formulas: Any(expectations.MinExcelOpenedFormulaCells, expectations.MinExcelReopenedFormulaCells),
                StructuredTables: Any(expectations.MinExcelOpenedStructuredTables, expectations.MinExcelReopenedStructuredTables),
                AutoFilters: Any(expectations.MinExcelOpenedAutoFilterSheets, expectations.MinExcelReopenedAutoFilterSheets),
                PivotTables: Any(expectations.MinExcelOpenedPivotTables, expectations.MinExcelReopenedPivotTables));
        }
    }

    private readonly record struct FreeXFormattingSummary(
        int StyledCellCount,
        int NumberFormatCellCount,
        int BoldCellCount,
        int FilledCellCount,
        int BorderedCellCount,
        int AlignedCellCount,
        int WrappedCellCount);

    private static ExcelWorkbookSummary CountWorkbookContents(
        object workbook,
        WorkbookSmokeExpectations? expectations)
    {
        var probePlan = ExcelContentProbePlan.From(expectations);
        object? worksheets = null;
        try
        {
            worksheets = ((dynamic)workbook).Worksheets;
            var worksheetCount = Convert.ToInt32(((dynamic)worksheets).Count, CultureInfo.InvariantCulture);
            var namedRangeCount = probePlan.NamedRanges ? CountWorkbookUserDefinedNames(workbook) : 0;
            var chartCount = probePlan.Charts ? CountWorkbookChartSheets(workbook) : 0;
            var dataValidationCellCount = 0;
            var conditionalFormatCount = 0;
            var hyperlinkCount = 0;
            var commentCount = 0;
            var protectedSheetCount = 0;
            var structureProtectionCount = probePlan.StructureProtection
                ? CountWorkbookStructureProtection(workbook)
                : 0;
            var pictureCount = 0;
            var sparklineCount = 0;
            var textBoxCount = 0;
            var drawingShapeCount = 0;
            var shapeCount = 0;
            var printAreaSheetCount = 0;
            var printTitleSheetCount = 0;
            var landscapeSheetCount = 0;
            var scaleToFitSheetCount = 0;
            var printOptionsSheetCount = 0;
            var headerFooterSheetCount = 0;
            var manualPageBreakCount = 0;
            var allowEditRangeCount = 0;
            var mergedAreaCount = 0;
            var freezePaneSheetCount = 0;
            var hiddenRowCount = 0;
            var hiddenColumnCount = 0;
            var customRowHeightCount = 0;
            var customColumnWidthCount = 0;
            var outlineRowCount = 0;
            var outlineColumnCount = 0;
            var styledCellCount = 0;
            var numberFormatCellCount = 0;
            var boldCellCount = 0;
            var filledCellCount = 0;
            var borderedCellCount = 0;
            var alignedCellCount = 0;
            var wrappedCellCount = 0;
            var formulaCellCount = 0;
            var structuredTableCount = 0;
            var autoFilterSheetCount = 0;
            var pivotTableCount = 0;

            for (var index = 1; index <= worksheetCount; index++)
            {
                object? worksheet = null;
                object? listObjects = null;
                object? pivotTables = null;
                try
                {
                    worksheet = ((dynamic)worksheets)[index];
                    if (probePlan.Charts || probePlan.Shapes)
                    {
                        try
                        {
                            if (probePlan.Charts)
                                chartCount += CountWorksheetChartObjects(worksheet);
                            if (probePlan.Shapes)
                            {
                                var worksheetShapes = CountWorksheetShapes(worksheet);
                                shapeCount += worksheetShapes.TotalCount;
                                pictureCount += worksheetShapes.PictureCount;
                                textBoxCount += worksheetShapes.TextBoxCount;
                                drawingShapeCount += worksheetShapes.DrawingShapeCount;
                            }
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel chart/shape count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.Formulas)
                    {
                        try
                        {
                            formulaCellCount += CountWorksheetFormulaCells(worksheet);
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel formula count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.DataValidations)
                    {
                        try
                        {
                            dataValidationCellCount += CountWorksheetDataValidationCells(worksheet);
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel data-validation count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.ConditionalFormats)
                    {
                        try
                        {
                            conditionalFormatCount += CountWorksheetConditionalFormats(worksheet);
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel conditional-format count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.Hyperlinks)
                    {
                        try
                        {
                            hyperlinkCount += CountWorksheetHyperlinks(worksheet);
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel hyperlink count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.Comments)
                    {
                        try
                        {
                            commentCount += CountWorksheetComments(worksheet);
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel comment count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.ProtectedSheets && IsWorksheetProtected(worksheet))
                        protectedSheetCount++;

                    if (probePlan.PageSetup)
                    {
                        try
                        {
                            var worksheetPageSetup = CountWorksheetPageSetup(worksheet);
                            printAreaSheetCount += worksheetPageSetup.PrintAreaSheetCount;
                            printTitleSheetCount += worksheetPageSetup.PrintTitleSheetCount;
                            landscapeSheetCount += worksheetPageSetup.LandscapeSheetCount;
                            scaleToFitSheetCount += worksheetPageSetup.ScaleToFitSheetCount;
                            printOptionsSheetCount += worksheetPageSetup.PrintOptionsSheetCount;
                            headerFooterSheetCount += worksheetPageSetup.HeaderFooterSheetCount;
                            manualPageBreakCount += worksheetPageSetup.ManualPageBreakCount;
                            allowEditRangeCount += worksheetPageSetup.AllowEditRangeCount;
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel page-setup count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.Structure)
                    {
                        try
                        {
                            var worksheetStructure = CountWorksheetStructure(workbook, worksheet);
                            mergedAreaCount += worksheetStructure.MergedAreaCount;
                            freezePaneSheetCount += worksheetStructure.FreezePaneSheetCount;
                            hiddenRowCount += worksheetStructure.HiddenRowCount;
                            hiddenColumnCount += worksheetStructure.HiddenColumnCount;
                            customRowHeightCount += worksheetStructure.CustomRowHeightCount;
                            customColumnWidthCount += worksheetStructure.CustomColumnWidthCount;
                            outlineRowCount += worksheetStructure.OutlineRowCount;
                            outlineColumnCount += worksheetStructure.OutlineColumnCount;
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel structure count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.Formatting)
                    {
                        try
                        {
                            var worksheetFormatting = CountWorksheetFormatting(worksheet);
                            styledCellCount += worksheetFormatting.StyledCellCount;
                            numberFormatCellCount += worksheetFormatting.NumberFormatCellCount;
                            boldCellCount += worksheetFormatting.BoldCellCount;
                            filledCellCount += worksheetFormatting.FilledCellCount;
                            borderedCellCount += worksheetFormatting.BorderedCellCount;
                            alignedCellCount += worksheetFormatting.AlignedCellCount;
                            wrappedCellCount += worksheetFormatting.WrappedCellCount;
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel formatting count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.Sparklines)
                    {
                        try
                        {
                            sparklineCount += CountWorksheetSparklines(worksheet);
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel sparkline count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.StructuredTables)
                    {
                        try
                        {
                            listObjects = ((dynamic)worksheet).ListObjects;
                            structuredTableCount += Convert.ToInt32(((dynamic)listObjects).Count, CultureInfo.InvariantCulture);
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel structured-table count failed for worksheet index {index}", ex);
                        }
                    }

                    if (probePlan.AutoFilters && IsWorksheetAutoFilterEnabled(worksheet))
                        autoFilterSheetCount++;

                    if (probePlan.PivotTables)
                    {
                        try
                        {
                            pivotTables = ((dynamic)worksheet).PivotTables();
                            pivotTableCount += Convert.ToInt32(((dynamic)pivotTables).Count, CultureInfo.InvariantCulture);
                        }
                        catch (COMException ex)
                        {
                            throw new InvalidDataException($"Excel PivotTable count failed for worksheet index {index}", ex);
                        }
                    }
                }
                finally
                {
                    ReleaseComObject(pivotTables);
                    ReleaseComObject(listObjects);
                    ReleaseComObject(worksheet);
                }
            }

            return new ExcelWorkbookSummary(
                worksheetCount,
                namedRangeCount,
                chartCount,
                dataValidationCellCount,
                conditionalFormatCount,
                hyperlinkCount,
                commentCount,
                protectedSheetCount,
                structureProtectionCount,
                pictureCount,
                sparklineCount,
                textBoxCount,
                drawingShapeCount,
                shapeCount,
                printAreaSheetCount,
                printTitleSheetCount,
                landscapeSheetCount,
                scaleToFitSheetCount,
                printOptionsSheetCount,
                headerFooterSheetCount,
                manualPageBreakCount,
                allowEditRangeCount,
                mergedAreaCount,
                freezePaneSheetCount,
                hiddenRowCount,
                hiddenColumnCount,
                customRowHeightCount,
                customColumnWidthCount,
                outlineRowCount,
                outlineColumnCount,
                styledCellCount,
                numberFormatCellCount,
                boldCellCount,
                filledCellCount,
                borderedCellCount,
                alignedCellCount,
                wrappedCellCount,
                formulaCellCount,
                structuredTableCount,
                autoFilterSheetCount,
                pivotTableCount);
        }
        finally
        {
            ReleaseComObject(worksheets);
        }
    }

    private static int CountWorkbookUserDefinedNames(object workbook)
    {
        object? names = null;
        try
        {
            names = ((dynamic)workbook).Names;
            var count = Convert.ToInt32(((dynamic)names).Count, CultureInfo.InvariantCulture);
            var userDefinedCount = 0;
            for (var index = 1; index <= count; index++)
            {
                object? name = null;
                try
                {
                    name = ((dynamic)names)[index];
                    var nameText = Convert.ToString(((dynamic)name).Name, CultureInfo.InvariantCulture) ?? string.Empty;
                    if (IsUserDefinedExcelName(nameText))
                        userDefinedCount++;
                }
                finally
                {
                    ReleaseComObject(name);
                }
            }

            return userDefinedCount;
        }
        catch (COMException ex)
        {
            throw new InvalidDataException("Excel named-range count failed.", ex);
        }
        finally
        {
            ReleaseComObject(names);
        }
    }

    private static bool IsUserDefinedExcelName(string name)
    {
        var localName = name;
        var scopeSeparator = localName.LastIndexOf('!');
        if (scopeSeparator >= 0)
            localName = localName[(scopeSeparator + 1)..];

        localName = localName.Trim('\'');
        return
            !localName.StartsWith("_xlchart.", StringComparison.OrdinalIgnoreCase) &&
            !localName.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(localName, "Print_Area", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(localName, "Print_Titles", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(localName, "_FilterDatabase", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(localName, "Criteria", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(localName, "Database", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(localName, "Extract", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountWorkbookChartSheets(object workbook)
    {
        object? charts = null;
        try
        {
            charts = ((dynamic)workbook).Charts;
            return Convert.ToInt32(((dynamic)charts).Count, CultureInfo.InvariantCulture);
        }
        catch (COMException ex)
        {
            throw new InvalidDataException("Excel chartsheet count failed.", ex);
        }
        finally
        {
            ReleaseComObject(charts);
        }
    }

    private static bool IsWorksheetAutoFilterEnabled(object worksheet)
    {
        try
        {
            if (Convert.ToBoolean(((dynamic)worksheet).AutoFilterMode, CultureInfo.InvariantCulture))
                return true;
        }
        catch (Exception ex) when (IsOptionalComMemberUnavailable(ex))
        {
        }

        object? listObjects = null;
        try
        {
            listObjects = ((dynamic)worksheet).ListObjects;
            var count = Convert.ToInt32(((dynamic)listObjects).Count, CultureInfo.InvariantCulture);
            for (var index = 1; index <= count; index++)
            {
                object? listObject = null;
                try
                {
                    listObject = ((dynamic)listObjects).Item(index);
                    if (IsListObjectAutoFilterEnabled(listObject))
                        return true;
                }
                finally
                {
                    ReleaseComObject(listObject);
                }
            }
        }
        catch (Exception ex) when (IsOptionalComMemberUnavailable(ex))
        {
            return false;
        }
        finally
        {
            ReleaseComObject(listObjects);
        }

        return false;
    }

    private static bool IsListObjectAutoFilterEnabled(object listObject)
    {
        object? autoFilter = null;
        try
        {
            autoFilter = ((dynamic)listObject).AutoFilter;
            return autoFilter is not null;
        }
        catch (Exception ex) when (IsOptionalComMemberUnavailable(ex))
        {
            return false;
        }
        finally
        {
            ReleaseComObject(autoFilter);
        }
    }

    private static int CountWorksheetChartObjects(object worksheet)
    {
        object? chartObjects = null;
        try
        {
            chartObjects = ((dynamic)worksheet).ChartObjects();
            return Convert.ToInt32(((dynamic)chartObjects).Count, CultureInfo.InvariantCulture);
        }
        finally
        {
            ReleaseComObject(chartObjects);
        }
    }

    private static ExcelShapeSummary CountWorksheetShapes(object worksheet)
    {
        object? shapes = null;
        try
        {
            shapes = ((dynamic)worksheet).Shapes;
            var totalCount = Convert.ToInt32(((dynamic)shapes).Count, CultureInfo.InvariantCulture);
            var pictureCount = 0;
            var textBoxCount = 0;
            var drawingShapeCount = 0;

            for (var index = 1; index <= totalCount; index++)
            {
                object? shape = null;
                try
                {
                    shape = ((dynamic)shapes).Item(index);
                    var type = Convert.ToInt32(((dynamic)shape).Type, CultureInfo.InvariantCulture);
                    if (IsExcelPictureShape(type))
                        pictureCount++;
                    else if (type == MsoShapeTypeTextBox)
                        textBoxCount++;
                    else if (IsExcelDrawingShape(type))
                        drawingShapeCount++;
                }
                finally
                {
                    ReleaseComObject(shape);
                }
            }

            return new ExcelShapeSummary(totalCount, pictureCount, textBoxCount, drawingShapeCount);
        }
        finally
        {
            ReleaseComObject(shapes);
        }
    }

    private static bool IsExcelPictureShape(int type) =>
        type is MsoShapeTypePicture or MsoShapeTypeLinkedPicture or MsoShapeTypeGraphic or MsoShapeTypeLinkedGraphic;

    private static bool IsExcelDrawingShape(int type) =>
        type is MsoShapeTypeAutoShape or MsoShapeTypeFreeform or MsoShapeTypeGroup or MsoShapeTypeLine;

    private static ExcelPageSetupSummary CountWorksheetPageSetup(object worksheet)
    {
        object? pageSetup = null;
        object? horizontalPageBreaks = null;
        object? verticalPageBreaks = null;
        object? protection = null;
        object? allowEditRanges = null;
        try
        {
            pageSetup = ((dynamic)worksheet).PageSetup;

            var printAreaSheetCount = HasComText(((dynamic)pageSetup).PrintArea) ? 1 : 0;
            var printTitleSheetCount =
                HasComText(((dynamic)pageSetup).PrintTitleRows) ||
                HasComText(((dynamic)pageSetup).PrintTitleColumns)
                    ? 1
                    : 0;
            var landscapeSheetCount = Convert.ToInt32(((dynamic)pageSetup).Orientation, CultureInfo.InvariantCulture) == XlLandscape ? 1 : 0;
            var scaleToFitSheetCount = IsScaleToFitPageSetup(pageSetup) ? 1 : 0;
            var printOptionsSheetCount =
                Convert.ToBoolean(((dynamic)pageSetup).PrintGridlines, CultureInfo.InvariantCulture) ||
                Convert.ToBoolean(((dynamic)pageSetup).PrintHeadings, CultureInfo.InvariantCulture)
                    ? 1
                    : 0;
            var headerFooterSheetCount = HasHeaderFooterText(pageSetup) ? 1 : 0;

            horizontalPageBreaks = ((dynamic)worksheet).HPageBreaks;
            verticalPageBreaks = ((dynamic)worksheet).VPageBreaks;
            var manualPageBreakCount =
                CountManualPageBreaks(horizontalPageBreaks) +
                CountManualPageBreaks(verticalPageBreaks);

            var allowEditRangeCount = 0;
            try
            {
                protection = ((dynamic)worksheet).Protection;
                allowEditRanges = ((dynamic)protection).AllowEditRanges;
                allowEditRangeCount = Convert.ToInt32(((dynamic)allowEditRanges).Count, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (IsOptionalComMemberUnavailable(ex))
            {
                allowEditRangeCount = 0;
            }

            return new ExcelPageSetupSummary(
                printAreaSheetCount,
                printTitleSheetCount,
                landscapeSheetCount,
                scaleToFitSheetCount,
                printOptionsSheetCount,
                headerFooterSheetCount,
                manualPageBreakCount,
                allowEditRangeCount);
        }
        finally
        {
            ReleaseComObject(allowEditRanges);
            ReleaseComObject(protection);
            ReleaseComObject(verticalPageBreaks);
            ReleaseComObject(horizontalPageBreaks);
            ReleaseComObject(pageSetup);
        }
    }

    private static bool HasHeaderFooterText(object pageSetup) =>
        HasComText(((dynamic)pageSetup).LeftHeader) ||
        HasComText(((dynamic)pageSetup).CenterHeader) ||
        HasComText(((dynamic)pageSetup).RightHeader) ||
        HasComText(((dynamic)pageSetup).LeftFooter) ||
        HasComText(((dynamic)pageSetup).CenterFooter) ||
        HasComText(((dynamic)pageSetup).RightFooter);

    private static int CountManualPageBreaks(object pageBreaks)
    {
        var count = Convert.ToInt32(((dynamic)pageBreaks).Count, CultureInfo.InvariantCulture);
        var manualCount = 0;
        for (var index = 1; index <= count; index++)
        {
            object? pageBreak = null;
            try
            {
                pageBreak = ((dynamic)pageBreaks).Item(index);
                if (Convert.ToInt32(((dynamic)pageBreak).Type, CultureInfo.InvariantCulture) == XlPageBreakManual)
                    manualCount++;
            }
            catch (COMException)
            {
                // Excel sometimes reports automatic page breaks in Count but rejects indexed access.
            }
            finally
            {
                ReleaseComObject(pageBreak);
            }
        }

        return manualCount;
    }

    private static bool IsScaleToFitPageSetup(object pageSetup)
    {
        var zoom = ((dynamic)pageSetup).Zoom;
        if (zoom is bool zoomFlag && !zoomFlag)
            return IsPositivePageSetupValue(((dynamic)pageSetup).FitToPagesWide) ||
                   IsPositivePageSetupValue(((dynamic)pageSetup).FitToPagesTall);

        return false;
    }

    private static bool IsPositivePageSetupValue(object? value)
    {
        if (value is null)
            return false;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0;
    }

    private static bool HasComText(object? value) =>
        !string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));

    private static ExcelStructureSummary CountWorksheetStructure(object workbook, object worksheet)
    {
        var mergedAreaCount = CountWorksheetMergedAreas(worksheet);
        var freezePaneSheetCount = HasWorksheetFreezePanes(workbook, worksheet) ? 1 : 0;
        var rowColumnSummary = CountWorksheetRowColumnStructure(worksheet);

        return new ExcelStructureSummary(
            mergedAreaCount,
            freezePaneSheetCount,
            rowColumnSummary.HiddenRowCount,
            rowColumnSummary.HiddenColumnCount,
            rowColumnSummary.CustomRowHeightCount,
            rowColumnSummary.CustomColumnWidthCount,
            rowColumnSummary.OutlineRowCount,
            rowColumnSummary.OutlineColumnCount);
    }

    private static int CountWorksheetMergedAreas(object worksheet)
    {
        object? usedRange = null;
        object? rows = null;
        object? columns = null;
        object? cells = null;
        try
        {
            usedRange = ((dynamic)worksheet).UsedRange;
            rows = ((dynamic)usedRange).Rows;
            columns = ((dynamic)usedRange).Columns;
            cells = ((dynamic)worksheet).Cells;

            var firstRow = Convert.ToInt32(((dynamic)usedRange).Row, CultureInfo.InvariantCulture);
            var firstColumn = Convert.ToInt32(((dynamic)usedRange).Column, CultureInfo.InvariantCulture);
            var rowCount = Convert.ToInt32(((dynamic)rows).Count, CultureInfo.InvariantCulture);
            var columnCount = Convert.ToInt32(((dynamic)columns).Count, CultureInfo.InvariantCulture);
            var mergedAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var probed = 0;

            for (var rowOffset = 0; rowOffset < rowCount && probed < MaxMergedAreaProbeCells; rowOffset++)
            {
                for (var columnOffset = 0; columnOffset < columnCount && probed < MaxMergedAreaProbeCells; columnOffset++)
                {
                    object? cell = null;
                    object? mergeArea = null;
                    try
                    {
                        cell = ((dynamic)cells)[firstRow + rowOffset, firstColumn + columnOffset];
                        probed++;
                        if (!Convert.ToBoolean(((dynamic)cell).MergeCells, CultureInfo.InvariantCulture))
                            continue;

                        mergeArea = ((dynamic)cell).MergeArea;
                        var address = Convert.ToString(((dynamic)mergeArea).Address(false, false), CultureInfo.InvariantCulture);
                        if (!string.IsNullOrWhiteSpace(address))
                            mergedAreas.Add(address);
                    }
                    finally
                    {
                        ReleaseComObject(mergeArea);
                        ReleaseComObject(cell);
                    }
                }
            }

            return mergedAreas.Count;
        }
        catch (COMException)
        {
            return 0;
        }
        finally
        {
            ReleaseComObject(cells);
            ReleaseComObject(columns);
            ReleaseComObject(rows);
            ReleaseComObject(usedRange);
        }
    }

    private static bool HasWorksheetFreezePanes(object workbook, object worksheet)
    {
        object? windows = null;
        object? window = null;
        try
        {
            ((dynamic)worksheet).Activate();
            windows = ((dynamic)workbook).Windows;
            window = ((dynamic)windows).Item(1);
            return Convert.ToBoolean(((dynamic)window).FreezePanes, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (IsOptionalComMemberUnavailable(ex))
        {
            return false;
        }
        finally
        {
            ReleaseComObject(window);
            ReleaseComObject(windows);
        }
    }

    private static ExcelStructureSummary CountWorksheetRowColumnStructure(object worksheet)
    {
        object? rows = null;
        object? columns = null;
        try
        {
            rows = ((dynamic)worksheet).Rows;
            columns = ((dynamic)worksheet).Columns;
            var standardHeight = Convert.ToDouble(((dynamic)worksheet).StandardHeight, CultureInfo.InvariantCulture);
            var standardWidth = Convert.ToDouble(((dynamic)worksheet).StandardWidth, CultureInfo.InvariantCulture);

            var hiddenRows = 0;
            var hiddenColumns = 0;
            var customRowHeights = 0;
            var customColumnWidths = 0;
            var outlineRows = 0;
            var outlineColumns = 0;

            for (var rowIndex = 1; rowIndex <= MaxStructureProbeRows; rowIndex++)
            {
                object? row = null;
                try
                {
                    row = ((dynamic)rows)[rowIndex];
                    var hidden = Convert.ToBoolean(((dynamic)row).Hidden, CultureInfo.InvariantCulture);
                    if (hidden)
                    {
                        hiddenRows++;
                    }
                    else
                    {
                        var rowHeight = Convert.ToDouble(((dynamic)row).RowHeight, CultureInfo.InvariantCulture);
                        if (Math.Abs(rowHeight - standardHeight) > ExcelMeasurementTolerance)
                            customRowHeights++;
                    }

                    if (Convert.ToInt32(((dynamic)row).OutlineLevel, CultureInfo.InvariantCulture) > 1)
                        outlineRows++;
                }
                finally
                {
                    ReleaseComObject(row);
                }
            }

            for (var columnIndex = 1; columnIndex <= MaxStructureProbeColumns; columnIndex++)
            {
                object? column = null;
                try
                {
                    column = ((dynamic)columns)[columnIndex];
                    var hidden = Convert.ToBoolean(((dynamic)column).Hidden, CultureInfo.InvariantCulture);
                    if (hidden)
                    {
                        hiddenColumns++;
                    }
                    else
                    {
                        var columnWidth = Convert.ToDouble(((dynamic)column).ColumnWidth, CultureInfo.InvariantCulture);
                        if (Math.Abs(columnWidth - standardWidth) > ExcelMeasurementTolerance)
                            customColumnWidths++;
                    }

                    if (Convert.ToInt32(((dynamic)column).OutlineLevel, CultureInfo.InvariantCulture) > 1)
                        outlineColumns++;
                }
                finally
                {
                    ReleaseComObject(column);
                }
            }

            return new ExcelStructureSummary(
                MergedAreaCount: 0,
                FreezePaneSheetCount: 0,
                hiddenRows,
                hiddenColumns,
                customRowHeights,
                customColumnWidths,
                outlineRows,
                outlineColumns);
        }
        catch (COMException)
        {
            return default;
        }
        finally
        {
            ReleaseComObject(columns);
            ReleaseComObject(rows);
        }
    }

    private static ExcelFormattingSummary CountWorksheetFormatting(object worksheet)
    {
        object? usedRange = null;
        object? rows = null;
        object? columns = null;
        object? cells = null;
        try
        {
            usedRange = ((dynamic)worksheet).UsedRange;
            rows = ((dynamic)usedRange).Rows;
            columns = ((dynamic)usedRange).Columns;
            cells = ((dynamic)worksheet).Cells;

            var firstRow = Convert.ToInt32(((dynamic)usedRange).Row, CultureInfo.InvariantCulture);
            var firstColumn = Convert.ToInt32(((dynamic)usedRange).Column, CultureInfo.InvariantCulture);
            var rowCount = Convert.ToInt32(((dynamic)rows).Count, CultureInfo.InvariantCulture);
            var columnCount = Convert.ToInt32(((dynamic)columns).Count, CultureInfo.InvariantCulture);

            var styledCells = 0;
            var numberFormatCells = 0;
            var boldCells = 0;
            var filledCells = 0;
            var borderedCells = 0;
            var alignedCells = 0;
            var wrappedCells = 0;
            var probed = 0;

            for (var rowOffset = 0; rowOffset < rowCount && probed < MaxFormattingProbeCells; rowOffset++)
            {
                for (var columnOffset = 0; columnOffset < columnCount && probed < MaxFormattingProbeCells; columnOffset++)
                {
                    object? cell = null;
                    object? font = null;
                    object? interior = null;
                    try
                    {
                        cell = ((dynamic)cells)[firstRow + rowOffset, firstColumn + columnOffset];
                        probed++;

                        var hasNumberFormat = HasNonGeneralNumberFormat(((dynamic)cell).NumberFormat);
                        font = ((dynamic)cell).Font;
                        var isBold = Convert.ToBoolean(((dynamic)font).Bold, CultureInfo.InvariantCulture);
                        interior = ((dynamic)cell).Interior;
                        var hasFill = HasVisibleFill(interior);
                        var hasBorder = HasVisibleBorder(cell);
                        var hasAlignment = HasExplicitAlignment(cell);
                        var isWrapped = Convert.ToBoolean(((dynamic)cell).WrapText, CultureInfo.InvariantCulture);

                        if (hasNumberFormat)
                            numberFormatCells++;
                        if (isBold)
                            boldCells++;
                        if (hasFill)
                            filledCells++;
                        if (hasBorder)
                            borderedCells++;
                        if (hasAlignment)
                            alignedCells++;
                        if (isWrapped)
                            wrappedCells++;
                        if (hasNumberFormat || isBold || hasFill || hasBorder || hasAlignment || isWrapped)
                            styledCells++;
                    }
                    finally
                    {
                        ReleaseComObject(interior);
                        ReleaseComObject(font);
                        ReleaseComObject(cell);
                    }
                }
            }

            return new ExcelFormattingSummary(
                styledCells,
                numberFormatCells,
                boldCells,
                filledCells,
                borderedCells,
                alignedCells,
                wrappedCells);
        }
        catch (COMException)
        {
            return default;
        }
        finally
        {
            ReleaseComObject(cells);
            ReleaseComObject(columns);
            ReleaseComObject(rows);
            ReleaseComObject(usedRange);
        }
    }

    private static bool HasNonGeneralNumberFormat(object? numberFormat)
    {
        var text = Convert.ToString(numberFormat, CultureInfo.InvariantCulture);
        return !string.IsNullOrWhiteSpace(text) &&
               !string.Equals(text, "General", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasVisibleFill(object interior)
    {
        try
        {
            var colorIndex = Convert.ToInt32(((dynamic)interior).ColorIndex, CultureInfo.InvariantCulture);
            if (colorIndex == XlColorIndexNone)
                return false;

            return colorIndex != 0;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static bool HasVisibleBorder(object cell)
    {
        object? borders = null;
        try
        {
            borders = ((dynamic)cell).Borders;
            return HasVisibleBorderEdge(borders, XlBorderIndexLeft) ||
                   HasVisibleBorderEdge(borders, XlBorderIndexTop) ||
                   HasVisibleBorderEdge(borders, XlBorderIndexBottom) ||
                   HasVisibleBorderEdge(borders, XlBorderIndexRight);
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            ReleaseComObject(borders);
        }
    }

    private static bool HasVisibleBorderEdge(object borders, int borderIndex)
    {
        object? border = null;
        try
        {
            border = ((dynamic)borders)[borderIndex];
            var lineStyle = Convert.ToInt32(((dynamic)border).LineStyle, CultureInfo.InvariantCulture);
            return lineStyle != 0 && lineStyle != XlLineStyleNone;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            ReleaseComObject(border);
        }
    }

    private static bool HasExplicitAlignment(object cell)
    {
        try
        {
            var horizontalAlignment = Convert.ToInt32(((dynamic)cell).HorizontalAlignment, CultureInfo.InvariantCulture);
            var verticalAlignment = Convert.ToInt32(((dynamic)cell).VerticalAlignment, CultureInfo.InvariantCulture);
            return horizontalAlignment != XlHAlignGeneral || verticalAlignment != XlVAlignBottom;
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static int CountWorksheetSparklines(object worksheet)
    {
        object? cells = null;
        object? sparklineGroups = null;
        try
        {
            cells = ((dynamic)worksheet).Cells;
            sparklineGroups = ((dynamic)cells).SparklineGroups;
            var groupCount = Convert.ToInt32(((dynamic)sparklineGroups).Count, CultureInfo.InvariantCulture);
            var sparklineCount = 0;

            for (var index = 1; index <= groupCount; index++)
            {
                object? group = null;
                try
                {
                    group = ((dynamic)sparklineGroups).Item(index);
                    sparklineCount += Convert.ToInt32(((dynamic)group).Count, CultureInfo.InvariantCulture);
                }
                finally
                {
                    ReleaseComObject(group);
                }
            }

            return sparklineCount;
        }
        catch (Exception ex) when (IsOptionalComMemberUnavailable(ex))
        {
            return 0;
        }
        finally
        {
            ReleaseComObject(sparklineGroups);
            ReleaseComObject(cells);
        }
    }

    private static int CountWorksheetDataValidationCells(object worksheet)
    {
        object? cells = null;
        object? validationCells = null;
        try
        {
            cells = ((dynamic)worksheet).Cells;
            validationCells = ((dynamic)cells).SpecialCells(ExcelCellTypeAllValidation);
            return CountRangeCellsCapped(validationCells);
        }
        catch (COMException)
        {
            return IsWorksheetProtected(worksheet)
                ? CountWorksheetDataValidationCellsByProbe(worksheet)
                : 0;
        }
        finally
        {
            ReleaseComObject(validationCells);
            ReleaseComObject(cells);
        }
    }

    private static bool IsWorksheetProtected(object worksheet)
    {
        try
        {
            return Convert.ToBoolean(((dynamic)worksheet).ProtectContents, CultureInfo.InvariantCulture);
        }
        catch (COMException)
        {
            return false;
        }
    }

    private static int CountWorksheetDataValidationCellsByProbe(object worksheet)
    {
        object? usedRange = null;
        object? rows = null;
        object? columns = null;
        object? cells = null;
        try
        {
            usedRange = ((dynamic)worksheet).UsedRange;
            rows = ((dynamic)usedRange).Rows;
            columns = ((dynamic)usedRange).Columns;
            cells = ((dynamic)worksheet).Cells;

            var firstRow = Convert.ToInt32(((dynamic)usedRange).Row, CultureInfo.InvariantCulture);
            var firstColumn = Convert.ToInt32(((dynamic)usedRange).Column, CultureInfo.InvariantCulture);
            var rowCount = Convert.ToInt32(((dynamic)rows).Count, CultureInfo.InvariantCulture);
            var columnCount = Convert.ToInt32(((dynamic)columns).Count, CultureInfo.InvariantCulture);
            var count = 0;
            var probed = 0;

            for (var rowOffset = 0; rowOffset < rowCount && probed < MaxDataValidationProbeCells; rowOffset++)
            {
                for (var columnOffset = 0; columnOffset < columnCount && probed < MaxDataValidationProbeCells; columnOffset++)
                {
                    object? cell = null;
                    try
                    {
                        cell = ((dynamic)cells)[firstRow + rowOffset, firstColumn + columnOffset];
                        probed++;
                        if (CellHasDataValidation(cell))
                            count++;
                    }
                    finally
                    {
                        ReleaseComObject(cell);
                    }
                }
            }

            return count;
        }
        catch (COMException)
        {
            return 0;
        }
        finally
        {
            ReleaseComObject(cells);
            ReleaseComObject(columns);
            ReleaseComObject(rows);
            ReleaseComObject(usedRange);
        }
    }

    private static bool CellHasDataValidation(object cell)
    {
        object? validation = null;
        try
        {
            validation = ((dynamic)cell).Validation;
            var typeText = Convert.ToString(((dynamic)validation).Type, CultureInfo.InvariantCulture);
            return !string.IsNullOrWhiteSpace(typeText);
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            ReleaseComObject(validation);
        }
    }

    private static int CountWorksheetConditionalFormats(object worksheet)
    {
        object? cells = null;
        object? formatConditions = null;
        try
        {
            cells = ((dynamic)worksheet).Cells;
            formatConditions = ((dynamic)cells).FormatConditions;
            return Convert.ToInt32(((dynamic)formatConditions).Count, CultureInfo.InvariantCulture);
        }
        finally
        {
            ReleaseComObject(formatConditions);
            ReleaseComObject(cells);
        }
    }

    private static int CountWorksheetHyperlinks(object worksheet)
    {
        object? hyperlinks = null;
        try
        {
            hyperlinks = ((dynamic)worksheet).Hyperlinks;
            return Convert.ToInt32(((dynamic)hyperlinks).Count, CultureInfo.InvariantCulture);
        }
        finally
        {
            ReleaseComObject(hyperlinks);
        }
    }

    private static int CountWorksheetComments(object worksheet) =>
        CountWorksheetLegacyComments(worksheet) + CountWorksheetThreadedComments(worksheet);

    private static int CountWorksheetLegacyComments(object worksheet)
    {
        object? comments = null;
        try
        {
            comments = ((dynamic)worksheet).Comments;
            return Convert.ToInt32(((dynamic)comments).Count, CultureInfo.InvariantCulture);
        }
        finally
        {
            ReleaseComObject(comments);
        }
    }

    private static int CountWorksheetThreadedComments(object worksheet)
    {
        object? comments = null;
        try
        {
            comments = ((dynamic)worksheet).CommentsThreaded;
            return Convert.ToInt32(((dynamic)comments).Count, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (IsOptionalComMemberUnavailable(ex))
        {
            return 0;
        }
        finally
        {
            ReleaseComObject(comments);
        }
    }

    private static bool IsOptionalComMemberUnavailable(Exception ex) =>
        ex is COMException ||
        string.Equals(ex.GetType().FullName, "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException", StringComparison.Ordinal);

    private static int CountWorkbookStructureProtection(object workbook)
    {
        try
        {
            return Convert.ToBoolean(((dynamic)workbook).ProtectStructure, CultureInfo.InvariantCulture) ? 1 : 0;
        }
        catch (COMException)
        {
            return 0;
        }
    }

    private static int CountRangeCellsCapped(object range)
    {
        try
        {
            var countLarge = Convert.ToDouble(((dynamic)range).CountLarge, CultureInfo.InvariantCulture);
            return countLarge >= int.MaxValue ? int.MaxValue : Convert.ToInt32(countLarge, CultureInfo.InvariantCulture);
        }
        catch (COMException)
        {
            return Convert.ToInt32(((dynamic)range).Count, CultureInfo.InvariantCulture);
        }
    }

    private static int CountWorksheetFormulaCells(object worksheet)
    {
        object? usedRange = null;
        try
        {
            usedRange = ((dynamic)worksheet).UsedRange;
            var specialCellsCount = TryCountWorksheetFormulaSpecialCells(usedRange);
            if (specialCellsCount > 0)
                return specialCellsCount;

            var evaluatedCount = TryCountWorksheetFormulaIsFormula(worksheet, usedRange);
            if (evaluatedCount >= 0)
                return evaluatedCount;

            try
            {
                return CountFormulaPropertyValues(((dynamic)usedRange).Formula);
            }
            catch (COMException)
            {
                return 0;
            }
        }
        finally
        {
            ReleaseComObject(usedRange);
        }
    }

    private static int TryCountWorksheetFormulaSpecialCells(object usedRange)
    {
        object? formulaCells = null;
        try
        {
            formulaCells = ((dynamic)usedRange).SpecialCells(ExcelCellTypeFormulas);
            return Convert.ToInt32(((dynamic)formulaCells).Count, CultureInfo.InvariantCulture);
        }
        catch (COMException)
        {
            return 0;
        }
        finally
        {
            ReleaseComObject(formulaCells);
        }
    }

    private static int TryCountWorksheetFormulaIsFormula(object worksheet, object usedRange)
    {
        try
        {
            var address = Convert.ToString(((dynamic)usedRange).Address(false, false), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(address))
                return 0;

            var result = ((dynamic)worksheet).Evaluate($"SUMPRODUCT(--ISFORMULA({address}))");
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        catch (COMException)
        {
            return -1;
        }
    }

    private static int CountFormulaPropertyValues(object? formulas)
    {
        if (formulas is string formula)
            return IsFormulaText(formula) ? 1 : 0;

        if (formulas is not Array formulaArray)
            return 0;

        var count = 0;
        foreach (var item in formulaArray)
        {
            if (item is string value && IsFormulaText(value))
                count++;
        }

        return count;
    }

    private static bool IsFormulaText(string value) =>
        value.StartsWith("=", StringComparison.Ordinal);

    private static FreeXSaveResult SaveThroughFreeX(string sourcePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var adapter = new XlsxFileAdapter();
        XlsxLoadResult loadResult;
        using (var input = File.OpenRead(sourcePath))
        {
            loadResult = adapter.LoadWithWarnings(input);
        }

        var workbook = loadResult.Workbook;
        var summary = SummarizeWorkbook(workbook);
        AddFreeXSaveMarker(workbook);
        var outputPath = CreateDerivedOutputPath(outputDirectory, sourcePath, "freex-saved");
        using (var output = File.Create(outputPath))
        {
            adapter.Save(workbook, output);
        }

        return new FreeXSaveResult(outputPath, summary, loadResult.Warnings);
    }

    private static FreeXLoadSummaryResult LoadWorkbookSummary(string sourcePath)
    {
        using var input = File.OpenRead(sourcePath);
        var result = new XlsxFileAdapter().LoadWithWarnings(input);
        return new FreeXLoadSummaryResult(SummarizeWorkbook(result.Workbook), result.Warnings);
    }

    private static FreeXWorkbookSummary SummarizeWorkbook(Workbook workbook)
    {
        var formatting = CountFreeXWorkbookFormatting(workbook);
        return new FreeXWorkbookSummary(
            workbook.SheetCount,
            workbook.Sheets.Sum(sheet => sheet.CellCount),
            workbook.Sheets.Sum(sheet => sheet.FormulaCellCount),
            workbook.NamedRanges.Count,
            workbook.Sheets.Sum(sheet => sheet.Charts.Count),
            workbook.Sheets.Sum(sheet => sheet.StructuredTables.Count),
            workbook.Sheets.Count(sheet => sheet.AutoFilter is not null || sheet.StructuredTables.Any(table => table.HasAutoFilter)),
            workbook.Sheets.Sum(sheet => sheet.DataValidations.Count),
            workbook.Sheets.Sum(sheet => sheet.ConditionalFormats.Count),
            workbook.Sheets.Sum(sheet => sheet.Hyperlinks.Count),
            workbook.Sheets.Sum(sheet => sheet.Comments.Count + sheet.ThreadedComments.Count),
            workbook.Sheets.Sum(sheet => sheet.Pictures.Count),
            workbook.Sheets.Sum(sheet => sheet.Sparklines.Count),
            workbook.Sheets.Sum(sheet => sheet.TextBoxes.Count),
            workbook.Sheets.Sum(sheet => sheet.DrawingShapes.Count),
            workbook.Sheets.Count(sheet => sheet.IsProtected),
            workbook.IsStructureProtected ? 1 : 0,
            workbook.Sheets.Sum(sheet => sheet.MergedRegions.Count),
            workbook.Sheets.Count(sheet => sheet.FrozenRows > 0 || sheet.FrozenCols > 0),
            workbook.Sheets.Sum(sheet => sheet.HiddenRows.Concat(sheet.FilterHiddenRows).Concat(sheet.GroupHiddenRows).Distinct().Count()),
            workbook.Sheets.Sum(sheet => sheet.HiddenCols.Concat(sheet.GroupHiddenCols).Distinct().Count()),
            workbook.Sheets.Sum(sheet => sheet.RowHeights.Count),
            workbook.Sheets.Sum(sheet => sheet.ColumnWidths.Count),
            workbook.Sheets.Sum(sheet => sheet.RowOutlineLevels.Count),
            workbook.Sheets.Sum(sheet => sheet.ColOutlineLevels.Count),
            formatting.StyledCellCount,
            formatting.NumberFormatCellCount,
            formatting.BoldCellCount,
            formatting.FilledCellCount,
            formatting.BorderedCellCount,
            formatting.AlignedCellCount,
            formatting.WrappedCellCount,
            workbook.Sheets.Sum(sheet => sheet.PivotTables.Count),
            workbook.PivotCaches.Count);
    }

    private static FreeXFormattingSummary CountFreeXWorkbookFormatting(Workbook workbook)
    {
        var styledCells = 0;
        var numberFormatCells = 0;
        var boldCells = 0;
        var filledCells = 0;
        var borderedCells = 0;
        var alignedCells = 0;
        var wrappedCells = 0;

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var (_, cell) in sheet.EnumerateCells())
            {
                CountFreeXCellStyle(workbook, cell.StyleId);
            }

            foreach (var (_, styleId) in sheet.GetStyleOnlyEntries())
            {
                CountFreeXCellStyle(workbook, styleId);
            }
        }

        return new FreeXFormattingSummary(
            styledCells,
            numberFormatCells,
            boldCells,
            filledCells,
            borderedCells,
            alignedCells,
            wrappedCells);

        void CountFreeXCellStyle(Workbook workbook, StyleId styleId)
        {
            if (styleId == StyleId.Default)
                return;

            styledCells++;
            var style = workbook.GetStyle(styleId);
            if (IsNonGeneralNumberFormat(style.NumberFormat))
                numberFormatCells++;
            if (style.Bold)
                boldCells++;
            if (HasVisibleFill(style))
                filledCells++;
            if (HasVisibleBorder(style))
                borderedCells++;
            if (HasExplicitAlignment(style))
                alignedCells++;
            if (style.WrapText)
                wrappedCells++;
        }
    }

    private static bool IsNonGeneralNumberFormat(string? numberFormat) =>
        !string.IsNullOrWhiteSpace(numberFormat) &&
        !string.Equals(numberFormat, "General", StringComparison.OrdinalIgnoreCase);

    private static bool HasVisibleFill(CellStyle style) =>
        style.FillColor is not null ||
        style.FillThemeColor is not null ||
        style.FillPatternStyle != CellFillPatternStyle.None ||
        style.FillPatternColor is not null ||
        style.FillPatternThemeColor is not null;

    private static bool HasVisibleBorder(CellStyle style) =>
        style.BorderTop.Style != BorderStyle.None ||
        style.BorderRight.Style != BorderStyle.None ||
        style.BorderBottom.Style != BorderStyle.None ||
        style.BorderLeft.Style != BorderStyle.None;

    private static bool HasExplicitAlignment(CellStyle style) =>
        style.HorizontalAlignment != HorizontalAlignment.General ||
        style.VerticalAlignment != VerticalAlignment.Bottom;

    private static void AddFreeXSaveMarker(Workbook workbook)
    {
        var markerName = "FreeXSmoke";
        for (var suffix = 2; workbook.GetSheet(markerName) is not null; suffix++)
            markerName = $"FreeXSmoke{suffix}";

        var marker = workbook.AddSheet(markerName);
        marker.SetCell(new CellAddress(marker.Id, 1, 1), new TextValue("FreeX save marker"));
        marker.SetCell(new CellAddress(marker.Id, 2, 1), new TextValue("XlsxFileAdapter wrote this validation copy."));
    }

    private static string CopyToStagingDirectory(string inputFile, string stagingDirectory)
    {
        var fileName = Path.GetFileName(inputFile);
        var stagedPath = Path.Combine(stagingDirectory, fileName);
        if (File.Exists(stagedPath))
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            stagedPath = Path.Combine(stagingDirectory, $"{name}-{Guid.NewGuid():N}{extension}");
        }

        File.Copy(inputFile, stagedPath, overwrite: false);
        return stagedPath;
    }

    private static string CreateDerivedOutputPath(string outputDirectory, string sourcePath, string suffix)
    {
        Directory.CreateDirectory(outputDirectory);
        var name = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
        var candidate = Path.Combine(outputDirectory, $"{name}-{suffix}.xlsx");
        if (!File.Exists(candidate))
            return candidate;

        return Path.Combine(outputDirectory, $"{name}-{suffix}-{Guid.NewGuid():N}.xlsx");
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            value = value.Replace(invalidChar, '_');

        return string.IsNullOrWhiteSpace(value) ? "workbook" : value;
    }

    private static IReadOnlyList<string> ResolveInputFiles(IReadOnlyList<string> inputs, string pattern)
    {
        var files = new List<string>();
        foreach (var input in inputs)
        {
            var fullPath = Path.GetFullPath(input);
            if (Directory.Exists(fullPath))
            {
                files.AddRange(Directory.GetFiles(fullPath, pattern, SearchOption.TopDirectoryOnly));
                continue;
            }

            if (!File.Exists(fullPath))
                throw new ArgumentException($"Input path was not found: {input}");

            if (!string.Equals(Path.GetExtension(fullPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Input is not an .xlsx file: {input}");

            files.Add(fullPath);
        }

        return files
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddUniqueInput(List<WorkbookSmokeInput> inputs, WorkbookSmokeInput candidate)
    {
        if (inputs.Any(existing =>
                string.Equals(existing.SourcePath, candidate.SourcePath, StringComparison.OrdinalIgnoreCase) &&
                existing.Workflow == candidate.Workflow))
        {
            return;
        }

        inputs.Add(candidate);
    }

    private static string DescribeGeneratedFixture(string description, WorkbookValidationWorkflow workflow) =>
        workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel
            ? $"{description} via FreeX resave"
            : description;

    private static WorkbookSmokeInput WithCorpusExpectations(WorkbookSmokeInput input, bool saveReopen)
    {
        if (input.CorpusRow is not { } corpusRow)
            return input;

        var expectations = ExpectationsForCorpusRow(corpusRow, saveReopen, input.Workflow);
        return expectations is null
            ? input
            : input with { Expectations = expectations };
    }

    private static WorkbookSmokeExpectations? ExpectationsForCorpusRow(
        CorpusManifestRow row,
        bool saveReopen,
        WorkbookValidationWorkflow workflow)
    {
        if (string.Equals(row.Id, "local-private-sample-dashboard-20250116", StringComparison.OrdinalIgnoreCase))
        {
            return ApplyFreeXLoadWarningExpectation(
                row,
                PartnerDashboardExpectations(
                    saveReopen,
                    expectFreeXPreSave: workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel));
        }

        if (IsSupportedMetadataPass(row))
        {
            return ApplyFreeXLoadWarningExpectation(
                row,
                SupportedMetadataCorpusExpectations(row, saveReopen));
        }

        if (HasSupportedFeatureExpectations(row))
        {
            var supportedExpectations = SupportedCorpusExpectations(
                row,
                saveReopen,
                expectFreeXPreSave: workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel,
                expectFreeXReopened: saveReopen);
            supportedExpectations = ApplySupportedCorpusRowExpectations(row, supportedExpectations, saveReopen, workflow);
            return ApplyFreeXLoadWarningExpectation(row, supportedExpectations);
        }

        return ApplyFreeXLoadWarningExpectation(row, null);
    }

    private static WorkbookSmokeExpectations? ApplySupportedCorpusRowExpectations(
        CorpusManifestRow row,
        WorkbookSmokeExpectations? expectations,
        bool saveReopen,
        WorkbookValidationWorkflow workflow)
    {
        if (!string.Equals(row.Id, "generated-table-autofilter-003", StringComparison.OrdinalIgnoreCase))
            return expectations;

        var expectFreeXPreSave = workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel;
        return (expectations ?? new WorkbookSmokeExpectations()) with
        {
            MinFreeXPreSaveStructuredTables = expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveAutoFilterSheets = expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveHiddenRows = expectFreeXPreSave ? 2 : 0,
            MinExcelOpenedStructuredTables = 1,
            MinExcelOpenedAutoFilterSheets = 1,
            MinExcelOpenedHiddenRows = 2,
            MinExcelReopenedStructuredTables = saveReopen ? 1 : 0,
            MinExcelReopenedAutoFilterSheets = saveReopen ? 1 : 0,
            MinExcelReopenedHiddenRows = saveReopen ? 2 : 0,
            MinFreeXReopenedStructuredTables = saveReopen ? 1 : 0,
            MinFreeXReopenedAutoFilterSheets = saveReopen ? 1 : 0,
            MinFreeXReopenedHiddenRows = saveReopen ? 2 : 0
        };
    }

    private static bool IsSupportedMetadataPass(CorpusManifestRow row) =>
        string.Equals(row.ExpectedStatus, "supported-metadata-pass", StringComparison.OrdinalIgnoreCase);

    private static bool HasSupportedFeatureExpectations(CorpusManifestRow row) =>
        string.Equals(row.ExpectedStatus, "supported-pass", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(row.ExpectedStatus, "public-pass", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(row.ExpectedStatus, "supported-pivot-metadata-pass", StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(row.ExpectedStatus, "supported-metadata-pass", StringComparison.OrdinalIgnoreCase) &&
         HasConcreteMetadataFeatureExpectations(row));

    private static bool HasConcreteMetadataFeatureExpectations(CorpusManifestRow row)
    {
        var tags = row.FeatureTags
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tags.Contains("native-metadata") ||
            tags.Contains("workbook-native-metadata") ||
            tags.Contains("worksheet-native-metadata") ||
            tags.Contains("stylesheet-native-metadata"))
        {
            return false;
        }

        return
            tags.Contains("charts") ||
            tags.Contains("data-validation") ||
            tags.Contains("conditional-formatting");
    }

    private static WorkbookSmokeExpectations? SupportedMetadataCorpusExpectations(
        CorpusManifestRow row,
        bool saveReopen)
    {
        var expectations = HasConcreteMetadataFeatureExpectations(row)
            ? SupportedCorpusExpectations(
                row,
                saveReopen,
                expectFreeXPreSave: false,
                expectFreeXReopened: false)
            : null;

        WorkbookSmokeExpectations EnsureExpectations() => expectations ??= new WorkbookSmokeExpectations();
        var reopen = saveReopen ? 1 : 0;

        if (string.Equals(row.Id, "generated-workbook-protection-native-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinExcelOpenedStructureProtection = 1,
                MinExcelReopenedStructureProtection = reopen
            };
        }
        else if (string.Equals(row.Id, "generated-worksheet-protection-native-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinExcelOpenedProtectedSheets = 1,
                MinExcelReopenedProtectedSheets = reopen
            };
        }
        else if (string.Equals(row.Id, "generated-worksheet-protected-ranges-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinExcelOpenedAllowEditRanges = 1,
                MinExcelReopenedAllowEditRanges = reopen
            };
        }
        else if (string.Equals(row.Id, "generated-workbook-defined-names-native-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinExcelOpenedNamedRanges = 1,
                MinExcelReopenedNamedRanges = reopen
            };
        }
        else if (string.Equals(row.Id, "generated-worksheet-header-footer-native-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinExcelOpenedHeaderFooterSheets = 1,
                MinExcelReopenedHeaderFooterSheets = reopen
            };
        }
        else if (string.Equals(row.Id, "generated-worksheet-extension-list-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinExcelOpenedSparklines = 1,
                MinExcelReopenedSparklines = reopen
            };
        }
        else if (string.Equals(row.Id, "generated-worksheet-auto-filter-metadata-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinFreeXPreSaveAutoFilterSheets = 1,
                MinExcelOpenedAutoFilterSheets = 1,
                MinExcelReopenedAutoFilterSheets = reopen,
                MinFreeXReopenedAutoFilterSheets = reopen
            };
        }
        else if (string.Equals(row.Id, "generated-printer-settings-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "xl/printerSettings/printerSettings1.bin",
                    "xl/worksheets/_rels/sheet1.xml.rels"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "xl/printerSettings/printerSettings1.bin",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings",
                        "xl/printerSettings/printerSettings1.bin",
                        Id: "rIdPrinterSettings1")
                ],
                RequiredExcelSavedPackageParts =
                [
                    "xl/printerSettings/printerSettings1.bin",
                    "xl/worksheets/_rels/sheet1.xml.rels"
                ],
                RequiredExcelSavedPackageContentTypes =
                [
                    new(
                        "xl/printerSettings/printerSettings1.bin",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings")
                ],
                RequiredExcelSavedPackageRelationships =
                [
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings",
                        "xl/printerSettings/printerSettings1.bin")
                ]
            };
        }
        else if (string.Equals(row.Id, "generated-calc-chain-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "xl/calcChain.xml"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "xl/calcChain.xml",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain",
                        "xl/calcChain.xml")
                ],
                RequiredExcelSavedPackageParts =
                [
                    "xl/calcChain.xml"
                ],
                RequiredExcelSavedPackageContentTypes =
                [
                    new(
                        "xl/calcChain.xml",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.calcChain+xml")
                ],
                RequiredExcelSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain",
                        "xl/calcChain.xml")
                ]
            };
        }
        else if (string.Equals(row.Id, "generated-document-properties-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "docProps/core.xml",
                    "docProps/app.xml"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "docProps/core.xml",
                        "application/vnd.openxmlformats-package.core-properties+xml"),
                    new(
                        "docProps/app.xml",
                        "application/vnd.openxmlformats-officedocument.extended-properties+xml")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "_rels/.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties",
                        "docProps/app.xml")
                ],
                RequiredExcelSavedPackageContentTypes =
                [
                    new(
                        "docProps/core.xml",
                        "application/vnd.openxmlformats-package.core-properties+xml"),
                    new(
                        "docProps/app.xml",
                        "application/vnd.openxmlformats-officedocument.extended-properties+xml")
                ],
                RequiredExcelSavedPackageRelationships =
                [
                    new(
                        "_rels/.rels",
                        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
                        "docProps/core.xml"),
                    new(
                        "_rels/.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties",
                        "docProps/app.xml")
                ]
            };
        }
        else if (string.Equals(row.Id, "generated-header-footer-legacy-drawing-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "xl/drawings/vmlDrawing1.vml",
                    "xl/drawings/_rels/vmlDrawing1.vml.rels",
                    "xl/media/headerFooterImage1.png",
                    "xl/worksheets/_rels/sheet1.xml.rels"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "xl/drawings/vmlDrawing1.vml",
                        "application/vnd.openxmlformats-officedocument.vmlDrawing"),
                    new(
                        "xl/media/headerFooterImage1.png",
                        "image/png")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing",
                        "xl/drawings/vmlDrawing1.vml",
                        Id: "rIdHeaderFooterDrawing1"),
                    new(
                        "xl/drawings/_rels/vmlDrawing1.vml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                        "xl/media/headerFooterImage1.png",
                        Id: "rIdImage1")
                ],
                RequiredExcelSavedPackageParts =
                [
                    "xl/drawings/vmlDrawing1.vml",
                    "xl/worksheets/_rels/sheet1.xml.rels"
                ],
                RequiredExcelSavedPackageContentTypes =
                [
                    new(
                        "xl/drawings/vmlDrawing1.vml",
                        "application/vnd.openxmlformats-officedocument.vmlDrawing")
                ],
                RequiredExcelSavedPackageRelationships =
                [
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing",
                        "xl/drawings/vmlDrawing1.vml")
                ]
            };
        }
        else if (string.Equals(row.Id, "generated-worksheet-legacy-drawing-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "xl/drawings/vmlDrawing1.vml",
                    "xl/drawings/_rels/vmlDrawing1.vml.rels",
                    "xl/media/vmlImage1.png",
                    "xl/worksheets/_rels/sheet1.xml.rels"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "xl/drawings/vmlDrawing1.vml",
                        "application/vnd.openxmlformats-officedocument.vmlDrawing"),
                    new(
                        "xl/media/vmlImage1.png",
                        "image/png")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing",
                        "xl/drawings/vmlDrawing1.vml",
                        Id: "rIdFreeXLegacyDrawing"),
                    new(
                        "xl/drawings/_rels/vmlDrawing1.vml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image",
                        "xl/media/vmlImage1.png",
                        Id: "rIdFreeXVmlImage")
                ]
            };
        }
        else if (string.Equals(row.Id, "generated-slicers-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "xl/slicers/slicer1.xml",
                    "xl/slicerCaches/slicerCache1.xml"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "xl/slicers/slicer1.xml",
                        "application/vnd.ms-excel.slicer+xml"),
                    new(
                        "xl/slicerCaches/slicerCache1.xml",
                        "application/vnd.ms-excel.slicerCache+xml")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.microsoft.com/office/2007/relationships/slicerCache",
                        "xl/slicerCaches/slicerCache1.xml",
                        Id: "rIdFreeXSlicerCache1"),
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
                        "xl/drawings/drawing1.xml",
                        Id: "rIdFreeXFloatingDrawing1"),
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.microsoft.com/office/2007/relationships/slicer",
                        "xl/slicers/slicer1.xml",
                        Id: "rIdFreeXSlicerView1")
                ],
                RequiredExcelSavedPackageParts =
                [
                    "xl/slicers/slicer1.xml",
                    "xl/slicerCaches/slicerCache1.xml"
                ],
                RequiredExcelSavedPackageContentTypes =
                [
                    new(
                        "xl/slicers/slicer1.xml",
                        "application/vnd.ms-excel.slicer+xml"),
                    new(
                        "xl/slicerCaches/slicerCache1.xml",
                        "application/vnd.ms-excel.slicerCache+xml")
                ],
                RequiredExcelSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.microsoft.com/office/2007/relationships/slicerCache",
                        "xl/slicerCaches/slicerCache1.xml"),
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
                        "xl/drawings/drawing1.xml"),
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.microsoft.com/office/2007/relationships/slicer",
                        "xl/slicers/slicer1.xml")
                ]
            };
        }
        else if (string.Equals(row.Id, "generated-timelines-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "xl/timelines/timeline1.xml",
                    "xl/timelineCaches/timelineCache1.xml"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "xl/timelines/timeline1.xml",
                        "application/vnd.ms-excel.timeline+xml"),
                    new(
                        "xl/timelineCaches/timelineCache1.xml",
                        "application/vnd.ms-excel.timelineCache+xml")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.microsoft.com/office/2010/relationships/TimelineCache",
                        "xl/timelineCaches/timelineCache1.xml",
                        Id: "rIdFreeXTimelineCache1"),
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
                        "xl/drawings/drawing1.xml",
                        Id: "rIdFreeXFloatingDrawing1"),
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.microsoft.com/office/2010/relationships/Timeline",
                        "xl/timelines/timeline1.xml",
                        Id: "rIdFreeXTimelineView1"),
                    new(
                        "xl/drawings/_rels/drawing1.xml.rels",
                        "http://schemas.microsoft.com/office/2010/relationships/Timeline",
                        "xl/timelines/timeline1.xml",
                        Id: "rIdFreeXNativeControl1")
                ],
                RequiredExcelSavedPackageParts =
                [
                    "xl/timelines/timeline1.xml",
                    "xl/timelineCaches/timelineCache1.xml"
                ],
                RequiredExcelSavedPackageContentTypes =
                [
                    new(
                        "xl/timelines/timeline1.xml",
                        "application/vnd.ms-excel.timeline+xml"),
                    new(
                        "xl/timelineCaches/timelineCache1.xml",
                        "application/vnd.ms-excel.timelineCache+xml")
                ],
                RequiredExcelSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.microsoft.com/office/2010/relationships/TimelineCache",
                        "xl/timelineCaches/timelineCache1.xml"),
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing",
                        "xl/drawings/drawing1.xml"),
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.microsoft.com/office/2010/relationships/Timeline",
                        "xl/timelines/timeline1.xml"),
                    new(
                        "xl/drawings/_rels/drawing1.xml.rels",
                        "http://schemas.microsoft.com/office/2010/relationships/Timeline",
                        "xl/timelines/timeline1.xml")
                ]
            };
        }
        else if (string.Equals(row.Id, "generated-external-links-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "xl/externalLinks/externalLink1.xml",
                    "xl/externalLinks/_rels/externalLink1.xml.rels"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "xl/externalLinks/externalLink1.xml",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink",
                        "xl/externalLinks/externalLink1.xml",
                        Id: "rIdFreeXExternalLink1"),
                    new(
                        "xl/externalLinks/_rels/externalLink1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath",
                        "ExternalWorkbook.xlsx",
                        Id: "rIdExternalBook1",
                        TargetMode: "External")
                ],
                RequiredExcelSavedPackageParts =
                [
                    "xl/externalLinks/externalLink1.xml",
                    "xl/externalLinks/_rels/externalLink1.xml.rels"
                ],
                RequiredExcelSavedPackageContentTypes =
                [
                    new(
                        "xl/externalLinks/externalLink1.xml",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml")
                ],
                RequiredExcelSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink",
                        "xl/externalLinks/externalLink1.xml"),
                    new(
                        "xl/externalLinks/_rels/externalLink1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLinkPath",
                        "ExternalWorkbook.xlsx",
                        TargetMode: "External")
                ]
            };
        }
        else if (string.Equals(row.Id, "generated-custom-xml-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "customXml/item1.xml",
                    "customXml/itemProps1.xml",
                    "customXml/_rels/item1.xml.rels"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "customXml/item1.xml",
                        "application/xml"),
                    new(
                        "customXml/itemProps1.xml",
                        "application/vnd.openxmlformats-officedocument.customXmlProperties+xml")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
                        "customXml/item1.xml",
                        Id: "rIdFreeXCustomXml1"),
                    new(
                        "customXml/_rels/item1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps",
                        "customXml/itemProps1.xml",
                        Id: "rIdCustomXmlProps1")
                ],
                RequiredExcelSavedPackageParts =
                [
                    "customXml/item1.xml",
                    "customXml/itemProps1.xml",
                    "customXml/_rels/item1.xml.rels"
                ],
                RequiredExcelSavedPackageContentTypes =
                [
                    new(
                        "customXml/item1.xml",
                        "application/xml"),
                    new(
                        "customXml/itemProps1.xml",
                        "application/vnd.openxmlformats-officedocument.customXmlProperties+xml")
                ],
                RequiredExcelSavedPackageRelationships =
                [
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
                        "customXml/item1.xml"),
                    new(
                        "customXml/_rels/item1.xml.rels",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps",
                        "customXml/itemProps1.xml")
                ]
            };
        }
        else if (string.Equals(row.Id, "generated-table-ref-formulas-package-003", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinFreeXPreSaveFormulaCells = 2,
                MinFreeXPreSaveStructuredTables = 1,
                MinExcelOpenedFormulaCells = 2,
                MinExcelOpenedStructuredTables = 1,
                MinExcelReopenedFormulaCells = saveReopen ? 2 : 0,
                MinExcelReopenedStructuredTables = reopen,
                MinFreeXReopenedFormulaCells = saveReopen ? 2 : 0,
                MinFreeXReopenedStructuredTables = reopen
            };
        }
        else if (string.Equals(row.Id, "generated-cross-sheet-range-package-003", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinFreeXPreSaveFormulaCells = 2,
                MinExcelOpenedFormulaCells = 2,
                MinExcelReopenedFormulaCells = saveReopen ? 2 : 0,
                MinFreeXReopenedFormulaCells = saveReopen ? 2 : 0
            };
        }
        else if (string.Equals(row.Id, "generated-named-range-count-package-003", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinFreeXPreSaveNamedRanges = 12,
                MinExcelOpenedNamedRanges = 12,
                MinExcelReopenedNamedRanges = saveReopen ? 12 : 0,
                MinFreeXReopenedNamedRanges = saveReopen ? 12 : 0
            };
        }

        return expectations;
    }

    private static WorkbookSmokeExpectations? ApplyFreeXLoadWarningExpectation(
        CorpusManifestRow row,
        WorkbookSmokeExpectations? expectations)
    {
        if (!RequiresNoFreeXLoadWarnings(row))
            return expectations;

        return expectations is null
            ? new WorkbookSmokeExpectations(RequireNoFreeXLoadWarnings: true)
            : expectations with { RequireNoFreeXLoadWarnings = true };
    }

    private static bool RequiresNoFreeXLoadWarnings(CorpusManifestRow row) =>
        string.IsNullOrWhiteSpace(row.ExpectedWarnings) &&
        (string.Equals(row.ExpectedStatus, "supported-pass", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(row.ExpectedStatus, "supported-metadata-pass", StringComparison.OrdinalIgnoreCase) ||
          string.Equals(row.ExpectedStatus, "supported-pivot-metadata-pass", StringComparison.OrdinalIgnoreCase) ||
          (string.Equals(row.ExpectedStatus, "public-pass", StringComparison.OrdinalIgnoreCase) &&
           !HasWarningToleratedFeatureTags(row)));

    private static bool HasWarningToleratedFeatureTags(CorpusManifestRow row)
    {
        var tags = row.FeatureTags
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return
            tags.Contains("unsupported-chart-family") ||
            tags.Contains("embedded-objects") ||
            tags.Contains("threaded-comments") ||
            tags.Contains("track-changes") ||
            tags.Contains("revision-history") ||
            tags.Contains("form-controls") ||
            tags.Contains("activex") ||
            tags.Contains("digital-signatures") ||
            tags.Contains("custom-ribbon-ui") ||
            tags.Contains("office-addins") ||
            tags.Contains("webextensions") ||
            tags.Contains("live-web-queries") ||
            tags.Contains("web-publish") ||
            tags.Contains("sensitivity-labels") ||
            tags.Contains("irm") ||
            tags.Contains("smartart") ||
            tags.Contains("diagrams") ||
            tags.Contains("chart-sheets") ||
            tags.Contains("dialog-sheets") ||
            tags.Contains("macro-sheets") ||
            tags.Contains("unsupported-sheet-types") ||
            tags.Contains("macros") ||
            tags.Contains("power-query") ||
            tags.Contains("connections") ||
            tags.Contains("data-model") ||
            tags.Contains("power-pivot") ||
            tags.Contains("linked-data-types") ||
            tags.Contains("rich-data");
    }

    private static WorkbookSmokeExpectations? SupportedCorpusExpectations(
        CorpusManifestRow row,
        bool saveReopen,
        bool expectFreeXPreSave,
        bool expectFreeXReopened)
    {
        var tags = row.FeatureTags
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool HasTag(string tag) => tags.Contains(tag);
        var minFormulaCells = HasTag("formulas") ? 1 : 0;
        var minNamedRanges = HasTag("named-ranges") ? 1 : 0;
        var minStructuredTables = HasTag("structured-tables") || HasTag("listobjects") || HasTag("tables") ? 1 : 0;
        var minAutoFilterSheets = HasTag("autofilter") ? 1 : 0;
        var minCharts = HasTag("charts") ? 1 : 0;
        var minDataValidations = HasTag("data-validation") ? 1 : 0;
        var minConditionalFormats = HasTag("conditional-formatting") ? 1 : 0;
        var minHyperlinks = HasTag("hyperlinks") ? 1 : 0;
        var minComments = HasTag("comments") || HasTag("notes") ? 1 : 0;
        var minPictures = HasTag("images") ? 1 : 0;
        var minSparklines = HasTag("sparklines") ? 1 : 0;
        var minTextBoxes = HasTag("text-boxes") ? 1 : 0;
        var minDrawingShapes = HasTag("shapes") ? 1 : 0;
        var minPrintAreaSheets = HasTag("page-setup") ? 1 : 0;
        var minPrintTitleSheets = HasTag("print-titles") ? 1 : 0;
        var minLandscapeSheets = HasTag("page-setup") ? 1 : 0;
        var minScaleToFitSheets = HasTag("page-setup") ? 1 : 0;
        var minPrintOptionsSheets = HasTag("page-setup") ? 1 : 0;
        var minHeaderFooterSheets = HasTag("page-setup") || HasTag("headers-footers") ? 1 : 0;
        var minManualPageBreaks = HasTag("page-breaks") ? 1 : 0;
        var minAllowEditRanges = HasTag("allow-edit-ranges") || HasTag("protected-ranges") ? 1 : 0;
        var minMergedAreas = HasTag("merged-cells") ? 1 : 0;
        var minFreezePaneSheets = HasTag("freeze-panes") ? 1 : 0;
        var minHiddenRows = HasTag("hidden-rows") ? 1 : 0;
        var minHiddenColumns = HasTag("hidden-columns") || HasTag("hidden-cols") ? 1 : 0;
        var minCustomRowHeights = HasTag("custom-dimensions") || HasTag("custom-row-heights") ? 1 : 0;
        var minCustomColumnWidths = HasTag("custom-dimensions") || HasTag("custom-column-widths") ? 1 : 0;
        var minOutlineRows = HasTag("outline-groups") || HasTag("row-column-groups") ? 1 : 0;
        var minOutlineColumns = HasTag("outline-groups") || HasTag("row-column-groups") ? 1 : 0;
        var minStyledCells = HasTag("formatting") || HasTag("styles") || HasTag("number-formats") ? 1 : 0;
        var minNumberFormatCells = HasTag("number-formats") ? 1 : 0;
        var minBoldCells = HasTag("bold-cells") || HasTag("font-bold") ? 1 : 0;
        var minFilledCells = HasTag("fills") || HasTag("fill-color") ? 1 : 0;
        var minBorderedCells = HasTag("borders") ? 1 : 0;
        var minAlignedCells = HasTag("alignment") || HasTag("aligned-cells") ? 1 : 0;
        var minWrappedCells = HasTag("wrapped-text") || HasTag("wrap-text") ? 1 : 0;
        var minProtectedSheets = HasTag("protection") ? 1 : 0;
        var minStructureProtection = HasTag("protection") ? 1 : 0;
        var minPivotTables = HasTag("pivottables") ? 1 : 0;
        var minPivotCaches = HasTag("pivot-caches") ? 1 : 0;
        var minExcelShapes =
            HasTag("charts") ||
            HasTag("images") ||
            HasTag("text-boxes") ||
            HasTag("shapes") ||
            HasTag("comments") ||
            HasTag("notes")
                ? 1
                : 0;

        if (minFormulaCells == 0 &&
            minNamedRanges == 0 &&
            minStructuredTables == 0 &&
            minAutoFilterSheets == 0 &&
            minCharts == 0 &&
            minDataValidations == 0 &&
            minConditionalFormats == 0 &&
            minHyperlinks == 0 &&
            minComments == 0 &&
            minPictures == 0 &&
            minSparklines == 0 &&
            minTextBoxes == 0 &&
            minDrawingShapes == 0 &&
            minPrintAreaSheets == 0 &&
            minPrintTitleSheets == 0 &&
            minLandscapeSheets == 0 &&
            minScaleToFitSheets == 0 &&
            minPrintOptionsSheets == 0 &&
            minHeaderFooterSheets == 0 &&
            minManualPageBreaks == 0 &&
            minAllowEditRanges == 0 &&
            minMergedAreas == 0 &&
            minFreezePaneSheets == 0 &&
            minHiddenRows == 0 &&
            minHiddenColumns == 0 &&
            minCustomRowHeights == 0 &&
            minCustomColumnWidths == 0 &&
            minOutlineRows == 0 &&
            minOutlineColumns == 0 &&
            minStyledCells == 0 &&
            minNumberFormatCells == 0 &&
            minBoldCells == 0 &&
            minFilledCells == 0 &&
            minBorderedCells == 0 &&
            minAlignedCells == 0 &&
            minWrappedCells == 0 &&
            minProtectedSheets == 0 &&
            minStructureProtection == 0 &&
            minPivotTables == 0 &&
            minPivotCaches == 0 &&
            minExcelShapes == 0)
        {
            return null;
        }

        return new WorkbookSmokeExpectations(
            MinFreeXPreSaveFormulaCells: expectFreeXPreSave ? minFormulaCells : 0,
            MinFreeXPreSaveNamedRanges: expectFreeXPreSave ? minNamedRanges : 0,
            MinFreeXPreSaveStructuredTables: expectFreeXPreSave ? minStructuredTables : 0,
            MinFreeXPreSaveAutoFilterSheets: expectFreeXPreSave ? minAutoFilterSheets : 0,
            MinFreeXPreSaveCharts: expectFreeXPreSave ? minCharts : 0,
            MinFreeXPreSaveDataValidations: expectFreeXPreSave ? minDataValidations : 0,
            MinFreeXPreSaveConditionalFormats: expectFreeXPreSave ? minConditionalFormats : 0,
            MinFreeXPreSaveHyperlinks: expectFreeXPreSave ? minHyperlinks : 0,
            MinFreeXPreSaveComments: expectFreeXPreSave ? minComments : 0,
            MinFreeXPreSavePictures: expectFreeXPreSave ? minPictures : 0,
            MinFreeXPreSaveSparklines: expectFreeXPreSave ? minSparklines : 0,
            MinFreeXPreSaveTextBoxes: expectFreeXPreSave ? minTextBoxes : 0,
            MinFreeXPreSaveDrawingShapes: expectFreeXPreSave ? minDrawingShapes : 0,
            MinFreeXPreSaveProtectedSheets: expectFreeXPreSave ? minProtectedSheets : 0,
            MinFreeXPreSaveStructureProtection: expectFreeXPreSave ? minStructureProtection : 0,
            MinFreeXPreSaveMergedRegions: expectFreeXPreSave ? minMergedAreas : 0,
            MinFreeXPreSaveFrozenSheets: expectFreeXPreSave ? minFreezePaneSheets : 0,
            MinFreeXPreSaveHiddenRows: expectFreeXPreSave ? minHiddenRows : 0,
            MinFreeXPreSaveHiddenColumns: expectFreeXPreSave ? minHiddenColumns : 0,
            MinFreeXPreSaveCustomRowHeights: expectFreeXPreSave ? minCustomRowHeights : 0,
            MinFreeXPreSaveCustomColumnWidths: expectFreeXPreSave ? minCustomColumnWidths : 0,
            MinFreeXPreSaveOutlineRows: expectFreeXPreSave ? minOutlineRows : 0,
            MinFreeXPreSaveOutlineColumns: expectFreeXPreSave ? minOutlineColumns : 0,
            MinFreeXPreSaveStyledCells: expectFreeXPreSave ? minStyledCells : 0,
            MinFreeXPreSaveNumberFormatCells: expectFreeXPreSave ? minNumberFormatCells : 0,
            MinFreeXPreSaveBoldCells: expectFreeXPreSave ? minBoldCells : 0,
            MinFreeXPreSaveFilledCells: expectFreeXPreSave ? minFilledCells : 0,
            MinFreeXPreSaveBorderedCells: expectFreeXPreSave ? minBorderedCells : 0,
            MinFreeXPreSaveAlignedCells: expectFreeXPreSave ? minAlignedCells : 0,
            MinFreeXPreSaveWrappedCells: expectFreeXPreSave ? minWrappedCells : 0,
            MinExcelOpenedFormulaCells: minFormulaCells,
            MinExcelOpenedStructuredTables: minStructuredTables,
            MinExcelOpenedAutoFilterSheets: minAutoFilterSheets,
            MinExcelOpenedDataValidationCells: minDataValidations > 0 ? 1 : 0,
            MinExcelOpenedConditionalFormats: minConditionalFormats,
            MinExcelOpenedHyperlinks: minHyperlinks,
            MinExcelOpenedComments: minComments,
            MinExcelOpenedProtectedSheets: minProtectedSheets,
            MinExcelOpenedStructureProtection: minStructureProtection,
            MinExcelOpenedPictures: minPictures,
            MinExcelOpenedSparklines: minSparklines,
            MinExcelOpenedTextBoxes: minTextBoxes,
            MinExcelOpenedDrawingShapes: minDrawingShapes,
            MinExcelOpenedShapes: minExcelShapes,
            MinExcelOpenedPrintAreaSheets: minPrintAreaSheets,
            MinExcelOpenedPrintTitleSheets: minPrintTitleSheets,
            MinExcelOpenedLandscapeSheets: minLandscapeSheets,
            MinExcelOpenedScaleToFitSheets: minScaleToFitSheets,
            MinExcelOpenedPrintOptionsSheets: minPrintOptionsSheets,
            MinExcelOpenedHeaderFooterSheets: minHeaderFooterSheets,
            MinExcelOpenedManualPageBreaks: minManualPageBreaks,
            MinExcelOpenedAllowEditRanges: minAllowEditRanges,
            MinExcelOpenedMergedAreas: minMergedAreas,
            MinExcelOpenedFreezePaneSheets: minFreezePaneSheets,
            MinExcelOpenedHiddenRows: minHiddenRows,
            MinExcelOpenedHiddenColumns: minHiddenColumns,
            MinExcelOpenedCustomRowHeights: minCustomRowHeights,
            MinExcelOpenedCustomColumnWidths: minCustomColumnWidths,
            MinExcelOpenedOutlineRows: minOutlineRows,
            MinExcelOpenedOutlineColumns: minOutlineColumns,
            MinExcelOpenedStyledCells: minStyledCells,
            MinExcelOpenedNumberFormatCells: minNumberFormatCells,
            MinExcelOpenedBoldCells: minBoldCells,
            MinExcelOpenedFilledCells: minFilledCells,
            MinExcelOpenedBorderedCells: minBorderedCells,
            MinExcelOpenedAlignedCells: minAlignedCells,
            MinExcelOpenedWrappedCells: minWrappedCells,
            MinExcelReopenedFormulaCells: saveReopen ? minFormulaCells : 0,
            MinExcelReopenedStructuredTables: saveReopen ? minStructuredTables : 0,
            MinExcelReopenedAutoFilterSheets: saveReopen ? minAutoFilterSheets : 0,
            MinExcelReopenedDataValidationCells: saveReopen && minDataValidations > 0 ? 1 : 0,
            MinExcelReopenedConditionalFormats: saveReopen ? minConditionalFormats : 0,
            MinExcelReopenedHyperlinks: saveReopen ? minHyperlinks : 0,
            MinExcelReopenedComments: saveReopen ? minComments : 0,
            MinExcelReopenedProtectedSheets: saveReopen ? minProtectedSheets : 0,
            MinExcelReopenedStructureProtection: saveReopen ? minStructureProtection : 0,
            MinExcelReopenedPictures: saveReopen ? minPictures : 0,
            MinExcelReopenedSparklines: saveReopen ? minSparklines : 0,
            MinExcelReopenedTextBoxes: saveReopen ? minTextBoxes : 0,
            MinExcelReopenedDrawingShapes: saveReopen ? minDrawingShapes : 0,
            MinExcelReopenedShapes: saveReopen ? minExcelShapes : 0,
            MinExcelReopenedPrintAreaSheets: saveReopen ? minPrintAreaSheets : 0,
            MinExcelReopenedPrintTitleSheets: saveReopen ? minPrintTitleSheets : 0,
            MinExcelReopenedLandscapeSheets: saveReopen ? minLandscapeSheets : 0,
            MinExcelReopenedScaleToFitSheets: saveReopen ? minScaleToFitSheets : 0,
            MinExcelReopenedPrintOptionsSheets: saveReopen ? minPrintOptionsSheets : 0,
            MinExcelReopenedHeaderFooterSheets: saveReopen ? minHeaderFooterSheets : 0,
            MinExcelReopenedManualPageBreaks: saveReopen ? minManualPageBreaks : 0,
            MinExcelReopenedAllowEditRanges: saveReopen ? minAllowEditRanges : 0,
            MinExcelReopenedMergedAreas: saveReopen ? minMergedAreas : 0,
            MinExcelReopenedFreezePaneSheets: saveReopen ? minFreezePaneSheets : 0,
            MinExcelReopenedHiddenRows: saveReopen ? minHiddenRows : 0,
            MinExcelReopenedHiddenColumns: saveReopen ? minHiddenColumns : 0,
            MinExcelReopenedCustomRowHeights: saveReopen ? minCustomRowHeights : 0,
            MinExcelReopenedCustomColumnWidths: saveReopen ? minCustomColumnWidths : 0,
            MinExcelReopenedOutlineRows: saveReopen ? minOutlineRows : 0,
            MinExcelReopenedOutlineColumns: saveReopen ? minOutlineColumns : 0,
            MinExcelReopenedStyledCells: saveReopen ? minStyledCells : 0,
            MinExcelReopenedNumberFormatCells: saveReopen ? minNumberFormatCells : 0,
            MinExcelReopenedBoldCells: saveReopen ? minBoldCells : 0,
            MinExcelReopenedFilledCells: saveReopen ? minFilledCells : 0,
            MinExcelReopenedBorderedCells: saveReopen ? minBorderedCells : 0,
            MinExcelReopenedAlignedCells: saveReopen ? minAlignedCells : 0,
            MinExcelReopenedWrappedCells: saveReopen ? minWrappedCells : 0,
            MinFreeXReopenedFormulaCells: expectFreeXReopened ? minFormulaCells : 0,
            MinFreeXReopenedStructuredTables: expectFreeXReopened ? minStructuredTables : 0,
            MinFreeXReopenedAutoFilterSheets: expectFreeXReopened ? minAutoFilterSheets : 0,
            MinFreeXReopenedDataValidations: expectFreeXReopened ? minDataValidations : 0,
            MinFreeXReopenedConditionalFormats: expectFreeXReopened ? minConditionalFormats : 0,
            MinFreeXReopenedHyperlinks: expectFreeXReopened ? minHyperlinks : 0,
            MinFreeXReopenedComments: expectFreeXReopened ? minComments : 0,
            MinFreeXReopenedPictures: expectFreeXReopened ? minPictures : 0,
            MinFreeXReopenedSparklines: expectFreeXReopened ? minSparklines : 0,
            MinFreeXReopenedTextBoxes: expectFreeXReopened ? minTextBoxes : 0,
            MinFreeXReopenedDrawingShapes: expectFreeXReopened ? minDrawingShapes : 0,
            MinFreeXReopenedProtectedSheets: expectFreeXReopened ? minProtectedSheets : 0,
            MinFreeXReopenedStructureProtection: expectFreeXReopened ? minStructureProtection : 0,
            MinFreeXReopenedMergedRegions: expectFreeXReopened ? minMergedAreas : 0,
            MinFreeXReopenedFrozenSheets: expectFreeXReopened ? minFreezePaneSheets : 0,
            MinFreeXReopenedHiddenRows: expectFreeXReopened ? minHiddenRows : 0,
            MinFreeXReopenedHiddenColumns: expectFreeXReopened ? minHiddenColumns : 0,
            MinFreeXReopenedCustomRowHeights: expectFreeXReopened ? minCustomRowHeights : 0,
            MinFreeXReopenedCustomColumnWidths: expectFreeXReopened ? minCustomColumnWidths : 0,
            MinFreeXReopenedOutlineRows: expectFreeXReopened ? minOutlineRows : 0,
            MinFreeXReopenedOutlineColumns: expectFreeXReopened ? minOutlineColumns : 0,
            MinFreeXReopenedStyledCells: expectFreeXReopened ? minStyledCells : 0,
            MinFreeXReopenedNumberFormatCells: expectFreeXReopened ? minNumberFormatCells : 0,
            MinFreeXReopenedBoldCells: expectFreeXReopened ? minBoldCells : 0,
            MinFreeXReopenedFilledCells: expectFreeXReopened ? minFilledCells : 0,
            MinFreeXReopenedBorderedCells: expectFreeXReopened ? minBorderedCells : 0,
            MinFreeXReopenedAlignedCells: expectFreeXReopened ? minAlignedCells : 0,
            MinFreeXReopenedWrappedCells: expectFreeXReopened ? minWrappedCells : 0,
            MinFreeXPreSavePivotTables: expectFreeXPreSave ? minPivotTables : 0,
            MinFreeXPreSavePivotCaches: expectFreeXPreSave ? minPivotCaches : 0,
            MinExcelOpenedPivotTables: minPivotTables,
            MinExcelOpenedNamedRanges: minNamedRanges,
            MinExcelOpenedCharts: minCharts,
            MinExcelReopenedPivotTables: saveReopen ? minPivotTables : 0,
            MinExcelReopenedNamedRanges: saveReopen ? minNamedRanges : 0,
            MinExcelReopenedCharts: saveReopen ? minCharts : 0,
            MinFreeXReopenedPivotTables: expectFreeXReopened ? minPivotTables : 0,
            MinFreeXReopenedPivotCaches: expectFreeXReopened ? minPivotCaches : 0,
            MinFreeXReopenedNamedRanges: expectFreeXReopened ? minNamedRanges : 0,
            MinFreeXReopenedCharts: expectFreeXReopened ? minCharts : 0);
    }

    private static WorkbookSmokeExpectations PartnerDashboardExpectations(
        bool saveReopen,
        bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSaveFormulaCells: expectFreeXPreSave ? 16000 : 0,
            MinFreeXPreSaveStructuredTables: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveDataValidations: expectFreeXPreSave ? 5 : 0,
            MinFreeXPreSaveConditionalFormats: expectFreeXPreSave ? 100 : 0,
            MinFreeXPreSaveHyperlinks: expectFreeXPreSave ? 47 : 0,
            MinFreeXPreSaveComments: expectFreeXPreSave ? 117 : 0,
            MinFreeXPreSavePictures: expectFreeXPreSave ? 1 : 0,
            MinExcelOpenedFormulaCells: 16000,
            MinExcelOpenedStructuredTables: 1,
            MinExcelOpenedHyperlinks: 47,
            MinExcelOpenedComments: 117,
            MinExcelOpenedPictures: 1,
            MinExcelOpenedShapes: 120,
            MinExcelReopenedFormulaCells: saveReopen ? 16000 : 0,
            MinExcelReopenedStructuredTables: saveReopen ? 1 : 0,
            MinExcelReopenedHyperlinks: saveReopen ? 47 : 0,
            MinExcelReopenedComments: saveReopen ? 117 : 0,
            MinExcelReopenedPictures: saveReopen ? 1 : 0,
            MinExcelReopenedShapes: saveReopen ? 120 : 0,
            MinFreeXReopenedFormulaCells: saveReopen ? 16000 : 0,
            MinFreeXReopenedStructuredTables: saveReopen ? 1 : 0,
            MinFreeXReopenedDataValidations: saveReopen ? 5 : 0,
            MinFreeXReopenedConditionalFormats: saveReopen ? 66 : 0,
            MinFreeXReopenedHyperlinks: saveReopen ? 47 : 0,
            MinFreeXReopenedComments: saveReopen ? 117 : 0,
            MinFreeXReopenedPictures: saveReopen ? 1 : 0,
            MinFreeXPreSavePivotTables: expectFreeXPreSave ? 3 : 0,
            MinFreeXPreSavePivotCaches: expectFreeXPreSave ? 1 : 0,
            MinExcelOpenedPivotTables: 3,
            MinExcelReopenedPivotTables: saveReopen ? 3 : 0,
            MinFreeXReopenedPivotTables: saveReopen ? 3 : 0,
            MinFreeXReopenedPivotCaches: saveReopen ? 1 : 0);

    private static WorkbookSmokeExpectations? ExpectationsForGeneratedFixture(
        string generatedFile,
        bool saveReopen,
        WorkbookValidationWorkflow workflow)
    {
        var fileName = Path.GetFileName(generatedFile);
        var expectFreeXPreSave = workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel;
        WorkbookSmokeExpectations? expectations = null;

        if (fileName.Contains("grid_formulas", StringComparison.OrdinalIgnoreCase))
            expectations = FormulaExpectations(saveReopen, expectFreeXPreSave, minFormulaCells: 4, minNamedRanges: 2);
        else if (fileName.Contains("validation_cf", StringComparison.OrdinalIgnoreCase))
            expectations = ValidationCfExpectations(saveReopen, expectFreeXPreSave);
        else if (fileName.Contains("tables", StringComparison.OrdinalIgnoreCase))
            expectations = StructuredTableExpectations(saveReopen, expectFreeXPreSave, minStructuredTables: 1);
        else if (fileName.Contains("objects_links", StringComparison.OrdinalIgnoreCase))
            expectations = ObjectsLinksExpectations(saveReopen, expectFreeXPreSave);
        else if (fileName.Contains("images_sparklines", StringComparison.OrdinalIgnoreCase))
            expectations = ImagesSparklinesExpectations(saveReopen, expectFreeXPreSave);
        else if (fileName.Contains("shapes_text", StringComparison.OrdinalIgnoreCase))
            expectations = ShapesTextExpectations(saveReopen, expectFreeXPreSave);
        else if (fileName.Contains("pivots", StringComparison.OrdinalIgnoreCase))
            expectations = PivotTableExpectations(saveReopen, expectFreeXPreSave);
        else if (fileName.Contains("protection_page", StringComparison.OrdinalIgnoreCase))
            expectations = ProtectionPageExpectations(saveReopen, expectFreeXPreSave);

        return RequireNoFreeXLoadWarnings(expectations);
    }

    private static WorkbookSmokeExpectations? RequireNoFreeXLoadWarnings(WorkbookSmokeExpectations? expectations) =>
        expectations is null
            ? null
            : expectations with { RequireNoFreeXLoadWarnings = true };

    private static WorkbookSmokeExpectations ExcelAuthoredFixtureExpectations(bool saveReopen) =>
        new(
            MinFreeXPreSaveFormulaCells: 1,
            MinFreeXPreSaveNamedRanges: 1,
            MinFreeXPreSaveStructuredTables: 1,
            MinFreeXPreSaveDataValidations: 1,
            MinFreeXPreSaveConditionalFormats: 1,
            MinFreeXPreSaveHyperlinks: 1,
            MinFreeXPreSaveComments: 1,
            MinFreeXPreSaveTextBoxes: 1,
            MinFreeXPreSaveProtectedSheets: 1,
            MinFreeXPreSaveStructureProtection: 1,
            MinExcelOpenedFormulaCells: 1,
            MinExcelOpenedNamedRanges: 1,
            MinExcelOpenedStructuredTables: 1,
            MinExcelOpenedDataValidationCells: 1,
            MinExcelOpenedConditionalFormats: 1,
            MinExcelOpenedHyperlinks: 1,
            MinExcelOpenedComments: 1,
            MinExcelOpenedProtectedSheets: 1,
            MinExcelOpenedStructureProtection: 1,
            MinExcelOpenedTextBoxes: 1,
            MinExcelOpenedShapes: 2,
            MinExcelReopenedFormulaCells: saveReopen ? 1 : 0,
            MinExcelReopenedNamedRanges: saveReopen ? 1 : 0,
            MinExcelReopenedStructuredTables: saveReopen ? 1 : 0,
            MinExcelReopenedDataValidationCells: saveReopen ? 1 : 0,
            MinExcelReopenedConditionalFormats: saveReopen ? 1 : 0,
            MinExcelReopenedHyperlinks: saveReopen ? 1 : 0,
            MinExcelReopenedComments: saveReopen ? 1 : 0,
            MinExcelReopenedProtectedSheets: saveReopen ? 1 : 0,
            MinExcelReopenedStructureProtection: saveReopen ? 1 : 0,
            MinExcelReopenedTextBoxes: saveReopen ? 1 : 0,
            MinExcelReopenedShapes: saveReopen ? 2 : 0,
            MinFreeXReopenedFormulaCells: saveReopen ? 1 : 0,
            MinFreeXReopenedNamedRanges: saveReopen ? 1 : 0,
            MinFreeXReopenedStructuredTables: saveReopen ? 1 : 0,
            MinFreeXReopenedDataValidations: saveReopen ? 1 : 0,
            MinFreeXReopenedConditionalFormats: saveReopen ? 1 : 0,
            MinFreeXReopenedHyperlinks: saveReopen ? 1 : 0,
            MinFreeXReopenedComments: saveReopen ? 1 : 0,
            MinFreeXReopenedTextBoxes: saveReopen ? 1 : 0,
            MinFreeXReopenedProtectedSheets: saveReopen ? 1 : 0,
            MinFreeXReopenedStructureProtection: saveReopen ? 1 : 0,
            MinFreeXPreSavePivotTables: 1,
            MinFreeXPreSavePivotCaches: 1,
            MinExcelOpenedPivotTables: 1,
            MinExcelReopenedPivotTables: saveReopen ? 1 : 0,
            MinFreeXReopenedPivotTables: saveReopen ? 1 : 0,
            MinFreeXReopenedPivotCaches: saveReopen ? 1 : 0,
            RequireNoFreeXLoadWarnings: true);

    private static WorkbookSmokeExpectations FormulaExpectations(
        bool saveReopen,
        bool expectFreeXPreSave,
        int minFormulaCells,
        int minNamedRanges = 0) =>
        new(
            MinFreeXPreSaveFormulaCells: expectFreeXPreSave ? minFormulaCells : 0,
            MinFreeXPreSaveNamedRanges: expectFreeXPreSave ? minNamedRanges : 0,
            MinExcelOpenedFormulaCells: minFormulaCells,
            MinExcelOpenedNamedRanges: minNamedRanges,
            MinExcelReopenedFormulaCells: saveReopen ? minFormulaCells : 0,
            MinExcelReopenedNamedRanges: saveReopen ? minNamedRanges : 0,
            MinFreeXReopenedFormulaCells: saveReopen ? minFormulaCells : 0,
            MinFreeXReopenedNamedRanges: saveReopen ? minNamedRanges : 0);

    private static WorkbookSmokeExpectations ChartExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSaveCharts: expectFreeXPreSave ? 1 : 0,
            MinExcelOpenedCharts: 1,
            MinExcelOpenedShapes: 1,
            MinExcelReopenedCharts: saveReopen ? 1 : 0,
            MinExcelReopenedShapes: saveReopen ? 1 : 0,
            MinFreeXReopenedCharts: saveReopen ? 1 : 0,
            RequireNoFreeXLoadWarnings: true);

    private static WorkbookSmokeExpectations ValidationCfExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSaveDataValidations: expectFreeXPreSave ? 3 : 0,
            MinFreeXPreSaveConditionalFormats: expectFreeXPreSave ? 4 : 0,
            MinExcelOpenedDataValidationCells: 1,
            MinExcelOpenedConditionalFormats: 4,
            MinExcelReopenedDataValidationCells: saveReopen ? 1 : 0,
            MinExcelReopenedConditionalFormats: saveReopen ? 4 : 0,
            MinFreeXReopenedDataValidations: saveReopen ? 3 : 0,
            MinFreeXReopenedConditionalFormats: saveReopen ? 4 : 0);

    private static WorkbookSmokeExpectations StructuredTableExpectations(
        bool saveReopen,
        bool expectFreeXPreSave,
        int minStructuredTables) =>
        new(
            MinFreeXPreSaveStructuredTables: expectFreeXPreSave ? minStructuredTables : 0,
            MinExcelOpenedStructuredTables: minStructuredTables,
            MinExcelReopenedStructuredTables: saveReopen ? minStructuredTables : 0,
            MinFreeXReopenedStructuredTables: saveReopen ? minStructuredTables : 0);

    private static WorkbookSmokeExpectations ShapeExpectations(bool saveReopen, int minShapes) =>
        new(
            MinExcelOpenedShapes: minShapes,
            MinExcelReopenedShapes: saveReopen ? minShapes : 0);

    private static WorkbookSmokeExpectations ObjectsLinksExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSaveHyperlinks: expectFreeXPreSave ? 3 : 0,
            MinFreeXPreSaveComments: expectFreeXPreSave ? 1 : 0,
            MinExcelOpenedHyperlinks: 3,
            MinExcelOpenedComments: 1,
            MinExcelOpenedShapes: 1,
            MinExcelReopenedHyperlinks: saveReopen ? 3 : 0,
            MinExcelReopenedComments: saveReopen ? 1 : 0,
            MinExcelReopenedShapes: saveReopen ? 1 : 0,
            MinFreeXReopenedHyperlinks: saveReopen ? 3 : 0,
            MinFreeXReopenedComments: saveReopen ? 1 : 0);

    private static WorkbookSmokeExpectations ImagesSparklinesExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSavePictures: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveSparklines: expectFreeXPreSave ? 2 : 0,
            MinExcelOpenedPictures: 1,
            MinExcelOpenedSparklines: 2,
            MinExcelOpenedShapes: 1,
            MinExcelReopenedPictures: saveReopen ? 1 : 0,
            MinExcelReopenedSparklines: saveReopen ? 2 : 0,
            MinExcelReopenedShapes: saveReopen ? 1 : 0,
            MinFreeXReopenedPictures: saveReopen ? 1 : 0,
            MinFreeXReopenedSparklines: saveReopen ? 2 : 0);

    private static WorkbookSmokeExpectations ShapesTextExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSaveTextBoxes: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveDrawingShapes: expectFreeXPreSave ? 1 : 0,
            MinExcelOpenedTextBoxes: 1,
            MinExcelOpenedDrawingShapes: 1,
            MinExcelOpenedShapes: 2,
            MinExcelReopenedTextBoxes: saveReopen ? 1 : 0,
            MinExcelReopenedDrawingShapes: saveReopen ? 1 : 0,
            MinExcelReopenedShapes: saveReopen ? 2 : 0,
            MinFreeXReopenedTextBoxes: saveReopen ? 1 : 0,
            MinFreeXReopenedDrawingShapes: saveReopen ? 1 : 0);

    private static WorkbookSmokeExpectations ProtectionPageExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSaveProtectedSheets: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveStructureProtection: expectFreeXPreSave ? 1 : 0,
            MinExcelOpenedProtectedSheets: 1,
            MinExcelOpenedStructureProtection: 1,
            MinExcelOpenedPrintAreaSheets: 1,
            MinExcelOpenedPrintTitleSheets: 1,
            MinExcelOpenedLandscapeSheets: 1,
            MinExcelOpenedScaleToFitSheets: 1,
            MinExcelOpenedPrintOptionsSheets: 1,
            MinExcelOpenedHeaderFooterSheets: 1,
            MinExcelOpenedManualPageBreaks: 2,
            MinExcelOpenedAllowEditRanges: 1,
            MinExcelReopenedProtectedSheets: saveReopen ? 1 : 0,
            MinExcelReopenedStructureProtection: saveReopen ? 1 : 0,
            MinExcelReopenedPrintAreaSheets: saveReopen ? 1 : 0,
            MinExcelReopenedPrintTitleSheets: saveReopen ? 1 : 0,
            MinExcelReopenedLandscapeSheets: saveReopen ? 1 : 0,
            MinExcelReopenedScaleToFitSheets: saveReopen ? 1 : 0,
            MinExcelReopenedPrintOptionsSheets: saveReopen ? 1 : 0,
            MinExcelReopenedHeaderFooterSheets: saveReopen ? 1 : 0,
            MinExcelReopenedManualPageBreaks: saveReopen ? 2 : 0,
            MinExcelReopenedAllowEditRanges: saveReopen ? 1 : 0,
            MinFreeXReopenedProtectedSheets: saveReopen ? 1 : 0,
            MinFreeXReopenedStructureProtection: saveReopen ? 1 : 0);

    private static WorkbookSmokeExpectations PivotTableExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSavePivotTables: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSavePivotCaches: expectFreeXPreSave ? 1 : 0,
            MinExcelOpenedPivotTables: 1,
            MinExcelReopenedPivotTables: saveReopen ? 1 : 0,
            MinFreeXReopenedPivotTables: saveReopen ? 1 : 0,
            MinFreeXReopenedPivotCaches: saveReopen ? 1 : 0);

    private static void AssertSmokeExpectations(
        WorkbookSmokeInput input,
        FreeXWorkbookSummary? freeXPreSave,
        ExcelWorkbookSummary opened,
        ExcelWorkbookSummary? reopened,
        FreeXWorkbookSummary? freeXReopenedExcelSave)
    {
        var expectations = input.Expectations;
        if (expectations is null)
            return;

        AssertMin(
            "FreeX source load formula cells",
            freeXPreSave?.FormulaCellCount,
            expectations.MinFreeXPreSaveFormulaCells,
            input);
        AssertMin(
            "FreeX source load named ranges",
            freeXPreSave?.NamedRangeCount,
            expectations.MinFreeXPreSaveNamedRanges,
            input);
        AssertMin(
            "FreeX source load structured tables",
            freeXPreSave?.StructuredTableCount,
            expectations.MinFreeXPreSaveStructuredTables,
            input);
        AssertMin(
            "FreeX source load AutoFilter sheets",
            freeXPreSave?.AutoFilterSheetCount,
            expectations.MinFreeXPreSaveAutoFilterSheets,
            input);
        AssertMin(
            "FreeX source load charts",
            freeXPreSave?.ChartCount,
            expectations.MinFreeXPreSaveCharts,
            input);
        AssertFreeXMetadataExpectations("FreeX source load", freeXPreSave, expectations, input, preSave: true);
        AssertMin(
            "Excel open formula cells",
            opened.FormulaCellCount,
            expectations.MinExcelOpenedFormulaCells,
            input);
        AssertMin(
            "Excel open named ranges",
            opened.NamedRangeCount,
            expectations.MinExcelOpenedNamedRanges,
            input);
        AssertMin(
            "Excel open structured tables",
            opened.StructuredTableCount,
            expectations.MinExcelOpenedStructuredTables,
            input);
        AssertMin(
            "Excel open AutoFilter sheets",
            opened.AutoFilterSheetCount,
            expectations.MinExcelOpenedAutoFilterSheets,
            input);
        AssertMin(
            "Excel open charts",
            opened.ChartCount,
            expectations.MinExcelOpenedCharts,
            input);
        AssertMin(
            "Excel open data-validation cells",
            opened.DataValidationCellCount,
            expectations.MinExcelOpenedDataValidationCells,
            input);
        AssertMin(
            "Excel open conditional formats",
            opened.ConditionalFormatCount,
            expectations.MinExcelOpenedConditionalFormats,
            input);
        AssertMin(
            "Excel open hyperlinks",
            opened.HyperlinkCount,
            expectations.MinExcelOpenedHyperlinks,
            input);
        AssertMin(
            "Excel open comments",
            opened.CommentCount,
            expectations.MinExcelOpenedComments,
            input);
        AssertMin(
            "Excel open protected sheets",
            opened.ProtectedSheetCount,
            expectations.MinExcelOpenedProtectedSheets,
            input);
        AssertMin(
            "Excel open structure protection",
            opened.StructureProtectionCount,
            expectations.MinExcelOpenedStructureProtection,
            input);
        AssertMin(
            "Excel open pictures",
            opened.PictureCount,
            expectations.MinExcelOpenedPictures,
            input);
        AssertMin(
            "Excel open sparklines",
            opened.SparklineCount,
            expectations.MinExcelOpenedSparklines,
            input);
        AssertMin(
            "Excel open text boxes",
            opened.TextBoxCount,
            expectations.MinExcelOpenedTextBoxes,
            input);
        AssertMin(
            "Excel open drawing shapes",
            opened.DrawingShapeCount,
            expectations.MinExcelOpenedDrawingShapes,
            input);
        AssertMin(
            "Excel open worksheet shapes",
            opened.ShapeCount,
            expectations.MinExcelOpenedShapes,
            input);
        AssertMin(
            "Excel open print-area sheets",
            opened.PrintAreaSheetCount,
            expectations.MinExcelOpenedPrintAreaSheets,
            input);
        AssertMin(
            "Excel open print-title sheets",
            opened.PrintTitleSheetCount,
            expectations.MinExcelOpenedPrintTitleSheets,
            input);
        AssertMin(
            "Excel open landscape sheets",
            opened.LandscapeSheetCount,
            expectations.MinExcelOpenedLandscapeSheets,
            input);
        AssertMin(
            "Excel open scale-to-fit sheets",
            opened.ScaleToFitSheetCount,
            expectations.MinExcelOpenedScaleToFitSheets,
            input);
        AssertMin(
            "Excel open print grid/headings sheets",
            opened.PrintOptionsSheetCount,
            expectations.MinExcelOpenedPrintOptionsSheets,
            input);
        AssertMin(
            "Excel open header/footer sheets",
            opened.HeaderFooterSheetCount,
            expectations.MinExcelOpenedHeaderFooterSheets,
            input);
        AssertMin(
            "Excel open manual page breaks",
            opened.ManualPageBreakCount,
            expectations.MinExcelOpenedManualPageBreaks,
            input);
        AssertMin(
            "Excel open allow-edit ranges",
            opened.AllowEditRangeCount,
            expectations.MinExcelOpenedAllowEditRanges,
            input);
        AssertMin(
            "Excel open merged areas",
            opened.MergedAreaCount,
            expectations.MinExcelOpenedMergedAreas,
            input);
        AssertMin(
            "Excel open freeze-pane sheets",
            opened.FreezePaneSheetCount,
            expectations.MinExcelOpenedFreezePaneSheets,
            input);
        AssertMin(
            "Excel open hidden rows",
            opened.HiddenRowCount,
            expectations.MinExcelOpenedHiddenRows,
            input);
        AssertMin(
            "Excel open hidden columns",
            opened.HiddenColumnCount,
            expectations.MinExcelOpenedHiddenColumns,
            input);
        AssertMin(
            "Excel open custom row heights",
            opened.CustomRowHeightCount,
            expectations.MinExcelOpenedCustomRowHeights,
            input);
        AssertMin(
            "Excel open custom column widths",
            opened.CustomColumnWidthCount,
            expectations.MinExcelOpenedCustomColumnWidths,
            input);
        AssertMin(
            "Excel open outline rows",
            opened.OutlineRowCount,
            expectations.MinExcelOpenedOutlineRows,
            input);
        AssertMin(
            "Excel open outline columns",
            opened.OutlineColumnCount,
            expectations.MinExcelOpenedOutlineColumns,
            input);
        AssertMin(
            "Excel open styled cells",
            opened.StyledCellCount,
            expectations.MinExcelOpenedStyledCells,
            input);
        AssertMin(
            "Excel open number-format cells",
            opened.NumberFormatCellCount,
            expectations.MinExcelOpenedNumberFormatCells,
            input);
        AssertMin(
            "Excel open bold cells",
            opened.BoldCellCount,
            expectations.MinExcelOpenedBoldCells,
            input);
        AssertMin(
            "Excel open filled cells",
            opened.FilledCellCount,
            expectations.MinExcelOpenedFilledCells,
            input);
        AssertMin(
            "Excel open bordered cells",
            opened.BorderedCellCount,
            expectations.MinExcelOpenedBorderedCells,
            input);
        AssertMin(
            "Excel open aligned cells",
            opened.AlignedCellCount,
            expectations.MinExcelOpenedAlignedCells,
            input);
        AssertMin(
            "Excel open wrapped cells",
            opened.WrappedCellCount,
            expectations.MinExcelOpenedWrappedCells,
            input);
        AssertMin(
            "Excel reopen formula cells",
            reopened?.FormulaCellCount,
            expectations.MinExcelReopenedFormulaCells,
            input);
        AssertMin(
            "Excel reopen named ranges",
            reopened?.NamedRangeCount,
            expectations.MinExcelReopenedNamedRanges,
            input);
        AssertMin(
            "Excel reopen structured tables",
            reopened?.StructuredTableCount,
            expectations.MinExcelReopenedStructuredTables,
            input);
        AssertMin(
            "Excel reopen AutoFilter sheets",
            reopened?.AutoFilterSheetCount,
            expectations.MinExcelReopenedAutoFilterSheets,
            input);
        AssertMin(
            "Excel reopen charts",
            reopened?.ChartCount,
            expectations.MinExcelReopenedCharts,
            input);
        AssertMin(
            "Excel reopen data-validation cells",
            reopened?.DataValidationCellCount,
            expectations.MinExcelReopenedDataValidationCells,
            input);
        AssertMin(
            "Excel reopen conditional formats",
            reopened?.ConditionalFormatCount,
            expectations.MinExcelReopenedConditionalFormats,
            input);
        AssertMin(
            "Excel reopen hyperlinks",
            reopened?.HyperlinkCount,
            expectations.MinExcelReopenedHyperlinks,
            input);
        AssertMin(
            "Excel reopen comments",
            reopened?.CommentCount,
            expectations.MinExcelReopenedComments,
            input);
        AssertMin(
            "Excel reopen protected sheets",
            reopened?.ProtectedSheetCount,
            expectations.MinExcelReopenedProtectedSheets,
            input);
        AssertMin(
            "Excel reopen structure protection",
            reopened?.StructureProtectionCount,
            expectations.MinExcelReopenedStructureProtection,
            input);
        AssertMin(
            "Excel reopen pictures",
            reopened?.PictureCount,
            expectations.MinExcelReopenedPictures,
            input);
        AssertMin(
            "Excel reopen sparklines",
            reopened?.SparklineCount,
            expectations.MinExcelReopenedSparklines,
            input);
        AssertMin(
            "Excel reopen text boxes",
            reopened?.TextBoxCount,
            expectations.MinExcelReopenedTextBoxes,
            input);
        AssertMin(
            "Excel reopen drawing shapes",
            reopened?.DrawingShapeCount,
            expectations.MinExcelReopenedDrawingShapes,
            input);
        AssertMin(
            "Excel reopen worksheet shapes",
            reopened?.ShapeCount,
            expectations.MinExcelReopenedShapes,
            input);
        AssertMin(
            "Excel reopen print-area sheets",
            reopened?.PrintAreaSheetCount,
            expectations.MinExcelReopenedPrintAreaSheets,
            input);
        AssertMin(
            "Excel reopen print-title sheets",
            reopened?.PrintTitleSheetCount,
            expectations.MinExcelReopenedPrintTitleSheets,
            input);
        AssertMin(
            "Excel reopen landscape sheets",
            reopened?.LandscapeSheetCount,
            expectations.MinExcelReopenedLandscapeSheets,
            input);
        AssertMin(
            "Excel reopen scale-to-fit sheets",
            reopened?.ScaleToFitSheetCount,
            expectations.MinExcelReopenedScaleToFitSheets,
            input);
        AssertMin(
            "Excel reopen print grid/headings sheets",
            reopened?.PrintOptionsSheetCount,
            expectations.MinExcelReopenedPrintOptionsSheets,
            input);
        AssertMin(
            "Excel reopen header/footer sheets",
            reopened?.HeaderFooterSheetCount,
            expectations.MinExcelReopenedHeaderFooterSheets,
            input);
        AssertMin(
            "Excel reopen manual page breaks",
            reopened?.ManualPageBreakCount,
            expectations.MinExcelReopenedManualPageBreaks,
            input);
        AssertMin(
            "Excel reopen allow-edit ranges",
            reopened?.AllowEditRangeCount,
            expectations.MinExcelReopenedAllowEditRanges,
            input);
        AssertMin(
            "Excel reopen merged areas",
            reopened?.MergedAreaCount,
            expectations.MinExcelReopenedMergedAreas,
            input);
        AssertMin(
            "Excel reopen freeze-pane sheets",
            reopened?.FreezePaneSheetCount,
            expectations.MinExcelReopenedFreezePaneSheets,
            input);
        AssertMin(
            "Excel reopen hidden rows",
            reopened?.HiddenRowCount,
            expectations.MinExcelReopenedHiddenRows,
            input);
        AssertMin(
            "Excel reopen hidden columns",
            reopened?.HiddenColumnCount,
            expectations.MinExcelReopenedHiddenColumns,
            input);
        AssertMin(
            "Excel reopen custom row heights",
            reopened?.CustomRowHeightCount,
            expectations.MinExcelReopenedCustomRowHeights,
            input);
        AssertMin(
            "Excel reopen custom column widths",
            reopened?.CustomColumnWidthCount,
            expectations.MinExcelReopenedCustomColumnWidths,
            input);
        AssertMin(
            "Excel reopen outline rows",
            reopened?.OutlineRowCount,
            expectations.MinExcelReopenedOutlineRows,
            input);
        AssertMin(
            "Excel reopen outline columns",
            reopened?.OutlineColumnCount,
            expectations.MinExcelReopenedOutlineColumns,
            input);
        AssertMin(
            "Excel reopen styled cells",
            reopened?.StyledCellCount,
            expectations.MinExcelReopenedStyledCells,
            input);
        AssertMin(
            "Excel reopen number-format cells",
            reopened?.NumberFormatCellCount,
            expectations.MinExcelReopenedNumberFormatCells,
            input);
        AssertMin(
            "Excel reopen bold cells",
            reopened?.BoldCellCount,
            expectations.MinExcelReopenedBoldCells,
            input);
        AssertMin(
            "Excel reopen filled cells",
            reopened?.FilledCellCount,
            expectations.MinExcelReopenedFilledCells,
            input);
        AssertMin(
            "Excel reopen bordered cells",
            reopened?.BorderedCellCount,
            expectations.MinExcelReopenedBorderedCells,
            input);
        AssertMin(
            "Excel reopen aligned cells",
            reopened?.AlignedCellCount,
            expectations.MinExcelReopenedAlignedCells,
            input);
        AssertMin(
            "Excel reopen wrapped cells",
            reopened?.WrappedCellCount,
            expectations.MinExcelReopenedWrappedCells,
            input);
        AssertMin(
            "FreeX reopened Excel save formula cells",
            freeXReopenedExcelSave?.FormulaCellCount,
            expectations.MinFreeXReopenedFormulaCells,
            input);
        AssertMin(
            "FreeX reopened Excel save named ranges",
            freeXReopenedExcelSave?.NamedRangeCount,
            expectations.MinFreeXReopenedNamedRanges,
            input);
        AssertMin(
            "FreeX reopened Excel save structured tables",
            freeXReopenedExcelSave?.StructuredTableCount,
            expectations.MinFreeXReopenedStructuredTables,
            input);
        AssertMin(
            "FreeX reopened Excel save AutoFilter sheets",
            freeXReopenedExcelSave?.AutoFilterSheetCount,
            expectations.MinFreeXReopenedAutoFilterSheets,
            input);
        AssertMin(
            "FreeX reopened Excel save charts",
            freeXReopenedExcelSave?.ChartCount,
            expectations.MinFreeXReopenedCharts,
            input);
        AssertFreeXMetadataExpectations("FreeX reopened Excel save", freeXReopenedExcelSave, expectations, input, preSave: false);
        AssertMin(
            "FreeX source load pivot tables",
            freeXPreSave?.PivotTableCount,
            expectations.MinFreeXPreSavePivotTables,
            input);
        AssertMin(
            "FreeX source load pivot caches",
            freeXPreSave?.PivotCacheCount,
            expectations.MinFreeXPreSavePivotCaches,
            input);
        AssertMin(
            "Excel open pivot tables",
            opened.PivotTableCount,
            expectations.MinExcelOpenedPivotTables,
            input);
        AssertMin(
            "Excel reopen pivot tables",
            reopened?.PivotTableCount,
            expectations.MinExcelReopenedPivotTables,
            input);
        AssertMin(
            "FreeX reopened Excel save pivot tables",
            freeXReopenedExcelSave?.PivotTableCount,
            expectations.MinFreeXReopenedPivotTables,
            input);
        AssertMin(
            "FreeX reopened Excel save pivot caches",
            freeXReopenedExcelSave?.PivotCacheCount,
            expectations.MinFreeXReopenedPivotCaches,
            input);
    }

    private static void AssertFreeXLoadWarnings(
        WorkbookSmokeInput input,
        string label,
        IReadOnlyList<string> warnings)
    {
        if (input.Expectations?.RequireNoFreeXLoadWarnings != true || warnings.Count == 0)
            return;

        throw new InvalidDataException(
            $"{label} produced {warnings.Count} warning(s) for {input.Description}: {FormatWarnings(warnings)}");
    }

    private static string FormatWarnings(IReadOnlyList<string> warnings)
    {
        const int maxWarningsToReport = 8;
        var sample = string.Join("; ", warnings.Take(maxWarningsToReport));
        var suffix = warnings.Count > maxWarningsToReport
            ? $"; ... {warnings.Count - maxWarningsToReport} more"
            : string.Empty;
        return $"{sample}{suffix}";
    }

    private static void AssertFreeXMetadataExpectations(
        string label,
        FreeXWorkbookSummary? summary,
        WorkbookSmokeExpectations expectations,
        WorkbookSmokeInput input,
        bool preSave)
    {
        AssertMin(
            $"{label} data validations",
            summary?.DataValidationCount,
            preSave ? expectations.MinFreeXPreSaveDataValidations : expectations.MinFreeXReopenedDataValidations,
            input);
        AssertMin(
            $"{label} conditional formats",
            summary?.ConditionalFormatCount,
            preSave ? expectations.MinFreeXPreSaveConditionalFormats : expectations.MinFreeXReopenedConditionalFormats,
            input);
        AssertMin(
            $"{label} hyperlinks",
            summary?.HyperlinkCount,
            preSave ? expectations.MinFreeXPreSaveHyperlinks : expectations.MinFreeXReopenedHyperlinks,
            input);
        AssertMin(
            $"{label} comments",
            summary?.CommentCount,
            preSave ? expectations.MinFreeXPreSaveComments : expectations.MinFreeXReopenedComments,
            input);
        AssertMin(
            $"{label} pictures",
            summary?.PictureCount,
            preSave ? expectations.MinFreeXPreSavePictures : expectations.MinFreeXReopenedPictures,
            input);
        AssertMin(
            $"{label} sparklines",
            summary?.SparklineCount,
            preSave ? expectations.MinFreeXPreSaveSparklines : expectations.MinFreeXReopenedSparklines,
            input);
        AssertMin(
            $"{label} text boxes",
            summary?.TextBoxCount,
            preSave ? expectations.MinFreeXPreSaveTextBoxes : expectations.MinFreeXReopenedTextBoxes,
            input);
        AssertMin(
            $"{label} drawing shapes",
            summary?.DrawingShapeCount,
            preSave ? expectations.MinFreeXPreSaveDrawingShapes : expectations.MinFreeXReopenedDrawingShapes,
            input);
        AssertMin(
            $"{label} protected sheets",
            summary?.ProtectedSheetCount,
            preSave ? expectations.MinFreeXPreSaveProtectedSheets : expectations.MinFreeXReopenedProtectedSheets,
            input);
        AssertMin(
            $"{label} structure protection",
            summary?.StructureProtectionCount,
            preSave ? expectations.MinFreeXPreSaveStructureProtection : expectations.MinFreeXReopenedStructureProtection,
            input);
        AssertMin(
            $"{label} merged regions",
            summary?.MergedRegionCount,
            preSave ? expectations.MinFreeXPreSaveMergedRegions : expectations.MinFreeXReopenedMergedRegions,
            input);
        AssertMin(
            $"{label} frozen sheets",
            summary?.FrozenSheetCount,
            preSave ? expectations.MinFreeXPreSaveFrozenSheets : expectations.MinFreeXReopenedFrozenSheets,
            input);
        AssertMin(
            $"{label} hidden rows",
            summary?.HiddenRowCount,
            preSave ? expectations.MinFreeXPreSaveHiddenRows : expectations.MinFreeXReopenedHiddenRows,
            input);
        AssertMin(
            $"{label} hidden columns",
            summary?.HiddenColumnCount,
            preSave ? expectations.MinFreeXPreSaveHiddenColumns : expectations.MinFreeXReopenedHiddenColumns,
            input);
        AssertMin(
            $"{label} custom row heights",
            summary?.CustomRowHeightCount,
            preSave ? expectations.MinFreeXPreSaveCustomRowHeights : expectations.MinFreeXReopenedCustomRowHeights,
            input);
        AssertMin(
            $"{label} custom column widths",
            summary?.CustomColumnWidthCount,
            preSave ? expectations.MinFreeXPreSaveCustomColumnWidths : expectations.MinFreeXReopenedCustomColumnWidths,
            input);
        AssertMin(
            $"{label} outline rows",
            summary?.OutlineRowCount,
            preSave ? expectations.MinFreeXPreSaveOutlineRows : expectations.MinFreeXReopenedOutlineRows,
            input);
        AssertMin(
            $"{label} outline columns",
            summary?.OutlineColumnCount,
            preSave ? expectations.MinFreeXPreSaveOutlineColumns : expectations.MinFreeXReopenedOutlineColumns,
            input);
        AssertMin(
            $"{label} styled cells",
            summary?.StyledCellCount,
            preSave ? expectations.MinFreeXPreSaveStyledCells : expectations.MinFreeXReopenedStyledCells,
            input);
        AssertMin(
            $"{label} number-format cells",
            summary?.NumberFormatCellCount,
            preSave ? expectations.MinFreeXPreSaveNumberFormatCells : expectations.MinFreeXReopenedNumberFormatCells,
            input);
        AssertMin(
            $"{label} bold cells",
            summary?.BoldCellCount,
            preSave ? expectations.MinFreeXPreSaveBoldCells : expectations.MinFreeXReopenedBoldCells,
            input);
        AssertMin(
            $"{label} filled cells",
            summary?.FilledCellCount,
            preSave ? expectations.MinFreeXPreSaveFilledCells : expectations.MinFreeXReopenedFilledCells,
            input);
        AssertMin(
            $"{label} bordered cells",
            summary?.BorderedCellCount,
            preSave ? expectations.MinFreeXPreSaveBorderedCells : expectations.MinFreeXReopenedBorderedCells,
            input);
        AssertMin(
            $"{label} aligned cells",
            summary?.AlignedCellCount,
            preSave ? expectations.MinFreeXPreSaveAlignedCells : expectations.MinFreeXReopenedAlignedCells,
            input);
        AssertMin(
            $"{label} wrapped cells",
            summary?.WrappedCellCount,
            preSave ? expectations.MinFreeXPreSaveWrappedCells : expectations.MinFreeXReopenedWrappedCells,
            input);
    }

    private static void AssertMin(string label, int? actual, int minimum, WorkbookSmokeInput input)
    {
        if (minimum <= 0)
            return;

        if (actual is null)
        {
            throw new InvalidDataException(
                $"{label} expectation for {input.Description} was not measured; expected at least {minimum}.");
        }

        if (actual < minimum)
        {
            throw new InvalidDataException(
                $"{label} expectation failed for {input.Description}: expected at least {minimum}, observed {actual}.");
        }
    }

    private static void WriteWorkbookReport(WorkbookSmokeResult result, bool saveReopen)
    {
        var status = result.Success
            ? saveReopen ? "SAVE-REOPEN OK" : "OPEN OK"
            : saveReopen ? "SAVE-REOPEN FAILED" : "OPEN FAILED";

        Console.WriteLine($"{status}: {result.Input.SourcePath}");
        Console.WriteLine($"  Source: {result.Input.Description}; workflow: {FormatWorkflow(result.Input.Workflow)}");
        if (result.Input.CorpusRow is { } corpusRow)
        {
            Console.WriteLine(
                $"  Corpus: {corpusRow.Id}; source {corpusRow.SourceType}; status {corpusRow.ExpectedStatus}; tags {corpusRow.FeatureTags}");
        }
        if (result.FreeXSavedPath is not null)
            Console.WriteLine($"  FreeX saved: {result.FreeXSavedPath}");
        if (result.FreeXSavedPath is not null &&
            result.Input.Expectations?.RequiredFreeXSavedPackageParts is { Count: > 0 } freeXRequiredParts)
        {
            Console.WriteLine(
                $"  FreeX-saved package parts asserted: {string.Join(", ", freeXRequiredParts)}");
        }
        if (result.FreeXSavedPath is not null &&
            result.Input.Expectations?.RequiredFreeXSavedPackageContentTypes is { Count: > 0 } freeXRequiredContentTypes)
        {
            Console.WriteLine(
                $"  FreeX-saved package content types asserted: {string.Join(", ", freeXRequiredContentTypes.Select(FormatPackageContentTypeExpectation))}");
        }
        if (result.FreeXSavedPath is not null &&
            result.Input.Expectations?.RequiredFreeXSavedPackageRelationships is { Count: > 0 } freeXRequiredRelationships)
        {
            Console.WriteLine(
                $"  FreeX-saved package relationships asserted: {string.Join(", ", freeXRequiredRelationships.Select(FormatPackageRelationshipExpectation))}");
        }
        if (result.StagedPath is not null)
            Console.WriteLine($"  Staged: {result.StagedPath}");
        if (result.ExcelSavedPath is not null)
            Console.WriteLine($"  Excel saved: {result.ExcelSavedPath}");
        if (result.ExcelSavedPath is not null &&
            result.Input.Expectations?.RequiredExcelSavedPackageParts is { Count: > 0 } requiredParts)
        {
            Console.WriteLine(
                $"  Excel-saved package parts asserted: {string.Join(", ", requiredParts)}");
        }
        if (result.ExcelSavedPath is not null &&
            result.Input.Expectations?.RequiredExcelSavedPackageContentTypes is { Count: > 0 } requiredContentTypes)
        {
            Console.WriteLine(
                $"  Excel-saved package content types asserted: {string.Join(", ", requiredContentTypes.Select(FormatPackageContentTypeExpectation))}");
        }
        if (result.ExcelSavedPath is not null &&
            result.Input.Expectations?.RequiredExcelSavedPackageRelationships is { Count: > 0 } requiredRelationships)
        {
            Console.WriteLine(
                $"  Excel-saved package relationships asserted: {string.Join(", ", requiredRelationships.Select(FormatPackageRelationshipExpectation))}");
        }

        if (result.FreeXPreSave is { } freeXPreSave)
            WriteFreeXSummary("FreeX source load", freeXPreSave);
        WriteFreeXWarnings("FreeX source load", result.FreeXPreSaveWarnings);

        if (result.Opened is { } opened)
        {
            Console.WriteLine(
                $"  Excel open: worksheets {opened.WorksheetCount}; named ranges {opened.NamedRangeCount}; formulas {opened.FormulaCellCount}; tables {opened.StructuredTableCount}; AutoFilter sheets {opened.AutoFilterSheetCount}; charts {opened.ChartCount}; validation cells {opened.DataValidationCellCount}; conditional formats {opened.ConditionalFormatCount}; hyperlinks {opened.HyperlinkCount}; comments {opened.CommentCount}; protected sheets {opened.ProtectedSheetCount}; structure protection {opened.StructureProtectionCount}; pictures {opened.PictureCount}; sparklines {opened.SparklineCount}; text boxes {opened.TextBoxCount}; drawing shapes {opened.DrawingShapeCount}; worksheet shapes {opened.ShapeCount}; print areas {opened.PrintAreaSheetCount}; print titles {opened.PrintTitleSheetCount}; landscape sheets {opened.LandscapeSheetCount}; scale-to-fit sheets {opened.ScaleToFitSheetCount}; print grid/headings sheets {opened.PrintOptionsSheetCount}; header/footer sheets {opened.HeaderFooterSheetCount}; manual page breaks {opened.ManualPageBreakCount}; allow-edit ranges {opened.AllowEditRangeCount}; merged areas {opened.MergedAreaCount}; freeze-pane sheets {opened.FreezePaneSheetCount}; hidden rows {opened.HiddenRowCount}; hidden columns {opened.HiddenColumnCount}; custom row heights {opened.CustomRowHeightCount}; custom column widths {opened.CustomColumnWidthCount}; outline rows {opened.OutlineRowCount}; outline columns {opened.OutlineColumnCount}; styled cells {opened.StyledCellCount}; number-format cells {opened.NumberFormatCellCount}; bold cells {opened.BoldCellCount}; filled cells {opened.FilledCellCount}; bordered cells {opened.BorderedCellCount}; aligned cells {opened.AlignedCellCount}; wrapped cells {opened.WrappedCellCount}; pivots {opened.PivotTableCount}");
        }
        if (result.Reopened is { } reopened)
        {
            Console.WriteLine(
                $"  Excel reopen: worksheets {reopened.WorksheetCount}; named ranges {reopened.NamedRangeCount}; formulas {reopened.FormulaCellCount}; tables {reopened.StructuredTableCount}; AutoFilter sheets {reopened.AutoFilterSheetCount}; charts {reopened.ChartCount}; validation cells {reopened.DataValidationCellCount}; conditional formats {reopened.ConditionalFormatCount}; hyperlinks {reopened.HyperlinkCount}; comments {reopened.CommentCount}; protected sheets {reopened.ProtectedSheetCount}; structure protection {reopened.StructureProtectionCount}; pictures {reopened.PictureCount}; sparklines {reopened.SparklineCount}; text boxes {reopened.TextBoxCount}; drawing shapes {reopened.DrawingShapeCount}; worksheet shapes {reopened.ShapeCount}; print areas {reopened.PrintAreaSheetCount}; print titles {reopened.PrintTitleSheetCount}; landscape sheets {reopened.LandscapeSheetCount}; scale-to-fit sheets {reopened.ScaleToFitSheetCount}; print grid/headings sheets {reopened.PrintOptionsSheetCount}; header/footer sheets {reopened.HeaderFooterSheetCount}; manual page breaks {reopened.ManualPageBreakCount}; allow-edit ranges {reopened.AllowEditRangeCount}; merged areas {reopened.MergedAreaCount}; freeze-pane sheets {reopened.FreezePaneSheetCount}; hidden rows {reopened.HiddenRowCount}; hidden columns {reopened.HiddenColumnCount}; custom row heights {reopened.CustomRowHeightCount}; custom column widths {reopened.CustomColumnWidthCount}; outline rows {reopened.OutlineRowCount}; outline columns {reopened.OutlineColumnCount}; styled cells {reopened.StyledCellCount}; number-format cells {reopened.NumberFormatCellCount}; bold cells {reopened.BoldCellCount}; filled cells {reopened.FilledCellCount}; bordered cells {reopened.BorderedCellCount}; aligned cells {reopened.AlignedCellCount}; wrapped cells {reopened.WrappedCellCount}; pivots {reopened.PivotTableCount}");
        }
        if (result.FreeXReopenedExcelSave is { } freeXReopened)
            WriteFreeXSummary("FreeX reopened Excel save", freeXReopened);
        WriteFreeXWarnings("FreeX reopened Excel save", result.FreeXReopenedExcelSaveWarnings);

        if (!result.Success)
            Console.WriteLine($"  Error: {result.Error}");
    }

    private static void WriteFreeXSummary(string label, FreeXWorkbookSummary summary)
    {
        Console.WriteLine(
            $"  {label}: sheets {summary.SheetCount}; cells {summary.CellCount}; named ranges {summary.NamedRangeCount}; formulas {summary.FormulaCellCount}; tables {summary.StructuredTableCount}; AutoFilter sheets {summary.AutoFilterSheetCount}; charts {summary.ChartCount}; pivots {summary.PivotTableCount}; pivot caches {summary.PivotCacheCount}");
        Console.WriteLine(
            $"  {label} metadata: validations {summary.DataValidationCount}; conditional formats {summary.ConditionalFormatCount}; hyperlinks {summary.HyperlinkCount}; comments {summary.CommentCount}; pictures {summary.PictureCount}; sparklines {summary.SparklineCount}; text boxes {summary.TextBoxCount}; drawing shapes {summary.DrawingShapeCount}; protected sheets {summary.ProtectedSheetCount}; structure protection {summary.StructureProtectionCount}; merged regions {summary.MergedRegionCount}; frozen sheets {summary.FrozenSheetCount}; hidden rows {summary.HiddenRowCount}; hidden columns {summary.HiddenColumnCount}; custom row heights {summary.CustomRowHeightCount}; custom column widths {summary.CustomColumnWidthCount}; outline rows {summary.OutlineRowCount}; outline columns {summary.OutlineColumnCount}");
        Console.WriteLine(
            $"  {label} formatting: styled cells {summary.StyledCellCount}; number-format cells {summary.NumberFormatCellCount}; bold cells {summary.BoldCellCount}; filled cells {summary.FilledCellCount}; bordered cells {summary.BorderedCellCount}; aligned cells {summary.AlignedCellCount}; wrapped cells {summary.WrappedCellCount}");
    }

    private static void WriteFreeXWarnings(string label, IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
            return;

        Console.WriteLine($"  {label} warnings: {FormatWarnings(warnings)}");
    }

    private static string FormatWorkflow(WorkbookValidationWorkflow workflow) =>
        workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel
            ? "FreeX load/save -> Excel"
            : "Excel";

    private static string FormatFailure(Exception ex)
    {
        if (ex is InvalidDataException invalidDataException &&
            invalidDataException.InnerException is COMException innerComException)
        {
            return $"{invalidDataException.Message}: COMException 0x{(uint)innerComException.HResult:X8}: {innerComException.Message}";
        }

        if (ex is COMException comException && (uint)comException.HResult == ExcelOpenRejectedHResult)
        {
            return $"Excel rejected the workbook with 0x{(uint)comException.HResult:X8}: {comException.Message}";
        }

        var hresult = (uint)ex.HResult;
        return $"{ex.GetType().Name} 0x{hresult:X8}: {ex.Message}";
    }

    private static void WriteMachineReadableReport(
        string runDirectory,
        SmokeOptions options,
        ExcelSmokeSummary summary,
        CorpusManifestSelection? corpusSelection)
    {
        var reportPath = Path.Combine(runDirectory, "excel-smoke-report.json");
        var report = new
        {
            generatedAt = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            runDirectory,
            validationMode = options.SaveReopen ? "save-reopen" : "open-only",
            freeXResaveBeforeExcel = options.FreeXResaveBeforeExcel,
            total = summary.Total,
            passed = summary.Passed,
            failed = summary.Failed,
            corpus = corpusSelection is null
                ? null
                : new
                {
                    manifestPath = corpusSelection.ManifestPath,
                    selected = corpusSelection.Inputs.Count,
                    skipped = corpusSelection.Skipped.Count,
                    skippedRows = corpusSelection.Skipped.Select(skip => new
                    {
                        id = skip.Row.Id,
                        path = skip.Row.RelativePath,
                        sourceType = skip.Row.SourceType,
                        expectedStatus = skip.Row.ExpectedStatus,
                        reason = skip.Reason,
                        fullPath = skip.FullPath
                    })
                },
            results = summary.Results.Select(result => new
            {
                success = result.Success,
                sourcePath = result.Input.SourcePath,
                description = result.Input.Description,
                workflow = FormatWorkflow(result.Input.Workflow),
                expectations = result.Input.Expectations,
                corpus = result.Input.CorpusRow is null
                    ? null
                    : new
                    {
                        id = result.Input.CorpusRow.Id,
                        sourceType = result.Input.CorpusRow.SourceType,
                        expectedStatus = result.Input.CorpusRow.ExpectedStatus,
                        featureTags = result.Input.CorpusRow.FeatureTags,
                        expectedWarnings = result.Input.CorpusRow.ExpectedWarnings
                    },
                stagedPath = result.StagedPath,
                freeXSavedPath = result.FreeXSavedPath,
                excelSavedPath = result.ExcelSavedPath,
                opened = result.Opened,
                reopened = result.Reopened,
                freeXPreSave = result.FreeXPreSave,
                freeXPreSaveWarnings = result.FreeXPreSaveWarnings,
                freeXReopenedExcelSave = result.FreeXReopenedExcelSave,
                freeXReopenedExcelSaveWarnings = result.FreeXReopenedExcelSaveWarnings,
                error = result.Error
            })
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(reportPath, json);
        Console.WriteLine($"Report: {reportPath}");
    }

    private static string GetUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
            userProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(userProfile))
            throw new InvalidOperationException("USERPROFILE could not be resolved.");

        return Path.GetFullPath(userProfile);
    }

    private static string CreateDefaultRunDirectory(string userProfile) =>
        Path.Combine(
            userProfile,
            "freex-xlsx-verify",
            "excel-smoke",
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

    private static void EnsureUnderUserProfile(string path, string userProfile)
    {
        var fullPath = Path.GetFullPath(path);
        var fullUserProfile = Path.GetFullPath(userProfile);
        if (!fullUserProfile.EndsWith(Path.DirectorySeparatorChar))
            fullUserProfile += Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(fullUserProfile, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Output directory must be under USERPROFILE to avoid Excel Protected View. Requested: {fullPath}; USERPROFILE: {userProfile}");
        }
    }

    private sealed record SharedStringCellReference(
        string WorksheetPart,
        string CellReference,
        string? ValueText);

    private sealed record StyleReference(
        string WorksheetPart,
        string Description,
        string? ValueText);

    private sealed record WorksheetHyperlinkReference(
        int Ordinal,
        string? Reference,
        string? RelationshipId,
        string? Location);

    private sealed record WorksheetPictureReference(
        int Ordinal,
        string? RelationshipId);

    private sealed record WorksheetPrinterSettingsReference(
        int Ordinal,
        string? RelationshipId);

    private sealed record TablePartReference(
        int Ordinal,
        string? RelationshipId);

    private sealed record LegacyDrawingReference(
        string ElementName,
        int Ordinal,
        string? RelationshipId);

    private sealed record PivotCacheReference(
        int Ordinal,
        int CacheId,
        string PackagePart);

    private sealed record PivotTablePartReference(
        int Ordinal,
        string? RelationshipId);

    private sealed record WorkbookExternalReference(
        int Ordinal,
        string? RelationshipId);

    private sealed record ExternalBookReference(
        int Ordinal,
        string? RelationshipId);

    private sealed record PackageRelationshipReference(
        string RelationshipPart,
        XElement Relationship);

    private sealed record SlicerTimelineRelationshipReference(
        int Ordinal,
        string Description,
        string? RelationshipId);
}
