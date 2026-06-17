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
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue("Select two or more cells to merge.");
            return;
        }

        var contentResolution = MergeCellContentResolution.KeepFirstCell;
        var contentPlan = CellMergePlanner.AnalyzeContent(_session.ActiveSheet, range);
        if (contentPlan.WouldLoseContent)
        {
            var choice = await ShowMergeCellsContentWarningDialogAsync(contentPlan);
            if (choice == MergeCellsWarningChoice.Cancel)
            {
                RefreshShell(_statusText.Text ?? "Ready");
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
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Merge Cells failed.");
            return;
        }

        RefreshShell($"Merged {rangeReference}");
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
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue("Select two or more columns to merge across.");
            return;
        }

        var sheet = _session.ActiveSheet;
        var sheetId = sheet.Id;

        // Build one horizontal merge per row. Each per-row range spans the full selected column span.
        var rowCommands = new List<IWorkbookCommand>();
        for (var row = range.Start.Row; row <= range.End.Row; row++)
        {
            var rowRange = new GridRange(
                new CellAddress(sheetId, row, range.Start.Col),
                new CellAddress(sheetId, row, range.End.Col));

            // Per-row merge with no centering. KeepFirstCell mirrors Excel's Merge Across, which keeps
            // each row's left-most value and discards the rest without prompting per row.
            rowCommands.Add(BuildMergeWithoutCenterCommand(
                sheet, sheetId, rowRange, MergeCellContentResolution.KeepFirstCell));
        }

        if (rowCommands.Count == 0)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            return;
        }

        var rangeReference = FormatRangeReference(range);
        var command = new CompositeWorkbookCommand("Merge Across", rowCommands);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            RefreshShell(_statusText.Text ?? "Ready");
            ShowEditIssue(result.ErrorMessage ?? "Merge Across failed.");
            return;
        }

        RefreshShell($"Merged across {rangeReference}");
    }

    /// <summary>
    /// Builds the command(s) to merge <paramref name="range"/> into one region WITHOUT re-centering.
    /// This deliberately omits the center <see cref="ApplyStyleCommand"/> that
    /// <see cref="FreeX.App.Services.CellMergePlanner.CreateMergeAndCenterCommands(Sheet?, SheetId, GridRange, MergeCellContentResolution)"/>
    /// appends, while reusing the same concatenation logic. A degenerate (single-cell) range produces a
    /// no-op composite, which the edit service treats as a successful empty command.
    /// </summary>
    private static IWorkbookCommand BuildMergeWithoutCenterCommand(
        Sheet sheet,
        SheetId sheetId,
        GridRange range,
        MergeCellContentResolution contentResolution)
    {
        var commands = new List<IWorkbookCommand>();

        if (contentResolution == MergeCellContentResolution.ConcatenateAllCells)
        {
            var contentPlan = CellMergePlanner.AnalyzeContent(sheet, range);
            if (!string.IsNullOrEmpty(contentPlan.ConcatenatedText))
                commands.Add(EditCellsCommand.ForValue(sheetId, range.Start, new TextValue(contentPlan.ConcatenatedText)));
        }

        if (range.CellCount > 1)
            commands.Add(new MergeCellsCommand(sheetId, range));

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
            RefreshShell(_statusText.Text ?? "Ready");
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
            Title = "Paste Special",
            Width = 320,
            Height = 280,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        AutomationProperties.SetAutomationId(dialog, "PasteSpecialDialog");

        var root = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 12
        };

        root.Children.Add(new TextBlock
        {
            Text = "Paste",
            FontWeight = FontWeight.SemiBold
        });

        var options = new (PasteCellsMode Mode, string Label, string AutomationId)[]
        {
            (PasteCellsMode.All, "All", "PasteSpecialAllRadio"),
            (PasteCellsMode.Values, "Values", "PasteSpecialValuesRadio"),
            (PasteCellsMode.Formulas, "Formulas", "PasteSpecialFormulasRadio"),
            (PasteCellsMode.Formats, "Formats", "PasteSpecialFormatsRadio")
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
                Tag = option.Mode
            };
            AutomationProperties.SetAutomationId(radio, option.AutomationId);
            radios.Add(radio);
            optionsPanel.Children.Add(radio);
        }

        root.Children.Add(optionsPanel);

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 82,
            IsDefault = true
        };
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
            Content = "Cancel",
            MinWidth = 82,
            IsCancel = true
        };
        AutomationProperties.SetAutomationId(cancelButton, "PasteSpecialCancelButton");
        cancelButton.Click += (_, _) =>
        {
            result = null;
            dialog.Close();
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
