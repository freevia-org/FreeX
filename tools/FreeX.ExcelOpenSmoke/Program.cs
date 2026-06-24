using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Free.Shared.Opc;
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
    private const string CustomDocumentPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.custom-properties+xml";
    private const string CustomDocumentPropertiesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    private const string ExtendedPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.extended-properties+xml";
    private const string ExtendedPropertiesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";
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
    private const string PackageCorePropertiesContentType =
        "application/vnd.openxmlformats-package.core-properties+xml";
    private const string PackageCorePropertiesRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    private const string PrinterSettingsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.printerSettings";
    private const string PrinterSettingsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings";
    private const string WorksheetCustomPropertyContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.customProperty";
    private const string WorksheetCustomPropertyRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customProperty";
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
    private const string ThemeContentType =
        "application/vnd.openxmlformats-officedocument.theme+xml";
    private const string ThemeRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";
    private const string TimelineContentType =
        "application/vnd.ms-excel.timeline+xml";
    private const string TimelineCacheContentType =
        "application/vnd.ms-excel.timelineCache+xml";
    private const string TimelineRelationshipType =
        "http://schemas.microsoft.com/office/2010/relationships/Timeline";
    private const string TimelineCacheRelationshipType =
        "http://schemas.microsoft.com/office/2010/relationships/TimelineCache";
    private const string TimelineRelationshipType2011 =
        "http://schemas.microsoft.com/office/2011/relationships/timeline";
    private const string TimelineCacheRelationshipType2011 =
        "http://schemas.microsoft.com/office/2011/relationships/timelineCache";
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
    private static readonly XNamespace PackageCorePropertiesNs =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace ExtendedPropertiesNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    private static readonly XNamespace CustomDocumentPropertiesNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    private static readonly XNamespace OfficeRelationshipNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace CustomXmlNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
    private static readonly XNamespace SpreadsheetNs =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace SpreadsheetRevision2Ns =
        "http://schemas.microsoft.com/office/spreadsheetml/2015/revision2";
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
    private static readonly char[] InvalidWorkbookSheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];

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

            if (options.GenerateExcelPivotCorpusFixtures)
            {
                foreach (var fixturePath in GetExcelPivotCorpusFixturePaths(Path.Combine(runDirectory, "generated-excel-pivots")))
                {
                    AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                        fixturePath,
                        WorkbookValidationWorkflow.FreeXSaveThenExcel,
                        "Excel-authored native PivotTable corpus fixture",
                        GenerateWithExcel: true,
                        Expectations: ExcelNativePivotCorpusExpectations(options.SaveReopen)));
                }
            }

            if (options.GenerateExcelCommentCorpusFixtures)
            {
                foreach (var fixturePath in GetExcelCommentCorpusFixturePaths(Path.Combine(runDirectory, "generated-excel-comments")))
                {
                    AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                        fixturePath,
                        WorkbookValidationWorkflow.DirectExcel,
                        "Excel/FreeX-authored comment corpus fixture",
                        GenerateWithExcel: true));
                }
            }

            if (options.GenerateExcelCfCorpusFixtures)
            {
                foreach (var fixturePath in GetExcelCfCorpusFixturePaths(Path.Combine(runDirectory, "generated-excel-cf")))
                {
                    AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                        fixturePath,
                        WorkbookValidationWorkflow.DirectExcel,
                        "Excel-authored CF corpus fixture",
                        GenerateWithExcel: true));
                }
            }

            if (options.GenerateExcelTableCorpusFixtures)
            {
                foreach (var fixturePath in GetExcelTableCorpusFixturePaths(Path.Combine(runDirectory, "generated-excel-tables")))
                {
                    AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                        fixturePath,
                        WorkbookValidationWorkflow.DirectExcel,
                        "Excel-authored structured-table corpus fixture",
                        GenerateWithExcel: true));
                }
            }

            if (options.GenerateExcelSparklineCorpusFixtures)
            {
                foreach (var fixturePath in GetExcelSparklineCorpusFixturePaths(Path.Combine(runDirectory, "generated-excel-sparklines")))
                {
                    AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                        fixturePath,
                        WorkbookValidationWorkflow.DirectExcel,
                        "Excel-authored sparkline corpus fixture",
                        GenerateWithExcel: true));
                }
            }

            if (smokeInputs.Count == 0)
            {
                if (CorpusSelectionHasOnlyMissingOptionalPrivateRows(corpusSelection))
                {
                    var zeroInputSummary = new ExcelSmokeSummary(0, 0, 0, []);
                    Console.WriteLine(options.SaveReopen ? "Excel save/reopen smoke" : "Excel open smoke");
                    Console.WriteLine($"Run directory: {runDirectory}");
                    Console.WriteLine("Input count: 0");
                    Console.WriteLine($"Validation mode: {(options.SaveReopen ? "open -> SaveCopyAs -> close -> reopen" : "open only")}");
                    Console.WriteLine($"Corpus manifest: {corpusSelection!.ManifestPath}");
                    Console.WriteLine($"Corpus selected: {corpusSelection.Inputs.Count}; skipped: {corpusSelection.Skipped.Count}");
                    WriteMachineReadableReport(runDirectory, options, zeroInputSummary, corpusSelection);
                    Console.WriteLine("PASS: Corpus rows were skipped because optional private local workbooks are missing.");
                    return 0;
                }

                Console.Error.WriteLine("No XLSX/XLSM files matched the requested inputs.");
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

    private static bool CorpusSelectionHasOnlyMissingOptionalPrivateRows(CorpusManifestSelection? corpusSelection)
    {
        if (corpusSelection is null || corpusSelection.Inputs.Count > 0)
            return false;

        var selectedSkips = corpusSelection.Skipped
            .Where(skip => !IsSelectionFilterSkip(skip.Reason))
            .ToArray();

        return selectedSkips.Length > 0 &&
            selectedSkips.All(skip =>
                skip.Reason == "missing-file" &&
                string.Equals(skip.Row.SourceType, "local-private", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSelectionFilterSkip(string reason) =>
        reason is "id-filter" or "source-filter" or "status-filter";

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
                AssertFreeXSaveWarnings(input, "FreeX source save", freeXSave.SaveWarnings);
                AssertPackageHealth(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertNoExcelRecoveryLog(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertOpenXmlValid(freeXSave.SavedPath, "FreeX-saved workbook");
                AssertWorkbookPackageRoot(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertDocumentPropertiesPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookSheetRelationshipsComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookThemePackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookFileVersionMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookFileSharingMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookPropertiesMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookProtectionMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookViewMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookFunctionGroupsMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookDefinedNamesMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookCalculationPropertiesMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookOleSizeMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookWebPublishingMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookFileRecoveryMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookWebPublishObjectsMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorkbookExtensionListMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertSharedStringTableComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertStylesPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetHyperlinkPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetDrawingPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetBackgroundImagePackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetPrinterSettingsPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetCustomPropertyPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetScenarioPackageComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetSheetPropertiesMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetDimensionMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetCellStructureMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetSheetFormatMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetCalculationPropertiesMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetProtectionMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetAutoFilterMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetSheetViewsMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetCustomSheetViewsMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetMergeCellsMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetPhoneticPropertiesMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetSortAndDataConsolidationMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetConditionalFormattingMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetDataValidationMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetPrintOptionsMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetPageMarginsMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetPageSetupMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetHeaderFooterMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetPageBreakMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetDiagnosticMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetSingleXmlCellsMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertSmartTagMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
                AssertWorksheetExtensionListMetadataComplete(freeXSave.SavedPath, "FreeX-saved workbook", input.SourcePath);
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
                freeXPreSaveWarnings = CombineFreeXWarnings(freeXSave.LoadWarnings, freeXSave.SaveWarnings);
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
                FormatFailure(ex),
                ex.Data["ExpectationFailureCounter"] as string,
                ex.Data["ExpectationFailureKind"] as string);
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
            AssertPackageHealth(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertOpenXmlValid(excelSavedPath, "Excel-saved workbook");
            AssertWorkbookPackageRoot(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertDocumentPropertiesPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookSheetRelationshipsComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookThemePackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookFileVersionMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookFileSharingMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookPropertiesMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookProtectionMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookViewMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookFunctionGroupsMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookDefinedNamesMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookCalculationPropertiesMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookOleSizeMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookWebPublishingMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookFileRecoveryMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookWebPublishObjectsMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorkbookExtensionListMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertSharedStringTableComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertStylesPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetHyperlinkPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetDrawingPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetBackgroundImagePackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetPrinterSettingsPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetCustomPropertyPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetScenarioPackageComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetSheetPropertiesMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetDimensionMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetCellStructureMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetSheetFormatMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetCalculationPropertiesMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetProtectionMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetAutoFilterMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetSheetViewsMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetCustomSheetViewsMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetMergeCellsMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetPhoneticPropertiesMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetSortAndDataConsolidationMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetConditionalFormattingMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetDataValidationMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetPrintOptionsMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetPageMarginsMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetPageSetupMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetHeaderFooterMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetPageBreakMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetDiagnosticMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetSingleXmlCellsMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertSmartTagMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
            AssertWorksheetExtensionListMetadataComplete(excelSavedPath, "Excel-saved workbook", stagedPath);
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

    private static void AssertPackageHealth(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = XlsxPackageHealthValidator.Validate(archive);
        if (issues.Count == 0)
            return;

        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid package health: {sample}{suffix}");
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

        if (HasSharedStringPackageTag(tags) &&
            !PackageEntryExists(archive, "xl/sharedStrings.xml"))
        {
            issues.Add("missing xl/sharedStrings.xml for public shared-strings tag");
        }

        if (HasSharedStringPackageTag(tags) &&
            !PackageRelationshipExists(
                archive,
                new PackageRelationshipExpectation(
                    WorkbookRelationshipPart,
                    SharedStringsRelationshipType,
                    "xl/sharedStrings.xml")))
        {
            issues.Add("missing workbook relationship to xl/sharedStrings.xml for public shared-strings tag");
        }

        if (HasSharedStringPackageTag(tags))
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
        HasSharedStringPackageTag(tags) ||
        tags.Contains("hyperlinks") ||
        tags.Contains("merged-cells") ||
        tags.Contains("inline-strings") ||
        tags.Contains("cell-types") ||
        (tags.Contains("sheet-names") && tags.Contains("boundary")) ||
        tags.Contains("unsupported-sheet-types");

    private static bool HasExpectedPublicWorksheetPackageTags(IReadOnlySet<string> tags) =>
        tags.Contains("styles") ||
        tags.Contains("formatting") ||
        HasSharedStringPackageTag(tags) ||
        tags.Contains("hyperlinks") ||
        tags.Contains("merged-cells") ||
        tags.Contains("inline-strings") ||
        tags.Contains("cell-types") ||
        (tags.Contains("sheet-names") && tags.Contains("boundary"));

    private static bool HasSharedStringPackageTag(IReadOnlySet<string> tags) =>
        tags.Contains("shared-strings") ||
        tags.Contains("shared-string-package");

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
        return LoadPackageXml(stream);
    }

    private static XDocument LoadPackageXml(Stream stream)
    {
        using var reader = XmlReader.Create(stream, SecureXmlReaderSettings.Create());
        return XDocument.Load(reader);
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
            contentTypesXml = LoadPackageXml(stream);

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
            contentTypesXml = LoadPackageXml(stream);

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
                relationshipsXml = LoadPackageXml(stream);

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

    private static void AssertDocumentPropertiesPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var relationshipEntry = FindPackageEntry(archive, PackageRootRelationshipPart);
        XElement[] rootRelationships = relationshipEntry is null
            ? []
            : LoadPackageXml(relationshipEntry)
                .Root?
                .Elements(PackageRelationshipNs + "Relationship")
                .ToArray() ?? [];

        foreach (var definition in GetDocumentPropertyPackageDefinitions())
        {
            var relationships = rootRelationships
                .Where(relationship => string.Equals(
                    relationship.Attribute("Type")?.Value,
                    definition.RelationshipType,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (relationships.Length > 1)
                issues.Add($"{PackageRootRelationshipPart} has {relationships.Length} {definition.Label} relationships; expected at most one");

            var relationshipTargetsExpectedPart = false;
            foreach (var relationship in relationships)
            {
                if (AddDocumentPropertyRelationshipIssues(archive, definition, relationship, issues))
                    relationshipTargetsExpectedPart = true;
            }

            var expectedEntry = FindPackageEntry(archive, definition.PackagePart);
            if (expectedEntry is not null && relationships.Length == 0)
            {
                issues.Add($"{definition.PackagePart} exists without a package root {definition.Label} relationship");
                AddDocumentPropertyPartIssues(archive, definition, expectedEntry, issues);
            }
            else if (expectedEntry is not null && !relationshipTargetsExpectedPart)
            {
                issues.Add($"{definition.PackagePart} exists but no package root {definition.Label} relationship targets it");
                AddDocumentPropertyPartIssues(archive, definition, expectedEntry, issues);
            }
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidDocumentPropertiesPackage(label, sourcePath, issues);
    }

    private static DocumentPropertyPackageDefinition[] GetDocumentPropertyPackageDefinitions() =>
    [
        new(
            "core-properties",
            PackageCorePropertiesRelationshipType,
            "docProps/core.xml",
            PackageCorePropertiesContentType,
            PackageCorePropertiesNs + "coreProperties"),
        new(
            "extended-properties",
            ExtendedPropertiesRelationshipType,
            "docProps/app.xml",
            ExtendedPropertiesContentType,
            ExtendedPropertiesNs + "Properties"),
        new(
            "custom-properties",
            CustomDocumentPropertiesRelationshipType,
            "docProps/custom.xml",
            CustomDocumentPropertiesContentType,
            CustomDocumentPropertiesNs + "Properties")
    ];

    private static bool AddDocumentPropertyRelationshipIssues(
        ZipArchive archive,
        DocumentPropertyPackageDefinition definition,
        XElement relationship,
        List<string> issues)
    {
        var relationshipId = relationship.Attribute("Id")?.Value ?? "(no Id)";
        var relationshipLabel = $"{PackageRootRelationshipPart} {definition.Label} relationship {relationshipId}";

        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipLabel} is external");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipLabel} has invalid TargetMode {targetMode}");
            return false;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
        {
            issues.Add($"{relationshipLabel} has no Target");
            return false;
        }

        target = target.Trim();
        if (IsAbsoluteRelationshipTarget(target))
        {
            issues.Add($"{relationshipLabel} targets external URI without TargetMode=External: {target}");
            return false;
        }

        if (!TryResolvePackageRelationshipTarget(
                PackageRootRelationshipPart,
                target,
                out var packagePart,
                out var targetIssue))
        {
            issues.Add($"{relationshipLabel} has invalid Target {target}: {targetIssue}");
            return false;
        }

        var targetsExpectedPart = string.Equals(
            packagePart,
            definition.PackagePart,
            StringComparison.OrdinalIgnoreCase);
        if (!targetsExpectedPart)
            issues.Add($"{relationshipLabel} targets {packagePart}; expected {definition.PackagePart}");

        var packageEntry = FindPackageEntry(archive, packagePart);
        if (packageEntry is null)
        {
            issues.Add($"{relationshipLabel} targets missing package part {packagePart}");
            return targetsExpectedPart;
        }

        AddDocumentPropertyPartIssues(archive, definition with { PackagePart = packagePart }, packageEntry, issues);
        return targetsExpectedPart;
    }

    private static void AddDocumentPropertyPartIssues(
        ZipArchive archive,
        DocumentPropertyPackageDefinition definition,
        ZipArchiveEntry packageEntry,
        List<string> issues)
    {
        var contentTypeIssue = FindPackageContentTypeIssue(archive, definition.PackagePart, definition.ContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var document = LoadPackageXml(packageEntry);
        if (document.Root?.Name != definition.RootElement)
            issues.Add($"{definition.PackagePart} has an invalid {definition.Label} root element");
    }

    private static void ThrowInvalidDocumentPropertiesPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid document properties package graph: {sample}{suffix}");
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
        var workbookXml = LoadPackageXml(workbookEntry);
        var workbookRoot = workbookXml.Root;
        if (workbookRoot is null)
        {
            issues.Add($"{WorkbookPart} has no workbook root element");
            ThrowInvalidWorkbookSheetGraph(label, sourcePath, issues);
            return;
        }

        if (workbookRoot.Name != SpreadsheetNs + "workbook")
            issues.Add($"{WorkbookPart} has invalid workbook root element {workbookRoot.Name.LocalName}");

        var sheetsContainers = workbookRoot.Elements(SpreadsheetNs + "sheets").ToArray();
        if (sheetsContainers.Length == 0)
            issues.Add("workbook has no sheets container");

        if (sheetsContainers.Length > 1)
            issues.Add($"workbook has {sheetsContainers.Length} sheets containers; expected one");

        foreach (var sheetsContainer in sheetsContainers.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            foreach (var unexpectedChild in sheetsContainer.Element.Elements().Where(element => element.Name != SpreadsheetNs + "sheet"))
            {
                issues.Add($"workbook sheets container #{sheetsContainer.Ordinal} has unexpected child element {unexpectedChild.Name.LocalName}; expected sheet entries only");
            }
        }

        var sheets = sheetsContainers
            .SelectMany(container => container.Elements(SpreadsheetNs + "sheet"))
            .ToArray();
        if (sheets.Length == 0)
            issues.Add("workbook has no sheet elements");

        AddWorkbookSheetViewIndexIssues(workbookRoot, sheets.Length, issues);

        var seenRelationshipIds = new HashSet<string>(StringComparer.Ordinal);
        var seenSheetParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSheetIds = new HashSet<uint>();
        foreach (var sheetReference in sheets.Select((element, index) => new WorkbookSheetReference(index + 1, element)))
        {
            var sheet = sheetReference.Element;
            var sheetName = sheet.Attribute("name")?.Value;
            var sheetDescription = FormatWorkbookSheetDescription(sheetReference.Ordinal, sheetName);
            AddWorkbookSheetNameIssues(sheetDescription, sheetName, seenSheetNames, issues);
            AddWorkbookSheetIdIssues(sheetDescription, sheet.Attribute("sheetId")?.Value, seenSheetIds, issues);
            AddWorkbookSheetStateIssues(sheetDescription, sheet.Attribute("state")?.Value, issues);

            if (sheet.Elements().Any())
                issues.Add($"{sheetDescription} has child elements; expected attributes only");

            var relationshipId = sheet.Attribute(OfficeRelationshipNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId))
            {
                issues.Add($"{sheetDescription} has no relationship id");
                continue;
            }

            if (!seenRelationshipIds.Add(relationshipId))
            {
                issues.Add($"{sheetDescription} reuses relationship id {relationshipId}");
                continue;
            }

            var relationship = relationships.FirstOrDefault(relationship =>
                string.Equals(
                    relationship.Attribute("Id")?.Value,
                    relationshipId,
                    StringComparison.Ordinal));
            if (relationship is null)
            {
                issues.Add($"{sheetDescription} targets missing relationship {relationshipId} in {WorkbookRelationshipPart}");
                continue;
            }

            var relationshipType = relationship.Attribute("Type")?.Value;
            if (!TryGetWorkbookSheetExpectedContentType(relationshipType, out var expectedContentType))
            {
                issues.Add($"{sheetDescription} relationship {relationshipId} has unsupported Type={relationshipType}");
                continue;
            }

            var targetMode = relationship.Attribute("TargetMode")?.Value;
            if (string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{sheetDescription} relationship {relationshipId} must not target an external sheet package part");
                continue;
            }

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
            {
                issues.Add($"{sheetDescription} relationship {relationshipId} has no Target");
                continue;
            }

            if (!TryResolvePackageRelationshipTarget(WorkbookRelationshipPart, target, out var sheetPart, out var targetError))
            {
                issues.Add($"{sheetDescription} relationship {relationshipId} has invalid Target {target}: {targetError}");
                continue;
            }

            if (!PackageEntryExists(archive, sheetPart))
            {
                issues.Add($"{sheetDescription} relationship {relationshipId} targets missing package part {sheetPart}");
                continue;
            }

            if (!seenSheetParts.Add(sheetPart))
                issues.Add($"{sheetDescription} relationship {relationshipId} reuses package part {sheetPart}");

            var contentTypeIssue = FindPackageContentTypeIssue(archive, sheetPart, expectedContentType);
            if (contentTypeIssue is not null)
                issues.Add(contentTypeIssue);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookSheetGraph(label, sourcePath, issues);
    }

    private static string FormatWorkbookSheetDescription(int ordinal, string? sheetName)
    {
        if (string.IsNullOrWhiteSpace(sheetName))
            return $"workbook sheet #{ordinal}";

        return $"workbook sheet #{ordinal} '{sheetName}'";
    }

    private static void AddWorkbookSheetNameIssues(
        string sheetDescription,
        string? sheetName,
        HashSet<string> seenSheetNames,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(sheetName))
        {
            issues.Add($"{sheetDescription} has no name");
            return;
        }

        if (sheetName.Length > 31)
            issues.Add($"{sheetDescription} name is {sheetName.Length} characters; expected 31 or fewer");

        if (sheetName.IndexOfAny(InvalidWorkbookSheetNameChars) >= 0)
            issues.Add($"{sheetDescription} name contains invalid Excel sheet-name character(s)");

        if (!seenSheetNames.Add(sheetName))
            issues.Add($"{sheetDescription} duplicates another workbook sheet name ignoring case");
    }

    private static void AddWorkbookSheetIdIssues(
        string sheetDescription,
        string? sheetIdText,
        HashSet<uint> seenSheetIds,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(sheetIdText))
        {
            issues.Add($"{sheetDescription} has no sheetId");
            return;
        }

        if (!uint.TryParse(sheetIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var sheetId) ||
            sheetId == 0)
        {
            issues.Add($"{sheetDescription} has invalid sheetId value '{sheetIdText}'");
            return;
        }

        if (!seenSheetIds.Add(sheetId))
            issues.Add($"{sheetDescription} duplicates workbook sheetId {sheetId}");
    }

    private static void AddWorkbookSheetStateIssues(
        string sheetDescription,
        string? state,
        List<string> issues)
    {
        if (!string.IsNullOrWhiteSpace(state) && !IsKnownWorkbookViewVisibility(state))
            issues.Add($"{sheetDescription} has invalid state value '{state}'");
    }

    private static void AddWorkbookSheetViewIndexIssues(XElement workbookRoot, int sheetCount, List<string> issues)
    {
        if (sheetCount <= 0)
            return;

        foreach (var bookViews in workbookRoot.Elements(SpreadsheetNs + "bookViews").Select((element, index) => new WorkbookBookViewsReference(index + 1, element)))
        {
            var bookViewsDescription = $"bookViews #{bookViews.Ordinal}";
            foreach (var workbookView in bookViews.Element.Elements(SpreadsheetNs + "workbookView").Select((element, index) => new WorkbookViewReference(index + 1, element)))
            {
                var viewDescription = $"{bookViewsDescription} workbookView #{workbookView.Ordinal}";
                AddWorkbookSheetViewIndexIssue(viewDescription, "firstSheet", workbookView.Element.Attribute("firstSheet")?.Value, sheetCount, issues);
                AddWorkbookSheetViewIndexIssue(viewDescription, "activeTab", workbookView.Element.Attribute("activeTab")?.Value, sheetCount, issues);
            }
        }
    }

    private static void AddWorkbookSheetViewIndexIssue(
        string viewDescription,
        string attributeName,
        string? value,
        int sheetCount,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var viewIndex))
        {
            issues.Add($"{WorkbookPart} {viewDescription} has invalid {attributeName} value '{value}'");
            return;
        }

        if (viewIndex >= (uint)sheetCount)
            issues.Add($"{WorkbookPart} {viewDescription} has {attributeName} index {viewIndex} outside workbook sheet count {sheetCount}");
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

    private static void AssertWorkbookThemePackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var themeRelationships = FindPackageRelationshipsByType(
            archive,
            WorkbookRelationshipPart,
            ThemeRelationshipType);
        var themeEntries = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Where(entry => IsWorkbookThemePart(NormalizePackagePart(entry.FullName)))
            .ToArray();
        if (themeRelationships.Length == 0 && themeEntries.Length == 0)
            return;

        var issues = new List<string>();
        if (themeRelationships.Length > 1)
            issues.Add($"{WorkbookRelationshipPart} has {themeRelationships.Length} workbook theme relationships; expected at most one");

        var referencedThemeParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validatedThemeParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in themeRelationships)
        {
            AddWorkbookThemeRelationshipIssues(
                archive,
                relationship,
                referencedThemeParts,
                validatedThemeParts,
                issues);
        }

        foreach (var themeEntry in themeEntries)
        {
            var themePart = NormalizePackagePart(themeEntry.FullName);
            if (referencedThemeParts.Contains(themePart))
                continue;

            issues.Add($"{themePart} is present without a workbook theme relationship");
            if (validatedThemeParts.Add(themePart))
                AddWorkbookThemePartIssues(archive, themePart, themeEntry, issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookThemePackage(label, sourcePath, issues);
    }

    private static void AddWorkbookThemeRelationshipIssues(
        ZipArchive archive,
        XElement relationship,
        HashSet<string> referencedThemeParts,
        HashSet<string> validatedThemeParts,
        List<string> issues)
    {
        var relationshipId = relationship.Attribute("Id")?.Value;
        var relationshipLabel =
            $"{WorkbookRelationshipPart} workbook theme relationship {FormatRelationshipIssueId(relationshipId)}";

        if (!string.Equals(relationship.Attribute("Type")?.Value, ThemeRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{relationshipLabel} has Type={relationship.Attribute("Type")?.Value}; expected {ThemeRelationshipType}");
            return;
        }

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
                out var themePart,
                out var targetIssue))
        {
            issues.Add($"{relationshipLabel} has invalid Target {target}: {targetIssue}");
            return;
        }

        referencedThemeParts.Add(themePart);

        if (!IsWorkbookThemePart(themePart))
            issues.Add($"{relationshipLabel} targets {themePart}, which is not an xl/theme XML part");

        var themeEntry = FindPackageEntry(archive, themePart);
        if (themeEntry is null)
        {
            issues.Add($"{relationshipLabel} targets missing package part {themePart}");
            return;
        }

        if (validatedThemeParts.Add(themePart))
            AddWorkbookThemePartIssues(archive, themePart, themeEntry, issues);
    }

    private static bool IsWorkbookThemePart(string packagePart)
    {
        packagePart = NormalizePackagePart(packagePart);
        return packagePart.StartsWith("xl/theme/", StringComparison.OrdinalIgnoreCase) &&
            packagePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddWorkbookThemePartIssues(
        ZipArchive archive,
        string themePart,
        ZipArchiveEntry themeEntry,
        List<string> issues)
    {
        var contentTypeIssue = FindPackageContentTypeIssue(archive, themePart, ThemeContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        XDocument themeXml;
        try
        {
            themeXml = LoadPackageXml(themeEntry);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Xml.XmlException)
        {
            issues.Add($"{themePart} is not parseable theme XML: {ex.Message}");
            return;
        }

        var themeRoot = themeXml.Root;
        if (themeRoot?.Name != DrawingNs + "theme")
        {
            issues.Add($"{themePart} has an invalid theme root element");
            return;
        }

        AddDrawingMlChildOrderIssues(
            $"{themePart} theme",
            themeRoot,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["themeElements"] = 0,
                ["objectDefaults"] = 1,
                ["extraClrSchemeLst"] = 2,
                ["custClrLst"] = 3,
                ["extLst"] = 4
            },
            issues);

        var themeElements = FindRequiredSingleDrawingMlChild(
            themeRoot,
            "themeElements",
            $"{themePart} theme",
            issues);
        if (themeElements is null)
            return;

        AddWorkbookThemeElementsIssues(themePart, themeElements, issues);
    }

    private static void AddWorkbookThemeElementsIssues(
        string themePart,
        XElement themeElements,
        List<string> issues)
    {
        AddDrawingMlChildOrderIssues(
            $"{themePart} themeElements",
            themeElements,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["clrScheme"] = 0,
                ["fontScheme"] = 1,
                ["fmtScheme"] = 2,
                ["extLst"] = 3
            },
            issues);

        var colorScheme = FindRequiredSingleDrawingMlChild(
            themeElements,
            "clrScheme",
            $"{themePart} themeElements",
            issues);
        if (colorScheme is not null)
            AddWorkbookThemeColorSchemeIssues(themePart, colorScheme, issues);

        var fontScheme = FindRequiredSingleDrawingMlChild(
            themeElements,
            "fontScheme",
            $"{themePart} themeElements",
            issues);
        if (fontScheme is not null)
            AddWorkbookThemeFontSchemeIssues(themePart, fontScheme, issues);

        var formatScheme = FindRequiredSingleDrawingMlChild(
            themeElements,
            "fmtScheme",
            $"{themePart} themeElements",
            issues);
        if (formatScheme is not null)
            AddWorkbookThemeFormatSchemeIssues(themePart, formatScheme, issues);
    }

    private static void AddWorkbookThemeColorSchemeIssues(
        string themePart,
        XElement colorScheme,
        List<string> issues)
    {
        AddRequiredNonEmptyAttributeIssue($"{themePart} clrScheme", colorScheme, "name", issues);
        AddDrawingMlChildOrderIssues(
            $"{themePart} clrScheme",
            colorScheme,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["dk1"] = 0,
                ["lt1"] = 1,
                ["dk2"] = 2,
                ["lt2"] = 3,
                ["accent1"] = 4,
                ["accent2"] = 5,
                ["accent3"] = 6,
                ["accent4"] = 7,
                ["accent5"] = 8,
                ["accent6"] = 9,
                ["hlink"] = 10,
                ["folHlink"] = 11,
                ["extLst"] = 12
            },
            issues);

        foreach (var colorSlot in new[]
                 {
                     "dk1",
                     "lt1",
                     "dk2",
                     "lt2",
                     "accent1",
                     "accent2",
                     "accent3",
                     "accent4",
                     "accent5",
                     "accent6",
                     "hlink",
                     "folHlink"
                 })
        {
            FindRequiredSingleDrawingMlChild(colorScheme, colorSlot, $"{themePart} clrScheme", issues);
        }
    }

    private static void AddWorkbookThemeFontSchemeIssues(
        string themePart,
        XElement fontScheme,
        List<string> issues)
    {
        AddRequiredNonEmptyAttributeIssue($"{themePart} fontScheme", fontScheme, "name", issues);
        AddDrawingMlChildOrderIssues(
            $"{themePart} fontScheme",
            fontScheme,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["majorFont"] = 0,
                ["minorFont"] = 1,
                ["extLst"] = 2
            },
            issues);
        FindRequiredSingleDrawingMlChild(fontScheme, "majorFont", $"{themePart} fontScheme", issues);
        FindRequiredSingleDrawingMlChild(fontScheme, "minorFont", $"{themePart} fontScheme", issues);
    }

    private static void AddWorkbookThemeFormatSchemeIssues(
        string themePart,
        XElement formatScheme,
        List<string> issues)
    {
        AddRequiredNonEmptyAttributeIssue($"{themePart} fmtScheme", formatScheme, "name", issues);
        AddDrawingMlChildOrderIssues(
            $"{themePart} fmtScheme",
            formatScheme,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["fillStyleLst"] = 0,
                ["lnStyleLst"] = 1,
                ["effectStyleLst"] = 2,
                ["bgFillStyleLst"] = 3,
                ["extLst"] = 4
            },
            issues);
        FindRequiredSingleDrawingMlChild(formatScheme, "fillStyleLst", $"{themePart} fmtScheme", issues);
        FindRequiredSingleDrawingMlChild(formatScheme, "lnStyleLst", $"{themePart} fmtScheme", issues);
        FindRequiredSingleDrawingMlChild(formatScheme, "effectStyleLst", $"{themePart} fmtScheme", issues);
        FindRequiredSingleDrawingMlChild(formatScheme, "bgFillStyleLst", $"{themePart} fmtScheme", issues);
    }

    private static XElement? FindRequiredSingleDrawingMlChild(
        XElement parent,
        string localName,
        string description,
        List<string> issues)
    {
        var children = parent.Elements(DrawingNs + localName).ToArray();
        if (children.Length == 0)
        {
            issues.Add($"{description} is missing {localName}");
            return null;
        }

        if (children.Length > 1)
            issues.Add($"{description} has {children.Length} {localName} elements; expected one");

        return children[0];
    }

    private static void AddRequiredNonEmptyAttributeIssue(
        string description,
        XElement element,
        string attributeName,
        List<string> issues)
    {
        if (!string.IsNullOrWhiteSpace(element.Attribute(attributeName)?.Value))
            return;

        issues.Add($"{description} has no {attributeName}");
    }

    private static void AddDrawingMlChildOrderIssues(
        string description,
        XElement parent,
        IReadOnlyDictionary<string, int> expectedOrder,
        List<string> issues)
    {
        var lastOrder = -1;
        string? lastElementName = null;
        foreach (var child in parent.Elements())
        {
            if (child.Name.Namespace != DrawingNs)
            {
                issues.Add($"{description} has unexpected child '{child.Name}'");
                continue;
            }

            if (!expectedOrder.TryGetValue(child.Name.LocalName, out var order))
            {
                issues.Add($"{description} has unexpected child {child.Name.LocalName}");
                continue;
            }

            if (order < lastOrder)
            {
                issues.Add($"{description} child {child.Name.LocalName} appears after {lastElementName}; expected schema order");
                continue;
            }

            lastOrder = order;
            lastElementName = child.Name.LocalName;
        }
    }

    private static void ThrowInvalidWorkbookThemePackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook theme package graph: {sample}{suffix}");
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
        AddStylesheetMetadataIssues(stylesXml.Root, issues);
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

    private static void AddStylesheetMetadataIssues(XElement stylesheetRoot, List<string> issues)
    {
        AddStylesheetRootChildOrderingIssues(stylesheetRoot, issues);
        AddStylesheetSingleChildIssues(stylesheetRoot, "colors", issues);
        AddStylesheetSingleChildIssues(stylesheetRoot, "dxfs", issues);
        AddStylesheetSingleChildIssues(stylesheetRoot, "tableStyles", issues);
        AddStylesheetSingleChildIssues(stylesheetRoot, "extLst", issues);

        var differentialStyleCount = 0;
        foreach (var differentialStyles in stylesheetRoot.Elements(SpreadsheetNs + "dxfs").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            differentialStyleCount = Math.Max(
                differentialStyleCount,
                AddStylesheetDifferentialStyleIssues(differentialStyles.Ordinal, differentialStyles.Element, issues));
        }

        foreach (var colors in stylesheetRoot.Elements(SpreadsheetNs + "colors").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddStylesheetColorsIssues(colors.Ordinal, colors.Element, issues);
        }

        foreach (var tableStyles in stylesheetRoot.Elements(SpreadsheetNs + "tableStyles").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddStylesheetTableStylesIssues(tableStyles.Ordinal, tableStyles.Element, differentialStyleCount, issues);
        }

        foreach (var extensionList in stylesheetRoot.Elements(SpreadsheetNs + "extLst").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues("xl/styles.xml", "styleSheet", extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddStylesheetRootChildOrderingIssues(XElement stylesheetRoot, List<string> issues)
    {
        var previousKnownChildOrder = -1;
        foreach (var child in stylesheetRoot.Elements())
        {
            var childOrder = GetStylesheetRootChildOrder(child);
            if (childOrder < 0)
                continue;

            if (childOrder < previousKnownChildOrder)
                issues.Add($"xl/styles.xml child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;
        }
    }

    private static int GetStylesheetRootChildOrder(XElement child)
    {
        if (child.Name.Namespace != SpreadsheetNs)
            return -1;

        return child.Name.LocalName switch
        {
            "numFmts" => 0,
            "fonts" => 1,
            "fills" => 2,
            "borders" => 3,
            "cellStyleXfs" => 4,
            "cellXfs" => 5,
            "cellStyles" => 6,
            "dxfs" => 7,
            "tableStyles" => 8,
            "colors" => 9,
            "extLst" => 10,
            _ => -1
        };
    }

    private static void AddStylesheetSingleChildIssues(XElement stylesheetRoot, string childName, List<string> issues)
    {
        var elements = stylesheetRoot.Elements(SpreadsheetNs + childName).ToArray();
        if (elements.Length > 1)
            issues.Add($"xl/styles.xml has {elements.Length} {childName} elements; expected at most one");
    }

    private static int AddStylesheetDifferentialStyleIssues(
        int ordinal,
        XElement differentialStyles,
        List<string> issues)
    {
        var description = $"dxfs #{ordinal}";
        var styles = differentialStyles.Elements(SpreadsheetNs + "dxf").ToArray();
        AddStyleCountAttributeIssues(issues, description, differentialStyles, styles.Length);

        var extensionLists = differentialStyles.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"xl/styles.xml {description} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var unexpectedChild in differentialStyles.Elements().Where(child =>
                     child.Name.Namespace == SpreadsheetNs &&
                     child.Name != SpreadsheetNs + "dxf" &&
                     child.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"xl/styles.xml {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var children = differentialStyles.Elements().ToArray();
        var firstExtensionListIndex = Array.FindIndex(children, child => child.Name == SpreadsheetNs + "extLst");
        if (firstExtensionListIndex >= 0 &&
            children.Skip(firstExtensionListIndex + 1).Any(child => child.Name == SpreadsheetNs + "dxf"))
        {
            issues.Add($"xl/styles.xml {description} has dxf entries after extLst; expected extLst last");
        }

        foreach (var style in styles.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddStylesheetDifferentialStyleEntryIssues(description, style.Ordinal, style.Element, issues);
        }

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues("xl/styles.xml", description, extensionList.Ordinal, extensionList.Element, issues);
        }

        return styles.Length;
    }

    private static void AddStylesheetDifferentialStyleEntryIssues(
        string differentialStylesDescription,
        int ordinal,
        XElement differentialStyle,
        List<string> issues)
    {
        var description = $"{differentialStylesDescription} dxf #{ordinal}";
        AddStylesheetDifferentialStyleChildOrderingIssues(description, differentialStyle, issues);

        var extensionLists = differentialStyle.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"xl/styles.xml {description} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues("xl/styles.xml", description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddStylesheetDifferentialStyleChildOrderingIssues(
        string description,
        XElement differentialStyle,
        List<string> issues)
    {
        var previousKnownChildOrder = -1;
        foreach (var child in differentialStyle.Elements())
        {
            var childOrder = GetStylesheetDifferentialStyleChildOrder(child);
            if (childOrder < 0)
                continue;

            if (childOrder < previousKnownChildOrder)
                issues.Add($"xl/styles.xml {description} child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;
        }
    }

    private static int GetStylesheetDifferentialStyleChildOrder(XElement child)
    {
        if (child.Name.Namespace != SpreadsheetNs)
            return -1;

        return child.Name.LocalName switch
        {
            "font" => 0,
            "numFmt" => 1,
            "fill" => 2,
            "alignment" => 3,
            "border" => 4,
            "protection" => 5,
            "extLst" => 6,
            _ => -1
        };
    }

    private static void AddStylesheetColorsIssues(int ordinal, XElement colors, List<string> issues)
    {
        var description = $"colors #{ordinal}";
        var indexedColorContainers = colors.Elements(SpreadsheetNs + "indexedColors").ToArray();
        var mruColorContainers = colors.Elements(SpreadsheetNs + "mruColors").ToArray();
        if (indexedColorContainers.Length > 1)
            issues.Add($"xl/styles.xml {description} has {indexedColorContainers.Length} indexedColors elements; expected at most one");
        if (mruColorContainers.Length > 1)
            issues.Add($"xl/styles.xml {description} has {mruColorContainers.Length} mruColors elements; expected at most one");

        foreach (var unexpectedChild in colors.Elements().Where(child =>
                     child.Name.Namespace == SpreadsheetNs &&
                     child.Name != SpreadsheetNs + "indexedColors" &&
                     child.Name != SpreadsheetNs + "mruColors"))
        {
            issues.Add($"xl/styles.xml {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var previousKnownChildOrder = -1;
        foreach (var child in colors.Elements())
        {
            var childOrder = child.Name == SpreadsheetNs + "indexedColors"
                ? 0
                : child.Name == SpreadsheetNs + "mruColors"
                    ? 1
                    : -1;
            if (childOrder < 0)
                continue;

            if (childOrder < previousKnownChildOrder)
                issues.Add($"xl/styles.xml {description} child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;
        }

        foreach (var indexedColors in indexedColorContainers.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddStylesheetColorContainerIssues(
                $"{description} indexedColors #{indexedColors.Ordinal}",
                indexedColors.Element,
                "rgbColor",
                requireRgb: true,
                issues);
        }

        foreach (var mruColors in mruColorContainers.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddStylesheetColorContainerIssues(
                $"{description} mruColors #{mruColors.Ordinal}",
                mruColors.Element,
                "color",
                requireRgb: false,
                issues);
        }
    }

    private static void AddStylesheetColorContainerIssues(
        string description,
        XElement colorContainer,
        string colorElementName,
        bool requireRgb,
        List<string> issues)
    {
        var colorName = SpreadsheetNs + colorElementName;
        var colors = colorContainer.Elements(colorName).ToArray();

        foreach (var unexpectedChild in colorContainer.Elements().Where(child => child.Name != colorName))
        {
            issues.Add($"xl/styles.xml {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        foreach (var color in colors.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            var colorDescription = $"{description} {colorElementName} #{color.Ordinal}";
            var hasKnownColorAttribute =
                color.Element.Attribute("rgb") is not null ||
                color.Element.Attribute("indexed") is not null ||
                color.Element.Attribute("theme") is not null ||
                color.Element.Attribute("auto") is not null;
            if (!hasKnownColorAttribute)
                issues.Add($"xl/styles.xml {colorDescription} has no color attribute");

            var rgb = color.Element.Attribute("rgb")?.Value;
            if (string.IsNullOrWhiteSpace(rgb))
            {
                if (requireRgb)
                    issues.Add($"xl/styles.xml {colorDescription} has no rgb");
            }
            else if (!IsValidPackageHexColor(rgb))
            {
                issues.Add($"xl/styles.xml {colorDescription} has invalid rgb value '{rgb}'");
            }

            AddOptionalPackageNonNegativeIntIssue("xl/styles.xml", colorDescription, "indexed", color.Element.Attribute("indexed")?.Value, issues);
            AddOptionalPackageNonNegativeIntIssue("xl/styles.xml", colorDescription, "theme", color.Element.Attribute("theme")?.Value, issues);
            AddOptionalPackageBooleanIssue("xl/styles.xml", colorDescription, "auto", color.Element.Attribute("auto")?.Value, issues);

            if (color.Element.Elements().Any())
                issues.Add($"xl/styles.xml {colorDescription} has child elements; expected attributes only");
        }
    }

    private static void AddStylesheetTableStylesIssues(
        int ordinal,
        XElement tableStyles,
        int differentialStyleCount,
        List<string> issues)
    {
        var description = $"tableStyles #{ordinal}";
        var styleElements = tableStyles.Elements(SpreadsheetNs + "tableStyle").ToArray();
        AddStyleCountAttributeIssues(issues, description, tableStyles, styleElements.Length);
        AddOptionalPackageStringAttributeIssue("xl/styles.xml", description, "defaultTableStyle", tableStyles.Attribute("defaultTableStyle")?.Value, issues);
        AddOptionalPackageStringAttributeIssue("xl/styles.xml", description, "defaultPivotStyle", tableStyles.Attribute("defaultPivotStyle")?.Value, issues);

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tableStyle in styleElements.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddStylesheetTableStyleIssues(description, tableStyle.Ordinal, tableStyle.Element, differentialStyleCount, seenNames, issues);
        }
    }

    private static void AddStylesheetTableStyleIssues(
        string tableStylesDescription,
        int ordinal,
        XElement tableStyle,
        int differentialStyleCount,
        HashSet<string> seenNames,
        List<string> issues)
    {
        var description = $"{tableStylesDescription} tableStyle #{ordinal}";
        var name = tableStyle.Attribute("name")?.Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            issues.Add($"xl/styles.xml {description} has no name");
        }
        else if (!seenNames.Add(name.Trim()))
        {
            issues.Add($"xl/styles.xml {tableStylesDescription} has duplicate tableStyle name '{name}'");
        }

        AddOptionalPackageBooleanIssue("xl/styles.xml", description, "pivot", tableStyle.Attribute("pivot")?.Value, issues);
        AddOptionalPackageBooleanIssue("xl/styles.xml", description, "table", tableStyle.Attribute("table")?.Value, issues);

        var elements = tableStyle.Elements(SpreadsheetNs + "tableStyleElement").ToArray();
        AddStyleCountAttributeIssues(issues, description, tableStyle, elements.Length);

        foreach (var unexpectedChild in tableStyle.Elements().Where(child =>
                     child.Name.Namespace == SpreadsheetNs &&
                     child.Name != SpreadsheetNs + "tableStyleElement" &&
                     child.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"xl/styles.xml {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var extensionLists = tableStyle.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"xl/styles.xml {description} has {extensionLists.Length} extLst elements; expected at most one");

        var children = tableStyle.Elements().ToArray();
        var firstExtensionListIndex = Array.FindIndex(children, child => child.Name == SpreadsheetNs + "extLst");
        if (firstExtensionListIndex >= 0 &&
            children.Skip(firstExtensionListIndex + 1).Any(child => child.Name == SpreadsheetNs + "tableStyleElement"))
        {
            issues.Add($"xl/styles.xml {description} has tableStyleElement entries after extLst; expected extLst last");
        }

        foreach (var element in elements.Select((child, index) => (Ordinal: index + 1, Element: child)))
        {
            AddStylesheetTableStyleElementIssues(description, element.Ordinal, element.Element, differentialStyleCount, issues);
        }

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues("xl/styles.xml", description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddStylesheetTableStyleElementIssues(
        string tableStyleDescription,
        int ordinal,
        XElement element,
        int differentialStyleCount,
        List<string> issues)
    {
        var description = $"{tableStyleDescription} tableStyleElement #{ordinal}";
        AddRequiredKnownPackageValueIssue(
            "xl/styles.xml",
            description,
            "type",
            element.Attribute("type")?.Value,
            [
                "wholeTable",
                "headerRow",
                "totalRow",
                "firstColumn",
                "lastColumn",
                "firstRowStripe",
                "secondRowStripe",
                "firstColumnStripe",
                "secondColumnStripe",
                "firstHeaderCell",
                "lastHeaderCell",
                "firstTotalCell",
                "lastTotalCell",
                "firstSubtotalColumn",
                "secondSubtotalColumn",
                "thirdSubtotalColumn",
                "firstSubtotalRow",
                "secondSubtotalRow",
                "thirdSubtotalRow",
                "blankRow",
                "firstColumnSubheading",
                "secondColumnSubheading",
                "thirdColumnSubheading",
                "firstRowSubheading",
                "secondRowSubheading",
                "thirdRowSubheading",
                "pageFieldLabels",
                "pageFieldValues"
            ],
            issues);

        AddOptionalPositivePackageIntIssue("xl/styles.xml", description, "size", element.Attribute("size")?.Value, issues);

        var dxfIdText = element.Attribute("dxfId")?.Value;
        if (!string.IsNullOrWhiteSpace(dxfIdText))
        {
            if (!TryParseNonNegativePackageInt(dxfIdText, out var dxfId))
            {
                issues.Add($"xl/styles.xml {description} has invalid dxfId value '{dxfIdText}'");
            }
            else if (differentialStyleCount == 0)
            {
                issues.Add($"xl/styles.xml {description} references dxfId {dxfId}, but dxfs is missing or empty");
            }
            else if (dxfId >= differentialStyleCount)
            {
                issues.Add($"xl/styles.xml {description} references dxfId {dxfId}, but dxfs contains {differentialStyleCount} entries");
            }
        }

        if (element.Elements().Any())
            issues.Add($"xl/styles.xml {description} has child elements; expected attributes only");
    }

    private static void AddRequiredKnownPackageValueIssue(
        string packagePart,
        string description,
        string attributeName,
        string? value,
        IReadOnlyCollection<string> knownValues,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{packagePart} {description} has no {attributeName}");
            return;
        }

        AddOptionalKnownPackageValueIssue(packagePart, description, attributeName, value, knownValues, issues);
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

    private static void AssertWorksheetCustomPropertyPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var referencedRelationships = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencedPackageParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetRelationshipPart = GetRelationshipPartForPackagePart(worksheetPart);
            var worksheetXml = LoadPackageXml(worksheetEntry);
            var customPropertyReferences = worksheetXml.Root?
                .Element(SpreadsheetNs + "customProperties")?
                .Elements(SpreadsheetNs + "customPr")
                .Select((customProperty, index) => new WorksheetCustomPropertyReference(
                    index + 1,
                    customProperty.Attribute("name")?.Value,
                    customProperty.Attribute("id")?.Value,
                    customProperty.Attribute(OfficeRelationshipNs + "id")?.Value))
                .ToArray() ?? [];

            foreach (var customPropertyReference in customPropertyReferences)
            {
                var referenceDescription = FormatWorksheetCustomPropertyReference(customPropertyReference);
                if (string.IsNullOrWhiteSpace(customPropertyReference.Name))
                    issues.Add($"{worksheetPart} {referenceDescription} has no name");

                if (!string.IsNullOrWhiteSpace(customPropertyReference.LegacyId) &&
                    (!int.TryParse(customPropertyReference.LegacyId, NumberStyles.None, CultureInfo.InvariantCulture, out var legacyId) ||
                     legacyId <= 0))
                {
                    issues.Add($"{worksheetPart} {referenceDescription} has invalid id '{customPropertyReference.LegacyId}'");
                }

                if (string.IsNullOrWhiteSpace(customPropertyReference.RelationshipId))
                {
                    if (string.IsNullOrWhiteSpace(customPropertyReference.LegacyId))
                        issues.Add($"{worksheetPart} {referenceDescription} has neither id nor relationship id");
                    continue;
                }

                var relationshipKey = $"{NormalizePackagePart(worksheetRelationshipPart)}|{customPropertyReference.RelationshipId}";
                if (!referencedRelationships.Add(relationshipKey))
                {
                    issues.Add($"{worksheetPart} {referenceDescription} duplicates worksheet custom-property relationship {customPropertyReference.RelationshipId}");
                    continue;
                }

                AddWorksheetCustomPropertyRelationshipIssues(
                    archive,
                    worksheetPart,
                    worksheetRelationshipPart,
                    referenceDescription,
                    customPropertyReference.RelationshipId,
                    referencedPackageParts,
                    issues);
            }

            foreach (var customPropertyRelationship in FindPackageRelationshipsByType(
                         archive,
                         worksheetRelationshipPart,
                         WorksheetCustomPropertyRelationshipType))
            {
                var relationshipId = customPropertyRelationship.Attribute("Id")?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    issues.Add($"{worksheetRelationshipPart} has a customProperty relationship without Id");
                    continue;
                }

                var relationshipKey = $"{NormalizePackagePart(worksheetRelationshipPart)}|{relationshipId}";
                if (!referencedRelationships.Contains(relationshipKey))
                    issues.Add($"{worksheetRelationshipPart} customProperty relationship {relationshipId} is not referenced by a worksheet customPr in {worksheetPart}");
            }
        }

        foreach (var customPropertyPart in FindWorksheetCustomPropertyParts(archive))
        {
            if (!referencedPackageParts.Contains(customPropertyPart))
                issues.Add($"{customPropertyPart} is present without a worksheet customPr relationship reference");
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetCustomPropertyPackage(label, sourcePath, issues);
    }

    private static void AddWorksheetCustomPropertyRelationshipIssues(
        ZipArchive archive,
        string worksheetPart,
        string worksheetRelationshipPart,
        string referenceDescription,
        string relationshipId,
        HashSet<string> referencedPackageParts,
        List<string> issues)
    {
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

        if (!string.Equals(relationship.Attribute("Type")?.Value, WorksheetCustomPropertyRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} in {worksheetRelationshipPart} has Type={relationship.Attribute("Type")?.Value}; expected {WorksheetCustomPropertyRelationshipType}");
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
                out var customPropertyPart,
                out var targetIssue))
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} in {worksheetRelationshipPart} has invalid Target {target}: {targetIssue}");
            return;
        }

        if (!IsWorksheetCustomPropertyPart(customPropertyPart))
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} targets {customPropertyPart}, which is not a worksheet custom-property binary part");

        var contentTypeIssue = FindPackageContentTypeIssue(archive, customPropertyPart, WorksheetCustomPropertyContentType);
        if (contentTypeIssue is not null)
            issues.Add(contentTypeIssue);

        var customPropertyEntry = FindPackageEntry(archive, customPropertyPart);
        if (customPropertyEntry is null)
        {
            issues.Add($"{worksheetPart} {referenceDescription} relationship {relationshipId} targets missing package part {customPropertyPart}");
            return;
        }

        referencedPackageParts.Add(customPropertyPart);
        if (customPropertyEntry.Length == 0)
            issues.Add($"{customPropertyPart} is an empty worksheet custom-property binary part");
    }

    private static HashSet<string> FindWorksheetCustomPropertyParts(ZipArchive archive) =>
        archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => NormalizePackagePart(entry.FullName))
            .Where(IsWorksheetCustomPropertyPart)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsWorksheetCustomPropertyPart(string packagePart)
    {
        packagePart = NormalizePackagePart(packagePart);
        if (!packagePart.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            return false;

        if (packagePart.StartsWith("xl/customProperty/", StringComparison.OrdinalIgnoreCase))
        {
            return !packagePart.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
        }

        if (!packagePart.StartsWith("xl/customProperty", StringComparison.OrdinalIgnoreCase))
            return false;

        var fileName = packagePart["xl/".Length..];
        return !fileName.Contains('/', StringComparison.Ordinal);
    }

    private static string FormatWorksheetCustomPropertyReference(WorksheetCustomPropertyReference customProperty) =>
        string.IsNullOrWhiteSpace(customProperty.Name)
            ? $"customPr #{customProperty.Ordinal}"
            : $"customPr '{customProperty.Name}'";

    private static void ThrowInvalidWorksheetCustomPropertyPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet custom-property package graph: {sample}{suffix}");
    }

    private static void AssertWorksheetScenarioPackageComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var worksheetPart = NormalizePackagePart(worksheetEntry.FullName);
            var worksheetXml = LoadPackageXml(worksheetEntry);
            foreach (var scenarios in worksheetXml.Root?.Elements(SpreadsheetNs + "scenarios") ?? [])
            {
                AddWorksheetScenariosIssues(worksheetPart, scenarios, issues);
            }
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetScenarioPackage(label, sourcePath, issues);
    }

    private static void AddWorksheetScenariosIssues(
        string worksheetPart,
        XElement scenarios,
        List<string> issues)
    {
        var scenarioElements = scenarios.Elements(SpreadsheetNs + "scenario").ToArray();
        if (scenarioElements.Length == 0)
        {
            issues.Add($"{worksheetPart} scenarios element has no scenario entries");
            return;
        }

        AddWorksheetScenariosIndexIssue(worksheetPart, "current", scenarios.Attribute("current")?.Value, scenarioElements.Length, issues);
        AddWorksheetScenariosIndexIssue(worksheetPart, "show", scenarios.Attribute("show")?.Value, scenarioElements.Length, issues);

        foreach (var scenario in scenarioElements.Select((element, index) => new WorksheetScenarioReference(index + 1, element)))
        {
            AddWorksheetScenarioIssues(worksheetPart, scenario, issues);
        }
    }

    private static void AddWorksheetScenariosIndexIssue(
        string worksheetPart,
        string attributeName,
        string? value,
        int scenarioCount,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!TryParseNonNegativePackageInt(value, out var index))
        {
            issues.Add($"{worksheetPart} scenarios has invalid {attributeName} index '{value}'");
            return;
        }

        if (index >= scenarioCount)
            issues.Add($"{worksheetPart} scenarios {attributeName} index {index} is outside {scenarioCount} scenario entries");
    }

    private static void AddWorksheetScenarioIssues(
        string worksheetPart,
        WorksheetScenarioReference scenarioReference,
        List<string> issues)
    {
        var scenario = scenarioReference.Element;
        var scenarioName = scenario.Attribute("name")?.Value;
        var description = string.IsNullOrWhiteSpace(scenarioName)
            ? $"scenario #{scenarioReference.Ordinal}"
            : $"scenario '{scenarioName}'";

        if (string.IsNullOrWhiteSpace(scenarioName))
            issues.Add($"{worksheetPart} {description} has no name");

        AddWorksheetScenarioBooleanIssue(worksheetPart, description, "hidden", scenario.Attribute("hidden")?.Value, issues);
        AddWorksheetScenarioBooleanIssue(worksheetPart, description, "locked", scenario.Attribute("locked")?.Value, issues);

        var inputCells = scenario.Elements(SpreadsheetNs + "inputCells").ToArray();
        if (inputCells.Length == 0)
        {
            issues.Add($"{worksheetPart} {description} has no inputCells entries");
        }

        var countText = scenario.Attribute("count")?.Value;
        if (!string.IsNullOrWhiteSpace(countText))
        {
            if (!TryParseNonNegativePackageInt(countText, out var declaredCount))
            {
                issues.Add($"{worksheetPart} {description} has invalid count '{countText}'");
            }
            else if (declaredCount != inputCells.Length)
            {
                issues.Add($"{worksheetPart} {description} count is {declaredCount}, but contains {inputCells.Length} inputCells entries");
            }
        }

        foreach (var inputCell in inputCells.Select((element, index) => new WorksheetScenarioInputCellReference(index + 1, element)))
        {
            AddWorksheetScenarioInputCellIssues(worksheetPart, description, inputCell, issues);
        }
    }

    private static void AddWorksheetScenarioBooleanIssue(
        string worksheetPart,
        string scenarioDescription,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || IsValidPackageBoolean(value))
            return;

        issues.Add($"{worksheetPart} {scenarioDescription} has invalid {attributeName} value '{value}'");
    }

    private static void AddWorksheetScenarioInputCellIssues(
        string worksheetPart,
        string scenarioDescription,
        WorksheetScenarioInputCellReference inputCellReference,
        List<string> issues)
    {
        var inputCell = inputCellReference.Element;
        var reference = inputCell.Attribute("r")?.Value;
        if (string.IsNullOrWhiteSpace(reference))
        {
            issues.Add($"{worksheetPart} {scenarioDescription} inputCells #{inputCellReference.Ordinal} has no r reference");
        }
        else if (!IsValidLocalWorksheetReference(reference))
        {
            issues.Add($"{worksheetPart} {scenarioDescription} inputCells #{inputCellReference.Ordinal} has invalid local r reference '{reference}'");
        }

        if (inputCell.Attribute("val") is null)
            issues.Add($"{worksheetPart} {scenarioDescription} inputCells #{inputCellReference.Ordinal} has no val attribute");
    }

    private static bool IsValidLocalWorksheetReference(string reference)
    {
        reference = reference.Trim();
        if (reference.Length == 0 ||
            reference.Contains('!', StringComparison.Ordinal) ||
            reference.Contains('[', StringComparison.Ordinal) ||
            reference.Contains(']', StringComparison.Ordinal))
        {
            return false;
        }

        var sheet = SheetId.New();
        if (CellAddress.TryParse(reference, sheet, out _))
            return true;

        var rangeParts = reference.Split(':', StringSplitOptions.TrimEntries);
        return rangeParts.Length == 2 &&
            CellAddress.TryParse(rangeParts[0], sheet, out _) &&
            CellAddress.TryParse(rangeParts[1], sheet, out _);
    }

    private static bool IsValidLocalCellReference(string reference)
    {
        reference = reference.Trim();
        if (reference.Length == 0 ||
            reference.Contains('!', StringComparison.Ordinal) ||
            reference.Contains('[', StringComparison.Ordinal) ||
            reference.Contains(']', StringComparison.Ordinal) ||
            reference.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return CellAddress.TryParse(reference, SheetId.New(), out _);
    }

    private static bool IsValidPackageBoolean(string value)
    {
        value = value.Trim();
        return value is "0" or "1" ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static void ThrowInvalidWorksheetScenarioPackage(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet scenario metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookFileVersionMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookFileVersionMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookFileVersionMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookFileVersionMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var fileVersionElements = root.Elements(SpreadsheetNs + "fileVersion").ToArray();
        if (fileVersionElements.Length > 1)
            issues.Add($"{WorkbookPart} has {fileVersionElements.Length} fileVersion elements; expected at most one");

        foreach (var fileVersion in fileVersionElements.Select((element, index) => new WorkbookFileVersionReference(index + 1, element)))
        {
            AddWorkbookFileVersionIssues(root, fileVersion, issues);
        }
    }

    private static void AddWorkbookFileVersionIssues(
        XElement workbookRoot,
        WorkbookFileVersionReference fileVersionReference,
        List<string> issues)
    {
        var fileVersion = fileVersionReference.Element;
        var description = $"fileVersion #{fileVersionReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            fileVersion,
            description,
            [
                "fileSharing",
                "workbookPr",
                "workbookProtection",
                "bookViews",
                "sheets",
                "functionGroups",
                "externalReferences",
                "definedNames",
                "calcPr",
                "oleSize",
                "customWorkbookViews",
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        AddOptionalWorkbookMetadataNonNegativeIntIssue(description, "lastEdited", fileVersion.Attribute("lastEdited")?.Value, issues);
        AddOptionalWorkbookMetadataNonNegativeIntIssue(description, "lowestEdited", fileVersion.Attribute("lowestEdited")?.Value, issues);
        AddOptionalWorkbookMetadataNonNegativeIntIssue(description, "rupBuild", fileVersion.Attribute("rupBuild")?.Value, issues);

        if (fileVersion.Elements().Any())
            issues.Add($"{WorkbookPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorkbookMetadataOrderingIssues(
        XElement workbookRoot,
        XElement metadataElement,
        string description,
        IReadOnlyCollection<string> laterWorkbookElements,
        List<string> issues)
    {
        var workbookChildren = workbookRoot.Elements().ToArray();
        var metadataIndex = Array.IndexOf(workbookChildren, metadataElement);
        if (metadataIndex < 0)
            return;

        foreach (var earlierLaterElement in workbookChildren
                     .Take(metadataIndex)
                     .Where(element =>
                         element.Name.Namespace == SpreadsheetNs &&
                         laterWorkbookElements.Contains(element.Name.LocalName)))
        {
            issues.Add($"{WorkbookPart} {description} appears after {earlierLaterElement.Name.LocalName}; expected schema order before that element");
        }
    }

    private static void AddOptionalWorkbookMetadataNonNegativeIntIssue(
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || TryParseNonNegativePackageInt(value, out _))
            return;

        issues.Add($"{WorkbookPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void AddOptionalWorkbookMetadataUnsignedIntIssue(
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return;
        }

        issues.Add($"{WorkbookPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void AddOptionalWorkbookMetadataIntIssue(
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return;
        }

        issues.Add($"{WorkbookPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void AddOptionalWorkbookMetadataNonNegativeDoubleIssue(
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue) &&
            !double.IsNaN(parsedValue) &&
            !double.IsInfinity(parsedValue) &&
            parsedValue >= 0)
        {
            return;
        }

        issues.Add($"{WorkbookPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void AddOptionalWorkbookMetadataBooleanIssue(
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || IsValidPackageBoolean(value))
            return;

        issues.Add($"{WorkbookPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void ThrowInvalidWorkbookFileVersionMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook fileVersion metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookFileSharingMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookFileSharingMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookFileSharingMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookFileSharingMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var fileSharingElements = root.Elements(SpreadsheetNs + "fileSharing").ToArray();
        if (fileSharingElements.Length > 1)
            issues.Add($"{WorkbookPart} has {fileSharingElements.Length} fileSharing elements; expected at most one");

        foreach (var fileSharing in fileSharingElements.Select((element, index) => new WorkbookFileSharingReference(index + 1, element)))
        {
            AddWorkbookFileSharingIssues(root, fileSharing, issues);
        }
    }

    private static void AddWorkbookFileSharingIssues(
        XElement workbookRoot,
        WorkbookFileSharingReference fileSharingReference,
        List<string> issues)
    {
        var fileSharing = fileSharingReference.Element;
        var description = $"fileSharing #{fileSharingReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            fileSharing,
            description,
            [
                "workbookPr",
                "workbookProtection",
                "bookViews",
                "sheets",
                "functionGroups",
                "externalReferences",
                "definedNames",
                "calcPr",
                "oleSize",
                "customWorkbookViews",
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        AddOptionalWorkbookMetadataBooleanIssue(description, "readOnlyRecommended", fileSharing.Attribute("readOnlyRecommended")?.Value, issues);
        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "spinCount", fileSharing.Attribute("spinCount")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "userName", fileSharing.Attribute("userName")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "reservationPassword", fileSharing.Attribute("reservationPassword")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "algorithmName", fileSharing.Attribute("algorithmName")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "hashValue", fileSharing.Attribute("hashValue")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "saltValue", fileSharing.Attribute("saltValue")?.Value, issues);

        if (fileSharing.Elements().Any())
            issues.Add($"{WorkbookPart} {description} has child elements; expected attributes only");
    }

    private static void AddOptionalWorkbookMetadataNonEmptyAttributeIssue(
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (value is null || !string.IsNullOrWhiteSpace(value))
            return;

        issues.Add($"{WorkbookPart} {description} has empty {attributeName} value");
    }

    private static void ThrowInvalidWorkbookFileSharingMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook fileSharing metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookPropertiesMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookPropertiesMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookPropertiesMetadata(label, sourcePath, issues);
    }

    private static void ThrowInvalidWorkbookPropertiesMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbookPr metadata: {sample}{suffix}");
    }

    private static void AddWorkbookPropertiesMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var workbookPropertiesElements = root.Elements(SpreadsheetNs + "workbookPr").ToArray();
        if (workbookPropertiesElements.Length > 1)
            issues.Add($"{WorkbookPart} has {workbookPropertiesElements.Length} workbookPr elements; expected at most one");

        foreach (var workbookProperties in workbookPropertiesElements.Select((element, index) => new WorkbookPropertiesReference(index + 1, element)))
        {
            AddWorkbookPropertiesIssues(root, workbookProperties, issues);
        }
    }

    private static void AddWorkbookPropertiesIssues(
        XElement workbookRoot,
        WorkbookPropertiesReference workbookPropertiesReference,
        List<string> issues)
    {
        var workbookProperties = workbookPropertiesReference.Element;
        var description = $"workbookPr #{workbookPropertiesReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            workbookProperties,
            description,
            [
                "workbookProtection",
                "bookViews",
                "sheets",
                "functionGroups",
                "externalReferences",
                "definedNames",
                "calcPr",
                "oleSize",
                "customWorkbookViews",
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        foreach (var attribute in workbookProperties.Attributes().Where(attribute => IsKnownWorkbookPropertiesBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorkbookMetadataBooleanIssue(description, attribute.Name.LocalName, attribute.Value, issues);
        }

        AddOptionalWorkbookMetadataNonNegativeIntIssue(description, "defaultThemeVersion", workbookProperties.Attribute("defaultThemeVersion")?.Value, issues);

        var showObjects = workbookProperties.Attribute("showObjects")?.Value;
        if (!string.IsNullOrWhiteSpace(showObjects) && !IsKnownWorkbookPropertiesShowObjectsValue(showObjects))
            issues.Add($"{WorkbookPart} {description} has invalid showObjects value '{showObjects}'");

        var updateLinks = workbookProperties.Attribute("updateLinks")?.Value;
        if (!string.IsNullOrWhiteSpace(updateLinks) && !IsKnownWorkbookPropertiesUpdateLinksValue(updateLinks))
            issues.Add($"{WorkbookPart} {description} has invalid updateLinks value '{updateLinks}'");

        if (workbookProperties.Elements().Any())
            issues.Add($"{WorkbookPart} {description} has child elements; expected attributes only");
    }

    private static bool IsKnownWorkbookPropertiesBooleanAttribute(string name) =>
        name is "date1904" or
            "showBorderUnselectedTables" or
            "filterPrivacy" or
            "promptedSolutions" or
            "showInkAnnotation" or
            "backupFile" or
            "saveExternalLinkValues" or
            "hidePivotFieldList" or
            "showPivotChartFilter" or
            "allowRefreshQuery" or
            "publishItems" or
            "checkCompatibility" or
            "autoCompressPictures" or
            "refreshAllConnections";

    private static bool IsKnownWorkbookPropertiesShowObjectsValue(string value) =>
        value is "all" or "placeholders" or "none";

    private static bool IsKnownWorkbookPropertiesUpdateLinksValue(string value) =>
        value is "userSet" or "never" or "always";

    private static void AssertWorkbookProtectionMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookProtectionMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookProtectionMetadata(label, sourcePath, issues);
    }

    private static void ThrowInvalidWorkbookProtectionMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbookProtection metadata: {sample}{suffix}");
    }

    private static void AddWorkbookProtectionMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var workbookProtectionElements = root.Elements(SpreadsheetNs + "workbookProtection").ToArray();
        if (workbookProtectionElements.Length > 1)
            issues.Add($"{WorkbookPart} has {workbookProtectionElements.Length} workbookProtection elements; expected at most one");

        foreach (var workbookProtection in workbookProtectionElements.Select((element, index) => new WorkbookProtectionReference(index + 1, element)))
        {
            AddWorkbookProtectionIssues(root, workbookProtection, issues);
        }
    }

    private static void AddWorkbookProtectionIssues(
        XElement workbookRoot,
        WorkbookProtectionReference workbookProtectionReference,
        List<string> issues)
    {
        var workbookProtection = workbookProtectionReference.Element;
        var description = $"workbookProtection #{workbookProtectionReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            workbookProtection,
            description,
            [
                "bookViews",
                "sheets",
                "functionGroups",
                "externalReferences",
                "definedNames",
                "calcPr",
                "oleSize",
                "customWorkbookViews",
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        foreach (var attribute in workbookProtection.Attributes().Where(attribute => IsKnownWorkbookProtectionBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorkbookMetadataBooleanIssue(description, attribute.Name.LocalName, attribute.Value, issues);
        }

        foreach (var attribute in workbookProtection.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && IsKnownWorkbookProtectionAttribute(attribute.Name.LocalName)))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "workbookSpinCount", workbookProtection.Attribute("workbookSpinCount")?.Value, issues);
        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "revisionsSpinCount", workbookProtection.Attribute("revisionsSpinCount")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "workbookPassword", workbookProtection.Attribute("workbookPassword")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "revisionsPassword", workbookProtection.Attribute("revisionsPassword")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "workbookAlgorithmName", workbookProtection.Attribute("workbookAlgorithmName")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "workbookHashValue", workbookProtection.Attribute("workbookHashValue")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "workbookSaltValue", workbookProtection.Attribute("workbookSaltValue")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "revisionsAlgorithmName", workbookProtection.Attribute("revisionsAlgorithmName")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "revisionsHashValue", workbookProtection.Attribute("revisionsHashValue")?.Value, issues);
        AddOptionalWorkbookMetadataNonEmptyAttributeIssue(description, "revisionsSaltValue", workbookProtection.Attribute("revisionsSaltValue")?.Value, issues);

        if (workbookProtection.Elements().Any())
            issues.Add($"{WorkbookPart} {description} has child elements; expected attributes only");
    }

    private static bool IsKnownWorkbookProtectionAttribute(string name) =>
        name is "workbookPassword" or
            "revisionsPassword" or
            "lockStructure" or
            "lockWindows" or
            "lockRevision" or
            "revisionsAlgorithmName" or
            "revisionsHashValue" or
            "revisionsSaltValue" or
            "revisionsSpinCount" or
            "workbookAlgorithmName" or
            "workbookHashValue" or
            "workbookSaltValue" or
            "workbookSpinCount";

    private static bool IsKnownWorkbookProtectionBooleanAttribute(string name) =>
        name is "lockStructure" or "lockWindows" or "lockRevision";

    private static void AssertWorkbookViewMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookViewMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookViewMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookViewMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var bookViewElements = root.Elements(SpreadsheetNs + "bookViews").ToArray();
        if (bookViewElements.Length > 1)
            issues.Add($"{WorkbookPart} has {bookViewElements.Length} bookViews elements; expected at most one");

        foreach (var bookViews in bookViewElements.Select((element, index) => new WorkbookBookViewsReference(index + 1, element)))
        {
            AddWorkbookBookViewsIssues(root, bookViews, issues);
        }

        var customWorkbookViewElements = root.Elements(SpreadsheetNs + "customWorkbookViews").ToArray();
        if (customWorkbookViewElements.Length > 1)
            issues.Add($"{WorkbookPart} has {customWorkbookViewElements.Length} customWorkbookViews elements; expected at most one");

        foreach (var customWorkbookViews in customWorkbookViewElements.Select((element, index) => new WorkbookCustomWorkbookViewsReference(index + 1, element)))
        {
            AddWorkbookCustomWorkbookViewsIssues(root, customWorkbookViews, issues);
        }
    }

    private static void AddWorkbookBookViewsIssues(
        XElement workbookRoot,
        WorkbookBookViewsReference bookViewsReference,
        List<string> issues)
    {
        var bookViews = bookViewsReference.Element;
        var description = $"bookViews #{bookViewsReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            bookViews,
            description,
            [
                "sheets",
                "functionGroups",
                "externalReferences",
                "definedNames",
                "calcPr",
                "oleSize",
                "customWorkbookViews",
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        foreach (var attribute in bookViews.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
        {
            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        foreach (var unexpectedChild in bookViews.Elements().Where(element => element.Name != SpreadsheetNs + "workbookView"))
        {
            issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected workbookView entries only");
        }

        var workbookViewElements = bookViews.Elements(SpreadsheetNs + "workbookView").ToArray();
        if (workbookViewElements.Length == 0)
            issues.Add($"{WorkbookPart} {description} has no workbookView entries");

        foreach (var workbookView in workbookViewElements.Select((element, index) => new WorkbookViewReference(index + 1, element)))
        {
            AddWorkbookViewIssues(description, workbookView, issues);
        }
    }

    private static void AddWorkbookViewIssues(
        string bookViewsDescription,
        WorkbookViewReference workbookViewReference,
        List<string> issues)
    {
        var workbookView = workbookViewReference.Element;
        var description = $"{bookViewsDescription} workbookView #{workbookViewReference.Ordinal}";

        foreach (var attribute in workbookView.Attributes().Where(attribute => IsKnownWorkbookViewBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorkbookMetadataBooleanIssue(description, attribute.Name.LocalName, attribute.Value, issues);
        }

        foreach (var attribute in workbookView.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                IsKnownNamespacedWorkbookViewAttribute(attribute.Name) ||
                (attribute.Name.NamespaceName.Length == 0 && IsKnownWorkbookViewAttribute(attribute.Name.LocalName)))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        var visibility = workbookView.Attribute("visibility")?.Value;
        if (!string.IsNullOrWhiteSpace(visibility) && !IsKnownWorkbookViewVisibility(visibility))
            issues.Add($"{WorkbookPart} {description} has invalid visibility value '{visibility}'");

        AddOptionalWorkbookMetadataIntIssue(description, "xWindow", workbookView.Attribute("xWindow")?.Value, issues);
        AddOptionalWorkbookMetadataIntIssue(description, "yWindow", workbookView.Attribute("yWindow")?.Value, issues);

        foreach (var attributeName in new[]
                 {
                     "windowWidth",
                     "windowHeight",
                     "tabRatio",
                     "firstSheet",
                     "activeTab"
                 })
        {
            AddOptionalWorkbookMetadataUnsignedIntIssue(description, attributeName, workbookView.Attribute(attributeName)?.Value, issues);
        }

        foreach (var unexpectedChild in workbookView.Elements().Where(element => element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected extLst only");
        }
    }

    private static bool IsKnownWorkbookViewAttribute(string name) =>
        name is "visibility" or
            "minimized" or
            "showHorizontalScroll" or
            "showVerticalScroll" or
            "showSheetTabs" or
            "xWindow" or
            "yWindow" or
            "windowWidth" or
            "windowHeight" or
            "tabRatio" or
            "firstSheet" or
            "activeTab" or
            "autoFilterDateGrouping";

    private static bool IsKnownWorkbookViewBooleanAttribute(string name) =>
        name is "minimized" or
            "showHorizontalScroll" or
            "showVerticalScroll" or
            "showSheetTabs" or
            "autoFilterDateGrouping";

    private static bool IsKnownNamespacedWorkbookViewAttribute(XName name) =>
        name == SpreadsheetRevision2Ns + "uid";

    private static bool IsKnownWorkbookViewVisibility(string value) =>
        value.Trim() is "visible" or "hidden" or "veryHidden";

    private static void AddWorkbookCustomWorkbookViewsIssues(
        XElement workbookRoot,
        WorkbookCustomWorkbookViewsReference customWorkbookViewsReference,
        List<string> issues)
    {
        var customWorkbookViews = customWorkbookViewsReference.Element;
        var description = $"customWorkbookViews #{customWorkbookViewsReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            customWorkbookViews,
            description,
            [
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        foreach (var attribute in customWorkbookViews.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
        {
            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        foreach (var unexpectedChild in customWorkbookViews.Elements().Where(element => element.Name != SpreadsheetNs + "customWorkbookView"))
        {
            issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected customWorkbookView entries only");
        }

        var customWorkbookViewElements = customWorkbookViews.Elements(SpreadsheetNs + "customWorkbookView").ToArray();
        if (customWorkbookViewElements.Length == 0)
            issues.Add($"{WorkbookPart} {description} has no customWorkbookView entries");

        foreach (var customWorkbookView in customWorkbookViewElements.Select((element, index) => new WorkbookCustomWorkbookViewReference(index + 1, element)))
        {
            AddWorkbookCustomWorkbookViewIssues(description, customWorkbookView, issues);
        }
    }

    private static void AddWorkbookCustomWorkbookViewIssues(
        string customWorkbookViewsDescription,
        WorkbookCustomWorkbookViewReference customWorkbookViewReference,
        List<string> issues)
    {
        var customWorkbookView = customWorkbookViewReference.Element;
        var description = $"{customWorkbookViewsDescription} customWorkbookView #{customWorkbookViewReference.Ordinal}";

        if (string.IsNullOrWhiteSpace(customWorkbookView.Attribute("name")?.Value))
            issues.Add($"{WorkbookPart} {description} has no name");

        var guid = customWorkbookView.Attribute("guid")?.Value;
        if (string.IsNullOrWhiteSpace(guid))
            issues.Add($"{WorkbookPart} {description} has no guid");
        else if (!Guid.TryParse(guid, out _))
            issues.Add($"{WorkbookPart} {description} has invalid guid value '{guid}'");

        foreach (var attribute in customWorkbookView.Attributes().Where(attribute => IsKnownCustomWorkbookViewBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorkbookMetadataBooleanIssue(description, attribute.Name.LocalName, attribute.Value, issues);
        }

        foreach (var attribute in customWorkbookView.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && IsKnownCustomWorkbookViewAttribute(attribute.Name.LocalName)))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        foreach (var attributeName in new[]
                 {
                     "mergeInterval",
                     "activeSheetId",
                     "windowWidth",
                     "windowHeight",
                     "tabRatio"
                 })
        {
            AddOptionalWorkbookMetadataUnsignedIntIssue(description, attributeName, customWorkbookView.Attribute(attributeName)?.Value, issues);
        }

        AddOptionalWorkbookMetadataIntIssue(description, "xWindow", customWorkbookView.Attribute("xWindow")?.Value, issues);
        AddOptionalWorkbookMetadataIntIssue(description, "yWindow", customWorkbookView.Attribute("yWindow")?.Value, issues);

        var showObjects = customWorkbookView.Attribute("showObjects")?.Value;
        if (!string.IsNullOrWhiteSpace(showObjects) && !IsKnownWorkbookPropertiesShowObjectsValue(showObjects))
            issues.Add($"{WorkbookPart} {description} has invalid showObjects value '{showObjects}'");

        var showComments = customWorkbookView.Attribute("showComments")?.Value;
        if (!string.IsNullOrWhiteSpace(showComments) && !IsKnownCustomWorkbookViewShowCommentsValue(showComments))
            issues.Add($"{WorkbookPart} {description} has invalid showComments value '{showComments}'");

        foreach (var unexpectedChild in customWorkbookView.Elements().Where(element => element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected extLst only");
        }
    }

    private static bool IsKnownCustomWorkbookViewAttribute(string name) =>
        name is "name" or
            "guid" or
            "autoUpdate" or
            "mergeInterval" or
            "changesSavedWin" or
            "onlySync" or
            "personalView" or
            "includePrintSettings" or
            "includeHiddenRowCol" or
            "maximized" or
            "minimized" or
            "showHorizontalScroll" or
            "showVerticalScroll" or
            "showSheetTabs" or
            "xWindow" or
            "yWindow" or
            "windowWidth" or
            "windowHeight" or
            "tabRatio" or
            "activeSheetId" or
            "showFormulaBar" or
            "showStatusbar" or
            "showComments" or
            "showObjects";

    private static bool IsKnownCustomWorkbookViewBooleanAttribute(string name) =>
        name is "autoUpdate" or
            "changesSavedWin" or
            "onlySync" or
            "personalView" or
            "includePrintSettings" or
            "includeHiddenRowCol" or
            "maximized" or
            "minimized" or
            "showHorizontalScroll" or
            "showVerticalScroll" or
            "showSheetTabs" or
            "showFormulaBar" or
            "showStatusbar";

    private static bool IsKnownCustomWorkbookViewShowCommentsValue(string value) =>
        value.Trim() is "commNone" or "commIndicator" or "commIndAndComment";

    private static void ThrowInvalidWorkbookViewMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook view metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookFunctionGroupsMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookFunctionGroupsMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookFunctionGroupsMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookFunctionGroupsMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var functionGroupsElements = root.Elements(SpreadsheetNs + "functionGroups").ToArray();
        if (functionGroupsElements.Length > 1)
            issues.Add($"{WorkbookPart} has {functionGroupsElements.Length} functionGroups elements; expected at most one");

        foreach (var functionGroups in functionGroupsElements.Select((element, index) => new WorkbookFunctionGroupsReference(index + 1, element)))
        {
            AddWorkbookFunctionGroupsIssues(root, functionGroups, issues);
        }
    }

    private static void AddWorkbookFunctionGroupsIssues(
        XElement workbookRoot,
        WorkbookFunctionGroupsReference functionGroupsReference,
        List<string> issues)
    {
        var functionGroups = functionGroupsReference.Element;
        var description = $"functionGroups #{functionGroupsReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            functionGroups,
            description,
            [
                "externalReferences",
                "definedNames",
                "calcPr",
                "oleSize",
                "customWorkbookViews",
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "builtInGroupCount", functionGroups.Attribute("builtInGroupCount")?.Value, issues);

        foreach (var unexpectedChild in functionGroups.Elements().Where(element => element.Name != SpreadsheetNs + "functionGroup"))
        {
            issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected functionGroup entries only");
        }

        foreach (var functionGroup in functionGroups
                     .Elements(SpreadsheetNs + "functionGroup")
                     .Select((element, index) => new WorkbookFunctionGroupReference(index + 1, element)))
        {
            AddWorkbookFunctionGroupIssues(description, functionGroup, issues);
        }
    }

    private static void AddWorkbookFunctionGroupIssues(
        string functionGroupsDescription,
        WorkbookFunctionGroupReference functionGroupReference,
        List<string> issues)
    {
        var functionGroup = functionGroupReference.Element;
        var description = $"{functionGroupsDescription} functionGroup #{functionGroupReference.Ordinal}";
        if (string.IsNullOrWhiteSpace(functionGroup.Attribute("name")?.Value))
            issues.Add($"{WorkbookPart} {description} has no name");

        if (functionGroup.Elements().Any())
            issues.Add($"{WorkbookPart} {description} has child elements; expected attributes only");
    }

    private static void ThrowInvalidWorkbookFunctionGroupsMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook functionGroups metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookDefinedNamesMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookDefinedNamesMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookDefinedNamesMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookDefinedNamesMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var definedNamesElements = root.Elements(SpreadsheetNs + "definedNames").ToArray();
        if (definedNamesElements.Length > 1)
            issues.Add($"{WorkbookPart} has {definedNamesElements.Length} definedNames elements; expected at most one");

        foreach (var definedNames in definedNamesElements.Select((element, index) => new WorkbookDefinedNamesReference(index + 1, element)))
        {
            AddWorkbookDefinedNamesIssues(root, definedNames, issues);
        }
    }

    private static void AddWorkbookDefinedNamesIssues(
        XElement workbookRoot,
        WorkbookDefinedNamesReference definedNamesReference,
        List<string> issues)
    {
        var definedNames = definedNamesReference.Element;
        var description = $"definedNames #{definedNamesReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            definedNames,
            description,
            [
                "calcPr",
                "oleSize",
                "customWorkbookViews",
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        foreach (var attribute in definedNames.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
        {
            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        foreach (var unexpectedChild in definedNames.Elements().Where(element => element.Name != SpreadsheetNs + "definedName"))
        {
            issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected definedName entries only");
        }

        var definedNameElements = definedNames.Elements(SpreadsheetNs + "definedName").ToArray();
        foreach (var definedName in definedNameElements.Select((element, index) => new WorkbookDefinedNameReference(index + 1, element)))
        {
            AddWorkbookDefinedNameIssues(description, definedName, issues);
        }
    }

    private static void AddWorkbookDefinedNameIssues(
        string definedNamesDescription,
        WorkbookDefinedNameReference definedNameReference,
        List<string> issues)
    {
        var definedName = definedNameReference.Element;
        var description = $"{definedNamesDescription} definedName #{definedNameReference.Ordinal}";

        if (string.IsNullOrWhiteSpace(definedName.Attribute("name")?.Value))
            issues.Add($"{WorkbookPart} {description} has no name");

        foreach (var attribute in definedName.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && IsKnownWorkbookDefinedNameAttribute(attribute.Name.LocalName)))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        foreach (var attribute in definedName.Attributes().Where(attribute => IsKnownWorkbookDefinedNameBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorkbookMetadataBooleanIssue(description, attribute.Name.LocalName, attribute.Value, issues);
        }

        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "localSheetId", definedName.Attribute("localSheetId")?.Value, issues);
        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "functionGroupId", definedName.Attribute("functionGroupId")?.Value, issues);

        if (definedName.Elements().Any())
            issues.Add($"{WorkbookPart} {description} has child elements; expected formula text only");
    }

    private static bool IsKnownWorkbookDefinedNameAttribute(string name) =>
        name is "name" or
            "comment" or
            "customMenu" or
            "description" or
            "help" or
            "statusBar" or
            "localSheetId" or
            "hidden" or
            "function" or
            "vbProcedure" or
            "xlm" or
            "functionGroupId" or
            "shortcutKey" or
            "publishToServer" or
            "workbookParameter";

    private static bool IsKnownWorkbookDefinedNameBooleanAttribute(string name) =>
        name is "hidden" or
            "function" or
            "vbProcedure" or
            "xlm" or
            "publishToServer" or
            "workbookParameter";

    private static void ThrowInvalidWorkbookDefinedNamesMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook definedNames metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookCalculationPropertiesMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookCalculationPropertiesMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookCalculationPropertiesMetadata(label, sourcePath, issues);
    }

    private static void ThrowInvalidWorkbookCalculationPropertiesMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook calcPr metadata: {sample}{suffix}");
    }

    private static void AddWorkbookCalculationPropertiesMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var calculationPropertiesElements = root.Elements(SpreadsheetNs + "calcPr").ToArray();
        if (calculationPropertiesElements.Length > 1)
            issues.Add($"{WorkbookPart} has {calculationPropertiesElements.Length} calcPr elements; expected at most one");

        foreach (var calculationProperties in calculationPropertiesElements.Select((element, index) => new WorkbookCalculationPropertiesReference(index + 1, element)))
        {
            AddWorkbookCalculationPropertyIssues(root, calculationProperties, issues);
        }
    }

    private static void AddWorkbookCalculationPropertyIssues(
        XElement workbookRoot,
        WorkbookCalculationPropertiesReference calculationPropertiesReference,
        List<string> issues)
    {
        var calculationProperties = calculationPropertiesReference.Element;
        var description = $"calcPr #{calculationPropertiesReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            calculationProperties,
            description,
            [
                "oleSize",
                "customWorkbookViews",
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        foreach (var attribute in calculationProperties.Attributes().Where(attribute => IsKnownWorkbookCalculationBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorkbookMetadataBooleanIssue(description, attribute.Name.LocalName, attribute.Value, issues);
        }

        foreach (var attribute in calculationProperties.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && IsKnownWorkbookCalculationAttribute(attribute.Name.LocalName)))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "calcId", calculationProperties.Attribute("calcId")?.Value, issues);
        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "iterateCount", calculationProperties.Attribute("iterateCount")?.Value, issues);
        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "concurrentManualCount", calculationProperties.Attribute("concurrentManualCount")?.Value, issues);
        AddOptionalWorkbookMetadataNonNegativeDoubleIssue(description, "iterateDelta", calculationProperties.Attribute("iterateDelta")?.Value, issues);

        var calcMode = calculationProperties.Attribute("calcMode")?.Value;
        if (!string.IsNullOrWhiteSpace(calcMode) && !IsKnownWorkbookCalculationModeValue(calcMode))
            issues.Add($"{WorkbookPart} {description} has invalid calcMode value '{calcMode}'");

        var refMode = calculationProperties.Attribute("refMode")?.Value;
        if (!string.IsNullOrWhiteSpace(refMode) && !IsKnownWorkbookCalculationReferenceModeValue(refMode))
            issues.Add($"{WorkbookPart} {description} has invalid refMode value '{refMode}'");

        if (calculationProperties.Elements().Any())
            issues.Add($"{WorkbookPart} {description} has child elements; expected attributes only");
    }

    private static bool IsKnownWorkbookCalculationAttribute(string name) =>
        name is "calcId" or
            "calcMode" or
            "fullCalcOnLoad" or
            "refMode" or
            "iterate" or
            "iterateCount" or
            "iterateDelta" or
            "fullPrecision" or
            "calcCompleted" or
            "calcOnSave" or
            "concurrentCalc" or
            "concurrentManualCount" or
            "forceFullCalc";

    private static bool IsKnownWorkbookCalculationBooleanAttribute(string name) =>
        name is "fullCalcOnLoad" or
            "iterate" or
            "fullPrecision" or
            "calcCompleted" or
            "calcOnSave" or
            "concurrentCalc" or
            "forceFullCalc";

    private static bool IsKnownWorkbookCalculationModeValue(string value) =>
        value is "manual" or "auto" or "autoNoTable";

    private static bool IsKnownWorkbookCalculationReferenceModeValue(string value) =>
        value is "A1" or "R1C1";

    private static void AssertWorkbookOleSizeMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookOleSizeMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookOleSizeMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookOleSizeMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var oleSizeElements = root.Elements(SpreadsheetNs + "oleSize").ToArray();
        if (oleSizeElements.Length > 1)
            issues.Add($"{WorkbookPart} has {oleSizeElements.Length} oleSize elements; expected at most one");

        foreach (var oleSize in oleSizeElements.Select((element, index) => new WorkbookOleSizeReference(index + 1, element)))
        {
            AddWorkbookOleSizeIssues(root, oleSize, issues);
        }
    }

    private static void AddWorkbookOleSizeIssues(
        XElement workbookRoot,
        WorkbookOleSizeReference oleSizeReference,
        List<string> issues)
    {
        var oleSize = oleSizeReference.Element;
        var description = $"oleSize #{oleSizeReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            oleSize,
            description,
            [
                "customWorkbookViews",
                "pivotCaches",
                "smartTagPr",
                "smartTagTypes",
                "webPublishing",
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        foreach (var attribute in oleSize.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "ref"))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        var reference = oleSize.Attribute("ref")?.Value;
        if (string.IsNullOrWhiteSpace(reference))
            issues.Add($"{WorkbookPart} {description} has no ref");
        else if (!IsValidLocalWorksheetReference(reference))
            issues.Add($"{WorkbookPart} {description} has invalid local ref reference '{reference}'");

        if (oleSize.Nodes().Any())
            issues.Add($"{WorkbookPart} {description} has child content; expected attributes only");
    }

    private static void ThrowInvalidWorkbookOleSizeMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook oleSize metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookWebPublishingMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookWebPublishingMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookWebPublishingMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookWebPublishingMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var webPublishingElements = root.Elements(SpreadsheetNs + "webPublishing").ToArray();
        if (webPublishingElements.Length > 1)
            issues.Add($"{WorkbookPart} has {webPublishingElements.Length} webPublishing elements; expected at most one");

        foreach (var webPublishing in webPublishingElements.Select((element, index) => new WorkbookWebPublishingReference(index + 1, element)))
        {
            AddWorkbookWebPublishingIssues(root, webPublishing, issues);
        }
    }

    private static void AddWorkbookWebPublishingIssues(
        XElement workbookRoot,
        WorkbookWebPublishingReference webPublishingReference,
        List<string> issues)
    {
        var webPublishing = webPublishingReference.Element;
        var description = $"webPublishing #{webPublishingReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            webPublishing,
            description,
            [
                "fileRecoveryPr",
                "webPublishObjects",
                "extLst"
            ],
            issues);

        foreach (var attribute in webPublishing.Attributes().Where(attribute => IsKnownWorkbookWebPublishingBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorkbookMetadataBooleanIssue(description, attribute.Name.LocalName, attribute.Value, issues);
        }

        foreach (var attribute in webPublishing.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && IsKnownWorkbookWebPublishingAttribute(attribute.Name.LocalName)))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        var targetScreenSize = webPublishing.Attribute("targetScreenSize")?.Value;
        if (!string.IsNullOrWhiteSpace(targetScreenSize) && !IsKnownWorkbookTargetScreenSizeValue(targetScreenSize))
            issues.Add($"{WorkbookPart} {description} has invalid targetScreenSize value '{targetScreenSize}'");

        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "dpi", webPublishing.Attribute("dpi")?.Value, issues);
        AddOptionalWorkbookMetadataUnsignedIntIssue(description, "codePage", webPublishing.Attribute("codePage")?.Value, issues);

        if (webPublishing.Attribute("characterSet") is { } characterSet &&
            string.IsNullOrWhiteSpace(characterSet.Value))
        {
            issues.Add($"{WorkbookPart} {description} has empty characterSet value");
        }

        if (webPublishing.Nodes().Any())
            issues.Add($"{WorkbookPart} {description} has child content; expected attributes only");
    }

    private static bool IsKnownWorkbookWebPublishingAttribute(string name) =>
        name is "css" or
            "thicket" or
            "longFileNames" or
            "vml" or
            "allowPng" or
            "targetScreenSize" or
            "dpi" or
            "codePage" or
            "characterSet";

    private static bool IsKnownWorkbookWebPublishingBooleanAttribute(string name) =>
        name is "css" or
            "thicket" or
            "longFileNames" or
            "vml" or
            "allowPng";

    private static bool IsKnownWorkbookTargetScreenSizeValue(string value) =>
        value is "544x376" or
            "640x480" or
            "720x512" or
            "800x600" or
            "1024x768" or
            "1152x882" or
            "1152x900" or
            "1280x1024" or
            "1600x1200" or
            "1800x1440" or
            "1920x1200";

    private static void ThrowInvalidWorkbookWebPublishingMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook webPublishing metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookFileRecoveryMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookFileRecoveryMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookFileRecoveryMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookFileRecoveryMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        foreach (var fileRecovery in root.Elements(SpreadsheetNs + "fileRecoveryPr").Select((element, index) => new WorkbookFileRecoveryReference(index + 1, element)))
        {
            AddWorkbookFileRecoveryIssues(root, fileRecovery, issues);
        }
    }

    private static void AddWorkbookFileRecoveryIssues(
        XElement workbookRoot,
        WorkbookFileRecoveryReference fileRecoveryReference,
        List<string> issues)
    {
        var fileRecovery = fileRecoveryReference.Element;
        var description = $"fileRecoveryPr #{fileRecoveryReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            fileRecovery,
            description,
            [
                "webPublishObjects",
                "extLst"
            ],
            issues);

        foreach (var attribute in fileRecovery.Attributes().Where(attribute => IsKnownWorkbookFileRecoveryBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorkbookMetadataBooleanIssue(description, attribute.Name.LocalName, attribute.Value, issues);
        }

        if (fileRecovery.Elements().Any())
            issues.Add($"{WorkbookPart} {description} has child elements; expected attributes only");
    }

    private static bool IsKnownWorkbookFileRecoveryBooleanAttribute(string name) =>
        name is "autoRecover" or "crashSave" or "dataExtractLoad" or "repairLoad";

    private static void ThrowInvalidWorkbookFileRecoveryMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook fileRecoveryPr metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookWebPublishObjectsMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookWebPublishObjectsMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookWebPublishObjectsMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookWebPublishObjectsMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var webPublishObjectsElements = root.Elements(SpreadsheetNs + "webPublishObjects").ToArray();
        if (webPublishObjectsElements.Length > 1)
            issues.Add($"{WorkbookPart} has {webPublishObjectsElements.Length} webPublishObjects elements; expected at most one");

        foreach (var webPublishObjects in webPublishObjectsElements.Select((element, index) => new WorkbookWebPublishObjectsReference(index + 1, element)))
        {
            AddWorkbookWebPublishObjectsIssues(root, webPublishObjects, issues);
        }
    }

    private static void AddWorkbookWebPublishObjectsIssues(
        XElement workbookRoot,
        WorkbookWebPublishObjectsReference webPublishObjectsReference,
        List<string> issues)
    {
        var webPublishObjects = webPublishObjectsReference.Element;
        var description = $"webPublishObjects #{webPublishObjectsReference.Ordinal}";
        AddWorkbookMetadataOrderingIssues(
            workbookRoot,
            webPublishObjects,
            description,
            [
                "extLst"
            ],
            issues);

        foreach (var attribute in webPublishObjects.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "count"))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        var webPublishObjectElements = webPublishObjects.Elements(SpreadsheetNs + "webPublishObject").ToArray();
        var count = webPublishObjects.Attribute("count")?.Value;
        if (string.IsNullOrWhiteSpace(count))
        {
            issues.Add($"{WorkbookPart} {description} has no count");
        }
        else
        {
            AddOptionalWorkbookMetadataUnsignedIntIssue(description, "count", count, issues);
            if (uint.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var declaredCount) &&
                declaredCount != webPublishObjectElements.Length)
            {
                issues.Add($"{WorkbookPart} {description} count {declaredCount} does not match webPublishObject child count {webPublishObjectElements.Length}");
            }
        }

        foreach (var unexpectedChild in webPublishObjects.Elements().Where(element => element.Name != SpreadsheetNs + "webPublishObject"))
        {
            issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected webPublishObject entries only");
        }

        if (webPublishObjectElements.Length == 0)
            issues.Add($"{WorkbookPart} {description} has no webPublishObject entries");

        foreach (var webPublishObject in webPublishObjectElements.Select((element, index) => new WorkbookWebPublishObjectReference(index + 1, element)))
        {
            AddWorkbookWebPublishObjectIssues(description, webPublishObject, issues);
        }
    }

    private static void AddWorkbookWebPublishObjectIssues(
        string webPublishObjectsDescription,
        WorkbookWebPublishObjectReference webPublishObjectReference,
        List<string> issues)
    {
        var webPublishObject = webPublishObjectReference.Element;
        var description = $"{webPublishObjectsDescription} webPublishObject #{webPublishObjectReference.Ordinal}";

        foreach (var attribute in webPublishObject.Attributes().Where(attribute => attribute.Name.LocalName == "autoRepublish"))
        {
            AddOptionalWorkbookMetadataBooleanIssue(description, attribute.Name.LocalName, attribute.Value, issues);
        }

        foreach (var attribute in webPublishObject.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && IsKnownWorkbookWebPublishObjectAttribute(attribute.Name.LocalName)))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        if (string.IsNullOrWhiteSpace(webPublishObject.Attribute("id")?.Value))
            issues.Add($"{WorkbookPart} {description} has no id");
        else
            AddOptionalWorkbookMetadataUnsignedIntIssue(description, "id", webPublishObject.Attribute("id")?.Value, issues);

        foreach (var attributeName in new[] { "divId", "sourceObject", "destinationFile" })
        {
            if (string.IsNullOrWhiteSpace(webPublishObject.Attribute(attributeName)?.Value))
                issues.Add($"{WorkbookPart} {description} has no {attributeName}");
        }

        if (webPublishObject.Attribute("title") is { } title &&
            string.IsNullOrWhiteSpace(title.Value))
        {
            issues.Add($"{WorkbookPart} {description} has empty title value");
        }

        if (webPublishObject.Nodes().Any())
            issues.Add($"{WorkbookPart} {description} has child content; expected attributes only");
    }

    private static bool IsKnownWorkbookWebPublishObjectAttribute(string name) =>
        name is "id" or
            "divId" or
            "sourceObject" or
            "destinationFile" or
            "title" or
            "autoRepublish";

    private static void ThrowInvalidWorkbookWebPublishObjectsMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook webPublishObjects metadata: {sample}{suffix}");
    }

    private static void AssertWorkbookExtensionListMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookExtensionListMetadataIssues(LoadPackageXml(workbookEntry), issues);

        if (issues.Count == 0)
            return;

        ThrowInvalidWorkbookExtensionListMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookExtensionListMetadataIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var extensionLists = root.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{WorkbookPart} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => new WorkbookExtensionListReference(index + 1, element)))
        {
            AddWorkbookExtensionListIssues(root, extensionList, issues);
        }
    }

    private static void AddWorkbookExtensionListIssues(
        XElement workbookRoot,
        WorkbookExtensionListReference extensionListReference,
        List<string> issues)
    {
        var extensionList = extensionListReference.Element;
        var description = $"extLst #{extensionListReference.Ordinal}";
        AddWorkbookExtensionListOrderingIssues(workbookRoot, extensionList, description, issues);

        if (extensionList.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            issues.Add($"{WorkbookPart} {description} has attributes; expected extension entries only");

        foreach (var unexpectedChild in extensionList.Elements().Where(element => element.Name != SpreadsheetNs + "ext"))
        {
            issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected ext entries only");
        }

        var extensions = extensionList.Elements(SpreadsheetNs + "ext").ToArray();
        if (extensions.Length == 0)
            issues.Add($"{WorkbookPart} {description} has no ext entries");

        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in extensions.Select((element, index) => new WorkbookExtensionReference(index + 1, element)))
        {
            AddWorkbookExtensionIssues(description, extension, seenUris, issues);
        }
    }

    private static void AddWorkbookExtensionListOrderingIssues(
        XElement workbookRoot,
        XElement extensionList,
        string description,
        List<string> issues)
    {
        string[] earlierWorkbookElements =
        [
            "fileVersion",
            "fileSharing",
            "workbookPr",
            "workbookProtection",
            "bookViews",
            "sheets",
            "functionGroups",
            "externalReferences",
            "definedNames",
            "calcPr",
            "oleSize",
            "customWorkbookViews",
            "pivotCaches",
            "smartTagPr",
            "smartTagTypes",
            "webPublishing",
            "fileRecoveryPr",
            "webPublishObjects"
        ];

        var workbookChildren = workbookRoot.Elements().ToArray();
        var extensionListIndex = Array.IndexOf(workbookChildren, extensionList);
        if (extensionListIndex < 0)
            return;

        foreach (var laterEarlierElement in workbookChildren
                     .Skip(extensionListIndex + 1)
                     .Where(element =>
                         element.Name.Namespace == SpreadsheetNs &&
                         earlierWorkbookElements.Contains(element.Name.LocalName, StringComparer.Ordinal)))
        {
            issues.Add($"{WorkbookPart} {description} appears before {laterEarlierElement.Name.LocalName}; expected schema order after that element");
        }
    }

    private static void AddWorkbookExtensionIssues(
        string extensionListDescription,
        WorkbookExtensionReference extensionReference,
        HashSet<string> seenUris,
        List<string> issues)
    {
        var extension = extensionReference.Element;
        var description = $"{extensionListDescription} ext #{extensionReference.Ordinal}";
        foreach (var attribute in extension.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "uri"))
            {
                continue;
            }

            issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
        }

        var uri = extension.Attribute("uri")?.Value;
        if (string.IsNullOrWhiteSpace(uri))
        {
            issues.Add($"{WorkbookPart} {description} has no uri");
        }
        else if (!seenUris.Add(uri.Trim()))
        {
            issues.Add($"{WorkbookPart} {extensionListDescription} has duplicate ext uri '{uri}'");
        }
    }

    private static void ThrowInvalidWorkbookExtensionListMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid workbook extLst metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetSheetPropertiesMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetSheetPropertiesMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetSheetPropertiesMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetSheetPropertiesMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var sheetPropertiesElements = root.Elements(SpreadsheetNs + "sheetPr").ToArray();
        if (sheetPropertiesElements.Length > 1)
            issues.Add($"{worksheetPart} has {sheetPropertiesElements.Length} sheetPr elements; expected at most one");

        foreach (var sheetProperties in sheetPropertiesElements.Select((element, index) => new WorksheetSheetPropertiesReference(index + 1, element)))
        {
            AddWorksheetSheetPropertiesIssues(worksheetPart, root, sheetProperties, issues);
        }
    }

    private static void AddWorksheetSheetPropertiesIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetSheetPropertiesReference sheetPropertiesReference,
        List<string> issues)
    {
        var sheetProperties = sheetPropertiesReference.Element;
        var description = $"sheetPr #{sheetPropertiesReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            sheetProperties,
            description,
            [
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "syncHorizontal", sheetProperties.Attribute("syncHorizontal")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "syncVertical", sheetProperties.Attribute("syncVertical")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "transitionEvaluation", sheetProperties.Attribute("transitionEvaluation")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "transitionEntry", sheetProperties.Attribute("transitionEntry")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "published", sheetProperties.Attribute("published")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "filterMode", sheetProperties.Attribute("filterMode")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "enableFormatConditionsCalculation", sheetProperties.Attribute("enableFormatConditionsCalculation")?.Value, issues);

        var syncRef = sheetProperties.Attribute("syncRef")?.Value;
        if (!string.IsNullOrWhiteSpace(syncRef) && !IsValidLocalWorksheetReference(syncRef))
            issues.Add($"{worksheetPart} {description} has invalid syncRef value '{syncRef}'");

        var seenChildNames = new HashSet<string>(StringComparer.Ordinal);
        var previousKnownChildOrder = -1;
        foreach (var child in sheetProperties.Elements())
        {
            if (child.Name.Namespace != SpreadsheetNs || !IsKnownWorksheetSheetPropertiesChild(child.Name.LocalName))
            {
                issues.Add($"{worksheetPart} {description} has unexpected child element {child.Name.LocalName}");
                continue;
            }

            if (!seenChildNames.Add(child.Name.LocalName))
                issues.Add($"{worksheetPart} {description} has duplicate {child.Name.LocalName} elements");

            var childOrder = GetWorksheetSheetPropertiesChildOrder(child.Name.LocalName);
            if (childOrder < previousKnownChildOrder)
                issues.Add($"{worksheetPart} {description} child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;

            if (child.Elements().Any())
                issues.Add($"{worksheetPart} {description} child {child.Name.LocalName} has child elements; expected attributes only");

            AddWorksheetSheetPropertiesChildIssues(worksheetPart, description, child, issues);
        }
    }

    private static void AddWorksheetSheetPropertiesChildIssues(
        string worksheetPart,
        string sheetPropertiesDescription,
        XElement child,
        List<string> issues)
    {
        var description = $"{sheetPropertiesDescription} child {child.Name.LocalName}";
        switch (child.Name.LocalName)
        {
            case "outlinePr":
                AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "applyStyles", child.Attribute("applyStyles")?.Value, issues);
                AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "summaryBelow", child.Attribute("summaryBelow")?.Value, issues);
                AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "summaryRight", child.Attribute("summaryRight")?.Value, issues);
                AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "showOutlineSymbols", child.Attribute("showOutlineSymbols")?.Value, issues);
                break;
            case "pageSetUpPr":
                AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "autoPageBreaks", child.Attribute("autoPageBreaks")?.Value, issues);
                AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "fitToPage", child.Attribute("fitToPage")?.Value, issues);
                break;
        }
    }

    private static bool IsKnownWorksheetSheetPropertiesChild(string name) =>
        GetWorksheetSheetPropertiesChildOrder(name) >= 0;

    private static int GetWorksheetSheetPropertiesChildOrder(string name) =>
        name switch
        {
            "tabColor" => 0,
            "outlinePr" => 1,
            "pageSetUpPr" => 2,
            _ => -1
        };

    private static void ThrowInvalidWorksheetSheetPropertiesMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet sheetPr metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetDimensionMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetDimensionMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetDimensionMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetDimensionMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var dimensions = root.Elements(SpreadsheetNs + "dimension").ToArray();
        if (dimensions.Length > 1)
            issues.Add($"{worksheetPart} has {dimensions.Length} dimension elements; expected at most one");

        foreach (var dimension in dimensions.Select((element, index) => new WorksheetDimensionReference(index + 1, element)))
        {
            AddWorksheetDimensionIssues(worksheetPart, root, dimension, issues);
        }
    }

    private static void AddWorksheetDimensionIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetDimensionReference dimensionReference,
        List<string> issues)
    {
        var dimension = dimensionReference.Element;
        var description = $"dimension #{dimensionReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            dimension,
            description,
            [
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        var reference = dimension.Attribute("ref")?.Value;
        if (string.IsNullOrWhiteSpace(reference))
            issues.Add($"{worksheetPart} {description} has no ref attribute");
        else if (!IsValidLocalWorksheetReference(reference))
            issues.Add($"{worksheetPart} {description} has invalid local ref '{reference}'");

        if (dimension.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void ThrowInvalidWorksheetDimensionMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet dimension metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetCellStructureMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetCellStructureMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetCellStructureMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetCellStructureMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        foreach (var columns in root.Elements(SpreadsheetNs + "cols").Select((element, index) => new WorksheetColumnsReference(index + 1, element)))
        {
            AddWorksheetColumnsIssues(worksheetPart, root, columns, issues);
        }

        var sheetDataContainers = root.Elements(SpreadsheetNs + "sheetData").ToArray();
        if (sheetDataContainers.Length == 0)
            issues.Add($"{worksheetPart} has no sheetData element");
        else if (sheetDataContainers.Length > 1)
            issues.Add($"{worksheetPart} has {sheetDataContainers.Length} sheetData elements; expected one");

        foreach (var sheetData in sheetDataContainers.Select((element, index) => new WorksheetSheetDataReference(index + 1, element)))
        {
            AddWorksheetSheetDataIssues(worksheetPart, root, sheetData, issues);
        }
    }

    private static void AddWorksheetColumnsIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetColumnsReference columnsReference,
        List<string> issues)
    {
        var columns = columnsReference.Element;
        var description = $"cols #{columnsReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            columns,
            description,
            [
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);
        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            columns,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr"
            ],
            issues);

        foreach (var unexpectedChild in columns.Elements().Where(element => element.Name != SpreadsheetNs + "col"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected col entries only");
        }

        var columnElements = columns.Elements(SpreadsheetNs + "col").ToArray();
        if (columnElements.Length == 0)
            issues.Add($"{worksheetPart} {description} has no col entries");

        foreach (var column in columnElements.Select((element, index) => new WorksheetColumnReference(index + 1, element)))
        {
            AddWorksheetColumnIssues(worksheetPart, description, column, issues);
        }
    }

    private static void AddWorksheetColumnIssues(
        string worksheetPart,
        string columnsDescription,
        WorksheetColumnReference columnReference,
        List<string> issues)
    {
        var column = columnReference.Element;
        var description = $"{columnsDescription} col #{columnReference.Ordinal}";
        var minIsValid = TryParsePositivePackageInt(column.Attribute("min")?.Value, out var min);
        var maxIsValid = TryParsePositivePackageInt(column.Attribute("max")?.Value, out var max);
        if (!minIsValid)
            issues.Add($"{worksheetPart} {description} has invalid min value '{column.Attribute("min")?.Value ?? string.Empty}'");
        if (!maxIsValid)
            issues.Add($"{worksheetPart} {description} has invalid max value '{column.Attribute("max")?.Value ?? string.Empty}'");
        if (minIsValid && maxIsValid && max < min)
            issues.Add($"{worksheetPart} {description} has max {max} before min {min}");

        AddOptionalNonNegativePackageDoubleIssue(worksheetPart, description, "width", column.Attribute("width")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "style", column.Attribute("style")?.Value, issues);
        AddOptionalWorksheetMetadataOutlineLevelIssue(worksheetPart, description, "outlineLevel", column.Attribute("outlineLevel")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "hidden", column.Attribute("hidden")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "bestFit", column.Attribute("bestFit")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "customWidth", column.Attribute("customWidth")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "phonetic", column.Attribute("phonetic")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "collapsed", column.Attribute("collapsed")?.Value, issues);

        if (column.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetSheetDataIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetSheetDataReference sheetDataReference,
        List<string> issues)
    {
        var sheetData = sheetDataReference.Element;
        var description = $"sheetData #{sheetDataReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            sheetData,
            description,
            [
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);
        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            sheetData,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols"
            ],
            issues);

        if (sheetData.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name.NamespaceName.Length == 0))
            issues.Add($"{worksheetPart} {description} has attributes; expected row children only");

        foreach (var unexpectedChild in sheetData.Elements().Where(element => element.Name != SpreadsheetNs + "row"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected row entries only");
        }

        var seenRows = new HashSet<int>();
        var seenCells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int? previousRowNumber = null;
        foreach (var row in sheetData.Elements(SpreadsheetNs + "row").Select((element, index) => new WorksheetRowReference(index + 1, element)))
        {
            AddWorksheetRowIssues(worksheetPart, description, row, seenRows, seenCells, ref previousRowNumber, issues);
        }
    }

    private static void AddWorksheetRowIssues(
        string worksheetPart,
        string sheetDataDescription,
        WorksheetRowReference rowReference,
        HashSet<int> seenRows,
        HashSet<string> seenCells,
        ref int? previousRowNumber,
        List<string> issues)
    {
        var row = rowReference.Element;
        var description = $"{sheetDataDescription} row #{rowReference.Ordinal}";
        int? rowNumber = null;
        var rowNumberText = row.Attribute("r")?.Value;
        if (!string.IsNullOrWhiteSpace(rowNumberText))
        {
            if (!TryParsePositivePackageInt(rowNumberText, out var parsedRow))
            {
                issues.Add($"{worksheetPart} {description} has invalid r value '{rowNumberText}'");
            }
            else
            {
                rowNumber = parsedRow;
                if (!seenRows.Add(parsedRow))
                    issues.Add($"{worksheetPart} {sheetDataDescription} has duplicate row r '{parsedRow}'");

                if (previousRowNumber is { } previous && parsedRow < previous)
                    issues.Add($"{worksheetPart} {description} appears after row {previous}; expected ascending row order");

                previousRowNumber = parsedRow;
            }
        }

        AddWorksheetRowSpansIssue(worksheetPart, description, row.Attribute("spans")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "s", row.Attribute("s")?.Value, issues);
        AddOptionalNonNegativePackageDoubleIssue(worksheetPart, description, "ht", row.Attribute("ht")?.Value, issues);
        AddOptionalNonNegativePackageDoubleIssue(worksheetPart, description, "dyDescent", row.Attribute("dyDescent")?.Value, issues);
        AddOptionalWorksheetMetadataOutlineLevelIssue(worksheetPart, description, "outlineLevel", row.Attribute("outlineLevel")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "customFormat", row.Attribute("customFormat")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "hidden", row.Attribute("hidden")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "customHeight", row.Attribute("customHeight")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "collapsed", row.Attribute("collapsed")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "thickTop", row.Attribute("thickTop")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "thickBot", row.Attribute("thickBot")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "ph", row.Attribute("ph")?.Value, issues);

        foreach (var unexpectedChild in row.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "c" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected c/extLst children only");
        }

        var extensionLists = row.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        AddWorksheetCellStructureChildOrderingIssues(worksheetPart, description, row, "c", issues);

        foreach (var cell in row.Elements(SpreadsheetNs + "c").Select((element, index) => new WorksheetCellReference(index + 1, element)))
        {
            AddWorksheetCellIssues(worksheetPart, description, cell, rowNumber, seenCells, issues);
        }

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetCellIssues(
        string worksheetPart,
        string rowDescription,
        WorksheetCellReference cellReference,
        int? rowNumber,
        HashSet<string> seenCells,
        List<string> issues)
    {
        var cell = cellReference.Element;
        var description = $"{rowDescription} c #{cellReference.Ordinal}";
        var reference = cell.Attribute("r")?.Value;
        if (!string.IsNullOrWhiteSpace(reference))
        {
            if (!TryParseLocalCellReference(reference, out var address))
            {
                issues.Add($"{worksheetPart} {description} has invalid local r reference '{reference}'");
            }
            else
            {
                if (!seenCells.Add(address.ToA1()))
                    issues.Add($"{worksheetPart} has duplicate cell reference '{address.ToA1()}'");

                if (rowNumber is { } row && address.Row != row)
                    issues.Add($"{worksheetPart} {description} reference '{address.ToA1()}' does not match row {row}");
            }
        }

        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "s", cell.Attribute("s")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "cm", cell.Attribute("cm")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "vm", cell.Attribute("vm")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "ph", cell.Attribute("ph")?.Value, issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "t",
            cell.Attribute("t")?.Value,
            ["b", "d", "e", "inlineStr", "n", "s", "str"],
            issues);

        foreach (var unexpectedChild in cell.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "f" &&
                     element.Name != SpreadsheetNs + "v" &&
                     element.Name != SpreadsheetNs + "is" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected f/v/is/extLst children only");
        }

        var formulas = cell.Elements(SpreadsheetNs + "f").ToArray();
        if (formulas.Length > 1)
            issues.Add($"{worksheetPart} {description} has {formulas.Length} f elements; expected at most one");

        var values = cell.Elements(SpreadsheetNs + "v").ToArray();
        if (values.Length > 1)
            issues.Add($"{worksheetPart} {description} has {values.Length} v elements; expected at most one");

        var inlineStrings = cell.Elements(SpreadsheetNs + "is").ToArray();
        if (inlineStrings.Length > 1)
            issues.Add($"{worksheetPart} {description} has {inlineStrings.Length} is elements; expected at most one");
        if (values.Length > 0 && inlineStrings.Length > 0)
            issues.Add($"{worksheetPart} {description} has both v and is children");

        var extensionLists = cell.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        AddWorksheetCellChildOrderingIssues(worksheetPart, description, cell, issues);

        foreach (var formula in formulas.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetFormulaIssues(worksheetPart, description, formula.Ordinal, formula.Element, issues);
        }

        foreach (var value in values.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetValueIssues(worksheetPart, description, value.Ordinal, value.Element, issues);
        }

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetFormulaIssues(
        string worksheetPart,
        string cellDescription,
        int ordinal,
        XElement formula,
        List<string> issues)
    {
        var description = $"{cellDescription} f #{ordinal}";
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "t",
            formula.Attribute("t")?.Value,
            ["normal", "array", "dataTable", "shared"],
            issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "aca", formula.Attribute("aca")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "ca", formula.Attribute("ca")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "bx", formula.Attribute("bx")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "dt2D", formula.Attribute("dt2D")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "dtr", formula.Attribute("dtr")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "del1", formula.Attribute("del1")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "del2", formula.Attribute("del2")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "si", formula.Attribute("si")?.Value, issues);
        AddOptionalLocalWorksheetReferenceIssue(worksheetPart, description, "ref", formula.Attribute("ref")?.Value, issues);
        AddOptionalLocalWorksheetReferenceIssue(worksheetPart, description, "r1", formula.Attribute("r1")?.Value, issues);
        AddOptionalLocalWorksheetReferenceIssue(worksheetPart, description, "r2", formula.Attribute("r2")?.Value, issues);

        if (formula.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected formula text only");
    }

    private static void AddWorksheetValueIssues(
        string worksheetPart,
        string cellDescription,
        int ordinal,
        XElement value,
        List<string> issues)
    {
        var description = $"{cellDescription} v #{ordinal}";
        if (value.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            issues.Add($"{worksheetPart} {description} has attributes; expected value text only");

        if (value.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected value text only");
    }

    private static void AddWorksheetRowSpansIssue(
        string worksheetPart,
        string description,
        string? spans,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(spans))
            return;

        foreach (var span in spans.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = span.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !TryParsePositivePackageInt(parts[0], out var start) ||
                !TryParsePositivePackageInt(parts[1], out var end) ||
                end < start)
            {
                issues.Add($"{worksheetPart} {description} has invalid spans value '{spans}'");
                return;
            }
        }
    }

    private static void AddWorksheetCellStructureChildOrderingIssues(
        string worksheetPart,
        string description,
        XElement element,
        string preExtensionChildName,
        List<string> issues)
    {
        var children = element.Elements().ToArray();
        var firstExtensionListIndex = Array.FindIndex(children, child => child.Name == SpreadsheetNs + "extLst");
        if (firstExtensionListIndex >= 0 &&
            children
                .Skip(firstExtensionListIndex + 1)
                .Any(child => child.Name == SpreadsheetNs + preExtensionChildName))
        {
            issues.Add($"{worksheetPart} {description} has {preExtensionChildName} entries after extLst; expected extLst last");
        }
    }

    private static void AddWorksheetCellChildOrderingIssues(
        string worksheetPart,
        string description,
        XElement cell,
        List<string> issues)
    {
        var children = cell.Elements().ToArray();
        var firstValueIndex = Array.FindIndex(children, element =>
            element.Name == SpreadsheetNs + "v" ||
            element.Name == SpreadsheetNs + "is");
        if (firstValueIndex >= 0 &&
            children
                .Skip(firstValueIndex + 1)
                .Any(element => element.Name == SpreadsheetNs + "f"))
        {
            issues.Add($"{worksheetPart} {description} has f after v/is; expected formula before value children");
        }

        var firstInlineStringIndex = Array.FindIndex(children, element => element.Name == SpreadsheetNs + "is");
        if (firstInlineStringIndex >= 0 &&
            children
                .Skip(firstInlineStringIndex + 1)
                .Any(element => element.Name == SpreadsheetNs + "v"))
        {
            issues.Add($"{worksheetPart} {description} has v after is; expected value children before inline string children");
        }

        AddWorksheetCellStructureChildOrderingIssues(worksheetPart, description, cell, "f", issues);
        AddWorksheetCellStructureChildOrderingIssues(worksheetPart, description, cell, "v", issues);
        AddWorksheetCellStructureChildOrderingIssues(worksheetPart, description, cell, "is", issues);
    }

    private static void AddOptionalLocalWorksheetReferenceIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!IsValidLocalWorksheetReference(value))
            issues.Add($"{worksheetPart} {description} has invalid local {attributeName} reference '{value}'");
    }

    private static bool TryParseLocalCellReference(string reference, out CellAddress address)
    {
        address = default;
        reference = reference.Trim();
        if (reference.Length == 0 ||
            reference.Contains('!', StringComparison.Ordinal) ||
            reference.Contains('[', StringComparison.Ordinal) ||
            reference.Contains(']', StringComparison.Ordinal) ||
            reference.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return CellAddress.TryParse(reference, SheetId.New(), out address);
    }

    private static bool TryParsePositivePackageInt(string? text, out int value)
    {
        if (TryParseNonNegativePackageInt(text, out value) && value > 0)
            return true;

        value = -1;
        return false;
    }

    private static void AddOptionalNonNegativePackageDoubleIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue) &&
            !double.IsNaN(parsedValue) &&
            !double.IsInfinity(parsedValue) &&
            parsedValue >= 0)
        {
            return;
        }

        issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void ThrowInvalidWorksheetCellStructureMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet cell-structure metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetCalculationPropertiesMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetCalculationPropertiesMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetCalculationPropertiesMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetCalculationPropertiesMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var calculationProperties = root.Elements(SpreadsheetNs + "sheetCalcPr").ToArray();
        if (calculationProperties.Length > 1)
            issues.Add($"{worksheetPart} has {calculationProperties.Length} sheetCalcPr elements; expected at most one");

        foreach (var sheetCalcPr in calculationProperties.Select((element, index) => new WorksheetCalculationPropertiesReference(index + 1, element)))
        {
            AddWorksheetCalculationPropertyIssues(worksheetPart, root, sheetCalcPr, issues);
        }
    }

    private static void AddWorksheetCalculationPropertyIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetCalculationPropertiesReference calculationPropertiesReference,
        List<string> issues)
    {
        var sheetCalcPr = calculationPropertiesReference.Element;
        var description = $"sheetCalcPr #{calculationPropertiesReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            sheetCalcPr,
            description,
            [
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        var worksheetChildren = worksheetRoot.Elements().ToArray();
        var calculationPropertiesIndex = Array.IndexOf(worksheetChildren, sheetCalcPr);
        if (calculationPropertiesIndex >= 0 &&
            worksheetChildren
                .Skip(calculationPropertiesIndex + 1)
                .Any(element => element.Name.Namespace == SpreadsheetNs && element.Name.LocalName == "sheetData"))
        {
            issues.Add($"{worksheetPart} {description} appears before sheetData; expected schema order after that element");
        }

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "fullCalcOnLoad", sheetCalcPr.Attribute("fullCalcOnLoad")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "calcId", sheetCalcPr.Attribute("calcId")?.Value, issues);

        if (sheetCalcPr.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void ThrowInvalidWorksheetCalculationPropertiesMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet sheetCalcPr metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetSheetFormatMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetSheetFormatMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetSheetFormatMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetSheetFormatMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var sheetFormats = root.Elements(SpreadsheetNs + "sheetFormatPr").ToArray();
        if (sheetFormats.Length > 1)
            issues.Add($"{worksheetPart} has {sheetFormats.Length} sheetFormatPr elements; expected at most one");

        foreach (var sheetFormat in sheetFormats.Select((element, index) => new WorksheetSheetFormatReference(index + 1, element)))
        {
            AddWorksheetSheetFormatIssues(worksheetPart, root, sheetFormat, issues);
        }
    }

    private static void AddWorksheetSheetFormatIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetSheetFormatReference sheetFormatReference,
        List<string> issues)
    {
        var sheetFormat = sheetFormatReference.Element;
        var description = $"sheetFormatPr #{sheetFormatReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            sheetFormat,
            description,
            [
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "baseColWidth", sheetFormat.Attribute("baseColWidth")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "defaultColWidth", sheetFormat.Attribute("defaultColWidth")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "defaultRowHeight", sheetFormat.Attribute("defaultRowHeight")?.Value, issues);
        AddOptionalWorksheetMetadataOutlineLevelIssue(worksheetPart, description, "outlineLevelRow", sheetFormat.Attribute("outlineLevelRow")?.Value, issues);
        AddOptionalWorksheetMetadataOutlineLevelIssue(worksheetPart, description, "outlineLevelCol", sheetFormat.Attribute("outlineLevelCol")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "thickTop", sheetFormat.Attribute("thickTop")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "thickBottom", sheetFormat.Attribute("thickBottom")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "zeroHeight", sheetFormat.Attribute("zeroHeight")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "customHeight", sheetFormat.Attribute("customHeight")?.Value, issues);

        if (sheetFormat.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddOptionalWorksheetMetadataOutlineLevelIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!TryParseNonNegativePackageInt(value, out var outlineLevel) || outlineLevel > 7)
            issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void ThrowInvalidWorksheetSheetFormatMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet sheetFormatPr metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetProtectionMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetProtectionMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetProtectionMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetProtectionMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var sheetProtectionElements = root.Elements(SpreadsheetNs + "sheetProtection").ToArray();
        if (sheetProtectionElements.Length > 1)
            issues.Add($"{worksheetPart} has {sheetProtectionElements.Length} sheetProtection elements; expected at most one");

        foreach (var sheetProtection in sheetProtectionElements.Select((element, index) => new WorksheetSheetProtectionReference(index + 1, element)))
        {
            AddWorksheetSheetProtectionIssues(worksheetPart, root, sheetProtection, issues);
        }

        var protectedRangesElements = root.Elements(SpreadsheetNs + "protectedRanges").ToArray();
        if (protectedRangesElements.Length > 1)
            issues.Add($"{worksheetPart} has {protectedRangesElements.Length} protectedRanges elements; expected at most one");

        foreach (var protectedRanges in protectedRangesElements.Select((element, index) => new WorksheetProtectedRangesReference(index + 1, element)))
        {
            AddWorksheetProtectedRangesIssues(worksheetPart, root, protectedRanges, issues);
        }
    }

    private static void AddWorksheetSheetProtectionIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetSheetProtectionReference sheetProtectionReference,
        List<string> issues)
    {
        var sheetProtection = sheetProtectionReference.Element;
        var description = $"sheetProtection #{sheetProtectionReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            sheetProtection,
            description,
            [
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            sheetProtection,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr"
            ],
            issues);

        foreach (var attribute in sheetProtection.Attributes().Where(attribute => IsKnownWorksheetSheetProtectionBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, attribute.Name.LocalName, attribute.Value, issues);
        }

        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "spinCount", sheetProtection.Attribute("spinCount")?.Value, issues);
        AddOptionalWorksheetMetadataNonEmptyAttributeIssue(worksheetPart, description, "algorithmName", sheetProtection.Attribute("algorithmName")?.Value, issues);
        AddOptionalWorksheetMetadataNonEmptyAttributeIssue(worksheetPart, description, "hashValue", sheetProtection.Attribute("hashValue")?.Value, issues);
        AddOptionalWorksheetMetadataNonEmptyAttributeIssue(worksheetPart, description, "saltValue", sheetProtection.Attribute("saltValue")?.Value, issues);
        AddOptionalWorksheetMetadataNonEmptyAttributeIssue(worksheetPart, description, "password", sheetProtection.Attribute("password")?.Value, issues);

        if (sheetProtection.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static bool IsKnownWorksheetSheetProtectionBooleanAttribute(string name) =>
        name is "sheet" or
            "objects" or
            "scenarios" or
            "formatCells" or
            "formatColumns" or
            "formatRows" or
            "insertColumns" or
            "insertRows" or
            "insertHyperlinks" or
            "deleteColumns" or
            "deleteRows" or
            "selectLockedCells" or
            "sort" or
            "autoFilter" or
            "pivotTables" or
            "selectUnlockedCells";

    private static void AddWorksheetProtectedRangesIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetProtectedRangesReference protectedRangesReference,
        List<string> issues)
    {
        var protectedRanges = protectedRangesReference.Element;
        var description = $"protectedRanges #{protectedRangesReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            protectedRanges,
            description,
            [
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            protectedRanges,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection"
            ],
            issues);

        foreach (var unexpectedChild in protectedRanges.Elements().Where(element => element.Name != SpreadsheetNs + "protectedRange"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected protectedRange entries only");
        }

        var protectedRangeElements = protectedRanges.Elements(SpreadsheetNs + "protectedRange").ToArray();
        if (protectedRangeElements.Length == 0)
            issues.Add($"{worksheetPart} {description} has no protectedRange entries");

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenRanges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var protectedRange in protectedRangeElements.Select((element, index) => new WorksheetProtectedRangeReference(index + 1, element)))
        {
            AddWorksheetProtectedRangeIssues(worksheetPart, description, protectedRange, seenNames, seenRanges, issues);
        }
    }

    private static void AddWorksheetProtectedRangeIssues(
        string worksheetPart,
        string protectedRangesDescription,
        WorksheetProtectedRangeReference protectedRangeReference,
        HashSet<string> seenNames,
        HashSet<string> seenRanges,
        List<string> issues)
    {
        var protectedRange = protectedRangeReference.Element;
        var description = $"{protectedRangesDescription} protectedRange #{protectedRangeReference.Ordinal}";

        var sqref = protectedRange.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(sqref))
        {
            issues.Add($"{worksheetPart} {description} has no sqref");
        }
        else if (!IsValidPackageSqref(sqref))
        {
            issues.Add($"{worksheetPart} {description} has invalid sqref '{sqref}'");
        }
        else
        {
            var normalizedSqref = NormalizePackageSqref(sqref);
            if (!seenRanges.Add(normalizedSqref))
                issues.Add($"{worksheetPart} {protectedRangesDescription} has duplicate protectedRange sqref '{normalizedSqref}'");
        }

        var name = protectedRange.Attribute("name")?.Value;
        if (protectedRange.Attribute("name") is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                issues.Add($"{worksheetPart} {description} has blank name");
            }
            else if (!seenNames.Add(name.Trim()))
            {
                issues.Add($"{worksheetPart} {protectedRangesDescription} has duplicate protectedRange name '{name}'");
            }
        }

        AddOptionalWorksheetMetadataNonEmptyAttributeIssue(worksheetPart, description, "password", protectedRange.Attribute("password")?.Value, issues);
        AddOptionalWorksheetMetadataNonEmptyAttributeIssue(worksheetPart, description, "securityDescriptor", protectedRange.Attribute("securityDescriptor")?.Value, issues);
        AddOptionalWorksheetMetadataNonEmptyAttributeIssue(worksheetPart, description, "algorithmName", protectedRange.Attribute("algorithmName")?.Value, issues);
        AddOptionalWorksheetMetadataNonEmptyAttributeIssue(worksheetPart, description, "hashValue", protectedRange.Attribute("hashValue")?.Value, issues);
        AddOptionalWorksheetMetadataNonEmptyAttributeIssue(worksheetPart, description, "saltValue", protectedRange.Attribute("saltValue")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "spinCount", protectedRange.Attribute("spinCount")?.Value, issues);

        var extensionLists = protectedRange.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetProtectedRangeExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
        }

        foreach (var unexpectedChild in protectedRange.Elements().Where(element => element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }
    }

    private static void AddWorksheetProtectedRangeExtensionListIssues(
        string worksheetPart,
        string protectedRangeDescription,
        int ordinal,
        XElement extensionList,
        List<string> issues)
    {
        var description = $"{protectedRangeDescription} extLst #{ordinal}";
        var extensions = extensionList.Elements(SpreadsheetNs + "ext").ToArray();
        if (extensions.Length == 0)
            issues.Add($"{worksheetPart} {description} has no ext entries");

        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in extensions.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            var extensionDescription = $"{description} ext #{extension.Ordinal}";
            var uri = extension.Element.Attribute("uri")?.Value;
            if (string.IsNullOrWhiteSpace(uri))
                issues.Add($"{worksheetPart} {extensionDescription} has no uri");
            else if (!seenUris.Add(uri.Trim()))
                issues.Add($"{worksheetPart} {description} has duplicate ext uri '{uri}'");
        }

        foreach (var unexpectedChild in extensionList.Elements().Where(element => element.Name != SpreadsheetNs + "ext"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected ext entries only");
        }
    }

    private static void AddOptionalWorksheetMetadataNonEmptyAttributeIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (value is null || !string.IsNullOrWhiteSpace(value))
            return;

        issues.Add($"{worksheetPart} {description} has blank {attributeName} attribute");
    }

    private static void ThrowInvalidWorksheetProtectionMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet protection metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetAutoFilterMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetAutoFilterMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetAutoFilterMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetAutoFilterMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var autoFilterElements = root.Elements(SpreadsheetNs + "autoFilter").ToArray();
        if (autoFilterElements.Length > 1)
            issues.Add($"{worksheetPart} has {autoFilterElements.Length} autoFilter elements; expected at most one");

        foreach (var autoFilter in autoFilterElements.Select((element, index) => new WorksheetAutoFilterReference(index + 1, element)))
        {
            AddWorksheetAutoFilterIssues(worksheetPart, root, autoFilter, issues);
        }
    }

    private static void AddWorksheetAutoFilterIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetAutoFilterReference autoFilterReference,
        List<string> issues)
    {
        var autoFilter = autoFilterReference.Element;
        var description = $"autoFilter #{autoFilterReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            autoFilter,
            description,
            [
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            autoFilter,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios"
            ],
            issues);

        var reference = autoFilter.Attribute("ref")?.Value;
        if (autoFilter.Attribute("ref") is not null && string.IsNullOrWhiteSpace(reference))
            issues.Add($"{worksheetPart} {description} has blank ref attribute");
        else if (!string.IsNullOrWhiteSpace(reference) && !IsValidLocalWorksheetReference(reference))
            issues.Add($"{worksheetPart} {description} has invalid ref value '{reference}'");

        AddWorksheetAutoFilterChildOrderingIssues(worksheetPart, description, autoFilter, issues);

        foreach (var unexpectedChild in autoFilter.Elements().Where(element => !IsKnownWorksheetAutoFilterChild(element)))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var seenFilterColumns = new HashSet<int>();
        foreach (var filterColumn in autoFilter.Elements(SpreadsheetNs + "filterColumn").Select((element, index) => new WorksheetAutoFilterColumnReference(index + 1, element)))
        {
            AddWorksheetAutoFilterColumnIssues(worksheetPart, description, filterColumn, seenFilterColumns, issues);
        }

        var nestedSortStates = autoFilter.Elements(SpreadsheetNs + "sortState").ToArray();
        if (nestedSortStates.Length > 1)
            issues.Add($"{worksheetPart} {description} has {nestedSortStates.Length} sortState elements; expected at most one");

        foreach (var nestedSortState in nestedSortStates.Select((element, index) => new WorksheetSortStateReference(index + 1, element)))
        {
            AddWorksheetAutoFilterSortStateIssues(worksheetPart, description, nestedSortState, issues);
        }

        var extensionLists = autoFilter.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetAutoFilterColumnIssues(
        string worksheetPart,
        string autoFilterDescription,
        WorksheetAutoFilterColumnReference filterColumnReference,
        HashSet<int> seenFilterColumns,
        List<string> issues,
        bool allowExtensionList = false)
    {
        var filterColumn = filterColumnReference.Element;
        var description = $"{autoFilterDescription} filterColumn #{filterColumnReference.Ordinal}";
        var colIdValue = filterColumn.Attribute("colId")?.Value;
        if (string.IsNullOrWhiteSpace(colIdValue))
        {
            issues.Add($"{worksheetPart} {description} has no colId");
        }
        else if (TryParseNonNegativePackageInt(colIdValue, out var colId))
        {
            if (!seenFilterColumns.Add(colId))
                issues.Add($"{worksheetPart} {autoFilterDescription} has duplicate filterColumn colId {colId}");
        }
        else
        {
            issues.Add($"{worksheetPart} {description} has invalid colId value '{colIdValue}'");
        }

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "hiddenButton", filterColumn.Attribute("hiddenButton")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "showButton", filterColumn.Attribute("showButton")?.Value, issues);
        AddWorksheetAutoFilterColumnChildOrderingIssues(worksheetPart, description, filterColumn, issues);

        foreach (var unexpectedFilterChild in filterColumn.Elements().Where(element =>
                     !IsKnownWorksheetAutoFilterColumnChild(element) ||
                     (!allowExtensionList && element.Name == SpreadsheetNs + "extLst")))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedFilterChild.Name.LocalName}");
        }

        AddWorksheetAutoFilterColumnChildCountIssues(worksheetPart, description, filterColumn, issues, allowExtensionList);

        foreach (var filters in filterColumn.Elements(SpreadsheetNs + "filters").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetAutoFilterFiltersIssues(worksheetPart, description, filters.Ordinal, filters.Element, issues);
        }

        foreach (var customFilters in filterColumn.Elements(SpreadsheetNs + "customFilters").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetAutoFilterCustomFiltersIssues(worksheetPart, description, customFilters.Ordinal, customFilters.Element, issues);
        }

        foreach (var top10 in filterColumn.Elements(SpreadsheetNs + "top10").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetAutoFilterTop10Issues(worksheetPart, description, top10.Ordinal, top10.Element, issues);
        }

        foreach (var dynamicFilter in filterColumn.Elements(SpreadsheetNs + "dynamicFilter").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetAutoFilterDynamicFilterIssues(worksheetPart, description, dynamicFilter.Ordinal, dynamicFilter.Element, issues);
        }

        foreach (var colorFilter in filterColumn.Elements(SpreadsheetNs + "colorFilter").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetAutoFilterColorFilterIssues(worksheetPart, description, colorFilter.Ordinal, colorFilter.Element, issues);
        }

        foreach (var iconFilter in filterColumn.Elements(SpreadsheetNs + "iconFilter").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetAutoFilterIconFilterIssues(worksheetPart, description, iconFilter.Ordinal, iconFilter.Element, issues);
        }

        if (allowExtensionList)
        {
            var extensionLists = filterColumn.Elements(SpreadsheetNs + "extLst").ToArray();
            if (extensionLists.Length > 1)
                issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

            foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
            {
                AddWorksheetNestedExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
            }
        }
    }

    private static void AddWorksheetAutoFilterColumnChildCountIssues(
        string worksheetPart,
        string filterColumnDescription,
        XElement filterColumn,
        List<string> issues,
        bool allowExtensionList = false)
    {
        string[] childNames = allowExtensionList
            ?
        [
            "filters",
            "top10",
            "customFilters",
            "dynamicFilter",
            "colorFilter",
            "iconFilter",
            "extLst"
        ]
            :
        [
            "filters",
            "top10",
            "customFilters",
            "dynamicFilter",
            "colorFilter",
            "iconFilter"
        ];

        foreach (var childName in childNames)
        {
            var count = filterColumn.Elements(SpreadsheetNs + childName).Count();
            if (count > 1)
                issues.Add($"{worksheetPart} {filterColumnDescription} has {count} {childName} elements; expected at most one");
        }
    }

    private static void AddWorksheetAutoFilterFiltersIssues(
        string worksheetPart,
        string filterColumnDescription,
        int ordinal,
        XElement filters,
        List<string> issues)
    {
        var description = $"{filterColumnDescription} filters #{ordinal}";
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "blank", filters.Attribute("blank")?.Value, issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "calendarType",
            filters.Attribute("calendarType")?.Value,
            [
                "none",
                "gregorian",
                "gregorianUs",
                "gregorianMeFrench",
                "gregorianArabic",
                "hijri",
                "hebrew",
                "taiwan",
                "japan",
                "thai",
                "korea",
                "saka",
                "gregorianXlitEnglish",
                "gregorianXlitFrench"
            ],
            issues);

        foreach (var unexpectedChild in filters.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "filter" &&
                     element.Name != SpreadsheetNs + "dateGroupItem"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        foreach (var filter in filters.Elements(SpreadsheetNs + "filter").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            var filterDescription = $"{description} filter #{filter.Ordinal}";
            if (filter.Element.Attribute("val") is null)
                issues.Add($"{worksheetPart} {filterDescription} has no val attribute");

            if (filter.Element.Elements().Any())
                issues.Add($"{worksheetPart} {filterDescription} has child elements; expected attributes only");
        }

        foreach (var dateGroup in filters.Elements(SpreadsheetNs + "dateGroupItem").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetAutoFilterDateGroupItemIssues(worksheetPart, description, dateGroup.Ordinal, dateGroup.Element, issues);
        }
    }

    private static void AddWorksheetAutoFilterDateGroupItemIssues(
        string worksheetPart,
        string filtersDescription,
        int ordinal,
        XElement dateGroup,
        List<string> issues)
    {
        var description = $"{filtersDescription} dateGroupItem #{ordinal}";
        if (string.IsNullOrWhiteSpace(dateGroup.Attribute("dateTimeGrouping")?.Value))
        {
            issues.Add($"{worksheetPart} {description} has no dateTimeGrouping");
        }
        else
        {
            AddOptionalKnownWorksheetMetadataValueIssue(
                worksheetPart,
                description,
                "dateTimeGrouping",
                dateGroup.Attribute("dateTimeGrouping")?.Value,
                ["year", "month", "day", "hour", "minute", "second"],
                issues);
        }

        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "year", dateGroup.Attribute("year")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "month", dateGroup.Attribute("month")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "day", dateGroup.Attribute("day")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "hour", dateGroup.Attribute("hour")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "minute", dateGroup.Attribute("minute")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "second", dateGroup.Attribute("second")?.Value, issues);

        if (dateGroup.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetAutoFilterCustomFiltersIssues(
        string worksheetPart,
        string filterColumnDescription,
        int ordinal,
        XElement customFilters,
        List<string> issues)
    {
        var description = $"{filterColumnDescription} customFilters #{ordinal}";
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "and", customFilters.Attribute("and")?.Value, issues);

        foreach (var unexpectedChild in customFilters.Elements().Where(element => element.Name != SpreadsheetNs + "customFilter"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var customFilterElements = customFilters.Elements(SpreadsheetNs + "customFilter").ToArray();
        if (customFilterElements.Length == 0)
            issues.Add($"{worksheetPart} {description} has no customFilter entries");
        if (customFilterElements.Length > 2)
            issues.Add($"{worksheetPart} {description} has {customFilterElements.Length} customFilter entries; expected at most two");

        foreach (var customFilter in customFilterElements.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            var customFilterDescription = $"{description} customFilter #{customFilter.Ordinal}";
            var operatorValue = customFilter.Element.Attribute("operator")?.Value;
            if (!string.IsNullOrWhiteSpace(operatorValue) && !IsKnownAutoFilterCustomFilterOperator(operatorValue))
                issues.Add($"{worksheetPart} {customFilterDescription} has unknown operator value '{operatorValue}'");

            if (customFilter.Element.Attribute("val") is null)
                issues.Add($"{worksheetPart} {customFilterDescription} has no val attribute");

            if (customFilter.Element.Elements().Any())
                issues.Add($"{worksheetPart} {customFilterDescription} has child elements; expected attributes only");
        }
    }

    private static bool IsKnownAutoFilterCustomFilterOperator(string value) =>
        value.Trim() is "equal" or
            "lessThan" or
            "lessThanOrEqual" or
            "notEqual" or
            "greaterThanOrEqual" or
            "greaterThan";

    private static void AddWorksheetAutoFilterTop10Issues(
        string worksheetPart,
        string filterColumnDescription,
        int ordinal,
        XElement top10,
        List<string> issues)
    {
        var description = $"{filterColumnDescription} top10 #{ordinal}";
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "top", top10.Attribute("top")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "percent", top10.Attribute("percent")?.Value, issues);
        AddRequiredNonNegativePackageDecimalIssue(worksheetPart, description, "val", top10.Attribute("val")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "filterVal", top10.Attribute("filterVal")?.Value, issues);

        if (top10.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetAutoFilterDynamicFilterIssues(
        string worksheetPart,
        string filterColumnDescription,
        int ordinal,
        XElement dynamicFilter,
        List<string> issues)
    {
        var description = $"{filterColumnDescription} dynamicFilter #{ordinal}";
        if (string.IsNullOrWhiteSpace(dynamicFilter.Attribute("type")?.Value))
        {
            issues.Add($"{worksheetPart} {description} has no type");
        }
        else
        {
            AddOptionalKnownWorksheetMetadataValueIssue(
                worksheetPart,
                description,
                "type",
                dynamicFilter.Attribute("type")?.Value,
                [
                    "aboveAverage",
                    "belowAverage",
                    "tomorrow",
                    "today",
                    "yesterday",
                    "nextWeek",
                    "thisWeek",
                    "lastWeek",
                    "nextMonth",
                    "thisMonth",
                    "lastMonth",
                    "nextQuarter",
                    "thisQuarter",
                    "lastQuarter",
                    "nextYear",
                    "thisYear",
                    "lastYear",
                    "yearToDate",
                    "Q1",
                    "Q2",
                    "Q3",
                    "Q4",
                    "M1",
                    "M2",
                    "M3",
                    "M4",
                    "M5",
                    "M6",
                    "M7",
                    "M8",
                    "M9",
                    "M10",
                    "M11",
                    "M12"
                ],
                issues);
        }

        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "val", dynamicFilter.Attribute("val")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "maxVal", dynamicFilter.Attribute("maxVal")?.Value, issues);

        if (dynamicFilter.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetAutoFilterColorFilterIssues(
        string worksheetPart,
        string filterColumnDescription,
        int ordinal,
        XElement colorFilter,
        List<string> issues)
    {
        var description = $"{filterColumnDescription} colorFilter #{ordinal}";
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "dxfId", colorFilter.Attribute("dxfId")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "cellColor", colorFilter.Attribute("cellColor")?.Value, issues);

        if (colorFilter.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetAutoFilterIconFilterIssues(
        string worksheetPart,
        string filterColumnDescription,
        int ordinal,
        XElement iconFilter,
        List<string> issues)
    {
        var description = $"{filterColumnDescription} iconFilter #{ordinal}";
        if (iconFilter.Attribute("iconSet") is not null && string.IsNullOrWhiteSpace(iconFilter.Attribute("iconSet")?.Value))
            issues.Add($"{worksheetPart} {description} has blank iconSet attribute");
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "iconId", iconFilter.Attribute("iconId")?.Value, issues);

        if (iconFilter.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetAutoFilterSortStateIssues(
        string worksheetPart,
        string autoFilterDescription,
        WorksheetSortStateReference sortStateReference,
        List<string> issues)
    {
        var sortState = sortStateReference.Element;
        var description = $"{autoFilterDescription} sortState #{sortStateReference.Ordinal}";
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "columnSort", sortState.Attribute("columnSort")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "caseSensitive", sortState.Attribute("caseSensitive")?.Value, issues);

        var reference = sortState.Attribute("ref")?.Value;
        if (!string.IsNullOrWhiteSpace(reference) && !IsValidLocalWorksheetReference(reference))
            issues.Add($"{worksheetPart} {description} has invalid local ref reference '{reference}'");

        foreach (var unexpectedChild in sortState.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "sortCondition" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var conditions = sortState.Elements(SpreadsheetNs + "sortCondition").ToArray();
        if (conditions.Length > 64)
            issues.Add($"{worksheetPart} {description} has {conditions.Length} sortCondition entries; expected at most 64");

        foreach (var condition in conditions.Select((element, index) => new WorksheetSortConditionReference(index + 1, element)))
        {
            AddWorksheetSortConditionIssues(worksheetPart, description, condition, issues);
        }

        var extensionLists = sortState.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetAutoFilterChildOrderingIssues(
        string worksheetPart,
        string description,
        XElement autoFilter,
        List<string> issues)
    {
        var previousKnownChildOrder = -1;
        foreach (var child in autoFilter.Elements())
        {
            var childOrder = GetWorksheetAutoFilterChildOrder(child);
            if (childOrder < 0)
                continue;

            if (childOrder < previousKnownChildOrder)
                issues.Add($"{worksheetPart} {description} child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;
        }
    }

    private static void AddWorksheetAutoFilterColumnChildOrderingIssues(
        string worksheetPart,
        string description,
        XElement filterColumn,
        List<string> issues)
    {
        var previousKnownChildOrder = -1;
        foreach (var child in filterColumn.Elements())
        {
            var childOrder = GetWorksheetAutoFilterColumnChildOrder(child);
            if (childOrder < 0)
                continue;

            if (childOrder < previousKnownChildOrder)
                issues.Add($"{worksheetPart} {description} child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;
        }
    }

    private static bool IsKnownWorksheetAutoFilterChild(XElement child) =>
        GetWorksheetAutoFilterChildOrder(child) >= 0;

    private static int GetWorksheetAutoFilterChildOrder(XElement child)
    {
        if (child.Name.Namespace != SpreadsheetNs)
            return -1;

        return child.Name.LocalName switch
        {
            "filterColumn" => 0,
            "sortState" => 1,
            "extLst" => 2,
            _ => -1
        };
    }

    private static bool IsKnownWorksheetAutoFilterColumnChild(XElement child) =>
        GetWorksheetAutoFilterColumnChildOrder(child) >= 0;

    private static int GetWorksheetAutoFilterColumnChildOrder(XElement child)
    {
        if (child.Name.Namespace != SpreadsheetNs)
            return -1;

        return child.Name.LocalName switch
        {
            "filters" => 0,
            "top10" => 1,
            "customFilters" => 2,
            "dynamicFilter" => 3,
            "colorFilter" => 4,
            "iconFilter" => 5,
            "extLst" => 6,
            _ => -1
        };
    }

    private static void AddRequiredNonNegativePackageDecimalIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{worksheetPart} {description} has no {attributeName}");
            return;
        }

        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, attributeName, value, issues);
    }

    private static void AddWorksheetNestedExtensionListIssues(
        string worksheetPart,
        string ownerDescription,
        int ordinal,
        XElement extensionList,
        List<string> issues)
    {
        var description = $"{ownerDescription} extLst #{ordinal}";
        if (extensionList.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            issues.Add($"{worksheetPart} {description} has attributes; expected extension entries only");

        foreach (var unexpectedChild in extensionList.Elements().Where(element => element.Name != SpreadsheetNs + "ext"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected ext entries only");
        }

        var extensions = extensionList.Elements(SpreadsheetNs + "ext").ToArray();
        if (extensions.Length == 0)
            issues.Add($"{worksheetPart} {description} has no ext entries");

        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in extensions.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            var extensionDescription = $"{description} ext #{extension.Ordinal}";
            foreach (var attribute in extension.Element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration ||
                    (attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "uri"))
                {
                    continue;
                }

                issues.Add($"{worksheetPart} {extensionDescription} has unsupported attribute {attribute.Name}");
            }

            var uri = extension.Element.Attribute("uri")?.Value;
            if (string.IsNullOrWhiteSpace(uri))
            {
                issues.Add($"{worksheetPart} {extensionDescription} has no uri");
            }
            else
            {
                var trimmedUri = uri.Trim();
                if (!string.Equals(uri, trimmedUri, StringComparison.Ordinal))
                    issues.Add($"{worksheetPart} {extensionDescription} has untrimmed uri '{uri}'");
                if (!seenUris.Add(trimmedUri))
                    issues.Add($"{worksheetPart} {description} has duplicate ext uri '{uri}'");
            }
        }
    }

    private static void ThrowInvalidWorksheetAutoFilterMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet autoFilter metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetSheetViewsMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetSheetViewsMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetSheetViewsMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetSheetViewsMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var sheetViewsContainers = root.Elements(SpreadsheetNs + "sheetViews").ToArray();
        if (sheetViewsContainers.Length > 1)
            issues.Add($"{worksheetPart} has {sheetViewsContainers.Length} sheetViews elements; expected at most one");

        foreach (var sheetViews in sheetViewsContainers.Select((element, index) => new WorksheetSheetViewsReference(index + 1, element)))
        {
            AddWorksheetSheetViewsIssues(worksheetPart, root, sheetViews, issues);
        }
    }

    private static void AddWorksheetSheetViewsIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetSheetViewsReference sheetViewsReference,
        List<string> issues)
    {
        var sheetViews = sheetViewsReference.Element;
        var description = $"sheetViews #{sheetViewsReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            sheetViews,
            description,
            [
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        foreach (var unexpectedChild in sheetViews.Elements().Where(element => element.Name != SpreadsheetNs + "sheetView"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var sheetViewElements = sheetViews.Elements(SpreadsheetNs + "sheetView").ToArray();
        if (sheetViewElements.Length == 0)
            issues.Add($"{worksheetPart} {description} has no sheetView entries");

        var seenWorkbookViewIds = new HashSet<int>();
        foreach (var sheetView in sheetViewElements.Select((element, index) => new WorksheetSheetViewReference(index + 1, element)))
        {
            AddWorksheetSheetViewIssues(worksheetPart, description, sheetView, seenWorkbookViewIds, issues);
        }
    }

    private static void AddWorksheetSheetViewIssues(
        string worksheetPart,
        string containerDescription,
        WorksheetSheetViewReference sheetViewReference,
        HashSet<int> seenWorkbookViewIds,
        List<string> issues)
    {
        var sheetView = sheetViewReference.Element;
        var description = $"{containerDescription} sheetView #{sheetViewReference.Ordinal}";
        AddRequiredNonNegativePackageIntIssue(worksheetPart, description, "workbookViewId", sheetView.Attribute("workbookViewId")?.Value, issues);
        if (TryParseNonNegativePackageInt(sheetView.Attribute("workbookViewId")?.Value, out var workbookViewId) &&
            !seenWorkbookViewIds.Add(workbookViewId))
        {
            issues.Add($"{worksheetPart} {containerDescription} has duplicate sheetView workbookViewId {workbookViewId}");
        }

        foreach (var attribute in sheetView.Attributes().Where(attribute => IsKnownSheetViewBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, attribute.Name.LocalName, attribute.Value, issues);
        }

        var view = sheetView.Attribute("view")?.Value;
        if (!string.IsNullOrWhiteSpace(view) && !IsKnownSheetViewMode(view))
            issues.Add($"{worksheetPart} {description} has invalid view value '{view}'");

        var topLeftCell = sheetView.Attribute("topLeftCell")?.Value;
        if (!string.IsNullOrWhiteSpace(topLeftCell) && !IsValidLocalCellReference(topLeftCell))
            issues.Add($"{worksheetPart} {description} has invalid topLeftCell reference '{topLeftCell}'");

        foreach (var attributeName in new[]
                 {
                     "colorId",
                     "zoomScale",
                     "zoomScaleNormal",
                     "zoomScalePageLayoutView",
                     "zoomScaleSheetLayoutView"
                 })
        {
            AddOptionalNonNegativePackageIntIssue(worksheetPart, description, attributeName, sheetView.Attribute(attributeName)?.Value, issues);
        }

        var paneElements = sheetView.Elements(SpreadsheetNs + "pane").ToArray();
        if (paneElements.Length > 1)
            issues.Add($"{worksheetPart} {description} has {paneElements.Length} pane elements; expected at most one");

        foreach (var pane in paneElements.Select((element, index) => new WorksheetSheetViewPaneReference(index + 1, element)))
        {
            AddWorksheetSheetViewPaneIssues(worksheetPart, description, pane, issues);
        }

        foreach (var selection in sheetView.Elements(SpreadsheetNs + "selection").Select((element, index) => new WorksheetSheetViewSelectionReference(index + 1, element)))
        {
            AddWorksheetSheetViewSelectionIssues(worksheetPart, description, selection, issues);
        }

        foreach (var pivotSelection in sheetView.Elements(SpreadsheetNs + "pivotSelection").Select((element, index) => new WorksheetSheetViewPivotSelectionReference(index + 1, element)))
        {
            AddWorksheetSheetViewPivotSelectionIssues(worksheetPart, description, pivotSelection, issues);
        }

        foreach (var unexpectedChild in sheetView.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "pane" &&
                     element.Name != SpreadsheetNs + "selection" &&
                     element.Name != SpreadsheetNs + "pivotSelection" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }
    }

    private static bool IsKnownSheetViewBooleanAttribute(string name) =>
        name is "windowProtection" or
            "showFormulas" or
            "showGridLines" or
            "showRowColHeaders" or
            "showZeros" or
            "rightToLeft" or
            "tabSelected" or
            "showRuler" or
            "showOutlineSymbols" or
            "defaultGridColor" or
            "showWhiteSpace";

    private static bool IsKnownSheetViewMode(string value) =>
        value.Trim() is "normal" or "pageBreakPreview" or "pageLayout";

    private static void AddWorksheetSheetViewPaneIssues(
        string worksheetPart,
        string sheetViewDescription,
        WorksheetSheetViewPaneReference paneReference,
        List<string> issues)
    {
        var pane = paneReference.Element;
        var description = $"{sheetViewDescription} pane #{paneReference.Ordinal}";
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "xSplit", pane.Attribute("xSplit")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "ySplit", pane.Attribute("ySplit")?.Value, issues);

        var topLeftCell = pane.Attribute("topLeftCell")?.Value;
        if (!string.IsNullOrWhiteSpace(topLeftCell) && !IsValidLocalCellReference(topLeftCell))
            issues.Add($"{worksheetPart} {description} has invalid topLeftCell reference '{topLeftCell}'");

        var activePane = pane.Attribute("activePane")?.Value;
        if (!string.IsNullOrWhiteSpace(activePane) && !IsKnownPaneValue(activePane))
            issues.Add($"{worksheetPart} {description} has invalid activePane value '{activePane}'");

        var state = pane.Attribute("state")?.Value;
        if (!string.IsNullOrWhiteSpace(state) && !IsKnownPaneState(state))
            issues.Add($"{worksheetPart} {description} has invalid state value '{state}'");

        if (pane.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetSheetViewSelectionIssues(
        string worksheetPart,
        string sheetViewDescription,
        WorksheetSheetViewSelectionReference selectionReference,
        List<string> issues)
    {
        var selection = selectionReference.Element;
        var description = $"{sheetViewDescription} selection #{selectionReference.Ordinal}";

        var pane = selection.Attribute("pane")?.Value;
        if (!string.IsNullOrWhiteSpace(pane) && !IsKnownPaneValue(pane))
            issues.Add($"{worksheetPart} {description} has invalid pane value '{pane}'");

        var activeCell = selection.Attribute("activeCell")?.Value;
        if (!string.IsNullOrWhiteSpace(activeCell) && !IsValidLocalCellReference(activeCell))
            issues.Add($"{worksheetPart} {description} has invalid activeCell reference '{activeCell}'");

        var sqref = selection.Attribute("sqref")?.Value;
        if (!string.IsNullOrWhiteSpace(sqref) && !IsValidPackageSqref(sqref))
            issues.Add($"{worksheetPart} {description} has invalid sqref '{sqref}'");

        if (selection.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetSheetViewPivotSelectionIssues(
        string worksheetPart,
        string sheetViewDescription,
        WorksheetSheetViewPivotSelectionReference pivotSelectionReference,
        List<string> issues)
    {
        var pivotSelection = pivotSelectionReference.Element;
        var description = $"{sheetViewDescription} pivotSelection #{pivotSelectionReference.Ordinal}";

        var pane = pivotSelection.Attribute("pane")?.Value;
        if (!string.IsNullOrWhiteSpace(pane) && !IsKnownPaneValue(pane))
            issues.Add($"{worksheetPart} {description} has invalid pane value '{pane}'");

        foreach (var attributeName in new[] { "activeRow", "activeCol", "previousRow", "previousCol" })
        {
            AddOptionalNonNegativePackageIntIssue(worksheetPart, description, attributeName, pivotSelection.Attribute(attributeName)?.Value, issues);
        }

        if (pivotSelection.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static bool IsKnownPaneValue(string value) =>
        value.Trim() is "bottomRight" or "topRight" or "bottomLeft" or "topLeft";

    private static bool IsKnownPaneState(string value) =>
        value.Trim() is "split" or "frozen" or "frozenSplit";

    private static void AddOptionalNonNegativePackageDecimalIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed < 0m)
            issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void ThrowInvalidWorksheetSheetViewsMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet sheetViews metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetCustomSheetViewsMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetCustomSheetViewsMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetCustomSheetViewsMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetCustomSheetViewsMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var customSheetViewsContainers = root.Elements(SpreadsheetNs + "customSheetViews").ToArray();
        if (customSheetViewsContainers.Length > 1)
            issues.Add($"{worksheetPart} has {customSheetViewsContainers.Length} customSheetViews elements; expected at most one");

        foreach (var customSheetViews in customSheetViewsContainers.Select((element, index) => new WorksheetCustomSheetViewsReference(index + 1, element)))
        {
            AddWorksheetCustomSheetViewsIssues(worksheetPart, root, customSheetViews, issues);
        }
    }

    private static void AddWorksheetCustomSheetViewsIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetCustomSheetViewsReference customSheetViewsReference,
        List<string> issues)
    {
        var customSheetViews = customSheetViewsReference.Element;
        var description = $"customSheetViews #{customSheetViewsReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            customSheetViews,
            description,
            [
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            customSheetViews,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate"
            ],
            issues);

        foreach (var unexpectedChild in customSheetViews.Elements().Where(element => element.Name != SpreadsheetNs + "customSheetView"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected customSheetView entries only");
        }

        var customSheetViewElements = customSheetViews.Elements(SpreadsheetNs + "customSheetView").ToArray();
        if (customSheetViewElements.Length == 0)
            issues.Add($"{worksheetPart} {description} has no customSheetView entries");

        var seenGuids = new HashSet<Guid>();
        foreach (var customSheetView in customSheetViewElements.Select((element, index) => new WorksheetCustomSheetViewReference(index + 1, element)))
        {
            AddWorksheetCustomSheetViewIssues(worksheetPart, description, customSheetView, seenGuids, issues);
        }
    }

    private static void AddWorksheetCustomSheetViewIssues(
        string worksheetPart,
        string containerDescription,
        WorksheetCustomSheetViewReference customSheetViewReference,
        HashSet<Guid> seenGuids,
        List<string> issues)
    {
        var customSheetView = customSheetViewReference.Element;
        var description = $"{containerDescription} customSheetView #{customSheetViewReference.Ordinal}";

        var guid = customSheetView.Attribute("guid")?.Value;
        if (string.IsNullOrWhiteSpace(guid))
        {
            issues.Add($"{worksheetPart} {description} has no guid");
        }
        else if (!Guid.TryParse(guid, out var parsedGuid))
        {
            issues.Add($"{worksheetPart} {description} has invalid guid value '{guid}'");
        }
        else if (!seenGuids.Add(parsedGuid))
        {
            issues.Add($"{worksheetPart} {containerDescription} has duplicate customSheetView guid '{guid}'");
        }

        foreach (var attribute in customSheetView.Attributes().Where(attribute => IsKnownCustomSheetViewBooleanAttribute(attribute.Name.LocalName)))
        {
            AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, attribute.Name.LocalName, attribute.Value, issues);
        }

        foreach (var attributeName in new[] { "scale", "colorId" })
        {
            AddOptionalNonNegativePackageIntIssue(worksheetPart, description, attributeName, customSheetView.Attribute(attributeName)?.Value, issues);
        }

        var view = customSheetView.Attribute("view")?.Value;
        if (!string.IsNullOrWhiteSpace(view) && !IsKnownSheetViewMode(view))
            issues.Add($"{worksheetPart} {description} has invalid view value '{view}'");

        var state = customSheetView.Attribute("state")?.Value;
        if (!string.IsNullOrWhiteSpace(state) && !IsKnownWorkbookViewVisibility(state))
            issues.Add($"{worksheetPart} {description} has invalid state value '{state}'");

        var topLeftCell = customSheetView.Attribute("topLeftCell")?.Value;
        if (!string.IsNullOrWhiteSpace(topLeftCell) && !IsValidLocalCellReference(topLeftCell))
            issues.Add($"{worksheetPart} {description} has invalid topLeftCell reference '{topLeftCell}'");

        AddWorksheetCustomSheetViewChildOrderingIssues(worksheetPart, description, customSheetView, issues);

        var singletonChildNames = new[]
        {
            "pane",
            "rowBreaks",
            "colBreaks",
            "pageMargins",
            "printOptions",
            "pageSetup",
            "headerFooter",
            "autoFilter",
            "extLst"
        };
        foreach (var childName in singletonChildNames)
        {
            var childCount = customSheetView.Elements(SpreadsheetNs + childName).Count();
            if (childCount > 1)
                issues.Add($"{worksheetPart} {description} has {childCount} {childName} elements; expected at most one");
        }

        foreach (var pane in customSheetView.Elements(SpreadsheetNs + "pane").Select((element, index) => new WorksheetSheetViewPaneReference(index + 1, element)))
        {
            AddWorksheetSheetViewPaneIssues(worksheetPart, description, pane, issues);
        }

        foreach (var selection in customSheetView.Elements(SpreadsheetNs + "selection").Select((element, index) => new WorksheetSheetViewSelectionReference(index + 1, element)))
        {
            AddWorksheetSheetViewSelectionIssues(worksheetPart, description, selection, issues);
        }

        foreach (var rowBreaks in customSheetView.Elements(SpreadsheetNs + "rowBreaks").Select((element, index) => new WorksheetPageBreaksReference(
                     index + 1,
                     element,
                     "rowBreaks",
                     (int)CellAddress.MaxRow,
                     (int)CellAddress.MaxCol - 1)))
        {
            AddWorksheetCustomSheetViewPageBreaksIssues(worksheetPart, description, rowBreaks, issues);
        }

        foreach (var columnBreaks in customSheetView.Elements(SpreadsheetNs + "colBreaks").Select((element, index) => new WorksheetPageBreaksReference(
                     index + 1,
                     element,
                     "colBreaks",
                     (int)CellAddress.MaxCol,
                     (int)CellAddress.MaxRow - 1)))
        {
            AddWorksheetCustomSheetViewPageBreaksIssues(worksheetPart, description, columnBreaks, issues);
        }

        foreach (var pageMargins in customSheetView.Elements(SpreadsheetNs + "pageMargins").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetCustomSheetViewPageMarginsIssues(worksheetPart, description, pageMargins.Ordinal, pageMargins.Element, issues);
        }

        foreach (var printOptions in customSheetView.Elements(SpreadsheetNs + "printOptions").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetCustomSheetViewPrintOptionsIssues(worksheetPart, description, printOptions.Ordinal, printOptions.Element, issues);
        }

        foreach (var pageSetup in customSheetView.Elements(SpreadsheetNs + "pageSetup").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetCustomSheetViewPageSetupIssues(worksheetPart, description, pageSetup.Ordinal, pageSetup.Element, issues);
        }

        foreach (var headerFooter in customSheetView.Elements(SpreadsheetNs + "headerFooter").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetCustomSheetViewHeaderFooterIssues(worksheetPart, description, headerFooter.Ordinal, headerFooter.Element, issues);
        }

        foreach (var autoFilter in customSheetView.Elements(SpreadsheetNs + "autoFilter").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetCustomSheetViewAutoFilterIssues(worksheetPart, description, autoFilter.Ordinal, autoFilter.Element, issues);
        }

        foreach (var unexpectedChild in customSheetView.Elements().Where(element => !IsKnownCustomSheetViewChild(element)))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }
    }

    private static bool IsKnownCustomSheetViewBooleanAttribute(string name) =>
        name is "showPageBreaks" or
            "showFormulas" or
            "showGridLines" or
            "showRowCol" or
            "outlineSymbols" or
            "zeroValues" or
            "fitToPage" or
            "printArea" or
            "filter" or
            "showAutoFilter" or
            "hiddenRows" or
            "hiddenColumns" or
            "filterUnique" or
            "showRuler";

    private static void AddWorksheetCustomSheetViewChildOrderingIssues(
        string worksheetPart,
        string description,
        XElement customSheetView,
        List<string> issues)
    {
        var previousKnownChildOrder = -1;
        foreach (var child in customSheetView.Elements())
        {
            var childOrder = GetCustomSheetViewChildOrder(child);
            if (childOrder < 0)
                continue;

            if (childOrder < previousKnownChildOrder)
                issues.Add($"{worksheetPart} {description} child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;
        }
    }

    private static bool IsKnownCustomSheetViewChild(XElement child) =>
        GetCustomSheetViewChildOrder(child) >= 0;

    private static int GetCustomSheetViewChildOrder(XElement child)
    {
        if (child.Name.Namespace != SpreadsheetNs)
            return -1;

        return child.Name.LocalName switch
        {
            "pane" => 0,
            "selection" => 1,
            "rowBreaks" => 2,
            "colBreaks" => 3,
            "pageMargins" => 4,
            "printOptions" => 5,
            "pageSetup" => 6,
            "headerFooter" => 7,
            "autoFilter" => 8,
            "extLst" => 9,
            _ => -1
        };
    }

    private static void AddWorksheetCustomSheetViewPageBreaksIssues(
        string worksheetPart,
        string customSheetViewDescription,
        WorksheetPageBreaksReference pageBreaksReference,
        List<string> issues)
    {
        var pageBreaks = pageBreaksReference.Element;
        var description = $"{customSheetViewDescription} {pageBreaksReference.ElementName} #{pageBreaksReference.Ordinal}";
        var breakElements = pageBreaks.Elements(SpreadsheetNs + "brk").ToArray();
        AddOptionalPackageCountIssue(worksheetPart, description, "count", pageBreaks.Attribute("count")?.Value, breakElements.Length, issues);
        AddOptionalWorksheetPageBreakManualCountIssue(worksheetPart, description, pageBreaks.Attribute("manualBreakCount")?.Value, breakElements.Length, issues);

        foreach (var unexpectedChild in pageBreaks.Elements().Where(element => element.Name != SpreadsheetNs + "brk"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var seenBreakIds = new HashSet<int>();
        foreach (var breakElement in breakElements.Select((element, index) => new WorksheetPageBreakReference(index + 1, element)))
        {
            AddWorksheetPageBreakIssues(
                worksheetPart,
                description,
                breakElement,
                pageBreaksReference.MaxBreakId,
                pageBreaksReference.MaxBreakSpan,
                seenBreakIds,
                issues);
        }
    }

    private static void AddWorksheetCustomSheetViewPageMarginsIssues(
        string worksheetPart,
        string customSheetViewDescription,
        int ordinal,
        XElement pageMargins,
        List<string> issues)
    {
        var description = $"{customSheetViewDescription} pageMargins #{ordinal}";
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "left", pageMargins.Attribute("left")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "right", pageMargins.Attribute("right")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "top", pageMargins.Attribute("top")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "bottom", pageMargins.Attribute("bottom")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "header", pageMargins.Attribute("header")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "footer", pageMargins.Attribute("footer")?.Value, issues);

        if (pageMargins.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetCustomSheetViewPrintOptionsIssues(
        string worksheetPart,
        string customSheetViewDescription,
        int ordinal,
        XElement printOptions,
        List<string> issues)
    {
        var description = $"{customSheetViewDescription} printOptions #{ordinal}";
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "horizontalCentered", printOptions.Attribute("horizontalCentered")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "verticalCentered", printOptions.Attribute("verticalCentered")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "headings", printOptions.Attribute("headings")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "gridLines", printOptions.Attribute("gridLines")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "gridLinesSet", printOptions.Attribute("gridLinesSet")?.Value, issues);

        if (printOptions.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetCustomSheetViewPageSetupIssues(
        string worksheetPart,
        string customSheetViewDescription,
        int ordinal,
        XElement pageSetup,
        List<string> issues)
    {
        var description = $"{customSheetViewDescription} pageSetup #{ordinal}";
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "paperSize", pageSetup.Attribute("paperSize")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "scale", pageSetup.Attribute("scale")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "firstPageNumber", pageSetup.Attribute("firstPageNumber")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "fitToWidth", pageSetup.Attribute("fitToWidth")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "fitToHeight", pageSetup.Attribute("fitToHeight")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "horizontalDpi", pageSetup.Attribute("horizontalDpi")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "verticalDpi", pageSetup.Attribute("verticalDpi")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "copies", pageSetup.Attribute("copies")?.Value, issues);

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "usePrinterDefaults", pageSetup.Attribute("usePrinterDefaults")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "blackAndWhite", pageSetup.Attribute("blackAndWhite")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "draft", pageSetup.Attribute("draft")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "useFirstPageNumber", pageSetup.Attribute("useFirstPageNumber")?.Value, issues);

        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "pageOrder",
            pageSetup.Attribute("pageOrder")?.Value,
            ["downThenOver", "overThenDown"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "orientation",
            pageSetup.Attribute("orientation")?.Value,
            ["default", "portrait", "landscape"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "cellComments",
            pageSetup.Attribute("cellComments")?.Value,
            ["none", "asDisplayed", "atEnd"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "errors",
            pageSetup.Attribute("errors")?.Value,
            ["displayed", "blank", "dash", "NA"],
            issues);

        if (pageSetup.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetCustomSheetViewHeaderFooterIssues(
        string worksheetPart,
        string customSheetViewDescription,
        int ordinal,
        XElement headerFooter,
        List<string> issues)
    {
        var description = $"{customSheetViewDescription} headerFooter #{ordinal}";
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "differentOddEven", headerFooter.Attribute("differentOddEven")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "differentFirst", headerFooter.Attribute("differentFirst")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "scaleWithDoc", headerFooter.Attribute("scaleWithDoc")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "alignWithMargins", headerFooter.Attribute("alignWithMargins")?.Value, issues);

        var seenChildNames = new HashSet<string>(StringComparer.Ordinal);
        var previousKnownChildOrder = -1;
        foreach (var child in headerFooter.Elements())
        {
            if (child.Name.Namespace != SpreadsheetNs || !IsKnownWorksheetHeaderFooterChild(child.Name.LocalName))
            {
                issues.Add($"{worksheetPart} {description} has unexpected child element {child.Name.LocalName}");
                continue;
            }

            if (!seenChildNames.Add(child.Name.LocalName))
                issues.Add($"{worksheetPart} {description} has duplicate {child.Name.LocalName} elements");

            var childOrder = GetWorksheetHeaderFooterChildOrder(child.Name.LocalName);
            if (childOrder < previousKnownChildOrder)
                issues.Add($"{worksheetPart} {description} child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;

            if (child.Elements().Any())
                issues.Add($"{worksheetPart} {description} child {child.Name.LocalName} has child elements; expected text only");
        }
    }

    private static void AddWorksheetCustomSheetViewAutoFilterIssues(
        string worksheetPart,
        string customSheetViewDescription,
        int ordinal,
        XElement autoFilter,
        List<string> issues)
    {
        var description = $"{customSheetViewDescription} autoFilter #{ordinal}";
        var reference = autoFilter.Attribute("ref")?.Value;
        if (!string.IsNullOrWhiteSpace(reference) && !IsValidLocalWorksheetReference(reference))
            issues.Add($"{worksheetPart} {description} has invalid ref value '{reference}'");

        foreach (var unexpectedChild in autoFilter.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "filterColumn" &&
                     element.Name != SpreadsheetNs + "sortState" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var seenFilterColumns = new HashSet<int>();
        foreach (var filterColumn in autoFilter.Elements(SpreadsheetNs + "filterColumn").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            var filterDescription = $"{description} filterColumn #{filterColumn.Ordinal}";
            if (TryParseNonNegativePackageInt(filterColumn.Element.Attribute("colId")?.Value, out var colId))
            {
                if (!seenFilterColumns.Add(colId))
                    issues.Add($"{worksheetPart} {description} has duplicate filterColumn colId {colId}");
            }
            else if (!string.IsNullOrWhiteSpace(filterColumn.Element.Attribute("colId")?.Value))
            {
                issues.Add($"{worksheetPart} {filterDescription} has invalid colId value '{filterColumn.Element.Attribute("colId")?.Value}'");
            }

            foreach (var unexpectedFilterChild in filterColumn.Element.Elements().Where(element =>
                         element.Name != SpreadsheetNs + "filters" &&
                         element.Name != SpreadsheetNs + "top10" &&
                         element.Name != SpreadsheetNs + "customFilters" &&
                         element.Name != SpreadsheetNs + "dynamicFilter" &&
                         element.Name != SpreadsheetNs + "colorFilter" &&
                         element.Name != SpreadsheetNs + "iconFilter" &&
                         element.Name != SpreadsheetNs + "extLst"))
            {
                issues.Add($"{worksheetPart} {filterDescription} has unexpected child element {unexpectedFilterChild.Name.LocalName}");
            }
        }
    }

    private static void ThrowInvalidWorksheetCustomSheetViewsMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet customSheetViews metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetMergeCellsMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetMergeCellsMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetMergeCellsMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetMergeCellsMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var mergeCellsContainers = root.Elements(SpreadsheetNs + "mergeCells").ToArray();
        if (mergeCellsContainers.Length > 1)
            issues.Add($"{worksheetPart} has {mergeCellsContainers.Length} mergeCells elements; expected at most one");

        foreach (var mergeCells in mergeCellsContainers.Select((element, index) => new WorksheetMergeCellsReference(index + 1, element)))
        {
            AddWorksheetMergeCellsIssues(worksheetPart, root, mergeCells, issues);
        }
    }

    private static void AddWorksheetMergeCellsIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetMergeCellsReference mergeCellsReference,
        List<string> issues)
    {
        var mergeCells = mergeCellsReference.Element;
        var description = $"mergeCells #{mergeCellsReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            mergeCells,
            description,
            [
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            mergeCells,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews"
            ],
            issues);

        foreach (var unexpectedChild in mergeCells.Elements().Where(element => element.Name != SpreadsheetNs + "mergeCell"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected mergeCell entries only");
        }

        var mergeCellElements = mergeCells.Elements(SpreadsheetNs + "mergeCell").ToArray();
        if (mergeCellElements.Length == 0)
            issues.Add($"{worksheetPart} {description} has no mergeCell entries");

        AddOptionalPackageCountIssue(worksheetPart, description, "count", mergeCells.Attribute("count")?.Value, mergeCellElements.Length, issues);

        var seenRanges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parsedRanges = new List<WorksheetMergeRange>();
        foreach (var mergeCell in mergeCellElements.Select((element, index) => new WorksheetMergeCellReference(index + 1, element)))
        {
            AddWorksheetMergeCellIssues(worksheetPart, description, mergeCell, seenRanges, parsedRanges, issues);
        }
    }

    private static void AddWorksheetMergeCellIssues(
        string worksheetPart,
        string mergeCellsDescription,
        WorksheetMergeCellReference mergeCellReference,
        HashSet<string> seenRanges,
        List<WorksheetMergeRange> parsedRanges,
        List<string> issues)
    {
        var mergeCell = mergeCellReference.Element;
        var description = $"{mergeCellsDescription} mergeCell #{mergeCellReference.Ordinal}";
        var reference = mergeCell.Attribute("ref")?.Value;
        if (string.IsNullOrWhiteSpace(reference))
        {
            issues.Add($"{worksheetPart} {description} has no ref");
        }
        else if (!TryParseLocalWorksheetMergeRange(reference, out var mergeRange))
        {
            issues.Add($"{worksheetPart} {description} has invalid local ref range '{reference}'");
        }
        else
        {
            if (!seenRanges.Add(mergeRange.NormalizedReference))
                issues.Add($"{worksheetPart} {mergeCellsDescription} has duplicate mergeCell ref '{mergeRange.NormalizedReference}'");

            foreach (var previousRange in parsedRanges)
            {
                if (MergeRangesOverlap(previousRange, mergeRange))
                {
                    issues.Add($"{worksheetPart} {mergeCellsDescription} mergeCell ref '{mergeRange.NormalizedReference}' overlaps '{previousRange.NormalizedReference}'");
                    break;
                }
            }

            parsedRanges.Add(mergeRange);
        }

        if (mergeCell.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static bool TryParseLocalWorksheetMergeRange(string reference, out WorksheetMergeRange mergeRange)
    {
        mergeRange = default;
        if (!IsValidLocalWorksheetReference(reference))
            return false;

        var rangeParts = reference.Trim().Split(':', StringSplitOptions.TrimEntries);
        if (rangeParts.Length is not 1 and not 2)
            return false;

        var sheet = SheetId.New();
        if (!CellAddress.TryParse(rangeParts[0], sheet, out var start))
            return false;

        var end = start;
        if (rangeParts.Length == 2 && !CellAddress.TryParse(rangeParts[1], sheet, out end))
            return false;

        if (start.Row > end.Row || start.Col > end.Col)
            return false;

        var normalizedReference = rangeParts.Length == 1
            ? start.ToA1()
            : $"{start.ToA1()}:{end.ToA1()}";
        mergeRange = new WorksheetMergeRange(
            normalizedReference,
            start.Row,
            end.Row,
            start.Col,
            end.Col);
        return true;
    }

    private static bool MergeRangesOverlap(WorksheetMergeRange left, WorksheetMergeRange right) =>
        left.StartRow <= right.EndRow &&
        left.EndRow >= right.StartRow &&
        left.StartColumn <= right.EndColumn &&
        left.EndColumn >= right.StartColumn;

    private static void ThrowInvalidWorksheetMergeCellsMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet mergeCells metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetPhoneticPropertiesMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetPhoneticPropertiesMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetPhoneticPropertiesMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetPhoneticPropertiesMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var phoneticProperties = root.Elements(SpreadsheetNs + "phoneticPr").ToArray();
        if (phoneticProperties.Length > 1)
            issues.Add($"{worksheetPart} has {phoneticProperties.Length} phoneticPr elements; expected at most one");

        foreach (var phoneticPr in phoneticProperties.Select((element, index) => new WorksheetPhoneticPropertiesReference(index + 1, element)))
        {
            AddWorksheetPhoneticPropertyIssues(worksheetPart, root, phoneticPr, issues);
        }
    }

    private static void AddWorksheetPhoneticPropertyIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetPhoneticPropertiesReference phoneticPropertiesReference,
        List<string> issues)
    {
        var phoneticPr = phoneticPropertiesReference.Element;
        var description = $"phoneticPr #{phoneticPropertiesReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            phoneticPr,
            description,
            [
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "fontId", phoneticPr.Attribute("fontId")?.Value, issues);

        var type = phoneticPr.Attribute("type")?.Value;
        if (!string.IsNullOrWhiteSpace(type) && !IsKnownPhoneticType(type))
            issues.Add($"{worksheetPart} {description} has invalid type value '{type}'");

        var alignment = phoneticPr.Attribute("alignment")?.Value;
        if (!string.IsNullOrWhiteSpace(alignment) && !IsKnownPhoneticAlignment(alignment))
            issues.Add($"{worksheetPart} {description} has invalid alignment value '{alignment}'");

        if (phoneticPr.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static bool IsKnownPhoneticType(string value) =>
        value.Trim() is "halfwidthKatakana" or "fullwidthKatakana" or "hiragana" or "noConversion";

    private static bool IsKnownPhoneticAlignment(string value) =>
        value.Trim() is "noControl" or "left" or "center" or "distributed";

    private static void ThrowInvalidWorksheetPhoneticPropertiesMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet phoneticPr metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetSortAndDataConsolidationMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetSortAndDataConsolidationMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetSortAndDataConsolidationMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetSortAndDataConsolidationMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var sortStates = root.Elements(SpreadsheetNs + "sortState").ToArray();
        if (sortStates.Length > 1)
            issues.Add($"{worksheetPart} has {sortStates.Length} sortState elements; expected at most one");

        foreach (var sortState in sortStates.Select((element, index) => new WorksheetSortStateReference(index + 1, element)))
        {
            AddWorksheetSortStateIssues(worksheetPart, root, sortState, issues);
        }

        var dataConsolidates = root.Elements(SpreadsheetNs + "dataConsolidate").ToArray();
        if (dataConsolidates.Length > 1)
            issues.Add($"{worksheetPart} has {dataConsolidates.Length} dataConsolidate elements; expected at most one");

        foreach (var dataConsolidate in dataConsolidates.Select((element, index) => new WorksheetDataConsolidationReference(index + 1, element)))
        {
            AddWorksheetDataConsolidationIssues(worksheetPart, root, dataConsolidate, issues);
        }
    }

    private static void AddWorksheetSortStateIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetSortStateReference sortStateReference,
        List<string> issues)
    {
        var sortState = sortStateReference.Element;
        var description = $"sortState #{sortStateReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            sortState,
            description,
            [
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "columnSort", sortState.Attribute("columnSort")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "caseSensitive", sortState.Attribute("caseSensitive")?.Value, issues);

        var reference = sortState.Attribute("ref")?.Value;
        if (!string.IsNullOrWhiteSpace(reference) && !IsValidLocalWorksheetReference(reference))
            issues.Add($"{worksheetPart} {description} has invalid local ref reference '{reference}'");

        foreach (var unexpectedChild in sortState.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "sortCondition" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var conditions = sortState.Elements(SpreadsheetNs + "sortCondition").ToArray();
        if (conditions.Length > 64)
            issues.Add($"{worksheetPart} {description} has {conditions.Length} sortCondition entries; expected at most 64");

        foreach (var condition in conditions.Select((element, index) => new WorksheetSortConditionReference(index + 1, element)))
        {
            AddWorksheetSortConditionIssues(worksheetPart, description, condition, issues);
        }
    }

    private static void AddWorksheetSortConditionIssues(
        string worksheetPart,
        string sortStateDescription,
        WorksheetSortConditionReference conditionReference,
        List<string> issues)
    {
        var condition = conditionReference.Element;
        var description = $"{sortStateDescription} sortCondition #{conditionReference.Ordinal}";
        var reference = condition.Attribute("ref")?.Value;
        if (string.IsNullOrWhiteSpace(reference))
        {
            issues.Add($"{worksheetPart} {description} has no ref reference");
        }
        else if (!IsValidLocalWorksheetReference(reference))
        {
            issues.Add($"{worksheetPart} {description} has invalid local ref reference '{reference}'");
        }

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "descending", condition.Attribute("descending")?.Value, issues);

        var sortBy = condition.Attribute("sortBy")?.Value;
        if (!string.IsNullOrWhiteSpace(sortBy) && !IsKnownSortByValue(sortBy))
            issues.Add($"{worksheetPart} {description} has invalid sortBy value '{sortBy}'");

        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "dxfId", condition.Attribute("dxfId")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "iconId", condition.Attribute("iconId")?.Value, issues);

        if (condition.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static bool IsKnownSortByValue(string value) =>
        value.Trim() is "value" or "cellColor" or "fontColor" or "icon";

    private static void AddWorksheetDataConsolidationIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetDataConsolidationReference dataConsolidationReference,
        List<string> issues)
    {
        var dataConsolidate = dataConsolidationReference.Element;
        var description = $"dataConsolidate #{dataConsolidationReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            dataConsolidate,
            description,
            [
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        var function = dataConsolidate.Attribute("function")?.Value;
        if (!string.IsNullOrWhiteSpace(function) && !IsKnownDataConsolidationFunction(function))
            issues.Add($"{worksheetPart} {description} has invalid function value '{function}'");

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "leftLabels", dataConsolidate.Attribute("leftLabels")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "topLabels", dataConsolidate.Attribute("topLabels")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "link", dataConsolidate.Attribute("link")?.Value, issues);

        foreach (var unexpectedChild in dataConsolidate.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "dataRefs" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var dataRefsContainers = dataConsolidate.Elements(SpreadsheetNs + "dataRefs").ToArray();
        if (dataRefsContainers.Length > 1)
            issues.Add($"{worksheetPart} {description} has {dataRefsContainers.Length} dataRefs elements; expected at most one");

        foreach (var dataRefs in dataRefsContainers.Select((element, index) => new WorksheetDataRefsReference(index + 1, element)))
        {
            AddWorksheetDataRefsIssues(worksheetPart, description, dataRefs, issues);
        }
    }

    private static bool IsKnownDataConsolidationFunction(string value) =>
        value.Trim() is "average" or
            "count" or
            "countNums" or
            "max" or
            "min" or
            "product" or
            "stdDev" or
            "stdDevp" or
            "sum" or
            "var" or
            "varp";

    private static void AddWorksheetDataRefsIssues(
        string worksheetPart,
        string dataConsolidationDescription,
        WorksheetDataRefsReference dataRefsReference,
        List<string> issues)
    {
        var dataRefs = dataRefsReference.Element;
        var description = $"{dataConsolidationDescription} dataRefs #{dataRefsReference.Ordinal}";

        foreach (var unexpectedChild in dataRefs.Elements().Where(element => element.Name != SpreadsheetNs + "dataRef"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var references = dataRefs.Elements(SpreadsheetNs + "dataRef").ToArray();
        AddOptionalPackageCountIssue(worksheetPart, description, "count", dataRefs.Attribute("count")?.Value, references.Length, issues);

        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references.Select((element, index) => new WorksheetDataRefReference(index + 1, element)))
        {
            AddWorksheetDataRefIssues(worksheetPart, description, reference, seenReferences, issues);
        }
    }

    private static void AddWorksheetDataRefIssues(
        string worksheetPart,
        string dataRefsDescription,
        WorksheetDataRefReference dataRefReference,
        HashSet<string> seenReferences,
        List<string> issues)
    {
        var dataRef = dataRefReference.Element;
        var description = $"{dataRefsDescription} dataRef #{dataRefReference.Ordinal}";
        var reference = dataRef.Attribute("ref")?.Value;
        var name = dataRef.Attribute("name")?.Value;
        var sheet = dataRef.Attribute("sheet")?.Value;

        if (string.IsNullOrWhiteSpace(reference) && string.IsNullOrWhiteSpace(name))
        {
            issues.Add($"{worksheetPart} {description} has no ref reference or name");
        }
        else if (!string.IsNullOrWhiteSpace(reference) && !IsValidLocalWorksheetReference(reference))
        {
            issues.Add($"{worksheetPart} {description} has invalid local ref reference '{reference}'");
        }

        if (dataRef.Attribute("sheet") is not null && string.IsNullOrWhiteSpace(sheet))
            issues.Add($"{worksheetPart} {description} has blank sheet attribute");

        if (dataRef.Attribute("name") is not null && string.IsNullOrWhiteSpace(name))
            issues.Add($"{worksheetPart} {description} has blank name attribute");

        var normalizedKey = $"{sheet?.Trim() ?? string.Empty}|{reference?.Trim() ?? string.Empty}|{name?.Trim() ?? string.Empty}";
        if (normalizedKey != "||" && !seenReferences.Add(normalizedKey))
            issues.Add($"{worksheetPart} {dataRefsDescription} has duplicate dataRef '{normalizedKey}'");

        if (dataRef.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddOptionalWorksheetMetadataBooleanIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || IsValidPackageBoolean(value))
            return;

        issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void AddOptionalNonNegativePackageIntIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!TryParseNonNegativePackageInt(value, out _))
            issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void AddOptionalPackageCountIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        int actualCount,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!TryParseNonNegativePackageInt(value, out var declaredCount))
        {
            issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
            return;
        }

        if (declaredCount != actualCount)
            issues.Add($"{worksheetPart} {description} {attributeName} is {declaredCount}, but contains {actualCount} entries");
    }

    private static void ThrowInvalidWorksheetSortAndDataConsolidationMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet sort/data-consolidation metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetConditionalFormattingMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();
        var differentialStyleCount = GetPackageDifferentialStyleCount(archive);

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetConditionalFormattingMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                differentialStyleCount,
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetConditionalFormattingMetadata(label, sourcePath, issues);
    }

    private static int? GetPackageDifferentialStyleCount(ZipArchive archive)
    {
        var stylesEntry = FindPackageEntry(archive, "xl/styles.xml");
        if (stylesEntry is null)
            return null;

        var stylesXml = LoadPackageXml(stylesEntry);
        if (stylesXml.Root?.Name != SpreadsheetNs + "styleSheet")
            return null;

        return stylesXml.Root.Element(SpreadsheetNs + "dxfs")?.Elements(SpreadsheetNs + "dxf").Count() ?? 0;
    }

    private static void AddWorksheetConditionalFormattingMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        int? differentialStyleCount,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        foreach (var conditionalFormatting in root
                     .Elements(SpreadsheetNs + "conditionalFormatting")
                     .Select((element, index) => new WorksheetConditionalFormattingReference(index + 1, element)))
        {
            AddWorksheetConditionalFormattingIssues(worksheetPart, root, conditionalFormatting, differentialStyleCount, issues);
        }
    }

    private static void AddWorksheetConditionalFormattingIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetConditionalFormattingReference conditionalFormattingReference,
        int? differentialStyleCount,
        List<string> issues)
    {
        var conditionalFormatting = conditionalFormattingReference.Element;
        var description = $"conditionalFormatting #{conditionalFormattingReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            conditionalFormatting,
            description,
            [
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);
        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            conditionalFormatting,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr"
            ],
            issues);

        var sqref = conditionalFormatting.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(sqref))
        {
            issues.Add($"{worksheetPart} {description} has no sqref");
        }
        else if (!IsValidPackageSqref(sqref))
        {
            issues.Add($"{worksheetPart} {description} has invalid local sqref '{sqref}'");
        }

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "pivot", conditionalFormatting.Attribute("pivot")?.Value, issues);

        foreach (var unexpectedChild in conditionalFormatting.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "cfRule" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var rules = conditionalFormatting.Elements(SpreadsheetNs + "cfRule").ToArray();
        if (rules.Length == 0)
            issues.Add($"{worksheetPart} {description} has no cfRule entries");

        var extensionLists = conditionalFormatting.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
        }

        AddWorksheetConditionalFormattingChildOrderingIssues(worksheetPart, description, conditionalFormatting, issues);

        foreach (var rule in rules.Select((element, index) => new WorksheetConditionalFormattingRuleReference(index + 1, element)))
        {
            AddWorksheetConditionalFormattingRuleIssues(worksheetPart, description, rule, differentialStyleCount, issues);
        }
    }

    private static void AddWorksheetConditionalFormattingChildOrderingIssues(
        string worksheetPart,
        string conditionalFormattingDescription,
        XElement conditionalFormatting,
        List<string> issues)
    {
        var children = conditionalFormatting.Elements().ToArray();
        var firstExtensionListIndex = Array.FindIndex(children, element => element.Name == SpreadsheetNs + "extLst");
        if (firstExtensionListIndex >= 0 &&
            children
                .Skip(firstExtensionListIndex + 1)
                .Any(element => element.Name == SpreadsheetNs + "cfRule"))
        {
            issues.Add($"{worksheetPart} {conditionalFormattingDescription} has cfRule entries after extLst; expected extLst last");
        }
    }

    private static void AddWorksheetConditionalFormattingRuleIssues(
        string worksheetPart,
        string conditionalFormattingDescription,
        WorksheetConditionalFormattingRuleReference ruleReference,
        int? differentialStyleCount,
        List<string> issues)
    {
        var rule = ruleReference.Element;
        var description = $"{conditionalFormattingDescription} cfRule #{ruleReference.Ordinal}";
        var type = rule.Attribute("type")?.Value;
        if (string.IsNullOrWhiteSpace(type))
            issues.Add($"{worksheetPart} {description} has no type");

        AddRequiredPositivePackageIntIssue(worksheetPart, description, "priority", rule.Attribute("priority")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "stopIfTrue", rule.Attribute("stopIfTrue")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "aboveAverage", rule.Attribute("aboveAverage")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "bottom", rule.Attribute("bottom")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "percent", rule.Attribute("percent")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "equalAverage", rule.Attribute("equalAverage")?.Value, issues);
        AddOptionalPositivePackageIntIssue(worksheetPart, description, "rank", rule.Attribute("rank")?.Value, issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "operator",
            rule.Attribute("operator")?.Value,
            ["lessThan", "lessThanOrEqual", "equal", "notEqual", "greaterThanOrEqual", "greaterThan", "between", "notBetween", "containsText", "notContains", "beginsWith", "endsWith"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "timePeriod",
            rule.Attribute("timePeriod")?.Value,
            ["yesterday", "today", "tomorrow", "last7Days", "thisMonth", "lastMonth", "nextMonth", "thisWeek", "lastWeek", "nextWeek"],
            issues);
        AddWorksheetConditionalFormattingDxfReferenceIssues(
            worksheetPart,
            description,
            rule.Attribute("dxfId")?.Value,
            differentialStyleCount,
            issues);

        foreach (var unexpectedChild in rule.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "formula" &&
                     element.Name != SpreadsheetNs + "colorScale" &&
                     element.Name != SpreadsheetNs + "dataBar" &&
                     element.Name != SpreadsheetNs + "iconSet" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var payloadElements = rule.Elements()
            .Where(element =>
                element.Name == SpreadsheetNs + "colorScale" ||
                element.Name == SpreadsheetNs + "dataBar" ||
                element.Name == SpreadsheetNs + "iconSet")
            .ToArray();
        if (payloadElements.Length > 1)
            issues.Add($"{worksheetPart} {description} has {payloadElements.Length} conditional-format payload elements; expected at most one");

        var extensionLists = rule.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        AddWorksheetConditionalFormattingRuleChildOrderingIssues(worksheetPart, description, rule, issues);

        foreach (var formula in rule.Elements(SpreadsheetNs + "formula").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetConditionalFormattingFormulaIssues(worksheetPart, description, formula.Ordinal, formula.Element, issues);
        }

        foreach (var colorScale in rule.Elements(SpreadsheetNs + "colorScale").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetColorScaleIssues(worksheetPart, description, colorScale.Ordinal, colorScale.Element, issues);
        }

        foreach (var dataBar in rule.Elements(SpreadsheetNs + "dataBar").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetDataBarIssues(worksheetPart, description, dataBar.Ordinal, dataBar.Element, issues);
        }

        foreach (var iconSet in rule.Elements(SpreadsheetNs + "iconSet").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetIconSetIssues(worksheetPart, description, iconSet.Ordinal, iconSet.Element, issues);
        }

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetConditionalFormattingDxfReferenceIssues(
        string worksheetPart,
        string description,
        string? dxfIdText,
        int? differentialStyleCount,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(dxfIdText))
            return;

        if (!TryParseNonNegativePackageInt(dxfIdText, out var dxfId))
        {
            issues.Add($"{worksheetPart} {description} has invalid dxfId value '{dxfIdText}'");
            return;
        }

        if (differentialStyleCount is null)
        {
            issues.Add($"{worksheetPart} {description} references dxfId {dxfId}, but xl/styles.xml is missing or invalid");
        }
        else if (dxfId >= differentialStyleCount)
        {
            issues.Add($"{worksheetPart} {description} references dxfId {dxfId}, but xl/styles.xml dxfs contains {differentialStyleCount.Value} entries");
        }
    }

    private static void AddWorksheetConditionalFormattingRuleChildOrderingIssues(
        string worksheetPart,
        string description,
        XElement rule,
        List<string> issues)
    {
        var children = rule.Elements().ToArray();
        var firstPayloadIndex = Array.FindIndex(children, element =>
            element.Name == SpreadsheetNs + "colorScale" ||
            element.Name == SpreadsheetNs + "dataBar" ||
            element.Name == SpreadsheetNs + "iconSet");
        if (firstPayloadIndex >= 0 &&
            children
                .Skip(firstPayloadIndex + 1)
                .Any(element => element.Name == SpreadsheetNs + "formula"))
        {
            issues.Add($"{worksheetPart} {description} has formula after a conditional-format payload; expected formulas before payloads");
        }

        var firstExtensionListIndex = Array.FindIndex(children, element => element.Name == SpreadsheetNs + "extLst");
        if (firstExtensionListIndex >= 0 &&
            children
                .Skip(firstExtensionListIndex + 1)
                .Any(element =>
                    element.Name == SpreadsheetNs + "formula" ||
                    element.Name == SpreadsheetNs + "colorScale" ||
                    element.Name == SpreadsheetNs + "dataBar" ||
                    element.Name == SpreadsheetNs + "iconSet"))
        {
            issues.Add($"{worksheetPart} {description} has child payloads after extLst; expected extLst last");
        }
    }

    private static void AddWorksheetConditionalFormattingFormulaIssues(
        string worksheetPart,
        string ruleDescription,
        int ordinal,
        XElement formula,
        List<string> issues)
    {
        var description = $"{ruleDescription} formula #{ordinal}";
        if (formula.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            issues.Add($"{worksheetPart} {description} has attributes; expected formula text only");

        if (formula.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected formula text only");
    }

    private static void AddWorksheetColorScaleIssues(
        string worksheetPart,
        string ruleDescription,
        int ordinal,
        XElement colorScale,
        List<string> issues)
    {
        var description = $"{ruleDescription} colorScale #{ordinal}";
        if (colorScale.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            issues.Add($"{worksheetPart} {description} has attributes; expected cfvo/color children only");

        foreach (var unexpectedChild in colorScale.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "cfvo" &&
                     element.Name != SpreadsheetNs + "color" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var thresholds = colorScale.Elements(SpreadsheetNs + "cfvo").ToArray();
        if (thresholds.Length is < 2 or > 3)
            issues.Add($"{worksheetPart} {description} has {thresholds.Length} cfvo entries; expected 2 or 3");

        var colors = colorScale.Elements(SpreadsheetNs + "color").ToArray();
        if (colors.Length != thresholds.Length)
            issues.Add($"{worksheetPart} {description} has {colors.Length} color entries for {thresholds.Length} cfvo entries");

        AddWorksheetConditionalFormattingPayloadOrderingIssues(worksheetPart, description, colorScale, "cfvo", "color", issues);
        foreach (var threshold in thresholds.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetConditionalFormattingThresholdIssues(worksheetPart, description, threshold.Ordinal, threshold.Element, issues);
        }

        foreach (var color in colors.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetConditionalFormattingColorIssues(worksheetPart, description, $"color #{color.Ordinal}", color.Element, issues);
        }

        AddWorksheetConditionalFormattingPayloadExtensionListIssues(worksheetPart, description, colorScale, issues);
    }

    private static void AddWorksheetDataBarIssues(
        string worksheetPart,
        string ruleDescription,
        int ordinal,
        XElement dataBar,
        List<string> issues)
    {
        var description = $"{ruleDescription} dataBar #{ordinal}";
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "showValue", dataBar.Attribute("showValue")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "border", dataBar.Attribute("border")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "minLength", dataBar.Attribute("minLength")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "maxLength", dataBar.Attribute("maxLength")?.Value, issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "axisPosition",
            dataBar.Attribute("axisPosition")?.Value,
            ["automatic", "middle", "none"],
            issues);

        foreach (var unexpectedChild in dataBar.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "cfvo" &&
                     element.Name != SpreadsheetNs + "color" &&
                     element.Name != SpreadsheetNs + "negativeFillColor" &&
                     element.Name != SpreadsheetNs + "negativeBorderColor" &&
                     element.Name != SpreadsheetNs + "axisColor" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var thresholds = dataBar.Elements(SpreadsheetNs + "cfvo").ToArray();
        if (thresholds.Length != 2)
            issues.Add($"{worksheetPart} {description} has {thresholds.Length} cfvo entries; expected 2");

        var colors = dataBar.Elements(SpreadsheetNs + "color").ToArray();
        if (colors.Length != 1)
            issues.Add($"{worksheetPart} {description} has {colors.Length} color entries; expected 1");

        AddWorksheetConditionalFormattingPayloadOrderingIssues(worksheetPart, description, dataBar, "cfvo", "color", issues);
        foreach (var threshold in thresholds.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetConditionalFormattingThresholdIssues(worksheetPart, description, threshold.Ordinal, threshold.Element, issues);
        }

        foreach (var color in colors.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetConditionalFormattingColorIssues(worksheetPart, description, $"color #{color.Ordinal}", color.Element, issues);
        }

        foreach (var nativeColor in dataBar
                     .Elements()
                     .Where(element =>
                         element.Name == SpreadsheetNs + "negativeFillColor" ||
                         element.Name == SpreadsheetNs + "negativeBorderColor" ||
                         element.Name == SpreadsheetNs + "axisColor")
                     .Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetConditionalFormattingColorIssues(worksheetPart, description, $"{nativeColor.Element.Name.LocalName} #{nativeColor.Ordinal}", nativeColor.Element, issues);
        }

        AddWorksheetConditionalFormattingPayloadExtensionListIssues(worksheetPart, description, dataBar, issues);
    }

    private static void AddWorksheetIconSetIssues(
        string worksheetPart,
        string ruleDescription,
        int ordinal,
        XElement iconSet,
        List<string> issues)
    {
        var description = $"{ruleDescription} iconSet #{ordinal}";
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "showValue", iconSet.Attribute("showValue")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "reverse", iconSet.Attribute("reverse")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "percent", iconSet.Attribute("percent")?.Value, issues);

        foreach (var unexpectedChild in iconSet.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "cfvo" &&
                     element.Name != SpreadsheetNs + "cfIcon" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var thresholds = iconSet.Elements(SpreadsheetNs + "cfvo").ToArray();
        if (thresholds.Length == 0)
            issues.Add($"{worksheetPart} {description} has no cfvo entries");

        AddWorksheetConditionalFormattingPayloadOrderingIssues(worksheetPart, description, iconSet, "cfvo", "cfIcon", issues);
        foreach (var threshold in thresholds.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetConditionalFormattingThresholdIssues(worksheetPart, description, threshold.Ordinal, threshold.Element, issues);
        }

        foreach (var icon in iconSet.Elements(SpreadsheetNs + "cfIcon").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetConditionalFormattingIconIssues(worksheetPart, description, icon.Ordinal, icon.Element, issues);
        }

        AddWorksheetConditionalFormattingPayloadExtensionListIssues(worksheetPart, description, iconSet, issues);
    }

    private static void AddWorksheetConditionalFormattingPayloadExtensionListIssues(
        string worksheetPart,
        string payloadDescription,
        XElement payload,
        List<string> issues)
    {
        var extensionLists = payload.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {payloadDescription} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(worksheetPart, payloadDescription, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetConditionalFormattingPayloadOrderingIssues(
        string worksheetPart,
        string description,
        XElement payload,
        string firstChildName,
        string laterChildName,
        List<string> issues)
    {
        var children = payload.Elements().ToArray();
        var firstLaterChildIndex = Array.FindIndex(children, element => element.Name == SpreadsheetNs + laterChildName);
        if (firstLaterChildIndex >= 0 &&
            children
                .Skip(firstLaterChildIndex + 1)
                .Any(element => element.Name == SpreadsheetNs + firstChildName))
        {
            issues.Add($"{worksheetPart} {description} has {firstChildName} entries after {laterChildName}; expected {firstChildName} before {laterChildName}");
        }

        var firstExtensionListIndex = Array.FindIndex(children, element => element.Name == SpreadsheetNs + "extLst");
        if (firstExtensionListIndex >= 0 &&
            children
                .Skip(firstExtensionListIndex + 1)
                .Any(element => element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has child entries after extLst; expected extLst last");
        }
    }

    private static void AddWorksheetConditionalFormattingThresholdIssues(
        string worksheetPart,
        string payloadDescription,
        int ordinal,
        XElement threshold,
        List<string> issues)
    {
        var description = $"{payloadDescription} cfvo #{ordinal}";
        var type = threshold.Attribute("type")?.Value;
        if (string.IsNullOrWhiteSpace(type))
        {
            issues.Add($"{worksheetPart} {description} has no type");
        }
        else
        {
            AddOptionalKnownWorksheetMetadataValueIssue(
                worksheetPart,
                description,
                "type",
                type,
                ["num", "percent", "max", "min", "formula", "percentile", "autoMin", "autoMax"],
                issues);
        }

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "gte", threshold.Attribute("gte")?.Value, issues);

        if (threshold.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetConditionalFormattingColorIssues(
        string worksheetPart,
        string payloadDescription,
        string colorDescription,
        XElement color,
        List<string> issues)
    {
        var description = $"{payloadDescription} {colorDescription}";
        var hasKnownColorAttribute =
            color.Attribute("rgb") is not null ||
            color.Attribute("indexed") is not null ||
            color.Attribute("theme") is not null ||
            color.Attribute("auto") is not null;
        if (!hasKnownColorAttribute)
            issues.Add($"{worksheetPart} {description} has no color attribute");

        var rgb = color.Attribute("rgb")?.Value;
        if (!string.IsNullOrWhiteSpace(rgb) && !IsValidPackageHexColor(rgb))
            issues.Add($"{worksheetPart} {description} has invalid rgb value '{rgb}'");

        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "indexed", color.Attribute("indexed")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "theme", color.Attribute("theme")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "auto", color.Attribute("auto")?.Value, issues);

        if (color.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetConditionalFormattingIconIssues(
        string worksheetPart,
        string iconSetDescription,
        int ordinal,
        XElement icon,
        List<string> issues)
    {
        var description = $"{iconSetDescription} cfIcon #{ordinal}";
        if (icon.Attribute("iconSet") is not null && string.IsNullOrWhiteSpace(icon.Attribute("iconSet")?.Value))
            issues.Add($"{worksheetPart} {description} has blank iconSet");

        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "iconId", icon.Attribute("iconId")?.Value, issues);

        if (icon.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddRequiredPositivePackageIntIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{worksheetPart} {description} has no {attributeName}");
            return;
        }

        if (!TryParseNonNegativePackageInt(value, out var parsedValue) || parsedValue <= 0)
            issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void AddOptionalPositivePackageIntIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!TryParseNonNegativePackageInt(value, out var parsedValue) || parsedValue <= 0)
            issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
    }

    private static bool IsValidPackageHexColor(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length is 6 or 8 &&
            trimmed.All(character =>
                character is >= '0' and <= '9' ||
                character is >= 'A' and <= 'F' ||
                character is >= 'a' and <= 'f');
    }

    private static void ThrowInvalidWorksheetConditionalFormattingMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet conditionalFormatting metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetDataValidationMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetDataValidationMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetDataValidationMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetDataValidationMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var dataValidationsContainers = root.Elements(SpreadsheetNs + "dataValidations").ToArray();
        if (dataValidationsContainers.Length > 1)
            issues.Add($"{worksheetPart} has {dataValidationsContainers.Length} dataValidations elements; expected at most one");

        foreach (var dataValidations in dataValidationsContainers.Select((element, index) => new WorksheetDataValidationsReference(index + 1, element)))
        {
            AddWorksheetDataValidationsIssues(worksheetPart, root, dataValidations, issues);
        }
    }

    private static void AddWorksheetDataValidationsIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetDataValidationsReference dataValidationsReference,
        List<string> issues)
    {
        var dataValidations = dataValidationsReference.Element;
        var description = $"dataValidations #{dataValidationsReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            dataValidations,
            description,
            [
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);
        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            dataValidations,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting"
            ],
            issues);

        var validations = dataValidations.Elements(SpreadsheetNs + "dataValidation").ToArray();
        AddOptionalPackageCountIssue(worksheetPart, description, "count", dataValidations.Attribute("count")?.Value, validations.Length, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "disablePrompts", dataValidations.Attribute("disablePrompts")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "xWindow", dataValidations.Attribute("xWindow")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "yWindow", dataValidations.Attribute("yWindow")?.Value, issues);

        foreach (var unexpectedChild in dataValidations.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "dataValidation" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var extensionLists = dataValidations.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
        }

        AddWorksheetDataValidationChildOrderingIssues(worksheetPart, description, dataValidations, issues);

        var seenSqrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dataValidation in validations.Select((element, index) => new WorksheetDataValidationReference(index + 1, element)))
        {
            AddWorksheetDataValidationIssues(worksheetPart, description, dataValidation, seenSqrefs, issues);
        }
    }

    private static void AddWorksheetDataValidationChildOrderingIssues(
        string worksheetPart,
        string containerDescription,
        XElement dataValidations,
        List<string> issues)
    {
        var children = dataValidations.Elements().ToArray();
        var firstExtensionListIndex = Array.FindIndex(children, element => element.Name == SpreadsheetNs + "extLst");
        if (firstExtensionListIndex < 0)
            return;

        if (children
            .Skip(firstExtensionListIndex + 1)
            .Any(element => element.Name == SpreadsheetNs + "dataValidation"))
        {
            issues.Add($"{worksheetPart} {containerDescription} has dataValidation entries after extLst; expected extLst last");
        }
    }

    private static void AddWorksheetDataValidationIssues(
        string worksheetPart,
        string containerDescription,
        WorksheetDataValidationReference dataValidationReference,
        HashSet<string> seenSqrefs,
        List<string> issues)
    {
        var dataValidation = dataValidationReference.Element;
        var description = $"{containerDescription} dataValidation #{dataValidationReference.Ordinal}";
        var sqref = dataValidation.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(sqref))
        {
            issues.Add($"{worksheetPart} {description} has no sqref");
        }
        else if (!IsValidPackageSqref(sqref))
        {
            issues.Add($"{worksheetPart} {description} has invalid local sqref '{sqref}'");
        }
        else if (!seenSqrefs.Add(NormalizePackageSqref(sqref)))
        {
            issues.Add($"{worksheetPart} {containerDescription} has duplicate dataValidation sqref '{sqref}'");
        }

        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "type",
            dataValidation.Attribute("type")?.Value,
            ["none", "whole", "decimal", "list", "date", "time", "textLength", "custom"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "operator",
            dataValidation.Attribute("operator")?.Value,
            ["between", "notBetween", "equal", "notEqual", "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "errorStyle",
            dataValidation.Attribute("errorStyle")?.Value,
            ["stop", "warning", "information"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "imeMode",
            dataValidation.Attribute("imeMode")?.Value,
            ["noControl", "off", "on", "disabled", "hiragana", "fullKatakana", "halfKatakana", "fullAlpha", "halfAlpha", "fullHangul", "halfHangul"],
            issues);

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "allowBlank", dataValidation.Attribute("allowBlank")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "showDropDown", dataValidation.Attribute("showDropDown")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "showInputMessage", dataValidation.Attribute("showInputMessage")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "showErrorMessage", dataValidation.Attribute("showErrorMessage")?.Value, issues);

        foreach (var unexpectedChild in dataValidation.Elements().Where(element =>
                     element.Name != SpreadsheetNs + "formula1" &&
                     element.Name != SpreadsheetNs + "formula2" &&
                     element.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var formula1Elements = dataValidation.Elements(SpreadsheetNs + "formula1").ToArray();
        if (formula1Elements.Length > 1)
            issues.Add($"{worksheetPart} {description} has {formula1Elements.Length} formula1 elements; expected at most one");

        var formula2Elements = dataValidation.Elements(SpreadsheetNs + "formula2").ToArray();
        if (formula2Elements.Length > 1)
            issues.Add($"{worksheetPart} {description} has {formula2Elements.Length} formula2 elements; expected at most one");

        var extensionLists = dataValidation.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        AddWorksheetDataValidationFormulaSlotIssues(worksheetPart, description, dataValidation, issues);

        foreach (var formula1 in formula1Elements.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetDataValidationFormulaIssues(worksheetPart, description, "formula1", formula1.Ordinal, formula1.Element, issues);
        }

        foreach (var formula2 in formula2Elements.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetDataValidationFormulaIssues(worksheetPart, description, "formula2", formula2.Ordinal, formula2.Element, issues);
        }

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(worksheetPart, description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetDataValidationFormulaSlotIssues(
        string worksheetPart,
        string description,
        XElement dataValidation,
        List<string> issues)
    {
        var children = dataValidation.Elements().ToArray();
        var firstFormula2Index = Array.FindIndex(children, element => element.Name == SpreadsheetNs + "formula2");
        if (firstFormula2Index >= 0 &&
            children
                .Skip(firstFormula2Index + 1)
                .Any(element => element.Name == SpreadsheetNs + "formula1"))
        {
            issues.Add($"{worksheetPart} {description} has formula1 after formula2; expected formula1 before formula2");
        }

        var firstExtensionListIndex = Array.FindIndex(children, element => element.Name == SpreadsheetNs + "extLst");
        if (firstExtensionListIndex >= 0 &&
            children
                .Skip(firstExtensionListIndex + 1)
                .Any(element => element.Name == SpreadsheetNs + "formula1" || element.Name == SpreadsheetNs + "formula2"))
        {
            issues.Add($"{worksheetPart} {description} has formula elements after extLst; expected extLst last");
        }
    }

    private static void AddWorksheetDataValidationFormulaIssues(
        string worksheetPart,
        string dataValidationDescription,
        string elementName,
        int ordinal,
        XElement formula,
        List<string> issues)
    {
        var description = $"{dataValidationDescription} {elementName} #{ordinal}";
        if (formula.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            issues.Add($"{worksheetPart} {description} has attributes; expected formula text only");

        if (formula.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected formula text only");
    }

    private static void ThrowInvalidWorksheetDataValidationMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet dataValidations metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetPrintOptionsMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetPrintOptionsMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetPrintOptionsMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetPrintOptionsMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var printOptionsElements = root.Elements(SpreadsheetNs + "printOptions").ToArray();
        if (printOptionsElements.Length > 1)
            issues.Add($"{worksheetPart} has {printOptionsElements.Length} printOptions elements; expected at most one");

        foreach (var printOptions in printOptionsElements.Select((element, index) => new WorksheetPrintOptionsReference(index + 1, element)))
        {
            AddWorksheetPrintOptionsIssues(worksheetPart, root, printOptions, issues);
        }
    }

    private static void AddWorksheetPrintOptionsIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetPrintOptionsReference printOptionsReference,
        List<string> issues)
    {
        var printOptions = printOptionsReference.Element;
        var description = $"printOptions #{printOptionsReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            printOptions,
            description,
            [
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            printOptions,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks"
            ],
            issues);

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "horizontalCentered", printOptions.Attribute("horizontalCentered")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "verticalCentered", printOptions.Attribute("verticalCentered")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "headings", printOptions.Attribute("headings")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "gridLines", printOptions.Attribute("gridLines")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "gridLinesSet", printOptions.Attribute("gridLinesSet")?.Value, issues);

        if (printOptions.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetMetadataPreviousOrderingIssues(
        string worksheetPart,
        XElement worksheetRoot,
        XElement metadataElement,
        string description,
        IReadOnlyCollection<string> earlierWorksheetElements,
        List<string> issues)
    {
        var worksheetChildren = worksheetRoot.Elements().ToArray();
        var metadataIndex = Array.IndexOf(worksheetChildren, metadataElement);
        if (metadataIndex < 0)
            return;

        foreach (var laterEarlierElement in worksheetChildren
                     .Skip(metadataIndex + 1)
                     .Where(element =>
                         element.Name.Namespace == SpreadsheetNs &&
                         earlierWorksheetElements.Contains(element.Name.LocalName)))
        {
            issues.Add($"{worksheetPart} {description} appears before {laterEarlierElement.Name.LocalName}; expected schema order after that element");
        }
    }

    private static void ThrowInvalidWorksheetPrintOptionsMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet printOptions metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetPageMarginsMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetPageMarginsMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetPageMarginsMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetPageMarginsMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var pageMarginsElements = root.Elements(SpreadsheetNs + "pageMargins").ToArray();
        if (pageMarginsElements.Length > 1)
            issues.Add($"{worksheetPart} has {pageMarginsElements.Length} pageMargins elements; expected at most one");

        foreach (var pageMargins in pageMarginsElements.Select((element, index) => new WorksheetPageMarginsReference(index + 1, element)))
        {
            AddWorksheetPageMarginsIssues(worksheetPart, root, pageMargins, issues);
        }
    }

    private static void AddWorksheetPageMarginsIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetPageMarginsReference pageMarginsReference,
        List<string> issues)
    {
        var pageMargins = pageMarginsReference.Element;
        var description = $"pageMargins #{pageMarginsReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            pageMargins,
            description,
            [
                "pageSetup",
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            pageMargins,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions"
            ],
            issues);

        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "left", pageMargins.Attribute("left")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "right", pageMargins.Attribute("right")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "top", pageMargins.Attribute("top")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "bottom", pageMargins.Attribute("bottom")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "header", pageMargins.Attribute("header")?.Value, issues);
        AddOptionalNonNegativePackageDecimalIssue(worksheetPart, description, "footer", pageMargins.Attribute("footer")?.Value, issues);

        if (pageMargins.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void ThrowInvalidWorksheetPageMarginsMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet pageMargins metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetPageSetupMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetPageSetupMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetPageSetupMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetPageSetupMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var pageSetupElements = root.Elements(SpreadsheetNs + "pageSetup").ToArray();
        if (pageSetupElements.Length > 1)
            issues.Add($"{worksheetPart} has {pageSetupElements.Length} pageSetup elements; expected at most one");

        foreach (var pageSetup in pageSetupElements.Select((element, index) => new WorksheetPageSetupReference(index + 1, element)))
        {
            AddWorksheetPageSetupIssues(worksheetPart, root, pageSetup, issues);
        }
    }

    private static void AddWorksheetPageSetupIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetPageSetupReference pageSetupReference,
        List<string> issues)
    {
        var pageSetup = pageSetupReference.Element;
        var description = $"pageSetup #{pageSetupReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            pageSetup,
            description,
            [
                "headerFooter",
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            pageSetup,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins"
            ],
            issues);

        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "paperSize", pageSetup.Attribute("paperSize")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "scale", pageSetup.Attribute("scale")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "firstPageNumber", pageSetup.Attribute("firstPageNumber")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "fitToWidth", pageSetup.Attribute("fitToWidth")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "fitToHeight", pageSetup.Attribute("fitToHeight")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "horizontalDpi", pageSetup.Attribute("horizontalDpi")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "verticalDpi", pageSetup.Attribute("verticalDpi")?.Value, issues);
        AddOptionalNonNegativePackageIntIssue(worksheetPart, description, "copies", pageSetup.Attribute("copies")?.Value, issues);

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "usePrinterDefaults", pageSetup.Attribute("usePrinterDefaults")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "blackAndWhite", pageSetup.Attribute("blackAndWhite")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "draft", pageSetup.Attribute("draft")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "useFirstPageNumber", pageSetup.Attribute("useFirstPageNumber")?.Value, issues);

        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "pageOrder",
            pageSetup.Attribute("pageOrder")?.Value,
            ["downThenOver", "overThenDown"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "orientation",
            pageSetup.Attribute("orientation")?.Value,
            ["default", "portrait", "landscape"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "cellComments",
            pageSetup.Attribute("cellComments")?.Value,
            ["none", "asDisplayed", "atEnd"],
            issues);
        AddOptionalKnownWorksheetMetadataValueIssue(
            worksheetPart,
            description,
            "errors",
            pageSetup.Attribute("errors")?.Value,
            ["displayed", "blank", "dash", "NA"],
            issues);

        if (pageSetup.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddOptionalKnownWorksheetMetadataValueIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        IReadOnlyCollection<string> knownValues,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            knownValues.Contains(value.Trim(), StringComparer.Ordinal))
        {
            return;
        }

        issues.Add($"{worksheetPart} {description} has unknown {attributeName} value '{value}'");
    }

    private static void ThrowInvalidWorksheetPageSetupMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet pageSetup metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetHeaderFooterMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetHeaderFooterMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetHeaderFooterMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetHeaderFooterMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var headerFooterElements = root.Elements(SpreadsheetNs + "headerFooter").ToArray();
        if (headerFooterElements.Length > 1)
            issues.Add($"{worksheetPart} has {headerFooterElements.Length} headerFooter elements; expected at most one");

        foreach (var headerFooter in headerFooterElements.Select((element, index) => new WorksheetHeaderFooterReference(index + 1, element)))
        {
            AddWorksheetHeaderFooterIssues(worksheetPart, root, headerFooter, issues);
        }
    }

    private static void AddWorksheetHeaderFooterIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetHeaderFooterReference headerFooterReference,
        List<string> issues)
    {
        var headerFooter = headerFooterReference.Element;
        var description = $"headerFooter #{headerFooterReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            headerFooter,
            description,
            [
                "rowBreaks",
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            headerFooter,
            description,
            [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup"
            ],
            issues);

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "differentOddEven", headerFooter.Attribute("differentOddEven")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "differentFirst", headerFooter.Attribute("differentFirst")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "scaleWithDoc", headerFooter.Attribute("scaleWithDoc")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "alignWithMargins", headerFooter.Attribute("alignWithMargins")?.Value, issues);

        var seenChildNames = new HashSet<string>(StringComparer.Ordinal);
        var previousKnownChildOrder = -1;
        foreach (var child in headerFooter.Elements())
        {
            if (child.Name.Namespace != SpreadsheetNs || !IsKnownWorksheetHeaderFooterChild(child.Name.LocalName))
            {
                issues.Add($"{worksheetPart} {description} has unexpected child element {child.Name.LocalName}");
                continue;
            }

            if (!seenChildNames.Add(child.Name.LocalName))
                issues.Add($"{worksheetPart} {description} has duplicate {child.Name.LocalName} elements");

            var childOrder = GetWorksheetHeaderFooterChildOrder(child.Name.LocalName);
            if (childOrder < previousKnownChildOrder)
                issues.Add($"{worksheetPart} {description} child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;

            if (child.Elements().Any())
                issues.Add($"{worksheetPart} {description} child {child.Name.LocalName} has child elements; expected text only");
        }
    }

    private static bool IsKnownWorksheetHeaderFooterChild(string name) =>
        GetWorksheetHeaderFooterChildOrder(name) >= 0;

    private static int GetWorksheetHeaderFooterChildOrder(string name) =>
        name switch
        {
            "oddHeader" => 0,
            "oddFooter" => 1,
            "evenHeader" => 2,
            "evenFooter" => 3,
            "firstHeader" => 4,
            "firstFooter" => 5,
            _ => -1
        };

    private static void ThrowInvalidWorksheetHeaderFooterMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet headerFooter metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetPageBreakMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetPageBreakMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetPageBreakMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetPageBreakMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var rowBreaksElements = root.Elements(SpreadsheetNs + "rowBreaks").ToArray();
        if (rowBreaksElements.Length > 1)
            issues.Add($"{worksheetPart} has {rowBreaksElements.Length} rowBreaks elements; expected at most one");

        foreach (var rowBreaks in rowBreaksElements.Select((element, index) => new WorksheetPageBreaksReference(
                     index + 1,
                     element,
                     "rowBreaks",
                     (int)CellAddress.MaxRow,
                     (int)CellAddress.MaxCol - 1)))
        {
            AddWorksheetPageBreaksIssues(worksheetPart, root, rowBreaks, issues);
        }

        var columnBreaksElements = root.Elements(SpreadsheetNs + "colBreaks").ToArray();
        if (columnBreaksElements.Length > 1)
            issues.Add($"{worksheetPart} has {columnBreaksElements.Length} colBreaks elements; expected at most one");

        foreach (var columnBreaks in columnBreaksElements.Select((element, index) => new WorksheetPageBreaksReference(
                     index + 1,
                     element,
                     "colBreaks",
                     (int)CellAddress.MaxCol,
                     (int)CellAddress.MaxRow - 1)))
        {
            AddWorksheetPageBreaksIssues(worksheetPart, root, columnBreaks, issues);
        }
    }

    private static void AddWorksheetPageBreaksIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetPageBreaksReference pageBreaksReference,
        List<string> issues)
    {
        var pageBreaks = pageBreaksReference.Element;
        var description = $"{pageBreaksReference.ElementName} #{pageBreaksReference.Ordinal}";
        string[] laterElements = string.Equals(pageBreaksReference.ElementName, "rowBreaks", StringComparison.Ordinal)
            ? [
                "colBreaks",
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ]
            : [
                "customProperties",
                "cellWatches",
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ];

        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            pageBreaks,
            description,
            laterElements,
            issues);

        string[] earlierElements = string.Equals(pageBreaksReference.ElementName, "colBreaks", StringComparison.Ordinal)
            ? [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter",
                "rowBreaks"
            ]
            : [
                "sheetPr",
                "dimension",
                "sheetViews",
                "sheetFormatPr",
                "cols",
                "sheetData",
                "sheetCalcPr",
                "sheetProtection",
                "protectedRanges",
                "scenarios",
                "autoFilter",
                "sortState",
                "dataConsolidate",
                "customSheetViews",
                "mergeCells",
                "phoneticPr",
                "conditionalFormatting",
                "dataValidations",
                "hyperlinks",
                "printOptions",
                "pageMargins",
                "pageSetup",
                "headerFooter"
            ];

        AddWorksheetMetadataPreviousOrderingIssues(
            worksheetPart,
            worksheetRoot,
            pageBreaks,
            description,
            earlierElements,
            issues);

        var breakElements = pageBreaks.Elements(SpreadsheetNs + "brk").ToArray();
        AddOptionalPackageCountIssue(worksheetPart, description, "count", pageBreaks.Attribute("count")?.Value, breakElements.Length, issues);
        AddOptionalWorksheetPageBreakManualCountIssue(worksheetPart, description, pageBreaks.Attribute("manualBreakCount")?.Value, breakElements.Length, issues);

        foreach (var unexpectedChild in pageBreaks.Elements().Where(element => element.Name != SpreadsheetNs + "brk"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var seenBreakIds = new HashSet<int>();
        foreach (var breakElement in breakElements.Select((element, index) => new WorksheetPageBreakReference(index + 1, element)))
        {
            AddWorksheetPageBreakIssues(
                worksheetPart,
                description,
                breakElement,
                pageBreaksReference.MaxBreakId,
                pageBreaksReference.MaxBreakSpan,
                seenBreakIds,
                issues);
        }
    }

    private static void AddOptionalWorksheetPageBreakManualCountIssue(
        string worksheetPart,
        string description,
        string? value,
        int breakCount,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!TryParseNonNegativePackageInt(value, out var manualBreakCount))
        {
            issues.Add($"{worksheetPart} {description} has invalid manualBreakCount value '{value}'");
            return;
        }

        if (manualBreakCount > breakCount)
            issues.Add($"{worksheetPart} {description} manualBreakCount is {manualBreakCount}, but contains {breakCount} brk entries");
    }

    private static void AddWorksheetPageBreakIssues(
        string worksheetPart,
        string pageBreaksDescription,
        WorksheetPageBreakReference pageBreakReference,
        int maxBreakId,
        int maxBreakSpan,
        HashSet<int> seenBreakIds,
        List<string> issues)
    {
        var breakElement = pageBreakReference.Element;
        var description = $"{pageBreaksDescription} brk #{pageBreakReference.Ordinal}";
        if (AddRequiredWorksheetPageBreakIntIssue(worksheetPart, description, "id", breakElement.Attribute("id")?.Value, 2, maxBreakId, issues, out var id) &&
            !seenBreakIds.Add(id))
        {
            issues.Add($"{worksheetPart} {pageBreaksDescription} has duplicate brk id {id}");
        }

        AddOptionalWorksheetPageBreakIntIssue(worksheetPart, description, "min", breakElement.Attribute("min")?.Value, 0, maxBreakSpan, issues, out var min);
        AddOptionalWorksheetPageBreakIntIssue(worksheetPart, description, "max", breakElement.Attribute("max")?.Value, 0, maxBreakSpan, issues, out var max);
        if (min is not null && max is not null && min > max)
            issues.Add($"{worksheetPart} {description} min value {min} is greater than max value {max}");

        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "man", breakElement.Attribute("man")?.Value, issues);
        AddOptionalWorksheetMetadataBooleanIssue(worksheetPart, description, "pt", breakElement.Attribute("pt")?.Value, issues);

        if (breakElement.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static bool AddRequiredWorksheetPageBreakIntIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        int minValue,
        int maxValue,
        List<string> issues,
        out int parsedValue)
    {
        parsedValue = -1;
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{worksheetPart} {description} has no {attributeName} attribute");
            return false;
        }

        return AddWorksheetPageBreakIntIssue(worksheetPart, description, attributeName, value, minValue, maxValue, issues, out parsedValue);
    }

    private static void AddOptionalWorksheetPageBreakIntIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        int minValue,
        int maxValue,
        List<string> issues,
        out int? parsedValue)
    {
        parsedValue = null;
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (AddWorksheetPageBreakIntIssue(worksheetPart, description, attributeName, value, minValue, maxValue, issues, out var parsed))
            parsedValue = parsed;
    }

    private static bool AddWorksheetPageBreakIntIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string value,
        int minValue,
        int maxValue,
        List<string> issues,
        out int parsedValue)
    {
        parsedValue = -1;
        if (!TryParseNonNegativePackageInt(value, out parsedValue))
        {
            issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
            return false;
        }

        if (parsedValue < minValue || parsedValue > maxValue)
        {
            issues.Add($"{worksheetPart} {description} {attributeName} value {parsedValue} is outside {minValue}-{maxValue}");
            return false;
        }

        return true;
    }

    private static void ThrowInvalidWorksheetPageBreakMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet page-break metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetDiagnosticMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetDiagnosticMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetDiagnosticMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetDiagnosticMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var cellWatchesContainers = root.Elements(SpreadsheetNs + "cellWatches").ToArray();
        if (cellWatchesContainers.Length > 1)
            issues.Add($"{worksheetPart} has {cellWatchesContainers.Length} cellWatches elements; expected at most one");

        foreach (var cellWatches in cellWatchesContainers.Select((element, index) => new WorksheetCellWatchesReference(index + 1, element)))
        {
            AddWorksheetCellWatchesIssues(worksheetPart, root, cellWatches, issues);
        }

        var ignoredErrorsContainers = root.Elements(SpreadsheetNs + "ignoredErrors").ToArray();
        if (ignoredErrorsContainers.Length > 1)
            issues.Add($"{worksheetPart} has {ignoredErrorsContainers.Length} ignoredErrors elements; expected at most one");

        foreach (var ignoredErrors in ignoredErrorsContainers.Select((element, index) => new WorksheetIgnoredErrorsReference(index + 1, element)))
        {
            AddWorksheetIgnoredErrorsIssues(worksheetPart, root, ignoredErrors, issues);
        }
    }

    private static void AddWorksheetCellWatchesIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetCellWatchesReference cellWatchesReference,
        List<string> issues)
    {
        var cellWatches = cellWatchesReference.Element;
        var description = $"cellWatches #{cellWatchesReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            cellWatches,
            description,
            [
                "ignoredErrors",
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        foreach (var unexpectedChild in cellWatches.Elements().Where(element => element.Name != SpreadsheetNs + "cellWatch"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var watches = cellWatches.Elements(SpreadsheetNs + "cellWatch").ToArray();
        if (watches.Length == 0)
            issues.Add($"{worksheetPart} {description} has no cellWatch entries");

        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var watch in watches.Select((element, index) => new WorksheetCellWatchReference(index + 1, element)))
        {
            AddWorksheetCellWatchIssues(worksheetPart, description, watch, seenReferences, issues);
        }
    }

    private static void AddWorksheetCellWatchIssues(
        string worksheetPart,
        string containerDescription,
        WorksheetCellWatchReference watchReference,
        HashSet<string> seenReferences,
        List<string> issues)
    {
        var watch = watchReference.Element;
        var description = $"{containerDescription} cellWatch #{watchReference.Ordinal}";
        var reference = watch.Attribute("r")?.Value;
        if (string.IsNullOrWhiteSpace(reference))
        {
            issues.Add($"{worksheetPart} {description} has no r reference");
        }
        else if (!IsValidLocalCellReference(reference))
        {
            issues.Add($"{worksheetPart} {description} has invalid local r reference '{reference}'");
        }
        else if (!seenReferences.Add(reference.Trim()))
        {
            issues.Add($"{worksheetPart} {containerDescription} has duplicate cellWatch r reference '{reference}'");
        }

        if (watch.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetIgnoredErrorsIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetIgnoredErrorsReference ignoredErrorsReference,
        List<string> issues)
    {
        var ignoredErrors = ignoredErrorsReference.Element;
        var description = $"ignoredErrors #{ignoredErrorsReference.Ordinal}";
        AddWorksheetMetadataOrderingIssues(
            worksheetPart,
            worksheetRoot,
            ignoredErrors,
            description,
            [
                "singleXmlCells",
                "smartTags",
                "drawing",
                "legacyDrawing",
                "legacyDrawingHF",
                "picture",
                "oleObjects",
                "controls",
                "webPublishItems",
                "tableParts",
                "extLst"
            ],
            issues);

        foreach (var unexpectedChild in ignoredErrors.Elements().Where(element => element.Name != SpreadsheetNs + "ignoredError"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var errors = ignoredErrors.Elements(SpreadsheetNs + "ignoredError").ToArray();
        if (errors.Length == 0)
            issues.Add($"{worksheetPart} {description} has no ignoredError entries");

        var seenSqrefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ignoredError in errors.Select((element, index) => new WorksheetIgnoredErrorReference(index + 1, element)))
        {
            AddWorksheetIgnoredErrorIssues(worksheetPart, description, ignoredError, seenSqrefs, issues);
        }
    }

    private static void AddWorksheetIgnoredErrorIssues(
        string worksheetPart,
        string containerDescription,
        WorksheetIgnoredErrorReference ignoredErrorReference,
        HashSet<string> seenSqrefs,
        List<string> issues)
    {
        var ignoredError = ignoredErrorReference.Element;
        var description = $"{containerDescription} ignoredError #{ignoredErrorReference.Ordinal}";
        var sqref = ignoredError.Attribute("sqref")?.Value;
        if (string.IsNullOrWhiteSpace(sqref))
        {
            issues.Add($"{worksheetPart} {description} has no sqref");
        }
        else if (!IsValidPackageSqref(sqref))
        {
            issues.Add($"{worksheetPart} {description} has invalid local sqref '{sqref}'");
        }
        else if (!seenSqrefs.Add(NormalizePackageSqref(sqref)))
        {
            issues.Add($"{worksheetPart} {containerDescription} has duplicate ignoredError sqref '{sqref}'");
        }

        foreach (var attribute in ignoredError.Attributes().Where(attribute => IsKnownIgnoredErrorFlag(attribute.Name.LocalName)))
        {
            var value = attribute.Value;
            if (!string.IsNullOrWhiteSpace(value) && !IsValidPackageBoolean(value))
                issues.Add($"{worksheetPart} {description} has invalid {attribute.Name.LocalName} value '{value}'");
        }

        if (ignoredError.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetMetadataOrderingIssues(
        string worksheetPart,
        XElement worksheetRoot,
        XElement metadataElement,
        string description,
        IReadOnlyCollection<string> laterWorksheetElements,
        List<string> issues)
    {
        var worksheetChildren = worksheetRoot.Elements().ToArray();
        var metadataIndex = Array.IndexOf(worksheetChildren, metadataElement);
        if (metadataIndex < 0)
            return;

        foreach (var earlierLaterElement in worksheetChildren
                     .Take(metadataIndex)
                     .Where(element =>
                         element.Name.Namespace == SpreadsheetNs &&
                         laterWorksheetElements.Contains(element.Name.LocalName)))
        {
            issues.Add($"{worksheetPart} {description} appears after {earlierLaterElement.Name.LocalName}; expected schema order before that element");
        }
    }

    private static bool IsValidPackageSqref(string sqref)
    {
        var tokens = sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length > 0 && tokens.All(IsValidLocalWorksheetReference);
    }

    private static string NormalizePackageSqref(string sqref) =>
        string.Join(" ", sqref.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool IsKnownIgnoredErrorFlag(string name) =>
        name is "numberStoredAsText" or
            "evalError" or
            "formula" or
            "formulaRange" or
            "unlockedFormula" or
            "emptyCellReference" or
            "listDataValidation" or
            "calculatedColumn" or
            "twoDigitTextYear";

    private static void ThrowInvalidWorksheetDiagnosticMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet diagnostic metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetSingleXmlCellsMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetSingleXmlCellsIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetSingleXmlCellsMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetSingleXmlCellsIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var containers = root.Elements(SpreadsheetNs + "singleXmlCells").ToArray();
        if (containers.Length > 1)
            issues.Add($"{worksheetPart} has {containers.Length} singleXmlCells elements; expected at most one");

        foreach (var container in containers.Select((element, index) => new WorksheetSingleXmlCellsReference(index + 1, element)))
        {
            AddWorksheetSingleXmlCellsContainerIssues(worksheetPart, root, container, issues);
        }
    }

    private static void AddWorksheetSingleXmlCellsContainerIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetSingleXmlCellsReference containerReference,
        List<string> issues)
    {
        var container = containerReference.Element;
        var description = $"singleXmlCells #{containerReference.Ordinal}";

        AddWorksheetSingleXmlCellsOrderingIssues(worksheetPart, worksheetRoot, container, description, issues);

        foreach (var unexpectedChild in container.Elements().Where(element => element.Name != SpreadsheetNs + "singleXmlCell"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var cells = container.Elements(SpreadsheetNs + "singleXmlCell").ToArray();
        if (cells.Length == 0)
            issues.Add($"{worksheetPart} {description} has no singleXmlCell entries");

        var seenIds = new HashSet<int>();
        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in cells.Select((element, index) => new WorksheetSingleXmlCellReference(index + 1, element)))
        {
            AddWorksheetSingleXmlCellIssues(worksheetPart, description, cell, issues);

            if (TryParseNonNegativePackageInt(cell.Element.Attribute("id")?.Value, out var id) && !seenIds.Add(id))
                issues.Add($"{worksheetPart} {description} has duplicate singleXmlCell id {id}");

            var reference = cell.Element.Attribute("r")?.Value;
            if (!string.IsNullOrWhiteSpace(reference) &&
                IsValidLocalCellReference(reference) &&
                !seenReferences.Add(reference.Trim()))
            {
                issues.Add($"{worksheetPart} {description} has duplicate singleXmlCell r reference '{reference}'");
            }
        }
    }

    private static void AddWorksheetSingleXmlCellsOrderingIssues(
        string worksheetPart,
        XElement worksheetRoot,
        XElement singleXmlCells,
        string description,
        List<string> issues)
    {
        string[] laterWorksheetElements =
        [
            "smartTags",
            "drawing",
            "legacyDrawing",
            "legacyDrawingHF",
            "picture",
            "oleObjects",
            "controls",
            "webPublishItems",
            "tableParts",
            "extLst"
        ];

        var worksheetChildren = worksheetRoot.Elements().ToArray();
        var singleXmlCellsIndex = Array.IndexOf(worksheetChildren, singleXmlCells);
        if (singleXmlCellsIndex < 0)
            return;

        foreach (var earlierLaterElement in worksheetChildren
                     .Take(singleXmlCellsIndex)
                     .Where(element =>
                         element.Name.Namespace == SpreadsheetNs &&
                         laterWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal)))
        {
            issues.Add($"{worksheetPart} {description} appears after {earlierLaterElement.Name.LocalName}; expected schema order before that element");
        }
    }

    private static void AddWorksheetSingleXmlCellIssues(
        string worksheetPart,
        string containerDescription,
        WorksheetSingleXmlCellReference cellReference,
        List<string> issues)
    {
        var cell = cellReference.Element;
        var description = $"{containerDescription} singleXmlCell #{cellReference.Ordinal}";

        AddRequiredNonNegativePackageIntIssue(worksheetPart, description, "id", cell.Attribute("id")?.Value, issues);

        var reference = cell.Attribute("r")?.Value;
        if (string.IsNullOrWhiteSpace(reference))
        {
            issues.Add($"{worksheetPart} {description} has no r reference");
        }
        else if (!IsValidLocalCellReference(reference))
        {
            issues.Add($"{worksheetPart} {description} has invalid local r reference '{reference}'");
        }

        AddRequiredNonNegativePackageIntIssue(worksheetPart, description, "xmlCellPrId", cell.Attribute("xmlCellPrId")?.Value, issues);

        if (cell.Elements().Any())
            issues.Add($"{worksheetPart} {description} has child elements; expected attributes only");
    }

    private static void AddRequiredNonNegativePackageIntIssue(
        string worksheetPart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{worksheetPart} {description} has no {attributeName}");
        }
        else if (!TryParseNonNegativePackageInt(value, out _))
        {
            issues.Add($"{worksheetPart} {description} has invalid {attributeName} value '{value}'");
        }
    }

    private static void ThrowInvalidWorksheetSingleXmlCellsMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet singleXmlCells metadata: {sample}{suffix}");
    }

    private static void AssertWorksheetExtensionListMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetExtensionListMetadataIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidWorksheetExtensionListMetadata(label, sourcePath, issues);
    }

    private static void AddWorksheetExtensionListMetadataIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        var root = worksheetXml.Root;
        if (root is null)
            return;

        var extensionLists = root.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{worksheetPart} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => new WorksheetExtensionListReference(index + 1, element)))
        {
            AddWorksheetExtensionListIssues(worksheetPart, root, extensionList, issues);
        }
    }

    private static void AddWorksheetExtensionListIssues(
        string worksheetPart,
        XElement worksheetRoot,
        WorksheetExtensionListReference extensionListReference,
        List<string> issues)
    {
        var extensionList = extensionListReference.Element;
        var description = $"extLst #{extensionListReference.Ordinal}";
        AddWorksheetExtensionListOrderingIssues(worksheetPart, worksheetRoot, extensionList, description, issues);

        if (extensionList.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration))
            issues.Add($"{worksheetPart} {description} has attributes; expected extension entries only");

        foreach (var unexpectedChild in extensionList.Elements().Where(element => element.Name != SpreadsheetNs + "ext"))
        {
            issues.Add($"{worksheetPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected ext entries only");
        }

        var extensions = extensionList.Elements(SpreadsheetNs + "ext").ToArray();
        if (extensions.Length == 0)
            issues.Add($"{worksheetPart} {description} has no ext entries");

        var seenUris = new HashSet<string>(StringComparer.Ordinal);
        foreach (var extension in extensions.Select((element, index) => new WorksheetExtensionReference(index + 1, element)))
        {
            AddWorksheetExtensionIssues(worksheetPart, description, extension, seenUris, issues);
        }
    }

    private static void AddWorksheetExtensionListOrderingIssues(
        string worksheetPart,
        XElement worksheetRoot,
        XElement extensionList,
        string description,
        List<string> issues)
    {
        string[] earlierWorksheetElements =
        [
            "sheetPr",
            "dimension",
            "sheetViews",
            "sheetFormatPr",
            "cols",
            "sheetData",
            "sheetCalcPr",
            "sheetProtection",
            "protectedRanges",
            "scenarios",
            "autoFilter",
            "sortState",
            "dataConsolidate",
            "customSheetViews",
            "mergeCells",
            "phoneticPr",
            "conditionalFormatting",
            "dataValidations",
            "hyperlinks",
            "printOptions",
            "pageMargins",
            "pageSetup",
            "headerFooter",
            "rowBreaks",
            "colBreaks",
            "customProperties",
            "cellWatches",
            "ignoredErrors",
            "singleXmlCells",
            "smartTags",
            "drawing",
            "legacyDrawing",
            "legacyDrawingHF",
            "picture",
            "oleObjects",
            "controls",
            "webPublishItems",
            "tableParts"
        ];

        var worksheetChildren = worksheetRoot.Elements().ToArray();
        var extensionListIndex = Array.IndexOf(worksheetChildren, extensionList);
        if (extensionListIndex < 0)
            return;

        foreach (var laterEarlierElement in worksheetChildren
                     .Skip(extensionListIndex + 1)
                     .Where(element =>
                         element.Name.Namespace == SpreadsheetNs &&
                         earlierWorksheetElements.Contains(element.Name.LocalName, StringComparer.Ordinal)))
        {
            issues.Add($"{worksheetPart} {description} appears before {laterEarlierElement.Name.LocalName}; expected schema order after that element");
        }
    }

    private static void AddWorksheetExtensionIssues(
        string worksheetPart,
        string extensionListDescription,
        WorksheetExtensionReference extensionReference,
        HashSet<string> seenUris,
        List<string> issues)
    {
        var extension = extensionReference.Element;
        var description = $"{extensionListDescription} ext #{extensionReference.Ordinal}";
        foreach (var attribute in extension.Attributes())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "uri"))
            {
                continue;
            }

            issues.Add($"{worksheetPart} {description} has unsupported attribute {attribute.Name}");
        }

        var uri = extension.Attribute("uri")?.Value;
        if (string.IsNullOrWhiteSpace(uri))
        {
            issues.Add($"{worksheetPart} {description} has no uri");
        }
        else
        {
            var trimmedUri = uri.Trim();
            if (!string.Equals(uri, trimmedUri, StringComparison.Ordinal))
                issues.Add($"{worksheetPart} {description} has untrimmed uri '{uri}'");
            if (!seenUris.Add(trimmedUri))
                issues.Add($"{worksheetPart} {extensionListDescription} has duplicate ext uri '{uri}'");
        }
    }

    private static void ThrowInvalidWorksheetExtensionListMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid worksheet extLst metadata: {sample}{suffix}");
    }

    private static void AssertSmartTagMetadataComplete(string xlsxPath, string label, string sourcePath)
    {
        using var archive = ZipFile.OpenRead(xlsxPath);
        var issues = new List<string>();

        var workbookEntry = FindPackageEntry(archive, WorkbookPart);
        if (workbookEntry is not null)
            AddWorkbookSmartTagIssues(LoadPackageXml(workbookEntry), issues);

        foreach (var worksheetEntry in archive.Entries.Where(entry =>
                     NormalizePackagePart(entry.FullName).StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            AddWorksheetSmartTagIssues(
                NormalizePackagePart(worksheetEntry.FullName),
                LoadPackageXml(worksheetEntry),
                issues);
        }

        if (issues.Count == 0)
            return;

        ThrowInvalidSmartTagMetadata(label, sourcePath, issues);
    }

    private static void AddWorkbookSmartTagIssues(XDocument workbookXml, List<string> issues)
    {
        var root = workbookXml.Root;
        if (root is null)
            return;

        var smartTagProperties = root.Elements(SpreadsheetNs + "smartTagPr").ToArray();
        if (smartTagProperties.Length > 1)
            issues.Add($"{WorkbookPart} has {smartTagProperties.Length} smartTagPr elements; expected at most one");

        foreach (var smartTagProperty in smartTagProperties)
        {
            var embed = smartTagProperty.Attribute("embed")?.Value;
            if (!string.IsNullOrWhiteSpace(embed) && !IsValidPackageBoolean(embed))
                issues.Add($"{WorkbookPart} smartTagPr has invalid embed value '{embed}'");

            var show = smartTagProperty.Attribute("show")?.Value;
            if (show is not null && string.IsNullOrWhiteSpace(show))
                issues.Add($"{WorkbookPart} smartTagPr has an empty show value");
        }

        var smartTagTypeContainers = root.Elements(SpreadsheetNs + "smartTagTypes").ToArray();
        if (smartTagTypeContainers.Length > 1)
            issues.Add($"{WorkbookPart} has {smartTagTypeContainers.Length} smartTagTypes elements; expected at most one");

        foreach (var smartTagTypes in smartTagTypeContainers)
        {
            var types = smartTagTypes.Elements(SpreadsheetNs + "smartTagType").ToArray();
            if (types.Length == 0)
                issues.Add($"{WorkbookPart} smartTagTypes has no smartTagType entries");

            foreach (var smartTagType in types.Select((element, index) => new WorkbookSmartTagTypeReference(index + 1, element)))
            {
                AddWorkbookSmartTagTypeIssues(smartTagType, issues);
            }
        }
    }

    private static void AddWorkbookSmartTagTypeIssues(
        WorkbookSmartTagTypeReference smartTagTypeReference,
        List<string> issues)
    {
        var smartTagType = smartTagTypeReference.Element;
        var namespaceUri = smartTagType.Attribute("namespaceUri")?.Value;
        if (string.IsNullOrWhiteSpace(namespaceUri))
        {
            issues.Add($"{WorkbookPart} smartTagType #{smartTagTypeReference.Ordinal} has no namespaceUri");
        }
        else if (!Uri.TryCreate(namespaceUri.Trim(), UriKind.Absolute, out _))
        {
            issues.Add($"{WorkbookPart} smartTagType #{smartTagTypeReference.Ordinal} has invalid namespaceUri '{namespaceUri}'");
        }

        if (string.IsNullOrWhiteSpace(smartTagType.Attribute("name")?.Value))
            issues.Add($"{WorkbookPart} smartTagType #{smartTagTypeReference.Ordinal} has no name");

        if (smartTagType.Elements().Any())
            issues.Add($"{WorkbookPart} smartTagType #{smartTagTypeReference.Ordinal} has child elements; expected attributes only");
    }

    private static void AddWorksheetSmartTagIssues(
        string worksheetPart,
        XDocument worksheetXml,
        List<string> issues)
    {
        foreach (var smartTags in worksheetXml.Root?.Elements(SpreadsheetNs + "smartTags") ?? [])
        {
            var cellSmartTags = smartTags.Elements(SpreadsheetNs + "cellSmartTags").ToArray();
            if (cellSmartTags.Length == 0)
                issues.Add($"{worksheetPart} smartTags has no cellSmartTags entries");

            foreach (var cellSmartTag in cellSmartTags.Select((element, index) => new WorksheetCellSmartTagsReference(index + 1, element)))
            {
                AddWorksheetCellSmartTagsIssues(worksheetPart, cellSmartTag, issues);
            }
        }
    }

    private static void AddWorksheetCellSmartTagsIssues(
        string worksheetPart,
        WorksheetCellSmartTagsReference cellSmartTagsReference,
        List<string> issues)
    {
        var cellSmartTags = cellSmartTagsReference.Element;
        var reference = cellSmartTags.Attribute("r")?.Value;
        var description = $"cellSmartTags #{cellSmartTagsReference.Ordinal}";
        if (string.IsNullOrWhiteSpace(reference))
        {
            issues.Add($"{worksheetPart} {description} has no r reference");
        }
        else if (!IsValidLocalWorksheetReference(reference))
        {
            issues.Add($"{worksheetPart} {description} has invalid local r reference '{reference}'");
        }

        var tags = cellSmartTags.Elements(SpreadsheetNs + "cellSmartTag").ToArray();
        if (tags.Length == 0)
            issues.Add($"{worksheetPart} {description} has no cellSmartTag entries");

        foreach (var smartTag in tags.Select((element, index) => new WorksheetCellSmartTagReference(index + 1, element)))
        {
            AddWorksheetCellSmartTagIssues(worksheetPart, description, smartTag, issues);
        }
    }

    private static void AddWorksheetCellSmartTagIssues(
        string worksheetPart,
        string cellSmartTagsDescription,
        WorksheetCellSmartTagReference smartTagReference,
        List<string> issues)
    {
        var smartTag = smartTagReference.Element;
        var description = $"{cellSmartTagsDescription} cellSmartTag #{smartTagReference.Ordinal}";
        var type = smartTag.Attribute("type")?.Value;
        if (string.IsNullOrWhiteSpace(type))
        {
            issues.Add($"{worksheetPart} {description} has no type");
        }
        else if (!TryParseNonNegativePackageInt(type, out _))
        {
            issues.Add($"{worksheetPart} {description} has invalid type value '{type}'");
        }

        var deleted = smartTag.Attribute("deleted")?.Value;
        if (!string.IsNullOrWhiteSpace(deleted) && !IsValidPackageBoolean(deleted))
            issues.Add($"{worksheetPart} {description} has invalid deleted value '{deleted}'");

        var properties = smartTag.Elements(SpreadsheetNs + "cellSmartTagPr").ToArray();
        if (properties.Length == 0)
            issues.Add($"{worksheetPart} {description} has no cellSmartTagPr entries");

        foreach (var property in properties.Select((element, index) => new WorksheetCellSmartTagPropertyReference(index + 1, element)))
        {
            AddWorksheetCellSmartTagPropertyIssues(worksheetPart, description, property, issues);
        }
    }

    private static void AddWorksheetCellSmartTagPropertyIssues(
        string worksheetPart,
        string smartTagDescription,
        WorksheetCellSmartTagPropertyReference propertyReference,
        List<string> issues)
    {
        var property = propertyReference.Element;
        var description = $"{smartTagDescription} cellSmartTagPr #{propertyReference.Ordinal}";
        if (string.IsNullOrWhiteSpace(property.Attribute("key")?.Value))
            issues.Add($"{worksheetPart} {description} has no key");

        if (property.Attribute("val") is null)
            issues.Add($"{worksheetPart} {description} has no val attribute");
    }

    private static void ThrowInvalidSmartTagMetadata(string label, string sourcePath, IReadOnlyList<string> issues)
    {
        var sample = string.Join("; ", issues.Take(MaxPackageRelationshipIssuesToReport));
        var suffix = issues.Count > MaxPackageRelationshipIssuesToReport
            ? $"; ... {issues.Count - MaxPackageRelationshipIssuesToReport} more"
            : string.Empty;

        throw new InvalidDataException(
            $"{label} for '{sourcePath}' has invalid smart-tag metadata: {sample}{suffix}");
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
                    {
                        issues.Add($"{tablePart} has an invalid table root element");
                        continue;
                    }

                    AddWorksheetTableMetadataIssues(tablePart, tableXml.Root, issues);
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

    private static void AddWorksheetTableMetadataIssues(
        string tablePart,
        XElement table,
        List<string> issues)
    {
        AddRequiredPositivePackageIntIssue(tablePart, "table", "id", table.Attribute("id")?.Value, issues);

        var reference = table.Attribute("ref")?.Value;
        if (string.IsNullOrWhiteSpace(reference))
            issues.Add($"{tablePart} table has no ref");
        else if (!IsValidLocalWorksheetReference(reference))
            issues.Add($"{tablePart} table has invalid local ref reference '{reference}'");

        AddRequiredPackageStringAttributeIssue(tablePart, "table", "displayName", table.Attribute("displayName")?.Value, issues);
        AddOptionalPackageStringAttributeIssue(tablePart, "table", "name", table.Attribute("name")?.Value, issues);
        AddOptionalKnownPackageValueIssue(
            tablePart,
            "table",
            "tableType",
            table.Attribute("tableType")?.Value,
            ["worksheet", "xml", "queryTable"],
            issues);
        AddOptionalPackageBooleanIssue(tablePart, "table", "published", table.Attribute("published")?.Value, issues);
        AddOptionalPackageBooleanIssue(tablePart, "table", "insertRow", table.Attribute("insertRow")?.Value, issues);
        AddOptionalPackageBooleanIssue(tablePart, "table", "insertRowShift", table.Attribute("insertRowShift")?.Value, issues);
        AddOptionalPackageBooleanIssue(tablePart, "table", "totalsRowShown", table.Attribute("totalsRowShown")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, "table", "headerRowCount", table.Attribute("headerRowCount")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, "table", "totalsRowCount", table.Attribute("totalsRowCount")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, "table", "connectionId", table.Attribute("connectionId")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, "table", "headerRowDxfId", table.Attribute("headerRowDxfId")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, "table", "dataDxfId", table.Attribute("dataDxfId")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, "table", "totalsRowDxfId", table.Attribute("totalsRowDxfId")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, "table", "headerRowBorderDxfId", table.Attribute("headerRowBorderDxfId")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, "table", "tableBorderDxfId", table.Attribute("tableBorderDxfId")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, "table", "totalsRowBorderDxfId", table.Attribute("totalsRowBorderDxfId")?.Value, issues);

        AddWorksheetTableChildOrderingIssues(tablePart, table, issues);
        AddWorksheetTableSingleChildIssues(tablePart, table, "autoFilter", issues);
        AddWorksheetTableSingleChildIssues(tablePart, table, "sortState", issues);
        AddWorksheetTableSingleChildIssues(tablePart, table, "tableColumns", issues);
        AddWorksheetTableSingleChildIssues(tablePart, table, "tableStyleInfo", issues);
        AddWorksheetTableSingleChildIssues(tablePart, table, "extLst", issues);

        if (!table.Elements(SpreadsheetNs + "tableColumns").Any())
            issues.Add($"{tablePart} table has no tableColumns element");

        foreach (var unexpectedChild in table.Elements().Where(child => !IsKnownWorksheetTableChild(child)))
        {
            issues.Add($"{tablePart} table has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        foreach (var autoFilter in table.Elements(SpreadsheetNs + "autoFilter").Select((element, index) => new WorksheetAutoFilterReference(index + 1, element)))
        {
            AddWorksheetTableAutoFilterIssues(tablePart, autoFilter, issues);
        }

        foreach (var sortState in table.Elements(SpreadsheetNs + "sortState").Select((element, index) => new WorksheetSortStateReference(index + 1, element)))
        {
            AddWorksheetAutoFilterSortStateIssues(tablePart, "table", sortState, issues);
        }

        foreach (var tableColumns in table.Elements(SpreadsheetNs + "tableColumns").Select((element, index) => new TableColumnsReference(index + 1, element)))
        {
            AddWorksheetTableColumnsIssues(tablePart, tableColumns, issues);
        }

        foreach (var styleInfo in table.Elements(SpreadsheetNs + "tableStyleInfo").Select((element, index) => new TableStyleInfoReference(index + 1, element)))
        {
            AddWorksheetTableStyleInfoIssues(tablePart, styleInfo, issues);
        }

        foreach (var extensionList in table.Elements(SpreadsheetNs + "extLst").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(tablePart, "table", extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetTableColumnsIssues(
        string tablePart,
        TableColumnsReference tableColumnsReference,
        List<string> issues)
    {
        var tableColumns = tableColumnsReference.Element;
        var description = $"tableColumns #{tableColumnsReference.Ordinal}";
        var columns = tableColumns.Elements(SpreadsheetNs + "tableColumn").ToArray();

        if (columns.Length == 0)
            issues.Add($"{tablePart} {description} has no tableColumn entries");

        AddRequiredNonNegativePackageIntIssue(tablePart, description, "count", tableColumns.Attribute("count")?.Value, issues);
        if (TryParseNonNegativePackageInt(tableColumns.Attribute("count")?.Value, out var declaredCount) &&
            declaredCount != columns.Length)
        {
            issues.Add($"{tablePart} {description} count is {declaredCount}, but contains {columns.Length} tableColumn entries");
        }

        AddWorksheetTableColumnsChildOrderingIssues(tablePart, description, tableColumns, issues);

        foreach (var unexpectedChild in tableColumns.Elements().Where(child =>
                     child.Name != SpreadsheetNs + "tableColumn"))
        {
            issues.Add($"{tablePart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var seenColumnIds = new HashSet<int>();
        var seenColumnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns.Select((element, index) => new TableColumnReference(index + 1, element)))
        {
            AddWorksheetTableColumnIssues(tablePart, description, column, seenColumnIds, seenColumnNames, issues);
        }
    }

    private static void AddWorksheetTableAutoFilterIssues(
        string tablePart,
        WorksheetAutoFilterReference autoFilterReference,
        List<string> issues)
    {
        var autoFilter = autoFilterReference.Element;
        var description = $"table autoFilter #{autoFilterReference.Ordinal}";
        var reference = autoFilter.Attribute("ref")?.Value;
        if (autoFilter.Attribute("ref") is not null && string.IsNullOrWhiteSpace(reference))
            issues.Add($"{tablePart} {description} has blank ref attribute");
        else if (!string.IsNullOrWhiteSpace(reference) && !IsValidLocalWorksheetReference(reference))
            issues.Add($"{tablePart} {description} has invalid ref value '{reference}'");

        AddWorksheetAutoFilterChildOrderingIssues(tablePart, description, autoFilter, issues);

        foreach (var unexpectedChild in autoFilter.Elements().Where(element => !IsKnownWorksheetAutoFilterChild(element)))
        {
            issues.Add($"{tablePart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        var seenFilterColumns = new HashSet<int>();
        foreach (var filterColumn in autoFilter.Elements(SpreadsheetNs + "filterColumn").Select((element, index) => new WorksheetAutoFilterColumnReference(index + 1, element)))
        {
            AddWorksheetAutoFilterColumnIssues(tablePart, description, filterColumn, seenFilterColumns, issues, allowExtensionList: false);
        }

        var nestedSortStates = autoFilter.Elements(SpreadsheetNs + "sortState").ToArray();
        if (nestedSortStates.Length > 1)
            issues.Add($"{tablePart} {description} has {nestedSortStates.Length} sortState elements; expected at most one");

        foreach (var nestedSortState in nestedSortStates.Select((element, index) => new WorksheetSortStateReference(index + 1, element)))
        {
            AddWorksheetAutoFilterSortStateIssues(tablePart, description, nestedSortState, issues);
        }

        var extensionLists = autoFilter.Elements(SpreadsheetNs + "extLst").ToArray();
        if (extensionLists.Length > 1)
            issues.Add($"{tablePart} {description} has {extensionLists.Length} extLst elements; expected at most one");

        foreach (var extensionList in extensionLists.Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(tablePart, description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetTableColumnIssues(
        string tablePart,
        string containerDescription,
        TableColumnReference columnReference,
        HashSet<int> seenColumnIds,
        HashSet<string> seenColumnNames,
        List<string> issues)
    {
        var column = columnReference.Element;
        var description = $"{containerDescription} tableColumn #{columnReference.Ordinal}";

        if (!TryParsePositivePackageInt(column.Attribute("id")?.Value, out var columnId))
        {
            issues.Add($"{tablePart} {description} has invalid id '{column.Attribute("id")?.Value}'");
        }
        else if (!seenColumnIds.Add(columnId))
        {
            issues.Add($"{tablePart} {containerDescription} has duplicate tableColumn id {columnId}");
        }

        var columnName = column.Attribute("name")?.Value;
        if (string.IsNullOrWhiteSpace(columnName))
        {
            issues.Add($"{tablePart} {description} has no name");
        }
        else if (!seenColumnNames.Add(columnName.Trim()))
        {
            issues.Add($"{tablePart} {containerDescription} has duplicate tableColumn name '{columnName}'");
        }

        AddOptionalPackageNonNegativeIntIssue(tablePart, description, "dataDxfId", column.Attribute("dataDxfId")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, description, "queryTableFieldId", column.Attribute("queryTableFieldId")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, description, "headerRowDxfId", column.Attribute("headerRowDxfId")?.Value, issues);
        AddOptionalPackageNonNegativeIntIssue(tablePart, description, "totalsRowDxfId", column.Attribute("totalsRowDxfId")?.Value, issues);
        AddOptionalPackageStringAttributeIssue(tablePart, description, "headerRowCellStyle", column.Attribute("headerRowCellStyle")?.Value, issues);
        AddOptionalPackageStringAttributeIssue(tablePart, description, "dataCellStyle", column.Attribute("dataCellStyle")?.Value, issues);
        AddOptionalPackageStringAttributeIssue(tablePart, description, "totalsRowCellStyle", column.Attribute("totalsRowCellStyle")?.Value, issues);
        AddOptionalKnownPackageValueIssue(
            tablePart,
            description,
            "totalsRowFunction",
            column.Attribute("totalsRowFunction")?.Value,
            ["none", "sum", "min", "max", "average", "count", "countNums", "stdDev", "var", "custom"],
            issues);

        AddWorksheetTableColumnChildOrderingIssues(tablePart, description, column, issues);

        foreach (var unexpectedChild in column.Elements().Where(child =>
                     child.Name != SpreadsheetNs + "calculatedColumnFormula" &&
                     child.Name != SpreadsheetNs + "totalsRowFormula" &&
                     child.Name != SpreadsheetNs + "xmlColumnPr" &&
                     child.Name != SpreadsheetNs + "extLst"))
        {
            issues.Add($"{tablePart} {description} has unexpected child element {unexpectedChild.Name.LocalName}");
        }

        AddWorksheetTableSingleChildIssues(tablePart, column, description, "calculatedColumnFormula", issues);
        AddWorksheetTableSingleChildIssues(tablePart, column, description, "totalsRowFormula", issues);
        AddWorksheetTableSingleChildIssues(tablePart, column, description, "xmlColumnPr", issues);
        AddWorksheetTableSingleChildIssues(tablePart, column, description, "extLst", issues);

        foreach (var formula in column.Elements()
                     .Where(element =>
                         element.Name == SpreadsheetNs + "calculatedColumnFormula" ||
                         element.Name == SpreadsheetNs + "totalsRowFormula")
                     .Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetTableFormulaIssues(tablePart, description, formula.Ordinal, formula.Element, issues);
        }

        foreach (var xmlColumnPr in column.Elements(SpreadsheetNs + "xmlColumnPr").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetTableXmlColumnPropertyIssues(tablePart, description, xmlColumnPr.Ordinal, xmlColumnPr.Element, issues);
        }

        foreach (var extensionList in column.Elements(SpreadsheetNs + "extLst").Select((element, index) => (Ordinal: index + 1, Element: element)))
        {
            AddWorksheetNestedExtensionListIssues(tablePart, description, extensionList.Ordinal, extensionList.Element, issues);
        }
    }

    private static void AddWorksheetTableFormulaIssues(
        string tablePart,
        string columnDescription,
        int ordinal,
        XElement formula,
        List<string> issues)
    {
        var description = $"{columnDescription} {formula.Name.LocalName} #{ordinal}";
        AddOptionalPackageBooleanIssue(tablePart, description, "array", formula.Attribute("array")?.Value, issues);

        if (formula.Elements().Any())
            issues.Add($"{tablePart} {description} has child elements; expected text only");
    }

    private static void AddWorksheetTableXmlColumnPropertyIssues(
        string tablePart,
        string columnDescription,
        int ordinal,
        XElement xmlColumnPr,
        List<string> issues)
    {
        var description = $"{columnDescription} xmlColumnPr #{ordinal}";
        AddRequiredNonNegativePackageIntIssue(tablePart, description, "mapId", xmlColumnPr.Attribute("mapId")?.Value, issues);
        AddRequiredPackageStringAttributeIssue(tablePart, description, "xpath", xmlColumnPr.Attribute("xpath")?.Value, issues);
        AddOptionalPackageBooleanIssue(tablePart, description, "denormalized", xmlColumnPr.Attribute("denormalized")?.Value, issues);

        if (xmlColumnPr.Elements().Any())
            issues.Add($"{tablePart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetTableStyleInfoIssues(
        string tablePart,
        TableStyleInfoReference styleInfoReference,
        List<string> issues)
    {
        var styleInfo = styleInfoReference.Element;
        var description = $"tableStyleInfo #{styleInfoReference.Ordinal}";
        AddOptionalPackageBooleanIssue(tablePart, description, "showFirstColumn", styleInfo.Attribute("showFirstColumn")?.Value, issues);
        AddOptionalPackageBooleanIssue(tablePart, description, "showLastColumn", styleInfo.Attribute("showLastColumn")?.Value, issues);
        AddOptionalPackageBooleanIssue(tablePart, description, "showRowStripes", styleInfo.Attribute("showRowStripes")?.Value, issues);
        AddOptionalPackageBooleanIssue(tablePart, description, "showColumnStripes", styleInfo.Attribute("showColumnStripes")?.Value, issues);

        if (styleInfo.Elements().Any())
            issues.Add($"{tablePart} {description} has child elements; expected attributes only");
    }

    private static void AddWorksheetTableSingleChildIssues(
        string tablePart,
        XElement parent,
        string childName,
        List<string> issues) =>
        AddWorksheetTableSingleChildIssues(tablePart, parent, "table", childName, issues);

    private static void AddWorksheetTableSingleChildIssues(
        string tablePart,
        XElement parent,
        string parentDescription,
        string childName,
        List<string> issues)
    {
        var children = parent.Elements(SpreadsheetNs + childName).ToArray();
        if (children.Length > 1)
            issues.Add($"{tablePart} {parentDescription} has {children.Length} {childName} elements; expected at most one");
    }

    private static void AddWorksheetTableChildOrderingIssues(
        string tablePart,
        XElement table,
        List<string> issues)
    {
        var previousKnownChildOrder = -1;
        foreach (var child in table.Elements())
        {
            var childOrder = GetWorksheetTableChildOrder(child);
            if (childOrder < 0)
                continue;

            if (childOrder < previousKnownChildOrder)
                issues.Add($"{tablePart} table child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;
        }
    }

    private static bool IsKnownWorksheetTableChild(XElement child) =>
        GetWorksheetTableChildOrder(child) >= 0;

    private static int GetWorksheetTableChildOrder(XElement child)
    {
        if (child.Name.Namespace != SpreadsheetNs)
            return -1;

        return child.Name.LocalName switch
        {
            "autoFilter" => 0,
            "sortState" => 1,
            "tableColumns" => 2,
            "tableStyleInfo" => 3,
            "extLst" => 4,
            _ => -1
        };
    }

    private static void AddWorksheetTableColumnsChildOrderingIssues(
        string tablePart,
        string description,
        XElement tableColumns,
        List<string> issues)
    {
        var children = tableColumns.Elements().ToArray();
        var firstExtensionListIndex = Array.FindIndex(children, element => element.Name == SpreadsheetNs + "extLst");
        if (firstExtensionListIndex >= 0 &&
            children.Skip(firstExtensionListIndex + 1).Any(element => element.Name == SpreadsheetNs + "tableColumn"))
        {
            issues.Add($"{tablePart} {description} has tableColumn entries after extLst; expected extLst last");
        }
    }

    private static void AddWorksheetTableColumnChildOrderingIssues(
        string tablePart,
        string description,
        XElement tableColumn,
        List<string> issues)
    {
        var previousKnownChildOrder = -1;
        foreach (var child in tableColumn.Elements())
        {
            var childOrder = GetWorksheetTableColumnChildOrder(child);
            if (childOrder < 0)
                continue;

            if (childOrder < previousKnownChildOrder)
                issues.Add($"{tablePart} {description} child {child.Name.LocalName} appears out of schema order");
            else
                previousKnownChildOrder = childOrder;
        }
    }

    private static int GetWorksheetTableColumnChildOrder(XElement child)
    {
        if (child.Name.Namespace != SpreadsheetNs)
            return -1;

        return child.Name.LocalName switch
        {
            "calculatedColumnFormula" => 0,
            "totalsRowFormula" => 1,
            "xmlColumnPr" => 2,
            "extLst" => 3,
            _ => -1
        };
    }

    private static void AddRequiredPackageStringAttributeIssue(
        string packagePart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
            issues.Add($"{packagePart} {description} has no {attributeName}");
    }

    private static void AddOptionalPackageStringAttributeIssue(
        string packagePart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
            issues.Add($"{packagePart} {description} has blank {attributeName}");
    }

    private static void AddOptionalKnownPackageValueIssue(
        string packagePart,
        string description,
        string attributeName,
        string? value,
        IReadOnlyCollection<string> knownValues,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            knownValues.Contains(value.Trim(), StringComparer.Ordinal))
        {
            return;
        }

        issues.Add($"{packagePart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void AddOptionalPackageBooleanIssue(
        string packagePart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || IsValidPackageBoolean(value))
            return;

        issues.Add($"{packagePart} {description} has invalid {attributeName} value '{value}'");
    }

    private static void AddOptionalPackageNonNegativeIntIssue(
        string packagePart,
        string description,
        string attributeName,
        string? value,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || TryParseNonNegativePackageInt(value, out _))
            return;

        issues.Add($"{packagePart} {description} has invalid {attributeName} value '{value}'");
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

        var workbookXml = LoadPackageXml(workbookEntry);
        var pivotCacheContainers = workbookXml.Root?
            .Elements(SpreadsheetNs + "pivotCaches")
            .ToArray() ?? [];
        if (pivotCacheContainers.Length == 0)
            return references;

        AddWorkbookPivotCachesSchemaIssues(pivotCacheContainers, issues);
        var pivotCaches = pivotCacheContainers
            .SelectMany(pivotCaches => pivotCaches.Elements(SpreadsheetNs + "pivotCache"))
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

    private static void AddWorkbookPivotCachesSchemaIssues(
        XElement[] pivotCacheContainers,
        List<string> issues)
    {
        if (pivotCacheContainers.Length > 1)
            issues.Add($"{WorkbookPart} has {pivotCacheContainers.Length} pivotCaches elements; expected at most one");

        var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pivotCaches in pivotCacheContainers.Select((element, index) => (Element: element, Ordinal: index + 1)))
        {
            var description = $"pivotCaches #{pivotCaches.Ordinal}";
            foreach (var attribute in pivotCaches.Element.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration)
                    issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
            }

            var pivotCacheElements = pivotCaches.Element.Elements(SpreadsheetNs + "pivotCache").ToArray();
            if (pivotCacheElements.Length == 0)
                issues.Add($"{WorkbookPart} {description} has no pivotCache entries");

            foreach (var unexpectedChild in pivotCaches.Element.Elements().Where(child => child.Name != SpreadsheetNs + "pivotCache"))
                issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected pivotCache entries only");

            foreach (var pivotCache in pivotCacheElements.Select((element, index) => (Element: element, Ordinal: index + 1)))
            {
                var childDescription = $"{description} pivotCache #{pivotCache.Ordinal}";
                foreach (var attribute in pivotCache.Element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration ||
                        (attribute.Name.NamespaceName.Length == 0 && attribute.Name.LocalName == "cacheId") ||
                        attribute.Name == OfficeRelationshipNs + "id")
                    {
                        continue;
                    }

                    issues.Add($"{WorkbookPart} {childDescription} has unsupported attribute {attribute.Name}");
                }

                var relationshipId = pivotCache.Element.Attribute(OfficeRelationshipNs + "id")?.Value;
                if (!string.IsNullOrWhiteSpace(relationshipId))
                {
                    var trimmedRelationshipId = relationshipId.Trim();
                    if (!string.Equals(relationshipId, trimmedRelationshipId, StringComparison.Ordinal))
                        issues.Add($"{WorkbookPart} {childDescription} has untrimmed relationship id '{relationshipId}'");
                    if (!seenRelationshipIds.Add(trimmedRelationshipId))
                        issues.Add($"{WorkbookPart} {childDescription} duplicates relationship id {trimmedRelationshipId}");
                }

                if (pivotCache.Element.Elements().Any())
                    issues.Add($"{WorkbookPart} {childDescription} has child elements; expected attributes only");
            }
        }
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

        var workbookXml = LoadPackageXml(workbookEntry);
        var workbookRoot = workbookXml.Root;
        if (workbookRoot is null)
            return;

        var issues = new List<string>();
        var externalReferenceContainers = workbookRoot
            .Elements(SpreadsheetNs + "externalReferences")
            .ToArray();
        if (externalReferenceContainers.Length == 0)
            return;

        AddWorkbookExternalReferencesSchemaIssues(externalReferenceContainers, issues);
        var externalReferences = externalReferenceContainers
            .SelectMany(externalReferences => externalReferences.Elements(SpreadsheetNs + "externalReference"))
            .Select((externalReference, index) => new WorkbookExternalReference(
                index + 1,
                externalReference.Attribute(OfficeRelationshipNs + "id")?.Value))
            .ToArray();
        if (externalReferences.Length == 0 && issues.Count == 0)
            return;

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

    private static void AddWorkbookExternalReferencesSchemaIssues(
        XElement[] externalReferenceContainers,
        List<string> issues)
    {
        if (externalReferenceContainers.Length > 1)
            issues.Add($"{WorkbookPart} has {externalReferenceContainers.Length} externalReferences elements; expected at most one");

        var seenRelationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var externalReferences in externalReferenceContainers.Select((element, index) => (Element: element, Ordinal: index + 1)))
        {
            var description = $"externalReferences #{externalReferences.Ordinal}";
            foreach (var attribute in externalReferences.Element.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration)
                    issues.Add($"{WorkbookPart} {description} has unsupported attribute {attribute.Name}");
            }

            var externalReferenceElements = externalReferences.Element.Elements(SpreadsheetNs + "externalReference").ToArray();
            if (externalReferenceElements.Length == 0)
                issues.Add($"{WorkbookPart} {description} has no externalReference entries");

            foreach (var unexpectedChild in externalReferences.Element.Elements().Where(child => child.Name != SpreadsheetNs + "externalReference"))
                issues.Add($"{WorkbookPart} {description} has unexpected child element {unexpectedChild.Name.LocalName}; expected externalReference entries only");

            foreach (var externalReference in externalReferenceElements.Select((element, index) => (Element: element, Ordinal: index + 1)))
            {
                var childDescription = $"{description} externalReference #{externalReference.Ordinal}";
                foreach (var attribute in externalReference.Element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration || attribute.Name == OfficeRelationshipNs + "id")
                        continue;

                    issues.Add($"{WorkbookPart} {childDescription} has unsupported attribute {attribute.Name}");
                }

                var relationshipId = externalReference.Element.Attribute(OfficeRelationshipNs + "id")?.Value;
                if (string.IsNullOrWhiteSpace(relationshipId))
                {
                    issues.Add($"{WorkbookPart} {childDescription} has no relationship id");
                }
                else
                {
                    var trimmedRelationshipId = relationshipId.Trim();
                    if (!string.Equals(relationshipId, trimmedRelationshipId, StringComparison.Ordinal))
                        issues.Add($"{WorkbookPart} {childDescription} has untrimmed relationship id '{relationshipId}'");
                    if (!seenRelationshipIds.Add(trimmedRelationshipId))
                        issues.Add($"{WorkbookPart} {childDescription} duplicates relationship id {trimmedRelationshipId}");
                }

                if (externalReference.Element.Elements().Any())
                    issues.Add($"{WorkbookPart} {childDescription} has child elements; expected attributes only");
            }
        }
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

            if (!IsExpectedSlicerTimelineRelationshipType(relationship.Attribute("Type")?.Value, expectedRelationshipType))
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
                else if (IsTimelineRelationshipType(relationshipType))
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
                else if (IsTimelineCacheRelationshipType(relationshipType))
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

    private static bool IsExpectedSlicerTimelineRelationshipType(string? actualType, string expectedRelationshipType) =>
        string.Equals(actualType, expectedRelationshipType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(expectedRelationshipType, TimelineRelationshipType, StringComparison.OrdinalIgnoreCase) &&
            IsTimelineRelationshipType(actualType) ||
        string.Equals(expectedRelationshipType, TimelineCacheRelationshipType, StringComparison.OrdinalIgnoreCase) &&
            IsTimelineCacheRelationshipType(actualType);

    private static bool IsTimelineRelationshipType(string? relationshipType) =>
        string.Equals(relationshipType, TimelineRelationshipType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(relationshipType, TimelineRelationshipType2011, StringComparison.OrdinalIgnoreCase);

    private static bool IsTimelineCacheRelationshipType(string? relationshipType) =>
        string.Equals(relationshipType, TimelineCacheRelationshipType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(relationshipType, TimelineCacheRelationshipType2011, StringComparison.OrdinalIgnoreCase);

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
                relationshipsXml = LoadPackageXml(stream);
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

        if (path.Contains("/mc:AlternateContent", StringComparison.Ordinal) &&
            path.Contains("/mc:Fallback", StringComparison.Ordinal) &&
            path.Contains("/xdr:cNvPr", StringComparison.Ordinal) &&
            description.Contains("unique value", StringComparison.OrdinalIgnoreCase) &&
            description.Contains("duplicates with others", StringComparison.OrdinalIgnoreCase))
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
            var saveResult = adapter.SaveWithWarnings(workbook, output);
            return new FreeXSaveResult(outputPath, summary, loadResult.Warnings, saveResult.Warnings);
        }
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
                        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
                        "docProps/core.xml"),
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
        else if (string.Equals(row.Id, "generated-threaded-comments-001", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                RequiredFreeXSavedPackageParts =
                [
                    "xl/threadedComments/threadedComment1.xml",
                    "xl/persons/person.xml",
                    "xl/worksheets/_rels/sheet1.xml.rels",
                    "xl/_rels/workbook.xml.rels"
                ],
                RequiredFreeXSavedPackageContentTypes =
                [
                    new(
                        "xl/threadedComments/threadedComment1.xml",
                        "application/vnd.ms-excel.threadedcomments+xml"),
                    new(
                        "xl/persons/person.xml",
                        "application/vnd.ms-excel.person+xml")
                ],
                RequiredFreeXSavedPackageRelationships =
                [
                    new(
                        "xl/worksheets/_rels/sheet1.xml.rels",
                        "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment",
                        "xl/threadedComments/threadedComment1.xml"),
                    new(
                        "xl/_rels/workbook.xml.rels",
                        "http://schemas.microsoft.com/office/2017/10/relationships/person",
                        "xl/persons/person.xml")
                ],
                MinFreeXPreSaveComments = 1
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
                ],
                MinExcelOpenedHeaderFooterSheets = 1,
                MinExcelReopenedHeaderFooterSheets = reopen
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
        else if (string.Equals(row.Id, "generated-dv-count-package-003", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinFreeXReopenedDataValidations = saveReopen ? 10 : 0
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
        else if (string.Equals(row.Id, "generated-cf-retention-package-003", StringComparison.OrdinalIgnoreCase))
        {
            expectations = EnsureExpectations() with
            {
                MinExcelOpenedConditionalFormats = 16,
                MinExcelReopenedConditionalFormats = saveReopen ? 16 : 0
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

    private static WorkbookSmokeExpectations ExcelNativePivotCorpusExpectations(bool saveReopen) =>
        new(
            MinFreeXPreSaveStructuredTables: 1,
            MinFreeXPreSavePivotTables: 1,
            MinFreeXPreSavePivotCaches: 1,
            MinExcelOpenedStructuredTables: 1,
            MinExcelOpenedPivotTables: 1,
            MinExcelReopenedStructuredTables: saveReopen ? 1 : 0,
            MinExcelReopenedPivotTables: saveReopen ? 1 : 0,
            MinFreeXReopenedStructuredTables: saveReopen ? 1 : 0,
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
            MinFreeXPreSaveFrozenSheets: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveCustomColumnWidths: expectFreeXPreSave ? 4 : 0,
            MinFreeXPreSaveStyledCells: expectFreeXPreSave ? 6 : 0,
            MinFreeXPreSaveNumberFormatCells: expectFreeXPreSave ? 4 : 0,
            MinFreeXPreSaveBoldCells: expectFreeXPreSave ? 6 : 0,
            MinFreeXPreSaveFilledCells: expectFreeXPreSave ? 6 : 0,
            MinFreeXPreSaveAlignedCells: expectFreeXPreSave ? 6 : 0,
            MinExcelOpenedFormulaCells: minFormulaCells,
            MinExcelOpenedNamedRanges: minNamedRanges,
            MinExcelOpenedFreezePaneSheets: 1,
            MinExcelOpenedCustomColumnWidths: 4,
            MinExcelOpenedStyledCells: 6,
            MinExcelOpenedNumberFormatCells: 4,
            MinExcelOpenedBoldCells: 6,
            MinExcelOpenedFilledCells: 6,
            MinExcelOpenedAlignedCells: 6,
            MinExcelReopenedFormulaCells: saveReopen ? minFormulaCells : 0,
            MinExcelReopenedNamedRanges: saveReopen ? minNamedRanges : 0,
            MinExcelReopenedFreezePaneSheets: saveReopen ? 1 : 0,
            MinExcelReopenedCustomColumnWidths: saveReopen ? 4 : 0,
            MinExcelReopenedStyledCells: saveReopen ? 6 : 0,
            MinExcelReopenedNumberFormatCells: saveReopen ? 4 : 0,
            MinExcelReopenedBoldCells: saveReopen ? 6 : 0,
            MinExcelReopenedFilledCells: saveReopen ? 6 : 0,
            MinExcelReopenedAlignedCells: saveReopen ? 6 : 0,
            MinFreeXReopenedFormulaCells: saveReopen ? minFormulaCells : 0,
            MinFreeXReopenedNamedRanges: saveReopen ? minNamedRanges : 0,
            MinFreeXReopenedFrozenSheets: saveReopen ? 1 : 0,
            MinFreeXReopenedCustomColumnWidths: saveReopen ? 4 : 0,
            MinFreeXReopenedStyledCells: saveReopen ? 6 : 0,
            MinFreeXReopenedNumberFormatCells: saveReopen ? 4 : 0,
            MinFreeXReopenedBoldCells: saveReopen ? 6 : 0,
            MinFreeXReopenedFilledCells: saveReopen ? 6 : 0,
            MinFreeXReopenedAlignedCells: saveReopen ? 6 : 0);

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
            MinFreeXPreSaveAutoFilterSheets: expectFreeXPreSave ? minStructuredTables : 0,
            MinExcelOpenedStructuredTables: minStructuredTables,
            MinExcelOpenedAutoFilterSheets: minStructuredTables,
            MinExcelReopenedStructuredTables: saveReopen ? minStructuredTables : 0,
            MinExcelReopenedAutoFilterSheets: saveReopen ? minStructuredTables : 0,
            MinFreeXReopenedStructuredTables: saveReopen ? minStructuredTables : 0,
            MinFreeXReopenedAutoFilterSheets: saveReopen ? minStructuredTables : 0,
            RequiredFreeXSavedPackageParts:
            [
                "xl/tables/table1.xml"
            ],
            RequiredExcelSavedPackageParts:
            [
                "xl/tables/table1.xml"
            ]);

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
            MinFreeXPreSaveFrozenSheets: expectFreeXPreSave ? 1 : 0,
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
            MinExcelOpenedFreezePaneSheets: 1,
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
            MinExcelReopenedFreezePaneSheets: saveReopen ? 1 : 0,
            MinFreeXReopenedProtectedSheets: saveReopen ? 1 : 0,
            MinFreeXReopenedStructureProtection: saveReopen ? 1 : 0,
            MinFreeXReopenedFrozenSheets: saveReopen ? 1 : 0);

    private static WorkbookSmokeExpectations PivotTableExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSavePivotTables: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSavePivotCaches: expectFreeXPreSave ? 1 : 0,
            MinExcelOpenedPivotTables: 1,
            MinExcelReopenedPivotTables: saveReopen ? 1 : 0,
            MinFreeXReopenedPivotTables: saveReopen ? 1 : 0,
            MinFreeXReopenedPivotCaches: saveReopen ? 1 : 0,
            RequiredFreeXSavedPackageParts:
            [
                "xl/pivotTables/pivotTable1.xml",
                "xl/pivotCache/pivotCacheDefinition1.xml"
            ],
            RequiredExcelSavedPackageParts:
            [
                "xl/pivotTables/pivotTable1.xml",
                "xl/pivotCache/pivotCacheDefinition1.xml"
            ]);

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

    private static void AssertFreeXSaveWarnings(
        WorkbookSmokeInput input,
        string label,
        IReadOnlyList<string> warnings)
    {
        if (input.Expectations?.RequireNoFreeXLoadWarnings != true || warnings.Count == 0)
            return;

        throw new InvalidDataException(
            $"{label} produced {warnings.Count} warning(s) for {input.Description}: {FormatWarnings(warnings)}");
    }

    private static IReadOnlyList<string> CombineFreeXWarnings(
        IReadOnlyList<string> loadWarnings,
        IReadOnlyList<string> saveWarnings)
    {
        if (loadWarnings.Count == 0)
            return saveWarnings;
        if (saveWarnings.Count == 0)
            return loadWarnings;

        var combined = new List<string>(loadWarnings.Count + saveWarnings.Count);
        combined.AddRange(loadWarnings.Select(warning => "Load: " + warning));
        combined.AddRange(saveWarnings.Select(warning => "Save: " + warning));
        return combined;
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
            var exception = new InvalidDataException(
                $"{label} expectation for {input.Description} was not measured; expected at least {minimum}.");
            exception.Data["ExpectationFailureCounter"] = label;
            exception.Data["ExpectationFailureKind"] = "not-measured";
            throw exception;
        }

        if (actual < minimum)
        {
            var exception = new InvalidDataException(
                $"{label} expectation failed for {input.Description}: expected at least {minimum}, observed {actual}.");
            exception.Data["ExpectationFailureCounter"] = label;
            exception.Data["ExpectationFailureKind"] = "below-minimum";
            throw exception;
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
            aggregates = new
            {
                openedTotals = BuildSummaryTotals(summary.Results.Select(result => result.Opened)),
                reopenedTotals = BuildSummaryTotals(summary.Results.Select(result => result.Reopened)),
                freeXPreSaveTotals = BuildSummaryTotals(summary.Results.Select(result => result.FreeXPreSave)),
                freeXReopenedExcelSaveTotals = BuildSummaryTotals(summary.Results.Select(result => result.FreeXReopenedExcelSave)),
                freeXWarningCounts = new
                {
                    preSaveWorkbookCount = summary.Results.Count(result => result.FreeXPreSaveWarnings.Count > 0),
                    preSaveTotal = summary.Results.Sum(result => result.FreeXPreSaveWarnings.Count),
                    reopenedWorkbookCount = summary.Results.Count(result => result.FreeXReopenedExcelSaveWarnings.Count > 0),
                    reopenedTotal = summary.Results.Sum(result => result.FreeXReopenedExcelSaveWarnings.Count)
                },
                expectationFailuresByCounter = CountExpectationFailuresByCounter(summary.Results)
            },
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
                generatedWithExcel = result.Input.GenerateWithExcel,
                sourceAuthorship = result.Input.GenerateWithExcel ? "excel-authored" : "external-or-freex-authored",
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
                error = result.Error,
                expectationFailureCounter = result.ExpectationFailureCounter,
                expectationFailureKind = result.ExpectationFailureKind
            })
        };

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(reportPath, json);
        Console.WriteLine($"Report: {reportPath}");
    }

    private static IReadOnlyDictionary<string, int> BuildSummaryTotals<TSummary>(IEnumerable<TSummary?> summaries)
        where TSummary : class
    {
        var properties = typeof(TSummary)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(int))
            .ToArray();
        var totals = properties.ToDictionary(
            property => property.Name,
            _ => 0,
            StringComparer.Ordinal);

        foreach (var summary in summaries)
        {
            if (summary is null)
                continue;

            foreach (var property in properties)
            {
                totals[property.Name] += (int)property.GetValue(summary)!;
            }
        }

        return totals;
    }

    private static IReadOnlyDictionary<string, int> CountExpectationFailuresByCounter(IEnumerable<WorkbookSmokeResult> results) =>
        results
            .Select(result => result.ExpectationFailureCounter)
            .Where(counter => !string.IsNullOrWhiteSpace(counter))
            .GroupBy(counter => counter!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

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

    private sealed record WorksheetCustomPropertyReference(
        int Ordinal,
        string? Name,
        string? LegacyId,
        string? RelationshipId);

    private sealed record WorksheetScenarioReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetScenarioInputCellReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookFileVersionReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookFileSharingReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookPropertiesReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookProtectionReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookSheetReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookBookViewsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookViewReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookCustomWorkbookViewsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookCustomWorkbookViewReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookFunctionGroupsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookFunctionGroupReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookDefinedNamesReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookDefinedNameReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookCalculationPropertiesReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookOleSizeReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookWebPublishingReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookFileRecoveryReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookWebPublishObjectsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookWebPublishObjectReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookExtensionListReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookExtensionReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSheetPropertiesReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetDimensionReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetColumnsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetColumnReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSheetDataReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetRowReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetCellReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetCalculationPropertiesReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSheetProtectionReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetProtectedRangesReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetProtectedRangeReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetAutoFilterReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetAutoFilterColumnReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSheetFormatReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSheetViewsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSheetViewReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSheetViewPaneReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSheetViewSelectionReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSheetViewPivotSelectionReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetCustomSheetViewsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetCustomSheetViewReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetMergeCellsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetMergeCellReference(
        int Ordinal,
        XElement Element);

    private readonly record struct WorksheetMergeRange(
        string NormalizedReference,
        uint StartRow,
        uint EndRow,
        uint StartColumn,
        uint EndColumn);

    private sealed record WorksheetPhoneticPropertiesReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSortStateReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSortConditionReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetDataConsolidationReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetDataRefsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetDataRefReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetConditionalFormattingReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetConditionalFormattingRuleReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetDataValidationsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetDataValidationReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetPrintOptionsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetPageMarginsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetPageSetupReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetHeaderFooterReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetPageBreaksReference(
        int Ordinal,
        XElement Element,
        string ElementName,
        int MaxBreakId,
        int MaxBreakSpan);

    private sealed record WorksheetPageBreakReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetCellWatchesReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetCellWatchReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetIgnoredErrorsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetIgnoredErrorReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSingleXmlCellsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetSingleXmlCellReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetExtensionListReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetExtensionReference(
        int Ordinal,
        XElement Element);

    private sealed record WorkbookSmartTagTypeReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetCellSmartTagsReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetCellSmartTagReference(
        int Ordinal,
        XElement Element);

    private sealed record WorksheetCellSmartTagPropertyReference(
        int Ordinal,
        XElement Element);

    private sealed record DocumentPropertyPackageDefinition(
        string Label,
        string RelationshipType,
        string PackagePart,
        string ContentType,
        XName RootElement);

    private sealed record TablePartReference(
        int Ordinal,
        string? RelationshipId);

    private sealed record TableColumnsReference(
        int Ordinal,
        XElement Element);

    private sealed record TableColumnReference(
        int Ordinal,
        XElement Element);

    private sealed record TableStyleInfoReference(
        int Ordinal,
        XElement Element);

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
