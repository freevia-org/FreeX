using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

using Free.Shared.Shell.Avalonia;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace FreeX.App.Avalonia;

/// <summary>
/// Windows-parity "PivotTable Options" dialog for the Avalonia/macOS shell. The full six-tab option surface
/// is backed by <see cref="PivotOptionsPlanner"/> and the shared Pivot application session, so
/// dialog edits round-trip through the same pivot/cache model as the WPF host.
/// </summary>
public sealed partial class MainWindow
{
    // Avalonia's Display-tab checkbox template measured a 20px row pitch and a 5px earlier first row
    // than the retained WPF authority. Fresh 2026-08-04 Docker/Xvfb bounds after this compensation are
    // approximately 130, 151, 172... versus WPF's 130, 151, 172...; keep these values host-specific.
    private const int AvaloniaDisplayOptionSpacingCompensation = 7;
    private const int AvaloniaDisplayOptionTopInsetCompensation = 3;
    private const int AvaloniaDisplayOptionBottomInsetCompensation = 8;

    // ── Shared pivot-dialog chrome helpers ───────────────────────────────────
    // Defined here (in the first pivot partial) so all sibling pivot partials can call them.

    private static AvaloniaCompactDialogChromeStyle PivotDialogChromeStyle => new(FormulaBarFontFamily)
    {
        ControlHeight = 22,
        ButtonHeight = 20,
        ButtonPadding = new Thickness(12, 1),
    };

    private static void ApplyPivotButtonChrome(Button button, double minWidth, bool isDefault = false)
    {
        AvaloniaCompactDialogChrome.ApplyButton(button, PivotDialogChromeStyle, minWidth, isDefault);
    }

    private static void ApplyPivotTextBoxChrome(TextBox textBox, bool fixedHeight = true)
    {
        AvaloniaCompactDialogChrome.ApplyTextBox(textBox, PivotDialogChromeStyle, fixedHeight);
        if (fixedHeight)
        {
            textBox.Height = 20;
            textBox.MinHeight = 20;
            textBox.MaxHeight = 20;
        }
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

        var cache = _session.Workbook.PivotCaches.FirstOrDefault(candidate => candidate.CacheId == pivot.CacheId);
        var values = PivotOptionsPlanner.CaptureDialogValues(pivot, cache);

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

        var reportLayoutBox = OptionComboBox(
            PivotOptionsPlanner.ReportLayouts.Select(item => (string?)item.Label).ToArray(),
            PivotOptionsPlanner.FindReportLayoutIndex(values.ReportLayout));
        ApplyPivotComboBoxChrome(reportLayoutBox);
        AutomationProperties.SetAutomationId(reportLayoutBox, "PivotOptionsReportLayoutBox");
        AutomationProperties.SetName(
            reportLayoutBox,
            UiText.CreateAutomationName(UiText.Get("PivotTableOptions_ReportLayoutLabel")));

        var compactIndentBox = OptionTextBox(
            PivotOptionsPlanner.CompactRowLabelIndentText(values.CompactRowLabelIndent),
            80);
        compactIndentBox.Width = 60;
        compactIndentBox.MinWidth = 60;
        compactIndentBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Center;
        ApplyPivotTextBoxChrome(compactIndentBox);
        AutomationProperties.SetAutomationId(compactIndentBox, "PivotOptionsCompactIndentBox");
        AutomationProperties.SetName(compactIndentBox, UiText.Get("PivotTableOptions_CompactIndentAutomationName"));

        var subtotalsBox = new CheckBox
        {
            Content = UiText.Get("PivotOptions_ShowSubtotals"),
            IsChecked = values.ShowSubtotals,
        };
        ApplyPivotCheckBoxChrome(subtotalsBox);
        AutomationProperties.SetAutomationId(subtotalsBox, "PivotOptionsSubtotalsBox");

        var subtotalPlacementBox = OptionComboBox(
            PivotOptionsPlanner.SubtotalPlacements.Select(item => (string?)item.Label).ToArray(),
            PivotOptionsPlanner.FindSubtotalPlacementIndex(values.SubtotalPlacement));
        AutomationProperties.SetAutomationId(subtotalPlacementBox, "PivotOptionsSubtotalPlacementBox");
        AutomationProperties.SetName(
            subtotalPlacementBox,
            UiText.CreateAutomationName(UiText.Get("PivotTableOptions_SubtotalPlacementLabel")));

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
            Height = PivotOptionsPlanner.LayoutAndFormatCaptureHeight,
            MinHeight = PivotOptionsPlanner.DialogMinHeight,
            SizeToContent = SizeToContent.Manual,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };
        AvaloniaCompactDialogChrome.ApplyWindow(dialog, PivotDialogChromeStyle);
        AutomationProperties.SetAutomationId(dialog, "PivotTableOptionsDialog");

