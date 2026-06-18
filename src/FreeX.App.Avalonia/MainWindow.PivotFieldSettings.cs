using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity per-field PivotTable configuration dialogs for the Avalonia/macOS shell: the
/// "Value Field Settings" dialog (summary function + custom display name + show-values-as) and the
/// "More Sort Options" dialog (label/value ascending/descending + value field). Both collect input then
/// call the portable planners (<see cref="PivotValueFieldPlanner"/> / <see cref="PivotSortPlanner"/>) for all
/// validation/result building, so the logic is single-sourced with the WPF host and reusable on macOS. The
/// value-field result replaces the data field in the layout (<see cref="ConfigurePivotTableLayoutCommand"/>);
/// the sort result replaces the field's sort in the pivot's view state
/// (<see cref="ConfigurePivotTableViewCommand"/>). These two header actions are deferred by
/// <c>PivotHeaderMenuCommandFactory</c>; <c>InvokePivotHeaderAction</c> routes them here before the factory.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Entry point for the per-field configuration dialogs the header dropdown defers. Opens the
    /// value-field-settings or more-sort-options dialog for <paramref name="target"/> depending on
    /// <paramref name="action"/>. Returns true when this partial handled the action (so the caller skips the
    /// command-factory deferred path), false otherwise.
    /// </summary>
    internal bool TryOpenPivotFieldSettings(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        PivotHeaderMenuAction action)
    {
        switch (action)
        {
            case PivotHeaderMenuAction.ValueFieldSettings:
                _ = OpenPivotValueFieldSettingsDialogAsync(pivot, headers, target);
                return true;
            case PivotHeaderMenuAction.MoreSortOptions:
                _ = OpenPivotSortOptionsDialogAsync(pivot, headers, target);
                return true;
            default:
                return false;
        }
    }

    // ── Value Field Settings ──────────────────────────────────────────────────
    private async Task OpenPivotValueFieldSettingsDialogAsync(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target)
    {
        if (_isOpening || _isSaving)
            return;

        var dataFieldIndex = ResolvePivotDataFieldIndex(pivot, target);
        if (dataFieldIndex is null)
        {
            ShowEditIssue("Select a value field to change its settings.");
            return;
        }

        var field = pivot.DataFields[dataFieldIndex.Value];

        var nameBox = new TextBox { MinWidth = 240, Text = field.Name };
        AutomationProperties.SetAutomationId(nameBox, "PivotValueFieldSettingsNameBox");
        AutomationProperties.SetName(nameBox, "Custom name");

        var summaryBox = new ComboBox { MinWidth = 240 };
        foreach (var (label, _) in PivotValueFieldPlanner.SummaryFunctions)
            summaryBox.Items.Add(label);
        summaryBox.SelectedIndex = PivotValueFieldPlanner.FindSummaryFunctionIndex(field.SummaryFunction);
        AutomationProperties.SetAutomationId(summaryBox, "PivotValueFieldSettingsSummaryBox");
        AutomationProperties.SetName(summaryBox, "Summarize by");

        var showValuesAsBox = new ComboBox { MinWidth = 240 };
        foreach (var (label, _) in PivotValueFieldPlanner.ShowValuesAsOptions)
            showValuesAsBox.Items.Add(label);
        showValuesAsBox.SelectedIndex = PivotValueFieldPlanner.FindShowValuesAsIndex(field.ShowValuesAs);
        AutomationProperties.SetAutomationId(showValuesAsBox, "PivotValueFieldSettingsShowValuesAsBox");
        AutomationProperties.SetName(showValuesAsBox, "Show values as");

        var baseFieldBox = new ComboBox { MinWidth = 240 };
        baseFieldBox.Items.Add(PivotValueFieldPlanner.AutomaticBaseFieldLabel);
        foreach (var header in headers)
            baseFieldBox.Items.Add(header);
        baseFieldBox.SelectedIndex = PivotValueFieldPlanner.FindBaseFieldIndex(field.BaseFieldIndex, headers.Count);
        AutomationProperties.SetAutomationId(baseFieldBox, "PivotValueFieldSettingsBaseFieldBox");
        AutomationProperties.SetName(baseFieldBox, "Base field");

        var baseItemBox = new TextBox { MinWidth = 240, Text = field.BaseItem ?? string.Empty, PlaceholderText = "Base item" };
        AutomationProperties.SetAutomationId(baseItemBox, "PivotValueFieldSettingsBaseItemBox");
        AutomationProperties.SetName(baseItemBox, "Base item");

        var basePanel = new StackPanel { Spacing = 8 };
        basePanel.Children.Add(new TextBlock { Text = "Base field:", Foreground = HeaderForeground });
        basePanel.Children.Add(baseFieldBox);
        basePanel.Children.Add(new TextBlock { Text = "Base item:", Foreground = HeaderForeground });
        basePanel.Children.Add(baseItemBox);

        void SyncBaseFieldState()
        {
            var showValuesAs = PivotValueFieldPlanner.ShowValuesAsFromIndex(showValuesAsBox.SelectedIndex);
            basePanel.IsVisible = PivotValueFieldPlanner.ShowValuesAsRequiresBaseField(showValuesAs);
        }

        showValuesAsBox.SelectionChanged += (_, _) => SyncBaseFieldState();
        SyncBaseFieldState();

        var dialog = new Window
        {
            Title = $"Value Field Settings ({target.FieldCaption})",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotValueFieldSettingsDialog");

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "PivotValueFieldSettingsOkButton");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "PivotValueFieldSettingsCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            var showValuesAs = PivotValueFieldPlanner.ShowValuesAsFromIndex(showValuesAsBox.SelectedIndex);
            var baseFieldIndex = PivotValueFieldPlanner.ResolveBaseFieldIndex(showValuesAs, baseFieldBox.SelectedIndex);
            var baseItem = PivotValueFieldPlanner.ResolveBaseItem(showValuesAs, baseItemBox.Text);
            if (!PivotValueFieldPlanner.TryValidateShowValuesAs(showValuesAs, baseFieldIndex, baseItem, out var error))
            {
                ShowEditIssue(error ?? "Complete the show-values-as settings.");
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock { Text = "Custom Name:", Foreground = HeaderForeground });
        content.Children.Add(nameBox);
        content.Children.Add(new TextBlock { Text = "Summarize value field by:", Foreground = HeaderForeground });
        content.Children.Add(summaryBox);
        content.Children.Add(new TextBlock { Text = "Show values as:", Foreground = HeaderForeground });
        content.Children.Add(showValuesAsBox);
        content.Children.Add(basePanel);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, cancel },
        });
        dialog.Content = content;

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        var result = PivotValueFieldPlanner.CreateResult(
            field,
            headers,
            nameBox.Text,
            summaryBox.SelectedIndex,
            showValuesAsBox.SelectedIndex,
            baseFieldBox.SelectedIndex,
            baseItemBox.Text);

        var dataFields = pivot.DataFields.ToList();
        dataFields[dataFieldIndex.Value] = result;

        var command = new ConfigurePivotTableLayoutCommand(
            _session.ActiveSheet.Id,
            pivot.Name,
            pivot.RowFields.ToList(),
            pivot.ColumnFields.ToList(),
            pivot.PageFields.ToList(),
            dataFields);
        ExecutePivotCommand(command);
    }

    // ── More Sort Options ─────────────────────────────────────────────────────
    private async Task OpenPivotSortOptionsDialogAsync(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target)
    {
        if (_isOpening || _isSaving)
            return;

        var caption = PivotFieldListPaneBuilder.FieldCaption(headers, target.SourceFieldIndex);
        var currentSort = pivot.Sorts.FirstOrDefault(sort => sort.FieldIndex == target.SourceFieldIndex);
        var dataFieldCount = pivot.DataFields.Count;

        var labelAscending = new RadioButton { Content = "Ascending (A to Z) by labels", GroupName = "PivotSortOptions" };
        var labelDescending = new RadioButton { Content = "Descending (Z to A) by labels", GroupName = "PivotSortOptions" };
        var valueAscending = new RadioButton { Content = "Ascending by values", GroupName = "PivotSortOptions" };
        var valueDescending = new RadioButton { Content = "Descending by values", GroupName = "PivotSortOptions" };
        AutomationProperties.SetAutomationId(labelAscending, "PivotSortOptionsLabelAscending");
        AutomationProperties.SetAutomationId(labelDescending, "PivotSortOptionsLabelDescending");
        AutomationProperties.SetAutomationId(valueAscending, "PivotSortOptionsValueAscending");
        AutomationProperties.SetAutomationId(valueDescending, "PivotSortOptionsValueDescending");

        var valueFieldBox = new ComboBox { MinWidth = 220 };
        foreach (var dataField in pivot.DataFields)
            valueFieldBox.Items.Add(dataField.Name);
        AutomationProperties.SetAutomationId(valueFieldBox, "PivotSortOptionsValueFieldBox");
        AutomationProperties.SetName(valueFieldBox, "Value field");

        var initialMode = PivotSortPlanner.InitialMode(currentSort, target.SourceFieldIndex);
        switch (initialMode)
        {
            case PivotSortOptionMode.LabelDescending: labelDescending.IsChecked = true; break;
            case PivotSortOptionMode.ValueAscending: valueAscending.IsChecked = true; break;
            case PivotSortOptionMode.ValueDescending: valueDescending.IsChecked = true; break;
            default: labelAscending.IsChecked = true; break;
        }

        valueFieldBox.SelectedIndex =
            PivotSortPlanner.InitialValueFieldIndex(currentSort, target.SourceFieldIndex, dataFieldCount);

        PivotSortOptionMode CurrentMode()
        {
            if (valueAscending.IsChecked == true)
                return PivotSortOptionMode.ValueAscending;
            if (valueDescending.IsChecked == true)
                return PivotSortOptionMode.ValueDescending;
            if (labelDescending.IsChecked == true)
                return PivotSortOptionMode.LabelDescending;
            return PivotSortOptionMode.LabelAscending;
        }

        void SyncValueFieldState()
        {
            valueFieldBox.IsEnabled = PivotSortPlanner.ValueFieldEnabled(CurrentMode(), dataFieldCount);
        }

        foreach (var button in new[] { labelAscending, labelDescending, valueAscending, valueDescending })
            button.IsCheckedChanged += (_, _) => SyncValueFieldState();
        SyncValueFieldState();

        var dialog = new Window
        {
            Title = $"More Sort Options ({caption})",
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotSortOptionsDialog");

        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "PivotSortOptionsOkButton");
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "PivotSortOptionsCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            if (!PivotSortPlanner.TryValidate(CurrentMode(), dataFieldCount, valueFieldBox.SelectedIndex, out var error))
            {
                ShowEditIssue(error ?? PivotSortPlanner.ValueSortRequiresValueFieldMessage);
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 6, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock
        {
            Text = $"Sort {caption}",
            FontWeight = FontWeight.SemiBold,
            Foreground = HeaderForeground,
            Margin = new Thickness(0, 0, 0, 4),
        });
        content.Children.Add(labelAscending);
        content.Children.Add(labelDescending);
        content.Children.Add(valueAscending);
        content.Children.Add(valueDescending);
        content.Children.Add(new TextBlock { Text = "Value field:", Foreground = HeaderForeground, Margin = new Thickness(18, 4, 0, 0) });
        content.Children.Add(new StackPanel { Margin = new Thickness(18, 0, 0, 0), Children = { valueFieldBox } });
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        var sort = PivotSortPlanner.CreateResult(CurrentMode(), target.SourceFieldIndex, valueFieldBox.SelectedIndex);
        var sorts = PivotSortPlanner.ReplaceFieldSort(pivot.Sorts.ToList(), sort);

        var command = new ConfigurePivotTableViewCommand(
            _session.ActiveSheet.Id,
            pivot.Name,
            pivot.LabelFilters.ToList(),
            pivot.ValueFilters.ToList(),
            sorts);
        ExecutePivotCommand(command);
    }

    // The pane chip carries the value-area data-field index directly; fall back to a source-field match.
    private static int? ResolvePivotDataFieldIndex(PivotTableModel pivot, PivotHeaderDropdownTargetModel target)
    {
        if (target.DataFieldIndex is { } index && index >= 0 && index < pivot.DataFields.Count)
            return index;

        for (var i = 0; i < pivot.DataFields.Count; i++)
        {
            if (pivot.DataFields[i].SourceFieldIndex == target.SourceFieldIndex)
                return i;
        }

        return null;
    }
}
