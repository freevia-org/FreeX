using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "PivotTable Options" dialog for the Avalonia/macOS shell: grand totals on/off for rows and
/// columns, the report layout (Compact / Outline / Tabular) with its compact-form row-label indent, the
/// subtotal display (show + placement), and the blank-row / repeat-labels / merge-labels layout toggles.
/// Input collection lives here; the catalogs, the compact-indent validation, and the result building come from
/// the portable <see cref="PivotOptionsPlanner"/> so the behavior is single-sourced with the WPF host and
/// reusable on macOS. The result round-trips through <see cref="ConfigurePivotTableOptionsCommand"/> (the same
/// command the Design contextual-tab toggles use), carrying only the totals/layout options this dialog edits
/// and leaving every other (cache / print / alt-text / tooltip / style) option untouched. Reached from the
/// Analyze ▸ Options ribbon command (<c>pivotAnalyze.options</c>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// Analyze ▸ Options — opens the PivotTable Options dialog for the active pivot and applies the result
    /// through the shared options command. Reports an honest status when no pivot is active.
    /// </summary>
    private void OpenPivotTableOptions()
    {
        if (!TryBeginPivotOption(out var pivot))
            return;

        _ = OpenPivotTableOptionsDialogAsync(pivot!);
    }

    private async Task OpenPivotTableOptionsDialogAsync(PivotTableModel pivot)
    {
        if (_isOpening || _isSaving)
            return;

        var values = PivotOptionsPlanner.Capture(pivot);

        var rowGrandTotalsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_ShowRowGrandTotals"),
            IsChecked = values.ShowRowGrandTotals,
        };
        AutomationProperties.SetAutomationId(rowGrandTotalsBox, "PivotOptionsRowGrandTotalsBox");
        var columnGrandTotalsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_ShowColumnGrandTotals"),
            IsChecked = values.ShowColumnGrandTotals,
        };
        AutomationProperties.SetAutomationId(columnGrandTotalsBox, "PivotOptionsColumnGrandTotalsBox");

        var reportLayoutBox = new ComboBox { MinWidth = 220 };
        foreach (var (label, _) in PivotOptionsPlanner.ReportLayouts)
            reportLayoutBox.Items.Add(label);
        reportLayoutBox.SelectedIndex = PivotOptionsPlanner.FindReportLayoutIndex(values.ReportLayout);
        AutomationProperties.SetAutomationId(reportLayoutBox, "PivotOptionsReportLayoutBox");
        AutomationProperties.SetName(reportLayoutBox, "Report layout");

        var compactIndentBox = new TextBox
        {
            MinWidth = 80,
            Text = PivotOptionsPlanner.CompactRowLabelIndentText(values.CompactRowLabelIndent),
        };
        AutomationProperties.SetAutomationId(compactIndentBox, "PivotOptionsCompactIndentBox");
        AutomationProperties.SetName(compactIndentBox, "Compact form row label indent");

        var subtotalsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_ShowSubtotals"),
            IsChecked = values.ShowSubtotals,
        };
        AutomationProperties.SetAutomationId(subtotalsBox, "PivotOptionsSubtotalsBox");

        var subtotalPlacementBox = new ComboBox { MinWidth = 220 };
        foreach (var (label, _) in PivotOptionsPlanner.SubtotalPlacements)
            subtotalPlacementBox.Items.Add(label);
        subtotalPlacementBox.SelectedIndex =
            PivotOptionsPlanner.FindSubtotalPlacementIndex(values.SubtotalPlacement);
        AutomationProperties.SetAutomationId(subtotalPlacementBox, "PivotOptionsSubtotalPlacementBox");
        AutomationProperties.SetName(subtotalPlacementBox, "Subtotal placement");

        var repeatLabelsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_RepeatItemLabels"),
            IsChecked = values.RepeatItemLabels,
        };
        AutomationProperties.SetAutomationId(repeatLabelsBox, "PivotOptionsRepeatLabelsBox");

        var blankRowBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_InsertBlankRow"),
            IsChecked = values.BlankLineAfterItems,
        };
        AutomationProperties.SetAutomationId(blankRowBox, "PivotOptionsBlankRowBox");

        var mergeLabelsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_MergeAndCenterLabels"),
            IsChecked = values.MergeAndCenterLabels,
        };
        AutomationProperties.SetAutomationId(mergeLabelsBox, "PivotOptionsMergeLabelsBox");

        void SyncSubtotalState()
        {
            subtotalPlacementBox.IsEnabled = subtotalsBox.IsChecked == true;
        }

        subtotalsBox.IsCheckedChanged += (_, _) => SyncSubtotalState();
        SyncSubtotalState();

        var dialog = new Window
        {
            Title = UiText.Format("PivotOptions_Title", pivot.Name),
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotTableOptionsDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(ok, "PivotTableOptionsOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        AutomationProperties.SetAutomationId(cancel, "PivotTableOptionsCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) =>
        {
            if (!PivotOptionsPlanner.TryParseCompactRowLabelIndent(compactIndentBox.Text, out _, out var error))
            {
                ShowEditIssue(error ?? PivotOptionsPlanner.CompactIndentRangeMessage);
                return;
            }

            dialog.Close(true);
        };

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12) };
        content.Children.Add(SectionHeader(UiText.Get("PivotOptions_GrandTotalsHeader")));
        content.Children.Add(rowGrandTotalsBox);
        content.Children.Add(columnGrandTotalsBox);
        content.Children.Add(SectionHeader(UiText.Get("PivotOptions_LayoutHeader")));
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotOptions_ReportLayoutLabel"), Foreground = HeaderForeground });
        content.Children.Add(reportLayoutBox);
        content.Children.Add(new TextBlock { Text = UiText.Get("PivotOptions_CompactIndentLabel"), Foreground = HeaderForeground });
        content.Children.Add(compactIndentBox);
        content.Children.Add(repeatLabelsBox);
        content.Children.Add(blankRowBox);
        content.Children.Add(mergeLabelsBox);
        content.Children.Add(SectionHeader(UiText.Get("PivotOptions_SubtotalsHeader")));
        content.Children.Add(subtotalsBox);
        content.Children.Add(subtotalPlacementBox);
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { ok, cancel },
        });
        dialog.Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 560,
            Content = content,
        };

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        if (!PivotOptionsPlanner.TryParseCompactRowLabelIndent(compactIndentBox.Text, out var indent, out _))
            indent = values.CompactRowLabelIndent;

        var result = PivotOptionsPlanner.CreateResult(
            rowGrandTotalsBox.IsChecked == true,
            columnGrandTotalsBox.IsChecked == true,
            subtotalsBox.IsChecked == true,
            subtotalPlacementBox.SelectedIndex,
            reportLayoutBox.SelectedIndex,
            indent,
            repeatLabelsBox.IsChecked == true,
            blankRowBox.IsChecked == true,
            mergeLabelsBox.IsChecked == true);

        var command = new ConfigurePivotTableOptionsCommand(
            _session.ActiveSheet.Id,
            pivot.Name,
            showRowGrandTotals: result.ShowRowGrandTotals,
            showColumnGrandTotals: result.ShowColumnGrandTotals,
            showSubtotals: result.ShowSubtotals,
            subtotalPlacement: result.SubtotalPlacement,
            repeatItemLabels: result.RepeatItemLabels,
            blankLineAfterItems: result.BlankLineAfterItems,
            styleName: pivot.StyleName,
            showRowHeaders: pivot.ShowRowHeaders,
            showColumnHeaders: pivot.ShowColumnHeaders,
            showRowStripes: pivot.ShowRowStripes,
            showColumnStripes: pivot.ShowColumnStripes,
            reportLayout: result.ReportLayout,
            compactRowLabelIndent: result.CompactRowLabelIndent,
            showFieldHeaders: pivot.ShowFieldHeaders,
            mergeAndCenterLabels: result.MergeAndCenterLabels);

        ExecutePivotTabCommand(command, "PivotTable options updated.");
    }

    private static TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontWeight = FontWeight.SemiBold,
        Foreground = HeaderForeground,
        Margin = new Thickness(0, 6, 0, 0),
    };
}
