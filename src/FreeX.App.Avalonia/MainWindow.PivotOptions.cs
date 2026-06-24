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
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;

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
    // ── Shared pivot-dialog chrome helpers ───────────────────────────────────
    // Defined here (in the first pivot partial) so all sibling pivot partials can call them.

    private static void ApplyPivotButtonChrome(Button button, double minWidth, bool isDefault = false)
    {
        button.MinWidth = minWidth;
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

    private static void ApplyPivotTextBoxChrome(TextBox textBox)
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

    private static void ApplyPivotComboBoxChrome(ComboBox comboBox)
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

    // ── PivotTable Options dialog ─────────────────────────────────────────────

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
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(rowGrandTotalsBox, "PivotOptionsRowGrandTotalsBox");
        var columnGrandTotalsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_ShowColumnGrandTotals"),
            IsChecked = values.ShowColumnGrandTotals,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(columnGrandTotalsBox, "PivotOptionsColumnGrandTotalsBox");

        var reportLayoutBox = new ComboBox { MinWidth = 220 };
        foreach (var (label, _) in PivotOptionsPlanner.ReportLayouts)
            reportLayoutBox.Items.Add(label);
        reportLayoutBox.SelectedIndex = PivotOptionsPlanner.FindReportLayoutIndex(values.ReportLayout);
        ApplyPivotComboBoxChrome(reportLayoutBox);
        AutomationProperties.SetAutomationId(reportLayoutBox, "PivotOptionsReportLayoutBox");
        AutomationProperties.SetName(reportLayoutBox, "Report layout");

        var compactIndentBox = new TextBox
        {
            MinWidth = 80,
            Text = PivotOptionsPlanner.CompactRowLabelIndentText(values.CompactRowLabelIndent),
        };
        ApplyPivotTextBoxChrome(compactIndentBox);
        AutomationProperties.SetAutomationId(compactIndentBox, "PivotOptionsCompactIndentBox");
        AutomationProperties.SetName(compactIndentBox, "Compact form row label indent");

        var subtotalsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_ShowSubtotals"),
            IsChecked = values.ShowSubtotals,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(subtotalsBox, "PivotOptionsSubtotalsBox");

        var subtotalPlacementBox = new ComboBox { MinWidth = 220 };
        foreach (var (label, _) in PivotOptionsPlanner.SubtotalPlacements)
            subtotalPlacementBox.Items.Add(label);
        subtotalPlacementBox.SelectedIndex =
            PivotOptionsPlanner.FindSubtotalPlacementIndex(values.SubtotalPlacement);
        ApplyPivotComboBoxChrome(subtotalPlacementBox);
        AutomationProperties.SetAutomationId(subtotalPlacementBox, "PivotOptionsSubtotalPlacementBox");
        AutomationProperties.SetName(subtotalPlacementBox, "Subtotal placement");

        var repeatLabelsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_RepeatItemLabels"),
            IsChecked = values.RepeatItemLabels,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(repeatLabelsBox, "PivotOptionsRepeatLabelsBox");

        var blankRowBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_InsertBlankRow"),
            IsChecked = values.BlankLineAfterItems,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
        };
        AutomationProperties.SetAutomationId(blankRowBox, "PivotOptionsBlankRowBox");

        var mergeLabelsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_MergeAndCenterLabels"),
            IsChecked = values.MergeAndCenterLabels,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
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
            Width = 520,
            MinHeight = 500,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AutomationProperties.SetAutomationId(dialog, "PivotTableOptionsDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 80 };
        ApplyPivotButtonChrome(ok, 80, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotTableOptionsOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 80 };
        ApplyPivotButtonChrome(cancel, 80);
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

        // Grand Totals section (WPF-style groupbox framing)
        var grandTotalsGroup = new StackPanel { Spacing = 4 };
        grandTotalsGroup.Children.Add(rowGrandTotalsBox);
        grandTotalsGroup.Children.Add(columnGrandTotalsBox);

        // Layout section (WPF-style groupbox framing)
        var layoutGroup = new StackPanel { Spacing = 6 };
        layoutGroup.Children.Add(new TextBlock { Text = UiText.Get("PivotOptions_ReportLayoutLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        layoutGroup.Children.Add(reportLayoutBox);
        layoutGroup.Children.Add(new TextBlock { Text = UiText.Get("PivotOptions_CompactIndentLabel"), FontSize = 12, FontFamily = FormulaBarFontFamily, Foreground = HeaderForeground });
        layoutGroup.Children.Add(compactIndentBox);
        layoutGroup.Children.Add(repeatLabelsBox);
        layoutGroup.Children.Add(blankRowBox);
        layoutGroup.Children.Add(mergeLabelsBox);

        // Subtotals section (WPF-style groupbox framing)
        var subtotalsGroup = new StackPanel { Spacing = 6 };
        subtotalsGroup.Children.Add(subtotalsBox);
        subtotalsGroup.Children.Add(subtotalPlacementBox);

        var content = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        content.Children.Add(MakeSectionGroupBox(UiText.Get("PivotOptions_GrandTotalsHeader"), grandTotalsGroup));
        content.Children.Add(MakeSectionGroupBox(UiText.Get("PivotOptions_LayoutHeader"), layoutGroup));
        content.Children.Add(MakeSectionGroupBox(UiText.Get("PivotOptions_SubtotalsHeader"), subtotalsGroup));
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0),
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
        FontSize = 12,
        FontFamily = FormulaBarFontFamily,
        FontWeight = FontWeight.SemiBold,
        Foreground = HeaderForeground,
        Margin = new Thickness(0, 6, 0, 0),
    };

    /// <summary>
    /// Creates a WPF-style GroupBox equivalent: a bold section label above a bordered panel that
    /// frames the given content, matching the visual grouping of WPF's PivotTableOptions dialog.
    /// </summary>
    private static StackPanel MakeSectionGroupBox(string label, Control content)
    {
        var wrapper = new StackPanel { Spacing = 4 };
        wrapper.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontFamily = FormulaBarFontFamily,
            FontWeight = FontWeight.SemiBold,
            Foreground = HeaderForeground,
        });
        wrapper.Children.Add(new Border
        {
            BorderBrush = Brush(180, 180, 180),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = content,
        });
        return wrapper;
    }
}
