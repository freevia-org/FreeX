using System.Diagnostics;
using System.Globalization;
using System.IO;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

internal static class Program
{
    private const int PrewarmSheetCount = 3;
    private const int PrewarmRowsPerSheet = 160;
    private const int PrewarmValueColumnsPerSheet = 12;
    private const int PrewarmStyleOnlyColumnsPerSheet = 80;
    private const int PrewarmStyleOnlyStartColumn = PrewarmValueColumnsPerSheet + 2;
    private static readonly object PerfOutputGate = new();

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

        try
        {
            var options = AppIoBenchOptions.Parse(args);
            if (options.ShowHelp)
            {
                WriteUsage();
                return 0;
            }

            if (options.Generate)
            {
                GenerateLargeWorkbook(options);
                return 0;
            }

            if (string.IsNullOrWhiteSpace(options.Path))
            {
                Console.Error.WriteLine("Missing required --path <xlsx-or-xlsm>.");
                WriteUsage();
                return 2;
            }

            if (!File.Exists(options.Path))
            {
                Console.Error.WriteLine($"Workbook not found: {options.Path}");
                return 2;
            }

            await RunAsync(options);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunAsync(AppIoBenchOptions options)
    {
        if (options.PrewarmXlsx)
            RunXlsxPrewarm(options);

        for (var iteration = 1; iteration <= options.RepeatCount; iteration++)
            await RunIterationAsync(options, iteration);
    }

    private static void RunXlsxPrewarm(AppIoBenchOptions options)
    {
        ForceFullCollection();
        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        PrewarmXlsxLoadSavePaths();
        stopwatch.Stop();
        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

        WritePerf(
            options,
            "PERF APP_XLSX_PREWARM " +
            $"mode=xlsx elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0}");
    }

    private static async Task RunIterationAsync(AppIoBenchOptions options, int iteration)
    {
        var adapter = new XlsxFileAdapter();
        var loader = new WorkbookOpenService(CreateRecalculationAction(options));
        var fileInfo = new FileInfo(options.Path!);
        var inputExtension = Path.GetExtension(options.Path!);
        var inputFormat = ResolveOpenFormat(adapter, inputExtension);
        var openProgress = new ThrottledProgress<WorkbookOpenProgressUpdate>(
            options,
            iteration,
            "open",
            update => (update.Phase.ToString(), update.Percent));

        WritePerf(
            options,
            "PERF APP_XLSX_STAGE " +
            $"{FormatIteration(options, iteration)}stage=open_start file=\"{fileInfo.Name}\" bytes={fileInfo.Length:N0}");
        ForceFullCollection();
        var openAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var openStopwatch = Stopwatch.StartNew();
        var openResult = await loader.LoadAsync(
            options.Path!,
            adapter,
            inputExtension,
            inputFormat,
            openProgress);
        openStopwatch.Stop();
        var openAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - openAllocatedBefore;

        var workbook = openResult.Workbook;
        WritePerf(
            options,
            "PERF APP_XLSX_OPEN " +
            $"{FormatIteration(options, iteration)}file=\"{fileInfo.Name}\" bytes={fileInfo.Length:N0} sheets={workbook.SheetCount} " +
            $"cells={workbook.Sheets.Sum(sheet => sheet.CellCount):N0} " +
            $"warnings={openResult.LoadWarnings?.Count ?? 0} unsupported_features={openResult.FeatureReport?.Features.Count ?? 0} " +
            $"progress_updates={openProgress.Count} elapsed_ms={openStopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={openAllocatedBytes:N0} {FormatLoadDiagnostics(adapter.LastLoadDiagnostics)}");

        if (options.OpenOnly)
        {
            WritePerf(
                options,
                "PERF APP_XLSX_STAGE " +
                $"{FormatIteration(options, iteration)}stage=open_only_done");
            return;
        }

        WritePerf(
            options,
            "PERF APP_XLSX_STAGE " +
            $"{FormatIteration(options, iteration)}stage=edit_start edit={options.EditMode}");
        if (options.EditMode != AppIoBenchEditMode.None)
        {
            ForceFullCollection();
            var prepareAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var prepareStopwatch = Stopwatch.StartNew();
            var prepared = XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var prepareReason);
            prepareStopwatch.Stop();
            var prepareAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - prepareAllocatedBefore;
            WritePerf(
                options,
                "PERF APP_XLSX_PREPARE_EDIT " +
                $"{FormatIteration(options, iteration)}prepared={prepared.ToString().ToLowerInvariant()} " +
                $"reason=\"{prepareReason ?? ""}\" elapsed_ms={prepareStopwatch.Elapsed.TotalMilliseconds:F2} " +
                $"allocated_bytes={prepareAllocatedBytes:N0}");
        }

        var editResult = ApplyEdit(workbook, options);
        if (!editResult.Applied)
        {
            WritePerf(
                options,
                "PERF APP_XLSX_EDIT " +
                $"{FormatIteration(options, iteration)}edit={options.EditMode} applied=false reason=\"{editResult.Reason}\"");
            return;
        }

        using var temporaryOutput = options.OutputPath is null ? TemporaryOutputFile.Create(".xlsx") : null;
        var savePath = options.OutputPath ?? temporaryOutput!.Path;
        var saveProgress = new ThrottledProgress<WorkbookSaveProgressUpdate>(
            options,
            iteration,
            "save",
            update => (update.Phase.ToString(), update.Percent));

        WritePerf(
            options,
            "PERF APP_XLSX_STAGE " +
            $"{FormatIteration(options, iteration)}stage=save_start edit={editResult.Label}");
        ForceFullCollection();
        var saveAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var saveStopwatch = Stopwatch.StartNew();
        await new WorkbookSaveService().SaveAsync(
            savePath,
            adapter,
            workbook,
            saveProgress);
        saveStopwatch.Stop();
        var saveAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - saveAllocatedBefore;

        var saveInfo = new FileInfo(savePath);
        WritePerf(
            options,
            "PERF APP_XLSX_SAVE " +
            $"{FormatIteration(options, iteration)}file=\"{fileInfo.Name}\" edit={editResult.Label} output_bytes={saveInfo.Length:N0} " +
            $"progress_updates={saveProgress.Count} elapsed_ms={saveStopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"allocated_bytes={saveAllocatedBytes:N0} {FormatSaveDiagnostics(adapter.LastSaveDiagnostics)}");
    }

    private static void GenerateLargeWorkbook(AppIoBenchOptions options)
    {
        var outPath = options.OutputPath
            ?? throw new ArgumentException("--generate requires --out <xlsx>.");
        var buildStopwatch = Stopwatch.StartNew();
        var workbook = new Workbook("Generated");

        var styles = new StyleId[4];
        for (var i = 0; i < styles.Length; i++)
        {
            var style = CellStyle.Default.Clone();
            style.FillColor = CellColor.FromArgb((byte)(200 + i * 12), 230, 240);
            style.FillPatternStyle = CellFillPatternStyle.Solid;
            style.NumberFormat = i % 2 == 0 ? "#,##0.00" : "0.0%";
            style.BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.FromArgb(91, 155, 213));
            style.Bold = i % 2 == 1;
            styles[i] = workbook.RegisterStyle(style);
        }

        var baseDate = new DateTime(2020, 1, 1);
        for (var sheetIndex = 0; sheetIndex < options.GenSheets; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Sheet{sheetIndex + 1}");
            for (var c = 1u; c <= (uint)options.GenCols; c++)
                sheet.SetCell(new CellAddress(sheet.Id, 1, c), new TextValue($"Col{c}"));

            for (var r = 2u; r <= (uint)options.GenRows + 1; r++)
            {
                for (var c = 1u; c <= (uint)options.GenCols; c++)
                {
                    var address = new CellAddress(sheet.Id, r, c);
                    Cell cell;
                    if (c == 1)
                        cell = Cell.FromValue(new TextValue($"Item-{r % 500}")); // repeated -> shared strings
                    else if (c == 2)
                        cell = Cell.FromValue(DateTimeValue.FromDateTime(baseDate.AddDays((r * c) % 2000)));
                    else if (c == (uint)options.GenCols && r % 12 == 0 && options.GenCols >= 5)
                        cell = Cell.FromFormula($"SUM(C{r}:E{r})");
                    else
                        cell = Cell.FromValue(new NumberValue((sheetIndex + 1) * 100000 + r * 10 + c + r * c % 97 * 0.5));

                    if ((r + c) % 4 == 0)
                        cell.StyleId = styles[(r + c) % (uint)styles.Length];
                    sheet.SetCell(address, cell);
                }
            }
        }

        buildStopwatch.Stop();

        var saveStopwatch = Stopwatch.StartNew();
        var adapter = new XlsxFileAdapter();
        using (var fileStream = File.Create(outPath))
            adapter.Save(workbook, fileStream);
        saveStopwatch.Stop();

        var info = new FileInfo(outPath);
        var totalCells = (long)options.GenSheets * options.GenRows * options.GenCols;
        Console.WriteLine(
            "PERF APP_XLSX_GENERATE " +
            $"out=\"{outPath}\" sheets={options.GenSheets} rows={options.GenRows} cols={options.GenCols} " +
            $"cells={totalCells:N0} bytes={info.Length:N0} build_ms={buildStopwatch.Elapsed.TotalMilliseconds:F2} " +
            $"save_ms={saveStopwatch.Elapsed.TotalMilliseconds:F2}");
    }

