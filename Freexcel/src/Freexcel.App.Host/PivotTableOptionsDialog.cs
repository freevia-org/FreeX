using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotTableOptionsDialogResult(
    bool ShowRowGrandTotals,
    bool ShowColumnGrandTotals,
    bool ShowSubtotals,
    PivotSubtotalPlacement SubtotalPlacement,
    bool RepeatItemLabels,
    bool BlankLineAfterItems,
    string StyleName,
    bool ShowRowHeaders,
    bool ShowColumnHeaders,
    bool ShowRowStripes,
    bool ShowColumnStripes,
    PivotReportLayout ReportLayout,
    string? EmptyValueText = null,
    bool RefreshOnOpen = false,
    bool SaveSourceData = true,
    bool EnableRefresh = true,
    bool PreserveSourceSortFilter = true,
    int? MissingItemsLimit = null,
    bool PrintTitles = false,
    bool PrintExpandCollapseButtons = false,
    string? AltTextTitle = null,
    string? AltTextDescription = null,
    int CompactRowLabelIndent = 1,
    bool ShowExpandCollapseButtons = true,
    bool AutofitColumnsOnUpdate = true,
    bool PreserveFormattingOnUpdate = true,
    bool ShowFieldHeaders = true,
    bool ShowContextualTooltips = true,
    bool ShowPropertiesInTooltips = true,
    bool ShowClassicLayout = false,
    bool MergeAndCenterLabels = false,
    bool ShowItemsWithNoDataOnRows = false,
    bool ShowItemsWithNoDataOnColumns = false,
    bool PageOverThenDown = false,
    int PageWrap = 0);

public sealed class PivotTableOptionsDialog : Window
{
    private static readonly string[] StyleNames =
    [
        ..Enumerable.Range(1, 28).Select(index => $"PivotStyleLight{index}"),
        ..Enumerable.Range(1, 28).Select(index => $"PivotStyleMedium{index}"),
        ..Enumerable.Range(1, 28).Select(index => $"PivotStyleDark{index}")
    ];

