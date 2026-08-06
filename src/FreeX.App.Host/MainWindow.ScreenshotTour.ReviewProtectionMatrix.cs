using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const string ReviewProtectionMatrixTourSavedWorkbookFileName = "freex_review_protection_matrix_saved.fxl";

    private async Task CaptureReviewProtectionMatrixTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteReviewProtectionMatrixTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 760;
        await Task.Delay(700);

        var context = EnsureReviewProtectionMatrixTourContext();
        var captures = new List<ReviewProtectionMatrixTourManifestCapture>();
        var commandOutcomes = new List<ReviewProtectionMatrixTourCommandOutcome>();
        Window? openDialog = null;

        try
        {
            SelectReviewProtectionMatrixRibbonTabForTour();

            openDialog = CreateReviewProtectionMatrixProtectSheetDialog(context);
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewProtectionMatrixDialogAsync(
                openDialog,
                outputDir,
                "protect-sheet-selected-permissions",
                "Protect Sheet dialog",
                "Review > Protect Sheet",
                "freex_review_protection_matrix_protect_sheet_permissions",
                "Protect Sheet dialog shows the optional password field and a selected permission matrix for unlocked cells, sort, AutoFilter, and row formatting."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            ExecuteReviewProtectionMatrixCommand(
                ProtectionWorkflowSession.CreateSheetCommandPlan(
                    context.Sheet,
                    ProtectSheetOptions.FromCorePermissions(
                        context.SelectedPermissions,
                        context.Password,
                        context.Password)).Command!,
                "Protect Sheet",
                commandOutcomes);
            RefreshSheetProtectionUi();
            SelectReviewProtectionMatrixRibbonTabForTour();
            SetSelectionRange(new GridRange(context.LockedCell, context.LockedCell), context.LockedCell);
            captures.Add(await CaptureReviewProtectionMatrixWindowStateAsync(
                outputDir,
                "protected-sheet-disabled-state",
                "Review tab",
                "Review > Protect group",
                "freex_review_protection_matrix_protected_disabled_state",
                "Protected Review tab state shows Protect Sheet retitled to Unprotect Sheet while Allow Users to Edit Ranges remains available."));

            var lockedOutcome = ExecuteReviewProtectionMatrixCommand(
                EditCellsCommand.ForValue(context.Sheet.Id, context.LockedCell, new TextValue("blocked edit")),
                "Edit locked protected cell",
                commandOutcomes,
                refreshOnSuccess: false);
            SetSelectionRange(new GridRange(context.LockedCell, context.LockedCell), context.LockedCell);
            captures.Add(await CaptureReviewProtectionMatrixWindowStateAsync(
                outputDir,
                "locked-cell-edit-blocked",
                "Worksheet grid",
                "Grid edit command guard",
                "freex_review_protection_matrix_locked_cell_blocked",
                $"Locked cell edit attempt returned Success={lockedOutcome.Success} with message '{lockedOutcome.ErrorMessage ?? ""}'."));

            var unlockedOutcome = ExecuteReviewProtectionMatrixCommand(
                EditCellsCommand.ForValue(context.Sheet.Id, context.UnlockedCell, new TextValue("unlocked edit allowed")),
                "Edit unlocked protected cell",
                commandOutcomes);
            SetSelectionRange(new GridRange(context.UnlockedCell, context.UnlockedCell), context.UnlockedCell);
            captures.Add(await CaptureReviewProtectionMatrixWindowStateAsync(
                outputDir,
                "unlocked-cell-edit-allowed",
                "Worksheet grid",
                "Grid edit command guard",
                "freex_review_protection_matrix_unlocked_cell_allowed",
                $"Unlocked cell edit attempt returned Success={unlockedOutcome.Success} through EditCellsCommand on the protected sheet."));

            var allowRangeOutcome = ExecuteReviewProtectionMatrixCommand(
                EditCellsCommand.ForValue(context.Sheet.Id, context.AllowEditCell, new TextValue("allow range edit allowed")),
                "Edit allowed protected range",
                commandOutcomes);
            SetSelectionRange(context.AllowEditRange, context.AllowEditCell);
            captures.Add(await CaptureReviewProtectionMatrixWindowStateAsync(
                outputDir,
                "allow-edit-range-edit-allowed",
                "Worksheet grid",
                "Grid edit command guard",
                "freex_review_protection_matrix_allow_range_allowed",
                $"Allowed edit range attempt returned Success={allowRangeOutcome.Success} while the cell style remained locked."));

            openDialog = new PasswordProtectionDialog(
                UiText.Get("Protection_UnprotectSheetTitle"),
                UiText.Get("Protection_Password2")) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewProtectionMatrixDialogAsync(
                openDialog,
                outputDir,
                "unprotect-password-dialog",
                "Unprotect Sheet dialog",
                "Review > Unprotect Sheet",
                "freex_review_protection_matrix_unprotect_password_dialog",
                "Unprotect Sheet dialog captures the owned password prompt with OK/Cancel surface before wrong-password and cancel limitations are recorded."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            ExecuteReviewProtectionMatrixCommand(
                new UnprotectSheetCommand(context.Sheet.Id, "wrong-password"),
                "Unprotect Sheet wrong password",
                commandOutcomes,
                refreshOnSuccess: false);
            ExecuteReviewProtectionMatrixCommand(
                ProtectionWorkflowSession.CreateSheetCommandPlan(
                    context.Sheet,
                    ProtectSheetOptions.FromCorePermissions(
                        SheetProtectionOptions.DefaultEnabledPermissions,
                        context.Password,
                        context.Password)).Command!,
                "Unprotect Sheet",
                commandOutcomes);
            RefreshSheetProtectionUi();
            SelectReviewProtectionMatrixRibbonTabForTour();
            captures.Add(await CaptureReviewProtectionMatrixWindowStateAsync(
                outputDir,
                "after-unprotect-sheet",
                "Review tab",
                "Review > Protect group",
                "freex_review_protection_matrix_after_unprotect",
                "After successful command-path unprotect, Protect Sheet returns to its protect label and Allow Users to Edit Ranges is enabled."));

            ExecuteReviewProtectionMatrixCommand(
                ProtectionWorkflowSession.CreateSheetCommandPlan(
                    context.Sheet,
                    ProtectSheetOptions.FromCorePermissions(
                        context.SelectedPermissions,
                        context.Password,
                        context.Password)).Command!,
                "Protect Sheet for persistence",
                commandOutcomes);
            ExecuteReviewProtectionMatrixCommand(
                ProtectionWorkflowSession.CreateWorkbookCommandPlan(_workbook, context.Password).Command!,
                "Protect Workbook",
                commandOutcomes);
            RefreshSheetProtectionUi();
            RefreshWorkbookProtectionUi();
            RefreshSheetTabs();
            SelectReviewProtectionMatrixRibbonTabForTour();
            captures.Add(await CaptureReviewProtectionMatrixWindowStateAsync(
                outputDir,
                "protected-workbook-structure-state",
                "Review tab",
                "Review > Protect Workbook",
                "freex_review_protection_matrix_protect_workbook_structure",
                "Workbook structure protection is enabled through ProtectionWorkflowSession and the Review Protect Workbook button shows the unprotect state."));

            var savedWorkbookPath = Path.Combine(outputDir, ReviewProtectionMatrixTourSavedWorkbookFileName);
            await SaveReviewProtectionMatrixTourWorkbookAsync(savedWorkbookPath);
            await OpenFileAsync(savedWorkbookPath);
            context = ResolveReviewProtectionMatrixTourContextAfterReopen(context, savedWorkbookPath);
            SelectReviewProtectionMatrixRibbonTabForTour();
            SetSelectionRange(new GridRange(context.LockedCell, context.LockedCell), context.LockedCell);
            captures.Add(await CaptureReviewProtectionMatrixWindowStateAsync(
                outputDir,
                "reopened-protection-persistence",
                "Review tab",
                "Host open path",
                "freex_review_protection_matrix_reopened_persistence",
                "Saved native FreeX workbook is reopened through OpenFileAsync; sheet protection, selected permissions, allow edit range, and workbook structure protection remain present."));

            ValidateReviewProtectionMatrixTourEvidence(outputDir, captures);
            await WriteReviewProtectionMatrixTourManifestAsync(outputDir, context, captures, commandOutcomes);
        }
        catch
        {
            DeleteReviewProtectionMatrixTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openDialog is { IsVisible: true })
                CloseDataToolsTourDialog(openDialog);
        }
    }

    private ReviewProtectionMatrixTourContext EnsureReviewProtectionMatrixTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Review protection matrix tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        sheet.Comments.Clear();
        sheet.ThreadedComments.Clear();
        sheet.Pictures.Clear();
        sheet.Charts.Clear();
        sheet.DrawingShapes.Clear();
        sheet.TextBoxes.Clear();
        sheet.ReplaceMergedRegions([]);
        sheet.AllowEditRanges.Clear();
        sheet.IsProtected = false;
        sheet.ProtectionPassword = null;
        sheet.ProtectionPermissions.Clear();
        _workbook.IsStructureProtected = false;
        _workbook.StructureProtectionPassword = null;

        for (uint row = 1; row <= 9; row++)
        {
            for (uint col = 1; col <= 5; col++)
            {
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
                sheet.ClearStyleOnly(row, col);
            }
        }

        sheet.ColumnWidths[1] = 24;
        sheet.ColumnWidths[2] = 24;
        sheet.ColumnWidths[3] = 24;
        sheet.ColumnWidths[4] = 24;

        SetTourCell(sheet, 1, 1, new TextValue("Protection matrix"));
        SetTourCell(sheet, 2, 1, new TextValue("Locked cell"));
        SetTourCell(sheet, 2, 2, new TextValue("Initial locked value"));
        SetTourCell(sheet, 3, 1, new TextValue("Unlocked cell"));
        SetTourCell(sheet, 3, 2, new TextValue("Initial unlocked value"));
        SetTourCell(sheet, 4, 1, new TextValue("Allowed locked range"));
        SetTourCell(sheet, 4, 2, new TextValue("B4:C4"));
        SetTourCell(sheet, 6, 1, new TextValue("Permissions"));
        SetTourCell(sheet, 6, 2, new TextValue("Unlocked, sort, filter, format rows"));

        var lockedCell = new CellAddress(sheet.Id, 2, 2);
        var unlockedCell = new CellAddress(sheet.Id, 3, 2);
        var allowEditCell = new CellAddress(sheet.Id, 4, 2);
        var allowEditRange = Range(sheet.Id, 4, 2, 4, 3);

        var unlockedStyle = _workbook.RegisterStyle(new CellStyle
        {
            Locked = false,
            FillColor = new CellColor(226, 239, 218)
        });
        sheet.GetCell(unlockedCell)!.StyleId = unlockedStyle;
        var lockedHighlightStyle = _workbook.RegisterStyle(new CellStyle
        {
            Locked = true,
            FillColor = new CellColor(252, 228, 214)
        });
        sheet.GetCell(lockedCell)!.StyleId = lockedHighlightStyle;
        sheet.GetCell(allowEditCell)!.StyleId = lockedHighlightStyle;

        if (!TryExecuteCommand(
                new AllowEditRangeCommand(sheet.Id, allowEditRange),
                "Allow Users to Edit Ranges",
                out var addRangeOutcome))
            throw new InvalidOperationException(addRangeOutcome.ErrorMessage ?? "Review protection matrix tour could not add an allowed edit range.");

        var selectedPermissions = new[]
        {
            SheetProtectionPermission.SelectUnlockedCells,
            SheetProtectionPermission.Sort,
            SheetProtectionPermission.UseAutoFilter,
            SheetProtectionPermission.FormatRows
        };
        var selectedLabels = selectedPermissions
            .Select(LocalizeReviewProtectionPermission)
            .ToArray();

        SetSelectionRange(new GridRange(lockedCell, lockedCell), lockedCell);
        EnsureCellVisible(lockedCell);
        RefreshSheetProtectionUi();
        RefreshWorkbookProtectionUi();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateLayout();

        return new ReviewProtectionMatrixTourContext(
            SheetName: sheet.Name,
            Sheet: sheet,
            LockedCell: lockedCell,
            UnlockedCell: unlockedCell,
            AllowEditCell: allowEditCell,
            AllowEditRange: allowEditRange,
            Password: "matrix-secret",
            SelectedPermissions: selectedPermissions,
            SelectedPermissionLabels: selectedLabels,
            SavedWorkbookOutputFileName: ReviewProtectionMatrixTourSavedWorkbookFileName,
            SavedWorkbookBytes: 0,
            SavedWorkbookRetained: false,
            ReopenedSheetProtected: false,
            ReopenedWorkbookStructureProtected: false,
            ReopenedAllowEditRangeCount: 0);
    }

    private void SelectReviewProtectionMatrixRibbonTabForTour()
    {
        HideStartScreen();
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Review"));
        RefreshReviewCommentNoteCommandStates();
        RefreshSheetProtectionUi();
        RefreshWorkbookProtectionUi();
        RefreshToolbar();
        UpdateLayout();
    }

    private PasswordProtectionDialog CreateReviewProtectionMatrixProtectSheetDialog(ReviewProtectionMatrixTourContext context)
    {
        var dialog = new PasswordProtectionDialog(
            UiText.Get("MainWindowMessage_ProtectSheetTitle"),
            UiText.Get("MainWindowMessage_OptionalPasswordLabel")) { Owner = this };
        return dialog;
    }

    private static void ApplyReviewProtectionMatrixDialogState(
        PasswordProtectionDialog dialog,
        ReviewProtectionMatrixTourContext context)
    {
        if (FindDescendantByAutomationId<PasswordBox>(dialog, "ProtectionPasswordBox") is { } passwordBox)
            passwordBox.Password = context.Password;

        var selected = context.SelectedPermissionLabels.ToHashSet(StringComparer.Ordinal);
        foreach (var checkBox in EnumerateReviewProtectionMatrixDescendants<CheckBox>(dialog))
        {
            var label = checkBox.Content?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(label))
                checkBox.IsChecked = selected.Contains(label);
        }
    }

    private async Task<ReviewProtectionMatrixTourManifestCapture> CaptureReviewProtectionMatrixWindowStateAsync(
        string outputDir,
        string state,
        string surface,
        string entryPath,
        string fileName,
        string evidenceSummary)
    {
        UpdateViewport();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateReviewProtectionMatrixCapture(
            state,
            surface,
            entryPath,
            fileName,
            "RenderTargetBitmap-main-window",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary);
    }

    private async Task<ReviewProtectionMatrixTourManifestCapture> CaptureReviewProtectionMatrixDialogAsync(
        Window dialog,
        string outputDir,
        string state,
        string surface,
        string entryPath,
        string fileName,
        string evidenceSummary)
    {
        if (dialog is PasswordProtectionDialog passwordDialog &&
            state == "protect-sheet-selected-permissions")
        {
            ApplyReviewProtectionMatrixDialogState(passwordDialog, EnsureReviewProtectionMatrixTourContextFromSheet());
        }

        await WaitForDataToolsDialogRenderAsync(dialog);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        return CreateReviewProtectionMatrixCapture(
            state,
            surface,
            entryPath,
            fileName,
            "RenderTargetBitmap-owned-dialog",
            dialog.ActualWidth,
            dialog.ActualHeight,
            evidenceSummary);
    }

    private ReviewProtectionMatrixTourContext EnsureReviewProtectionMatrixTourContextFromSheet()
    {
        var sheet = _workbook.GetSheet(_currentSheetId)
            ?? throw new InvalidOperationException("Review protection matrix tour could not resolve the active worksheet.");
        return new ReviewProtectionMatrixTourContext(
            SheetName: sheet.Name,
            Sheet: sheet,
            LockedCell: new CellAddress(sheet.Id, 2, 2),
            UnlockedCell: new CellAddress(sheet.Id, 3, 2),
            AllowEditCell: new CellAddress(sheet.Id, 4, 2),
            AllowEditRange: Range(sheet.Id, 4, 2, 4, 3),
            Password: "matrix-secret",
            SelectedPermissions:
            [
                SheetProtectionPermission.SelectUnlockedCells,
                SheetProtectionPermission.Sort,
                SheetProtectionPermission.UseAutoFilter,
                SheetProtectionPermission.FormatRows
            ],
            SelectedPermissionLabels:
            [
                LocalizeReviewProtectionPermission(SheetProtectionPermission.SelectUnlockedCells),
                LocalizeReviewProtectionPermission(SheetProtectionPermission.Sort),
                LocalizeReviewProtectionPermission(SheetProtectionPermission.UseAutoFilter),
                LocalizeReviewProtectionPermission(SheetProtectionPermission.FormatRows)
            ],
            SavedWorkbookOutputFileName: ReviewProtectionMatrixTourSavedWorkbookFileName,
            SavedWorkbookBytes: 0,
            SavedWorkbookRetained: false,
            ReopenedSheetProtected: false,
            ReopenedWorkbookStructureProtected: false,
            ReopenedAllowEditRangeCount: sheet.AllowEditRanges.Count);
    }

    private CommandOutcome ExecuteReviewProtectionMatrixCommand(
        IWorkbookCommand command,
        string title,
        List<ReviewProtectionMatrixTourCommandOutcome> commandOutcomes,
        bool refreshOnSuccess = true)
    {
        var succeeded = TryExecuteCommand(command, title, out var outcome);
        commandOutcomes.Add(new ReviewProtectionMatrixTourCommandOutcome(
            title,
            command.Label,
            outcome.Success,
            outcome.ErrorMessage));

        if (succeeded && refreshOnSuccess)
        {
            UpdateViewport();
            RefreshToolbar();
            RefreshStatusBar();
        }

        return outcome;
    }

    private async Task SaveReviewProtectionMatrixTourWorkbookAsync(string savedWorkbookPath)
    {
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);

        var adapter = FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ".fxl", out _)
            ?? throw new InvalidOperationException("Review protection matrix tour could not find the native FreeX save adapter.");
        var saved = await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter));
        if (!saved)
            throw new InvalidOperationException("Review protection matrix tour could not save the native FreeX workbook.");
    }

    private ReviewProtectionMatrixTourContext ResolveReviewProtectionMatrixTourContextAfterReopen(
        ReviewProtectionMatrixTourContext previous,
        string savedWorkbookPath)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Review protection matrix tour could not resolve the reopened worksheet.");
        _currentSheetId = sheet.Id;
        var lockedCell = new CellAddress(sheet.Id, 2, 2);
        var unlockedCell = new CellAddress(sheet.Id, 3, 2);
        var allowEditCell = new CellAddress(sheet.Id, 4, 2);
        var allowEditRange = Range(sheet.Id, 4, 2, 4, 3);
        return previous with
        {
            SheetName = sheet.Name,
            Sheet = sheet,
            LockedCell = lockedCell,
            UnlockedCell = unlockedCell,
            AllowEditCell = allowEditCell,
            AllowEditRange = allowEditRange,
            SavedWorkbookBytes = File.Exists(savedWorkbookPath) ? new FileInfo(savedWorkbookPath).Length : 0,
            SavedWorkbookRetained = File.Exists(savedWorkbookPath),
            ReopenedSheetProtected = sheet.IsProtected,
            ReopenedWorkbookStructureProtected = _workbook.IsStructureProtected,
            ReopenedAllowEditRangeCount = sheet.AllowEditRanges.Count
        };
    }

    private ReviewProtectionMatrixTourManifestCapture CreateReviewProtectionMatrixCapture(
        string state,
        string surface,
        string entryPath,
        string fileName,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidenceSummary)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        var focusedAutomationId = Keyboard.FocusedElement is DependencyObject focusedElement
            ? AutomationProperties.GetAutomationId(focusedElement)
            : null;
        return new ReviewProtectionMatrixTourManifestCapture(
            CaptureKey: $"review-protection-matrix:{state}",
            PairKey: $"interactive:review-protection-matrix:{state}",
            ScenarioId: "review-protection-matrix:visual-evidence",
            State: state,
            Surface: surface,
            EntryPath: entryPath,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            SelectedRange: SheetGrid.SelectedRange?.ToString() ?? string.Empty,
            FocusedElementAutomationId: focusedAutomationId,
            IsSheetProtected: sheet?.IsProtected ?? false,
            IsWorkbookStructureProtected: _workbook.IsStructureProtected,
            AllowEditRangesEnabled: FindRenderedRibbonControl("Allow Users to Edit Ranges")?.IsEnabled,
            ProtectSheetContent: GetRenderedRibbonCommandLabel("Protect Sheet"),
            ProtectWorkbookContent: GetRenderedRibbonCommandLabel("Protect Workbook"),
            SheetProtectionPermissions: sheet?.ProtectionPermissions.Select(permission => permission.ToString()).ToArray() ?? [],
            AllowEditRangeCount: sheet?.AllowEditRanges.Count ?? 0,
            EvidenceSummary: evidenceSummary);
    }

    private static IEnumerable<T> EnumerateReviewProtectionMatrixDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
            yield return match;

        if (root is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < childCount; index++)
            {
                foreach (var descendant in EnumerateReviewProtectionMatrixDescendants<T>(VisualTreeHelper.GetChild(root, index)))
                    yield return descendant;
            }
        }

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var descendant in EnumerateReviewProtectionMatrixDescendants<T>(logicalChild))
                yield return descendant;
        }
    }

    private static void DeleteReviewProtectionMatrixTourEvidence(string outputDir)
    {
        foreach (var file in ReviewProtectionMatrixTourExpectedFileNames().Append(ReviewProtectionMatrixTourManifestFileName).Append(ReviewProtectionMatrixTourSavedWorkbookFileName))
        {
            var path = Path.Combine(outputDir, file);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateReviewProtectionMatrixTourEvidence(
        string outputDir,
        IReadOnlyList<ReviewProtectionMatrixTourManifestCapture> captures)
    {
        var missing = ReviewProtectionMatrixTourExpectedFileNames()
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Review protection matrix tour did not capture expected evidence: {string.Join(", ", missing)}.");

        var blank = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !IsNonBlankPng(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (blank.Length > 0)
            throw new InvalidOperationException($"Review protection matrix tour created blank capture(s): {string.Join(", ", blank)}.");
    }

    private static IReadOnlyList<string> ReviewProtectionMatrixTourExpectedFileNames() =>
    [
        "freex_review_protection_matrix_protect_sheet_permissions.png",
        "freex_review_protection_matrix_protected_disabled_state.png",
        "freex_review_protection_matrix_locked_cell_blocked.png",
        "freex_review_protection_matrix_unlocked_cell_allowed.png",
        "freex_review_protection_matrix_allow_range_allowed.png",
        "freex_review_protection_matrix_unprotect_password_dialog.png",
        "freex_review_protection_matrix_after_unprotect.png",
        "freex_review_protection_matrix_protect_workbook_structure.png",
        "freex_review_protection_matrix_reopened_persistence.png"
    ];

    private static async Task WriteReviewProtectionMatrixTourManifestAsync(
        string outputDir,
        ReviewProtectionMatrixTourContext context,
        IReadOnlyList<ReviewProtectionMatrixTourManifestCapture> captures,
        IReadOnlyList<ReviewProtectionMatrixTourCommandOutcome> commandOutcomes)
    {
        var manifest = new ReviewProtectionMatrixTourManifest(
            Tool: "FREEX_REVIEW_PROTECTION_MATRIX_TOUR",
            EvidenceFamily: "review-protection-matrix",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "review-protection-matrix:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_review_protection_matrix_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-REVIEW-002",
                "UI-CMD-REVIEW-004",
                "UI-CMD-REVIEW-006"
            ],
            EntryPaths:
            [
                "Review > Protect Sheet",
                "Review > Allow Users to Edit Ranges",
                "Review > Protect Workbook",
                "Grid edit command path",
                "SaveWorkbookToTargetAsync(.fxl) > OpenFileAsync(.fxl)"
            ],
            SheetName: context.SheetName,
            LockedCell: context.LockedCell.ToA1(),
            UnlockedCell: context.UnlockedCell.ToA1(),
            AllowEditRange: context.AllowEditRange.ToString(),
            SelectedSheetPermissions: context.SelectedPermissionLabels,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            SavedWorkbookRetained: context.SavedWorkbookRetained,
            ReopenedSheetProtected: context.ReopenedSheetProtected,
            ReopenedWorkbookStructureProtected: context.ReopenedWorkbookStructureProtected,
            ReopenedAllowEditRangeCount: context.ReopenedAllowEditRangeCount,
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: ReviewProtectionMatrixTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            PlannedCaptures: ReviewProtectionMatrixTourExpectedFileNames()
                .Select(fileName => new ReviewProtectionMatrixTourPlannedCapture(fileName, captures.Any(capture => capture.OutputFileName == fileName) ? "captured" : "missing"))
                .ToArray(),
            Pairing: new ReviewProtectionMatrixTourManifestPairing(
                "interactive:review-protection-matrix:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process WPF RenderTargetBitmap capture; no global mouse, keyboard, keytip, physical edit, range-picker, or native screen capture input was used."
                    : "Abort before file write unless the expected FreeX main window or owned protection dialog owns foreground focus for each capture."),
            Captures: captures,
            CommandOutcomes: commandOutcomes,
            CoveredStates:
            [
                "Protect Sheet dialog with selected permissions.",
                "Protected sheet Review command state keeps Allow Users to Edit Ranges available.",
                "Locked protected cell edit blocked through EditCellsCommand.",
                "Unlocked protected cell edit allowed through EditCellsCommand.",
                "Locked cell inside Allow Edit Range allowed through EditCellsCommand.",
                "Unprotect password dialog surface, wrong-password command outcome, and cancel limitation note.",
                "Successful unprotect command state.",
                "Workbook structure protection command state.",
                "Native FreeX save/reopen persistence for sheet/workbook protection and allowed ranges."
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF surfaces with RenderTargetBitmap; it is not foreground CopyFromScreen proof.",
                "Physical mouse, keytip, access-key, inline editor typing, and range-picker paths are not synthesized because those would require foreground-global input.",
                "Wrong-password evidence is recorded as an UnprotectSheetCommand failure outcome; the transient warning MessageBox is not captured.",
                "Cancel behavior is represented by the owned Unprotect Sheet dialog's Cancel surface and by not submitting it; no physical Escape or Cancel click is performed.",
                "Permissions button behavior in Allow Edit Ranges remains disabled/guarded in the existing dialog evidence; per-range password permissions are not implemented.",
                "Persistence is proven for the native FreeX .fxl adapter through host save/open services; XLSX round-trip parity remains separate.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, ReviewProtectionMatrixTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.ReviewProtectionMatrixTourManifest);
    }

    private static string LocalizeReviewProtectionPermission(SheetProtectionPermission permission) =>
        UiText.Get(SheetProtectionOptions.All.Single(option => option.Permission == permission).LabelKey);

    private sealed record ReviewProtectionMatrixTourContext(
        string SheetName,
        Sheet Sheet,
        CellAddress LockedCell,
        CellAddress UnlockedCell,
        CellAddress AllowEditCell,
        GridRange AllowEditRange,
        string Password,
        IReadOnlyList<SheetProtectionPermission> SelectedPermissions,
        IReadOnlyList<string> SelectedPermissionLabels,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        bool SavedWorkbookRetained,
        bool ReopenedSheetProtected,
        bool ReopenedWorkbookStructureProtected,
        int ReopenedAllowEditRangeCount);

    private sealed record ReviewProtectionMatrixTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        IReadOnlyList<string> EntryPaths,
        string SheetName,
        string LockedCell,
        string UnlockedCell,
        string AllowEditRange,
        IReadOnlyList<string> SelectedSheetPermissions,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        bool SavedWorkbookRetained,
        bool ReopenedSheetProtected,
        bool ReopenedWorkbookStructureProtected,
        int ReopenedAllowEditRangeCount,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<ReviewProtectionMatrixTourPlannedCapture> PlannedCaptures,
        ReviewProtectionMatrixTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<ReviewProtectionMatrixTourManifestCapture> Captures,
        IReadOnlyList<ReviewProtectionMatrixTourCommandOutcome> CommandOutcomes,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record ReviewProtectionMatrixTourPlannedCapture(
        string OutputFileName,
        string Status);

    private sealed record ReviewProtectionMatrixTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record ReviewProtectionMatrixTourCommandOutcome(
        string Title,
        string CommandLabel,
        bool Success,
        string? ErrorMessage);

    private sealed record ReviewProtectionMatrixTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string EntryPath,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        string? FocusedElementAutomationId,
        bool IsSheetProtected,
        bool IsWorkbookStructureProtected,
        bool? AllowEditRangesEnabled,
        string? ProtectSheetContent,
        string? ProtectWorkbookContent,
        IReadOnlyList<string> SheetProtectionPermissions,
        int AllowEditRangeCount,
        string EvidenceSummary);
}
