using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "Group Field" / "Ungroup" PivotTable dialogs for the Avalonia/macOS shell: the Group dialog
/// picks a layout field (rows/columns/filters), a group-by mode (Year / Quarter / Month / Day / Number range),
/// and — for a number range — the starting/ending/by inputs; Ungroup clears grouping off the chosen field.
/// All catalogs, the current-grouping capture, the start/end/by validation, the result <see cref="PivotFieldModel"/>
/// building, and the row/column/page layout rewrite come from the portable <see cref="PivotGroupFieldPlanner"/>
/// so the behavior is single-sourced with the WPF host and reusable on macOS. The rewritten layout round-trips
/// through the shared Pivot application session (the same command policy the desktop host's grouping
/// uses), carrying the existing calculated fields/items untouched. Reached from the Analyze ▸ Group Field /
/// Ungroup ribbon commands (<c>pivotAnalyze.groupField</c> / <c>pivotAnalyze.ungroup</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Analyze ▸ Group Field — opens the grouping dialog for the active pivot.</summary>
    private void OpenPivotGroupField()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        RunGuarded(() => OpenPivotGroupFieldDialogAsync(pivot!));
    }

    /// <summary>
    /// Analyze ▸ Ungroup — clears grouping off the active pivot's first grouped field (falling back to the
    /// first layout field), without a dialog, matching the desktop host's direct-action Ungroup button.
    /// </summary>
    private void UngroupPivotField()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        var field = FirstGroupedOrLayoutField(pivot!);
        if (field is null)
        {
            ShowEditIssue(UiText.Get("PivotGroup_NoField"));
            return;
        }

        var headers = PivotApplication.ReadSourceHeaders(
            new PivotApplicationTarget(_session.ActiveSheet, pivot!));
        var caption = PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex);
        var submission = PivotGroupFieldPlanner.CreateSubmission(
            caption,
            field.SourceFieldIndex,
            PivotFieldGrouping.None,
            ungroup: true,
            start: null,
            end: null,
            interval: null);
        ApplyPivotGrouping(pivot!, submission, UiText.Format("PivotGroup_Ungrouped", caption));
    }

    private async Task OpenPivotGroupFieldDialogAsync(PivotTableModel pivot)
    {
        if (_isOpening || _isSaving)
            return;

        var headers = PivotApplication.ReadSourceHeaders(
            new PivotApplicationTarget(_session.ActiveSheet, pivot));
        var layoutFields = LayoutFieldOptions(pivot, headers);
        if (layoutFields.Count == 0)
        {
            ShowEditIssue(UiText.Get("PivotGroup_NoField"));
            return;
        }

        var fieldBox = new ComboBox { MinWidth = 240 };
        foreach (var option in layoutFields)
            fieldBox.Items.Add(option.Caption);
        fieldBox.SelectedIndex = 0;
        ApplyPivotComboBoxChrome(fieldBox);
        AutomationProperties.SetAutomationId(fieldBox, "PivotGroupFieldBox");
        AutomationProperties.SetName(fieldBox, UiText.Get("PivotGroup_FieldLabel"));

        var groupingBox = new ComboBox { MinWidth = 240 };
        foreach (var (label, _) in PivotGroupFieldPlanner.Groupings)
            groupingBox.Items.Add(label);
        ApplyPivotComboBoxChrome(groupingBox);
        AutomationProperties.SetAutomationId(groupingBox, "PivotGroupByBox");
        AutomationProperties.SetName(groupingBox, UiText.Get("PivotGroup_GroupByLabel"));

        var startBox = new TextBox { MinWidth = 120 };
        ApplyPivotTextBoxChrome(startBox);
        AutomationProperties.SetAutomationId(startBox, "PivotGroupStartBox");
        AutomationProperties.SetName(startBox, UiText.Get("PivotGroup_StartingAtLabel"));
        var endBox = new TextBox { MinWidth = 120 };
        ApplyPivotTextBoxChrome(endBox);
        AutomationProperties.SetAutomationId(endBox, "PivotGroupEndBox");
        AutomationProperties.SetName(endBox, UiText.Get("PivotGroup_EndingAtLabel"));
        var intervalBox = new TextBox { MinWidth = 120 };
        ApplyPivotTextBoxChrome(intervalBox);
        AutomationProperties.SetAutomationId(intervalBox, "PivotGroupByValueBox");
        AutomationProperties.SetName(intervalBox, UiText.Get("PivotGroup_ByLabel"));

        var rangePanel = new StackPanel { Spacing = 6 };
        rangePanel.Children.Add(new TextBlock { Text = UiText.Get("PivotGroup_StartingAtLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        rangePanel.Children.Add(startBox);
        rangePanel.Children.Add(new TextBlock { Text = UiText.Get("PivotGroup_EndingAtLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        rangePanel.Children.Add(endBox);
        rangePanel.Children.Add(new TextBlock { Text = UiText.Get("PivotGroup_ByLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        rangePanel.Children.Add(intervalBox);

        void LoadFromField(PivotGroupFieldOption option)
        {
            var current = PivotGroupFieldPlanner.FindLayoutField(pivot, option.SourceFieldIndex);
            groupingBox.SelectedIndex = PivotGroupFieldPlanner.FindGroupingIndex(current?.Grouping ?? PivotFieldGrouping.None);
            startBox.Text = PivotGroupFieldPlanner.FormatBound(current?.GroupStart);
            endBox.Text = PivotGroupFieldPlanner.FormatBound(current?.GroupEnd);
            intervalBox.Text = PivotGroupFieldPlanner.FormatBound(current?.GroupInterval);
        }

        void SyncRangeState()
        {
            var grouping = PivotGroupFieldPlanner.GroupingFromIndex(groupingBox.SelectedIndex);
            rangePanel.IsVisible = PivotGroupFieldPlanner.GroupingUsesNumberRange(grouping);
        }

        fieldBox.SelectionChanged += (_, _) =>
        {
            if (fieldBox.SelectedIndex >= 0 && fieldBox.SelectedIndex < layoutFields.Count)
                LoadFromField(layoutFields[fieldBox.SelectedIndex]);
        };
        groupingBox.SelectionChanged += (_, _) => SyncRangeState();
        LoadFromField(layoutFields[0]);
        SyncRangeState();

        var dialog = new Window
        {
            Title = UiText.Format("PivotGroup_Title", pivot.Name),
            Width = 420,
            Height = 430,
            MinWidth = 420,
            MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotGroupFieldDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyPivotButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotGroupFieldOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyPivotButtonChrome(cancel, 80);
        AutomationProperties.SetAutomationId(cancel, "PivotGroupFieldCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            var grouping = PivotGroupFieldPlanner.GroupingFromIndex(groupingBox.SelectedIndex);
            if (!PivotGroupFieldPlanner.TryValidate(
                    grouping, ungroup: false, startBox.Text, endBox.Text, intervalBox.Text,
                    out _, out _, out _, out var error))
            {
                ShowEditIssue(error ?? PivotGroupFieldPlanner.InvalidStartMessage);
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotGroup_FieldLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(fieldBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotGroup_GroupByLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        content.Children.Add(groupingBox);
        content.Children.Add(rangePanel);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = content;
        ConfigurePivotDialogLifecycle(dialog, fieldBox);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        var selected = fieldBox.SelectedIndex >= 0 && fieldBox.SelectedIndex < layoutFields.Count
            ? layoutFields[fieldBox.SelectedIndex]
            : layoutFields[0];
        var selectedGrouping = PivotGroupFieldPlanner.GroupingFromIndex(groupingBox.SelectedIndex);
        if (!PivotGroupFieldPlanner.TryCreateSubmission(
                selected.Caption,
                selected.SourceFieldIndex,
                selectedGrouping,
                ungroup: false,
                startBox.Text,
                endBox.Text,
                intervalBox.Text,
                out var submission,
                out var lateError))
        {
            ShowEditIssue(lateError ?? PivotGroupFieldPlanner.InvalidStartMessage);
            return;
        }

        var status = selectedGrouping == PivotFieldGrouping.None
            ? UiText.Format("PivotGroup_Ungrouped", selected.Caption)
            : UiText.Format("PivotGroup_Grouped", selected.Caption);
        ApplyPivotGrouping(pivot, submission!, status);
    }

    private void ApplyPivotGrouping(PivotTableModel pivot, PivotGroupFieldSubmission submission, string status)
    {
        var layout = PivotGroupFieldPlanner.BuildLayout(pivot, submission.Field);
        ApplyPivotApplicationPlan(
            PivotApplication.PlanCalculatedConfiguration(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                layout.RowFields,
                layout.ColumnFields,
                layout.PageFields,
                pivot.CalculatedFields.ToList(),
                pivot.CalculatedItems.ToList()),
            status);
    }

    // The row/column/page fields placed in the layout, captioned, in the order they appear (rows, columns,
    // then page/filter fields) — the candidates the Group dialog lets the user pick from.
    private static IReadOnlyList<PivotGroupFieldOption> LayoutFieldOptions(
        PivotTableModel pivot,
        IReadOnlyList<string> headers)
    {
        var options = new List<PivotGroupFieldOption>();
        var seen = new HashSet<int>();
        foreach (var field in pivot.RowFields.Concat(pivot.ColumnFields).Concat(pivot.PageFields))
        {
            if (seen.Add(field.SourceFieldIndex))
            {
                options.Add(new PivotGroupFieldOption(
                    field.SourceFieldIndex,
                    PivotFieldListPaneBuilder.FieldCaption(headers, field.SourceFieldIndex)));
            }
        }

        return options;
    }

    private static PivotFieldModel? FirstGroupedOrLayoutField(PivotTableModel pivot)
    {
        var layout = pivot.RowFields.Concat(pivot.ColumnFields).Concat(pivot.PageFields).ToList();
        return layout.FirstOrDefault(field => field.Grouping != PivotFieldGrouping.None)
            ?? layout.FirstOrDefault();
    }

    private sealed record PivotGroupFieldOption(int SourceFieldIndex, string Caption);
}
