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
            Width = 460,
            Height = 520,
            MinWidth = 420,
            MinHeight = 460,
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
        AutomationProperties.SetAutomationId(functionBox, "ConsolidateFunctionBox");

        var referenceBox = new TextBox { PlaceholderText = UiText.Get("TableLoc_ConsolidateReferencePlaceholder"), MinWidth = 220 };
        AutomationProperties.SetAutomationId(referenceBox, "ConsolidateReferenceBox");

        var referencesList = new ListBox { MinHeight = 96 };
        AutomationProperties.SetAutomationId(referencesList, "ConsolidateAllReferencesList");

        var addButton = new Button { Content = UiText.Get("TableLoc_Add"), MinWidth = 76 };
        AutomationProperties.SetAutomationId(addButton, "ConsolidateAddReferenceButton");
        var removeButton = new Button { Content = UiText.Get("TableLoc_Remove"), MinWidth = 76, IsEnabled = false };
        AutomationProperties.SetAutomationId(removeButton, "ConsolidateRemoveReferenceButton");

        var destinationBox = new TextBox
        {
            Text = FormatRangeReference(_session.SelectedRange),
            MinWidth = 220,
        };
        AutomationProperties.SetAutomationId(destinationBox, "ConsolidateDestinationCellBox");

        var topRowBox = new CheckBox { Content = UiText.Get("TableLoc_ConsolidateTopRow") };
        AutomationProperties.SetAutomationId(topRowBox, "ConsolidateTopRowLabelsBox");
        var leftColumnBox = new CheckBox { Content = UiText.Get("TableLoc_ConsolidateLeftColumn") };
        AutomationProperties.SetAutomationId(leftColumnBox, "ConsolidateLeftColumnLabelsBox");

        var warningText = new TextBlock
        {
            Foreground = Brush(180, 30, 30),
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
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
        AutomationProperties.SetAutomationId(applyButton, "ConsolidateApplyButton");
        var cancelButton = new Button { Content = UiText.Get("TableLoc_Cancel"), IsCancel = true, MinWidth = 84 };
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

        var referenceRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { referenceBox, addButton, removeButton },
        };

        var labelRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = UiText.Get("TableLoc_ConsolidateUseLabelsIn"), VerticalAlignment = AvaloniaVerticalAlignment.Center },
                topRowBox,
                leftColumnBox,
            },
        };

        var buttonRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancelButton, applyButton },
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
                            new TextBlock { Text = UiText.Get("TableLoc_ConsolidateFunctionLabel"), FontWeight = FontWeight.SemiBold },
                            functionBox,
                            new TextBlock { Text = UiText.Get("TableLoc_ConsolidateReferenceLabel"), FontWeight = FontWeight.SemiBold },
                            referenceRow,
                            new TextBlock { Text = UiText.Get("TableLoc_ConsolidateAllReferencesLabel"), Foreground = HeaderForeground },
                            referencesList,
                            new TextBlock { Text = UiText.Get("TableLoc_ConsolidateDestinationLabel"), FontWeight = FontWeight.SemiBold },
                            destinationBox,
                            labelRow,
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
}
