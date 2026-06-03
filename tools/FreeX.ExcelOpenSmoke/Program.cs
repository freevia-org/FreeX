using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
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
    private const int ExcelCellTypeAllValidation = -4174;
    private const int MaxDataValidationProbeCells = 20000;
    private const int MaxOpenXmlValidationErrorsToReport = 20;

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
                AssertOpenXmlValid(freeXSave.SavedPath, "FreeX-saved workbook");
                sourceForExcel = freeXSave.SavedPath;
                freeXSavedPath = freeXSave.SavedPath;
                freeXPreSave = freeXSave.Summary;
                freeXPreSaveWarnings = freeXSave.LoadWarnings;
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
                    freeXPreSaveWarnings,
                    null,
                    Array.Empty<string>());
            }

            var excelSavedPath = CreateDerivedOutputPath(excelSavedDirectory, stagedPath, "excel-saved");
            var saveReopenResult = OpenSaveCloseReopenWorkbook(workbooks, stagedPath, excelSavedPath);
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

    private static ExcelWorkbookSummary OpenWorkbook(dynamic workbooks, string stagedPath, bool readOnly)
    {
        object? workbook = null;
        var closed = false;
        try
        {
            workbook = OpenExcelWorkbook(workbooks, stagedPath, readOnly);
            ExcelWorkbookSummary contents;
            try
            {
                contents = CountWorkbookContents(workbook);
            }
            catch (COMException ex)
            {
                throw new InvalidDataException($"Excel content count failed for '{stagedPath}'", ex);
            }

            ((dynamic)workbook).Close(false);
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
            ExcelWorkbookSummary opened;
            try
            {
                opened = CountWorkbookContents(workbook);
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
                ((dynamic)workbook).SaveCopyAs(excelSavedPath);
            }
            catch (COMException ex)
            {
                throw new InvalidDataException($"Excel SaveCopyAs failed for '{stagedPath}'", ex);
            }

            AssertNoExcelRecoveryLog(excelSavedPath);
            ((dynamic)workbook).Close(false);
            workbookClosed = true;
            ReleaseComObject(workbook);
            workbook = null;
            CollectComReferences();
            AssertOpenXmlValid(excelSavedPath, "Excel-saved workbook");

            reopenedWorkbook = OpenExcelWorkbook(workbooks, excelSavedPath, readOnly: true);
            ExcelWorkbookSummary reopened;
            try
            {
                reopened = CountWorkbookContents(reopenedWorkbook);
            }
            catch (COMException ex)
            {
                throw new InvalidDataException($"Excel content count failed after reopening '{excelSavedPath}'", ex);
            }

            ((dynamic)reopenedWorkbook).Close(false);
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
            readOnly);

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

    private static ExcelWorkbookSummary CountWorkbookContents(object workbook)
    {
        object? worksheets = null;
        try
        {
            worksheets = ((dynamic)workbook).Worksheets;
            var worksheetCount = Convert.ToInt32(((dynamic)worksheets).Count, CultureInfo.InvariantCulture);
            var namedRangeCount = CountWorkbookUserDefinedNames(workbook);
            var chartCount = CountWorkbookChartSheets(workbook);
            var dataValidationCellCount = 0;
            var conditionalFormatCount = 0;
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
                    try
                    {
                        chartCount += CountWorksheetChartObjects(worksheet);
                        shapes = ((dynamic)worksheet).Shapes;
                        shapeCount += Convert.ToInt32(((dynamic)shapes).Count, CultureInfo.InvariantCulture);
                    }
                    catch (COMException ex)
                    {
                        throw new InvalidDataException($"Excel chart/shape count failed for worksheet index {index}", ex);
                    }

                    try
                    {
                        formulaCellCount += CountWorksheetFormulaCells(worksheet);
                    }
                    catch (COMException ex)
                    {
                        throw new InvalidDataException($"Excel formula count failed for worksheet index {index}", ex);
                    }

                    try
                    {
                        dataValidationCellCount += CountWorksheetDataValidationCells(worksheet);
                    }
                    catch (COMException ex)
                    {
                        throw new InvalidDataException($"Excel data-validation count failed for worksheet index {index}", ex);
                    }

                    try
                    {
                        conditionalFormatCount += CountWorksheetConditionalFormats(worksheet);
                    }
                    catch (COMException ex)
                    {
                        throw new InvalidDataException($"Excel conditional-format count failed for worksheet index {index}", ex);
                    }

                    try
                    {
                        listObjects = ((dynamic)worksheet).ListObjects;
                        structuredTableCount += Convert.ToInt32(((dynamic)listObjects).Count, CultureInfo.InvariantCulture);
                    }
                    catch (COMException ex)
                    {
                        throw new InvalidDataException($"Excel structured-table count failed for worksheet index {index}", ex);
                    }

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
                finally
                {
                    ReleaseComObject(pivotTables);
                    ReleaseComObject(listObjects);
                    ReleaseComObject(shapes);
                    ReleaseComObject(worksheet);
                }
            }

            return new ExcelWorkbookSummary(
                worksheetCount,
                namedRangeCount,
                chartCount,
                dataValidationCellCount,
                conditionalFormatCount,
                shapeCount,
                formulaCellCount,
                structuredTableCount,
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

    private static FreeXWorkbookSummary SummarizeWorkbook(Workbook workbook) =>
        new(
            workbook.SheetCount,
            workbook.Sheets.Sum(sheet => sheet.CellCount),
            workbook.Sheets.Sum(sheet => sheet.FormulaCellCount),
            workbook.NamedRanges.Count,
            workbook.Sheets.Sum(sheet => sheet.Charts.Count),
            workbook.Sheets.Sum(sheet => sheet.StructuredTables.Count),
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

        if (HasSupportedFeatureExpectations(row))
        {
            var supportedExpectations = SupportedCorpusExpectations(
                row,
                saveReopen,
                expectFreeXPreSave: workflow == WorkbookValidationWorkflow.FreeXSaveThenExcel);
            return ApplyFreeXLoadWarningExpectation(row, supportedExpectations);
        }

        return ApplyFreeXLoadWarningExpectation(row, null);
    }

    private static bool HasSupportedFeatureExpectations(CorpusManifestRow row) =>
        string.Equals(row.ExpectedStatus, "supported-pass", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(row.ExpectedStatus, "public-pass", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(row.ExpectedStatus, "supported-pivot-metadata-pass", StringComparison.OrdinalIgnoreCase);

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
        bool expectFreeXPreSave)
    {
        var tags = row.FeatureTags
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool HasTag(string tag) => tags.Contains(tag);
        var minFormulaCells = HasTag("formulas") ? 1 : 0;
        var minNamedRanges = HasTag("named-ranges") ? 1 : 0;
        var minStructuredTables = HasTag("structured-tables") || HasTag("listobjects") || HasTag("tables") ? 1 : 0;
        var minCharts = HasTag("charts") ? 1 : 0;
        var minDataValidations = HasTag("data-validation") ? 1 : 0;
        var minConditionalFormats = HasTag("conditional-formatting") ? 1 : 0;
        var minHyperlinks = HasTag("hyperlinks") ? 1 : 0;
        var minComments = HasTag("comments") || HasTag("notes") ? 1 : 0;
        var minPictures = HasTag("images") ? 1 : 0;
        var minSparklines = HasTag("sparklines") ? 1 : 0;
        var minTextBoxes = HasTag("text-boxes") ? 1 : 0;
        var minDrawingShapes = HasTag("shapes") ? 1 : 0;
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
            minCharts == 0 &&
            minDataValidations == 0 &&
            minConditionalFormats == 0 &&
            minHyperlinks == 0 &&
            minComments == 0 &&
            minPictures == 0 &&
            minSparklines == 0 &&
            minTextBoxes == 0 &&
            minDrawingShapes == 0 &&
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
            MinExcelOpenedFormulaCells: minFormulaCells,
            MinExcelOpenedStructuredTables: minStructuredTables,
            MinExcelOpenedDataValidationCells: minDataValidations > 0 ? 1 : 0,
            MinExcelOpenedConditionalFormats: minConditionalFormats,
            MinExcelOpenedShapes: minExcelShapes,
            MinExcelReopenedFormulaCells: saveReopen ? minFormulaCells : 0,
            MinExcelReopenedStructuredTables: saveReopen ? minStructuredTables : 0,
            MinExcelReopenedDataValidationCells: saveReopen && minDataValidations > 0 ? 1 : 0,
            MinExcelReopenedConditionalFormats: saveReopen ? minConditionalFormats : 0,
            MinExcelReopenedShapes: saveReopen ? minExcelShapes : 0,
            MinFreeXReopenedFormulaCells: saveReopen ? minFormulaCells : 0,
            MinFreeXReopenedStructuredTables: saveReopen ? minStructuredTables : 0,
            MinFreeXReopenedDataValidations: saveReopen ? minDataValidations : 0,
            MinFreeXReopenedConditionalFormats: saveReopen ? minConditionalFormats : 0,
            MinFreeXReopenedHyperlinks: saveReopen ? minHyperlinks : 0,
            MinFreeXReopenedComments: saveReopen ? minComments : 0,
            MinFreeXReopenedPictures: saveReopen ? minPictures : 0,
            MinFreeXReopenedSparklines: saveReopen ? minSparklines : 0,
            MinFreeXReopenedTextBoxes: saveReopen ? minTextBoxes : 0,
            MinFreeXReopenedDrawingShapes: saveReopen ? minDrawingShapes : 0,
            MinFreeXReopenedProtectedSheets: saveReopen ? minProtectedSheets : 0,
            MinFreeXReopenedStructureProtection: saveReopen ? minStructureProtection : 0,
            MinFreeXPreSavePivotTables: expectFreeXPreSave ? minPivotTables : 0,
            MinFreeXPreSavePivotCaches: expectFreeXPreSave ? minPivotCaches : 0,
            MinExcelOpenedPivotTables: minPivotTables,
            MinExcelOpenedNamedRanges: minNamedRanges,
            MinExcelOpenedCharts: minCharts,
            MinExcelReopenedPivotTables: saveReopen ? minPivotTables : 0,
            MinExcelReopenedNamedRanges: saveReopen ? minNamedRanges : 0,
            MinExcelReopenedCharts: saveReopen ? minCharts : 0,
            MinFreeXReopenedPivotTables: saveReopen ? minPivotTables : 0,
            MinFreeXReopenedPivotCaches: saveReopen ? minPivotCaches : 0,
            MinFreeXReopenedNamedRanges: saveReopen ? minNamedRanges : 0,
            MinFreeXReopenedCharts: saveReopen ? minCharts : 0);
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
            MinExcelOpenedShapes: 120,
            MinExcelReopenedFormulaCells: saveReopen ? 16000 : 0,
            MinExcelReopenedStructuredTables: saveReopen ? 1 : 0,
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
            MinExcelOpenedShapes: 2,
            MinExcelReopenedFormulaCells: saveReopen ? 1 : 0,
            MinExcelReopenedNamedRanges: saveReopen ? 1 : 0,
            MinExcelReopenedStructuredTables: saveReopen ? 1 : 0,
            MinExcelReopenedDataValidationCells: saveReopen ? 1 : 0,
            MinExcelReopenedConditionalFormats: saveReopen ? 1 : 0,
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
            MinExcelOpenedShapes: 1,
            MinExcelReopenedShapes: saveReopen ? 1 : 0,
            MinFreeXReopenedHyperlinks: saveReopen ? 3 : 0,
            MinFreeXReopenedComments: saveReopen ? 1 : 0);

    private static WorkbookSmokeExpectations ImagesSparklinesExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSavePictures: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveSparklines: expectFreeXPreSave ? 2 : 0,
            MinExcelOpenedShapes: 1,
            MinExcelReopenedShapes: saveReopen ? 1 : 0,
            MinFreeXReopenedPictures: saveReopen ? 1 : 0,
            MinFreeXReopenedSparklines: saveReopen ? 2 : 0);

    private static WorkbookSmokeExpectations ShapesTextExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSaveTextBoxes: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveDrawingShapes: expectFreeXPreSave ? 1 : 0,
            MinExcelOpenedShapes: 2,
            MinExcelReopenedShapes: saveReopen ? 2 : 0,
            MinFreeXReopenedTextBoxes: saveReopen ? 1 : 0,
            MinFreeXReopenedDrawingShapes: saveReopen ? 1 : 0);

    private static WorkbookSmokeExpectations ProtectionPageExpectations(bool saveReopen, bool expectFreeXPreSave) =>
        new(
            MinFreeXPreSaveProtectedSheets: expectFreeXPreSave ? 1 : 0,
            MinFreeXPreSaveStructureProtection: expectFreeXPreSave ? 1 : 0,
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
            WriteFreeXSummary("FreeX source load", freeXPreSave);
        WriteFreeXWarnings("FreeX source load", result.FreeXPreSaveWarnings);

        if (result.Opened is { } opened)
        {
            Console.WriteLine(
                $"  Excel open: worksheets {opened.WorksheetCount}; named ranges {opened.NamedRangeCount}; formulas {opened.FormulaCellCount}; tables {opened.StructuredTableCount}; charts {opened.ChartCount}; validation cells {opened.DataValidationCellCount}; conditional formats {opened.ConditionalFormatCount}; worksheet shapes {opened.ShapeCount}; pivots {opened.PivotTableCount}");
        }
        if (result.Reopened is { } reopened)
        {
            Console.WriteLine(
                $"  Excel reopen: worksheets {reopened.WorksheetCount}; named ranges {reopened.NamedRangeCount}; formulas {reopened.FormulaCellCount}; tables {reopened.StructuredTableCount}; charts {reopened.ChartCount}; validation cells {reopened.DataValidationCellCount}; conditional formats {reopened.ConditionalFormatCount}; worksheet shapes {reopened.ShapeCount}; pivots {reopened.PivotTableCount}");
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
            $"  {label}: sheets {summary.SheetCount}; cells {summary.CellCount}; named ranges {summary.NamedRangeCount}; formulas {summary.FormulaCellCount}; tables {summary.StructuredTableCount}; charts {summary.ChartCount}; pivots {summary.PivotTableCount}; pivot caches {summary.PivotCacheCount}");
        Console.WriteLine(
            $"  {label} metadata: validations {summary.DataValidationCount}; conditional formats {summary.ConditionalFormatCount}; hyperlinks {summary.HyperlinkCount}; comments {summary.CommentCount}; pictures {summary.PictureCount}; sparklines {summary.SparklineCount}; text boxes {summary.TextBoxCount}; drawing shapes {summary.DrawingShapeCount}; protected sheets {summary.ProtectedSheetCount}; structure protection {summary.StructureProtectionCount}");
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

}
