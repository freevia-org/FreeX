using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using FreeX.Core.IO;
using FreeX.Core.Model;
using static ExcelSmokeCom;
using static ExcelSmokeFixtures;
using static SmokeUsage;

Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

return ExcelOpenSmoke.Run(args);

internal static class ExcelOpenSmoke
{
    private const uint ExcelOpenRejectedHResult = 0x800A03ECu;
    private const int ExcelCellTypeFormulas = -4123;

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
                        DescribeGeneratedFixture("FreeX chart fixture", generatedWorkflow)));
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

            var inputWorkflow = options.FreeXResaveBeforeExcel
                ? WorkbookValidationWorkflow.FreeXSaveThenExcel
                : WorkbookValidationWorkflow.DirectExcel;
            if (options.CorpusManifestPath is not null)
            {
                corpusSelection = CorpusManifestResolver.Resolve(options, inputWorkflow);
                foreach (var input in corpusSelection.Inputs)
                    AddUniqueInput(smokeInputs, input);
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

        try
        {
            if (input.GenerateWithExcel)
                GenerateExcelAuthoredFixture(workbooks, input.SourcePath);

            if (input.Workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel)
            {
                var freeXSave = SaveThroughFreeX(input.SourcePath, freeXSavedDirectory);
                sourceForExcel = freeXSave.SavedPath;
                freeXSavedPath = freeXSave.SavedPath;
                freeXPreSave = freeXSave.Summary;
            }

            var stagedPath = CopyToStagingDirectory(sourceForExcel, stagingDirectory);
            if (!saveReopen)
            {
                var opened = OpenWorkbook(workbooks, stagedPath, readOnly: true);
                AssertSmokeExpectations(input, freeXPreSave, opened, null, null);
                return WorkbookSmokeResult.Pass(
                    input,
                    stagedPath,
                    freeXSavedPath,
                    null,
                    opened,
                    null,
                    freeXPreSave,
                    null);
            }

            var excelSavedPath = CreateDerivedOutputPath(excelSavedDirectory, stagedPath, "excel-saved");
            var saveReopenResult = OpenSaveCloseReopenWorkbook(workbooks, stagedPath, excelSavedPath);
            var freeXReopenedExcelSave = LoadWorkbookSummary(saveReopenResult.ExcelSavedPath);
            AssertSmokeExpectations(input, freeXPreSave, saveReopenResult.Opened, saveReopenResult.Reopened, freeXReopenedExcelSave);

            return WorkbookSmokeResult.Pass(
                input,
                stagedPath,
                freeXSavedPath,
                saveReopenResult.ExcelSavedPath,
                saveReopenResult.Opened,
                saveReopenResult.Reopened,
                freeXPreSave,
                freeXReopenedExcelSave);
        }
        catch (Exception ex)
        {
            return WorkbookSmokeResult.Fail(
                input,
                freeXSavedPath,
                FormatFailure(ex));
        }
    }

    private static ExcelWorkbookSummary OpenWorkbook(dynamic workbooks, string stagedPath, bool readOnly)
    {
        object? workbook = null;
        var closed = false;
        try
        {
            workbook = OpenExcelWorkbook(workbooks, stagedPath, readOnly);
            var contents = CountWorkbookContents(workbook);
            ((dynamic)workbook).Close(false);
            closed = true;
            return contents;
        }
        finally
        {
            try
            {
                if (workbook is not null && !closed)
                    ((dynamic)workbook).Close(false);
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
        string excelSavedPath)
    {
        object? workbook = null;
        object? reopenedWorkbook = null;
        var workbookClosed = false;
        var reopenedClosed = false;

        try
        {
            workbook = OpenExcelWorkbook(workbooks, stagedPath, readOnly: false);
            var opened = CountWorkbookContents(workbook);

            Directory.CreateDirectory(Path.GetDirectoryName(excelSavedPath)!);
            if (File.Exists(excelSavedPath))
                File.Delete(excelSavedPath);

            ((dynamic)workbook).SaveCopyAs(excelSavedPath);
            AssertNoExcelRecoveryLog(excelSavedPath);
            ((dynamic)workbook).Close(false);
            workbookClosed = true;
            ReleaseComObject(workbook);
            workbook = null;
            CollectComReferences();

            reopenedWorkbook = OpenExcelWorkbook(workbooks, excelSavedPath, readOnly: true);
            var reopened = CountWorkbookContents(reopenedWorkbook);
            ((dynamic)reopenedWorkbook).Close(false);
            reopenedClosed = true;

            return new ExcelSaveReopenResult(excelSavedPath, opened, reopened);
        }
        finally
        {
            try
            {
                if (workbook is not null && !workbookClosed)
                    ((dynamic)workbook).Close(false);
            }
            catch
            {
                // Best-effort cleanup; orphaned Excel processes are handled separately.
            }

            try
            {
                if (reopenedWorkbook is not null && !reopenedClosed)
                    ((dynamic)reopenedWorkbook).Close(false);
            }
            catch
            {
                // Best-effort cleanup; orphaned Excel processes are handled separately.
            }

            ReleaseComObject(reopenedWorkbook);
            ReleaseComObject(workbook);
        }
    }

    private static object OpenExcelWorkbook(dynamic workbooks, string path, bool readOnly) =>
        workbooks.Open(
            path,
            0,
            readOnly,
            Missing.Value,
            Missing.Value,
            Missing.Value,
            true,
            Missing.Value,
            Missing.Value,
            false,
            false,
            Missing.Value,
            false,
            true,
            0);

    private static void AssertNoExcelRecoveryLog(string xlsxPath)
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
                $"Excel saved copy contains repair/recovery log parts: {string.Join(", ", recoveryLogs)}");
        }
    }

    private static ExcelWorkbookSummary CountWorkbookContents(object workbook)
    {
        object? worksheets = null;
        try
        {
            worksheets = ((dynamic)workbook).Worksheets;
            var worksheetCount = Convert.ToInt32(((dynamic)worksheets).Count, CultureInfo.InvariantCulture);
            var shapeCount = 0;
            var formulaCellCount = 0;
            var structuredTableCount = 0;
            var pivotTableCount = 0;

            for (var index = 1; index <= worksheetCount; index++)
            {
                object? worksheet = null;
                object? shapes = null;
                object? listObjects = null;
                object? pivotTables = null;
                try
                {
                    worksheet = ((dynamic)worksheets)[index];
                    shapes = ((dynamic)worksheet).Shapes;
                    shapeCount += Convert.ToInt32(((dynamic)shapes).Count, CultureInfo.InvariantCulture);
                    formulaCellCount += CountWorksheetFormulaCells(worksheet);
                    listObjects = ((dynamic)worksheet).ListObjects;
                    structuredTableCount += Convert.ToInt32(((dynamic)listObjects).Count, CultureInfo.InvariantCulture);
                    pivotTables = ((dynamic)worksheet).PivotTables();
                    pivotTableCount += Convert.ToInt32(((dynamic)pivotTables).Count, CultureInfo.InvariantCulture);
                }
                finally
                {
                    ReleaseComObject(pivotTables);
                    ReleaseComObject(listObjects);
                    ReleaseComObject(shapes);
                    ReleaseComObject(worksheet);
                }
            }

            return new ExcelWorkbookSummary(worksheetCount, shapeCount, formulaCellCount, structuredTableCount, pivotTableCount);
        }
        finally
        {
            ReleaseComObject(worksheets);
        }
    }

    private static int CountWorksheetFormulaCells(object worksheet)
    {
        object? usedRange = null;
        object? formulaCells = null;
        try
        {
            usedRange = ((dynamic)worksheet).UsedRange;
            formulaCells = ((dynamic)usedRange).SpecialCells(ExcelCellTypeFormulas);
            return Convert.ToInt32(((dynamic)formulaCells).Count, CultureInfo.InvariantCulture);
        }
        catch (COMException ex) when ((uint)ex.HResult == ExcelOpenRejectedHResult)
        {
            return 0;
        }
        finally
        {
            ReleaseComObject(formulaCells);
            ReleaseComObject(usedRange);
        }
    }

    private static FreeXSaveResult SaveThroughFreeX(string sourcePath, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var adapter = new XlsxFileAdapter();
        Workbook workbook;
        using (var input = File.OpenRead(sourcePath))
        {
            workbook = adapter.Load(input);
        }

        var summary = SummarizeWorkbook(workbook);
        AddFreeXSaveMarker(workbook);
        var outputPath = CreateDerivedOutputPath(outputDirectory, sourcePath, "freex-saved");
        using (var output = File.Create(outputPath))
        {
            adapter.Save(workbook, output);
        }

        return new FreeXSaveResult(outputPath, summary);
    }

    private static FreeXWorkbookSummary LoadWorkbookSummary(string sourcePath)
    {
        using var input = File.OpenRead(sourcePath);
        var workbook = new XlsxFileAdapter().Load(input);
        return SummarizeWorkbook(workbook);
    }

    private static FreeXWorkbookSummary SummarizeWorkbook(Workbook workbook) =>
        new(
            workbook.SheetCount,
            workbook.Sheets.Sum(sheet => sheet.CellCount),
            workbook.Sheets.Sum(sheet => sheet.FormulaCellCount),
            workbook.Sheets.Sum(sheet => sheet.StructuredTables.Count),
            workbook.Sheets.Sum(sheet => sheet.PivotTables.Count),
            workbook.PivotCaches.Count);

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

    private static WorkbookSmokeExpectations? ExpectationsForGeneratedFixture(
        string generatedFile,
        bool saveReopen,
        WorkbookValidationWorkflow workflow)
    {
        var fileName = Path.GetFileName(generatedFile);
        var expectFreeXPreSave = workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel;

        if (fileName.Contains("grid_formulas", StringComparison.OrdinalIgnoreCase))
            return FormulaExpectations(saveReopen, expectFreeXPreSave, minFormulaCells: 4);

        if (fileName.Contains("tables", StringComparison.OrdinalIgnoreCase))
            return StructuredTableExpectations(saveReopen, expectFreeXPreSave, minStructuredTables: 1);

        if (fileName.Contains("objects_links", StringComparison.OrdinalIgnoreCase))
            return ShapeExpectations(saveReopen, minShapes: 1);

        if (fileName.Contains("images_sparklines", StringComparison.OrdinalIgnoreCase))
            return ShapeExpectations(saveReopen, minShapes: 1);

        if (fileName.Contains("shapes_text", StringComparison.OrdinalIgnoreCase))
            return ShapeExpectations(saveReopen, minShapes: 2);

        if (fileName.Contains("pivots", StringComparison.OrdinalIgnoreCase))
            return PivotTableExpectations(saveReopen, expectFreeXPreSave);

        return null;
    }

    private static WorkbookSmokeExpectations ExcelAuthoredFixtureExpectations(bool saveReopen) =>
        new(
            MinFreeXPreSaveFormulaCells: 1,
            MinFreeXPreSaveStructuredTables: 1,
            MinExcelOpenedFormulaCells: 1,
            MinExcelOpenedStructuredTables: 1,
            MinExcelOpenedShapes: 2,
            MinExcelReopenedFormulaCells: saveReopen ? 1 : 0,
            MinExcelReopenedStructuredTables: saveReopen ? 1 : 0,
            MinExcelReopenedShapes: saveReopen ? 2 : 0,
            MinFreeXReopenedFormulaCells: saveReopen ? 1 : 0,
            MinFreeXReopenedStructuredTables: saveReopen ? 1 : 0,
            MinFreeXPreSavePivotTables: 1,
            MinFreeXPreSavePivotCaches: 1,
            MinExcelOpenedPivotTables: 1,
            MinExcelReopenedPivotTables: saveReopen ? 1 : 0,
            MinFreeXReopenedPivotTables: saveReopen ? 1 : 0,
            MinFreeXReopenedPivotCaches: saveReopen ? 1 : 0);

    private static WorkbookSmokeExpectations FormulaExpectations(
        bool saveReopen,
        bool expectFreeXPreSave,
        int minFormulaCells) =>
        new(
            MinFreeXPreSaveFormulaCells: expectFreeXPreSave ? minFormulaCells : 0,
            MinExcelOpenedFormulaCells: minFormulaCells,
            MinExcelReopenedFormulaCells: saveReopen ? minFormulaCells : 0,
            MinFreeXReopenedFormulaCells: saveReopen ? minFormulaCells : 0);

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
            "FreeX source load structured tables",
            freeXPreSave?.StructuredTableCount,
            expectations.MinFreeXPreSaveStructuredTables,
            input);
        AssertMin(
            "Excel open formula cells",
            opened.FormulaCellCount,
            expectations.MinExcelOpenedFormulaCells,
            input);
        AssertMin(
            "Excel open structured tables",
            opened.StructuredTableCount,
            expectations.MinExcelOpenedStructuredTables,
            input);
        AssertMin(
            "Excel open worksheet shapes",
            opened.ShapeCount,
            expectations.MinExcelOpenedShapes,
            input);
        AssertMin(
            "Excel reopen formula cells",
            reopened?.FormulaCellCount,
            expectations.MinExcelReopenedFormulaCells,
            input);
        AssertMin(
            "Excel reopen structured tables",
            reopened?.StructuredTableCount,
            expectations.MinExcelReopenedStructuredTables,
            input);
        AssertMin(
            "Excel reopen worksheet shapes",
            reopened?.ShapeCount,
            expectations.MinExcelReopenedShapes,
            input);
        AssertMin(
            "FreeX reopened Excel save formula cells",
            freeXReopenedExcelSave?.FormulaCellCount,
            expectations.MinFreeXReopenedFormulaCells,
            input);
        AssertMin(
            "FreeX reopened Excel save structured tables",
            freeXReopenedExcelSave?.StructuredTableCount,
            expectations.MinFreeXReopenedStructuredTables,
            input);
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
        if (result.StagedPath is not null)
            Console.WriteLine($"  Staged: {result.StagedPath}");
        if (result.ExcelSavedPath is not null)
            Console.WriteLine($"  Excel saved: {result.ExcelSavedPath}");

        if (result.FreeXPreSave is { } freeXPreSave)
        {
            Console.WriteLine(
                $"  FreeX source load: sheets {freeXPreSave.SheetCount}; cells {freeXPreSave.CellCount}; formulas {freeXPreSave.FormulaCellCount}; tables {freeXPreSave.StructuredTableCount}; pivots {freeXPreSave.PivotTableCount}; pivot caches {freeXPreSave.PivotCacheCount}");
        }

        if (result.Opened is { } opened)
        {
            Console.WriteLine(
                $"  Excel open: worksheets {opened.WorksheetCount}; formulas {opened.FormulaCellCount}; tables {opened.StructuredTableCount}; worksheet shapes {opened.ShapeCount}; pivots {opened.PivotTableCount}");
        }
        if (result.Reopened is { } reopened)
        {
            Console.WriteLine(
                $"  Excel reopen: worksheets {reopened.WorksheetCount}; formulas {reopened.FormulaCellCount}; tables {reopened.StructuredTableCount}; worksheet shapes {reopened.ShapeCount}; pivots {reopened.PivotTableCount}");
        }
        if (result.FreeXReopenedExcelSave is { } freeXReopened)
        {
            Console.WriteLine(
                $"  FreeX reopened Excel save: sheets {freeXReopened.SheetCount}; cells {freeXReopened.CellCount}; formulas {freeXReopened.FormulaCellCount}; tables {freeXReopened.StructuredTableCount}; pivots {freeXReopened.PivotTableCount}; pivot caches {freeXReopened.PivotCacheCount}");
        }

        if (!result.Success)
            Console.WriteLine($"  Error: {result.Error}");
    }

    private static string FormatWorkflow(WorkbookValidationWorkflow workflow) =>
        workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel
            ? "FreeX load/save -> Excel"
            : "Excel";

    private static string FormatFailure(Exception ex)
    {
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
                freeXReopenedExcelSave = result.FreeXReopenedExcelSave,
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

}