        var ok = new Button { Content = UiText.Get("Common_Ok"), IsDefault = true, MinWidth = 76 };
        ApplyPivotButtonChrome(ok, 76, isDefault: true);
        AutomationProperties.SetAutomationId(ok, "PivotTableOptionsOkButton");
        var cancel = new Button { Content = UiText.Get("Common_Cancel"), IsCancel = true, MinWidth = 76 };
        ApplyPivotButtonChrome(cancel, 76);
        AutomationProperties.SetAutomationId(cancel, "PivotTableOptionsCancelButton");
        cancel.Click += (_, _) => dialog.Close(false);
        TextBox pageWrapBox = null!;
        TabControl tabs = null!;
        ok.Click += (_, _) =>
        {
            if (!PivotOptionsPlanner.TryParseCompactRowLabelIndent(compactIndentBox.Text, out _, out var error))
            {
                tabs.SelectedIndex = 0;
                compactIndentBox.Focus();
                ShowEditIssue(error ?? PivotOptionsPlanner.CompactIndentRangeMessage);
                return;
            }

            if (!PivotOptionsPlanner.TryParsePageWrap(pageWrapBox.Text, out _, out error))
            {
                tabs.SelectedIndex = 0;
                pageWrapBox.Focus();
                ShowEditIssue(error ?? PivotOptionsPlanner.PageWrapRangeMessage);
                return;
            }

            dialog.Close(true);
        };

        // ── Tab: Layout & Format ───────────────────────────────────────────────
        var pageFieldLayoutBox = OptionComboBox(
            PivotOptionsPlanner.PageFieldLayouts.Select(option => option.Label).ToArray(),
            PivotOptionsPlanner.FindPageFieldLayoutIndex(values.PageOverThenDown));
        AutomationProperties.SetAutomationId(pageFieldLayoutBox, "PivotOptionsPageFieldLayoutBox");
        AutomationProperties.SetName(pageFieldLayoutBox, UiText.Get("PivotTableOptions_ReportFilterAreaAutomationName"));
        pageWrapBox = OptionTextBox(PivotOptionsPlanner.PageWrapText(values.PageWrap), 80);
        pageWrapBox.Width = 60;
        pageWrapBox.MinWidth = 60;
        pageWrapBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(pageWrapBox, "PivotOptionsPageWrapBox");
        AutomationProperties.SetName(
            pageWrapBox,
            UiText.CreateAutomationName(UiText.Get("PivotTableOptions_ReportFilterFieldsPerColumnLabel")));

        var layoutSection = new StackPanel { Spacing = 6 };
        layoutSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_ReportLayoutLabel")));
        layoutSection.Children.Add(reportLayoutBox);
        layoutSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_CompactIndentLabel")));
        layoutSection.Children.Add(compactIndentBox);
        layoutSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_ReportFilterAreaLabel")));
        layoutSection.Children.Add(pageFieldLayoutBox);
        layoutSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_ReportFilterFieldsPerColumnLabel")));
        layoutSection.Children.Add(pageWrapBox);
        layoutSection.Children.Add(repeatLabelsBox);
        layoutSection.Children.Add(blankRowBox);
        layoutSection.Children.Add(mergeLabelsBox);

