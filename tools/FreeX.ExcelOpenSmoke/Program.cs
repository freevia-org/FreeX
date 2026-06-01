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

            if (!options.GenerateChartFixtures && options.Inputs.Count == 0)
            {
                Console.Error.WriteLine("No XLSX inputs were provided.");
                WriteUsage();
                return 2;
            }

            var userProfile = GetUserProfile();
            var runDirectory = options.OutputDirectory ?? CreateDefaultRunDirectory(userProfile);
            EnsureUnderUserProfile(runDirectory, userProfile);
            Directory.CreateDirectory(runDirectory);

            var generatedFiles = options.GenerateChartFixtures
                ? GenerateChartFixtures(Path.Combine(runDirectory, "generated"))
                : [];

            var inputFiles = ResolveInputFiles(options.Inputs, options.Pattern);
            var smokeInputs = generatedFiles.Concat(inputFiles).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (smokeInputs.Count == 0)
            {
                Console.Error.WriteLine("No XLSX files matched the requested inputs.");
                return 2;
            }

            Console.WriteLine("Excel open smoke");
            Console.WriteLine($"Run directory: {runDirectory}");
            Console.WriteLine($"Input count: {smokeInputs.Count}");

            var result = RunExcelSmoke(smokeInputs, Path.Combine(runDirectory, "staged"));
            Console.WriteLine(result.Failed == 0
                ? $"PASS: Excel opened {result.Passed}/{result.Total} workbook(s)."
                : $"FAIL: Excel opened {result.Passed}/{result.Total} workbook(s); {result.Failed} failed.");

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

    private static ExcelSmokeSummary RunExcelSmoke(IReadOnlyList<string> inputFiles, string stagingDirectory)
    {
        Directory.CreateDirectory(stagingDirectory);

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
                var stagedPath = CopyToStagingDirectory(inputFile, stagingDirectory);
                var result = OpenWorkbook((dynamic)workbooks, inputFile, stagedPath);
                results.Add(result);

                var status = result.Success ? "OPEN OK" : "OPEN FAILED";
                Console.WriteLine($"{status}: {result.OriginalPath}");
                Console.WriteLine($"  Staged: {result.StagedPath}");
                Console.WriteLine(result.Success
                    ? $"  Worksheets: {result.WorksheetCount}; worksheet shapes: {result.ShapeCount}"
                    : $"  Error: {result.Error}");
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

    private static WorkbookSmokeResult OpenWorkbook(dynamic workbooks, string originalPath, string stagedPath)
    {
        object? workbook = null;
        try
        {
            workbook = workbooks.Open(
                stagedPath,
                0,
                true,
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

            var (worksheetCount, shapeCount) = CountWorkbookContents(workbook);
            ((dynamic)workbook).Close(false);
            return WorkbookSmokeResult.Pass(originalPath, stagedPath, worksheetCount, shapeCount);
        }
        catch (COMException ex) when ((uint)ex.HResult == ExcelOpenRejectedHResult)
        {
            return WorkbookSmokeResult.Fail(
                originalPath,
                stagedPath,
                $"Excel rejected the workbook with 0x{(uint)ex.HResult:X8}: {ex.Message}");
        }
        catch (Exception ex)
        {
            var hresult = (uint)ex.HResult;
            return WorkbookSmokeResult.Fail(
                originalPath,
                stagedPath,
                $"{ex.GetType().Name} 0x{hresult:X8}: {ex.Message}");
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
                // The workbook may already be closed, or Excel may have rejected it before creating one.
            }

            ReleaseComObject(workbook);
        }
    }

    private static (int WorksheetCount, int ShapeCount) CountWorkbookContents(object workbook)
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

            return (worksheetCount, shapeCount);
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

    private static string SaveWorkbook(Workbook workbook, string path)
    {
        using var stream = File.Create(path);
        new XlsxFileAdapter().Save(workbook, stream);
        return path;
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
              --generate-chart-fixtures  Generate FreeX histogram and waterfall XLSX smoke files, then open them in Excel.
              --out <directory>          Run output directory. Must be under %USERPROFILE%.
              --pattern <glob>           Directory input glob. Defaults to *.xlsx.
              --help                     Show this help text.

            Examples:
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- --generate-chart-fixtures
              dotnet run --project tools/FreeX.ExcelOpenSmoke -- C:\Users\quinn\freex-xlsx-verify\H_histogram_fixed.xlsx C:\Users\quinn\freex-xlsx-verify\I_waterfall_fixed.xlsx
            """);
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
}

internal sealed record SmokeOptions(
    bool ShowHelp,
    bool GenerateChartFixtures,
    string? OutputDirectory,
    string Pattern,
    IReadOnlyList<string> Inputs)
{
    public static SmokeOptions Parse(string[] args)
    {
        var generateChartFixtures = false;
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
                    return new SmokeOptions(true, false, null, pattern, []);
                case "--generate-chart-fixtures":
                    generateChartFixtures = true;
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

        return new SmokeOptions(false, generateChartFixtures, outputDirectory, pattern, inputs);
    }

    private static string ReadOptionValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new ArgumentException($"{optionName} requires a value.");

        index++;
        return args[index];
    }
}

internal sealed record WorkbookSmokeResult(
    bool Success,
    string OriginalPath,
    string StagedPath,
    int WorksheetCount,
    int ShapeCount,
    string? Error)
{
    public static WorkbookSmokeResult Pass(string originalPath, string stagedPath, int worksheetCount, int shapeCount) =>
        new(true, originalPath, stagedPath, worksheetCount, shapeCount, null);

    public static WorkbookSmokeResult Fail(string originalPath, string stagedPath, string error) =>
        new(false, originalPath, stagedPath, 0, 0, error);
}

internal sealed record ExcelSmokeSummary(int Total, int Passed, int Failed);
