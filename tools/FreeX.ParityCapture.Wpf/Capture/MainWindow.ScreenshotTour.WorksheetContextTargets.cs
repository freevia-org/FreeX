using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FreeX.App.UI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const string WorksheetContextTargetsTourManifestFileName = "worksheet_context_targets_tour_manifest.json";

    private async Task CaptureWorksheetContextTargetsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteWorksheetContextTargetsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 820;
        await Task.Delay(700);

        var context = EnsureWorksheetContextTargetsTourContext();
        var captures = new List<WorksheetContextTargetsTourManifestCapture>();

        try
        {
            foreach (var target in WorksheetContextTargetsTourTargets())
                captures.Add(await CaptureWorksheetContextTargetMenuAsync(outputDir, context, target));

            ValidateWorksheetContextTargetsTourEvidence(outputDir, captures);
            await WriteWorksheetContextTargetsTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteWorksheetContextTargetsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (SheetGrid.ContextMenu is { IsOpen: true } menu)
                menu.IsOpen = false;
        }
    }

    private WorksheetContextTargetsTourContext EnsureWorksheetContextTargetsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Worksheet context-targets tour requires an active worksheet.");

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
        sheet.PivotTables.RemoveAll(pivot => string.Equals(pivot.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase));
        sheet.Charts.Clear();
        sheet.Sparklines.Clear();
        sheet.Pictures.Clear();
        sheet.DrawingShapes.Clear();
        sheet.TextBoxes.Clear();
        sheet.ReplaceMergedRegions([]);

        for (uint row = 1; row <= 12; row++)
        {
            for (uint col = 1; col <= 9; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        sheet.ColumnWidths[1] = 16;
        sheet.ColumnWidths[2] = 18;
        sheet.ColumnWidths[3] = 18;
        sheet.ColumnWidths[4] = 16;
        sheet.ColumnWidths[5] = 18;
        sheet.ColumnWidths[6] = 16;
        sheet.ColumnWidths[7] = 14;
        sheet.ColumnWidths[8] = 14;

        SeedWorksheetContextTargetsData(sheet);

        var filterRange = Range(sheet.Id, 1, 1, 6, 4);
        sheet.AutoFilter = new WorksheetAutoFilterModel(filterRange.ToString(), null);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(1, ["East"]));
        sheet.FilterHiddenRows.Add(4);

        var tableRange = Range(sheet.Id, 1, 6, 5, 8);
        if (!TryExecuteCommand(
                new CreateStructuredTableCommand(sheet.Id, tableRange, styleName: "TableStyleMedium2"),
                "Create Table",
                out var tableOutcome))
        {
            throw new InvalidOperationException(tableOutcome.ErrorMessage ?? "Worksheet context-targets tour could not create the structured table sample.");
        }

        if (sheet.StructuredTables.All(candidate => !candidate.Range.Equals(tableRange)))
            throw new InvalidOperationException("Worksheet context-targets tour could not find the structured table sample.");

        var noteCell = new CellAddress(sheet.Id, 7, 2);
        var hyperlinkCell = new CellAddress(sheet.Id, 7, 3);
        var protectedCell = new CellAddress(sheet.Id, 10, 5);
        sheet.Comments[noteCell] = "Worksheet context-targets tour note.";
        sheet.Hyperlinks[hyperlinkCell] = "https://example.test/freex-context-targets";
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 8, 2), new CellAddress(sheet.Id, 8, 2)),
            Type = DvType.List,
            Formula1 = "Open,Closed,Review",
            ShowDropdown = true
        });

        var seedRange = Range(sheet.Id, 1, 1, 8, 8);
        SetSelectionRange(new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 2, 2)), new CellAddress(sheet.Id, 2, 2));
        EnsureCellVisible(seedRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new WorksheetContextTargetsTourContext(
            SheetName: sheet.Name,
            FilterRange: filterRange.ToString(),
            StructuredTableRange: tableRange.ToString(),
            NoteCell: noteCell.ToA1(),
            HyperlinkCell: hyperlinkCell.ToA1(),
            ProtectedCell: protectedCell.ToA1(),
            ProtectedTargetStateSupported: false);
    }

    private static void SeedWorksheetContextTargetsData(Sheet sheet)
    {
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Owner")),
            (1, 3, new TextValue("Status")),
            (1, 4, new TextValue("Amount")),
            (2, 1, new TextValue("East")),
            (2, 2, new TextValue("Mara")),
            (2, 3, new TextValue("Open")),
            (2, 4, new NumberValue(1250)),
            (3, 1, new TextValue("West")),
            (3, 2, new TextValue("Ilya")),
            (3, 3, new TextValue("Closed")),
            (3, 4, new NumberValue(980)),
            (4, 1, new TextValue("East")),
            (4, 2, new TextValue("Nia")),
            (4, 3, new TextValue("Review")),
            (4, 4, new NumberValue(1410)),
            (5, 1, new TextValue("North")),
            (5, 2, new TextValue("Olek")),
            (5, 3, new TextValue("Open")),
            (5, 4, new NumberValue(1110)),
            (6, 1, new TextValue("South")),
            (6, 2, new TextValue("Rin")),
            (6, 3, new TextValue("Closed")),
            (6, 4, new NumberValue(870)),
            (1, 6, new TextValue("SKU")),
            (1, 7, new TextValue("Qty")),
            (1, 8, new TextValue("Price")),
            (2, 6, new TextValue("AX-1")),
            (2, 7, new NumberValue(4)),
            (2, 8, new NumberValue(12.5)),
            (3, 6, new TextValue("BX-2")),
            (3, 7, new NumberValue(7)),
            (3, 8, new NumberValue(9.75)),
            (4, 6, new TextValue("CX-3")),
            (4, 7, new NumberValue(3)),
            (4, 8, new NumberValue(18.25)),
            (5, 6, new TextValue("DX-4")),
            (5, 7, new NumberValue(5)),
            (5, 8, new NumberValue(14.5)),
            (7, 1, new TextValue("Special targets")),
            (7, 2, new TextValue("Note cell")),
            (7, 3, new TextValue("Link cell")),
            (10, 5, new TextValue("Protected locked cell")),
            (8, 1, new TextValue("Validation")),
            (8, 2, new TextValue("Open")),
            (9, 5, new TextValue("Protection"))
        };

        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
    }

    private async Task<WorksheetContextTargetsTourManifestCapture> CaptureWorksheetContextTargetMenuAsync(
        string outputDir,
        WorksheetContextTargetsTourContext context,
        WorksheetContextTargetsTourTarget target)
    {
        var sheet = _workbook.GetSheet(_currentSheetId)
            ?? throw new InvalidOperationException("Worksheet context-targets tour lost the active worksheet.");
        var address = new CellAddress(sheet.Id, target.Row, target.Col);
        sheet.IsProtected = target.State == "protected-locked-cell";

        ApplyWorksheetContextTargetSelection(target, address);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(200);

        ContextMenu? menu = null;
        try
        {
            if (target.HeaderTarget is { } headerTarget)
                OnGridHeaderContextMenuRequested(headerTarget, target.HeaderIndex!.Value, GetKeyboardContextMenuGridPoint(address));
            else
                OnGridContextMenuRequested(address, GetKeyboardContextMenuGridPoint(address));

            await Task.Delay(350);
            menu = SheetGrid.ContextMenu
                ?? throw new InvalidOperationException($"Worksheet context-targets tour could not locate the {target.State} menu.");
            menu.UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            await CaptureElementAsync(menu, outputDir, target.FileName);
            var menuItems = ReadWorksheetContextTargetsMenuItems(menu);
            var state = GetWorksheetContextMenuState(address);
            var targetKind = GetWorksheetContextMenuTargetKind(address);

            return new WorksheetContextTargetsTourManifestCapture(
                CaptureKey: $"interactive:worksheet-context-targets:{target.State}",
                PairKey: $"interactive:worksheet-context-targets:{target.State}",
                ScenarioId: "context-menu:worksheet-target-breadth",
                State: target.State,
                Surface: target.Surface,
                TargetAddress: address.ToA1(),
                SelectedRange: SheetGrid.SelectedRange?.ToString() ?? string.Empty,
                TargetKind: targetKind.ToString(),
                FileName: target.FileName,
                OutputFileName: $"{target.FileName}.png",
                CaptureMethod: "RenderTargetBitmap-worksheet-context-menu",
                CaptureLogicalWidth: menu.ActualWidth,
                CaptureLogicalHeight: menu.ActualHeight,
                IsSheetProtected: sheet.IsProtected,
                StateFlags: new WorksheetContextTargetsTourStateFlags(
                    state.HasThreadedComment,
                    state.IsThreadedCommentResolved,
                    state.HasNote,
                    state.HasHyperlink,
                    state.HasAutoFilterHeaderTarget,
                    state.HasDropdownTarget),
                MenuItemCount: menuItems.Count,
                EnabledMenuHeaders: menuItems.Where(item => item.IsEnabled).Select(item => item.Header).ToArray(),
                DisabledMenuHeaders: menuItems.Where(item => !item.IsEnabled).Select(item => item.Header).ToArray(),
                EvidenceSummary: target.EvidenceSummary,
                Limitation: target.Limitation);
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

    private void ApplyWorksheetContextTargetSelection(WorksheetContextTargetsTourTarget target, CellAddress address)
    {
        if (target.HeaderTarget == GridHeaderContextMenuTarget.Row)
        {
            SelectRow(target.HeaderIndex!.Value);
            return;
        }

        if (target.HeaderTarget == GridHeaderContextMenuTarget.Column)
        {
            SelectColumn(target.HeaderIndex!.Value);
            return;
        }

        var selection = target.State == "normal-range"
            ? Range(address.Sheet, 3, 2, 4, 3)
            : new GridRange(address, address);
        SetSelectionRange(selection, address);
        EnsureCellVisible(address);
    }

    private static IReadOnlyList<WorksheetContextTargetsTourMenuItem> ReadWorksheetContextTargetsMenuItems(ContextMenu menu) =>
        menu.Items
            .OfType<MenuItem>()
            .Select(item => new WorksheetContextTargetsTourMenuItem(
                item.Header?.ToString() ?? string.Empty,
                item.IsEnabled))
            .Where(item => !string.IsNullOrWhiteSpace(item.Header))
            .ToArray();

    private static IReadOnlyList<WorksheetContextTargetsTourTarget> WorksheetContextTargetsTourTargets() =>
    [
        new(
            State: "normal-cell",
            Surface: "Worksheet cell context menu",
            Row: 2,
            Col: 2,
            FileName: "freex_worksheet_context_target_normal_cell",
            EvidenceSummary: "Default worksheet cell target on B2."),
        new(
            State: "normal-range",
            Surface: "Worksheet range context menu",
            Row: 3,
            Col: 2,
            FileName: "freex_worksheet_context_target_normal_range",
            EvidenceSummary: "Normal multi-cell selected range B3:C4."),
        new(
            State: "whole-row",
            Surface: "Whole-row header context menu",
            Row: 5,
            Col: 1,
            FileName: "freex_worksheet_context_target_whole_row",
            EvidenceSummary: "Row-header target with whole-row selection and row sizing/visibility commands.",
            HeaderTarget: GridHeaderContextMenuTarget.Row,
            HeaderIndex: 5),
        new(
            State: "whole-column",
            Surface: "Whole-column header context menu",
            Row: 1,
            Col: 5,
            FileName: "freex_worksheet_context_target_whole_column",
            EvidenceSummary: "Column-header target with whole-column selection and column sizing/visibility commands.",
            HeaderTarget: GridHeaderContextMenuTarget.Column,
            HeaderIndex: 5),
        new(
            State: "table-cell",
            Surface: "Structured-table body cell context menu",
            Row: 3,
            Col: 6,
            FileName: "freex_worksheet_context_target_table_cell",
            EvidenceSummary: "Structured-table body cell target inside the seeded TourTable.",
            Limitation: "FreeX currently routes table-cell right-clicks through the worksheet-cell context menu; table-specific context-menu deltas remain future UX parity work."),
        new(
            State: "autofilter-header",
            Surface: "AutoFilter/current-region header context menu",
            Row: 1,
            Col: 1,
            FileName: "freex_worksheet_context_target_autofilter_header",
            EvidenceSummary: "Current-region header target with Clear Filter, Reapply Filter, and Pick From Drop-down List enabled."),
        new(
            State: "note-cell",
            Surface: "Note/comment cell context menu",
            Row: 7,
            Col: 2,
            FileName: "freex_worksheet_context_target_note_cell",
            EvidenceSummary: "Cell with a seeded legacy note/comment so Edit Note, Delete Note, and Show Notes are enabled."),
        new(
            State: "hyperlink-cell",
            Surface: "Hyperlink cell context menu",
            Row: 7,
            Col: 3,
            FileName: "freex_worksheet_context_target_hyperlink_cell",
            EvidenceSummary: "Cell with a seeded hyperlink so Open/Edit/Remove Hyperlink commands replace the default Hyperlink entry."),
        new(
            State: "protected-locked-cell",
            Surface: "Protected locked cell context menu",
            Row: 10,
            Col: 5,
            FileName: "freex_worksheet_context_target_protected_locked_cell",
            EvidenceSummary: "Protected worksheet with the default locked cell target selected.",
            Limitation: "WorksheetContextMenuPlanner does not yet disable or reshape menu commands for protected locked cells; command rejection remains command-layer behavior.")
    ];

    private static void DeleteWorksheetContextTargetsTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var fileName in WorksheetContextTargetsTourTargets().Select(target => $"{target.FileName}.png").Append(WorksheetContextTargetsTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static void ValidateWorksheetContextTargetsTourEvidence(
        string outputDir,
        IReadOnlyList<WorksheetContextTargetsTourManifestCapture> captures)
    {
        var expectedFiles = WorksheetContextTargetsTourTargets()
            .Select(target => $"{target.FileName}.png")
            .ToArray();
        var missing = expectedFiles
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (captures.Count != expectedFiles.Length || missing.Length > 0)
            throw new InvalidOperationException(
                $"Worksheet context-targets tour expected {expectedFiles.Length} captures and missed {missing.Length}: {string.Join(", ", missing)}.");

        foreach (var fileName in expectedFiles)
        {
            var path = Path.Combine(outputDir, fileName);
            if (new FileInfo(path).Length == 0)
                throw new InvalidOperationException($"Worksheet context-targets tour created an empty capture: {fileName}.");
        }
    }

    private static async Task WriteWorksheetContextTargetsTourManifestAsync(
        string outputDir,
        WorksheetContextTargetsTourContext context,
        IReadOnlyList<WorksheetContextTargetsTourManifestCapture> captures)
    {
        var targets = WorksheetContextTargetsTourTargets();
        var plannedTargets = targets
            .Select(target => new WorksheetContextTargetsTourPlannedTarget(
                State: target.State,
                Surface: target.Surface,
                TargetAddress: $"{CellAddress.NumberToColumnName(target.Col)}{target.Row}",
                ExpectedOutputFileName: $"{target.FileName}.png",
                ActualStatus: captures.Any(capture => capture.State == target.State)
                    ? target.State == "protected-locked-cell" ? "captured-with-limitation" : "captured"
                    : "missing",
                Limitation: target.Limitation))
            .ToArray();

        var manifest = new WorksheetContextTargetsTourManifest(
            Tool: "FREEX_WORKSHEET_CONTEXT_TARGETS_TOUR",
            EvidenceFamily: "worksheet-context-menu-target-breadth",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "context-menu:worksheet-target-breadth",
            OutputDirectory: outputDir,
            OutputNaming: "freex_worksheet_context_target_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-CONTEXT-001", "UI-CMD-HARNESS-001"],
            SheetName: context.SheetName,
            FilterRange: context.FilterRange,
            StructuredTableRange: context.StructuredTableRange,
            NoteCell: context.NoteCell,
            HyperlinkCell: context.HyperlinkCell,
            ProtectedCell: context.ProtectedCell,
            ProtectedTargetStateSupported: context.ProtectedTargetStateSupported,
            CaptureStatus: captures.Count == targets.Count ? "complete" : "partial",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: targets.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new WorksheetContextTargetsTourManifestPairing(
                "interactive:worksheet-context-targets:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, or screen capture input is used."
                    : "Abort before file write unless the expected FreeX window/menu owns foreground focus."),
            PlannedTargets: plannedTargets,
            Captures: captures,
            CoveredStates: captures.Select(capture => capture.State).ToArray(),
            Limitations:
            [
                "This tour captures FreeX-only production WPF ContextMenu surfaces with RenderTargetBitmap; paired Microsoft Excel screenshots remain separate.",
                "Foreground mouse right-click, Shift+F10/Menu-key traversal, access-key traversal, UIA invocation, focus return, and OS-composited popup proof remain open.",
                "FreeX currently exposes table-cell context through the worksheet-cell menu rather than a table-specialized context menu.",
                "FreeX currently leaves protected locked-cell context-menu command enablement to the shared worksheet planner and command-layer rejection; target-specific disabled protected states remain open."
            ]);

        var path = Path.Combine(outputDir, WorksheetContextTargetsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.WorksheetContextTargetsTourManifest);
    }


    private sealed record WorksheetContextTargetsTourTarget(
        string State,
        string Surface,
        uint Row,
        uint Col,
        string FileName,
        string EvidenceSummary,
        GridHeaderContextMenuTarget? HeaderTarget = null,
        uint? HeaderIndex = null,
        string Limitation = "");

    private sealed record WorksheetContextTargetsTourContext(
        string SheetName,
        string FilterRange,
        string StructuredTableRange,
        string NoteCell,
        string HyperlinkCell,
        string ProtectedCell,
        bool ProtectedTargetStateSupported);

    private sealed record WorksheetContextTargetsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string FilterRange,
        string StructuredTableRange,
        string NoteCell,
        string HyperlinkCell,
        string ProtectedCell,
        bool ProtectedTargetStateSupported,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        WorksheetContextTargetsTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<WorksheetContextTargetsTourPlannedTarget> PlannedTargets,
        IReadOnlyList<WorksheetContextTargetsTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record WorksheetContextTargetsTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartStatus);

    private sealed record WorksheetContextTargetsTourPlannedTarget(
        string State,
        string Surface,
        string TargetAddress,
        string ExpectedOutputFileName,
        string ActualStatus,
        string Limitation);

    private sealed record WorksheetContextTargetsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string TargetAddress,
        string SelectedRange,
        string TargetKind,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        bool IsSheetProtected,
        WorksheetContextTargetsTourStateFlags StateFlags,
        int MenuItemCount,
        IReadOnlyList<string> EnabledMenuHeaders,
        IReadOnlyList<string> DisabledMenuHeaders,
        string EvidenceSummary,
        string Limitation);

    private sealed record WorksheetContextTargetsTourStateFlags(
        bool HasThreadedComment,
        bool IsThreadedCommentResolved,
        bool HasNote,
        bool HasHyperlink,
        bool HasAutoFilterHeaderTarget,
        bool HasDropdownTarget);

    private sealed record WorksheetContextTargetsTourMenuItem(string Header, bool IsEnabled);
}