        var emptyCellsBox = OptionTextBox(values.EmptyValueText ?? string.Empty, 160);
        emptyCellsBox.Width = 120;
        emptyCellsBox.MinWidth = 120;
        emptyCellsBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(emptyCellsBox, "PivotOptionsEmptyCellsBox");
        var errorValuesBox = OptionTextBox(values.ErrorValueText ?? string.Empty, 160);
        errorValuesBox.Width = 120;
        errorValuesBox.MinWidth = 120;
        errorValuesBox.HorizontalAlignment = AvaloniaHorizontalAlignment.Center;
        AutomationProperties.SetAutomationId(errorValuesBox, "PivotOptionsErrorValuesBox");
        var autofitColumnsBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_AutofitColumnWidthsOnUpdate"), values.AutofitColumnsOnUpdate);
        AutomationProperties.SetAutomationId(autofitColumnsBox, "PivotOptionsAutofitColumnsBox");
        var preserveFormattingBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_PreserveCellFormattingOnUpdate"), values.PreserveFormattingOnUpdate);
        AutomationProperties.SetAutomationId(preserveFormattingBox, "PivotOptionsPreserveFormattingBox");
        var formatSection = new StackPanel { Spacing = 6 };
        formatSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_EmptyCellsLabel")));
        formatSection.Children.Add(emptyCellsBox);
        formatSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_ErrorValuesLabel")));
        formatSection.Children.Add(errorValuesBox);
        formatSection.Children.Add(autofitColumnsBox);
        formatSection.Children.Add(preserveFormattingBox);

        var layoutTab = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
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

        var totalsTab = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        totalsTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_GrandTotalsGroup"), grandTotalsSection));
        totalsTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_FiltersAndSubtotalsGroup"), subtotalsSection));

        // ── Tab: Display ───────────────────────────────────────────────────────
        var displaySection = new StackPanel
        {
            Spacing = AvaloniaDisplayOptionSpacingCompensation,
            Margin = new Thickness(
                0,
                AvaloniaDisplayOptionTopInsetCompensation,
                0,
                AvaloniaDisplayOptionBottomInsetCompensation),
        };
        var styleNames = PivotStyleGalleryPlanner.GetStyleNames(values.StyleName);
        var styleBox = OptionComboBox(
            styleNames.Select(name => (string?)name).ToArray(),
            PivotStyleGalleryPlanner.FindStyleIndex(styleNames, values.StyleName));
        AutomationProperties.SetAutomationId(styleBox, "PivotOptionsStyleBox");
        var rowHeadersBox = OptionCheckBox(UiText.Get("PivotTableOptions_RowHeaders"), values.ShowRowHeaders);
        var columnHeadersBox = OptionCheckBox(UiText.Get("PivotTableOptions_ColumnHeaders"), values.ShowColumnHeaders);
        var fieldHeadersBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_DisplayFieldCaptionsAndFilterDropDowns"), values.ShowFieldHeaders);
        var contextualTooltipsBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_ShowContextualTooltips"), values.ShowContextualTooltips);
        var propertiesInTooltipsBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_ShowPropertiesInTooltips"), values.ShowPropertiesInTooltips);
        var classicLayoutBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_ClassicPivotTableLayoutEnablesDraggingOfFieldsInTheGrid"), values.ShowClassicLayout);
        var showItemsWithNoDataRowsBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_ShowItemsWithNoDataOnRows"), values.ShowItemsWithNoDataOnRows);
        var showItemsWithNoDataColumnsBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_ShowItemsWithNoDataOnColumns"), values.ShowItemsWithNoDataOnColumns);
        var rowStripesBox = OptionCheckBox(UiText.Get("PivotTableOptions_BandedRows"), values.ShowRowStripes);
        var columnStripesBox = OptionCheckBox(UiText.Get("PivotTableOptions_BandedColumns"), values.ShowColumnStripes);
        var showExpandCollapseBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_ShowExpandCollapseButtons"), values.ShowExpandCollapseButtons);
        displaySection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_PivotTableStyleLabel")));
        displaySection.Children.Add(styleBox);
        displaySection.Children.Add(rowHeadersBox);
        displaySection.Children.Add(columnHeadersBox);
        displaySection.Children.Add(fieldHeadersBox);
        displaySection.Children.Add(contextualTooltipsBox);
        displaySection.Children.Add(propertiesInTooltipsBox);
        displaySection.Children.Add(classicLayoutBox);
        displaySection.Children.Add(showItemsWithNoDataRowsBox);
        displaySection.Children.Add(showItemsWithNoDataColumnsBox);
        displaySection.Children.Add(rowStripesBox);
        displaySection.Children.Add(columnStripesBox);
        displaySection.Children.Add(showExpandCollapseBox);
        var displayTab = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        displayTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_PivotTableStyleOptionsGroup"), displaySection));

