using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
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
    // ── Shared pivot-dialog chrome helpers ───────────────────────────────────
    // Defined here (in the first pivot partial) so all sibling pivot partials can call them.

    private static AvaloniaCompactDialogChromeStyle PivotDialogChromeStyle => new(FormulaBarFontFamily);

    private static void ApplyPivotButtonChrome(Button button, double minWidth, bool isDefault = false)
    {
        AvaloniaCompactDialogChrome.ApplyButton(button, PivotDialogChromeStyle, minWidth, isDefault);
    }

    private static void ApplyPivotTextBoxChrome(TextBox textBox, bool fixedHeight = true)
    {
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, PivotDialogChromeStyle, fixedHeight);
    }

    private static void ApplyPivotComboBoxChrome(ComboBox comboBox)
    {
        AvaloniaCompactDialogChrome.ApplyComboBox(comboBox, PivotDialogChromeStyle);
    }

    private static void ApplyPivotCheckBoxChrome(CheckBox checkBox)
    {
        AvaloniaCompactDialogChrome.ApplyCheckBox(checkBox, PivotDialogChromeStyle);
    }

    private static void ApplyPivotRadioButtonChrome(RadioButton radioButton)
    {
        AvaloniaCompactDialogChrome.ApplyRadioButton(radioButton, PivotDialogChromeStyle);
    }

    private static void ApplyPivotListBoxChrome(ListBox listBox)
    {
        AvaloniaCompactDialogChrome.ApplyListBox(listBox, PivotDialogChromeStyle);
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
        };
        ApplyPivotCheckBoxChrome(rowGrandTotalsBox);
        AutomationProperties.SetAutomationId(rowGrandTotalsBox, "PivotOptionsRowGrandTotalsBox");
        var columnGrandTotalsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_ShowColumnGrandTotals"),
            IsChecked = values.ShowColumnGrandTotals,
        };
        ApplyPivotCheckBoxChrome(columnGrandTotalsBox);
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
        };
        ApplyPivotCheckBoxChrome(subtotalsBox);
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
        };
        ApplyPivotCheckBoxChrome(repeatLabelsBox);
        AutomationProperties.SetAutomationId(repeatLabelsBox, "PivotOptionsRepeatLabelsBox");

        var blankRowBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_InsertBlankRow"),
            IsChecked = values.BlankLineAfterItems,
        };
        ApplyPivotCheckBoxChrome(blankRowBox);
        AutomationProperties.SetAutomationId(blankRowBox, "PivotOptionsBlankRowBox");

        var mergeLabelsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_MergeAndCenterLabels"),
            IsChecked = values.MergeAndCenterLabels,
        };
        ApplyPivotCheckBoxChrome(mergeLabelsBox);
        AutomationProperties.SetAutomationId(mergeLabelsBox, "PivotOptionsMergeLabelsBox");

        void SyncSubtotalState()
        {
            subtotalPlacementBox.IsEnabled = subtotalsBox.IsChecked == true;
        }

        subtotalsBox.IsCheckedChanged += (_, _) => SyncSubtotalState();
        SyncSubtotalState();

        var dialog = new Window
        {
            Title = UiText.Get("PivotTableOptions_PivotTableOptions"),
            Width = PivotOptionsPlanner.DialogWidth,
            MinHeight = PivotOptionsPlanner.DialogMinHeight,
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

        // ── Tab: Layout & Format ───────────────────────────────────────────────
        var layoutSection = new StackPanel { Spacing = 6 };
        layoutSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_ReportLayoutLabel")));
        layoutSection.Children.Add(reportLayoutBox);
        layoutSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_CompactIndentLabel")));
        layoutSection.Children.Add(compactIndentBox);
        layoutSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_ReportFilterAreaLabel")));
        // Page-field-layout labels mirror the WPF host's non-localized literals.
        layoutSection.Children.Add(OptionComboBox(new[] { "Down, then over", "Over, then down" }));
        layoutSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_ReportFilterFieldsPerColumnLabel")));
        layoutSection.Children.Add(OptionTextBox("0", 80));
        layoutSection.Children.Add(repeatLabelsBox);
        layoutSection.Children.Add(blankRowBox);
        layoutSection.Children.Add(mergeLabelsBox);

        var formatSection = new StackPanel { Spacing = 6 };
        formatSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_EmptyCellsLabel")));
        formatSection.Children.Add(OptionTextBox(string.Empty, 160));
        formatSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_ErrorValuesLabel")));
        formatSection.Children.Add(OptionTextBox(string.Empty, 160));
        formatSection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_AutofitColumnWidthsOnUpdate"), true));
        formatSection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_PreserveCellFormattingOnUpdate"), true));

        var layoutTab = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        layoutTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_LayoutSectionGroup"), layoutSection));
        layoutTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_FormatSectionGroup"), formatSection));
        layoutTab.Children.Add(new Border { Height = PivotOptionsPlanner.LayoutAndFormatAvaloniaSpacerHeight });

        // ── Tab: Totals & Filters ──────────────────────────────────────────────
        var grandTotalsSection = new StackPanel { Spacing = 4 };
        grandTotalsSection.Children.Add(rowGrandTotalsBox);
        grandTotalsSection.Children.Add(columnGrandTotalsBox);

        var subtotalsSection = new StackPanel { Spacing = 6 };
        subtotalsSection.Children.Add(subtotalsBox);
        subtotalsSection.Children.Add(subtotalPlacementBox);

        var totalsTab = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        totalsTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_GrandTotalsGroup"), grandTotalsSection));
        totalsTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_FiltersAndSubtotalsGroup"), subtotalsSection));

        // ── Tab: Display ───────────────────────────────────────────────────────
        var displaySection = new StackPanel { Spacing = 6 };
        displaySection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_PivotTableStyleLabel")));
        displaySection.Children.Add(OptionComboBox(new[] { pivot.StyleName }));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_RowHeaders"), pivot.ShowRowHeaders));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_ColumnHeaders"), pivot.ShowColumnHeaders));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_DisplayFieldCaptionsAndFilterDropDowns"), pivot.ShowFieldHeaders));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_ShowContextualTooltips"), true));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_ShowPropertiesInTooltips"), true));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_ClassicPivotTableLayoutEnablesDraggingOfFieldsInTheGrid"), false));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_ShowItemsWithNoDataOnRows"), false));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_ShowItemsWithNoDataOnColumns"), false));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_BandedRows"), pivot.ShowRowStripes));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_BandedColumns"), pivot.ShowColumnStripes));
        displaySection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_ShowExpandCollapseButtons"), true));
        var displayTab = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        displayTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_PivotTableStyleOptionsGroup"), displaySection));

        // ── Tab: Printing ──────────────────────────────────────────────────────
        var printSection = new StackPanel { Spacing = 6 };
        printSection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_SetPrintTitles"), false));
        printSection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_PrintExpandCollapseButtonsWhenDisplayedOnPivotTable"), false));
        var printTab = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        printTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_PrintOptionsGroup"), printSection));

        // ── Tab: Data ──────────────────────────────────────────────────────────
        var dataSection = new StackPanel { Spacing = 6 };
        dataSection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_RefreshDataWhenOpeningTheFile"), false));
        dataSection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_SaveSourceDataWithFile"), true));
        dataSection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_EnableRefresh"), true));
        dataSection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_EnableShowDetails"), true));
        dataSection.Children.Add(OptionCheckBox(UiText.Get("PivotTableOptions_PreserveSourceSortAndFilterSettings"), true));
        var dataTab = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        dataTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_DataOptionsGroup"), dataSection));

        // ── Tab: Alt Text ──────────────────────────────────────────────────────
        var altSection = new StackPanel { Spacing = 6 };
        altSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_TitleLabel")));
        altSection.Children.Add(OptionTextBox(string.Empty, 320));
        altSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_DescriptionLabel")));
        altSection.Children.Add(OptionTextBox(string.Empty, 320));
        var altTab = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        altTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_AltTextGroup"), altSection));

        var tabs = new TabControl
        {
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 12),
            Items =
            {
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotTableOptions_LayoutAndFormat")), Content = layoutTab, FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotTableOptions_TotalsAndFilters")), Content = totalsTab, FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotTableOptions_Display")), Content = displayTab, FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotTableOptions_Printing")), Content = printTab, FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotTableOptions_Data")), Content = dataTab, FontSize = 12, FontFamily = FormulaBarFontFamily },
                new TabItem { Header = StripDisplayMnemonic(UiText.Get("PivotTableOptions_AltText")), Content = altTab, FontSize = 12, FontFamily = FormulaBarFontFamily },
            },
        };
        AutomationProperties.SetAutomationId(tabs, "PivotTableOptionsTabs");
        AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs);

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel]);

        var content = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        content.Children.Add(buttonRow);
        content.Children.Add(tabs);
        dialog.Content = content;

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

    // ── Display-only option control factories ─────────────────────────────────
    // These render the WPF dialog's full tab surface for visual parity. Only the
    // nine totals/layout/subtotal controls above are wired to the options command;
    // the rest are presentation-only (the Avalonia options command does not carry
    // them), matching the WPF reference structure.
    private static TextBlock OptionLabel(string text) => new()
    {
        Text = StripDisplayMnemonic(text),
        FontSize = 12,
        FontFamily = FormulaBarFontFamily,
        Foreground = HeaderForeground,
    };

    private static CheckBox OptionCheckBox(string text, bool isChecked)
    {
        var checkBox = new CheckBox
        {
            Content = StripDisplayMnemonic(text),
            IsChecked = isChecked,
        };
        ApplyPivotCheckBoxChrome(checkBox);
        return checkBox;
    }

    private static TextBox OptionTextBox(string text, double minWidth)
    {
        var box = new TextBox
        {
            Text = text,
            MinWidth = minWidth,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Left,
        };
        ApplyPivotTextBoxChrome(box);
        return box;
    }

    private static ComboBox OptionComboBox(IReadOnlyList<string?> items)
    {
        var box = new ComboBox { MinWidth = 220 };
        foreach (var item in items)
            box.Items.Add(item ?? string.Empty);
        box.SelectedIndex = items.Count > 0 ? 0 : -1;
        ApplyPivotComboBoxChrome(box);
        return box;
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
            Text = StripDisplayMnemonic(label),
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