    private static AppIoBenchEditResult ApplyEdit(Workbook workbook, AppIoBenchOptions options)
    {
        if (options.EditMode == AppIoBenchEditMode.None)
            return AppIoBenchEditResult.Success("none");

        var sheet = ResolveSheet(workbook, options.Sheet);
        if (sheet is null)
            return AppIoBenchEditResult.Failure($"sheet '{options.Sheet}' was not found");

        var address = options.Address ?? FindDefaultAddress(sheet, options.EditMode);
        if (address is null)
            return AppIoBenchEditResult.Failure($"no suitable cell found for edit mode '{options.EditMode}'");

        var cellAddress = new CellAddress(sheet.Id, address.Value.Row, address.Value.Col);
        switch (options.EditMode)
        {
            case AppIoBenchEditMode.ExistingLiteral:
                if (sheet.GetCell(cellAddress) is not { HasFormula: false, Value: not BlankValue })
                    return AppIoBenchEditResult.Failure($"cell {FormatAddress(address.Value)} is not an existing literal cell");
                sheet.SetCell(cellAddress, new TextValue(options.Value ?? "freex-app-io-bench"));
                return AppIoBenchEditResult.Success($"existing_literal address={FormatAddress(address.Value)}");

            case AppIoBenchEditMode.InsertLiteral:
                if (sheet.GetCell(cellAddress) is not null)
                    return AppIoBenchEditResult.Failure($"cell {FormatAddress(address.Value)} is already occupied");
                sheet.SetCell(cellAddress, new TextValue(options.Value ?? "freex-app-io-bench"));
                return AppIoBenchEditResult.Success($"insert_literal address={FormatAddress(address.Value)}");

            case AppIoBenchEditMode.ClearCell:
                if (sheet.GetCell(cellAddress) is null)
                    return AppIoBenchEditResult.Failure($"cell {FormatAddress(address.Value)} is already blank");
                sheet.ClearCell(cellAddress.Row, cellAddress.Col);
                return AppIoBenchEditResult.Success($"clear_cell address={FormatAddress(address.Value)}");

            case AppIoBenchEditMode.FormulaText:
                if (sheet.GetCell(cellAddress) is not { HasFormula: true })
                    return AppIoBenchEditResult.Failure($"cell {FormatAddress(address.Value)} is not an existing formula cell");
                sheet.SetFormula(cellAddress, options.Value ?? "1+1");
                return AppIoBenchEditResult.Success($"formula_text address={FormatAddress(address.Value)}");

            default:
                return AppIoBenchEditResult.Failure($"unsupported edit mode '{options.EditMode}'");
        }
    }