        // ── Tab: Printing ──────────────────────────────────────────────────────
        var printSection = new StackPanel { Spacing = 6 };
        var printTitlesBox = OptionCheckBox(UiText.Get("PivotTableOptions_SetPrintTitles"), values.PrintTitles);
        var printExpandCollapseBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_PrintExpandCollapseButtonsWhenDisplayedOnPivotTable"),
            values.PrintExpandCollapseButtons);
        printSection.Children.Add(printTitlesBox);
        printSection.Children.Add(printExpandCollapseBox);
        var printTab = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        printTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_PrintOptionsGroup"), printSection));

        // ── Tab: Data ──────────────────────────────────────────────────────────
        var dataSection = new StackPanel { Spacing = 6 };
        var refreshOnOpenBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_RefreshDataWhenOpeningTheFile"), values.RefreshOnOpen);
        var saveSourceDataBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_SaveSourceDataWithFile"), values.SaveSourceData);
        var enableRefreshBox = OptionCheckBox(UiText.Get("PivotTableOptions_EnableRefresh"), values.EnableRefresh);
        var enableShowDetailsBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_EnableShowDetails"), values.EnableDrill);
        var preserveSourceSortFilterBox = OptionCheckBox(
            UiText.Get("PivotTableOptions_PreserveSourceSortAndFilterSettings"), values.PreserveSourceSortFilter);
        var missingItemsLimitBox = OptionComboBox(
            PivotOptionsPlanner.MissingItemsLimits.Select(option => option.Label).ToArray(),
            PivotOptionsPlanner.FindMissingItemsLimitIndex(values.MissingItemsLimit));
        AutomationProperties.SetAutomationId(missingItemsLimitBox, "PivotOptionsMissingItemsLimitBox");
        dataSection.Children.Add(refreshOnOpenBox);
        dataSection.Children.Add(saveSourceDataBox);
        dataSection.Children.Add(enableRefreshBox);
        dataSection.Children.Add(enableShowDetailsBox);
        dataSection.Children.Add(preserveSourceSortFilterBox);
        dataSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_RetainItemsDeletedLabel")));
        dataSection.Children.Add(missingItemsLimitBox);
        var dataTab = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        dataTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_DataOptionsGroup"), dataSection));

        // ── Tab: Alt Text ──────────────────────────────────────────────────────
        var altSection = new StackPanel { Spacing = 6 };
        var altTextTitleBox = OptionTextBox(values.AltTextTitle ?? string.Empty, 320);
        AutomationProperties.SetAutomationId(altTextTitleBox, "PivotOptionsAltTextTitleBox");
        var altTextDescriptionBox = OptionTextBox(values.AltTextDescription ?? string.Empty, 320, fixedHeight: false);
        altTextDescriptionBox.AcceptsReturn = true;
        altTextDescriptionBox.Height = 90;
        altTextDescriptionBox.MinHeight = 90;
        altTextDescriptionBox.MaxHeight = 90;
        altTextDescriptionBox.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetAutomationId(altTextDescriptionBox, "PivotOptionsAltTextDescriptionBox");
        altSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_TitleLabel")));
        altSection.Children.Add(altTextTitleBox);
        altSection.Children.Add(OptionLabel(UiText.Get("PivotTableOptions_DescriptionLabel")));
        altSection.Children.Add(altTextDescriptionBox);
        var altTab = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        altTab.Children.Add(MakeSectionGroupBox(UiText.Get("PivotTableOptions_AltTextGroup"), altSection));

        tabs = new TabControl
        {
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 23),
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
        tabs.SelectionChanged += (_, _) =>
        {
            dialog.Height = tabs.SelectedIndex == 0
                ? PivotOptionsPlanner.LayoutAndFormatCaptureHeight
                : PivotOptionsPlanner.DialogMinHeight;
        };

        var buttonRow = AvaloniaCompactDialogChrome.CreateActionRow([ok, cancel], new Thickness(0, 0, 0, 37));

        var content = new DockPanel { Margin = new Thickness(16, 16, 31, 16) };
        DockPanel.SetDock(buttonRow, Dock.Bottom);
        content.Children.Add(buttonRow);
        content.Children.Add(tabs);
        dialog.Content = content;
        dialog.Opened += (_, _) =>
        {
            ApplyPivotTextBoxChrome(compactIndentBox);
            ApplyPivotTextBoxChrome(pageWrapBox);
            ApplyPivotTextBoxChrome(emptyCellsBox);
            ApplyPivotTextBoxChrome(errorValuesBox);
            ApplyPivotTextBoxChrome(altTextTitleBox);
        };
        ConfigurePivotDialogLifecycle(dialog, reportLayoutBox);

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
            return;

        if (!PivotOptionsPlanner.TryParseCompactRowLabelIndent(compactIndentBox.Text, out var indent, out _))
            indent = values.CompactRowLabelIndent;
        PivotOptionsPlanner.TryParsePageWrap(pageWrapBox.Text, out var pageWrap, out _);

        var result = PivotOptionsPlanner.CreateDialogValues(
            rowGrandTotalsBox.IsChecked == true,
            columnGrandTotalsBox.IsChecked == true,
            subtotalsBox.IsChecked == true,
            PivotOptionsPlanner.SubtotalPlacementFromIndex(subtotalPlacementBox.SelectedIndex),
            repeatLabelsBox.IsChecked == true,
            blankRowBox.IsChecked == true,
            styleBox.SelectedItem?.ToString(),
            rowHeadersBox.IsChecked == true,
            columnHeadersBox.IsChecked == true,
            rowStripesBox.IsChecked == true,
            columnStripesBox.IsChecked == true,
            PivotOptionsPlanner.ReportLayoutFromIndex(reportLayoutBox.SelectedIndex),
            emptyValueText: emptyCellsBox.Text,
            refreshOnOpen: refreshOnOpenBox.IsChecked == true,
            saveSourceData: saveSourceDataBox.IsChecked == true,
            enableRefresh: enableRefreshBox.IsChecked == true,
            preserveSourceSortFilter: preserveSourceSortFilterBox.IsChecked == true,
            missingItemsLimit: PivotOptionsPlanner.MissingItemsLimitFromIndex(missingItemsLimitBox.SelectedIndex),
            printTitles: printTitlesBox.IsChecked == true,
            printExpandCollapseButtons: printExpandCollapseBox.IsChecked == true,
            altTextTitle: altTextTitleBox.Text,
            altTextDescription: altTextDescriptionBox.Text,
            compactRowLabelIndent: indent,
            showExpandCollapseButtons: showExpandCollapseBox.IsChecked == true,
            autofitColumnsOnUpdate: autofitColumnsBox.IsChecked == true,
            preserveFormattingOnUpdate: preserveFormattingBox.IsChecked == true,
            showFieldHeaders: fieldHeadersBox.IsChecked == true,
            showContextualTooltips: contextualTooltipsBox.IsChecked == true,
            showPropertiesInTooltips: propertiesInTooltipsBox.IsChecked == true,
            showClassicLayout: classicLayoutBox.IsChecked == true,
            mergeAndCenterLabels: mergeLabelsBox.IsChecked == true,
            showItemsWithNoDataOnRows: showItemsWithNoDataRowsBox.IsChecked == true,
            showItemsWithNoDataOnColumns: showItemsWithNoDataColumnsBox.IsChecked == true,
            pageOverThenDown: PivotOptionsPlanner.PageFieldLayoutFromIndex(pageFieldLayoutBox.SelectedIndex),
            pageWrap: pageWrap,
            errorValueText: errorValuesBox.Text,
            enableDrill: enableShowDetailsBox.IsChecked == true);

        ApplyPivotApplicationPlan(
            PivotApplication.PlanDialogOptions(
                new PivotApplicationTarget(_session.ActiveSheet, pivot),
                result),
            "PivotTable options updated.");
    }

    // ── Pivot option control factories ────────────────────────────────────────
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

    private static TextBox OptionTextBox(string text, double minWidth, bool fixedHeight = true)
    {
        var box = new TextBox
        {
            Text = text,
            MinWidth = minWidth,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        ApplyPivotTextBoxChrome(box, fixedHeight);
        return box;
    }

    private static ComboBox OptionComboBox(IReadOnlyList<string?> items, int selectedIndex = 0)
    {
        var box = new ComboBox
        {
            MinWidth = 220,
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        foreach (var item in items)
            box.Items.Add(item ?? string.Empty);
        box.SelectedIndex = items.Count > 0 ? Math.Clamp(selectedIndex, 0, items.Count - 1) : -1;
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
    private static GroupBox MakeSectionGroupBox(string label, Control content)
    {
        var groupBox = new GroupBox
        {
            Header = StripDisplayMnemonic(label),
            Content = content,
            Padding = new Thickness(8),
        };
        AvaloniaCompactDialogChrome.ApplyGroupBox(groupBox, PivotDialogChromeStyle);
        return groupBox;
    }
}
