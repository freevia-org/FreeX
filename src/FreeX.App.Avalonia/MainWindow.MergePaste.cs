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
        var contentPlan = CellMergePlanner.AnalyzeContent(_session.ActiveSheet, areas);
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
        var areaCommands = areas
            .Select(area => BuildMergeWithoutCenterCommand(_session.ActiveSheet, sheetId, area, contentResolution))
            .ToList();
        var command = areaCommands.Count == 1
            ? areaCommands[0]
            : new CompositeWorkbookCommand("Merge Cells", areaCommands);
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

        var sheet = _session.ActiveSheet;
        var sheetId = sheet.Id;

        // Analyze the WHOLE selection once up front, matching the WPF host's
        // TryResolveMergeContentResolution and this file's own MergeSelectedRangeAsync above, so
        // multi-cell rows (e.g. A1:C1 = "Jan"/"Feb"/"Mar") get the "Merging cells only keeps the
        // upper-leftmost value" confirmation instead of the per-row merges below silently discarding
        // every non-left-most value in each row with zero warning.
        //
        // R127-avalonia-mergepaste-multiarea-2 (data-loss fix): analyze EVERY disjoint area in `areas`,
        // not just the active `range` -- a Ctrl+click area other than the active one can hold content
        // that the per-area per-row loop below is about to discard with no warning at all.
        var contentResolution = MergeCellContentResolution.KeepFirstCell;
        var contentPlan = CellMergePlanner.AnalyzeContent(sheet, areas, perRow: true);
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

        // Build one composite per disjoint area, each containing one horizontal per-row merge for
        // that area's own column span. An area that is itself single-column contributes nothing
        // (BuildMergeWithoutCenterCommand degrades to a no-op composite for a CellCount<=1 range).
        var areaCommands = new List<IWorkbookCommand>();
        foreach (var area in areas)
        {
            if (area.ColCount <= 1)
                continue;

            var rowCommands = new List<IWorkbookCommand>();
            for (var row = area.Start.Row; row <= area.End.Row; row++)
            {
                var rowRange = new GridRange(
                    new CellAddress(sheetId, row, area.Start.Col),
                    new CellAddress(sheetId, row, area.End.Col));

                // Per-row merge with no centering, using the resolution chosen (once) above. Pass
                // allowUnmergeToggle: false so an already-merged row of the exact target shape is left
                // merged (a no-op re-merge) instead of being toggled back off by this per-row re-invocation.
                rowCommands.Add(BuildMergeWithoutCenterCommand(
                    sheet, sheetId, rowRange, contentResolution, allowUnmergeToggle: false));
            }

            if (rowCommands.Count > 0)
                areaCommands.Add(rowCommands.Count == 1 ? rowCommands[0] : new CompositeWorkbookCommand("Merge Across", rowCommands));
        }

        if (areaCommands.Count == 0)
        {
            RefreshShell(_statusText.Text ?? UiText.Get("TableLoc_Ready"));
            return;
        }

        var rangeReference = FormatRangeReference(range);
        var command = areaCommands.Count == 1
            ? areaCommands[0]
            : new CompositeWorkbookCommand("Merge Across", areaCommands);
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
    /// (mergeCells: true) rather than duplicating its merge/concatenate logic here. For the direct
    /// "Merge Cells" gesture (the <paramref name="allowUnmergeToggle"/> default of <c>true</c>),
    /// re-invoking on a selection that is already fully covered by an existing merged region unmerges it
    /// instead of failing with "Range overlaps an existing merged region.". The per-row loop in
    /// <see cref="MergeAcrossSelectedRangeAsync"/> passes <c>allowUnmergeToggle: false</c> instead, since
    /// "Merge Across" must always leave the selection uniformly merged per row -- an already-merged row
    /// of the exact target shape falls through to a no-op re-merge rather than being toggled back off.
    /// The center <see cref="ApplyStyleCommand"/> that
    /// <see cref="FreeX.App.Services.CellMergePlanner.CreateMergeAndCenterCommands(Sheet?, SheetId, GridRange, MergeCellContentResolution)"/>
    /// appends is filtered out by CreateFormatCellsMergeCommands for the concatenate path, and never
    /// added on the keep-first-cell path, so no re-centering leaks in here. A degenerate (single-cell)
    /// range produces a no-op composite, which the edit service treats as a successful empty command.
    /// </summary>
    private static IWorkbookCommand BuildMergeWithoutCenterCommand(
        Sheet sheet,
        SheetId sheetId,
        GridRange range,
        MergeCellContentResolution contentResolution,
        bool allowUnmergeToggle = true)
    {
        var commands = CellMergePlanner.CreateFormatCellsMergeCommands(
            sheet, sheetId, range, mergeCells: true, contentResolution, allowUnmergeToggle);

        return commands.Count == 1
            ? commands[0]
            : new CompositeWorkbookCommand("Merge Cells", commands);
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
        if (PasteSpecialWorkflowOverrideForTest is { } workflowOverride)
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

        var option = selection.Option;
        switch (option.Kind)
        {
            case PasteSpecialDialogActionKind.Comments:
                await PasteCommentsFromClipboardAsync(option.Label);
                return;
            case PasteSpecialDialogActionKind.Validation:
                await PasteDataValidationFromClipboardAsync(option.Label);
                return;
            case PasteSpecialDialogActionKind.ColumnWidths:
                await PasteColumnWidthsFromClipboardAsync(option.Label);
                return;
            case PasteSpecialDialogActionKind.Text:
            case PasteSpecialDialogActionKind.UnicodeText:
                await PasteSpecialExternalTextFromClipboardAsync(option.Label);
                return;
            case PasteSpecialDialogActionKind.Picture:
                await PastePictureFromClipboardAsync(option.Label, linkedPicture: false);
                return;
            case PasteSpecialDialogActionKind.LinkedPicture:
                await PastePictureFromClipboardAsync(option.Label, linkedPicture: true);
                return;
            case PasteSpecialDialogActionKind.Link:
                await PasteLinkFromClipboardAsync(option.Label);
                return;
            default:
                var pasteOptions = new PasteSpecialOptions(
                    Transpose: selection.Transpose,
                    Operation: selection.Operation,
                    SkipBlanks: selection.SkipBlanks,
                    ContentKind: option.ContentKind);
                await PasteSpecialClipboardTextAsync(option.Mode, pasteOptions, option.Label, selection.KeepSourceColumnWidths);
                return;
        }
    }

    internal Func<Task>? PasteSpecialWorkflowOverrideForTest { get; set; }

    /// <summary>
    /// Which existing execution method a content-kind radio in the ribbon's Paste Special dialog routes
    /// to. <see cref="Cells"/> is the composable family (goes through <see cref="PasteSpecialClipboardTextAsync"/>
    /// together with the Skip Blanks/Transpose/Keep Source Column Widths checkboxes and the Operation
    /// group); every other member is a fixed, non-composable action mirroring one submenu item in
    /// <see cref="CreatePasteSpecialMenuItems"/>.
    /// </summary>
    internal enum PasteSpecialDialogActionKind
    {
        Cells,
        Comments,
        Validation,
        ColumnWidths,
        Text,
        UnicodeText,
        Picture,
        LinkedPicture,
        Link,
    }

    internal sealed record PasteSpecialDialogOption(
        string Label,
        string AutomationId,
        PasteSpecialDialogActionKind Kind,
        PasteCellsMode Mode = PasteCellsMode.All,
        PasteSpecialContentKind ContentKind = PasteSpecialContentKind.Default);

    internal sealed record PasteSpecialDialogSelection(
        PasteSpecialDialogOption Option,
        bool SkipBlanks,
        bool Transpose,
        bool KeepSourceColumnWidths,
        PasteSpecialOperation Operation);

    /// <summary>
    /// Test-only hook (matching the established SmokeProbe convention used by
    /// <c>ShowFormatCellsInputDialogAsync</c>/<c>ShowFindDialogAsync</c>/etc. in MainWindow.cs) exposing
    /// the real, production content-kind radios / checkboxes / operation radios / footer buttons of the
    /// ribbon's Paste Special dialog so a headless test can drive them directly -- the OS clipboard itself
    /// (<c>IClipboard</c>) is <c>[NotClientImplementable]</c> so cannot be doubled in a headless test (see
    /// the R66/R68 rationale on <see cref="TryGetClipboardTextAsync"/>), but this probe fires from the
    /// dialog's own <c>Opened</c> event, before any clipboard access happens, so it can still exercise the
    /// real dialog end-to-end for everything up to (and including) the OK-click selection decision.
    /// </summary>
    internal sealed record PasteSpecialDialogSmokeProbe(
        Window Dialog,
        IReadOnlyList<RadioButton> ContentRadios,
        CheckBox SkipBlanksBox,
        CheckBox TransposeBox,
        CheckBox KeepColumnWidthsBox,
        IReadOnlyList<RadioButton> OperationRadios,
        Button PasteLinkButton,
        Button OkButton,
        Button CancelButton);

    /// <summary>
    /// The content-kind radio list for the ribbon's Paste Special dialog, in the same order as (and
    /// covering every entry of) the Paste split-button's Paste Special submenu built by
    /// <see cref="CreatePasteSpecialMenuItems"/>, minus the Transpose / Skip Blanks / the four math
    /// Operation entries there -- this dialog exposes those as composable checkboxes and an Operation
    /// group instead (see <see cref="PromptPasteSpecialModeAsync"/>), so they can be combined with any
    /// <see cref="PasteSpecialDialogActionKind.Cells"/> content kind, matching Excel and
    /// <see cref="FreeX.App.Host.PasteSpecialDialog"/>.
    /// </summary>
    private static readonly PasteSpecialDialogOption[] PasteSpecialDialogOptions =
    [
        new("All", "PasteSpecialAllRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All),
        new("Values", "PasteSpecialValuesRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.Values),
        new("Formulas", "PasteSpecialFormulasRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.Formulas),
        new("Formats", "PasteSpecialFormatsRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.Formats),
        new("Comments and Notes", "PasteSpecialCommentsRadio", PasteSpecialDialogActionKind.Comments),
        new("Validation", "PasteSpecialValidationRadio", PasteSpecialDialogActionKind.Validation),
        new("All Except Borders", "PasteSpecialAllExceptBordersRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.AllExceptBorders),
        new("All Merging Conditional Formats", "PasteSpecialAllMergingConditionalFormatsRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.AllMergingConditionalFormats),
        new("Column Widths", "PasteSpecialColumnWidthsRadio", PasteSpecialDialogActionKind.ColumnWidths),
        new("Formulas and Number Formats", "PasteSpecialFormulasAndNumberFormatsRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.FormulasAndNumberFormats),
        new("Values and Number Formats", "PasteSpecialValuesAndNumberFormatsRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.ValuesAndNumberFormats),
        new("Values and Source Formatting", "PasteSpecialValuesAndSourceFormattingRadio", PasteSpecialDialogActionKind.Cells, PasteCellsMode.All, PasteSpecialContentKind.ValuesAndSourceFormatting),
        new("Text", "PasteSpecialTextRadio", PasteSpecialDialogActionKind.Text),
        new("Unicode Text", "PasteSpecialUnicodeTextRadio", PasteSpecialDialogActionKind.UnicodeText),
        new("Picture", "PasteSpecialPictureRadio", PasteSpecialDialogActionKind.Picture),
        new("Linked Picture", "PasteSpecialLinkedPictureRadio", PasteSpecialDialogActionKind.LinkedPicture),
    ];

    private static readonly PasteSpecialDialogOption PasteSpecialPasteLinkOption =
        new("Paste Link", "PasteSpecialPasteLinkButton", PasteSpecialDialogActionKind.Link);

    /// <summary>
    /// Shows the Paste Special dialog and returns the selected content kind plus checkbox/operation state,
    /// or <c>null</c> if cancelled. The "Paste Link" footer button (matching WPF/Excel) closes the dialog
    /// with a dedicated <see cref="PasteSpecialDialogActionKind.Link"/> selection regardless of which
    /// content-kind radio is checked, mirroring <see cref="CreatePasteSpecialMenuItems"/>'s standalone
    /// "Paste Link" submenu entry.
    /// </summary>
    internal async Task<PasteSpecialDialogSelection?> PromptPasteSpecialModeAsync(
        Action<PasteSpecialDialogSmokeProbe>? launchSmokeProbe = null)
    {
        PasteSpecialDialogSelection? result = null;

        var dialog = new Window
        {
            Title = UiText.Get("TableLoc_PasteSpecialTitle"),
            Width = 420,
            Height = 600,
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

        var radios = new List<RadioButton>();
        var optionsPanel = new StackPanel { Spacing = 4 };
        foreach (var option in PasteSpecialDialogOptions)
        {
            var radio = new RadioButton
            {
                Content = option.Label,
                GroupName = "PasteSpecialMode",
                IsChecked = option == PasteSpecialDialogOptions[0],
                Tag = option,
            };
            ApplyDataOpsRadioButtonChrome(radio);
            AutomationProperties.SetAutomationId(radio, option.AutomationId);
            radios.Add(radio);
            optionsPanel.Children.Add(radio);
        }

        root.Children.Add(new ScrollViewer
        {
            MaxHeight = 230,
            Content = optionsPanel,
        });

        var skipBlanksBox = new CheckBox { Content = "Skip Blanks" };
        ApplyDataOpsCheckBoxChrome(skipBlanksBox);
        AutomationProperties.SetAutomationId(skipBlanksBox, "PasteSpecialSkipBlanksBox");

        var transposeBox = new CheckBox { Content = "Transpose" };
        ApplyDataOpsCheckBoxChrome(transposeBox);
        AutomationProperties.SetAutomationId(transposeBox, "PasteSpecialTransposeBox");

        var keepColumnWidthsBox = new CheckBox { Content = "Keep Source Column Widths" };
        ApplyDataOpsCheckBoxChrome(keepColumnWidthsBox);
        AutomationProperties.SetAutomationId(keepColumnWidthsBox, "PasteSpecialKeepColumnWidthsBox");

        var checkboxPanel = new StackPanel { Spacing = 4 };
        checkboxPanel.Children.Add(skipBlanksBox);
        checkboxPanel.Children.Add(transposeBox);
        checkboxPanel.Children.Add(keepColumnWidthsBox);
        root.Children.Add(checkboxPanel);

        root.Children.Add(new TextBlock
        {
            Text = "Operation",
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        });

        var operationChoices = new (PasteSpecialOperation Operation, string Label, string AutomationId)[]
        {
            (PasteSpecialOperation.None, "None", "PasteSpecialOperationNoneRadio"),
            (PasteSpecialOperation.Add, "Add", "PasteSpecialOperationAddRadio"),
            (PasteSpecialOperation.Subtract, "Subtract", "PasteSpecialOperationSubtractRadio"),
            (PasteSpecialOperation.Multiply, "Multiply", "PasteSpecialOperationMultiplyRadio"),
            (PasteSpecialOperation.Divide, "Divide", "PasteSpecialOperationDivideRadio"),
        };

        var operationRadios = new List<RadioButton>();
        var operationGrid = new Grid();
        operationGrid.ColumnDefinitions.Add(new ColumnDefinition());
        operationGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < 3; i++)
            operationGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var i = 0; i < operationChoices.Length; i++)
        {
            var choice = operationChoices[i];
            var radio = new RadioButton
            {
                Content = choice.Label,
                GroupName = "PasteSpecialOperation",
                IsChecked = choice.Operation == PasteSpecialOperation.None,
                Tag = choice.Operation,
            };
            ApplyDataOpsRadioButtonChrome(radio);
            AutomationProperties.SetAutomationId(radio, choice.AutomationId);
            Grid.SetRow(radio, i / 2);
            Grid.SetColumn(radio, i % 2);
            operationRadios.Add(radio);
            operationGrid.Children.Add(radio);
        }

        root.Children.Add(operationGrid);

        var pasteLinkButton = new Button
        {
            Content = "Paste Link",
            MinWidth = 82,
        };
        ApplyDataOpsButtonChrome(pasteLinkButton);
        AutomationProperties.SetAutomationId(pasteLinkButton, "PasteSpecialPasteLinkButton");
        pasteLinkButton.Click += (_, _) =>
        {
            result = new PasteSpecialDialogSelection(
                PasteSpecialPasteLinkOption, SkipBlanks: false, Transpose: false, KeepSourceColumnWidths: false, Operation: PasteSpecialOperation.None);
            dialog.Close();
        };

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
            if (selected?.Tag is PasteSpecialDialogOption option)
            {
                var operation = PasteSpecialOperation.None;
                if (operationRadios.FirstOrDefault(r => r.IsChecked == true) is { Tag: PasteSpecialOperation selectedOperation })
                    operation = selectedOperation;

                result = new PasteSpecialDialogSelection(
                    option,
                    SkipBlanks: skipBlanksBox.IsChecked == true,
                    Transpose: transposeBox.IsChecked == true,
                    KeepSourceColumnWidths: keepColumnWidthsBox.IsChecked == true,
                    Operation: operation);
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
        buttonRow.Children.Add(pasteLinkButton);
        buttonRow.Children.Add(okButton);
        buttonRow.Children.Add(cancelButton);
        root.Children.Add(buttonRow);

        dialog.Content = root;
        dialog.Opened += (_, _) => okButton.Focus();
        if (launchSmokeProbe is not null)
        {
            dialog.Opened += (_, _) =>
            {
                RunLaunchSmokeDialogProbe(
                    dialog,
                    () => launchSmokeProbe(new PasteSpecialDialogSmokeProbe(
                        dialog, radios, skipBlanksBox, transposeBox, keepColumnWidthsBox, operationRadios, pasteLinkButton, okButton, cancelButton)));
            };
        }

        await dialog.ShowDialog(this);
        return result;
    }
}