    private static Action<Workbook> CreateRecalculationAction(AppIoBenchOptions options)
    {
        if (!options.RecalculateFormulas)
            return _ => { };

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return workbook => engine.RecalculateAllFormulas(workbook);
    }

    private static void PrewarmXlsxLoadSavePaths()
    {
        var adapter = new XlsxFileAdapter();
        using var initialPackage = new MemoryStream();
        adapter.Save(CreateRepresentativeWorkbook(), initialPackage);

        initialPackage.Position = 0;
        var loaded = adapter.LoadWithWarnings(initialPackage, inspectFeatures: true).Workbook;
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(loaded, out _);

        var sheet = loaded.Sheets[0];
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("patched"));

        using var patchedPackage = new MemoryStream();
        adapter.Save(loaded, patchedPackage);
    }

    private static Workbook CreateRepresentativeWorkbook()
    {
        var workbook = new Workbook("XLSX Prewarm");
        var prewarmStyleId = CreatePrewarmStyleId(workbook);
        for (var sheetIndex = 1; sheetIndex <= PrewarmSheetCount; sheetIndex++)
            PopulatePrewarmSheet(workbook.AddSheet($"Sheet{sheetIndex}"), sheetIndex, prewarmStyleId);

        return workbook;
    }

    private static StyleId CreatePrewarmStyleId(Workbook workbook)
    {
        var style = CellStyle.Default.Clone();
        style.FillColor = CellColor.FromArgb(221, 235, 247);
        style.FillPatternStyle = CellFillPatternStyle.Solid;
        style.NumberFormat = "#,##0.00";
        style.BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.FromArgb(91, 155, 213));
        return workbook.RegisterStyle(style);
    }

    private static void PopulatePrewarmSheet(Sheet sheet, int sheetIndex, StyleId prewarmStyleId)
    {
        var styleOnlyRuns = new List<StyleOnlyRun>(PrewarmRowsPerSheet);
        for (var row = 1u; row <= PrewarmRowsPerSheet; row++)
        {
            for (var col = 1u; col <= PrewarmValueColumnsPerSheet; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (col == 1)
                {
                    sheet.SetCell(address, new TextValue($"S{sheetIndex}-R{row}"));
                    continue;
                }

                var value = sheetIndex * 1_000_000 + row * 1_000 + col;
                var cell = col == PrewarmValueColumnsPerSheet && row % 16 == 0
                    ? Cell.FromFormula($"B{row}+C{row}")
                    : Cell.FromValue(new NumberValue(value));
                cell.Value = new NumberValue(value);
                if (col % 4 == 0)
                    cell.StyleId = prewarmStyleId;
                sheet.SetCell(address, cell);
            }

            styleOnlyRuns.Add(new StyleOnlyRun(
                row,
                (uint)PrewarmStyleOnlyStartColumn,
                (uint)(PrewarmStyleOnlyStartColumn + PrewarmStyleOnlyColumnsPerSheet - 1),
                prewarmStyleId));
        }

        sheet.SetStyleOnlyRuns(styleOnlyRuns);
    }

    private static Sheet? ResolveSheet(Workbook workbook, string? requestedSheet)
    {
        if (string.IsNullOrWhiteSpace(requestedSheet))
            return workbook.SheetCount == 0 ? null : workbook.GetSheetAt(0);

        if (int.TryParse(requestedSheet, NumberStyles.None, CultureInfo.InvariantCulture, out var oneBasedIndex) &&
            oneBasedIndex >= 1 &&
            oneBasedIndex <= workbook.SheetCount)
        {
            return workbook.GetSheetAt(oneBasedIndex - 1);
        }

        return workbook.Sheets.FirstOrDefault(sheet =>
            sheet.Name.Equals(requestedSheet, StringComparison.OrdinalIgnoreCase));
    }

    private static (uint Row, uint Col)? FindDefaultAddress(Sheet sheet, AppIoBenchEditMode editMode)
    {
        if (editMode == AppIoBenchEditMode.InsertLiteral)
            return FindBlankCell(sheet);

        foreach (var (key, cell) in sheet.GetOccupiedCellMap().OrderBy(pair => pair.Key.Row).ThenBy(pair => pair.Key.Col))
        {
            if (editMode == AppIoBenchEditMode.FormulaText)
            {
                if (cell.HasFormula)
                    return key;
            }
            else if (cell.Value is not BlankValue)
            {
                return key;
            }
        }

        return null;
    }

    private static (uint Row, uint Col)? FindBlankCell(Sheet sheet)
    {
        for (var row = 1u; row <= Math.Min(CellAddress.MaxRow, 200u); row++)
        {
            for (var col = 1u; col <= Math.Min(CellAddress.MaxCol, 200u); col++)
            {
                if (sheet.GetCell(new CellAddress(sheet.Id, row, col)) is null &&
                    sheet.GetStyleOnly(row, col) is null)
                {
                    return (row, col);
                }
            }
        }

        return null;
    }

    private static (uint Row, uint Col) ParseA1Address(string value)
    {
        var trimmed = value.Trim();
        var index = 0;
        while (index < trimmed.Length && char.IsLetter(trimmed[index]))
            index++;

        if (index == 0 || index == trimmed.Length)
            throw new ArgumentException($"Invalid A1 address: {value}");

        var columnName = trimmed[..index].ToUpperInvariant();
        var rowText = trimmed[index..];
        if (!uint.TryParse(rowText, NumberStyles.None, CultureInfo.InvariantCulture, out var row) ||
            row is < 1 or > CellAddress.MaxRow)
        {
            throw new ArgumentException($"Invalid row in A1 address: {value}");
        }

        var col = 0u;
        foreach (var ch in columnName)
        {
            if (ch is < 'A' or > 'Z')
                throw new ArgumentException($"Invalid column in A1 address: {value}");
            col = checked((col * 26) + (uint)(ch - 'A' + 1));
        }

        if (col is < 1 or > CellAddress.MaxCol)
            throw new ArgumentException($"Invalid column in A1 address: {value}");

        return (row, col);
    }

    private static string FormatAddress((uint Row, uint Col) address) =>
        $"{CellAddress.NumberToColumnName(address.Col)}{address.Row}";

    private static FileFormatDescriptor ResolveOpenFormat(XlsxFileAdapter adapter, string extension)
    {
        var format = adapter.Formats.FirstOrDefault(candidate =>
            candidate.CanOpen &&
            candidate.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase));

        return format ?? throw new ArgumentException($"Unsupported Open XML workbook extension for AppIoBench: {extension}");
    }

    private static string FormatSaveDiagnostics(XlsxSaveDiagnostics diagnostics) =>
        $"save_path={diagnostics.PathLabel} save_reason={diagnostics.Reason} " +
        $"patch_changes={diagnostics.TotalPatchChangeCount} cell_changes={diagnostics.CellChangeCount} " +
        $"dimension_changes={diagnostics.DimensionChangeCount} merge_changes={diagnostics.MergeRegionChangeCount} " +
        $"hyperlink_changes={diagnostics.HyperlinkChangeCount} comment_changes={diagnostics.CommentChangeCount}";

    private static string FormatLoadDiagnostics(XlsxLoadDiagnostics diagnostics) =>
        $"load_core_ms={diagnostics.TotalElapsedMilliseconds:F2} load_core_allocated_bytes={diagnostics.TotalAllocatedBytes:N0} " +
        $"{FormatLoadPhase("package_copy", diagnostics.PackageCopy)} " +
        $"{FormatLoadPhase("package_metadata", diagnostics.PackageMetadata)} " +
        $"{FormatLoadPhase("style_metadata", diagnostics.StyleMetadata)} " +
        $"{FormatLoadPhase("sheet_xml_layout", diagnostics.SheetXmlLayout)} " +
        $"{FormatLoadPhase("closedxml_load", diagnostics.ClosedXmlLoad)} " +
        $"{FormatLoadPhase("closedxml_package_prep", diagnostics.ClosedXmlPackagePreparation)} " +
        $"{FormatLoadPhase("closedxml_workbook_open", diagnostics.ClosedXmlWorkbookOpen)} " +
        $"{FormatLoadPhase("workbook_materialize", diagnostics.WorkbookMaterialization)} " +
        $"{FormatLoadPhase("source_snapshot", diagnostics.SourceSnapshot)}";

    private static string FormatLoadPhase(string name, XlsxLoadPhaseDiagnostics diagnostics) =>
        $"load_{name}_ms={diagnostics.ElapsedMilliseconds:F2} load_{name}_allocated_bytes={diagnostics.AllocatedBytes:N0}";

    private static string FormatIteration(AppIoBenchOptions options, int iteration) =>
        options.RepeatCount > 1 ? $"iteration={iteration} " : string.Empty;

    private static void WritePerf(AppIoBenchOptions options, string line)
    {
        lock (PerfOutputGate)
        {
            Console.WriteLine(line);
            Console.Out.Flush();
            if (!string.IsNullOrWhiteSpace(options.LogPath))
                File.AppendAllText(options.LogPath, line + Environment.NewLine);
        }
    }

    private static string EscapeField(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string FormatNullablePercent(double? value) =>
        value.HasValue ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : "null";

    private static void ForceFullCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void WriteUsage()
    {
        Console.WriteLine(
            """
            Usage:
              dotnet run --project tools/FreeX.AppIoBench -- --path <xlsx-or-xlsm> [options]

            Options:
              --edit none|existing-literal|insert-literal|clear-cell|formula-text
              --sheet <name-or-1-based-index>
              --cell <A1>
              --value <text-or-formula>
              --recalc none|real
              --out <xlsx>
              --log <txt>
              --open-only
              --repeat <count>
              --prewarm xlsx
              --help
            """);
    }

    private sealed class ThrottledProgress<T>(
        AppIoBenchOptions options,
        int iteration,
        string kind,
        Func<T, (string Detail, double? Percent)> format) : IProgress<T>
    {
        private readonly object _lock = new();
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private string? _lastDetail;
        private double _lastLoggedSeconds = -5;
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void Report(T value)
        {
            Interlocked.Increment(ref _count);
            var (detail, percent) = format(value);
            var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;

            lock (_lock)
            {
                if (string.Equals(_lastDetail, detail, StringComparison.Ordinal) &&
                    elapsedSeconds - _lastLoggedSeconds < 5)
                {
                    return;
                }

                _lastDetail = detail;
                _lastLoggedSeconds = elapsedSeconds;
            }

            WritePerf(
                options,
                "PERF APP_XLSX_PROGRESS " +
                $"{FormatIteration(options, iteration)}kind={kind} elapsed_ms={_stopwatch.Elapsed.TotalMilliseconds:F2} " +
                $"percent={FormatNullablePercent(percent)} detail=\"{EscapeField(detail)}\"");
        }
    }

    private sealed record AppIoBenchEditResult(bool Applied, string Label, string? Reason)
    {
        public static AppIoBenchEditResult Success(string label) => new(true, label, null);

        public static AppIoBenchEditResult Failure(string reason) => new(false, string.Empty, reason);
    }

    private sealed record AppIoBenchOptions
    {
        public string? Path { get; private init; }

        public string? OutputPath { get; private init; }

        public string? LogPath { get; private init; }

        public AppIoBenchEditMode EditMode { get; private init; } = AppIoBenchEditMode.ExistingLiteral;

        public string? Sheet { get; private init; }

        public (uint Row, uint Col)? Address { get; private init; }

        public string? Value { get; private init; }

        public bool RecalculateFormulas { get; private init; }

        public bool OpenOnly { get; private init; }

        public int RepeatCount { get; private init; } = 1;

        public bool PrewarmXlsx { get; private init; }

        public bool Generate { get; private init; }

        public int GenRows { get; private init; } = 20000;

        public int GenCols { get; private init; } = 15;

        public int GenSheets { get; private init; } = 3;

        public bool ShowHelp { get; private init; }

        public static AppIoBenchOptions Parse(string[] args)
        {
            var options = new AppIoBenchOptions();
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--help":
                    case "-h":
                    case "/?":
                        options = options with { ShowHelp = true };
                        break;
                    case "--path":
                        options = options with { Path = ReadValue(args, ref index, "--path") };
                        break;
                    case "--out":
                        options = options with { OutputPath = ReadValue(args, ref index, "--out") };
                        break;
                    case "--log":
                        options = options with { LogPath = ReadValue(args, ref index, "--log") };
                        break;
                    case "--edit":
                        options = options with { EditMode = ParseEditMode(ReadValue(args, ref index, "--edit")) };
                        break;
                    case "--sheet":
                        options = options with { Sheet = ReadValue(args, ref index, "--sheet") };
                        break;
                    case "--cell":
                        options = options with { Address = ParseA1Address(ReadValue(args, ref index, "--cell")) };
                        break;
                    case "--value":
                        options = options with { Value = ReadValue(args, ref index, "--value") };
                        break;
                    case "--recalc":
                        options = options with
                        {
                            RecalculateFormulas = ParseRecalcMode(ReadValue(args, ref index, "--recalc"))
                        };
                        break;
                    case "--open-only":
                        options = options with { OpenOnly = true };
                        break;
                    case "--repeat":
                        options = options with { RepeatCount = ParseRepeatCount(ReadValue(args, ref index, "--repeat")) };
                        break;
                    case "--prewarm":
                        options = options with { PrewarmXlsx = ParsePrewarmMode(ReadValue(args, ref index, "--prewarm")) };
                        break;
                    case "--generate":
                        options = options with { Generate = true };
                        break;
                    case "--rows":
                        options = options with { GenRows = ParsePositiveInt(ReadValue(args, ref index, "--rows"), "--rows") };
                        break;
                    case "--cols":
                        options = options with { GenCols = ParsePositiveInt(ReadValue(args, ref index, "--cols"), "--cols") };
                        break;
                    case "--sheets":
                        options = options with { GenSheets = ParsePositiveInt(ReadValue(args, ref index, "--sheets"), "--sheets") };
                        break;
                    default:
                        if (options.Path is null && !args[index].StartsWith("-", StringComparison.Ordinal))
                        {
                            options = options with { Path = args[index] };
                            break;
                        }

                        throw new ArgumentException($"Unknown option: {args[index]}");
                }
            }

            return options;
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
                throw new ArgumentException($"{option} requires a value.");

            return args[++index];
        }

        private static AppIoBenchEditMode ParseEditMode(string value) =>
            value.Trim().ToLowerInvariant() switch
            {
                "none" => AppIoBenchEditMode.None,
                "existing-literal" => AppIoBenchEditMode.ExistingLiteral,
                "insert-literal" => AppIoBenchEditMode.InsertLiteral,
                "clear-cell" => AppIoBenchEditMode.ClearCell,
                "formula-text" => AppIoBenchEditMode.FormulaText,
                _ => throw new ArgumentException($"Unsupported edit mode: {value}")
            };

        private static bool ParseRecalcMode(string value) =>
            value.Trim().ToLowerInvariant() switch
            {
                "none" => false,
                "real" => true,
                _ => throw new ArgumentException($"Unsupported recalc mode: {value}")
            };

        private static int ParseRepeatCount(string value)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                count < 1)
            {
                throw new ArgumentException($"Invalid repeat count: {value}");
            }

            return count;
        }

        private static int ParsePositiveInt(string value, string option)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 1)
                throw new ArgumentException($"Invalid value for {option}: {value}");

            return parsed;
        }

        private static bool ParsePrewarmMode(string value) =>
            value.Trim().ToLowerInvariant() switch
            {
                "xlsx" => true,
                "none" => false,
                _ => throw new ArgumentException($"Unsupported prewarm mode: {value}")
            };
    }

    private sealed class TemporaryOutputFile : IDisposable
    {
        private readonly string _directory;

        private TemporaryOutputFile(string directory, string path)
        {
            _directory = directory;
            Path = path;
        }

        public string Path { get; }

        public static TemporaryOutputFile Create(string extension)
        {
            var directory = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"freex-app-io-bench-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            return new TemporaryOutputFile(directory, System.IO.Path.Combine(directory, $"output{extension}"));
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private enum AppIoBenchEditMode
    {
        None,
        ExistingLiteral,
        InsertLiteral,
        ClearCell,
        FormulaText
    }
}
