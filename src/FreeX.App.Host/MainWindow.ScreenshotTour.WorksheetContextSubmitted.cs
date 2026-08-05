using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const string WorksheetContextSubmittedTourManifestFileName = "worksheet_context_submitted_tour_manifest.json";

    private async Task CaptureWorksheetContextSubmittedTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteWorksheetContextSubmittedTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 780;
        await Task.Delay(700);

        var captures = new List<WorksheetContextSubmittedTourManifestCapture>();
        var workflows = new List<WorksheetContextSubmittedTourManifestWorkflow>();
        var commandOutcomes = new List<WorksheetContextSubmittedTourCommandOutcome>();
        var savedWorkbookPath = Path.Combine(outputDir, WorksheetContextSubmittedTourSavedWorkbookFileName);

        try
        {
            var context = EnsureWorksheetContextSubmittedTourContext();
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "seeded-before-submissions",
                "freex_worksheet_context_submitted_seeded_before",
                "Seeded worksheet before submitted worksheet context-menu command routes, with insert/delete, clear, note, threaded-comment, hyperlink, protected, undo/redo, and persistence zones visible.",
                "Setup seeded workbook; no submitted context command yet."));

            captures.Add(await CaptureWorksheetContextSubmittedMenuAsync(
                outputDir,
                context.NoteCell,
                "note-menu-available",
                "freex_worksheet_context_submitted_note_menu_available",
                "Context menu for the note cell shows note-backed commands enabled before Delete Note submission."));
            ExecuteWorksheetContextSubmittedAction(
                context,
                WorksheetContextMenuAction.DeleteNote,
                context.NoteCell,
                new GridRange(context.NoteCell, context.NoteCell),
                "Delete Note",
                "ExecuteWorksheetContextMenuAction(DeleteNote) -> ReviewDeleteCommentBtn_Click -> DeleteCommentCommand",
                commandOutcomes);
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "delete-note-result",
                "freex_worksheet_context_submitted_delete_note_result",
                "Delete Note result removes the seeded note from B9 through the worksheet context-menu route.",
                "ExecuteWorksheetContextMenuAction(DeleteNote) -> DeleteCommentCommand",
                new GridRange(context.NoteCell, context.NoteCell)));
            workflows.Add(CreateWorksheetContextSubmittedWorkflow(
                "Delete Note submitted result",
                "ExecuteWorksheetContextMenuAction(DeleteNote) -> DeleteCommentCommand",
                "delete-note-result"));

            ExecuteWorksheetContextSubmittedAction(
                context,
                WorksheetContextMenuAction.ResolveComment,
                context.ThreadedCommentCell,
                new GridRange(context.ThreadedCommentCell, context.ThreadedCommentCell),
                "Resolve Comment",
                "ExecuteWorksheetContextMenuAction(ResolveComment) -> ResolveContextThreadedComment -> ResolveThreadedCommentCommand",
                commandOutcomes);
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "resolve-threaded-comment-result",
                "freex_worksheet_context_submitted_resolve_comment_result",
                "Resolve Comment result marks the seeded threaded comment at B11 as resolved through the context-menu command path.",
                "ExecuteWorksheetContextMenuAction(ResolveComment) -> ResolveThreadedCommentCommand",
                new GridRange(context.ThreadedCommentCell, context.ThreadedCommentCell)));
            workflows.Add(CreateWorksheetContextSubmittedWorkflow(
                "Resolve Comment submitted result",
                "ExecuteWorksheetContextMenuAction(ResolveComment) -> ResolveThreadedCommentCommand",
                "resolve-threaded-comment-result"));

            captures.Add(await CaptureWorksheetContextSubmittedMenuAsync(
                outputDir,
                context.HyperlinkCell,
                "hyperlink-menu-available",
                "freex_worksheet_context_submitted_hyperlink_menu_available",
                "Context menu for the hyperlink cell shows Open/Edit/Remove Hyperlink and Clear Hyperlinks state before Remove Hyperlink submission."));
            ExecuteWorksheetContextSubmittedAction(
                context,
                WorksheetContextMenuAction.RemoveHyperlinks,
                context.HyperlinkCell,
                new GridRange(context.HyperlinkCell, context.HyperlinkCell),
                "Remove Hyperlink",
                "ExecuteWorksheetContextMenuAction(RemoveHyperlinks) -> RemoveHyperlinkMenuItem_Click -> ClearHyperlinksCommand",
                commandOutcomes);
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "remove-hyperlink-result",
                "freex_worksheet_context_submitted_remove_hyperlink_result",
                "Remove Hyperlink result clears the link target and preserves the cell's visible hyperlink styling (blue/underline) while preserving display text.",
                "ExecuteWorksheetContextMenuAction(RemoveHyperlinks) -> RemoveHyperlinkMenuItem_Click -> ClearHyperlinksCommand",
                new GridRange(context.HyperlinkCell, context.HyperlinkCell)));
            workflows.Add(CreateWorksheetContextSubmittedWorkflow(
                "Remove Hyperlink submitted result",
                "ExecuteWorksheetContextMenuAction(RemoveHyperlinks) -> RemoveHyperlinkMenuItem_Click -> ClearHyperlinksCommand",
                "remove-hyperlink-result"));

            ExecuteWorksheetContextSubmittedAction(
                context,
                WorksheetContextMenuAction.ClearContents,
                context.ClearContentsRange.Start,
                context.ClearContentsRange,
                "Clear Contents",
                "ExecuteWorksheetContextMenuAction(ClearContents) -> ExecuteClearSelection -> ClearContentsCommand",
                commandOutcomes);
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "clear-contents-result",
                "freex_worksheet_context_submitted_clear_contents_result",
                "Clear Contents result blanks B5:C5 through the worksheet context-menu route while retaining the formatted proof cells.",
                "ExecuteWorksheetContextMenuAction(ClearContents) -> ClearContentsCommand",
                context.ClearContentsRange));
            workflows.Add(CreateWorksheetContextSubmittedWorkflow(
                "Clear Contents submitted result",
                "ExecuteWorksheetContextMenuAction(ClearContents) -> ClearContentsCommand",
                "clear-contents-result"));

            ExecuteWorksheetContextSubmittedAction(
                context,
                WorksheetContextMenuAction.InsertRowAbove,
                context.InsertRowCell,
                new GridRange(context.InsertRowCell, context.InsertRowCell),
                "Insert Row Above",
                "ExecuteWorksheetContextMenuAction(InsertRowAbove) -> InsertRows -> InsertRowsCommand",
                commandOutcomes);
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "insert-row-above-result",
                "freex_worksheet_context_submitted_insert_row_above_result",
                "Insert Row Above result creates a blank row above the seeded row-16 target and shifts the row markers downward.",
                "ExecuteWorksheetContextMenuAction(InsertRowAbove) -> InsertRowsCommand",
                Range(_currentSheetId, 16, 1, 18, 4)));
            workflows.Add(CreateWorksheetContextSubmittedWorkflow(
                "Insert Row Above submitted result",
                "ExecuteWorksheetContextMenuAction(InsertRowAbove) -> InsertRowsCommand",
                "insert-row-above-result"));

            SelectColumn(context.DeleteColumnIndex);
            ExecuteWorksheetContextSubmittedAction(
                context,
                WorksheetContextMenuAction.DeleteColumns,
                context.DeleteColumnCell,
                SheetGrid.SelectedRange ?? new GridRange(context.DeleteColumnCell, context.DeleteColumnCell),
                "Delete Column(s)",
                "ExecuteWorksheetContextMenuAction(DeleteColumns) -> DeleteSelectedColumns -> DeleteColumnsCommand",
                commandOutcomes);
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "delete-column-result",
                "freex_worksheet_context_submitted_delete_column_result",
                "Delete Column(s) result removes the seeded H-column command target so the former I-column survivor shifts left.",
                "ExecuteWorksheetContextMenuAction(DeleteColumns) -> DeleteColumnsCommand",
                Range(_currentSheetId, 1, 7, 6, 9)));
            workflows.Add(CreateWorksheetContextSubmittedWorkflow(
                "Delete Column(s) submitted result",
                "ExecuteWorksheetContextMenuAction(DeleteColumns) -> DeleteColumnsCommand",
                "delete-column-result"));

            var protectedOutcome = ExecuteWorksheetContextSubmittedProtectedBlockedCommand(context);
            commandOutcomes.Add(new WorksheetContextSubmittedTourCommandOutcome(
                "protected-clear-contents-blocked",
                "ClearContentsCommand on protected locked B13",
                protectedOutcome.Success,
                protectedOutcome.ErrorMessage ?? "",
                _session.CanUndo,
                _session.CanRedo));
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "protected-clear-contents-blocked",
                "freex_worksheet_context_submitted_protected_clear_blocked",
                "Protected locked-cell proof keeps B13 unchanged after ClearContentsCommand is rejected with a protected-sheet outcome.",
                "CommandBus.ExecuteRepeatable(ClearContentsCommand on protected locked cell) -> blocked",
                new GridRange(context.ProtectedCell, context.ProtectedCell)));
            workflows.Add(CreateWorksheetContextSubmittedWorkflow(
                "Protected locked target blocked result",
                "CommandBus.ExecuteRepeatable(ClearContentsCommand) -> protected-sheet rejection",
                "protected-clear-contents-blocked"));

            context.Sheet.IsProtected = false;
            if (!ExecuteUndo())
                throw new InvalidOperationException("Worksheet context-submitted tour could not undo the submitted Delete Column(s) command.");
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "undo-restored-delete-column",
                "freex_worksheet_context_submitted_undo_restored_delete_column",
                "Undo proof restores the deleted H-column target through CommandBus.Undo.",
                "KeyboardCommandShortcut.Undo/Ctrl+Z -> ExecuteUndo -> CommandBus.Undo",
                Range(_currentSheetId, 1, 7, 6, 9)));

            if (!ExecuteRedo())
                throw new InvalidOperationException("Worksheet context-submitted tour could not redo the submitted Delete Column(s) command.");
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "redo-reapplied-delete-column",
                "freex_worksheet_context_submitted_redo_reapplied_delete_column",
                "Redo proof reapplies the Delete Column(s) context-menu mutation through CommandBus.Redo.",
                "KeyboardCommandShortcut.Redo/Ctrl+Y -> ExecuteRedo -> CommandBus.Redo",
                Range(_currentSheetId, 1, 7, 6, 9)));
            workflows.Add(CreateWorksheetContextSubmittedWorkflow(
                "Undo/redo submitted command result",
                "ExecuteUndo -> ExecuteRedo",
                "undo-restored-delete-column",
                "redo-reapplied-delete-column"));

            if (File.Exists(savedWorkbookPath))
                File.Delete(savedWorkbookPath);
            var adapter = FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ".fxl", out _)
                ?? throw new InvalidOperationException("Worksheet context-submitted tour could not find a native FreeX save adapter.");
            if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
                throw new InvalidOperationException("Worksheet context-submitted tour could not save the workflow workbook.");
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context,
                "save-persistence-path",
                "freex_worksheet_context_submitted_save_persistence_path",
                "Save result persists the submitted worksheet context-menu mutation state to a native FreeX workbook path.",
                "SaveWorkbookToTargetAsync(FileSaveTarget(savedWorkbookPath, fxlAdapter))"));

            await OpenFileAsync(savedWorkbookPath);
            var reopenedSheet = _workbook.Sheets.FirstOrDefault(sheet => sheet.Name == context.SheetName)
                ?? throw new InvalidOperationException("Worksheet context-submitted tour could not find the evidence sheet after reopen.");
            _currentSheetId = reopenedSheet.Id;
            SetSelectionRange(Range(reopenedSheet.Id, 1, 1, 18, 8), new CellAddress(reopenedSheet.Id, 1, 1));
            EnsureCellVisible(new CellAddress(reopenedSheet.Id, 1, 1));
            captures.Add(await CaptureWorksheetContextSubmittedWindowAsync(
                outputDir,
                context with { Sheet = reopenedSheet },
                "reopened-persistence-result",
                "freex_worksheet_context_submitted_reopened_persistence_result",
                "Reopened persistence result shows the submitted context-command state after OpenFileAsync loads the saved native workbook.",
                "OpenFileAsync(savedWorkbookPath) -> NativeJsonAdapter"));
            workflows.Add(CreateWorksheetContextSubmittedWorkflow(
                "Save/reopen persistence result",
                "SaveWorkbookToTargetAsync -> OpenFileAsync",
                "save-persistence-path",
                "reopened-persistence-result"));

            ValidateWorksheetContextSubmittedTourEvidence(outputDir, captures, savedWorkbookPath);
            await WriteWorksheetContextSubmittedTourManifestAsync(outputDir, savedWorkbookPath, context, captures, workflows, commandOutcomes);
        }
        catch
        {
            DeleteWorksheetContextSubmittedTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (SheetGrid.ContextMenu is { IsOpen: true } menu)
                menu.IsOpen = false;
        }
    }

    private WorksheetContextSubmittedTourContext EnsureWorksheetContextSubmittedTourContext()
    {
        CreateNewWorkbook();
        HideStartScreen();

        var sheet = _workbook.Sheets[0];
        sheet.Name = "Worksheet context submitted";
        _currentSheetId = sheet.Id;
        sheet.IsProtected = false;
        sheet.ProtectionPassword = null;
        sheet.ProtectionPermissions.Clear();
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.SelectLockedCells);
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.SelectUnlockedCells);
        sheet.AutoFilter = null;
        sheet.Comments.Clear();
        sheet.ThreadedComments.Clear();
        sheet.Hyperlinks.Clear();
        sheet.HyperlinkMetadata.Clear();
        sheet.DataValidations.Clear();
        sheet.StructuredTables.Clear();
        sheet.ReplaceMergedRegions([]);

        for (uint row = 1; row <= 22; row++)
        {
            for (uint col = 1; col <= 10; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        for (uint col = 1; col <= 10; col++)
            sheet.ColumnWidths[col] = col is 1 ? 24 : 18;

        SetTourCell(sheet, 1, 1, new TextValue("Context submitted"));
        SetTourCell(sheet, 1, 7, new TextValue("Column proof G"));
        SetTourCell(sheet, 1, 8, new TextValue("DELETE COLUMN H"));
        SetTourCell(sheet, 1, 9, new TextValue("Column survivor I"));

        SetTourCell(sheet, 3, 1, new TextValue("Delete column target"));
        SetTourCell(sheet, 3, 7, new TextValue("Keep G"));
        SetTourCell(sheet, 3, 8, new TextValue("Remove H"));
        SetTourCell(sheet, 3, 9, new TextValue("Shift left I"));

        SetTourCell(sheet, 5, 1, new TextValue("Clear contents target"));
        SetTourCell(sheet, 5, 2, new TextValue("Clear me"));
        SetTourCell(sheet, 5, 3, new TextValue("Clear me too"));

        SetTourCell(sheet, 7, 1, new TextValue("Formatted neighbors"));
        SetTourCell(sheet, 7, 2, new TextValue("Format remains"));
        SetTourCell(sheet, 7, 3, new TextValue("After clear"));

        SetTourCell(sheet, 9, 1, new TextValue("Note/hyperlink"));
        SetTourCell(sheet, 9, 2, new TextValue("Delete note target"));
        SetTourCell(sheet, 9, 3, new TextValue("Remove hyperlink target"));

        SetTourCell(sheet, 11, 1, new TextValue("Threaded comment"));
        SetTourCell(sheet, 11, 2, new TextValue("Resolve comment target"));

        SetTourCell(sheet, 13, 1, new TextValue("Protected locked target"));
        SetTourCell(sheet, 13, 2, new TextValue("Locked edit blocked"));

        SetTourCell(sheet, 16, 1, new TextValue("Insert row target"));
        SetTourCell(sheet, 16, 2, new TextValue("Insert Row Above target"));
        SetTourCell(sheet, 17, 1, new TextValue("Insert row survivor"));
        SetTourCell(sheet, 17, 2, new TextValue("Shifted down after insert"));

        var clearContentsRange = Range(sheet.Id, 5, 2, 5, 3);
        var noteCell = new CellAddress(sheet.Id, 9, 2);
        var hyperlinkCell = new CellAddress(sheet.Id, 9, 3);
        var threadedCommentCell = new CellAddress(sheet.Id, 11, 2);
        var protectedCell = new CellAddress(sheet.Id, 13, 2);
        var insertRowCell = new CellAddress(sheet.Id, 16, 2);
        var deleteColumnCell = new CellAddress(sheet.Id, 1, 8);

        ApplyWorksheetContextSubmittedStyle(clearContentsRange, new StyleDiff(FillColor: new CellColor(248, 203, 173)));
        ApplyWorksheetContextSubmittedStyle(Range(sheet.Id, 7, 2, 7, 3), new StyleDiff(Bold: true, FillColor: new CellColor(189, 215, 238)));
        ApplyWorksheetContextSubmittedStyle(new GridRange(protectedCell, protectedCell), new StyleDiff(FillColor: new CellColor(217, 217, 217)));

        if (!TryExecuteCommand(new SetCommentCommand(sheet.Id, noteCell, "Context submitted note evidence"), "Seed Note"))
            throw new InvalidOperationException("Worksheet context-submitted tour could not seed the note.");
        sheet.ThreadedComments[threadedCommentCell] = new ThreadedComment("Context submitted threaded comment", "FreeX");
        if (!TryExecuteCommand(
                new SetHyperlinkCommand(
                    sheet.Id,
                    hyperlinkCell,
                    "https://example.test/freex-worksheet-context-submitted",
                    "Remove hyperlink target",
                    new HyperlinkMetadata(ScreenTip: "Context submitted hyperlink evidence")),
                "Seed Hyperlink"))
        {
            throw new InvalidOperationException("Worksheet context-submitted tour could not seed the hyperlink.");
        }

        SetSelectionRange(Range(sheet.Id, 1, 1, 18, 9), new CellAddress(sheet.Id, 1, 1));
        EnsureCellVisible(new CellAddress(sheet.Id, 1, 1));
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Home"));
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new WorksheetContextSubmittedTourContext(
            Sheet: sheet,
            SheetName: sheet.Name,
            ClearContentsRange: clearContentsRange,
            NoteCell: noteCell,
            HyperlinkCell: hyperlinkCell,
            ThreadedCommentCell: threadedCommentCell,
            ProtectedCell: protectedCell,
            InsertRowCell: insertRowCell,
            DeleteColumnCell: deleteColumnCell,
            DeleteColumnIndex: 8);
    }

    private void ApplyWorksheetContextSubmittedStyle(GridRange range, StyleDiff diff)
    {
        if (!TryExecuteApplyStyle(range, diff, "Worksheet Context Submitted Style"))
            throw new InvalidOperationException($"Worksheet context-submitted tour could not apply style to {range}.");
    }

    private void ExecuteWorksheetContextSubmittedAction(
        WorksheetContextSubmittedTourContext context,
        WorksheetContextMenuAction action,
        CellAddress address,
        GridRange selection,
        string title,
        string commandRoute,
        List<WorksheetContextSubmittedTourCommandOutcome> commandOutcomes)
    {
        SetSelectionRange(selection, selection.Start);
        EnsureCellVisible(selection.Start);
        UpdateViewport();
        ExecuteWorksheetContextMenuAction(action, address);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();

        commandOutcomes.Add(new WorksheetContextSubmittedTourCommandOutcome(
            action.ToString(),
            commandRoute,
            true,
            "",
            _session.CanUndo,
            _session.CanRedo));
    }

    private CommandOutcome ExecuteWorksheetContextSubmittedProtectedBlockedCommand(WorksheetContextSubmittedTourContext context)
    {
        context.Sheet.IsProtected = true;
        SetSelectionRange(new GridRange(context.ProtectedCell, context.ProtectedCell), context.ProtectedCell);
        EnsureCellVisible(context.ProtectedCell);
        UpdateViewport();

        SynchronizeWorkbookSessionSelection();
        var outcome = ToCommandOutcome(_session.ExecuteRepeatableCommandPreservingSelection(
            () => new ClearContentsCommand(_currentSheetId, new GridRange(context.ProtectedCell, context.ProtectedCell))));
        if (outcome.Success)
            throw new InvalidOperationException("Worksheet context-submitted tour expected protected Clear Contents to be rejected.");

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        return outcome;
    }

    private async Task<WorksheetContextSubmittedTourManifestCapture> CaptureWorksheetContextSubmittedWindowAsync(
        string outputDir,
        WorksheetContextSubmittedTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
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
        return CreateWorksheetContextSubmittedCapture(
            context,
            state,
            "main-window-grid",
            fileName,
            "RenderTargetBitmap-main-window",
            commandRoute,
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary,
            []);
    }

    private async Task<WorksheetContextSubmittedTourManifestCapture> CaptureWorksheetContextSubmittedMenuAsync(
        string outputDir,
        CellAddress address,
        string state,
        string fileName,
        string evidenceSummary)
    {
        SetSelectionRange(new GridRange(address, address), address);
        EnsureCellVisible(address);
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();

        ContextMenu? menu = null;
        try
        {
            OnGridContextMenuRequested(address, GetKeyboardContextMenuGridPoint(address));
            await Task.Delay(350);
            menu = SheetGrid.ContextMenu
                ?? throw new InvalidOperationException($"Worksheet context-submitted tour could not open the {state} context menu.");
            menu.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();
            await CaptureElementAsync(menu, outputDir, fileName);
            var menuItems = ReadWorksheetContextSubmittedMenuItems(menu);

            return CreateWorksheetContextSubmittedCapture(
                EnsureWorksheetContextSubmittedCurrentContext(),
                state,
                "worksheet-context-menu",
                fileName,
                "RenderTargetBitmap-worksheet-context-menu",
                "OnGridContextMenuRequested -> WorksheetContextMenuPlanner.BuildCommands",
                menu.ActualWidth,
                menu.ActualHeight,
                evidenceSummary,
                menuItems);
        }
        finally
        {
            if (menu is not null)
            {
                menu.IsOpen = false;
                await Task.Delay(100);
            }
        }
    }

    private WorksheetContextSubmittedTourContext EnsureWorksheetContextSubmittedCurrentContext()
    {
        var sheet = _workbook.GetSheet(_currentSheetId)
            ?? throw new InvalidOperationException("Worksheet context-submitted tour lost the active worksheet.");
        return new WorksheetContextSubmittedTourContext(
            Sheet: sheet,
            SheetName: sheet.Name,
            ClearContentsRange: Range(sheet.Id, 5, 2, 5, 3),
            NoteCell: new CellAddress(sheet.Id, 9, 2),
            HyperlinkCell: new CellAddress(sheet.Id, 9, 3),
            ThreadedCommentCell: new CellAddress(sheet.Id, 11, 2),
            ProtectedCell: new CellAddress(sheet.Id, 13, 2),
            InsertRowCell: new CellAddress(sheet.Id, 16, 2),
            DeleteColumnCell: new CellAddress(sheet.Id, 1, 8),
            DeleteColumnIndex: 8);
    }

    private WorksheetContextSubmittedTourManifestCapture CreateWorksheetContextSubmittedCapture(
        WorksheetContextSubmittedTourContext context,
        string state,
        string surface,
        string fileName,
        string captureMethod,
        string commandRoute,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary,
        IReadOnlyList<WorksheetContextSubmittedTourMenuItem> menuItems) =>
        new(
            CaptureKey: $"interactive:worksheet-context-submitted:{state}",
            PairKey: $"interactive:worksheet-context-submitted:{state}",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CommandRoute: commandRoute,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SelectedRange: SheetGrid.SelectedRange?.ToString() ?? string.Empty,
            ActiveSheetName: _workbook.GetSheet(_currentSheetId)?.Name ?? "",
            WorkbookDirty: _workbookDirty,
            CanUndo: _session.CanUndo,
            CanRedo: _session.CanRedo,
            NoteExists: context.Sheet.Comments.ContainsKey(context.NoteCell),
            ThreadedCommentResolved: context.Sheet.ThreadedComments.TryGetValue(context.ThreadedCommentCell, out var threaded) && threaded.IsResolved,
            HyperlinkExists: context.Sheet.Hyperlinks.ContainsKey(context.HyperlinkCell),
            ProtectedCellValue: FormatWorksheetContextSubmittedCellValue(context.Sheet, context.ProtectedCell),
            ClearContentsValues: FormatWorksheetContextSubmittedRangeValues(context.Sheet, context.ClearContentsRange),
            ColumnProofValues: FormatWorksheetContextSubmittedRangeValues(context.Sheet, Range(context.Sheet.Id, 1, 7, 3, 9)),
            IsSheetProtected: context.Sheet.IsProtected,
            MenuItemCount: menuItems.Count,
            EnabledMenuHeaders: menuItems.Where(item => item.IsEnabled).Select(item => item.Header).ToArray(),
            DisabledMenuHeaders: menuItems.Where(item => !item.IsEnabled).Select(item => item.Header).ToArray(),
            EvidenceSummary: evidenceSummary);

    private static IReadOnlyList<WorksheetContextSubmittedTourMenuItem> ReadWorksheetContextSubmittedMenuItems(ContextMenu menu) =>
        menu.Items
            .OfType<MenuItem>()
            .Select(item => new WorksheetContextSubmittedTourMenuItem(
                item.Header?.ToString() ?? string.Empty,
                item.IsEnabled))
            .Where(item => !string.IsNullOrWhiteSpace(item.Header))
            .ToArray();

    private static string FormatWorksheetContextSubmittedRangeValues(Sheet sheet, GridRange range) =>
        string.Join(
            ";",
            range.AllCells().Select(address => $"{address.ToA1()}={FormatWorksheetContextSubmittedCellValue(sheet, address)}"));

    private static string FormatWorksheetContextSubmittedCellValue(Sheet sheet, CellAddress address) =>
        sheet.GetCell(address)?.Value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue date => date.ToDateTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            BlankValue => "",
            null => "",
            var value => value.ToString() ?? ""
        };

    private static WorksheetContextSubmittedTourManifestWorkflow CreateWorksheetContextSubmittedWorkflow(
        string name,
        string commandRoute,
        params string[] captureStates) =>
        new(
            Name: name,
            CatalogRows: ["UI-CAT-CONTEXT-001", "UI-CAT-CONTEXT-001C"],
            ActualStatus: "captured",
            CommandRoute: commandRoute,
            LimitationNote: "Captured through deterministic in-process worksheet context-menu command paths and RenderTargetBitmap; no global mouse, Shift+F10/Menu-key, access-key, UI Automation Invoke, native dialog, or screen-wide input is synthesized.",
            CaptureKeys: captureStates.Select(state => $"interactive:worksheet-context-submitted:{state}").ToArray());

    private static void DeleteWorksheetContextSubmittedTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_worksheet_context_submitted_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, WorksheetContextSubmittedTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);

        var savedWorkbookPath = Path.Combine(outputDir, WorksheetContextSubmittedTourSavedWorkbookFileName);
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);
    }

    private static void ValidateWorksheetContextSubmittedTourEvidence(
        string outputDir,
        IReadOnlyList<WorksheetContextSubmittedTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Worksheet context-submitted tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        if (!File.Exists(savedWorkbookPath))
            throw new InvalidOperationException("Worksheet context-submitted tour did not create the saved native workbook evidence.");
    }

    private static async Task WriteWorksheetContextSubmittedTourManifestAsync(
        string outputDir,
        string savedWorkbookPath,
        WorksheetContextSubmittedTourContext context,
        IReadOnlyList<WorksheetContextSubmittedTourManifestCapture> captures,
        IReadOnlyList<WorksheetContextSubmittedTourManifestWorkflow> workflows,
        IReadOnlyList<WorksheetContextSubmittedTourCommandOutcome> commandOutcomes)
    {
        var plannedCaptureKeys = new[]
        {
            "seeded-before-submissions",
            "note-menu-available",
            "delete-note-result",
            "resolve-threaded-comment-result",
            "hyperlink-menu-available",
            "remove-hyperlink-result",
            "clear-contents-result",
            "insert-row-above-result",
            "delete-column-result",
            "protected-clear-contents-blocked",
            "undo-restored-delete-column",
            "redo-reapplied-delete-column",
            "save-persistence-path",
            "reopened-persistence-result"
        };

        var manifest = new WorksheetContextSubmittedTourManifest(
            Tool: "FREEX_WORKSHEET_CONTEXT_SUBMITTED_TOUR",
            EvidenceFamily: "worksheet-context-menu-submitted-workflows",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "context-menu:worksheet-submitted-mutation-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_worksheet_context_submitted_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-CONTEXT-001C",
            CatalogRows: ["UI-CAT-CONTEXT-001", "UI-CAT-CONTEXT-001C"],
            SheetName: context.SheetName,
            ClearContentsRange: context.ClearContentsRange.ToString(),
            NoteCell: context.NoteCell.ToA1(),
            HyperlinkCell: context.HyperlinkCell.ToA1(),
            ThreadedCommentCell: context.ThreadedCommentCell.ToA1(),
            ProtectedCell: context.ProtectedCell.ToA1(),
            InsertRowCell: context.InsertRowCell.ToA1(),
            DeleteColumn: CellAddress.NumberToColumnName(context.DeleteColumnIndex),
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookExists: File.Exists(savedWorkbookPath),
            CaptureStatus: "complete-with-foreground-input-limitations",
            CaptureMode: "RenderTargetBitmap-in-process-context-command-routes",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures after real host context-command/save/open execution; no global mouse, keyboard, keytip, native dialog, UI Automation Invoke, or screen capture input is used."
                    : "Window/menu captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            PlannedWorkflowCount: workflows.Count,
            ActualWorkflowCount: workflows.Count,
            PlannedCaptureCount: plannedCaptureKeys.Length,
            ActualCaptureCount: captures.Count,
            PlannedCaptureKeys: plannedCaptureKeys,
            ActualCaptureKeys: captures.Select(capture => capture.State).ToArray(),
            CommandRoutesUsed: captures.Select(capture => capture.CommandRoute).Distinct().ToArray(),
            CommandOutcomes: commandOutcomes,
            Captures: captures,
            Workflows: workflows,
            CoveredStates:
            [
                "Seeded worksheet before submitted context-menu workflows",
                "Note-backed menu availability and Delete Note mutation",
                "Threaded comment Resolve Comment mutation",
                "Hyperlink-backed menu availability and Remove Hyperlink mutation",
                "Clear Contents range mutation",
                "Insert Row Above mutation",
                "Delete Column(s) mutation",
                "Protected locked-cell Clear Contents rejection",
                "Undo and redo of the submitted Delete Column(s) mutation",
                "Native FreeX save/reopen proof for submitted worksheet context state"
            ],
            Limitations:
            [
                "This bounded tour drives FreeX worksheet context-menu command routes in process and captures WPF menu/window state with RenderTargetBitmap.",
                "It does not synthesize physical right-click, Shift+F10/Menu-key traversal, access-key traversal, UI Automation Invoke, native dialogs, or screen-wide input.",
                "Dialog-only context commands such as Insert..., Delete..., Hyperlink..., New Note, New Comment, Format Cells, row height, and column width remain separate foreground/dialog-submission gaps.",
                "The protected locked-cell command state is captured as command-layer rejection; the planner still does not disable protected locked-cell menu items.",
                "Save/reopen persistence uses the native FreeX `.fxl` adapter; Microsoft Excel paired screenshots are not produced by this tool."
            ]);

        var path = Path.Combine(outputDir, WorksheetContextSubmittedTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.WorksheetContextSubmittedTourManifest);
    }

    private sealed record WorksheetContextSubmittedTourContext(
        Sheet Sheet,
        string SheetName,
        GridRange ClearContentsRange,
        CellAddress NoteCell,
        CellAddress HyperlinkCell,
        CellAddress ThreadedCommentCell,
        CellAddress ProtectedCell,
        CellAddress InsertRowCell,
        CellAddress DeleteColumnCell,
        uint DeleteColumnIndex);

    private sealed record WorksheetContextSubmittedTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogRows,
        string SheetName,
        string ClearContentsRange,
        string NoteCell,
        string HyperlinkCell,
        string ThreadedCommentCell,
        string ProtectedCell,
        string InsertRowCell,
        string DeleteColumn,
        string SavedWorkbookPath,
        bool SavedWorkbookExists,
        string CaptureStatus,
        string CaptureMode,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        int PlannedWorkflowCount,
        int ActualWorkflowCount,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<string> PlannedCaptureKeys,
        IReadOnlyList<string> ActualCaptureKeys,
        IReadOnlyList<string> CommandRoutesUsed,
        IReadOnlyList<WorksheetContextSubmittedTourCommandOutcome> CommandOutcomes,
        IReadOnlyList<WorksheetContextSubmittedTourManifestCapture> Captures,
        IReadOnlyList<WorksheetContextSubmittedTourManifestWorkflow> Workflows,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record WorksheetContextSubmittedTourManifestCapture(
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
        string ActiveSheetName,
        bool WorkbookDirty,
        bool CanUndo,
        bool CanRedo,
        bool NoteExists,
        bool ThreadedCommentResolved,
        bool HyperlinkExists,
        string ProtectedCellValue,
        string ClearContentsValues,
        string ColumnProofValues,
        bool IsSheetProtected,
        int MenuItemCount,
        IReadOnlyList<string> EnabledMenuHeaders,
        IReadOnlyList<string> DisabledMenuHeaders,
        string EvidenceSummary);

    private sealed record WorksheetContextSubmittedTourManifestWorkflow(
        string Name,
        IReadOnlyList<string> CatalogRows,
        string ActualStatus,
        string CommandRoute,
        string LimitationNote,
        IReadOnlyList<string> CaptureKeys);

    private sealed record WorksheetContextSubmittedTourCommandOutcome(
        string Action,
        string CommandRoute,
        bool Success,
        string ErrorMessage,
        bool CanUndo,
        bool CanRedo);

    private sealed record WorksheetContextSubmittedTourMenuItem(string Header, bool IsEnabled);
}
