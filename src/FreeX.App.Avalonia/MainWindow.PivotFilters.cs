using System.Globalization;

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
/// Windows-parity PivotTable field-filter dialogs for the Avalonia/macOS shell. The field-pane header
/// dropdown (built in <see cref="BuildPivotFieldChip"/> / <see cref="ShowPivotHeaderDropdown"/>) exposes
/// "Label Filters…", "Value Filters…" and a manual item (checkbox) filter; each opens a modal dialog and
/// applies the result through <see cref="ConfigurePivotTableFieldFiltersCommand"/> — the one Core command
/// that carries the row/column/page field lists (for manual <see cref="PivotFieldModel.SelectedItems"/>),
/// the <see cref="PivotLabelFilterModel"/> list and the <see cref="PivotValueFilterModel"/> list together.
/// Member text for the checkbox list is read from the pivot's source range and formatted to match the
/// engine's key text (see <see cref="ReadPivotFieldMembers"/> / <see cref="MemberKeyText"/>), so a checked
/// item agrees with what the refresh service keeps.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Entry point for the field pane's header dropdown. Opens the manual item (checkbox) filter, the label
    /// filter, or the value filter dialog for <paramref name="target"/> depending on <paramref name="action"/>.
    /// Returns true when the action was a filter action this partial handled (so the caller skips the
    /// deferred path), false otherwise.
    /// </summary>
    internal bool TryOpenPivotFieldFilter(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target,
        PivotHeaderMenuAction action)
    {
        switch (action)
        {
            case PivotHeaderMenuAction.LabelFilter:
                _ = OpenPivotLabelFilterDialogAsync(pivot, target);
                return true;
            case PivotHeaderMenuAction.ValueFilter:
                _ = OpenPivotValueFilterDialogAsync(pivot, target);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Opens the manual item (checkbox) filter for the field. Wired to a dedicated "Item Filter…" pane menu
    /// entry / field-pane affordance. Reads the field's distinct members, lets the user check the ones to
    /// keep, and writes them to the field's <see cref="PivotFieldModel.SelectedItems"/>.
    /// </summary>
    internal void OpenPivotItemFilter(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target)
    {
        _ = OpenPivotItemFilterDialogAsync(pivot, headers, target);
    }

    // ── Manual item (checkbox) filter ─────────────────────────────────────────
    private async Task OpenPivotItemFilterDialogAsync(
        PivotTableModel pivot,
        IReadOnlyList<string> headers,
        PivotHeaderDropdownTargetModel target)
    {
        if (_isOpening || _isSaving)
            return;

        var caption = PivotFieldListPaneBuilder.FieldCaption(headers, target.SourceFieldIndex);
        var members = ReadPivotFieldMembers(pivot, target.SourceFieldIndex);
        if (members.Count == 0)
        {
            ShowEditIssue(UiText.Get("PivotLoc_NoItemsToFilter"));
            return;
        }

        var current = FindFieldSelection(pivot, target);
        // No explicit selection (or "(All)") means every item is shown.
        var currentSet = PivotFieldFilterPlanner.ResolveAllowedItems(current);

        var checkBoxes = new List<CheckBox>();
        var listPanel = new StackPanel();
        foreach (var member in members)
        {
            var box = new CheckBox
            {
                Content = member,
                Tag = member,
                IsChecked = currentSet is null || currentSet.Contains(member),
            };
            checkBoxes.Add(box);
            listPanel.Children.Add(box);
        }

        var selectAll = new CheckBox
        {
            Content = UiText.Get("PivotLoc_SelectAll"),
            IsChecked = checkBoxes.All(box => box.IsChecked == true),
        };
        selectAll.IsCheckedChanged += (_, _) =>
        {
            if (selectAll.IsChecked is { } value)
                foreach (var box in checkBoxes)
                    box.IsChecked = value;
        };

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock
        {
            Text = UiText.Format("PivotFilter_ItemsHeading", caption),
            FontWeight = FontWeight.SemiBold,
            Foreground = HeaderForeground,
        });
        content.Children.Add(selectAll);
        content.Children.Add(new ScrollViewer
        {
            MaxHeight = 280,
            Content = listPanel,
        });

        var dialog = new Window
        {
            Title = UiText.Get("PivotFilter_ItemsTitle"),
            Width = 300,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotItemFilterDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "PivotItemFilterOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "PivotItemFilterCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) => dialog.Close(true);

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

        var checked_ = checkBoxes.Where(box => box.IsChecked == true).Select(box => (string)box.Tag!).ToList();
        // Selecting every item is "no filter": clear the selection so new members stay visible.
        var selection = PivotFieldFilterPlanner.ResolveItemSelection(checked_, members.Count);
        ApplyPivotItemFilter(pivot, target, selection);
    }

    private void ApplyPivotItemFilter(
        PivotTableModel pivot,
        PivotHeaderDropdownTargetModel target,
        IReadOnlyList<string>? selectedItems)
    {
        var rows = CloneFieldsWithSelection(pivot.RowFields, target.SourceFieldIndex, target.Area, PivotHeaderArea.Row, selectedItems);
        var columns = CloneFieldsWithSelection(pivot.ColumnFields, target.SourceFieldIndex, target.Area, PivotHeaderArea.Column, selectedItems);
        var pages = CloneFieldsWithSelection(pivot.PageFields, target.SourceFieldIndex, target.Area, PivotHeaderArea.Page, selectedItems);

        ExecutePivotFilterCommand(pivot, rows, columns, pages, pivot.LabelFilters.ToList(), pivot.ValueFilters.ToList());
    }

    private static IReadOnlyList<PivotFieldModel> CloneFieldsWithSelection(
        IReadOnlyList<PivotFieldModel> fields,
        int sourceFieldIndex,
        PivotHeaderArea targetArea,
        PivotHeaderArea thisArea,
        IReadOnlyList<string>? selectedItems)
    {
        var result = new List<PivotFieldModel>(fields.Count);
        foreach (var field in fields)
        {
            if (targetArea == thisArea && field.SourceFieldIndex == sourceFieldIndex)
                result.Add(field with { SelectedItem = null, SelectedItems = selectedItems });
            else
                result.Add(field);
        }

        return result;
    }

    private static IReadOnlyList<string>? FindFieldSelection(PivotTableModel pivot, PivotHeaderDropdownTargetModel target)
    {
        var fields = target.Area switch
        {
            PivotHeaderArea.Row => pivot.RowFields,
            PivotHeaderArea.Column => pivot.ColumnFields,
            PivotHeaderArea.Page => pivot.PageFields,
            _ => (IReadOnlyList<PivotFieldModel>)[],
        };

        foreach (var field in fields)
        {
            if (field.SourceFieldIndex != target.SourceFieldIndex)
                continue;
            if (field.SelectedItems is { Count: > 0 } items)
                return items;
            if (!string.IsNullOrWhiteSpace(field.SelectedItem))
                return [field.SelectedItem];
            return null;
        }

        return null;
    }

    // ── Label filter (Equals / Contains / Begins With / …) ─────────────────────
    private async Task OpenPivotLabelFilterDialogAsync(PivotTableModel pivot, PivotHeaderDropdownTargetModel target)
    {
        if (_isOpening || _isSaving)
            return;

        var existing = pivot.LabelFilters.FirstOrDefault(filter => filter.SourceFieldIndex == target.SourceFieldIndex);

        var kindBox = new ComboBox { MinWidth = 200 };
        foreach (var (label, _) in PivotFieldFilterPlanner.LabelFilterKinds)
            kindBox.Items.Add(label);
        kindBox.SelectedIndex = PivotFieldFilterPlanner.FindLabelKindIndex(existing?.Kind ?? PivotLabelFilterKind.Equals);
        AutomationProperties.SetAutomationId(kindBox, "PivotLabelFilterKindBox");
        AutomationProperties.SetName(kindBox, "Label filter kind");

        var value1 = new TextBox { MinWidth = 200, Text = existing?.Value ?? string.Empty, PlaceholderText = UiText.Get("PivotLoc_ValuePlaceholder") };
        AutomationProperties.SetAutomationId(value1, "PivotLabelFilterValueBox");
        AutomationProperties.SetName(value1, "Value");
        var value2 = new TextBox { MinWidth = 200, Text = existing?.Value2 ?? string.Empty, PlaceholderText = UiText.Get("PivotLoc_SecondValuePlaceholder") };
        AutomationProperties.SetAutomationId(value2, "PivotLabelFilterValue2Box");
        AutomationProperties.SetName(value2, "Second value");

        void SyncSecond()
        {
            var kind = PivotFieldFilterPlanner.LabelKindFromIndex(kindBox.SelectedIndex);
            value2.IsVisible = PivotFieldFilterPlanner.LabelKindNeedsSecondValue(kind);
        }

        kindBox.SelectionChanged += (_, _) => SyncSecond();
        SyncSecond();

        var dialog = new Window
        {
            Title = UiText.Format("PivotFilter_LabelTitle", target.FieldCaption),
            Width = 320,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotLabelFilterDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "PivotLabelFilterOkButton");
        var clear = new Button { Content = UiText.Get("Common_Clear"), MinWidth = 80, IsEnabled = existing is not null };
        AutomationProperties.SetAutomationId(clear, "PivotLabelFilterClearButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "PivotLabelFilterCancelButton");
        cancel.Click += (_, _) => dialog.Close(0);
        ok.Click += (_, _) =>
        {
            var kind = PivotFieldFilterPlanner.LabelKindFromIndex(kindBox.SelectedIndex);
            if (!PivotFieldFilterPlanner.TryCreateLabelFilter(
                    target.SourceFieldIndex, kind, value1.Text, value2.Text, out _, out var error))
            {
                ShowEditIssue(error ?? PivotFieldFilterPlanner.LabelValueRequiredMessage);
                return;
            }

            dialog.Close(1);
        };
        clear.Click += (_, _) => dialog.Close(2);

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("PivotFilter_LabelHeading"),
            Foreground = HeaderForeground,
        });
        content.Children.Add(kindBox);
        content.Children.Add(value1);
        content.Children.Add(value2);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, clear, cancel },
        });
        dialog.Content = content;

        var result = await dialog.ShowDialog<int>(this);
        if (result == 0)
            return;

        PivotLabelFilterModel? filter = null;
        if (result == 1)
        {
            var kind = PivotFieldFilterPlanner.LabelKindFromIndex(kindBox.SelectedIndex);
            if (!PivotFieldFilterPlanner.TryCreateLabelFilter(
                    target.SourceFieldIndex, kind, value1.Text, value2.Text, out filter, out var error))
            {
                ShowEditIssue(error ?? PivotFieldFilterPlanner.LabelValueRequiredMessage);
                return;
            }
        }

        var labelFilters = PivotFieldFilterPlanner.ReplaceFieldLabelFilter(
            pivot.LabelFilters, target.SourceFieldIndex, filter);

        ExecutePivotFilterCommand(
            pivot,
            pivot.RowFields.ToList(),
            pivot.ColumnFields.ToList(),
            pivot.PageFields.ToList(),
            labelFilters,
            pivot.ValueFilters.ToList());
    }

    // ── Value filter (Top N / Greater Than / Between / …) ──────────────────────
    private async Task OpenPivotValueFilterDialogAsync(PivotTableModel pivot, PivotHeaderDropdownTargetModel target)
    {
        if (_isOpening || _isSaving)
            return;

        if (pivot.DataFields.Count == 0)
        {
            ShowEditIssue(UiText.Get("PivotLoc_AddValueFieldBeforeFilter"));
            return;
        }

        var existing = pivot.ValueFilters
            .FirstOrDefault(filter => filter.SourceFieldIndex == target.SourceFieldIndex);

        var kindBox = new ComboBox { MinWidth = 200 };
        foreach (var (label, _) in PivotFieldFilterPlanner.ValueFilterKinds)
            kindBox.Items.Add(label);
        kindBox.SelectedIndex = PivotFieldFilterPlanner.FindValueKindIndex(existing?.Kind ?? PivotValueFilterKind.GreaterThan);
        AutomationProperties.SetAutomationId(kindBox, "PivotValueFilterKindBox");
        AutomationProperties.SetName(kindBox, "Value filter kind");

        var dataFieldBox = new ComboBox { MinWidth = 200 };
        for (var index = 0; index < pivot.DataFields.Count; index++)
            dataFieldBox.Items.Add(pivot.DataFields[index].Name);
        dataFieldBox.SelectedIndex = PivotFieldFilterPlanner.InitialDataFieldIndex(existing, pivot.DataFields.Count);
        AutomationProperties.SetAutomationId(dataFieldBox, "PivotValueFilterDataFieldBox");
        AutomationProperties.SetName(dataFieldBox, "Summarize by");

        var primary = new TextBox
        {
            MinWidth = 200,
            PlaceholderText = UiText.Get("PivotLoc_CountOrValuePlaceholder"),
            Text = PivotFieldFilterPlanner.PrimaryInputText(existing),
        };
        AutomationProperties.SetAutomationId(primary, "PivotValueFilterPrimaryBox");
        AutomationProperties.SetName(primary, "Count or value");
        var secondary = new TextBox
        {
            MinWidth = 200,
            PlaceholderText = UiText.Get("PivotLoc_SecondValuePlaceholder"),
            Text = PivotFieldFilterPlanner.SecondaryInputText(existing),
        };
        AutomationProperties.SetAutomationId(secondary, "PivotValueFilterSecondaryBox");
        AutomationProperties.SetName(secondary, "Second value");

        void SyncInputs()
        {
            var kind = PivotFieldFilterPlanner.ValueKindFromIndex(kindBox.SelectedIndex);
            primary.IsVisible = PivotFieldFilterPlanner.ValueKindNeedsPrimaryInput(kind);
            secondary.IsVisible = PivotFieldFilterPlanner.ValueKindNeedsSecondValue(kind);
        }

        kindBox.SelectionChanged += (_, _) => SyncInputs();
        SyncInputs();

        var dialog = new Window
        {
            Title = UiText.Format("PivotFilter_ValueTitle", target.FieldCaption),
            Width = 320,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotValueFilterDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "PivotValueFilterOkButton");
        var clear = new Button { Content = UiText.Get("Common_Clear"), MinWidth = 80, IsEnabled = existing is not null };
        AutomationProperties.SetAutomationId(clear, "PivotValueFilterClearButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "PivotValueFilterCancelButton");
        cancel.Click += (_, _) => dialog.Close(0);
        ok.Click += (_, _) =>
        {
            var kind = PivotFieldFilterPlanner.ValueKindFromIndex(kindBox.SelectedIndex);
            if (!PivotFieldFilterPlanner.TryCreateValueFilter(
                    target.SourceFieldIndex,
                    dataFieldBox.SelectedIndex,
                    kind,
                    primary.Text,
                    secondary.Text,
                    out _,
                    out var error))
            {
                ShowEditIssue(error ?? PivotFieldFilterPlanner.NumericValueRequiredMessage);
                return;
            }

            dialog.Close(1);
        };
        clear.Click += (_, _) => dialog.Close(2);

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotFilter_SummarizeBy"), Foreground = HeaderForeground });
        content.Children.Add(dataFieldBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotFilter_WhereValueIs"), Foreground = HeaderForeground });
        content.Children.Add(kindBox);
        content.Children.Add(primary);
        content.Children.Add(secondary);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Children = { ok, clear, cancel },
        });
        dialog.Content = content;

        var result = await dialog.ShowDialog<int>(this);
        if (result == 0)
            return;

        PivotValueFilterModel? filter = null;
        if (result == 1)
        {
            var kind = PivotFieldFilterPlanner.ValueKindFromIndex(kindBox.SelectedIndex);
            if (!PivotFieldFilterPlanner.TryCreateValueFilter(
                    target.SourceFieldIndex,
                    dataFieldBox.SelectedIndex,
                    kind,
                    primary.Text,
                    secondary.Text,
                    out filter,
                    out var error))
            {
                ShowEditIssue(error ?? PivotFieldFilterPlanner.NumericValueRequiredMessage);
                return;
            }
        }

        var valueFilters = PivotFieldFilterPlanner.ReplaceFieldValueFilter(
            pivot.ValueFilters, target.SourceFieldIndex, filter);

        ExecutePivotFilterCommand(
            pivot,
            pivot.RowFields.ToList(),
            pivot.ColumnFields.ToList(),
            pivot.PageFields.ToList(),
            pivot.LabelFilters.ToList(),
            valueFilters);
    }

    // ── Shared command execution + member reading ─────────────────────────────
    private void ExecutePivotFilterCommand(
        PivotTableModel pivot,
        IReadOnlyList<PivotFieldModel> rowFields,
        IReadOnlyList<PivotFieldModel> columnFields,
        IReadOnlyList<PivotFieldModel> pageFields,
        IReadOnlyList<PivotLabelFilterModel> labelFilters,
        IReadOnlyList<PivotValueFilterModel> valueFilters)
    {
        var command = new ConfigurePivotTableFieldFiltersCommand(
            _session.ActiveSheet.Id,
            pivot.Name,
            rowFields,
            columnFields,
            pageFields,
            labelFilters,
            valueFilters,
            pivot.Sorts.ToList());

        var result = _session.ExecuteReviewCommand(command);
        if (!result.Success)
        {
            ShowEditIssue(result.ErrorMessage ?? UiText.Get("PivotLoc_FilterFailed"));
            return;
        }

        _pivotPaneSignature = null;
        RefreshShell(command.Label);
    }

    /// <summary>
    /// Distinct member labels of a source field, in first-seen order, formatted to match the refresh
    /// service's group-key text (so a checked item agrees with what the engine keeps). Reads the source
    /// range column below the header row.
    /// </summary>
    private IReadOnlyList<string> ReadPivotFieldMembers(PivotTableModel pivot, int sourceFieldIndex)
    {
        var sourceSheet = _session.Workbook.GetSheet(pivot.SourceRange.Start.Sheet);
        if (sourceSheet is null || sourceFieldIndex < 0)
            return [];

        var col = pivot.SourceRange.Start.Col + (uint)sourceFieldIndex;
        if (col > pivot.SourceRange.End.Col)
            return [];

        var seen = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var members = new List<string>();
        for (var row = pivot.SourceRange.Start.Row + 1; row <= pivot.SourceRange.End.Row; row++)
        {
            var text = MemberKeyText(sourceSheet.GetCell(row, col)?.Value);
            if (seen.Add(text))
                members.Add(text);
        }

        return members;
    }

    // Mirrors PivotTableRefreshService.KeyText so checkbox labels match the engine's group keys.
    private static string MemberKeyText(ScalarValue? value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(CultureInfo.CurrentCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue date => date.ToDateTime().ToShortDateString(),
        ErrorValue error => error.Code,
        _ => "(blank)",
    };
}
