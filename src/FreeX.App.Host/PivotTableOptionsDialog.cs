using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed partial class PivotTableOptionsDialog : Window
{
    private readonly CheckBox _rowGrandTotalsBox = new() { Content = UiText.Get("PivotTableOptions_ShowRowGrandTotals") };
    private readonly CheckBox _columnGrandTotalsBox = new() { Content = UiText.Get("PivotTableOptions_ShowColumnGrandTotals") };
    private readonly CheckBox _subtotalsBox = new() { Content = UiText.Get("PivotTableOptions_ShowSubtotals") };
    private readonly ComboBox _subtotalPlacementBox = new();
    private readonly CheckBox _repeatItemLabelsBox = new() { Content = UiText.Get("PivotTableOptions_RepeatItemLabels") };
    private readonly CheckBox _blankLineBox = new() { Content = UiText.Get("PivotTableOptions_InsertBlankLineAfterEachItem") };
    private readonly CheckBox _mergeLabelsBox = new() { Content = UiText.Get("PivotTableOptions_MergeAndCenterCellsWithLabels") };
    private readonly ComboBox _reportLayoutBox = new();
    private readonly TextBox _compactIndentBox = new() { Width = 60 };
    private readonly ComboBox _pageFieldLayoutBox = new();
    private readonly TextBox _pageWrapBox = new() { Width = 60 };
    private readonly ComboBox _styleBox = new();
    private readonly CheckBox _rowHeadersBox = new() { Content = UiText.Get("PivotTableOptions_RowHeaders") };
    private readonly CheckBox _columnHeadersBox = new() { Content = UiText.Get("PivotTableOptions_ColumnHeaders") };
    private readonly CheckBox _fieldHeadersBox = new() { Content = UiText.Get("PivotTableOptions_DisplayFieldCaptionsAndFilterDropDowns"), IsChecked = true };
    private readonly CheckBox _contextualTooltipsBox = new() { Content = UiText.Get("PivotTableOptions_ShowContextualTooltips"), IsChecked = true };
    private readonly CheckBox _propertiesInTooltipsBox = new() { Content = UiText.Get("PivotTableOptions_ShowPropertiesInTooltips"), IsChecked = true };
    private readonly CheckBox _classicLayoutBox = new() { Content = UiText.Get("PivotTableOptions_ClassicPivotTableLayoutEnablesDraggingOfFieldsInTheGrid") };
    private readonly CheckBox _showItemsWithNoDataRowsBox = new() { Content = UiText.Get("PivotTableOptions_ShowItemsWithNoDataOnRows") };
    private readonly CheckBox _showItemsWithNoDataColumnsBox = new() { Content = UiText.Get("PivotTableOptions_ShowItemsWithNoDataOnColumns") };
    private readonly CheckBox _rowStripesBox = new() { Content = UiText.Get("PivotTableOptions_BandedRows") };
    private readonly CheckBox _columnStripesBox = new() { Content = UiText.Get("PivotTableOptions_BandedColumns") };
    private readonly TextBox _emptyCellsBox = new() { Width = 120 };
    private readonly TextBox _errorValuesBox = new() { Width = 120 };
    private readonly CheckBox _autofitColumnsBox = new() { Content = UiText.Get("PivotTableOptions_AutofitColumnWidthsOnUpdate"), IsChecked = true };
    private readonly CheckBox _preserveFormattingBox = new() { Content = UiText.Get("PivotTableOptions_PreserveCellFormattingOnUpdate"), IsChecked = true };
    private readonly CheckBox _refreshOnOpenBox = new() { Content = UiText.Get("PivotTableOptions_RefreshDataWhenOpeningTheFile") };
    private readonly CheckBox _saveSourceDataBox = new() { Content = UiText.Get("PivotTableOptions_SaveSourceDataWithFile"), IsChecked = true };
    private readonly CheckBox _enableRefreshBox = new() { Content = UiText.Get("PivotTableOptions_EnableRefresh"), IsChecked = true };
    private readonly CheckBox _enableShowDetailsBox = new() { Content = UiText.Get("PivotTableOptions_EnableShowDetails"), IsChecked = true };
    private readonly CheckBox _preserveSourceSortFilterBox = new()
    {
        Content = UiText.Get("PivotTableOptions_PreserveSourceSortAndFilterSettings"),
        IsChecked = true
    };
    private readonly ComboBox _missingItemsLimitBox = new();
    private readonly CheckBox _showExpandCollapseBox = new() { Content = UiText.Get("PivotTableOptions_ShowExpandCollapseButtons"), IsChecked = true };
    private readonly CheckBox _printTitlesBox = new() { Content = UiText.Get("PivotTableOptions_SetPrintTitles") };
    private readonly CheckBox _printExpandCollapseBox = new() { Content = UiText.Get("PivotTableOptions_PrintExpandCollapseButtonsWhenDisplayedOnPivotTable") };
    private readonly TextBox _altTextTitleBox = new();
    private readonly TextBox _altTextDescriptionBox = new() { AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TabControl _tabs = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly TabItem _layoutTab = new() { Header = UiText.Get("PivotTableOptions_LayoutAndFormat") };

    public PivotOptionsDialogValues Result { get; private set; }

    public PivotTableOptionsDialog(PivotTableModel pivotTable, PivotCacheModel? cache = null)
    {
        Result = PivotOptionsPlanner.CaptureDialogValues(pivotTable, cache);
        Title = UiText.Get("PivotTableOptions_PivotTableOptions");
        DialogSizing.ApplyContentHeight(this, width: PivotOptionsPlanner.DialogWidth, minHeight: PivotOptionsPlanner.DialogMinHeight);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private DockPanel CreateContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        _layoutTab.Content = CreateLayoutAndFormatTab();
        _tabs.Items.Add(_layoutTab);
        _tabs.Items.Add(new TabItem { Header = UiText.Get("PivotTableOptions_TotalsAndFilters"), Content = CreateTotalsAndFiltersTab() });
        _tabs.Items.Add(new TabItem { Header = UiText.Get("PivotTableOptions_Display"), Content = CreateDisplayTab() });
        _tabs.Items.Add(new TabItem { Header = UiText.Get("PivotTableOptions_Printing"), Content = CreatePrintingTab() });
        _tabs.Items.Add(new TabItem { Header = UiText.Get("PivotTableOptions_Data"), Content = CreateDataTab() });
        _tabs.Items.Add(new TabItem { Header = UiText.Get("PivotTableOptions_AltText"), Content = CreateAltTextTab() });

        var buttons = PivotDialogLayout.CreateButtonRow(Accept);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_tabs);
        return root;
    }

    private StackPanel CreateLayoutAndFormatTab()
    {
        var stack = CreateTabPanel();
        var layoutPanel = PivotDialogLayout.CreateGroupPanel();
        AddLabeledControl(layoutPanel, UiText.Get("PivotTableOptions_ReportLayoutLabel"), _reportLayoutBox, Enum.GetValues<PivotReportLayout>());
        AddLabeledControl(layoutPanel, UiText.Get("PivotTableOptions_CompactIndentLabel"), _compactIndentBox);
        AddLabeledControl(
            layoutPanel,
            UiText.Get("PivotTableOptions_ReportFilterAreaLabel"),
            _pageFieldLayoutBox,
            PivotOptionsPlanner.PageFieldLayouts.Select(option => option.Label));
        AddLabeledControl(layoutPanel, UiText.Get("PivotTableOptions_ReportFilterFieldsPerColumnLabel"), _pageWrapBox);
        AddCheckBox(layoutPanel, _repeatItemLabelsBox);
        AddCheckBox(layoutPanel, _blankLineBox);
        AddCheckBox(layoutPanel, _mergeLabelsBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotTableOptions_LayoutSectionGroup"), layoutPanel));

        var formatPanel = PivotDialogLayout.CreateGroupPanel();
        AddLabeledControl(formatPanel, UiText.Get("PivotTableOptions_EmptyCellsLabel"), _emptyCellsBox);
        AddLabeledControl(formatPanel, UiText.Get("PivotTableOptions_ErrorValuesLabel"), _errorValuesBox);
        AddCheckBox(formatPanel, _autofitColumnsBox);
        AddCheckBox(formatPanel, _preserveFormattingBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotTableOptions_FormatSectionGroup"), formatPanel));
        stack.Children.Add(new Border { Height = 1.5 });
        return stack;
    }

    private StackPanel CreateTotalsAndFiltersTab()
    {
        var stack = CreateTabPanel();
        var totalsPanel = PivotDialogLayout.CreateGroupPanel();
        AddCheckBox(totalsPanel, _rowGrandTotalsBox);
        AddCheckBox(totalsPanel, _columnGrandTotalsBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotTableOptions_GrandTotalsGroup"), totalsPanel));

        var filtersPanel = PivotDialogLayout.CreateGroupPanel();
        AddCheckBox(filtersPanel, _subtotalsBox);
        AddLabeledControl(filtersPanel, UiText.Get("PivotTableOptions_SubtotalPlacementLabel"), _subtotalPlacementBox, Enum.GetValues<PivotSubtotalPlacement>());
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotTableOptions_FiltersAndSubtotalsGroup"), filtersPanel));
        return stack;
    }

    private StackPanel CreateDisplayTab()
    {
        var stack = CreateTabPanel();
        var stylePanel = PivotDialogLayout.CreateGroupPanel();
        AddLabeledControl(stylePanel, UiText.Get("PivotTableOptions_PivotTableStyleLabel"), _styleBox, PivotStyleGalleryPlanner.BuiltInStyleNames);
        AddCheckBox(stylePanel, _rowHeadersBox);
        AddCheckBox(stylePanel, _columnHeadersBox);
        AddCheckBox(stylePanel, _fieldHeadersBox);
        AddCheckBox(stylePanel, _contextualTooltipsBox);
        AddCheckBox(stylePanel, _propertiesInTooltipsBox);
        AddCheckBox(stylePanel, _classicLayoutBox);
        AddCheckBox(stylePanel, _showItemsWithNoDataRowsBox);
        AddCheckBox(stylePanel, _showItemsWithNoDataColumnsBox);
        AddCheckBox(stylePanel, _rowStripesBox);
        AddCheckBox(stylePanel, _columnStripesBox);
        AddCheckBox(stylePanel, _showExpandCollapseBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotTableOptions_PivotTableStyleOptionsGroup"), stylePanel));
        return stack;
    }

    private StackPanel CreateDataTab()
    {
        var stack = CreateTabPanel();
        var dataPanel = PivotDialogLayout.CreateGroupPanel();
        AddCheckBox(dataPanel, _refreshOnOpenBox);
        AddCheckBox(dataPanel, _saveSourceDataBox);
        AddCheckBox(dataPanel, _enableRefreshBox);
        AddCheckBox(dataPanel, _enableShowDetailsBox);
        AddCheckBox(dataPanel, _preserveSourceSortFilterBox);
        AddLabeledControl(
            dataPanel,
            UiText.Get("PivotTableOptions_RetainItemsDeletedLabel"),
            _missingItemsLimitBox,
            PivotOptionsPlanner.MissingItemsLimits.Select(option => option.Label));
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotTableOptions_DataOptionsGroup"), dataPanel));
        return stack;
    }

    private StackPanel CreatePrintingTab()
    {
        var stack = CreateTabPanel();
        var printPanel = PivotDialogLayout.CreateGroupPanel();
        AddCheckBox(printPanel, _printTitlesBox);
        AddCheckBox(printPanel, _printExpandCollapseBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotTableOptions_PrintOptionsGroup"), printPanel));
        return stack;
    }

    private StackPanel CreateAltTextTab()
    {
        var stack = CreateTabPanel();
        var altPanel = PivotDialogLayout.CreateGroupPanel();
        AddLabeledControl(altPanel, UiText.Get("PivotTableOptions_TitleLabel"), _altTextTitleBox);
        AddLabeledControl(altPanel, UiText.Get("PivotTableOptions_DescriptionLabel"), _altTextDescriptionBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotTableOptions_AltTextGroup"), altPanel));
        return stack;
    }

    private static StackPanel CreateTabPanel() => new() { Margin = new Thickness(10) };

    private static void AddSectionHeader(Panel stack, string text) =>
        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });

    private static void AddCheckBox(Panel stack, CheckBox checkBox)
    {
        checkBox.Margin = new Thickness(0, 0, 0, 6);
        stack.Children.Add(checkBox);
    }

    private static void AddLabeledControl(Panel stack, string label, Control control)
    {
        stack.Children.Add(new Label
        {
            Content = label,
            Target = control,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 3, 0, 4)
        });
        control.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(control);
    }

    private static void AddLabeledControl<T>(Panel stack, string label, ComboBox comboBox, IEnumerable<T> items)
    {
        comboBox.ItemsSource = items;
        AddLabeledControl(stack, label, comboBox);
    }

    private void Load(PivotOptionsDialogValues result)
    {
        _rowGrandTotalsBox.IsChecked = result.ShowRowGrandTotals;
        _columnGrandTotalsBox.IsChecked = result.ShowColumnGrandTotals;
        _subtotalsBox.IsChecked = result.ShowSubtotals;
        _subtotalPlacementBox.SelectedItem = result.SubtotalPlacement;
        _repeatItemLabelsBox.IsChecked = result.RepeatItemLabels;
        _blankLineBox.IsChecked = result.BlankLineAfterItems;
        _reportLayoutBox.SelectedItem = result.ReportLayout;
        _compactIndentBox.Text = PivotOptionsPlanner.CompactRowLabelIndentText(result.CompactRowLabelIndent);
        _pageFieldLayoutBox.SelectedItem = PivotOptionsPlanner.GetPageFieldLayoutLabel(result.PageOverThenDown);
        _pageWrapBox.Text = PivotOptionsPlanner.PageWrapText(result.PageWrap);
        _mergeLabelsBox.IsChecked = result.MergeAndCenterLabels;
        var styleNames = PivotStyleGalleryPlanner.GetStyleNames(result.StyleName);
        _styleBox.ItemsSource = styleNames;
        _styleBox.SelectedIndex = PivotStyleGalleryPlanner.FindStyleIndex(styleNames, result.StyleName);
        _rowHeadersBox.IsChecked = result.ShowRowHeaders;
        _columnHeadersBox.IsChecked = result.ShowColumnHeaders;
        _fieldHeadersBox.IsChecked = result.ShowFieldHeaders;
        _contextualTooltipsBox.IsChecked = result.ShowContextualTooltips;
        _propertiesInTooltipsBox.IsChecked = result.ShowPropertiesInTooltips;
        _classicLayoutBox.IsChecked = result.ShowClassicLayout;
        _showItemsWithNoDataRowsBox.IsChecked = result.ShowItemsWithNoDataOnRows;
        _showItemsWithNoDataColumnsBox.IsChecked = result.ShowItemsWithNoDataOnColumns;
        _rowStripesBox.IsChecked = result.ShowRowStripes;
        _columnStripesBox.IsChecked = result.ShowColumnStripes;
        _emptyCellsBox.Text = result.EmptyValueText ?? "";
        _errorValuesBox.Text = result.ErrorValueText ?? "";
        _autofitColumnsBox.IsChecked = result.AutofitColumnsOnUpdate;
        _preserveFormattingBox.IsChecked = result.PreserveFormattingOnUpdate;
        _refreshOnOpenBox.IsChecked = result.RefreshOnOpen;
        _saveSourceDataBox.IsChecked = result.SaveSourceData;
        _enableRefreshBox.IsChecked = result.EnableRefresh;
        _enableShowDetailsBox.IsChecked = result.EnableDrill;
        _preserveSourceSortFilterBox.IsChecked = result.PreserveSourceSortFilter;
        _missingItemsLimitBox.SelectedItem = PivotOptionsPlanner.GetMissingItemsLimitLabel(result.MissingItemsLimit);
        _showExpandCollapseBox.IsChecked = result.ShowExpandCollapseButtons;
        _printTitlesBox.IsChecked = result.PrintTitles;
        _printExpandCollapseBox.IsChecked = result.PrintExpandCollapseButtons;
        _altTextTitleBox.Text = result.AltTextTitle ?? "";
        _altTextDescriptionBox.Text = result.AltTextDescription ?? "";
    }

    private void Accept()
    {
        if (!ValidateInputs())
            return;

        PivotOptionsPlanner.TryParseCompactRowLabelIndent(_compactIndentBox.Text, out var compactIndent, out _);
        PivotOptionsPlanner.TryParsePageWrap(_pageWrapBox.Text, out var pageWrap, out _);

        Result = PivotOptionsPlanner.CreateDialogValues(
            _rowGrandTotalsBox.IsChecked == true,
            _columnGrandTotalsBox.IsChecked == true,
            _subtotalsBox.IsChecked == true,
            _subtotalPlacementBox.SelectedItem is PivotSubtotalPlacement subtotalPlacement
                ? subtotalPlacement
                : PivotSubtotalPlacement.Bottom,
            _repeatItemLabelsBox.IsChecked == true,
            _blankLineBox.IsChecked == true,
            _styleBox.SelectedItem?.ToString() ?? string.Empty,
            _rowHeadersBox.IsChecked == true,
            _columnHeadersBox.IsChecked == true,
            _rowStripesBox.IsChecked == true,
            _columnStripesBox.IsChecked == true,
            _reportLayoutBox.SelectedItem is PivotReportLayout reportLayout
                ? reportLayout
                : PivotReportLayout.Tabular,
            emptyValueText: _emptyCellsBox.Text,
            refreshOnOpen: _refreshOnOpenBox.IsChecked == true,
            saveSourceData: _saveSourceDataBox.IsChecked == true,
            enableRefresh: _enableRefreshBox.IsChecked == true,
            preserveSourceSortFilter: _preserveSourceSortFilterBox.IsChecked == true,
            missingItemsLimit: PivotOptionsPlanner.MissingItemsLimitFromLabel(_missingItemsLimitBox.SelectedItem?.ToString()),
            printTitles: _printTitlesBox.IsChecked == true,
            printExpandCollapseButtons: _printExpandCollapseBox.IsChecked == true,
            altTextTitle: _altTextTitleBox.Text,
            altTextDescription: _altTextDescriptionBox.Text,
            compactRowLabelIndent: compactIndent,
            showExpandCollapseButtons: _showExpandCollapseBox.IsChecked == true,
            autofitColumnsOnUpdate: _autofitColumnsBox.IsChecked == true,
            preserveFormattingOnUpdate: _preserveFormattingBox.IsChecked == true,
            showFieldHeaders: _fieldHeadersBox.IsChecked == true,
            showContextualTooltips: _contextualTooltipsBox.IsChecked == true,
            showPropertiesInTooltips: _propertiesInTooltipsBox.IsChecked == true,
            showClassicLayout: _classicLayoutBox.IsChecked == true,
            mergeAndCenterLabels: _mergeLabelsBox.IsChecked == true,
            showItemsWithNoDataOnRows: _showItemsWithNoDataRowsBox.IsChecked == true,
            showItemsWithNoDataOnColumns: _showItemsWithNoDataColumnsBox.IsChecked == true,
            pageOverThenDown: PivotOptionsPlanner.PageFieldLayoutFromLabel(_pageFieldLayoutBox.SelectedItem?.ToString()),
            pageWrap: pageWrap,
            errorValueText: _errorValuesBox.Text,
            enableDrill: _enableShowDetailsBox.IsChecked == true);
        DialogResult = true;
    }

    private bool ValidateInputs()
    {
        if (!PivotOptionsPlanner.TryParseCompactRowLabelIndent(_compactIndentBox.Text, out _, out _))
        {
            ShowInvalidInputWarning(UiText.Get("PivotTableOptions_EnterCompactIndent"), _compactIndentBox);
            return false;
        }

        if (!PivotOptionsPlanner.TryParsePageWrap(_pageWrapBox.Text, out _, out _))
        {
            ShowInvalidInputWarning(UiText.Get("PivotTableOptions_EnterPageFieldsPerColumn"), _pageWrapBox);
            return false;
        }

        return true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        _tabs.SelectedItem = _layoutTab;
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
        return false;
    }

    private void FocusInitialKeyboardTarget()
    {
        _reportLayoutBox.Focus();
        Keyboard.Focus(_reportLayoutBox);
    }

}
