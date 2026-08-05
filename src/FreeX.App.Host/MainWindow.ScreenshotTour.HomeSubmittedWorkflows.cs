using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using FreeX.App.Services;
using FreeX.App.Services.Ribbon;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureHomeSubmittedWorkflowsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteHomeSubmittedWorkflowsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 768;
        await Task.Delay(700);

        var captures = new List<HomeSubmittedWorkflowsTourManifestCapture>();

        try
        {
            var context = EnsureHomeSubmittedWorkflowsTourContext();
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "seeded-before-submissions",
                "freex_home_submitted_workflows_seeded_before",
                "Seeded worksheet before submitted Home operations, with paste source/target, insert/delete, hide/unhide, clear, find/replace, and F4/undo proof zones visible.",
                "Seeded worksheet",
                "worksheet-seed"));

            ExecuteHomeSubmittedPasteSpecial(context);
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "paste-special-values-source-formatting-result",
                "freex_home_submitted_workflows_paste_special_values_source_formatting",
                "Paste Special result is produced through PasteCommandFactory.CreateInternalPasteCommand with ValuesAndSourceFormatting and selected at the target range.",
                "Paste Special result grid",
                "PasteCommandFactory.CreateInternalPasteCommand -> PasteSpecialCellsCommand",
                context.PasteTargetRange));

            ExecuteHomeSubmittedRepeatableCommand(
                "Insert Row",
                () => new InsertRowsCommand(_currentSheetId, context.InsertRow, 1));
            SetSelectionRange(context.InsertResultRange, context.InsertResultRange.Start);
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "insert-row-result",
                "freex_home_submitted_workflows_insert_row_result",
                "Insert Row result is visible at row 18, with the seeded insert anchor shifted downward by the repeatable InsertRowsCommand.",
                "Insert row result grid",
                "CommandBus.ExecuteRepeatable -> InsertRowsCommand",
                context.InsertResultRange));

            ExecuteHomeSubmittedRepeatableCommand(
                "Delete Cells",
                () => new DeleteCellsCommand(_currentSheetId, context.DeleteCellsRange, DeleteCellsShiftDirection.Left));
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "delete-cells-shift-left-result",
                "freex_home_submitted_workflows_delete_cells_shift_left_result",
                "Delete Cells result is visible in row 4, where the selected cells were removed and the remaining row values shifted left.",
                "Delete cells result grid",
                "CommandBus.ExecuteRepeatable -> DeleteCellsCommand(DeleteCellsShiftDirection.Left)",
                context.DeleteCellsResultRange));

            ExecuteHomeSubmittedRepeatableCommand(
                "Hide Row",
                () => RowColumnSizingPlanner.CreateRowsHiddenCommand(_currentSheetId, context.HideRowRange, hidden: true));
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "hide-row-result",
                "freex_home_submitted_workflows_hidden_row_result",
                "Hide Row result omits row 7 from the visible grid while adjacent rows remain visible.",
                "Hidden row result grid",
                "CommandBus.ExecuteRepeatable -> RowColumnSizingPlanner.CreateRowsHiddenCommand(hidden: true)",
                context.HideRowRange));

            ExecuteHomeSubmittedRepeatableCommand(
                "Unhide Row",
                () => RowColumnSizingPlanner.CreateRowsHiddenCommand(_currentSheetId, context.HideRowRange, hidden: false));
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "unhide-row-result",
                "freex_home_submitted_workflows_unhidden_row_result",
                "Unhide Row result restores row 7 to the visible grid through the matching row-dimension command path.",
                "Unhidden row result grid",
                "CommandBus.ExecuteRepeatable -> RowColumnSizingPlanner.CreateRowsHiddenCommand(hidden: false)",
                context.HideRowRange));

            ExecuteHomeSubmittedRepeatableCommand(
                "Clear Formats and Contents",
                () => new CompositeWorkbookCommand(
                    "Clear Formats and Contents",
                    [
                        new ClearContentsCommand(_currentSheetId, context.ClearContentsRange),
                        new ApplyStyleCommand(_currentSheetId, context.ClearFormatsRange, CellStyleDiffPlanner.ClearFormatsDiff())
                    ]));
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "clear-formats-contents-result",
                "freex_home_submitted_workflows_clear_formats_contents_result",
                "Clear result shows ClearContentsCommand blanking the contents range and ApplyStyleCommand with CellStyleDiffPlanner.ClearFormatsDiff removing formatting from the adjacent range.",
                "Clear formats and contents result grid",
                "CommandBus.ExecuteRepeatable -> CompositeWorkbookCommand(ClearContentsCommand, ApplyStyleCommand(ClearFormatsDiff))",
                context.ClearResultRange));

            SynchronizeWorkbookSessionSelection();
            var replaceResult = _session.ReplaceAllValues(
                "FR_PENDING",
                "FR_SUBMITTED",
                new FindOptions(Within: FindWithin.Sheet, CurrentSheetId: _currentSheetId, LookIn: FindLookIn.Values),
                matchCase: false,
                matchEntireCell: true);
            if (!replaceResult.Success || replaceResult.ReplacedCount == 0)
                throw new InvalidOperationException(replaceResult.ErrorMessage ?? "Home submitted workflows tour could not execute Find/Replace.");
            ApplyWorkbookSessionSelectionToRenderer();
            CompleteHomeSubmittedCommandMutation(outcome: null);
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "find-replace-submitted-result",
                "freex_home_submitted_workflows_find_replace_submitted_result",
                "Find/Replace result shows FR_PENDING changed to FR_SUBMITTED through FindReplaceService.TryReplaceAll and the command bus.",
                "Find/Replace submitted result grid",
                "FindReplaceService.TryReplaceAll -> CommandBus.Execute -> EditCellsCommand",
                context.FindReplaceResultRange));

            SetSelectionRange(context.RepeatFirstRange, context.RepeatFirstRange.Start);
            ExecuteHomeSubmittedRepeatableCommand(
                "Clear Contents",
                () =>
                {
                    var range = SheetGrid.SelectedRange ?? context.RepeatFirstRange;
                    return new ClearContentsCommand(_currentSheetId, range);
                });
            SetSelectionRange(context.RepeatSecondRange, context.RepeatSecondRange.Start);
            if (!_session.CanRepeatLastAction)
                throw new InvalidOperationException("Home submitted workflows tour expected a repeatable command before F4 proof.");
            ExecuteRepeatLast();
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "f4-repeat-clear-contents-result",
                "freex_home_submitted_workflows_f4_repeat_clear_contents_result",
                "F4 repeat proof shows the same repeatable Clear Contents command applied to the next selected cell through CommandBus.RepeatLast.",
                "F4 repeat result grid",
                "KeyboardCommandShortcut.RepeatLastAction/F4 -> ExecuteRepeatLast -> CommandBus.RepeatLast",
                context.RepeatProofRange));

            if (!ExecuteUndo())
                throw new InvalidOperationException("Home submitted workflows tour could not undo the repeated Clear Contents command.");
            captures.Add(await CaptureHomeSubmittedWorkflowsWindowAsync(
                outputDir,
                context,
                "undo-restored-repeat-target",
                "freex_home_submitted_workflows_undo_restored_repeat_target",
                "Undo proof shows the most recent repeated clear restored while the first clear remains in place.",
                "Undo result grid",
                "KeyboardCommandShortcut.Undo/Ctrl+Z -> ExecuteUndo -> CommandBus.Undo",
                context.RepeatProofRange));

            ValidateHomeSubmittedWorkflowsTourEvidence(outputDir, captures);
            await WriteHomeSubmittedWorkflowsTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteHomeSubmittedWorkflowsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            ClearClipboardVisualState();
            _internalClipboard = null;
        }
    }

    private HomeSubmittedWorkflowsTourContext EnsureHomeSubmittedWorkflowsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Home submitted workflows tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        sheet.Name = "Home submitted workflows";
        sheet.HiddenRows.Clear();
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenCols.Clear();
        sheet.Comments.Clear();
        sheet.ThreadedComments.Clear();
        sheet.Hyperlinks.Clear();
        sheet.ReplaceMergedRegions([]);

        for (uint row = 1; row <= 22; row++)
        {
            for (uint col = 1; col <= 10; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        SetTourCell(sheet, 1, 1, new TextValue("Region"));
        SetTourCell(sheet, 1, 2, new TextValue("Delete A"));
        SetTourCell(sheet, 1, 3, new TextValue("Delete B"));
        SetTourCell(sheet, 1, 4, new TextValue("Shift C"));
        SetTourCell(sheet, 1, 5, new TextValue("Shift D"));
        SetTourCell(sheet, 1, 6, new TextValue("Status"));
        SetTourCell(sheet, 1, 8, new TextValue("Paste target"));
        SetTourCell(sheet, 1, 9, new TextValue("Paste target"));

        SetTourCell(sheet, 2, 1, new TextValue("North"));
        SetTourCell(sheet, 2, 2, new NumberValue(10));
        SetTourCell(sheet, 2, 3, new NumberValue(20));
        SetTourCell(sheet, 2, 4, new TextValue("Source 1"));
        SetTourCell(sheet, 2, 5, new NumberValue(101));
        SetTourCell(sheet, 2, 6, new TextValue("Open"));
        SetTourCell(sheet, 3, 1, new TextValue("South"));
        SetTourCell(sheet, 3, 2, new NumberValue(11));
        SetTourCell(sheet, 3, 3, new NumberValue(21));
        SetTourCell(sheet, 3, 4, new TextValue("Source 2"));
        SetTourCell(sheet, 3, 5, new NumberValue(202));
        SetTourCell(sheet, 3, 6, new TextValue("Open"));
        SetTourCell(sheet, 4, 1, new TextValue("Delete cells"));
        SetTourCell(sheet, 4, 2, new TextValue("Remove A"));
        SetTourCell(sheet, 4, 3, new TextValue("Remove B"));
        SetTourCell(sheet, 4, 4, new TextValue("Shifted C"));
        SetTourCell(sheet, 4, 5, new TextValue("Shifted D"));
        SetTourCell(sheet, 4, 6, new TextValue("Tail"));
        SetTourCell(sheet, 5, 1, new TextValue("Neighbor row"));
        SetTourCell(sheet, 6, 1, new TextValue("Above hidden row"));
        SetTourCell(sheet, 7, 1, new TextValue("Hide/unhide proof row"));
        SetTourCell(sheet, 8, 1, new TextValue("Below hidden row"));

        SetTourCell(sheet, 10, 1, new TextValue("Clear contents"));
        SetTourCell(sheet, 10, 2, new TextValue("Clear me"));
        SetTourCell(sheet, 10, 3, new TextValue("Clear me too"));
        SetTourCell(sheet, 10, 4, new TextValue("Clear formats"));
        SetTourCell(sheet, 10, 5, new TextValue("Keep text"));
        SetTourCell(sheet, 12, 1, new TextValue("Find/Replace"));
        SetTourCell(sheet, 12, 2, new TextValue("FR_PENDING"));
        SetTourCell(sheet, 13, 2, new TextValue("FR_PENDING"));
        SetTourCell(sheet, 15, 1, new TextValue("F4 repeat"));
        SetTourCell(sheet, 15, 2, new TextValue("Repeat clear 1"));
        SetTourCell(sheet, 16, 2, new TextValue("Repeat clear 2"));
        SetTourCell(sheet, 18, 1, new TextValue("Insert row anchor"));
        SetTourCell(sheet, 19, 1, new TextValue("Insert row neighbor"));

        var pasteSourceRange = Range(sheet.Id, 2, 4, 3, 5);
        var pasteTargetRange = Range(sheet.Id, 2, 8, 3, 9);
        var deleteCellsRange = Range(sheet.Id, 4, 2, 4, 3);
        var deleteCellsResultRange = Range(sheet.Id, 4, 1, 4, 6);
        var hideRowRange = Range(sheet.Id, 7, 1, 7, 1);
        var clearContentsRange = Range(sheet.Id, 10, 2, 10, 3);
        var clearFormatsRange = Range(sheet.Id, 10, 4, 10, 5);
        var clearResultRange = Range(sheet.Id, 10, 1, 10, 5);
        var findReplaceResultRange = Range(sheet.Id, 12, 1, 13, 2);
        var repeatFirstRange = Range(sheet.Id, 15, 2, 15, 2);
        var repeatSecondRange = Range(sheet.Id, 16, 2, 16, 2);
        var repeatProofRange = Range(sheet.Id, 15, 1, 16, 2);
        var insertResultRange = Range(sheet.Id, 18, 1, 19, 1);

        ApplyHomeSubmittedStyle(pasteSourceRange, new StyleDiff(Bold: true, FillColor: new CellColor(226, 239, 218)));
        ApplyHomeSubmittedStyle(pasteTargetRange, new StyleDiff(FillColor: new CellColor(255, 242, 204)));
        ApplyHomeSubmittedStyle(clearContentsRange, new StyleDiff(FillColor: new CellColor(248, 203, 173)));
        ApplyHomeSubmittedStyle(clearFormatsRange, new StyleDiff(Bold: true, Italic: true, FillColor: new CellColor(189, 215, 238)));
        ApplyHomeSubmittedStyle(findReplaceResultRange, new StyleDiff(FillColor: new CellColor(217, 225, 242)));
        ApplyHomeSubmittedStyle(repeatProofRange, new StyleDiff(FillColor: new CellColor(234, 241, 221)));

        SetSelectionRange(Range(sheet.Id, 1, 1, 19, 9), new CellAddress(sheet.Id, 1, 1));
        EnsureCellVisible(new CellAddress(sheet.Id, 1, 1));
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new HomeSubmittedWorkflowsTourContext(
            sheet,
            pasteSourceRange,
            pasteTargetRange,
            deleteCellsRange,
            deleteCellsResultRange,
            hideRowRange,
            clearContentsRange,
            clearFormatsRange,
            clearResultRange,
            findReplaceResultRange,
            repeatFirstRange,
            repeatSecondRange,
            repeatProofRange,
            insertResultRange,
            InsertRow: 18);
    }

    private void ApplyHomeSubmittedStyle(GridRange range, StyleDiff diff)
    {
        if (!TryExecuteApplyStyle(range, diff, "Apply Style"))
            throw new InvalidOperationException($"Home submitted workflows tour could not apply style to {range}.");
    }

    private void ExecuteHomeSubmittedPasteSpecial(HomeSubmittedWorkflowsTourContext context)
    {
        var sourceCells = new List<(CellAddress Source, Cell Cell)>();
        for (var row = context.PasteSourceRange.Start.Row; row <= context.PasteSourceRange.End.Row; row++)
        {
            for (var col = context.PasteSourceRange.Start.Col; col <= context.PasteSourceRange.End.Col; col++)
            {
                var address = new CellAddress(_currentSheetId, row, col);
                sourceCells.Add((address, context.Sheet.GetCell(address)?.Clone() ?? Cell.FromValue(BlankValue.Instance)));
            }
        }

        ExecuteHomeSubmittedRepeatableCommand(
            "Paste Special",
            () => PasteCommandFactory.CreateInternalPasteCommand(
                _workbook,
                _currentSheetId,
                context.PasteSourceRange,
                sourceCells,
                context.PasteTargetRange.Start,
                PasteCellsMode.All,
                new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting)));
        SetSelectionRange(context.PasteTargetRange, context.PasteTargetRange.Start);
        SheetGrid.ClipboardRange = context.PasteSourceRange;
        SheetGrid.ClipboardIsCut = false;
    }

    private CommandOutcome ExecuteHomeSubmittedRepeatableCommand(string title, Func<IWorkbookCommand> createCommand)
    {
        if (!TryExecuteRepeatableCommand(createCommand, title, out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"Home submitted workflows tour could not execute {title}.");

        CompleteHomeSubmittedCommandMutation(outcome);
        return outcome;
    }

    private void CompleteHomeSubmittedCommandMutation(CommandOutcome? outcome)
    {
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private async Task<HomeSubmittedWorkflowsTourManifestCapture> CaptureHomeSubmittedWorkflowsWindowAsync(
        string outputDir,
        HomeSubmittedWorkflowsTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string surface,
        string commandRoute,
        GridRange? selectedRange = null)
    {
        HideStartScreen();
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
        if (selectedRange is { } range)
        {
            SetSelectionRange(range, range.Start);
            EnsureCellVisible(range.Start);
        }

        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateHomeSubmittedWorkflowsCapture(
            context,
            state,
            surface,
            fileName,
            commandRoute,
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary);
    }

    private HomeSubmittedWorkflowsTourManifestCapture CreateHomeSubmittedWorkflowsCapture(
        HomeSubmittedWorkflowsTourContext context,
        string state,
        string surface,
        string fileName,
        string commandRoute,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary) =>
        new(
            CaptureKey: $"interactive:home-submitted-workflows:{state}",
            PairKey: $"interactive:home-submitted-workflows:{state}",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-main-window",
            CommandRoute: commandRoute,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SelectedRange: SheetGrid.SelectedRange?.ToString() ?? string.Empty,
            HiddenRows: string.Join(",", context.Sheet.HiddenRows.OrderBy(row => row)),
            CanUndo: _session.CanUndo,
            CanRedo: _session.CanRedo,
            CanRepeat: _session.CanRepeatLastAction,
            EvidenceSummary: evidenceSummary);

    private static void DeleteHomeSubmittedWorkflowsTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_home_submitted_workflows_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, HomeSubmittedWorkflowsTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateHomeSubmittedWorkflowsTourEvidence(
        string outputDir,
        IReadOnlyList<HomeSubmittedWorkflowsTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Home submitted workflows tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private static async Task WriteHomeSubmittedWorkflowsTourManifestAsync(
        string outputDir,
        HomeSubmittedWorkflowsTourContext context,
        IReadOnlyList<HomeSubmittedWorkflowsTourManifestCapture> captures)
    {
        var plannedCaptureKeys = new[]
        {
            "seeded-before-submissions",
            "paste-special-values-source-formatting-result",
            "insert-row-result",
            "delete-cells-shift-left-result",
            "hide-row-result",
            "unhide-row-result",
            "clear-formats-contents-result",
            "find-replace-submitted-result",
            "f4-repeat-clear-contents-result",
            "undo-restored-repeat-target"
        };

        var manifest = new HomeSubmittedWorkflowsTourManifest(
            Tool: "FREEX_HOME_SUBMITTED_WORKFLOWS_TOUR",
            EvidenceFamily: "home-submitted-workflows",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "home-submitted-workflows:submitted-mutation-undo-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_home_submitted_workflows_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-HOME-004",
            CatalogCommandRows:
            [
                "UI-CMD-HOME-CLIP-002",
                "UI-CMD-HOME-CELLS-001",
                "UI-CMD-HOME-CELLS-002",
                "UI-CMD-HOME-CELLS-004",
                "UI-CMD-HOME-EDIT-003",
                "UI-CMD-HOME-EDIT-004"
            ],
            SheetName: context.Sheet.Name,
            PasteSourceRange: context.PasteSourceRange.ToString(),
            PasteTargetRange: context.PasteTargetRange.ToString(),
            DeleteCellsRange: context.DeleteCellsRange.ToString(),
            HideRowRange: context.HideRowRange.ToString(),
            ClearContentsRange: context.ClearContentsRange.ToString(),
            ClearFormatsRange: context.ClearFormatsRange.ToString(),
            FindReplaceRange: context.FindReplaceResultRange.ToString(),
            RepeatProofRange: context.RepeatProofRange.ToString(),
            CaptureStatus: "complete",
            CaptureMode: "RenderTargetBitmap-in-process",
            PlannedCaptureCount: plannedCaptureKeys.Length,
            ActualCaptureCount: captures.Count,
            PlannedCaptureKeys: plannedCaptureKeys,
            ActualCaptureKeys: captures.Select(capture => capture.State).ToArray(),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, keytip, OS clipboard, or screen capture input is used."
                    : "Window captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            CommandRoutesUsed: captures.Select(capture => capture.CommandRoute).Distinct().ToArray(),
            Captures: captures,
            CoveredStates:
            [
                "Seeded before state for submitted Home workflows",
                "Paste Special values/source-formatting result",
                "Insert row result",
                "Delete cells shift-left result",
                "Hide row and unhide row result proof",
                "Clear contents and clear formats result",
                "Find/Replace submitted mutation",
                "F4 repeat through CommandBus.RepeatLast",
                "Undo through CommandBus.Undo"
            ],
            Limitations:
            [
                "This bounded tour drives FreeX command/service paths in process and captures the resulting WPF grid/ribbon state with RenderTargetBitmap.",
                "The tour does not synthesize physical mouse, Alt/keytip, access-key dialog submission, foreground OS clipboard, UI Automation, range-picker, or screen-wide CopyFromScreen input.",
                "Paste Special is represented by the supported ValuesAndSourceFormatting command route rather than foreground Paste Special dialog OK submission.",
                "Insert/delete evidence uses representative row insertion and cell deletion; row/column/table/protected/grouped-sheet target breadth remains separate.",
                "Hide/unhide proof covers a representative row; column/sheet hide/unhide and protected disabled visuals remain separate.",
                "Find/Replace proof uses Replace All against cell values through the service/command bus; dialog access-key and option breadth remain covered by separate menu/dialog tours.",
                "Excel-paired screenshots, save/reload persistence breadth, foreground mouse/keytip proof, and broader target matrices remain open unless produced by a paired runner."
            ]);

        var path = Path.Combine(outputDir, HomeSubmittedWorkflowsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.HomeSubmittedWorkflowsTourManifest);
    }

    private sealed record HomeSubmittedWorkflowsTourContext(
        Sheet Sheet,
        GridRange PasteSourceRange,
        GridRange PasteTargetRange,
        GridRange DeleteCellsRange,
        GridRange DeleteCellsResultRange,
        GridRange HideRowRange,
        GridRange ClearContentsRange,
        GridRange ClearFormatsRange,
        GridRange ClearResultRange,
        GridRange FindReplaceResultRange,
        GridRange RepeatFirstRange,
        GridRange RepeatSecondRange,
        GridRange RepeatProofRange,
        GridRange InsertResultRange,
        uint InsertRow);

    private sealed record HomeSubmittedWorkflowsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogCommandRows,
        string SheetName,
        string PasteSourceRange,
        string PasteTargetRange,
        string DeleteCellsRange,
        string HideRowRange,
        string ClearContentsRange,
        string ClearFormatsRange,
        string FindReplaceRange,
        string RepeatProofRange,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<string> PlannedCaptureKeys,
        IReadOnlyList<string> ActualCaptureKeys,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<string> CommandRoutesUsed,
        IReadOnlyList<HomeSubmittedWorkflowsTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record HomeSubmittedWorkflowsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string CommandRoute,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        string HiddenRows,
        bool CanUndo,
        bool CanRedo,
        bool CanRepeat,
        string EvidenceSummary);
}
