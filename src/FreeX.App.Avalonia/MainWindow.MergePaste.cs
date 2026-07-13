using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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
///   - home.pasteSpecial ("Paste Special...") -> a radio-option dialog (All / Values / Formulas / Formats)
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
    /// </summary>
    private async Task MergeSelectedRangeAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        if (range.CellCount <= 1)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(UiText.Get("TableLoc_MergeSelectTwoOrMoreCells"));
            return;
        }

        var contentResolution = MergeCellContentResolution.KeepFirstCell;
        var contentPlan = CellMergePlanner.AnalyzeContent(_session.ActiveSheet, range);
        if (contentPlan.WouldLoseContent)
        {
            var choice = await ShowMergeCellsContentWarningDialogAsync(contentPlan);
            if (choice == MergeCellsWarningChoice.Cancel)
            {
                RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
                return;
            }

            contentResolution = choice == MergeCellsWarningChoice.ConcatenateAllCells
                ? MergeCellContentResolution.ConcatenateAllCells
                : MergeCellContentResolution.KeepFirstCell;
        }

        var rangeReference = FormatRangeReference(range);
        var sheetId = _session.ActiveSheet.Id;
        var command = BuildMergeWithoutCenterCommand(_session.ActiveSheet, sheetId, range, contentResolution);
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
    /// </summary>
    private async Task MergeAcrossSelectedRangeAsync()
    {
        if (_isOpening || _isSaving)
            return;

        if (!TryCommitPendingFormulaEdit())
            return;

        var range = _session.SelectedRange;
        if (range.ColCount <= 1)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            ShowEditIssue(UiText.Get("TableLoc_MergeAcrossSelectTwoOrMoreColumns"));
            return;
        }

        var sheet = _session.ActiveSheet;
        var sheetId = sheet.Id;

        // Analyze the WHOLE selection once up front, matching the WPF host's
        // TryResolveMergeContentResolution and this file's own MergeSelectedRangeAsync above, so
        // multi-cell rows (e.g. A1:C1 = "Jan"/"Feb"/"Mar") get the "Merging cells only keeps the
        // upper-leftmost value" confirmation instead of the per-row merges below silently discarding
        // every non-left-most value in each row with zero warning.
        var contentResolution = MergeCellContentResolution.KeepFirstCell;
        var contentPlan = CellMergePlanner.AnalyzeContent(sheet, range);
        if (contentPlan.WouldLoseContent)
        {
            var choice = await ShowMergeCellsContentWarningDialogAsync(contentPlan);
            if (choice == MergeCellsWarningChoice.Cancel)
            {
                RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
                return;
            }

            contentResolution = choice == MergeCellsWarningChoice.ConcatenateAllCells
                ? MergeCellContentResolution.ConcatenateAllCells
                : MergeCellContentResolution.KeepFirstCell;
        }

        // Build one horizontal merge per row. Each per-row range spans the full selected column span.
        var rowCommands = new List<IWorkbookCommand>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var rowRange = new GridRange(
                new CellAddress(sheetId, row, range.Start.Col),
                new CellAddress(sheetId, row, range.End.Col));

            // Per-row merge with no centering, using the resolution chosen (once) above.
            rowCommands.Add(BuildMergeWithoutCenterCommand(
                sheet, sheetId, rowRange, contentResolution));
        }

        if (rowCommands.Count == 0)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            return;
        }

        var rangeReference = FormatRangeReference(range);
        var command = new CompositeWorkbookCommand("Merge Across", rowCommands);
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
    /// Builds the command(s) to merge <paramref name="range"/> into one region WITHOUT re-centering.
    /// Delegates to <see cref="FreeX.App.Services.CellMergePlanner.CreateFormatCellsMergeCommands"/>
    /// (mergeCells: true) rather than duplicating its merge/concatenate logic here, so "Merge Cells"
    /// and, via the per-row loop in <see cref="MergeAcrossSelectedRangeAsync"/>, "Merge Across" pick up
    /// the same Excel-parity toggle-to-unmerge gesture that planner implements: re-invoking either
    /// command on a selection that is already fully covered by an existing merged region unmerges it
    /// instead of failing with "Range overlaps an existing merged region.". The center
    /// <see cref="ApplyStyleCommand"/> that
    /// <see cref="FreeX.App.Services.CellMergePlanner.CreateMergeAndCenterCommands(Sheet?, SheetId, GridRange, MergeCellContentResolution)"/>
    /// appends is filtered out by CreateFormatCellsMergeCommands for the concatenate path, and never
    /// added on the keep-first-cell path, so no re-centering leaks in here. A degenerate (single-cell)
    /// range produces a no-op composite, which the edit service treats as a successful empty command.
    /// </summary>
    private static IWorkbookCommand BuildMergeWithoutCenterCommand(
        Sheet sheet,
        SheetId sheetId,
        GridRange range,
        MergeCellContentResolution contentResolution)
    {
        var commands = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet, sheetId, range, mergeCells: true, contentResolution);

        return commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand("Merge Cells", commands);
    }

    /// <summary>
    /// "Paste Special..." (home.pasteSpecial). Opens a Windows-style dialog with radio options for the
    /// content kind to paste, then defers to the existing clipboard paste pipeline
    /// (<see cref="PasteSpecialClipboardTextAsync"/> -> <c>WorkbookSession.PasteSpecialClipboardAtActiveCell</c>).
    ///
    /// Scope: the shell clipboard model is text-backed, so the dialog surfaces the subset of Paste Special
    /// options that the text round-trip can honor faithfully:
    ///   All / Values / Formulas / Formats. These map to <see cref="PasteCellsMode"/> directly.
    /// The richer content kinds (e.g. "Values and Number Formats", Transpose, math Operations, Skip Blanks)
    /// remain available through the Paste split-button's Paste Special submenu and are intentionally not
    /// duplicated here to keep the ribbon dialog focused, matching the primary Excel radio set.
    /// </summary>
    private async Task ShowPasteSpecialDialogAsync()
    {
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

        await PasteSpecialClipboardTextAsync(selection.Mode, default, selection.Label);
    }

    private sealed record PasteSpecialModeChoice(PasteCellsMode Mode, string Label);

    /// <summary>
    /// Shows the Paste Special radio dialog and returns the selected mode, or <c>null</c> if cancelled.
    /// </summary>
    private async Task<PasteSpecialModeChoice?> PromptPasteSpecialModeAsync()
    {
        PasteSpecialModeChoice? result = null;

        var dialog = new Window
        {
            Title = UiText.Get("TableLoc_PasteSpecialTitle"),
            Width = 320,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PasteSpecialDialog");

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
        };

        root.Children.Add(new TextBlock
        {
            Text = UiText.Get("TableLoc_PasteSpecialPasteLabel"),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        });

        var options = new (PasteCellsMode Mode, string Label, string AutomationId)[]
        {
            (PasteCellsMode.All, UiText.Get("TableLoc_PasteSpecialAll"), "PasteSpecialAllRadio"),
            (PasteCellsMode.Values, UiText.Get("TableLoc_PasteSpecialValues"), "PasteSpecialValuesRadio"),
            (PasteCellsMode.Formulas, UiText.Get("TableLoc_PasteSpecialFormulas"), "PasteSpecialFormulasRadio"),
            (PasteCellsMode.Formats, UiText.Get("TableLoc_PasteSpecialFormats"), "PasteSpecialFormatsRadio"),
        };

        var radios = new List<RadioButton>();
        var optionsPanel = new StackPanel { Spacing = 6 };
        foreach (var option in options)
        {
            var radio = new RadioButton
            {
                Content = option.Label,
                GroupName = "PasteSpecialMode",
                IsChecked = option.Mode == PasteCellsMode.All,
                Tag = option.Mode,
            };
            ApplyDataOpsRadioButtonChrome(radio);
            AutomationProperties.SetAutomationId(radio, option.AutomationId);
            radios.Add(radio);
            optionsPanel.Children.Add(radio);
        }

        root.Children.Add(optionsPanel);

        var okButton = new Button
        {
            Content = UiText.Get("TableLoc_OK"),
            MinWidth = 82,
            IsDefault = true,
        };
        ApplyDataOpsButtonChrome(okButton, isDefault: true);
        AutomationProperties.SetAutomationId(okButton, "PasteSpecialOkButton");
        okButton.Click += (_, _) =>
        {
            var selected = radios.FirstOrDefault(r => r.IsChecked == true);
            if (selected?.Tag is PasteCellsMode mode)
            {
                var label = options.First(o => o.Mode == mode).Label;
                result = new PasteSpecialModeChoice(mode, label);
            }

            dialog.Close();
        };

        var cancelButton = new Button
        {
            Content = UiText.Get("TableLoc_Cancel"),
            MinWidth = 82,
            IsCancel = true,
        };
        ApplyDataOpsButtonChrome(cancelButton);
        AutomationProperties.SetAutomationId(cancelButton, "PasteSpecialCancelButton");
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
            Margin = new Thickness(0, 8, 0, 0),
        };
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);
        root.Children.Add(buttonRow);

        dialog.Content = root;
        dialog.Opened += (_, _) => okButton.Focus();
        await dialog.ShowDialog(this);
        return result;
    }
}