    private readonly CheckBox _rowGrandTotalsBox = new() { Content = "Show _row grand totals" };
    private readonly CheckBox _columnGrandTotalsBox = new() { Content = "Show _column grand totals" };
    private readonly CheckBox _subtotalsBox = new() { Content = "Show _subtotals" };
    private readonly ComboBox _subtotalPlacementBox = new();
    private readonly CheckBox _repeatItemLabelsBox = new() { Content = "_Repeat item labels" };
    private readonly CheckBox _blankLineBox = new() { Content = "Insert _blank line after each item" };
    private readonly CheckBox _mergeLabelsBox = new() { Content = "_Merge and center cells with labels" };
    private readonly ComboBox _reportLayoutBox = new();
    private readonly TextBox _compactIndentBox = new() { Width = 60 };
    private readonly ComboBox _pageFieldLayoutBox = new();
    private readonly TextBox _pageWrapBox = new() { Width = 60 };
    private readonly ComboBox _styleBox = new();
    private readonly CheckBox _rowHeadersBox = new() { Content = "Row _headers" };
    private readonly CheckBox _columnHeadersBox = new() { Content = "Column hea_ders" };
    private readonly CheckBox _fieldHeadersBox = new() { Content = "Display field _captions and filter drop-downs", IsChecked = true };
    private readonly CheckBox _contextualTooltipsBox = new() { Content = "Show contextual _tooltips", IsChecked = true };
    private readonly CheckBox _propertiesInTooltipsBox = new() { Content = "Show _properties in tooltips", IsChecked = true };
    private readonly CheckBox _classicLayoutBox = new() { Content = "_Classic PivotTable layout (enables dragging of fields in the grid)" };
    private readonly CheckBox _showItemsWithNoDataRowsBox = new() { Content = "Show items with no data on _rows" };
    private readonly CheckBox _showItemsWithNoDataColumnsBox = new() { Content = "Show items with no data on _columns" };
    private readonly CheckBox _rowStripesBox = new() { Content = "Banded _rows" };
    private readonly CheckBox _columnStripesBox = new() { Content = "Banded c_olumns" };
    private readonly TextBox _emptyCellsBox = new() { Width = 120 };
    private readonly CheckBox _autofitColumnsBox = new() { Content = "_Autofit column widths on update", IsChecked = true };
    private readonly CheckBox _preserveFormattingBox = new() { Content = "_Preserve cell formatting on update", IsChecked = true };
    private readonly CheckBox _refreshOnOpenBox = new() { Content = "_Refresh data when opening the file" };
    private readonly CheckBox _saveSourceDataBox = new() { Content = "_Save source data with file", IsChecked = true };
    private readonly CheckBox _enableRefreshBox = new() { Content = "_Enable refresh", IsChecked = true };
    private readonly CheckBox _preserveSourceSortFilterBox = new()
    {
        Content = "Preserve source sort and _filter settings",
        IsChecked = true
    };
    private readonly ComboBox _missingItemsLimitBox = new();
    private readonly CheckBox _showExpandCollapseBox = new() { Content = "Show expand/collapse _buttons", IsChecked = true };
    private readonly CheckBox _printTitlesBox = new() { Content = "Set print _titles" };
    private readonly CheckBox _printExpandCollapseBox = new() { Content = "Print expand/collapse _buttons when displayed on PivotTable" };
    private readonly TextBox _altTextTitleBox = new();
    private readonly TextBox _altTextDescriptionBox = new() { AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public PivotTableOptionsDialogResult Result { get; private set; }

    public PivotTableOptionsDialog(PivotTableModel pivotTable, PivotCacheModel? cache = null)
    {
        Result = FromPivotTable(pivotTable, cache);
        Title = "PivotTable Options";
        Width = 520;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static PivotTableOptionsDialogResult FromPivotTable(PivotTableModel pivotTable, PivotCacheModel? cache = null) =>
        CreateResult(
            pivotTable.ShowRowGrandTotals,
            pivotTable.ShowColumnGrandTotals,
            pivotTable.ShowSubtotals,
            pivotTable.SubtotalPlacement,
            pivotTable.RepeatItemLabels,
            pivotTable.BlankLineAfterItems,
            pivotTable.StyleName,
            pivotTable.ShowRowHeaders,
            pivotTable.ShowColumnHeaders,
            pivotTable.ShowRowStripes,
            pivotTable.ShowColumnStripes,
            pivotTable.ReportLayout,
            pivotTable.EmptyValueText,
            refreshOnOpen: cache?.RefreshOnLoad ?? false,
            saveSourceData: cache?.SaveData ?? true,
            enableRefresh: cache?.EnableRefresh ?? true,
            preserveSourceSortFilter: cache?.PreserveSourceSortFilter ?? true,
            missingItemsLimit: cache?.MissingItemsLimit,
            printTitles: pivotTable.PrintTitles,
            printExpandCollapseButtons: pivotTable.PrintExpandCollapseButtons,
            altTextTitle: pivotTable.AltTextTitle,
            altTextDescription: pivotTable.AltTextDescription,
            compactRowLabelIndent: pivotTable.CompactRowLabelIndent,
            showExpandCollapseButtons: pivotTable.ShowExpandCollapseButtons,
            autofitColumnsOnUpdate: pivotTable.AutofitColumnsOnUpdate,
            preserveFormattingOnUpdate: pivotTable.PreserveFormattingOnUpdate,
            showFieldHeaders: pivotTable.ShowFieldHeaders,
            showContextualTooltips: pivotTable.ShowContextualTooltips,
            showPropertiesInTooltips: pivotTable.ShowPropertiesInTooltips,
            showClassicLayout: pivotTable.ShowClassicLayout,
            mergeAndCenterLabels: pivotTable.MergeAndCenterLabels,
            showItemsWithNoDataOnRows: pivotTable.ShowItemsWithNoDataOnRows,
            showItemsWithNoDataOnColumns: pivotTable.ShowItemsWithNoDataOnColumns,
            pageOverThenDown: pivotTable.PageOverThenDown,
            pageWrap: pivotTable.PageWrap);

    public static PivotTableOptionsDialogResult CreateResult(
        bool showRowGrandTotals,
        bool showColumnGrandTotals,
        bool showSubtotals,
        PivotSubtotalPlacement subtotalPlacement,
        bool repeatItemLabels,
        bool blankLineAfterItems,
        string styleName,
        bool showRowHeaders,
        bool showColumnHeaders,
        bool showRowStripes,
        bool showColumnStripes,
        PivotReportLayout reportLayout,
        string? emptyValueText = null,
        bool refreshOnOpen = false,
        bool saveSourceData = true,
        bool enableRefresh = true,
        bool preserveSourceSortFilter = true,
        int? missingItemsLimit = null,
        bool printTitles = false,
        bool printExpandCollapseButtons = false,
        string? altTextTitle = null,
        string? altTextDescription = null,
        int compactRowLabelIndent = 1,
        bool showExpandCollapseButtons = true,
        bool autofitColumnsOnUpdate = true,
        bool preserveFormattingOnUpdate = true,
        bool showFieldHeaders = true,
        bool showContextualTooltips = true,
        bool showPropertiesInTooltips = true,
        bool showClassicLayout = false,
        bool mergeAndCenterLabels = false,
        bool showItemsWithNoDataOnRows = false,
        bool showItemsWithNoDataOnColumns = false,
        bool pageOverThenDown = false,
        int pageWrap = 0) =>
        new(
            showRowGrandTotals,
            showColumnGrandTotals,
            showSubtotals,
            subtotalPlacement,
            repeatItemLabels,
            blankLineAfterItems,
            string.IsNullOrWhiteSpace(styleName) ? "PivotStyleLight16" : styleName.Trim(),
            showRowHeaders,
            showColumnHeaders,
            showRowStripes,
            showColumnStripes,
            reportLayout,
            NormalizeEmptyValueText(emptyValueText),
            refreshOnOpen,
            saveSourceData,
            enableRefresh,
            preserveSourceSortFilter,
            NormalizeMissingItemsLimit(missingItemsLimit),
            printTitles,
            printExpandCollapseButtons,
            NormalizeOptionalText(altTextTitle),
            NormalizeOptionalText(altTextDescription),
            NormalizeCompactRowLabelIndent(compactRowLabelIndent),
            showExpandCollapseButtons,
            autofitColumnsOnUpdate,
            preserveFormattingOnUpdate,
            showFieldHeaders,
            showContextualTooltips,
            showPropertiesInTooltips,
            showClassicLayout,
            mergeAndCenterLabels,
            showItemsWithNoDataOnRows,
            showItemsWithNoDataOnColumns,
            pageOverThenDown,
            NormalizePageWrap(pageWrap));

    private DockPanel CreateContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(tabs, Dock.Top);

        tabs.Items.Add(new TabItem { Header = "Layout & Format", Content = CreateLayoutAndFormatTab() });
        tabs.Items.Add(new TabItem { Header = "Totals & Filters", Content = CreateTotalsAndFiltersTab() });
        tabs.Items.Add(new TabItem { Header = "Display", Content = CreateDisplayTab() });
        tabs.Items.Add(new TabItem { Header = "Printing", Content = CreatePrintingTab() });
        tabs.Items.Add(new TabItem { Header = "Data", Content = CreateDataTab() });
        tabs.Items.Add(new TabItem { Header = "Alt Text", Content = CreateAltTextTab() });

        root.Children.Add(tabs);
        root.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return root;
    }

    private StackPanel CreateLayoutAndFormatTab()
    {
        var stack = CreateTabPanel();
        var layoutPanel = PivotDialogLayout.CreateGroupPanel();
        AddLabeledControl(layoutPanel, "_Report layout", _reportLayoutBox, Enum.GetValues<PivotReportLayout>());
        AddLabeledControl(layoutPanel, "When in compact form indent row labels", _compactIndentBox);
        AddLabeledControl(layoutPanel, "Display fields in report _filter area", _pageFieldLayoutBox, PageFieldLayoutLabels);
        AddLabeledControl(layoutPanel, "Report filter fields per _column", _pageWrapBox);
        AddCheckBox(layoutPanel, _repeatItemLabelsBox);
        AddCheckBox(layoutPanel, _blankLineBox);
        AddCheckBox(layoutPanel, _mergeLabelsBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Layout section", layoutPanel));

        var formatPanel = PivotDialogLayout.CreateGroupPanel();
        AddLabeledControl(formatPanel, "For _empty cells show:", _emptyCellsBox);
        AddCheckBox(formatPanel, _autofitColumnsBox);
        AddCheckBox(formatPanel, _preserveFormattingBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Format section", formatPanel));
        return stack;
    }

    private StackPanel CreateTotalsAndFiltersTab()
    {
        var stack = CreateTabPanel();
        var totalsPanel = PivotDialogLayout.CreateGroupPanel();
        AddCheckBox(totalsPanel, _rowGrandTotalsBox);
        AddCheckBox(totalsPanel, _columnGrandTotalsBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Grand totals", totalsPanel));

        var filtersPanel = PivotDialogLayout.CreateGroupPanel();
        AddCheckBox(filtersPanel, _subtotalsBox);
        AddLabeledControl(filtersPanel, "Subtotal _placement", _subtotalPlacementBox, Enum.GetValues<PivotSubtotalPlacement>());
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Filters and subtotals", filtersPanel));
        return stack;
    }

    private StackPanel CreateDisplayTab()
    {
        var stack = CreateTabPanel();
        var stylePanel = PivotDialogLayout.CreateGroupPanel();
        AddLabeledControl(stylePanel, "PivotTable _style", _styleBox, StyleNames);
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
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("PivotTable Style Options", stylePanel));
        return stack;
    }

    private StackPanel CreateDataTab()
    {
        var stack = CreateTabPanel();
        var dataPanel = PivotDialogLayout.CreateGroupPanel();
        AddCheckBox(dataPanel, _refreshOnOpenBox);
        AddCheckBox(dataPanel, _saveSourceDataBox);
        AddCheckBox(dataPanel, _enableRefreshBox);
        AddCheckBox(dataPanel, _preserveSourceSortFilterBox);
        AddLabeledControl(dataPanel, "Retain items _deleted from the data source", _missingItemsLimitBox, MissingItemsLimitLabels);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Data options", dataPanel));
        return stack;
    }

    private StackPanel CreatePrintingTab()
    {
        var stack = CreateTabPanel();
        var printPanel = PivotDialogLayout.CreateGroupPanel();
        AddCheckBox(printPanel, _printTitlesBox);
        AddCheckBox(printPanel, _printExpandCollapseBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Print options", printPanel));
        return stack;
    }

    private StackPanel CreateAltTextTab()
    {
        var stack = CreateTabPanel();
        var altPanel = PivotDialogLayout.CreateGroupPanel();
        AddLabeledControl(altPanel, "_Title:", _altTextTitleBox);
        AddLabeledControl(altPanel, "_Description:", _altTextDescriptionBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Alt Text", altPanel));
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

    private void Load(PivotTableOptionsDialogResult result)
    {
        _rowGrandTotalsBox.IsChecked = result.ShowRowGrandTotals;
        _columnGrandTotalsBox.IsChecked = result.ShowColumnGrandTotals;
        _subtotalsBox.IsChecked = result.ShowSubtotals;
        _subtotalPlacementBox.SelectedItem = result.SubtotalPlacement;
        _repeatItemLabelsBox.IsChecked = result.RepeatItemLabels;
        _blankLineBox.IsChecked = result.BlankLineAfterItems;
        _reportLayoutBox.SelectedItem = result.ReportLayout;
        _compactIndentBox.Text = result.CompactRowLabelIndent.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _pageFieldLayoutBox.SelectedItem = result.PageOverThenDown ? PageFieldLayoutOverThenDown : PageFieldLayoutDownThenOver;
        _pageWrapBox.Text = result.PageWrap.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _mergeLabelsBox.IsChecked = result.MergeAndCenterLabels;
        var styleNames = StyleNames.Contains(result.StyleName, StringComparer.OrdinalIgnoreCase)
            ? StyleNames
            : [..StyleNames, result.StyleName];
        _styleBox.ItemsSource = styleNames;
        _styleBox.SelectedItem = styleNames.FirstOrDefault(styleName =>
            string.Equals(styleName, result.StyleName, StringComparison.OrdinalIgnoreCase)) ?? StyleNames[0];
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
        _autofitColumnsBox.IsChecked = result.AutofitColumnsOnUpdate;
        _preserveFormattingBox.IsChecked = result.PreserveFormattingOnUpdate;
        _refreshOnOpenBox.IsChecked = result.RefreshOnOpen;
        _saveSourceDataBox.IsChecked = result.SaveSourceData;
        _enableRefreshBox.IsChecked = result.EnableRefresh;
        _preserveSourceSortFilterBox.IsChecked = result.PreserveSourceSortFilter;
        _missingItemsLimitBox.SelectedItem = LabelForMissingItemsLimit(result.MissingItemsLimit);
        _showExpandCollapseBox.IsChecked = result.ShowExpandCollapseButtons;
        _printTitlesBox.IsChecked = result.PrintTitles;
        _printExpandCollapseBox.IsChecked = result.PrintExpandCollapseButtons;
        _altTextTitleBox.Text = result.AltTextTitle ?? "";
        _altTextDescriptionBox.Text = result.AltTextDescription ?? "";
    }

    private void Accept()
    {
        Result = CreateResult(
            _rowGrandTotalsBox.IsChecked == true,
            _columnGrandTotalsBox.IsChecked == true,
            _subtotalsBox.IsChecked == true,
            _subtotalPlacementBox.SelectedItem is PivotSubtotalPlacement subtotalPlacement
                ? subtotalPlacement
                : PivotSubtotalPlacement.Bottom,
            _repeatItemLabelsBox.IsChecked == true,
            _blankLineBox.IsChecked == true,
            _styleBox.SelectedItem?.ToString() ?? "PivotStyleLight16",
            _rowHeadersBox.IsChecked == true,
            _columnHeadersBox.IsChecked == true,
            _rowStripesBox.IsChecked == true,
            _columnStripesBox.IsChecked == true,
            _reportLayoutBox.SelectedItem is PivotReportLayout reportLayout
                ? reportLayout
                : PivotReportLayout.Tabular,
            _emptyCellsBox.Text,
            _refreshOnOpenBox.IsChecked == true,
            _saveSourceDataBox.IsChecked == true,
            _enableRefreshBox.IsChecked == true,
            _preserveSourceSortFilterBox.IsChecked == true,
            MissingItemsLimitForLabel(_missingItemsLimitBox.SelectedItem?.ToString()),
            _printTitlesBox.IsChecked == true,
            _printExpandCollapseBox.IsChecked == true,
            _altTextTitleBox.Text,
            _altTextDescriptionBox.Text,
            ParseCompactRowLabelIndent(_compactIndentBox.Text),
            _showExpandCollapseBox.IsChecked == true,
            _autofitColumnsBox.IsChecked == true,
            _preserveFormattingBox.IsChecked == true,
            _fieldHeadersBox.IsChecked == true,
            _contextualTooltipsBox.IsChecked == true,
            _propertiesInTooltipsBox.IsChecked == true,
            _classicLayoutBox.IsChecked == true,
            _mergeLabelsBox.IsChecked == true,
            _showItemsWithNoDataRowsBox.IsChecked == true,
            _showItemsWithNoDataColumnsBox.IsChecked == true,
            PageFieldLayoutForLabel(_pageFieldLayoutBox.SelectedItem?.ToString()),
            ParsePageWrap(_pageWrapBox.Text));
        DialogResult = true;
    }

    private static string? NormalizeEmptyValueText(string? text) => NormalizeOptionalText(text);

    private static int ParseCompactRowLabelIndent(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? NormalizeCompactRowLabelIndent(value)
            : 1;

    private static int NormalizeCompactRowLabelIndent(int indent) => Math.Clamp(indent, 0, 15);

    private const string PageFieldLayoutDownThenOver = "Down, then over";
    private const string PageFieldLayoutOverThenDown = "Over, then down";
    private static readonly string[] PageFieldLayoutLabels = [PageFieldLayoutDownThenOver, PageFieldLayoutOverThenDown];

    private static bool PageFieldLayoutForLabel(string? label) =>
        string.Equals(label, PageFieldLayoutOverThenDown, StringComparison.OrdinalIgnoreCase);

    private static int ParsePageWrap(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? NormalizePageWrap(value)
            : 0;

    private static int NormalizePageWrap(int pageWrap) => Math.Clamp(pageWrap, 0, 255);

    private const int MaximumMissingItemsLimit = 1_048_576;
    private const string MissingItemsAutomatic = "Automatic";
    private const string MissingItemsNone = "None";
    private const string MissingItemsMaximum = "Maximum";
    private static readonly string[] MissingItemsLimitLabels = [MissingItemsAutomatic, MissingItemsNone, MissingItemsMaximum];

    private static int? NormalizeMissingItemsLimit(int? value) =>
        value switch
        {
            null => null,
            <= 0 => 0,
            _ => MaximumMissingItemsLimit
        };

    private static string LabelForMissingItemsLimit(int? value) =>
        value switch
        {
            null => MissingItemsAutomatic,
            <= 0 => MissingItemsNone,
            _ => MissingItemsMaximum
        };

    private static int? MissingItemsLimitForLabel(string? label) =>
        string.Equals(label, MissingItemsNone, StringComparison.OrdinalIgnoreCase)
            ? 0
            : string.Equals(label, MissingItemsMaximum, StringComparison.OrdinalIgnoreCase)
                ? MaximumMissingItemsLimit
                : null;

    private static string? NormalizeOptionalText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.Trim();
    }
}
