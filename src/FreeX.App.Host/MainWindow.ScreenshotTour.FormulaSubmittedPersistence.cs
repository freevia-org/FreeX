using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureFormulaSubmittedPersistenceTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteFormulaSubmittedPersistenceTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, FormulaSubmittedPersistenceTourSavedWorkbookFileName);
        DeleteIfExists(savedWorkbookPath);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 768;
        await Task.Delay(700);

        var context = EnsureFormulaSubmittedPersistenceTourContext(savedWorkbookPath, savedWorkbookBytes: 0, persistenceStage: "seeded");
        var captures = new List<FormulaSubmittedPersistenceTourManifestCapture>();
        var submittedCommands = new List<string>();
        ContextMenu? useInFormulaMenu = null;
        NamedRangeDialog? nameManagerDialog = null;

        try
        {
            captures.Add(await CaptureFormulaSubmittedPersistenceWindowStateAsync(
                outputDir,
                context,
                "seeded-before-submit",
                "freex_formula_submitted_persistence_seeded_before_submit",
                "Seeded worksheet before submitted formula/name commands: source values are present, result cells and submitted names are still empty.",
                "Seeded worksheet state."));

            SubmitFormulaSubmittedPersistenceCommands(context, submittedCommands);
            context = ResolveFormulaSubmittedPersistenceCurrentContext(savedWorkbookPath, "submitted", savedWorkbookBytes: 0);
            captures.Add(await CaptureFormulaSubmittedPersistenceWindowStateAsync(
                outputDir,
                context,
                "submitted-formula-results",
                "freex_formula_submitted_persistence_formula_results",
                "Worksheet grid after DefineNamedRangeCommand, CreateNamedRangesFromSelectionCommand, and EditCellsCommand.ForFormula populated named ranges, formulas, AutoSum-style totals, and calculated values.",
                "TryExecuteCommand(DefineNamedRangeCommand/CreateNamedRangesFromSelectionCommand/EditCellsCommand.ForFormula); RecalculateIfAutomatic"));

            SelectFormulaSubmittedPersistenceCell(context.NamedInsertionCell);
            BeginFormulaBarFormulaEdit("=");
            InsertDefinedNameIntoFormula("TourRevenue");
            captures.Add(await CaptureFormulaSubmittedPersistenceCurrentWindowStateAsync(
                outputDir,
                context,
                "use-in-formula-inserted-reference",
                "freex_formula_submitted_persistence_use_in_formula_inserted_reference",
                "Use in Formula insertion path placed the TourRevenue defined name into the formula bar before the tour submitted the persisted reference formula.",
                "BeginFormulaBarFormulaEdit(\"=\"); InsertDefinedNameIntoFormula(\"TourRevenue\")"));

            var insertedFormulaText = FormulaBar.Text.TrimStart('=');
            ExecuteFormulaSubmittedPersistenceCommand(
                EditCellsCommand.ForFormula(context.Sheet.Id, context.NamedInsertionCell, insertedFormulaText),
                "Use in Formula",
                submittedCommands,
                out var namedInsertionOutcome);
            context = ResolveFormulaSubmittedPersistenceCurrentContext(savedWorkbookPath, "submitted-named-reference", savedWorkbookBytes: 0);

            useInFormulaMenu = await OpenFormulaSubmittedPersistenceUseInFormulaMenuAsync();
            await CaptureElementAsync(useInFormulaMenu, outputDir, "freex_formula_submitted_persistence_use_in_formula_menu");
            captures.Add(CreateFormulaSubmittedPersistenceCapture(
                context,
                "use-in-formula-menu-submitted-names",
                "freex_formula_submitted_persistence_use_in_formula_menu",
                "Use in Formula menu",
                "RenderTargetBitmap-formulas-context-menu",
                useInFormulaMenu.ActualWidth,
                useInFormulaMenu.ActualHeight,
                "Use in Formula menu lists the submitted defined names after the actual DefineNamedRange/Create from Selection commands ran.",
                AddMenuHeadersToArray(useInFormulaMenu)));
            useInFormulaMenu.IsOpen = false;
            useInFormulaMenu = null;

            nameManagerDialog = CreateFormulaSubmittedPersistenceNameManagerDialog(context);
            await CaptureFormulaSubmittedPersistenceNameManagerAsync(
                nameManagerDialog,
                outputDir,
                captures,
                context,
                "submitted-name-manager",
                "freex_formula_submitted_persistence_name_manager_submitted",
                "Name Manager dialog after submitted commands shows TourRevenue, TourProfit, and labels created from selection.");
            nameManagerDialog.Close();
            nameManagerDialog = null;

            context = await SaveFormulaSubmittedPersistenceWorkbookAsync(savedWorkbookPath, context);
            captures.Add(await CaptureFormulaSubmittedPersistenceWindowStateAsync(
                outputDir,
                context,
                "saved-native-workbook",
                "freex_formula_submitted_persistence_saved_native_workbook",
                "Native .fxl workbook saved through SaveWorkbookToTargetAsync while submitted formula/name state remains visible.",
                "SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, .fxl adapter))"));

            await OpenFileAsync(savedWorkbookPath);
            context = ResolveFormulaSubmittedPersistenceCurrentContext(
                savedWorkbookPath,
                "after-reopen",
                new FileInfo(savedWorkbookPath).Length);
            captures.Add(await CaptureFormulaSubmittedPersistenceWindowStateAsync(
                outputDir,
                context,
                "reopened-persisted-formulas-names",
                "freex_formula_submitted_persistence_reopened_grid",
                "OpenFileAsync reopened the saved native workbook and restored the submitted formulas, calculated results, and defined names.",
                "OpenFileAsync(savedWorkbookPath) -> native .fxl adapter"));

            nameManagerDialog = CreateFormulaSubmittedPersistenceNameManagerDialog(context);
            await CaptureFormulaSubmittedPersistenceNameManagerAsync(
                nameManagerDialog,
                outputDir,
                captures,
                context,
                "reopened-name-manager",
                "freex_formula_submitted_persistence_name_manager_reopened",
                "Name Manager dialog after native reopen shows the submitted defined names persisted.");
            nameManagerDialog.Close();
            nameManagerDialog = null;

            ValidateFormulaSubmittedPersistenceTourEvidence(outputDir, captures, savedWorkbookPath);
            await WriteFormulaSubmittedPersistenceTourManifestAsync(outputDir, context, captures, submittedCommands);
        }
        catch
        {
            DeleteFormulaSubmittedPersistenceTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (useInFormulaMenu is { IsOpen: true })
                useInFormulaMenu.IsOpen = false;
            if (nameManagerDialog is { IsVisible: true })
                nameManagerDialog.Close();
        }
    }

    private FormulaSubmittedPersistenceTourContext EnsureFormulaSubmittedPersistenceTourContext(
        string savedWorkbookPath,
        long savedWorkbookBytes,
        string persistenceStage)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Formula submitted/persistence tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        sheet.Name = "Formula Submit";
        _workbook.Name = "Formula submitted persistence";

        var clearRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 9, 7));
        foreach (var address in clearRange.AllCells())
            sheet.ClearCell(address);

        var values = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Revenue")),
            (1, 3, new TextValue("Cost")),
            (1, 4, new TextValue("TourProfit")),
            (1, 5, new TextValue("TourMargin")),
            (2, 1, new TextValue("North")),
            (3, 1, new TextValue("South")),
            (4, 1, new TextValue("West")),
            (2, 2, new NumberValue(4200)),
            (3, 2, new NumberValue(3900)),
            (4, 2, new NumberValue(5100)),
            (2, 3, new NumberValue(2600)),
            (3, 3, new NumberValue(2400)),
            (4, 3, new NumberValue(3150)),
            (6, 1, new TextValue("AutoSum")),
            (7, 1, new TextValue("Use in Formula"))
        };

        foreach (var (row, col, value) in values)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 5));
        SetSelectionRange(sourceRange, sourceRange.Start);
        EnsureCellVisible(sourceRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        MarkWorkbookDirty();
        UpdateTitleBar();

        return CreateFormulaSubmittedPersistenceContext(sheet, savedWorkbookPath, savedWorkbookBytes, persistenceStage);
    }

    private void SubmitFormulaSubmittedPersistenceCommands(
        FormulaSubmittedPersistenceTourContext context,
        List<string> submittedCommands)
    {
        ExecuteFormulaSubmittedPersistenceCommand(
            new DefineNamedRangeCommand(
                "TourRevenue",
                context.RevenueRange,
                new NamedRangeMetadata("Workbook", "Submitted through formula persistence tour.")),
            "Define Name",
            submittedCommands,
            out _);
        ExecuteFormulaSubmittedPersistenceCommand(
            new DefineNamedRangeCommand(
                "TourProfit",
                context.ProfitRange,
                new NamedRangeMetadata("Workbook", "Submitted profit formulas.")),
            "Define Name",
            submittedCommands,
            out _);

        ExecuteFormulaSubmittedPersistenceCommand(
            new CreateNamedRangesFromSelectionCommand(context.AuthoringRange, UseTopRow: true, UseLeftColumn: false, UseBottomRow: false, UseRightColumn: false),
            "Create from Selection",
            submittedCommands,
            out var namesFromSelectionOutcome);

        var formulaEdits = new (CellAddress Address, Cell NewCell)[]
        {
            (new CellAddress(context.Sheet.Id, 2, 4), Cell.FromFormula("B2-C2")),
            (new CellAddress(context.Sheet.Id, 3, 4), Cell.FromFormula("B3-C3")),
            (new CellAddress(context.Sheet.Id, 4, 4), Cell.FromFormula("B4-C4")),
            (new CellAddress(context.Sheet.Id, 2, 5), Cell.FromFormula("D2/B2")),
            (new CellAddress(context.Sheet.Id, 3, 5), Cell.FromFormula("D3/B3")),
            (new CellAddress(context.Sheet.Id, 4, 5), Cell.FromFormula("D4/B4")),
            (context.AutoSumFormulaCell, Cell.FromFormula("SUM(TourRevenue)")),
            (context.ProfitTotalFormulaCell, Cell.FromFormula("SUM(TourProfit)"))
        };
        ExecuteFormulaSubmittedPersistenceCommand(
            new EditCellsCommand(context.Sheet.Id, formulaEdits),
            "Submitted formulas and AutoSum",
            submittedCommands,
            out var formulaOutcome);
    }

    private void ExecuteFormulaSubmittedPersistenceCommand(
        IWorkbookCommand command,
        string title,
        List<string> submittedCommands,
        out CommandOutcome outcome)
    {
        if (!TryExecuteCommand(command, title, out outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"Formula submitted/persistence tour command '{title}' failed.");

        submittedCommands.Add($"{command.GetType().Name}:{title}");
    }

    private async Task<ContextMenu> OpenFormulaSubmittedPersistenceUseInFormulaMenuAsync()
    {
        SelectFormulaAuthoringNamesRibbonTabForTour();
        var button = FindDescendantByRibbonCommandName<Button>(RibbonTabs, "Use in Formula")
            ?? throw new InvalidOperationException("Formula submitted/persistence tour could not find the Use in Formula button.");
        UseInFormulaBtn_Click(button, new RoutedEventArgs(ButtonBase.ClickEvent, button));
        var menu = button.ContextMenu
            ?? throw new InvalidOperationException("Formula submitted/persistence tour did not open the Use in Formula menu.");
        await Task.Delay(350);
        menu.UpdateLayout();
        return menu;
    }

    private NamedRangeDialog CreateFormulaSubmittedPersistenceNameManagerDialog(FormulaSubmittedPersistenceTourContext context)
    {
        var dialog = new NamedRangeDialog(_workbook, ExecuteDialogCommandPreservingSelection, context.AuthoringRange)
        {
            Owner = this
        };
        dialog.Show();
        dialog.Activate();
        dialog.UpdateLayout();
        return dialog;
    }

    private async Task CaptureFormulaSubmittedPersistenceNameManagerAsync(
        NamedRangeDialog dialog,
        string outputDir,
        List<FormulaSubmittedPersistenceTourManifestCapture> captures,
        FormulaSubmittedPersistenceTourContext context,
        string state,
        string fileName,
        string evidenceSummary)
    {
        await Task.Delay(450);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        captures.Add(CreateFormulaSubmittedPersistenceCapture(
            context,
            state,
            fileName,
            "Name Manager dialog",
            "RenderTargetBitmap-name-manager-dialog",
            dialog.ActualWidth,
            dialog.ActualHeight,
            evidenceSummary,
            menuHeaders: []));
    }

    private async Task<FormulaSubmittedPersistenceTourContext> SaveFormulaSubmittedPersistenceWorkbookAsync(
        string savedWorkbookPath,
        FormulaSubmittedPersistenceTourContext context)
    {
        var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".fxl", out _)
            ?? throw new InvalidOperationException("Formula submitted/persistence tour could not find the native FreeX save adapter.");
        if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
            throw new InvalidOperationException("Formula submitted/persistence tour could not save the native FreeX workbook.");

        return ResolveFormulaSubmittedPersistenceCurrentContext(
            savedWorkbookPath,
            "saved",
            new FileInfo(savedWorkbookPath).Length);
    }

    private FormulaSubmittedPersistenceTourContext ResolveFormulaSubmittedPersistenceCurrentContext(
        string savedWorkbookPath,
        string persistenceStage,
        long savedWorkbookBytes)
    {
        var sheet = _workbook.Sheets.FirstOrDefault(candidate => string.Equals(candidate.Name, "Formula Submit", StringComparison.OrdinalIgnoreCase))
            ?? GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Formula submitted/persistence tour could not resolve the current worksheet.");
        _currentSheetId = sheet.Id;
        return CreateFormulaSubmittedPersistenceContext(sheet, savedWorkbookPath, savedWorkbookBytes, persistenceStage);
    }

    private FormulaSubmittedPersistenceTourContext CreateFormulaSubmittedPersistenceContext(
        Sheet sheet,
        string savedWorkbookPath,
        long savedWorkbookBytes,
        string persistenceStage)
    {
        var authoringRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 5));
        var revenueRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 2));
        var profitRange = new GridRange(new CellAddress(sheet.Id, 2, 4), new CellAddress(sheet.Id, 4, 4));
        var resultRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 7, 5));
        return new FormulaSubmittedPersistenceTourContext(
            Sheet: sheet,
            AuthoringRange: authoringRange,
            RevenueRange: revenueRange,
            ProfitRange: profitRange,
            ResultRange: resultRange,
            AutoSumFormulaCell: new CellAddress(sheet.Id, 6, 2),
            ProfitTotalFormulaCell: new CellAddress(sheet.Id, 6, 4),
            NamedInsertionCell: new CellAddress(sheet.Id, 7, 2),
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookBytes: savedWorkbookBytes,
            PersistenceStage: persistenceStage);
    }

    private async Task<FormulaSubmittedPersistenceTourManifestCapture> CaptureFormulaSubmittedPersistenceWindowStateAsync(
        string outputDir,
        FormulaSubmittedPersistenceTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string commandPath)
    {
        SelectFormulaAuthoringNamesRibbonTabForTour();
        SetSelectionRange(context.ResultRange, context.ResultRange.Start);
        EnsureCellVisible(context.ResultRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 768);
        return CreateFormulaSubmittedPersistenceCapture(
            context,
            state,
            fileName,
            "Formulas ribbon and worksheet",
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 768),
            evidenceSummary,
            menuHeaders: [],
            commandPath);
    }

    private async Task<FormulaSubmittedPersistenceTourManifestCapture> CaptureFormulaSubmittedPersistenceCurrentWindowStateAsync(
        string outputDir,
        FormulaSubmittedPersistenceTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string commandPath)
    {
        SelectFormulaAuthoringNamesRibbonTabForTour();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 768);
        return CreateFormulaSubmittedPersistenceCapture(
            context,
            state,
            fileName,
            "Formulas ribbon, worksheet, and formula bar edit",
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 768),
            evidenceSummary,
            menuHeaders: [],
            commandPath);
    }

    private void SelectFormulaSubmittedPersistenceCell(CellAddress address)
    {
        var range = new GridRange(address, address);
        SetSelectionRange(range, address);
        EnsureCellVisible(address);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private FormulaSubmittedPersistenceTourManifestCapture CreateFormulaSubmittedPersistenceCapture(
        FormulaSubmittedPersistenceTourContext context,
        string state,
        string fileName,
        string surface,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary,
        IReadOnlyList<string> menuHeaders,
        string commandPath = "")
    {
        return new FormulaSubmittedPersistenceTourManifestCapture(
            CaptureKey: $"interactive:formula-submitted-persistence:{state}",
            PairKey: $"interactive:formula-submitted-persistence:{state}",
            ScenarioId: "formula-submitted-persistence:submitted-persistence-evidence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SheetName: context.Sheet.Name,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            FormulaBarText: FormulaBar.Text,
            NameCount: _workbook.NamedRanges.Count,
            DefinedNames: _workbook.NamedRanges.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            AutoSumFormula: GetFormulaText(context.Sheet, context.AutoSumFormulaCell),
            ProfitTotalFormula: GetFormulaText(context.Sheet, context.ProfitTotalFormulaCell),
            NamedInsertionFormula: GetFormulaText(context.Sheet, context.NamedInsertionCell),
            AutoSumValue: FormatScalarValue(context.Sheet.GetCell(context.AutoSumFormulaCell)?.Value),
            ProfitTotalValue: FormatScalarValue(context.Sheet.GetCell(context.ProfitTotalFormulaCell)?.Value),
            NamedInsertionValue: FormatScalarValue(context.Sheet.GetCell(context.NamedInsertionCell)?.Value),
            PersistenceStage: context.PersistenceStage,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            MenuHeaders: menuHeaders,
            CommandPath: commandPath,
            EvidenceSummary: evidenceSummary);
    }

    private static string GetFormulaText(Sheet sheet, CellAddress address)
    {
        var formula = sheet.GetCell(address)?.FormulaText;
        return formula is null ? string.Empty : $"={formula}";
    }

    private static string FormatScalarValue(ScalarValue? value) =>
        value switch
        {
            NumberValue number => number.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
            TextValue text => text.Value,
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            ErrorValue error => error.Code,
            BlankValue or null => string.Empty,
            _ => value.ToString() ?? string.Empty
        };

    private static IReadOnlyList<string> AddMenuHeadersToArray(ContextMenu menu)
    {
        var headers = new List<string>();
        AddMenuHeaders(menu, headers);
        return headers;
    }

    private static void DeleteFormulaSubmittedPersistenceTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_formula_submitted_persistence_*.*"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, FormulaSubmittedPersistenceTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateFormulaSubmittedPersistenceTourEvidence(
        string outputDir,
        IReadOnlyList<FormulaSubmittedPersistenceTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Formula submitted/persistence tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        if (!File.Exists(savedWorkbookPath) || new FileInfo(savedWorkbookPath).Length == 0)
            throw new InvalidOperationException("Formula submitted/persistence tour did not create the saved native workbook artifact.");
    }

    private static async Task WriteFormulaSubmittedPersistenceTourManifestAsync(
        string outputDir,
        FormulaSubmittedPersistenceTourContext context,
        IReadOnlyList<FormulaSubmittedPersistenceTourManifestCapture> captures,
        IReadOnlyList<string> submittedCommands)
    {
        var manifest = new FormulaSubmittedPersistenceTourManifest(
            Tool: "FREEX_FORMULA_SUBMITTED_PERSISTENCE_TOUR",
            EvidenceFamily: "formula-submitted-persistence",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "formula-submitted-persistence:submitted-persistence-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_formula_submitted_persistence_<State>.png",
            SavedWorkbookFileName: FormulaSubmittedPersistenceTourSavedWorkbookFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistencePath: "SaveWorkbookToTargetAsync(.fxl native FreeX adapter) then OpenFileAsync(saved .fxl)",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-FORMULAS-001", "UI-CMD-FORM-001", "UI-CMD-FORM-002"],
            SheetName: context.Sheet.Name,
            AuthoringRange: context.AuthoringRange.ToString(),
            ResultRange: context.ResultRange.ToString(),
            RevenueRange: context.RevenueRange.ToString(),
            ProfitRange: context.ProfitRange.ToString(),
            AutoSumFormulaCell: context.AutoSumFormulaCell.ToA1(),
            ProfitTotalFormulaCell: context.ProfitTotalFormulaCell.ToA1(),
            NamedInsertionCell: context.NamedInsertionCell.ToA1(),
            SubmittedCommands: submittedCommands,
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new FormulaSubmittedPersistenceTourManifestPairing(
                "interactive:formula-submitted-persistence:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed in-process RenderTargetBitmap capture; no foreground mouse, keyboard, keytip, UIA, or screen capture input was used."
                    : "Abort before file write unless the expected FreeX window/dialog owns foreground focus for each capture."),
            Captures: captures,
            CoveredStates:
            [
                "Seeded worksheet before formula/name submission.",
                "Submitted DefineNamedRangeCommand and CreateNamedRangesFromSelectionCommand defined workbook names.",
                "Submitted EditCellsCommand.ForFormula formulas produced visible AutoSum-style and named-reference results.",
                "Use in Formula insertion path placed a submitted defined name in the formula bar.",
                "Use in Formula menu listed submitted defined names.",
                "Name Manager dialog showed submitted names before save and after reopen.",
                "Native FreeX .fxl save/reopen restored submitted formula and defined-name state."
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF windows/menus with RenderTargetBitmap; it is not foreground CopyFromScreen proof.",
                "No physical mouse, keytip, Shift+F3, or UIA invocation is synthesized; command handlers and command-bus operations are invoked deterministically in process.",
                "The AutoSum proof submits the equivalent SUM formula through EditCellsCommand.ForFormula after deterministic setup; dropdown/keytip AutoSum submission remains separate foreground evidence.",
                "Persistence is proven for the native FreeX .fxl adapter through host save/open services; XLSX formula/name persistence and Microsoft Excel paired evidence remain separate compatibility lanes."
            ]);

        var path = Path.Combine(outputDir, FormulaSubmittedPersistenceTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.FormulaSubmittedPersistenceTourManifest);
    }

    private sealed record FormulaSubmittedPersistenceTourContext(
        Sheet Sheet,
        GridRange AuthoringRange,
        GridRange RevenueRange,
        GridRange ProfitRange,
        GridRange ResultRange,
        CellAddress AutoSumFormulaCell,
        CellAddress ProfitTotalFormulaCell,
        CellAddress NamedInsertionCell,
        string SavedWorkbookPath,
        long SavedWorkbookBytes,
        string PersistenceStage);

    private sealed record FormulaSubmittedPersistenceTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string SavedWorkbookFileName,
        long SavedWorkbookBytes,
        string PersistencePath,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string AuthoringRange,
        string ResultRange,
        string RevenueRange,
        string ProfitRange,
        string AutoSumFormulaCell,
        string ProfitTotalFormulaCell,
        string NamedInsertionCell,
        IReadOnlyList<string> SubmittedCommands,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        FormulaSubmittedPersistenceTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<FormulaSubmittedPersistenceTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record FormulaSubmittedPersistenceTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record FormulaSubmittedPersistenceTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string SelectedRange,
        string FormulaBarText,
        int NameCount,
        IReadOnlyList<string> DefinedNames,
        string AutoSumFormula,
        string ProfitTotalFormula,
        string NamedInsertionFormula,
        string AutoSumValue,
        string ProfitTotalValue,
        string NamedInsertionValue,
        string PersistenceStage,
        long SavedWorkbookBytes,
        IReadOnlyList<string> MenuHeaders,
        string CommandPath,
        string EvidenceSummary);
}
