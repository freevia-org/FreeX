using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.Consolidate;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static AvaloniaCompactDialogChromeStyle DataOpsDialogChromeStyle => new(FormulaBarFontFamily);

    /// <summary>Opens the Consolidate dialog (invoked from the Data menu and the Data-tab ribbon button).</summary>
    private void Consolidate() => _ = ShowConsolidateDialogAsync();

    /// <summary>
    /// The compact Consolidate dialog: pick an aggregation function, add one or more source ranges (typed as
    /// <c>Sheet!A1:B5</c>-style references and resolved through <see cref="WorkbookReferenceNavigator"/>),
    /// choose a destination anchor (defaulting to the active selection's top-left), and toggle "Use labels in
    /// Top row" / "Left column". On Apply the shared planner reads the source ranges, plans the output, and
    /// the result is applied through the shared session command path (undoable + refreshing). Overwriting non-empty
    /// destination cells requires a second Apply click.
    /// </summary>
    private async Task ShowConsolidateDialogAsync()
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
        AutomationProperties.SetAutomationId(dialog, "ConsolidateDialog");

        var functionBox = new ComboBox
        {
            ItemsSource = ConsolidateDialogPlanner.FunctionChoices.Select(c => c.Label).ToList(),
            SelectedIndex = 0,
            MinWidth = 160,
        };
        ApplyDataOpsComboBoxChrome(functionBox);
        AutomationProperties.SetAutomationId(functionBox, "ConsolidateFunctionBox");

        var referenceBox = new TextBox { PlaceholderText = UiText.Get("TableLoc_ConsolidateReferencePlaceholder"), MinWidth = 220 };
        ApplyDataOpsTextBoxChrome(referenceBox);
        AutomationProperties.SetAutomationId(referenceBox, "ConsolidateReferenceBox");

        // Windows places an ellipsis ("...") range-picker next to the Reference field (matches the WPF host's
        // DialogReferencePicker which uses a literal "..." button, width 28, docked left of the text box).
        var browseButton = new Button { Content = "...", Width = 28, MinWidth = 28 };
        ApplyDataOpsButtonChrome(browseButton);
        AutomationProperties.SetAutomationId(browseButton, "ConsolidateBrowseReferenceButton");

        var referencesList = new ListBox { MinHeight = ConsolidateDialogPlanner.ReferencesListHeight };
        ApplyDataOpsListBoxChrome(referencesList);
        AutomationProperties.SetAutomationId(referencesList, "ConsolidateAllReferencesList");

        var addButton = new Button { Content = UiText.Get("TableLoc_Add"), MinWidth = 76 };
        ApplyDataOpsButtonChrome(addButton);
        AutomationProperties.SetAutomationId(addButton, "ConsolidateAddReferenceButton");
        var removeButton = new Button { Content = UiText.Get("TableLoc_Remove"), MinWidth = 76, IsEnabled = false };
        ApplyDataOpsButtonChrome(removeButton);
        AutomationProperties.SetAutomationId(removeButton, "ConsolidateRemoveReferenceButton");

        var destinationBox = new TextBox
        {
            Text = FormatRangeReference(_session.SelectedRange),
            MinWidth = 220,
        };
        ApplyDataOpsTextBoxChrome(destinationBox);
        AutomationProperties.SetAutomationId(destinationBox, "ConsolidateDestinationCellBox");

        var destinationBrowseButton = new Button { Content = "...", Width = 28, MinWidth = 28 };
        ApplyDataOpsButtonChrome(destinationBrowseButton);
        AutomationProperties.SetAutomationId(destinationBrowseButton, "ConsolidateBrowseDestinationButton");

        var topRowBox = new CheckBox { Content = UiText.Get("TableLoc_ConsolidateTopRow") };
        ApplyDataOpsCheckBoxChrome(topRowBox);
        AutomationProperties.SetAutomationId(topRowBox, "ConsolidateTopRowLabelsBox");
        var leftColumnBox = new CheckBox { Content = UiText.Get("TableLoc_ConsolidateLeftColumn") };
        ApplyDataOpsCheckBoxChrome(leftColumnBox);
        AutomationProperties.SetAutomationId(leftColumnBox, "ConsolidateLeftColumnLabelsBox");
        // WPF has a "Create links to source data" checkbox below the Use labels row
        var createLinksBox = new CheckBox { Content = UiText.Get("TableLoc_ConsolidateCreateLinks") };
        ApplyDataOpsCheckBoxChrome(createLinksBox);
        AutomationProperties.SetAutomationId(createLinksBox, "ConsolidateCreateLinksBox");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(warningText, "ConsolidateWarningText");

        var references = new List<string>();
        var overwriteConfirmed = false;

        void RefreshReferences()
        {
            referencesList.ItemsSource = references.ToList();
            overwriteConfirmed = false;
        }

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
                    rejectDuplicateReferences: true,
                    out var updatedReferences,
                    out var issue))
            {
                warningText.Text = ConsolidateAddWarningText(issue);
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

        var applyButton = new Button { Content = UiText.Get("TableLoc_Apply"), IsDefault = true, MinWidth = 84 };
        ApplyDataOpsButtonChrome(applyButton, isDefault: true);
        AutomationProperties.SetAutomationId(applyButton, "ConsolidateApplyButton");
        var cancelButton = new Button { Content = UiText.Get("TableLoc_Cancel"), IsCancel = true, MinWidth = 84 };
        ApplyDataOpsButtonChrome(cancelButton);
        AutomationProperties.SetAutomationId(cancelButton, "ConsolidateCancelButton");

        applyButton.Click += (_, _) =>
        {
            warningText.IsVisible = false;

            var options = new ConsolidateOptions
            {
                Function = ConsolidateDialogPlanner.FunctionChoices[Math.Max(0, functionBox.SelectedIndex)].Function,
                UseTopRowLabels = topRowBox.IsChecked == true,
                UseLeftColumnLabels = leftColumnBox.IsChecked == true,
            };

            if (!ConsolidateDialogPlanner.TryPlanApply(
                    _session.Workbook,
                    references,
                    destinationBox.Text?.Trim() ?? string.Empty,
                    TryParseConsolidateReference,
                    options,
                    out var plan,
                    out var issue))
            {
                warningText.Text = ConsolidateApplyWarningText(issue);
                warningText.IsVisible = true;
                return;
            }

            if (plan.OverwriteTargets.Count > 0 && !overwriteConfirmed)
            {
                overwriteConfirmed = true;
                warningText.Text = UiText.Format("TableLoc_ConsolidateOverwriteWarning", plan.OverwriteTargets.Count);
                warningText.IsVisible = true;
                return;
            }

            if (!ApplyConsolidatePlan(plan, createLinksBox.IsChecked == true))
                return;

            dialog.Close();
        };
        cancelButton.Click += (_, _) => dialog.Close();

        // Windows: "[...] <Reference textbox>" — Browse (ellipsis) sits left of the reference field.
        var referenceRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(browseButton, Dock.Left);
        browseButton.Margin = new Thickness(0, 0, 8, 0);
        referenceRow.Children.Add(browseButton);
        referenceRow.Children.Add(referenceBox);

        // Windows: Add / Delete buttons sit between the Reference field and the "All references" list,
        // right-aligned side by side (matches the WPF ConsolidateDialog layout / win.png ground truth).
        var addRemoveRow = AvaloniaCompactDialogChrome.CreateActionRow(
            [addButton, removeButton],
            new Thickness(0, 6, 0, 2));

        // Windows: "[...] <Destination textbox>" — Browse (ellipsis) sits left of the destination field.
        var destinationRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(destinationBrowseButton, Dock.Left);
        destinationBrowseButton.Margin = new Thickness(0, 0, 8, 0);
        destinationRow.Children.Add(destinationBrowseButton);
        destinationRow.Children.Add(destinationBox);

        var labelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = UiText.Get("TableLoc_ConsolidateUseLabelsIn"), VerticalAlignment = AvaloniaVerticalAlignment.Center, FontSize = 12, FontFamily = FormulaBarFontFamily },
                topRowBox,
                leftColumnBox,
            },
        };

        // WPF button order: [OK][Cancel] — primary on left; the Apply button maps to WPF OK
        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([applyButton, cancelButton], new Thickness(0, 10, 0, 0));
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        var root = new DockPanel
        {
            Margin = new Thickness(12),
            Children =
            {
                buttonRow,
                new ScrollViewer
                {
                    Content = new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            new TextBlock { Text = UiText.Get("TableLoc_ConsolidateFunctionLabel"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
                            functionBox,
                            new TextBlock { Text = UiText.Get("TableLoc_ConsolidateReferenceLabel"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
                            referenceRow,
                            addRemoveRow,
                            new TextBlock { Text = UiText.Get("TableLoc_ConsolidateAllReferencesLabel"), Foreground = HeaderForeground, FontSize = 12, FontFamily = FormulaBarFontFamily },
                            referencesList,
                            new TextBlock { Text = UiText.Get("TableLoc_ConsolidateDestinationLabel"), FontWeight = FontWeight.SemiBold, FontSize = 12, FontFamily = FormulaBarFontFamily },
                            destinationRow,
                            labelRow,
                            createLinksBox,
                            warningText,
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

    private static string ConsolidateAddWarningText(ConsolidateDialogIssue issue) =>
        issue.Kind == ConsolidateDialogIssueKind.DuplicateSourceReference
            ? UiText.Get("TableLoc_ConsolidateSourceAlreadyListed")
            : UiText.Get("TableLoc_ConsolidateEnterValidSource");

    private static string ConsolidateApplyWarningText(ConsolidateDialogIssue issue) =>
        issue.Kind switch
        {
            ConsolidateDialogIssueKind.InvalidSourceRange when !string.IsNullOrWhiteSpace(issue.InvalidPart) =>
                UiText.Format("TableLoc_ConsolidateCannotResolveSource", issue.InvalidPart),
            ConsolidateDialogIssueKind.MismatchedSourceSizes => UiText.Get("Consolidate_SourceRangesMustBeSameSize"),
            ConsolidateDialogIssueKind.InvalidDestinationCell => UiText.Get("TableLoc_ConsolidateEnterValidDestination"),
            ConsolidateDialogIssueKind.NoOutput => UiText.Get("TableLoc_ConsolidateNoOutput"),
            ConsolidateDialogIssueKind.OutsideWorksheetBounds => UiText.Get("TableLoc_ConsolidateOutsideBounds"),
            _ => UiText.Get("TableLoc_ConsolidateAddAtLeastOne")
        };

    /// <summary>Applies the shared Consolidate command through the session command path and refreshes the shell.</summary>
    private bool ApplyConsolidatePlan(ConsolidateApplyPlan plan, bool createLinksToSourceData)
    {
        var command = new ConsolidateCommand(
            plan.SourceRanges,
            plan.DestinationCell,
            plan.Options.Function,
            plan.Options.UseTopRowLabels,
            plan.Options.UseLeftColumnLabels,
            createLinksToSourceData);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_ConsolidateFailed"));
            return false;
        }

        RefreshShell(UiText.Format("TableLoc_ConsolidatedInto", FormatCellReference(plan.DestinationCell)));
        return true;
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
