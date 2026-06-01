using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using FreeX.Core.IO;
using FreeX.Core.Model;

Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

return ExcelOpenSmoke.Run(args);

internal static class ExcelOpenSmoke
{
    private const uint ExcelOpenRejectedHResult = 0x800A03ECu;
    private const int XlOpenXmlWorkbook = 51;
    private const int XlNoChange = 1;
    private const int XlLocalSessionChanges = 2;

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

            if (!options.HasGeneratedFixtures && options.Inputs.Count == 0)
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
            if (options.GenerateChartFixtures)
            {
                foreach (var generatedFile in GenerateChartFixtures(Path.Combine(runDirectory, "generated")))
                {
                    AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                        generatedFile,
                        WorkbookValidationWorkflow.DirectExcel,
                        "FreeX chart fixture"));
                }
            }

            if (options.GenerateFreexFixture)
            {
                var generatedFile = GenerateFreeXNonChartFixture(Path.Combine(runDirectory, "generated"));
                AddUniqueInput(smokeInputs, new WorkbookSmokeInput(
                    generatedFile,
                    WorkbookValidationWorkflow.DirectExcel,
                    "FreeX non-chart fixture"));
            }

            var inputFiles = ResolveInputFiles(options.Inputs, options.Pattern);
            var inputWorkflow = options.FreeXResaveBeforeExcel
                ? WorkbookValidationWorkflow.FreeXSaveThenExcel
                : WorkbookValidationWorkflow.DirectExcel;
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
                    GenerateWithExcel: true));
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

            var result = RunExcelSmoke(smokeInputs, runDirectory, options.SaveReopen);
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
            results.Count(result => !result.Success));
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

    private static ExcelWorkbookSummary CountWorkbookContents(object workbook)
    {
        object? worksheets = null;
        try
        {
            worksheets = ((dynamic)workbook).Worksheets;
            var worksheetCount = Convert.ToInt32(((dynamic)worksheets).Count, CultureInfo.InvariantCulture);
            var shapeCount = 0;

            for (var index = 1; index <= worksheetCount; index++)
            {
                object? worksheet = null;
                object? shapes = null;
                try
                {
                    worksheet = ((dynamic)worksheets)[index];
                    shapes = ((dynamic)worksheet).Shapes;
                    shapeCount += Convert.ToInt32(((dynamic)shapes).Count, CultureInfo.InvariantCulture);
                }
                finally
                {
                    ReleaseComObject(shapes);
                    ReleaseComObject(worksheet);
                }
            }

            return new ExcelWorkbookSummary(worksheetCount, shapeCount);
        }
        finally
        {
            ReleaseComObject(worksheets);
        }
    }

    private static IReadOnlyList<string> GenerateChartFixtures(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var generated = new List<string>
        {
            SaveWorkbook(CreateHistogramWorkbook(), Path.Combine(outputDirectory, "FreeX_histogram_smoke.xlsx")),
            SaveWorkbook(CreateWaterfallWorkbook(), Path.Combine(outputDirectory, "FreeX_waterfall_smoke.xlsx")),
        };

        foreach (var file in generated)
            Console.WriteLine($"Generated: {file}");

        return generated;
    }

    private static string GenerateFreeXNonChartFixture(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var generated = SaveWorkbook(CreateNonChartWorkbook(), Path.Combine(outputDirectory, "FreeX_nonchart_smoke.xlsx"));
        Console.WriteLine($"Generated: {generated}");
        return generated;
    }

    private static void GenerateExcelAuthoredFixture(dynamic workbooks, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        object? workbook = null;
        object? worksheet = null;
        try
        {
            workbook = workbooks.Add();
            worksheet = ((dynamic)workbook).Worksheets[1];
            ((dynamic)worksheet).Name = "ExcelData";

            SetExcelCellValue(worksheet, 1, 1, "Item");
            SetExcelCellValue(worksheet, 1, 2, "Amount");
            SetExcelCellValue(worksheet, 1, 3, "When");
            SetExcelCellValue(worksheet, 1, 4, "Complete");

            SetExcelCellValue(worksheet, 2, 1, "Alpha");
            SetExcelCellValue(worksheet, 2, 2, 125.50);
            SetExcelCellValue(worksheet, 2, 3, new DateTime(2026, 6, 1).ToOADate());
            SetExcelCellValue(worksheet, 2, 4, true);

            SetExcelCellValue(worksheet, 3, 1, "Beta");
            SetExcelCellValue(worksheet, 3, 2, 88.25);
            SetExcelCellValue(worksheet, 3, 3, new DateTime(2026, 6, 2).ToOADate());
            SetExcelCellValue(worksheet, 3, 4, false);

            SetExcelCellValue(worksheet, 4, 1, "Gamma");
            SetExcelCellValue(worksheet, 4, 2, 210.00);
            SetExcelCellValue(worksheet, 4, 3, new DateTime(2026, 6, 3).ToOADate());
            SetExcelCellValue(worksheet, 4, 4, true);

            SetExcelCellValue(worksheet, 6, 1, "Total");
            SetExcelCellFormula(worksheet, 6, 2, "=SUM(B2:B4)");
            ApplyExcelRangeFormat(worksheet, "A1:D1", range =>
            {
                range.Font.Bold = true;
                range.Font.Color = ToOleColor(255, 255, 255);
                range.Interior.Color = ToOleColor(31, 78, 121);
            });
            ApplyExcelRangeFormat(worksheet, "B2:B6", range => range.NumberFormat = "$#,##0.00");
            ApplyExcelRangeFormat(worksheet, "C2:C4", range => range.NumberFormat = "yyyy-mm-dd");
            AutoFitExcelColumns(worksheet, "A:D");

            ((dynamic)workbook).SaveAs(
                outputPath,
                XlOpenXmlWorkbook,
                Missing.Value,
                Missing.Value,
                false,
                false,
                XlNoChange,
                XlLocalSessionChanges,
                false,
                Missing.Value,
                Missing.Value,
                true);

            ((dynamic)workbook).Close(false);
            Console.WriteLine($"Generated: {outputPath}");
        }
        finally
        {
            try
            {
                if (workbook is not null)
                    ((dynamic)workbook).Close(false);
            }
            catch
            {
                // The workbook may already be closed after SaveAs.
            }

            ReleaseComObject(worksheet);
            ReleaseComObject(workbook);
        }
    }

    private static string SaveWorkbook(Workbook workbook, string path)
    {
        using var stream = File.Create(path);
        new XlsxFileAdapter().Save(workbook, stream);
        return path;
    }

    private static Workbook CreateNonChartWorkbook()
    {
        var workbook = new Workbook("FreeXNonChartSmoke");
        var sheet = workbook.AddSheet("Data");
        sheet.FrozenRows = 1;
        sheet.ColumnWidths[1] = 16;
        sheet.ColumnWidths[2] = 12;
        sheet.ColumnWidths[3] = 14;
        sheet.ColumnWidths[4] = 12;

        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = CellColor.White,
            FillColor = CellColor.FromArgb(31, 78, 121),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        var moneyStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "$#,##0.00" });
        var percentStyle = workbook.RegisterStyle(new CellStyle { NumberFormat = "0.0%" });

        SetStyledCell(sheet, 1, 1, new TextValue("Region"), headerStyle);
        SetStyledCell(sheet, 1, 2, new TextValue("Units"), headerStyle);
        SetStyledCell(sheet, 1, 3, new TextValue("Revenue"), headerStyle);
        SetStyledCell(sheet, 1, 4, new TextValue("Margin"), headerStyle);

        (string Region, double Units, double Revenue, double Margin)[] rows =
        [
            ("North", 42, 12500.25, 0.18),
            ("South", 37, 9800.00, 0.16),
            ("East", 55, 14210.75, 0.21),
            ("West", 31, 8700.50, 0.14),
            ("Online", 64, 21300.00, 0.27),
        ];

        var totalRevenue = 0.0;
        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)index + 2;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(rows[index].Region));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(rows[index].Units));
            SetStyledCell(sheet, row, 3, new NumberValue(rows[index].Revenue), moneyStyle);
            SetStyledCell(sheet, row, 4, new NumberValue(rows[index].Margin), percentStyle);
            totalRevenue += rows[index].Revenue;
        }

        sheet.SetCell(new CellAddress(sheet.Id, 8, 2), new TextValue("Total revenue"));
        var totalCell = Cell.FromFormula("SUM(C2:C6)");
        totalCell.Value = new NumberValue(totalRevenue);
        totalCell.StyleId = moneyStyle;
        sheet.SetCell(new CellAddress(sheet.Id, 8, 3), totalCell);
        sheet.Comments[new CellAddress(sheet.Id, 8, 3)] = "Cached formula value included for Excel reopen validation.";
        sheet.Hyperlinks[new CellAddress(sheet.Id, 10, 1)] = "https://github.com/tony-xmelon/FreeX";
        sheet.HyperlinkMetadata[new CellAddress(sheet.Id, 10, 1)] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "FreeX repository",
            "");
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new TextValue("FreeX project"));

        var summary = workbook.AddSheet("Summary");
        summary.SetCell(new CellAddress(summary.Id, 1, 1), new TextValue("Workbook"));
        summary.SetCell(new CellAddress(summary.Id, 1, 2), new TextValue("FreeX non-chart smoke"));
        summary.SetCell(new CellAddress(summary.Id, 2, 1), new TextValue("Generated"));
        summary.SetCell(new CellAddress(summary.Id, 2, 2), new TextValue("2026-06-01"));

        workbook.DefineNamedRange(
            "SalesData",
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 4)));

        return workbook;
    }

    private static void SetStyledCell(Sheet sheet, uint row, uint col, ScalarValue value, StyleId styleId)
    {
        var cell = Cell.FromValue(value);
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, row, col), cell);
    }

    private static Workbook CreateHistogramWorkbook()
    {
        var workbook = new Workbook("HistogramSmoke");
        var sheet = workbook.AddSheet("Histogram");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Value"));

        double[] values = [4, 7, 9, 11, 12, 16, 18, 19, 23, 27, 32, 38, 41, 47];
        for (var index = 0; index < values.Length; index++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)index + 2, 1), new NumberValue(values[index]));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Histogram,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)values.Length + 1, 1)),
            Title = "Histogram Smoke",
            ShowLegend = false,
            HistogramBinning = new HistogramBinningModel(HistogramBinningMode.BinCount, BinCount: 4),
            Left = 320,
            Top = 40,
            Width = 500,
            Height = 320,
        });

        return workbook;
    }

    private static Workbook CreateWaterfallWorkbook()
    {
        var workbook = new Workbook("WaterfallSmoke");
        var sheet = workbook.AddSheet("Waterfall");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Step"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));

        (string Label, double Amount)[] rows =
        [
            ("Opening", 120),
            ("Sales", 45),
            ("Returns", -18),
            ("Costs", -32),
            ("Closing", 115),
        ];

        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)index + 2;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(rows[index].Label));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(rows[index].Amount));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Waterfall,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)rows.Length + 1, 2)),
            Title = "Waterfall Smoke",
            ShowLegend = false,
            WaterfallTotalPointIndices = [0, rows.Length - 1],
            Left = 320,
            Top = 40,
            Width = 500,
            Height = 320,
        });

        return workbook;
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
            workbook.Sheets.Sum(sheet => sheet.FormulaCellCount));

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

    private static void WriteWorkbookReport(WorkbookSmokeResult result, bool saveReopen)
    {
        var status = result.Success
            ? saveReopen ? "SAVE-REOPEN OK" : "OPEN OK"
            : saveReopen ? "SAVE-REOPEN FAILED" : "OPEN FAILED";

        Console.WriteLine($"{status}: {result.Input.SourcePath}");
        Console.WriteLine($"  Source: {result.Input.Description}; workflow: {FormatWorkflow(result.Input.Workflow)}");
        if (result.FreeXSavedPath is not null)
            Console.WriteLine($"  FreeX saved: {result.FreeXSavedPath}");
        if (result.StagedPath is not null)
            Console.WriteLine($"  Staged: {result.StagedPath}");
        if (result.ExcelSavedPath is not null)
            Console.WriteLine($"  Excel saved: {result.ExcelSavedPath}");

        if (result.FreeXPreSave is { } freeXPreSave)
        {
            Console.WriteLine(
                $"  FreeX source load: sheets {freeXPreSave.SheetCount}; cells {freeXPreSave.CellCount}; formulas {freeXPreSave.FormulaCellCount}");
        }

        if (result.Opened is { } opened)
            Console.WriteLine($"  Excel open: worksheets {opened.WorksheetCount}; worksheet shapes {opened.ShapeCount}");
        if (result.Reopened is { } reopened)
            Console.WriteLine($"  Excel reopen: worksheets {reopened.WorksheetCount}; worksheet shapes {reopened.ShapeCount}");
        if (result.FreeXReopenedExcelSave is { } freeXReopened)
        {
            Console.WriteLine(
                $"  FreeX reopened Excel save: sheets {freeXReopened.SheetCount}; cells {freeXReopened.CellCount}; formulas {freeXReopened.FormulaCellCount}");
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

    private static void SetExcelCellValue(object worksheet, int row, int col, object value)
    {
        object? cell = null;
        try
        {
            cell = ((dynamic)worksheet).Cells[row, col];
            ((dynamic)cell).Value2 = value;
        }
        finally
        {
            ReleaseComObject(cell);
        }
    }

    private static void SetExcelCellFormula(object worksheet, int row, int col, string formula)
    {
        object? cell = null;
        try
        {
            cell = ((dynamic)worksheet).Cells[row, col];
            ((dynamic)cell).Formula = formula;
        }
        finally
        {
            ReleaseComObject(cell);
        }
    }

    private static void ApplyExcelRangeFormat(object worksheet, string address, Action<dynamic> apply)
    {
        object? range = null;
        try
        {
            range = ((dynamic)worksheet).Range(address);
            apply((dynamic)range);
        }
        finally
        {
            ReleaseComObject(range);
        }
    }

    private static void AutoFitExcelColumns(object worksheet, string address)
    {
        object? range = null;
        object? columns = null;
        try
        {
            range = ((dynamic)worksheet).Range(address);
            columns = ((dynamic)range).Columns;
            ((dynamic)columns).AutoFit();
        }
        finally
        {
            ReleaseComObject(columns);
            ReleaseComObject(range);
        }
    }

    private static int ToOleColor(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    private static void TrySetAutomationSecurity(dynamic excelApp)
    {
        try
        {
            excelApp.AutomationSecurity = 3;
        }
        catch
        {
            // Older Excel builds can reject this property; DisplayAlerts=false still covers the smoke.
        }
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

    private static HashSet<int> GetExcelProcessIds() =>
        Process.GetProcessesByName("EXCEL")
            .Select(process =>
            {
                using (process)
                    return process.Id;
            })
            .ToHashSet();

    private static int? TryGetExcelProcessId(object excel)
    {
        try
        {
            var hwnd = Convert.ToInt64(((dynamic)excel).Hwnd, CultureInfo.InvariantCulture);
            if (hwnd == 0)
                return null;

            _ = GetWindowThreadProcessId(new IntPtr(hwnd), out var processId);
            return processId == 0 ? null : processId;
        }
        catch
        {
            return null;
        }
    }

    private static void KillOrphanExcelProcesses(HashSet<int> baselineExcelPids, int? excelPid)
    {
        var candidatePids = new HashSet<int>();
        if (excelPid is { } trackedPid && !baselineExcelPids.Contains(trackedPid))
            candidatePids.Add(trackedPid);

        foreach (var process in Process.GetProcessesByName("EXCEL"))
        {
            using (process)
            {
                if (!baselineExcelPids.Contains(process.Id))
                    candidatePids.Add(process.Id);
            }
        }

        foreach (var pid in candidatePids)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
                Console.WriteLine($"Killed orphan EXCEL PID {pid}.");
            }
            catch (ArgumentException)
            {
                // Process already exited.
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to kill orphan EXCEL PID {pid}: {ex.Message}");
            }
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
            return;

        try
        {
            Marshal.FinalReleaseComObject(value);
        }
        catch
        {
            // Cleanup best effort; orphaned Excel processes are handled separately.
        }
    }

    private static void CollectComReferences()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- [options] <xlsx-file-or-directory> [...]

            Options:
              --save-reopen                 Open each workbook in Excel, SaveCopyAs, close, reopen in Excel,
                                            and load the Excel-saved copy through FreeX.
              --generate-freex-fixture      Generate a non-chart FreeX XLSX smoke file.
              --generate-chart-fixtures     Generate FreeX histogram and waterfall XLSX smoke files.
              --generate-excel-fixture      Generate an Excel-authored XLSX fixture through COM, then load/save it through FreeX.
              --freex-resave-before-excel   For user inputs, load/save through FreeX before Excel validation.
              --out <directory>             Run output directory. Must be under %USERPROFILE%.
              --pattern <glob>              Directory input glob. Defaults to *.xlsx.
              --help                        Show this help text.

            Examples:
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --generate-freex-fixture --generate-excel-fixture
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --generate-chart-fixtures
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --save-reopen --freex-resave-before-excel C:\Users\quinn\freex-xlsx-verify\excel-authored.xlsx
            """);
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
}

internal sealed record SmokeOptions(
    bool ShowHelp,
    bool SaveReopen,
    bool GenerateChartFixtures,
    bool GenerateFreexFixture,
    bool GenerateExcelFixture,
    bool FreeXResaveBeforeExcel,
    string? OutputDirectory,
    string Pattern,
    IReadOnlyList<string> Inputs)
{
    public bool HasGeneratedFixtures => GenerateChartFixtures || GenerateFreexFixture || GenerateExcelFixture;

    public static SmokeOptions Parse(string[] args)
    {
        var saveReopen = false;
        var generateChartFixtures = false;
        var generateFreexFixture = false;
        var generateExcelFixture = false;
        var freeXResaveBeforeExcel = false;
        string? outputDirectory = null;
        var pattern = "*.xlsx";
        var inputs = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    return new SmokeOptions(true, false, false, false, false, false, null, pattern, []);
                case "--save-reopen":
                    saveReopen = true;
                    break;
                case "--generate-chart-fixtures":
                    generateChartFixtures = true;
                    break;
                case "--generate-freex-fixture":
                    generateFreexFixture = true;
                    break;
                case "--generate-excel-fixture":
                    generateExcelFixture = true;
                    break;
                case "--freex-resave-before-excel":
                    freeXResaveBeforeExcel = true;
                    break;
                case "--out":
                    outputDirectory = ReadOptionValue(args, ref index, arg);
                    break;
                case "--pattern":
                    pattern = ReadOptionValue(args, ref index, arg);
                    break;
                default:
                    if (arg.StartsWith("-", StringComparison.Ordinal))
                        throw new ArgumentException($"Unknown option: {arg}");
                    inputs.Add(arg);
                    break;
            }
        }

        return new SmokeOptions(
            false,
            saveReopen,
            generateChartFixtures,
            generateFreexFixture,
            generateExcelFixture,
            freeXResaveBeforeExcel,
            outputDirectory,
            pattern,
            inputs);
    }

    private static string ReadOptionValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{optionName} requires a value.");

        index++;
        return args[index];
    }
}

internal enum WorkbookValidationWorkflow
{
    DirectExcel,
    FreeXSaveThenExcel
}

internal sealed record WorkbookSmokeInput(
    string SourcePath,
    WorkbookValidationWorkflow Workflow,
    string Description,
    bool GenerateWithExcel = false);

internal sealed record WorkbookSmokeResult(
    bool Success,
    WorkbookSmokeInput Input,
    string? StagedPath,
    string? FreeXSavedPath,
    string? ExcelSavedPath,
    ExcelWorkbookSummary? Opened,
    ExcelWorkbookSummary? Reopened,
    FreeXWorkbookSummary? FreeXPreSave,
    FreeXWorkbookSummary? FreeXReopenedExcelSave,
    string? Error)
{
    public static WorkbookSmokeResult Pass(
        WorkbookSmokeInput input,
        string stagedPath,
        string? freeXSavedPath,
        string? ExcelSavedPath,
        ExcelWorkbookSummary opened,
        ExcelWorkbookSummary? Reopened,
        FreeXWorkbookSummary? freeXPreSave,
        FreeXWorkbookSummary? FreeXReopenedExcelSave) =>
        new(
            true,
            input,
            stagedPath,
            freeXSavedPath,
            ExcelSavedPath,
            opened,
            Reopened,
            freeXPreSave,
            FreeXReopenedExcelSave,
            null);

    public static WorkbookSmokeResult Fail(WorkbookSmokeInput input, string? freeXSavedPath, string error) =>
        new(false, input, null, freeXSavedPath, null, null, null, null, null, error);
}

internal sealed record ExcelWorkbookSummary(int WorksheetCount, int ShapeCount);
internal sealed record FreeXWorkbookSummary(int SheetCount, int CellCount, int FormulaCellCount);
internal sealed record FreeXSaveResult(string SavedPath, FreeXWorkbookSummary Summary);
internal sealed record ExcelSaveReopenResult(
    string ExcelSavedPath,
    ExcelWorkbookSummary Opened,
    ExcelWorkbookSummary Reopened);
internal sealed record ExcelSmokeSummary(int Total, int Passed, int Failed);
