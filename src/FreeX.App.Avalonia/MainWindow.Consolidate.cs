using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.Consolidate;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    /// <summary>Opens the Consolidate dialog (invoked from the Data menu and the Data-tab ribbon button).</summary>
    private void Consolidate() => _ = ShowConsolidateDialogAsync();

    /// <summary>
    /// The compact Consolidate dialog: pick an aggregation function, add one or more source ranges (typed as
    /// <c>Sheet!A1:B5</c>-style references and resolved through <see cref="WorkbookReferenceNavigator"/>),
    /// choose a destination anchor (defaulting to the active selection's top-left), and toggle "Use labels in
    /// Top row" / "Left column". On Apply each source range is read into a <see cref="ConsolidateSource"/>
    /// grid, the <see cref="ConsolidatePlanner"/> aggregates them, and the resulting cells are written into the
    /// destination through the shared session command path (undoable + refreshing). Overwriting non-empty
    /// destination cells requires a second Apply click.
    /// </summary>
    private async Task ShowConsolidateDialogAsync()
    {
        if (_isOpening || _isSaving || !TryCommitPendingFormulaEdit())
            return;

        var dialog = new Window
        {
            Title = UiText.Get("TableLoc_ConsolidateDialogTitle"),
            Width = 420,
            MinWidth = 400,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "ConsolidateDialog");

        var functionBox = new ComboBox
        {
            ItemsSource = ConsolidateShellPlanner.FunctionChoices.Select(c => c.Label).ToList(),
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
        browseButton.Click += (_, _) =>
        {
            // The picker pre-fills the reference field with the current selection.
            if (string.IsNullOrWhiteSpace(referenceBox.Text))
                referenceBox.Text = FormatRangeReference(_session.SelectedRange);
        };

        var referencesList = new ListBox { MinHeight = 96 };
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
        destinationBrowseButton.Click += (_, _) =>
            destinationBox.Text = FormatRangeReference(_session.SelectedRange);

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
            if (!TryParseConsolidateReference(text, out _))
            {
                warningText.Text = UiText.Get("TableLoc_ConsolidateEnterValidSource");
                warningText.IsVisible = true;
                return;
            }

            if (references.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                warningText.Text = UiText.Get("TableLoc_ConsolidateSourceAlreadyListed");
                warningText.IsVisible = true;
                return;
            }

            references.Add(text);
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

            if (references.Count == 0)
            {
                warningText.Text = UiText.Get("TableLoc_ConsolidateAddAtLeastOne");
                warningText.IsVisible = true;
                return;
            }

            var sources = new List<ConsolidateSource>(references.Count);
            foreach (var reference in references)
            {
                if (!TryParseConsolidateReference(reference, out var sourceRange))
                {
                    warningText.Text = UiText.Format("TableLoc_ConsolidateCannotResolveSource", reference);
                    warningText.IsVisible = true;
                    return;
                }

                var sheet = _session.Workbook.GetSheet(sourceRange.Start.Sheet);
                if (sheet is null)
                {
                    warningText.Text = UiText.Format("TableLoc_ConsolidateCannotResolveSource", reference);
                    warningText.IsVisible = true;
                    return;
                }

                sources.Add(ConsolidateSource.FromGrid(ConsolidateShellPlanner.ReadSource(sheet, sourceRange)));
            }

            if (!TryParseConsolidateReference(destinationBox.Text?.Trim() ?? string.Empty, out var destinationRange))
            {
                warningText.Text = UiText.Get("TableLoc_ConsolidateEnterValidDestination");
                warningText.IsVisible = true;
                return;
            }

            var options = new ConsolidateOptions
            {
                Function = ConsolidateShellPlanner.FunctionChoices[Math.Max(0, functionBox.SelectedIndex)].Function,
                UseTopRowLabels = topRowBox.IsChecked == true,
                UseLeftColumnLabels = leftColumnBox.IsChecked == true,
            };

            var result = ConsolidatePlanner.Plan(sources, options);
            if (result.IsEmpty)
            {
                warningText.Text = UiText.Get("TableLoc_ConsolidateNoOutput");
                warningText.IsVisible = true;
                return;
            }

            var destinationSheet = _session.Workbook.GetSheet(destinationRange.Start.Sheet) ?? _session.ActiveSheet;
            var edits = ConsolidateShellPlanner.MapToEdits(destinationSheet.Id, result, destinationRange.Start);
            if (edits.Count == 0)
            {
                warningText.Text = UiText.Get("TableLoc_ConsolidateOutsideBounds");
                warningText.IsVisible = true;
                return;
            }

            var overwrites = ConsolidateShellPlanner.FindOverwriteTargets(destinationSheet, edits);
            if (overwrites.Count > 0 && !overwriteConfirmed)
            {
                overwriteConfirmed = true;
                warningText.Text = UiText.Format("TableLoc_ConsolidateOverwriteWarning", overwrites.Count);
                warningText.IsVisible = true;
                return;
            }

            if (!ApplyConsolidateEdits(destinationSheet.Id, edits, destinationRange.Start))
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
        addButton.Margin = new Thickness(0, 0, 8, 0);
        var addRemoveRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 2),
            Children = { addButton, removeButton },
        };

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
        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { applyButton, cancelButton },
        };
        DockPanel.SetDock(buttonRow, Dock.Bottom);

        dialog.Content = new DockPanel
        {
            Margin = new Thickness(16),
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

    /// <summary>Applies the consolidated cell edits through the shared session command path and refreshes the shell.</summary>
    private bool ApplyConsolidateEdits(
        SheetId sheetId,
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        CellAddress destination)
    {
        var command = new EditCellsCommand(sheetId, edits);
        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("TableLoc_ConsolidateFailed"));
            return false;
        }

        RefreshShell(UiText.Format("TableLoc_ConsolidatedInto", FormatCellReference(destination)));
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
    {
        button.Height = 24;
        button.MinHeight = 24;
        button.MaxHeight = 24;
        button.Padding = new Thickness(4, 1);
        button.Background = Brushes.White;
        button.BorderBrush = isDefault ? Brush(0, 120, 215) : Brush(112, 112, 112);
        button.BorderThickness = new Thickness(1);
        button.FontSize = 12;
        button.FontFamily = FormulaBarFontFamily;
        button.HorizontalContentAlignment = AvaloniaHorizontalAlignment.Center;
        button.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    /// <summary>
    /// Applies standard text box chrome: Height=24, Padding=(4,1), FontSize=12,
    /// Brush(130,130,130) border, BorderThickness=1.
    /// </summary>
    private static void ApplyDataOpsTextBoxChrome(TextBox textBox)
    {
        textBox.Height = 24;
        textBox.MinHeight = 24;
        textBox.MaxHeight = 24;
        textBox.Padding = new Thickness(4, 1);
        textBox.FontSize = 12;
        textBox.FontFamily = FormulaBarFontFamily;
        textBox.BorderBrush = Brush(130, 130, 130);
        textBox.BorderThickness = new Thickness(1);
        textBox.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    /// <summary>
    /// Applies standard combo box chrome: Height=24, Padding=(5,0,4,0), FontSize=12,
    /// Brush(130,130,130) border, BorderThickness=1.
    /// </summary>
    private static void ApplyDataOpsComboBoxChrome(ComboBox comboBox)
    {
        comboBox.Height = 24;
        comboBox.MinHeight = 24;
        comboBox.MaxHeight = 24;
        comboBox.Padding = new Thickness(5, 0, 4, 0);
        comboBox.FontSize = 12;
        comboBox.FontFamily = FormulaBarFontFamily;
        comboBox.BorderBrush = Brush(130, 130, 130);
        comboBox.BorderThickness = new Thickness(1);
        comboBox.VerticalContentAlignment = AvaloniaVerticalAlignment.Center;
    }

    /// <summary>
    /// Applies standard check box chrome: MinHeight=20, MaxHeight=20, FontSize=12,
    /// FontFamily=FormulaBarFontFamily.
    /// </summary>
    private static void ApplyDataOpsCheckBoxChrome(CheckBox checkBox)
    {
        checkBox.MinHeight = 20;
        checkBox.MaxHeight = 20;
        checkBox.FontSize = 12;
        checkBox.FontFamily = FormulaBarFontFamily;
    }

    /// <summary>
    /// Applies standard radio button chrome: MinHeight=20, MaxHeight=20, FontSize=12,
    /// FontFamily=FormulaBarFontFamily.
    /// </summary>
    private static void ApplyDataOpsRadioButtonChrome(RadioButton radioButton)
    {
        radioButton.MinHeight = 20;
        radioButton.MaxHeight = 20;
        radioButton.FontSize = 12;
        radioButton.FontFamily = FormulaBarFontFamily;
    }
}
