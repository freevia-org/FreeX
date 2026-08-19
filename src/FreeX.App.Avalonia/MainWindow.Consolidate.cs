using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Consolidate;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle DataOpsDialogChromeStyle => new(FormulaBarFontFamily);
    private static AvaloniaCompactDialogChromeStyle ConsolidateDialogChromeStyle =>
        DataOpsDialogChromeStyle with
        {
            ControlHeight = 20,
            ButtonHeight = 20,
            ButtonPadding = new Thickness(4, 1),
        };
    private static AvaloniaCompactDialogChromeStyle ConsolidateFunctionChromeStyle =>
        ConsolidateDialogChromeStyle with { ControlHeight = 22 };

    /// <summary>Opens the Consolidate dialog (invoked from the Data menu and the Data-tab ribbon button).</summary>
    private void Consolidate() => RunGuarded(() => ShowConsolidateDialogAsync());

    /// <summary>
    /// The compact Consolidate dialog: pick an aggregation function, add one or more source ranges (typed as
    /// <c>Sheet!A1:B5</c>-style references and resolved through <see cref="WorkbookReferenceNavigator"/>),
    /// choose a destination anchor (defaulting to the active selection's top-left), and toggle "Use labels in
    /// Top row" / "Left column". On Apply the shared planner reads the source ranges, plans the output, and
    /// the result is applied through the shared session command path (undoable + refreshing). Overwriting non-empty
    /// destination cells requires a second Apply click.
    /// </summary>
    private async Task ShowConsolidateDialogAsync(ConsolidateDialogInitialState? initialState = null)
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var dialog = new Window
        {
            Title = UiText.Get("TableLoc_ConsolidateDialogTitle"),
            Width = ConsolidateDialogPlanner.CaptureWidth,
            Height = ConsolidateDialogPlanner.CaptureHeight,
            MinWidth = ConsolidateDialogPlanner.MinWidth,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, FreeXAutomationIdCatalog.Consolidate.Dialog);

        var functionBox = new ComboBox
        {
            ItemsSource = ConsolidateDialogPlanner.FunctionChoices.Select(c => c.Label).ToList(),
            SelectedIndex = 0,
            MinWidth = 160,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        ApplyConsolidateFunctionComboBoxChrome(functionBox);
        functionBox.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetAutomationId(functionBox, FreeXAutomationIdCatalog.Consolidate.FunctionBox);
        AutomationProperties.SetName(functionBox, StripDisplayMnemonic(UiText.Get("Consolidate_FunctionAutomationName")));
        AutomationProperties.SetHelpText(functionBox, StripDisplayMnemonic(UiText.Get("Consolidate_ChooseTheFunctionUsedToCombineSourceRanges")));

        var selectedRange = _session.SelectedRange;
        var defaultSource = initialState?.SourceReference ?? FormatRangeReference(selectedRange);
        var defaultDestination = initialState?.DestinationReference ?? FormatCellReference(selectedRange.Start);
        var referenceBox = new TextBox
        {
            Text = defaultSource,
            PlaceholderText = UiText.Get("TableLoc_ConsolidateReferencePlaceholder"),
            MinWidth = 220,
        };
        ApplyConsolidateTextBoxChrome(referenceBox);
        AutomationProperties.SetAutomationId(referenceBox, FreeXAutomationIdCatalog.Consolidate.ReferenceBox);
        AutomationProperties.SetName(referenceBox, StripDisplayMnemonic(UiText.Get("Consolidate_Reference2")));
        AutomationProperties.SetHelpText(referenceBox, StripDisplayMnemonic(UiText.Get("Consolidate_EnterASourceRangeToAddToTheAllReferencesList")));

        // Windows places an ellipsis ("...") range-picker next to the Reference field (matches the WPF host's
        // DialogReferencePicker which uses a literal "..." button, width 28, docked left of the text box).
        var browseButton = new Button { Content = "...", Width = 28, MinWidth = 28 };
        ApplyDataOpsRangePickerButtonChrome(browseButton);
        AutomationProperties.SetAutomationId(browseButton, FreeXAutomationIdCatalog.Consolidate.BrowseReferenceButton);
        AutomationProperties.SetName(browseButton, StripDisplayMnemonic(UiText.Get("Consolidate_SelectReferenceRange")));

        var referencesList = new ListBox
        {
            MinHeight = ConsolidateDialogPlanner.ReferencesListHeight,
            Height = ConsolidateDialogPlanner.ReferencesListHeight,
        };
        ApplyDataOpsListBoxChrome(referencesList);
        AutomationProperties.SetAutomationId(referencesList, FreeXAutomationIdCatalog.Consolidate.AllReferencesList);
        AutomationProperties.SetName(referencesList, StripDisplayMnemonic(UiText.Get("Consolidate_AllReferences2")));
        AutomationProperties.SetHelpText(referencesList, StripDisplayMnemonic(UiText.Get("Consolidate_ListsTheSourceRangesThatWillBeConsolidated")));

        var addButton = new Button { Content = StripDisplayMnemonic(UiText.Get("Consolidate_Add")), MinWidth = 76 };
        ApplyConsolidateButtonChrome(addButton);
        AutomationProperties.SetAutomationId(addButton, FreeXAutomationIdCatalog.Consolidate.AddReferenceButton);
        AutomationProperties.SetName(addButton, StripDisplayMnemonic(UiText.Get("Consolidate_AddReferenceAutomationName")));
        AutomationProperties.SetHelpText(addButton, StripDisplayMnemonic(UiText.Get("Consolidate_AddTheReferenceRangeToTheAllReferencesList")));
        var removeButton = new Button { Content = StripDisplayMnemonic(UiText.Get("Consolidate_Delete")), MinWidth = 76, IsEnabled = false };
        ApplyConsolidateButtonChrome(removeButton);
        AutomationProperties.SetAutomationId(removeButton, FreeXAutomationIdCatalog.Consolidate.DeleteReferenceButton);
        AutomationProperties.SetName(removeButton, StripDisplayMnemonic(UiText.Get("Consolidate_DeleteReferenceAutomationName")));
        AutomationProperties.SetHelpText(removeButton, StripDisplayMnemonic(UiText.Get("Consolidate_DeleteTheSelectedReferenceRange")));

        var destinationBox = new TextBox
        {
            Text = defaultDestination,
            MinWidth = 220,
        };
        ApplyConsolidateTextBoxChrome(destinationBox);
        AutomationProperties.SetAutomationId(destinationBox, FreeXAutomationIdCatalog.Consolidate.DestinationCellBox);
        AutomationProperties.SetName(destinationBox, StripDisplayMnemonic(UiText.Get("Consolidate_DestinationCell2")));
        AutomationProperties.SetHelpText(destinationBox, StripDisplayMnemonic(UiText.Get("Consolidate_EnterTheUpperLeftDestinationCellForTheConsolidatedResult")));

        var destinationBrowseButton = new Button { Content = "...", Width = 28, MinWidth = 28 };
        ApplyDataOpsRangePickerButtonChrome(destinationBrowseButton);
        AutomationProperties.SetAutomationId(destinationBrowseButton, FreeXAutomationIdCatalog.Consolidate.BrowseDestinationButton);
        AutomationProperties.SetName(destinationBrowseButton, StripDisplayMnemonic(UiText.Get("Consolidate_SelectDestinationCell")));

        var topRowBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("Consolidate_TopRow")) };
        ApplyDataOpsCheckBoxChrome(topRowBox);
        AutomationProperties.SetAutomationId(topRowBox, FreeXAutomationIdCatalog.Consolidate.TopRowLabelsBox);
        AutomationProperties.SetName(topRowBox, StripDisplayMnemonic(UiText.Get("Consolidate_TopRowLabelsAutomationName")));
        AutomationProperties.SetHelpText(topRowBox, StripDisplayMnemonic(UiText.Get("Consolidate_UseLabelsFromTheTopRowOfEachSourceRange")));
        var leftColumnBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("Consolidate_LeftColumn")) };
        ApplyDataOpsCheckBoxChrome(leftColumnBox);
        AutomationProperties.SetAutomationId(leftColumnBox, FreeXAutomationIdCatalog.Consolidate.LeftColumnLabelsBox);
        AutomationProperties.SetName(leftColumnBox, StripDisplayMnemonic(UiText.Get("Consolidate_LeftColumnLabelsAutomationName")));
        AutomationProperties.SetHelpText(leftColumnBox, StripDisplayMnemonic(UiText.Get("Consolidate_UseLabelsFromTheLeftColumnOfEachSourceRange")));
        // WPF has a "Create links to source data" checkbox below the Use labels row
        var createLinksBox = new CheckBox { Content = StripDisplayMnemonic(UiText.Get("Consolidate_CreateLinksToSourceData")) };
        ApplyDataOpsCheckBoxChrome(createLinksBox);
        createLinksBox.Margin = new Thickness(0, 0, 0, 12);
        AutomationProperties.SetAutomationId(createLinksBox, FreeXAutomationIdCatalog.Consolidate.CreateLinksBox);
        AutomationProperties.SetName(createLinksBox, StripDisplayMnemonic(UiText.Get("Consolidate_CreateLinksToSourceDataAutomationName")));
        AutomationProperties.SetHelpText(createLinksBox, StripDisplayMnemonic(UiText.Get("Consolidate_CreateFormulasThatLinkTheResultToTheSourceCells")));

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, FreeXAutomationIdCatalog.Consolidate.WarningText);

        var references = ConsolidateDialogPlanner.SplitSourceRangeText(defaultSource).ToList();
        var overwriteConfirmed = false;

        void RefreshReferences()
        {
            referencesList.ItemsSource = references.ToList();
            overwriteConfirmed = false;
        }

        RefreshReferences();

        referencesList.SelectionChanged += (_, _) =>
            removeButton.IsEnabled = referencesList.SelectedItem is not null;

        addButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;
            var text = referenceBox.Text?.Trim() ?? string.Empty;
            if (!ConsolidateDialogPlanner.TryAddReference(
                    references,
                    text,
                    TryParseConsolidateSourceRanges,
                    // WPF intentionally accepts a duplicate source entry. Keep the portable dialog on
                    // the same side of this product decision rather than introducing a platform-only rule.
                    rejectDuplicateReferences: false,
                    out var updatedReferences,
                    out var issue))
            {
                warningText.Text = ConsolidateDialogPlanner
                    .DescribeIssue(
                        issue,
                        ConsolidateDialogMessageContext.AddReference)
                    .Message
                    .Resolve(UiText.Get, UiText.Format);
                warningText.IsVisible = true;
                return;
            }

            references.Clear();
            references.AddRange(updatedReferences);
            RefreshReferences();
            referenceBox.Clear();
        };

        removeButton.Click += (_, _) =>
        {
            if (referencesList.SelectedItem is string selected)
            {
                references.Remove(selected);
                RefreshReferences();
            }
        };

        var applyButton = new Button { Content = UiText.Ok, IsDefault = true, MinWidth = 72 };
        ApplyConsolidateButtonChrome(applyButton, isDefault: true);
        AutomationProperties.SetAutomationId(applyButton, FreeXAutomationIdCatalog.Consolidate.ApplyButton);
        var cancelButton = new Button { Content = UiText.Cancel, IsCancel = true, MinWidth = 72 };
        ApplyConsolidateButtonChrome(cancelButton);
        AutomationProperties.SetAutomationId(cancelButton, FreeXAutomationIdCatalog.Consolidate.CancelButton);

        applyButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;

            if (ConsolidateDialogPlanner.HasPendingReferenceText(references, referenceBox.Text))
            {
                warningText.Text = ConsolidateDialogPlanner
                    .DescribePendingReference()
                    .Message
                    .Resolve(UiText.Get, UiText.Format);
                warningText.IsVisible = true;
                referenceBox.Focus();
                referenceBox.SelectAll();
                return;
            }

            var options = new ConsolidateOptions
            {
                Function = ConsolidateDialogPlanner.FunctionChoices[Math.Max(0, functionBox.SelectedIndex)].Function,
                UseTopRowLabels = topRowBox.IsChecked == true,
                UseLeftColumnLabels = leftColumnBox.IsChecked == true,
            };

            var plan = ConsolidateApplicationWorkflow.Plan(
                _session.Workbook,
                references,
                destinationBox.Text?.Trim() ?? string.Empty,
                TryParseConsolidateReference,
                options,
                createLinksBox.IsChecked == true,
                overwriteConfirmed);
            if (plan.Disposition == ConsolidateApplicationDisposition.Invalid)
            {
                warningText.Text = ConsolidateDialogPlanner
                    .DescribeIssue(
                        plan.Issue,
                        ConsolidateDialogMessageContext.FinalValidation)
                    .Message
                    .Resolve(UiText.Get, UiText.Format);
                warningText.IsVisible = true;
                return;
            }

            if (plan.Disposition == ConsolidateApplicationDisposition.ConfirmOverwrite)
            {
                overwriteConfirmed = true;
                warningText.Text = ConsolidateApplicationWorkflow
                    .DescribeOverwriteConfirmation(plan)
                    .Resolve(UiText.Get, UiText.Format);
                warningText.IsVisible = true;
                return;
            }

            var outcome = ConsolidateApplicationWorkflow.Execute(
                plan,
                commandFactory =>
                {
                    var result = _session.ExecuteReviewCommand(commandFactory());
                    return new ConsolidateCommandAdapterResult(result.Success, result.ErrorMessage);
                });
            if (!outcome.Success)
            {
                ShowEditIssue(ConsolidateApplicationWorkflow
                    .DescribeFailure(outcome)
                    .Resolve(UiText.Get, UiText.Format));
                return;
            }

            SelectCell(outcome.DestinationCell);
            RefreshShell(ConsolidateApplicationWorkflow
                .DescribeSuccess(outcome)
                .Resolve(UiText.Get, UiText.Format));

            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        // Windows: "[...] <Reference textbox>" — Browse (ellipsis) sits left of the reference field.
        var referenceRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(browseButton, Dock.Left);
        browseButton.Margin = new Thickness(0, 0, 6, 0);
        referenceRow.Children.Add(browseButton);
        referenceRow.Children.Add(referenceBox);

        // Windows: Add / Delete buttons sit between the Reference field and the "All references" list,
        // right-aligned side by side (matches the WPF ConsolidateDialog layout / win.png ground truth).
        var addRemoveRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [addButton, removeButton],
            new Thickness(0, 6, 0, 13));

        // Windows: "[...] <Destination textbox>" — Browse (ellipsis) sits left of the destination field.
        var destinationRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(destinationBrowseButton, Dock.Left);
        destinationBrowseButton.Margin = new Thickness(0, 0, 6, 0);
        destinationRow.Children.Add(destinationBrowseButton);
        destinationRow.Children.Add(destinationBox);

        var useLabelsText = new TextBlock
        {
            Text = StripDisplayMnemonic(UiText.Get("Consolidate_UseLabelsIn")),
            Margin = new Thickness(0, 8, 0, 2),
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        var labelOptions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 1),
            Children =
            {
                topRowBox,
                leftColumnBox,
            },
        };
        topRowBox.Margin = new Thickness(0, 0, 16, 0);

        // WPF button order: [OK][Cancel] — primary on left; the Apply button maps to WPF OK
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([applyButton, cancelButton], new Thickness(0, 12, 0, 0));

        var root = new DockPanel
        {
            Margin = new Thickness(12, 10, 12, 10),
            Width = ConsolidateDialogPlanner.CaptureContentWidth,
            Height = ConsolidateDialogPlanner.CaptureContentHeight,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
            VerticalAlignment = AvaloniaVerticalAlignment.Top,
            Children =
            {
                new ScrollViewer
                {
                    Width = ConsolidateDialogPlanner.CaptureContentWidth,
                    Content = new StackPanel
                    {
                        Width = ConsolidateDialogPlanner.CaptureContentWidth,
                        Spacing = 0,
                        Margin = new Thickness(0, 4, 0, 0),
                        Children =
                        {
                            new TextBlock { Text = StripDisplayMnemonic(UiText.Get("Consolidate_Function")), Margin = new Thickness(0, 0, 0, 2), FontSize = 12, FontFamily = FormulaBarFontFamily },
                            functionBox,
                            new TextBlock { Text = StripDisplayMnemonic(UiText.Get("Consolidate_Reference")), FontSize = 12, FontFamily = FormulaBarFontFamily },
                            referenceRow,
                            addRemoveRow,
                            new TextBlock { Text = StripDisplayMnemonic(UiText.Get("Consolidate_AllReferences")), Foreground = HeaderForeground, FontSize = 12, FontFamily = FormulaBarFontFamily },
                            referencesList,
                            new TextBlock { Text = StripDisplayMnemonic(UiText.Get("Consolidate_DestinationCell")), Margin = new Thickness(0, 8, 0, 0), FontSize = 12, FontFamily = FormulaBarFontFamily },
                            destinationRow,
                            useLabelsText,
                            labelOptions,
                            createLinksBox,
                            warningText,
                            buttonRow,
                        },
                    },
                },
            },
        };
        dialog.Content = root;
        AttachDialogRangePicker(dialog, browseButton, referenceBox, "range.consolidate.reference");
        AttachDialogRangePicker(dialog, destinationBrowseButton, destinationBox, "range.consolidate.destination-cell");

        // WPF opens Consolidate with the Function combo box focused. Use the shared native-dialog
        // retry helper because the X11 window can finish realizing after ShowDialog starts, then
        // give the authored controls the same closed Tab graph and Escape contract as WPF.
        ConfigureDialogTabCycle(dialog, root);
        ConfigureNativeDialogInitialFocus(dialog, root, functionBox);

        await dialog.ShowDialog(this);
    }

    /// <summary>
    /// Parses a Consolidate source/destination reference (a cell, an <c>A1:B5</c> range, a sheet-qualified
    /// <c>Sheet!A1:B5</c> range, or a defined name) into a <see cref="GridRange"/>, resolving sheet names
    /// against the workbook and defaulting to the active sheet.
    /// </summary>
    private bool TryParseConsolidateReference(string text, out GridRange range) =>
        WorkbookReferenceNavigator.TryParseReferenceRange(
            text,
            _session.ActiveSheet.Id,
            name => _session.Workbook.GetSheet(name)?.Id,
            _session.Workbook.NamedRanges,
            out range);

    private bool TryParseConsolidateSourceRanges(
        string text,
        out IReadOnlyList<GridRange> ranges,
        out string? invalidPart)
    {
        if (TryParseConsolidateReference(text, out var range))
        {
            ranges = [range];
            invalidPart = null;
            return true;
        }

        ranges = [];
        invalidPart = text;
        return false;
    }

    // ── Shared data-operations dialog chrome helpers ───────────────────────────
    // These mirror the SelectionPane helpers (MainWindow.SelectionPane.cs) and apply the
    // Windows WPF visual spec to all data-operation dialogs: Consolidate, AllowEditRange,
    // FillSeries, and PasteSpecial.

    /// <summary>
    /// Applies standard button chrome: Height=24, Padding=(4,1), white background,
    /// Brush(112,112,112) border (or Brush(0,120,215) for default buttons), FontSize=12,
    /// FontFamily=FormulaBarFontFamily.
    /// </summary>
    private static void ApplyDataOpsButtonChrome(Button button, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, DataOpsDialogChromeStyle, button.MinWidth, isDefault);

    private static void ApplyDataOpsRangePickerButtonChrome(Button button)
    {
        ApplyConsolidateButtonChrome(button);
        button.Padding = new Thickness(0, 1);
    }

    private static void ApplyConsolidateButtonChrome(Button button, bool isDefault = false)
        => AvaloniaCompactDialogChrome.ApplyButton(button, ConsolidateDialogChromeStyle, button.MinWidth, isDefault);

    private static void ApplyConsolidateTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, ConsolidateDialogChromeStyle);

    private static void ApplyConsolidateFunctionComboBoxChrome(ComboBox comboBox)
        => AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, ConsolidateFunctionChromeStyle);

    /// <summary>
    /// Applies standard text box chrome: Height=24, Padding=(4,1), FontSize=12,
    /// Brush(130,130,130) border, BorderThickness=1.
    /// </summary>
    private static void ApplyDataOpsTextBoxChrome(TextBox textBox)
        => AvaloniaCompactDialogChrome.ApplyTextBox(textBox, DataOpsDialogChromeStyle);

    /// <summary>
    /// Applies standard combo box chrome: Height=24, Padding=(5,0,4,0), FontSize=12,
    /// Brush(130,130,130) border, BorderThickness=1.
    /// </summary>
    private static void ApplyDataOpsComboBoxChrome(ComboBox comboBox)
        => AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, DataOpsDialogChromeStyle);

    /// <summary>
    /// Applies standard list-box row chrome: MinHeight=24 per row, FontSize=12.
    /// </summary>
    private static void ApplyDataOpsListBoxChrome(ListBox listBox)
        => AvaloniaCompactDialogChrome.ApplyListBox(listBox, DataOpsDialogChromeStyle);

    /// <summary>
    /// Applies standard check box chrome: MinHeight=20, MaxHeight=20, FontSize=12,
    /// FontFamily=FormulaBarFontFamily.
    /// </summary>
    private static void ApplyDataOpsCheckBoxChrome(CheckBox checkBox)
    {
        StripContentMnemonic(checkBox);
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
        AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, DataOpsDialogChromeStyle);
    }

    /// <summary>
    /// Applies standard radio button chrome: MinHeight=20, MaxHeight=20, FontSize=12,
    /// FontFamily=FormulaBarFontFamily.
    /// </summary>
    private static void ApplyDataOpsRadioButtonChrome(RadioButton radioButton)
    {
        StripContentMnemonic(radioButton);
        radioButton.MinHeight = 20;
        radioButton.MaxHeight = 20;
        AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, DataOpsDialogChromeStyle);
    }
}
