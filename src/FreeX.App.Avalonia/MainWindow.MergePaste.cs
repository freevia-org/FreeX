using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation;
using FreeX.App.Presentation.Editing;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

// GOTCHA: an unqualified HorizontalAlignment inside a MainWindow partial resolves to the
// Avalonia.Controls.Control.HorizontalAlignment property, not the layout enum. Alias the enum
// so dialog layout code can set it explicitly.
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity Home-tab merge variants and the "Paste Special..." dialog for the FreeX
/// Avalonia shell.
///
/// These are the ribbon ids that previously resolved to no-ops:
///   - home.mergeCells   ("Merge Cells")    -> merge the selection into one region, NO re-centering.
///   - home.mergeAcross  ("Merge Across")   -> merge each selected ROW into one cell, row by row.
///   - home.unmerge      ("Unmerge Cells")  -> wired to the existing <see cref="UnmergeSelectedRange"/>.
///   - home.pasteSpecial ("Paste Special...") -> an Excel-parity dialog exposing the full content-kind
///                                               radio set, composable Skip Blanks/Transpose/Keep Source
///                                               Column Widths checkboxes, and a math Operation group,
///                                               layered on top of the existing clipboard paste model.
///
/// Everything here reuses the established shell glue: command construction via the
/// <see cref="FreeX.App.Services.CellMergePlanner"/> primitives, execution through
/// <c>WorkbookSession.ExecuteReviewCommand(IWorkbookCommand)</c>, and the same content-loss warning
/// dialog used by Merge &amp; Center. No new <see cref="FreeX.App.Services.WorkbookSession"/> method is
/// required.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// "Merge Cells" (home.mergeCells). Like Merge &amp; Center but WITHOUT re-centering the result.
    /// Merges the whole selection into a single merged region, keeping (or, on request, concatenating)
    /// the contents using the same warning dialog as Merge &amp; Center.
    ///
    /// R127-avalonia-mergepaste-multiarea-1: a Ctrl+click multi-area selection (<c>_session.SelectedRanges</c>)
    /// must merge EVERY disjoint area independently, not just the active <c>_session.SelectedRange</c> --
    /// matching Excel and the WPF host's MainWindow.HomeFormatting.cs fix for the same defect. Resolved via
    /// the same <see cref="SelectionStyleCommandPlanner.ResolveRanges"/> choke point the WPF host and
    /// <see cref="FreeX.App.Services.WorkbookSession"/>'s own multi-area fixes use.
    ///
    /// R130-avalonia-mergepaste-groupedsheet-1 (parity fix): when tabs are grouped (<c>_session.IsWorkbookGrouped</c>),
    /// Excel/WPF's <c>MergeCellsMenuItem_Click</c> (via <c>TryExecuteRepeatableCurrentSelectionRangesCommand</c> ->
    /// <see cref="SelectionStyleCommandPlanner.CreateRangeCommand"/>) fans the merge out to EVERY sheet
    /// <c>CurrentGroupedEditSheetIds()</c> returns, not just the active sheet. This handler previously scoped
    /// both the command build AND the content-loss analysis to <c>_session.ActiveSheet</c> only, so with
    /// grouped tabs, Windows merged every grouped sheet while Linux/macOS merged only the active one -- a
    /// silent functional divergence for the same user gesture (not itself data loss, since the narrow
    /// analysis matched the narrow execution). Both are now widened together: execution fans `areas` across
    /// <c>_session.GetCurrentGroupedEditSheetIds()</c> via <see cref="GroupedSheetRangePlanner.RemapRangeToSheet"/>
    /// (mirroring <see cref="SelectionStyleCommandPlanner.CreateRangeCommand"/>'s sheet x range fan-out), and
    /// the analysis uses <see cref="AnalyzeGroupedSheetMergeContent"/> -- the SAME grouped-sheet-aware helper
    /// already used by <c>MergeAndCenterSelectedRangeAsync</c> and <c>ShowFormatCellsDialogAsync</c> (R128B)
    /// -- so a non-active grouped sheet's content is warned about, not silently discarded (avoiding the
    /// r127/r128 trap of widening execution without widening the guard in the same change). When the
    /// workbook isn't grouped, <c>GetCurrentGroupedEditSheetIds()</c> returns just the active sheet, so
    /// ungrouped behaviour is unchanged.
    /// </summary>
    private async Task MergeSelectedRangeAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var areas = SelectionStyleCommandPlanner.ResolveRanges(range, _session.SelectedRanges);
        if (areas.Count == 0 || areas.All(area => area.CellCount <= 1))
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(UiText.Get("TableLoc_MergeSelectTwoOrMoreCells"));
            return;
        }

        var contentResolution = MergeCellContentResolution.KeepFirstCell;
        // R127-avalonia-mergepaste-multiarea-2 (data-loss fix): analyze EVERY disjoint area the merge
        // above will actually touch, not just the active `range` -- a Ctrl+click area other than the
        // active one can hold content that is about to be silently discarded with no warning at all.
        // R130-avalonia-mergepaste-groupedsheet-1: AnalyzeGroupedSheetMergeContent (not the single-sheet
        // CellMergePlanner.AnalyzeContent overload) so a non-active grouped sheet is covered too -- see
        // the fan-out execution below and the class doc comment above.
        var contentPlan = AnalyzeGroupedSheetMergeContent(areas);
        if (contentPlan.WouldLoseContent)
        {
            var decision = CellMergePlanner.ResolveContentChoice(
                await ShowMergeCellsContentWarningDialogAsync(contentPlan));
            if (!decision.ShouldProceed)
            {
                RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
                return;
            }

            contentResolution = decision.Resolution;
        }

        var rangeReference = FormatRangeReference(range);
        // R130-avalonia-mergepaste-groupedsheet-1: fan each disjoint area across every grouped-edit
        // sheet, remapping the area onto that sheet -- mirrors
        // SelectionStyleCommandPlanner.CreateRangeCommand's sheet x range cross product (the WPF host's
        // own execution choke point for MergeCellsMenuItem_Click). Ungrouped selections still resolve to
        // a single-sheet loop since GetCurrentGroupedEditSheetIds() returns just the active sheet.
        var targetSheetIds = _session.GetCurrentGroupedEditSheetIds();
        var areaCommands = new List<IWorkbookCommand>();
        foreach (var sheetId in targetSheetIds)
        {
            var sheet = _session.Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            foreach (var area in areas)
            {
                var sheetArea = GroupedSheetRangePlanner.RemapRangeToSheet(area, sheetId);
                areaCommands.Add(CellMergePlanner.CreateMergeCellsCommand(
                    sheet,
                    sheetId,
                    sheetArea,
                    contentResolution));
            }
        }

        var command = CellMergePlanner.WrapCommands("Merge Cells", areaCommands);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_MergeCellsFailed"));
            return;
        }

        RefreshShell(UiText.Format("TableLoc_Merged", rangeReference));
    }

    /// <summary>
    /// "Merge Across" (home.mergeAcross). Merges each selected ROW into a single horizontal merged
    /// region, leaving the rows independent from one another (matching Excel's Merge Across behaviour).
    /// A single-column selection has nothing to merge across.
    ///
    /// R127-avalonia-mergepaste-multiarea-1: extended to a Ctrl+click multi-area selection the same way
    /// as <see cref="MergeSelectedRangeAsync"/> above -- every disjoint area gets its own per-row merge
    /// batch, and an area that is itself single-column (but sits alongside a wider area) is skipped
    /// rather than rejecting the whole action, matching real Excel and the WPF host's
    /// MainWindow.HomeFormatting.cs fix for the same defect.
    ///
    /// R130-avalonia-mergepaste-groupedsheet-2 (parity fix): same grouped-sheet fan-out gap and fix as
    /// <see cref="MergeSelectedRangeAsync"/>'s R130 note above -- the WPF host's
    /// <c>MergeAcrossMenuItem_Click</c> fans across <c>CurrentGroupedEditSheetIds()</c> via the same
    /// <c>TryExecuteRepeatableCurrentSelectionRangesCommand</c> choke point, so this handler now builds
    /// its per-area per-row commands for every grouped-edit sheet (not just the active one), and analyzes
    /// content loss the same way via <see cref="AnalyzeGroupedSheetMergeContent"/> (widened together, not
    /// separately -- see the r127/r128 trap note there).
    /// </summary>
    private async Task MergeAcrossSelectedRangeAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        var areas = SelectionStyleCommandPlanner.ResolveRanges(range, _session.SelectedRanges);
        if (areas.Count == 0 || areas.All(area => area.ColCount <= 1))
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(UiText.Get("TableLoc_MergeAcrossSelectTwoOrMoreColumns"));
            return;
        }

        // Analyze the WHOLE selection once up front, matching the WPF host's
        // TryResolveMergeContentResolution and this file's own MergeSelectedRangeAsync above, so
        // multi-cell rows (e.g. A1:C1 = "Jan"/"Feb"/"Mar") get the "Merging cells only keeps the
        // upper-leftmost value" confirmation instead of the per-row merges below silently discarding
        // every non-left-most value in each row with zero warning.
        //
        // R127-avalonia-mergepaste-multiarea-2 (data-loss fix): analyze EVERY disjoint area in `areas`,
        // not just the active `range` -- a Ctrl+click area other than the active one can hold content
        // that the per-area per-row loop below is about to discard with no warning at all.
        //
        // R130-avalonia-mergepaste-groupedsheet-2: AnalyzeGroupedSheetMergeContent (perRow: true) covers
        // every grouped-edit sheet too, matching the fan-out execution below.
        var contentResolution = MergeCellContentResolution.KeepFirstCell;
        var contentPlan = AnalyzeGroupedSheetMergeContent(areas, perRow: true);
        if (contentPlan.WouldLoseContent)
        {
            var decision = CellMergePlanner.ResolveContentChoice(
                await ShowMergeCellsContentWarningDialogAsync(contentPlan));
            if (!decision.ShouldProceed)
            {
                RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
                return;
            }

            contentResolution = decision.Resolution;
        }

        // Build one composite per (grouped sheet, disjoint area) pair, each containing one horizontal
        // per-row merge for that area's own column span remapped onto that sheet. An area that is
        // itself single-column contributes nothing. Ungrouped selections resolve to a single-sheet loop since
        // GetCurrentGroupedEditSheetIds() returns just the active sheet.
        var targetSheetIds = _session.GetCurrentGroupedEditSheetIds();
        var areaCommands = new List<IWorkbookCommand>();
        foreach (var sheetId in targetSheetIds)
        {
            var sheet = _session.Workbook.GetSheet(sheetId);
            if (sheet is null)
                continue;

            foreach (var area in areas)
            {
                if (area.ColCount <= 1)
                    continue;

                var sheetArea = GroupedSheetRangePlanner.RemapRangeToSheet(area, sheetId);

                areaCommands.Add(CellMergePlanner.CreateMergeAcrossCommand(
                    sheet,
                    sheetId,
                    sheetArea,
                    contentResolution));
            }
        }

        if (areaCommands.Count == 0)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            return;
        }

        var rangeReference = FormatRangeReference(range);
        var command = CellMergePlanner.WrapCommands("Merge Across", areaCommands);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_MergeAcrossFailed"));
            return;
        }

        RefreshShell(UiText.Format("TableLoc_MergedAcross", rangeReference));
    }

    /// <summary>
    /// "Paste Special..." (home.pasteSpecial). Opens an Excel-parity dialog: a full content-kind radio
    /// group, composable Skip Blanks / Transpose / Keep Source Column Widths checkboxes, a math Operation
    /// group (None/Add/Subtract/Multiply/Divide) and a Paste Link button -- matching WPF's
    /// <see cref="FreeX.App.Host.PasteSpecialDialog"/> and real Excel's single unified Paste Special
    /// dialog.
    ///
    /// R120: previously this ribbon-triggered dialog exposed only All / Values / Formulas / Formats; the
    /// richer content kinds (All Except Borders, All Merging Conditional Formats, Formulas/Values and
    /// Number Formats, Values and Source Formatting, Column Widths, Comments and Notes, Validation, Text,
    /// Unicode Text, Picture, Linked Picture, Paste Link, Skip Blanks, Transpose, the math Operations) were
    /// reachable only through the Paste split-button's Paste Special submenu (<see cref="CreatePasteSpecialMenuItems"/>),
    /// never from this ribbon button. Every option below dispatches through those SAME already-wired
    /// execution methods (<see cref="PasteSpecialClipboardTextAsync"/>, <see cref="PasteCommentsFromClipboardAsync"/>,
    /// <see cref="PasteDataValidationFromClipboardAsync"/>, <see cref="PasteColumnWidthsFromClipboardAsync"/>,
    /// <see cref="PasteSpecialExternalTextFromClipboardAsync"/>, <see cref="PastePictureFromClipboardAsync"/>,
    /// <see cref="PasteLinkFromClipboardAsync"/>) -- no new paste behaviour is introduced, only a single
    /// consolidated surface for what the shell already does.
    /// </summary>
    private async Task ShowPasteSpecialDialogAsync()
    {
        Func<Task>? workflowOverride = null;
        ResolvePasteSpecialWorkflowOverride(ref workflowOverride);
        if (workflowOverride is not null)
        {
            await workflowOverride();
            return;
        }

        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var choice = await PromptPasteSpecialModeAsync();
        if (choice is not { } selection)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            return;
        }

        var plan = PasteSpecialPlanner.CreatePlan(selection);
        switch (plan.Action)
        {
            case PasteSpecialAction.Comments:
                await PasteCommentsFromClipboardAsync(plan.Label);
                return;
            case PasteSpecialAction.Validation:
                await PasteDataValidationFromClipboardAsync(plan.Label);
                return;
            case PasteSpecialAction.ColumnWidths:
                await PasteColumnWidthsFromClipboardAsync(plan.Label);
                return;
            case PasteSpecialAction.ExternalText:
                await PasteSpecialExternalTextFromClipboardAsync(plan.Label);
                return;
            case PasteSpecialAction.Picture:
                await PastePictureFromClipboardAsync(plan.Label, linkedPicture: false);
                return;
            case PasteSpecialAction.LinkedPicture:
                await PastePictureFromClipboardAsync(plan.Label, linkedPicture: true);
                return;
            case PasteSpecialAction.Link:
                await PasteLinkFromClipboardAsync(plan.Label);
                return;
            default:
                await PasteSpecialClipboardTextAsync(
                    ClipboardPastePlanner.ToCorePasteMode(plan.PasteMode),
                    plan.Options,
                    plan.Label,
                    plan.KeepColumnWidths);
                return;
        }
    }

    partial void ResolvePasteSpecialWorkflowOverride(ref Func<Task>? handler);

    /// <summary>
    /// Shows the native dialog and returns the shared typed selection, or <c>null</c>. The shared
    /// catalog supplies content order, action policy, labels, stable identities, and default state.
    /// </summary>
    internal async Task<PasteSpecialDialogSelection?> PromptPasteSpecialModeAsync()
    {
        PasteSpecialDialogSelection? result = null;
        var surface = PasteSpecialPlanner.Surface;

        // Layout mirrors WPF PasteSpecialDialog: a 470x550 NoResize window with a
        // 16px root margin, a "Paste" group holding the content kinds in two columns, the three
        // toggles, an "Operation" group in two columns and a right-aligned Paste Link / OK / Cancel row.
        // The height is sized to the content instead of guessing a caption allowance.
        var dialog = new Window
        {
            Title = surface.Title.ResolveAvalonia(UiText.Get),
            Width = 470,
            MinWidth = 470,
            MaxWidth = 470,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, surface.AutomationId);

        var root = new StackPanel
        {
            Margin = new Thickness(16),
        };

        // WPF places the content kinds by PasteSpecialChoiceDescriptor.WpfPlacement (column 0 top to
        // bottom, then column 1). Every WPF content kind is listed, including the source-theme one,
        // which pastes through the shared PasteSpecialContentKind the plan carries.
        var radios = new List<RadioButton>();
        var pasteGrid = new Grid();
        pasteGrid.ColumnDefinitions.Add(new ColumnDefinition());
        pasteGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var nextPasteRow = new int[2];
        foreach (var choice in surface.WpfChoices
            .OrderBy(descriptor => descriptor.WpfPlacement.Column)
            .ThenBy(descriptor => descriptor.WpfPlacement.Row))
        {
            var radio = new RadioButton
            {
                Content = UiText.Get(choice.WpfLabelTextKey),
                GroupName = "PasteSpecialMode",
                IsChecked = choice.IsDefault,
                IsEnabled = choice.IsEnabled,
                Tag = choice,
                // WPF: a ~16px radio with a 6px bottom margin; the compact radio here is a 20px row.
                Margin = new Thickness(0, 0, 0, 2),
            };
            ApplyDataOpsRadioButtonChrome(radio);
            AutomationProperties.SetAutomationId(radio, choice.AvaloniaAutomationId);
            var column = Math.Clamp(choice.WpfPlacement.Column, 0, 1);
            var row = nextPasteRow[column]++;
            while (pasteGrid.RowDefinitions.Count <= row)
                pasteGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(radio, row);
            Grid.SetColumn(radio, column);
            radios.Add(radio);
            pasteGrid.Children.Add(radio);
        }

        var pasteGroup = new GroupBox
        {
            Header = surface.PasteGroup.ResolveAvalonia(UiText.Get),
            Content = pasteGrid,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 10),
        };
        ApplyPlainWindowGroupBoxChrome(pasteGroup, DataOpsDialogChromeStyle);
        root.Children.Add(pasteGroup);

        var skipBlanksBox = CreateAvaloniaToggle(surface.GetToggle(PasteSpecialToggleKind.SkipBlanks));
        var transposeBox = CreateAvaloniaToggle(surface.GetToggle(PasteSpecialToggleKind.Transpose));
        var keepColumnWidthsBox = CreateAvaloniaToggle(surface.GetToggle(PasteSpecialToggleKind.KeepColumnWidths));

        // WPF toggle margins are (0,0,0,8) / (0,4,0,8) / (0,0,0,8) around ~16px check boxes; the
        // compact check box here is a 20px row, so 4px keeps the same 24px pitch.
        skipBlanksBox.Margin = new Thickness(0, 0, 0, 4);
        transposeBox.Margin = new Thickness(0, 4, 0, 4);
        keepColumnWidthsBox.Margin = new Thickness(0, 0, 0, 4);
        var checkboxPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        checkboxPanel.Children.Add(skipBlanksBox);
        checkboxPanel.Children.Add(transposeBox);
        checkboxPanel.Children.Add(keepColumnWidthsBox);
        root.Children.Add(checkboxPanel);

        var operationRadios = new List<RadioButton>();
        var operationGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        operationGrid.ColumnDefinitions.Add(new ColumnDefinition());
        operationGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 3; i++)
            operationGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        foreach (var operation in surface.Operations.OrderBy(descriptor => descriptor.Order))
        {
            var radio = new RadioButton
            {
                Content = UiText.Get(operation.WpfLabelTextKey),
                GroupName = "PasteSpecialOperation",
                IsChecked = operation.IsDefault,
                IsEnabled = operation.IsEnabled,
                Tag = operation.Operation,
                Margin = new Thickness(0, 0, 12, 2),
            };
            ApplyDataOpsRadioButtonChrome(radio);
            AutomationProperties.SetAutomationId(radio, operation.AvaloniaAutomationId);
            Grid.SetRow(radio, operation.Placement.Row);
            Grid.SetColumn(radio, operation.Placement.Column);
            operationRadios.Add(radio);
            operationGrid.Children.Add(radio);
        }

        var operationGroup = new GroupBox
        {
            Header = surface.OperationGroup.ResolveAvalonia(UiText.Get),
            Content = operationGrid,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12),
        };
        ApplyPlainWindowGroupBoxChrome(operationGroup, DataOpsDialogChromeStyle);
        root.Children.Add(operationGroup);

        var pasteLinkAction = surface.GetAction(PasteSpecialDialogActionKind.PasteLink);
        var pasteLinkButton = new Button
        {
            Content = pasteLinkAction.ResolveAvaloniaLabel(UiText.Get),
            // WPF footer: Paste Link 96 wide, OK and Cancel 80 wide, 8px apart.
            MinWidth = 96,
            IsEnabled = pasteLinkAction.IsEnabled,
        };
        ApplyDataOpsButtonChrome(pasteLinkButton);
        AutomationProperties.SetAutomationId(pasteLinkButton, pasteLinkAction.AvaloniaAutomationId);
        pasteLinkButton.Click += (_, _) =>
        {
            result = PasteSpecialPlanner.CreatePasteLinkSelection();
            dialog.Close();
        };

        var acceptAction = surface.GetAction(PasteSpecialDialogActionKind.Accept);
        var okButton = new Button
        {
            Content = acceptAction.ResolveAvaloniaLabel(UiText.Get),
            MinWidth = 80,
            IsDefault = acceptAction.IsDefault,
            IsEnabled = acceptAction.IsEnabled,
        };
        ApplyDataOpsButtonChrome(okButton, isDefault: acceptAction.IsDefault);
        AutomationProperties.SetAutomationId(okButton, acceptAction.AvaloniaAutomationId);
        okButton.Click += (_, _) =>
        {
            var selected = radios.FirstOrDefault(r => r.IsChecked == true);
            if (selected?.Tag is PasteSpecialChoiceDescriptor choice)
            {
                var operation = PasteSpecialOperation.None;
                if (operationRadios.FirstOrDefault(r => r.IsChecked == true) is { Tag: PasteSpecialOperation selectedOperation })
                    operation = selectedOperation;

                result = PasteSpecialPlanner.CreateSelection(
                    choice.Mode,
                    operation,
                    skipBlanks: skipBlanksBox.IsChecked == true,
                    transpose: transposeBox.IsChecked == true,
                    keepColumnWidths: keepColumnWidthsBox.IsChecked == true);
            }

            dialog.Close();
        };

        var cancelAction = surface.GetAction(PasteSpecialDialogActionKind.Cancel);
        var cancelButton = new Button
        {
            Content = cancelAction.ResolveAvaloniaLabel(UiText.Get),
            MinWidth = 80,
            IsCancel = cancelAction.IsCancel,
            IsEnabled = cancelAction.IsEnabled,
        };
        ApplyDataOpsButtonChrome(cancelButton);
        AutomationProperties.SetAutomationId(cancelButton, cancelAction.AvaloniaAutomationId);
        cancelButton.Click += (_, _) =>
        {
            result = null;
            dialog.Close();
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
        };
        buttonRow.Children.Add(pasteLinkButton);
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);
        root.Children.Add(buttonRow);

        dialog.Content = root;
        dialog.Opened += (_, _) => okButton.Focus();
        AttachOptionalPasteSpecialDialogObservation(
            dialog,
            radios,
            skipBlanksBox,
            transposeBox,
            keepColumnWidthsBox,
            operationRadios,
            pasteLinkButton,
            okButton,
            cancelButton);

        await dialog.ShowDialog(this);
        return result;
    }

    private static CheckBox CreateAvaloniaToggle(PasteSpecialToggleDescriptor toggle)
    {
        var checkBox = new CheckBox
        {
            Content = UiText.Get(toggle.WpfLabelTextKey),
            IsChecked = toggle.IsCheckedByDefault,
            IsEnabled = toggle.IsEnabled,
        };
        ApplyDataOpsCheckBoxChrome(checkBox);
        AutomationProperties.SetAutomationId(checkBox, toggle.AvaloniaAutomationId);
        return checkBox;
    }
}
