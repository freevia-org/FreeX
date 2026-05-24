using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotTableDataSourceDialogResult(string SourceRangeText);
public sealed record PivotTableDataSourceRangeSelectionRequest(
    string CurrentText,
    bool CollapseDialog = true);

public sealed class PivotTableDataSourceDialog : Window
{
    private readonly TextBox _sourceBox = new();
    private readonly Action<PivotTableDataSourceRangeSelectionRequest>? _requestRangeSelection;

    public PivotTableDataSourceDialogResult Result { get; private set; }
    public PivotTableDataSourceRangeSelectionRequest? RangeSelectionRequest { get; private set; }

    public PivotTableDataSourceDialog(
        string sourceRangeText,
        Action<PivotTableDataSourceRangeSelectionRequest>? requestRangeSelection = null)
    {
        _requestRangeSelection = requestRangeSelection;
        Result = CreateResult(sourceRangeText);
        Title = "Change PivotTable Data Source";
        Width = 420;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _sourceBox.Text = Result.SourceRangeText;
        Content = CreateContent();
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotTableDataSourceDialogResult CreateResult(string sourceRangeText) =>
        new(sourceRangeText.Trim());

    public static PivotTableDataSourceRangeSelectionRequest CreateRangeSelectionRequest(string currentText) =>
        new(currentText.Trim(), CollapseDialog: true);

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        PivotDialogLayout.AddLabeledControl(
            stack,
            "Table/_Range:",
            CreateReferenceEditor(_sourceBox, "Select PivotTable source range"),
            _sourceBox,
            new Thickness(0, 0, 0, 16));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(() =>
        {
            Result = CreateResult(_sourceBox.Text);
            DialogResult = true;
        }));
        return stack;
    }

    private DockPanel CreateReferenceEditor(TextBox textBox, string automationName) =>
        DialogReferencePicker.CreateEditor(
            textBox,
            automationName,
            requestSelection: request =>
            {
                RangeSelectionRequest = CreateRangeSelectionRequest(request.CurrentText);
                _requestRangeSelection?.Invoke(RangeSelectionRequest);
            });

    private void FocusInitialKeyboardTarget()
    {
        _sourceBox.Focus();
        _sourceBox.SelectAll();
        Keyboard.Focus(_sourceBox);
    }
}

internal static class PivotDialogLayout
{
    public static StackPanel CreateButtonRow(Action accept) =>
        DialogButtonRowFactory.Create(accept, buttonWidth: 76, rowMargin: new Thickness(0, 12, 0, 0));

    public static GroupBox CreateGroupBox(string header, UIElement content, Thickness? margin = null) => new()
    {
        Header = header,
        Content = content,
        Margin = margin ?? new Thickness(0, 0, 0, 12)
    };

    public static StackPanel CreateGroupPanel() => new() { Margin = new Thickness(10, 8, 10, 10) };

    public static void AddLabeledControl(Panel stack, string label, UIElement control) =>
        AddLabeledControl(stack, label, control, control, new Thickness(0, 0, 0, 8));

    public static void AddLabeledControl(
        Panel stack,
        string label,
        UIElement content,
        UIElement target,
        Thickness margin)
    {
        stack.Children.Add(new Label
        {
            Content = label,
            Target = target,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 3, 0, 4)
        });

        if (content is FrameworkElement frameworkElement)
            frameworkElement.Margin = margin;

        stack.Children.Add(content);
    }
}

public sealed record InsertSlicerDialogResult(string FieldName, string SlicerName);

public sealed class InsertSlicerDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly TextBox _nameBox = new();

    public InsertSlicerDialogResult Result { get; private set; }

    public InsertSlicerDialog(IEnumerable<string> fieldNames, string? selectedField = null)
    {
        var fields = fieldNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var field = fields.FirstOrDefault(name => string.Equals(name, selectedField, StringComparison.OrdinalIgnoreCase))
            ?? fields.FirstOrDefault()
            ?? "";
        Result = CreateResult(field, $"{field} Slicer");
        Title = "Insert Slicer";
        Width = 410;
        Height = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateFieldNameContent(fields, field, Result.SlicerName, Accept);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static InsertSlicerDialogResult CreateResult(string fieldName, string slicerName) =>
        new(fieldName.Trim(), slicerName.Trim());

    private void Accept()
    {
        Result = CreateResult(_fieldBox.Text, _nameBox.Text);
        DialogResult = true;
    }

    private StackPanel CreateFieldNameContent(IReadOnlyList<string> fields, string field, string name, Action accept)
    {
        var stack = new StackPanel { Margin = new Thickness(16) };

        var fieldPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldBox.ItemsSource = fields;
        _fieldBox.Text = field;
        _fieldBox.IsEditable = true;
        PivotDialogLayout.AddLabeledControl(fieldPanel, "_Field to connect", _fieldBox);
        _nameBox.Text = name;
        PivotDialogLayout.AddLabeledControl(fieldPanel, "Slicer _caption", _nameBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Choose fields", fieldPanel));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(accept));
        return stack;
    }

    private void FocusInitialKeyboardTarget()
    {
        _fieldBox.Focus();
        Keyboard.Focus(_fieldBox);
    }
}

public sealed record InsertTimelineDialogResult(string DateFieldName, string TimelineName);

public sealed class InsertTimelineDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly TextBox _nameBox = new();

    public InsertTimelineDialogResult Result { get; private set; }

    public InsertTimelineDialog(IEnumerable<string> fieldNames, string? selectedField = null)
    {
        var fields = fieldNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        var field = fields.FirstOrDefault(name => string.Equals(name, selectedField, StringComparison.OrdinalIgnoreCase))
            ?? fields.FirstOrDefault()
            ?? "";
        Result = CreateResult(field, $"{field} Timeline");
        Title = "Insert Timeline";
        Width = 410;
        Height = 270;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };

        var fieldPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldBox.ItemsSource = fields;
        _fieldBox.Text = field;
        _fieldBox.IsEditable = true;
        PivotDialogLayout.AddLabeledControl(fieldPanel, "_Date field to connect", _fieldBox);
        _nameBox.Text = Result.TimelineName;
        PivotDialogLayout.AddLabeledControl(fieldPanel, "Timeline _caption", _nameBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Choose date fields", fieldPanel));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static InsertTimelineDialogResult CreateResult(string dateFieldName, string timelineName) =>
        new(dateFieldName.Trim(), timelineName.Trim());

    private void Accept()
    {
        Result = CreateResult(_fieldBox.Text, _nameBox.Text);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _fieldBox.Focus();
        Keyboard.Focus(_fieldBox);
    }
}

public sealed record PivotChartTypeDialogResult(ChartType ChartType);

public sealed class PivotChartTypeDialog : Window
{
    private readonly TabControl _tabs = new();
    private readonly ListBox _recommendedGallery = new();
    private readonly ListBox _categoryList = new();
    private readonly ListBox _subtypeGallery = new();

    public ChartType SelectedChartType { get; private set; }
    public PivotChartTypeDialogResult Result { get; private set; }

    public PivotChartTypeDialog(ChartType currentType)
    {
        SelectedChartType = currentType;
        Result = CreateResult(currentType);
        Title = "Change PivotChart Type";
        Width = 640;
        Height = 410;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        _tabs.Margin = new Thickness(0, 0, 0, 12);
        _tabs.Height = 290;
        _recommendedGallery.ItemsSource = ChartTypePickerPlanner.GetRecommendedGalleryChoices();
        _recommendedGallery.DisplayMemberPath = nameof(ChartTypeGalleryChoice.SubtypeName);
        _recommendedGallery.SelectedItem = ChartTypePickerPlanner.GetRecommendedGalleryChoices()
            .FirstOrDefault(choice => choice.Type == currentType);
        if (_recommendedGallery.SelectedItem is null)
            _recommendedGallery.SelectedIndex = 0;
        _tabs.Items.Add(new TabItem
        {
            Header = "Recommended PivotCharts",
            Content = InsertChartDialog.CreateRecommendedChartsPanel(_recommendedGallery)
        });

        var allChartsPanel = InsertChartDialog.CreateAllChartsPanel(_categoryList, _subtypeGallery, currentType);
        allChartsPanel.ToolTip = "Chart categories and Chart subtype gallery match the Insert Chart picker.";
        _tabs.Items.Add(new TabItem { Header = "All Charts", Content = allChartsPanel });
        stack.Children.Add(_tabs);
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(() =>
        {
            if (SelectedGalleryChoice() is { } option)
                SelectedChartType = option.Type;
            Result = CreateResult(SelectedChartType);
            DialogResult = true;
        }));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);

    private ChartTypeGalleryChoice? SelectedGalleryChoice() =>
        _tabs.SelectedIndex == 0
            ? _recommendedGallery.SelectedItem as ChartTypeGalleryChoice
            : _subtypeGallery.SelectedItem as ChartTypeGalleryChoice;

    private void FocusInitialKeyboardTarget()
    {
        _recommendedGallery.Focus();
        Keyboard.Focus(_recommendedGallery);
    }
}

public sealed class PivotChartOptionsDialogResult : IEquatable<PivotChartOptionsDialogResult>
{
    public PivotChartOptionsDialogResult(int? chartStyleId, bool showFieldButtons)
        : this(chartStyleId, showFieldButtons, true, true, true)
    {
    }

    public PivotChartOptionsDialogResult(
        int? chartStyleId,
        bool showFieldButtons,
        bool showReportFilterButtons,
        bool showAxisFieldButtons,
        bool showValueFieldButtons,
        bool showDataTable = false,
        bool showDataTableLegendKeys = false,
        bool roundedCorners = false,
        bool showHiddenData = false,
        ChartBlankDisplayMode blankDisplayMode = ChartBlankDisplayMode.Gap)
    {
        ChartStyleId = chartStyleId;
        ShowFieldButtons = showFieldButtons;
        ShowReportFilterButtons = showReportFilterButtons;
        ShowAxisFieldButtons = showAxisFieldButtons;
        ShowValueFieldButtons = showValueFieldButtons;
        ShowDataTable = showDataTable;
        ShowDataTableLegendKeys = showDataTableLegendKeys;
        RoundedCorners = roundedCorners;
        ShowHiddenData = showHiddenData;
        BlankDisplayMode = blankDisplayMode;
    }

    public int? ChartStyleId { get; }
    public bool ShowFieldButtons { get; }
    public bool ShowReportFilterButtons { get; }
    public bool ShowAxisFieldButtons { get; }
    public bool ShowValueFieldButtons { get; }
    public bool ShowDataTable { get; }
    public bool ShowDataTableLegendKeys { get; }
    public bool RoundedCorners { get; }
    public bool ShowHiddenData { get; }
    public ChartBlankDisplayMode BlankDisplayMode { get; }

    public bool Equals(PivotChartOptionsDialogResult? other) =>
        other is not null &&
        ChartStyleId == other.ChartStyleId &&
        ShowFieldButtons == other.ShowFieldButtons &&
        ShowReportFilterButtons == other.ShowReportFilterButtons &&
        ShowAxisFieldButtons == other.ShowAxisFieldButtons &&
        ShowValueFieldButtons == other.ShowValueFieldButtons &&
        ShowDataTable == other.ShowDataTable &&
        ShowDataTableLegendKeys == other.ShowDataTableLegendKeys &&
        RoundedCorners == other.RoundedCorners &&
        ShowHiddenData == other.ShowHiddenData &&
        BlankDisplayMode == other.BlankDisplayMode;

    public override bool Equals(object? obj) => Equals(obj as PivotChartOptionsDialogResult);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChartStyleId);
        hash.Add(ShowFieldButtons);
        hash.Add(ShowReportFilterButtons);
        hash.Add(ShowAxisFieldButtons);
        hash.Add(ShowValueFieldButtons);
        hash.Add(ShowDataTable);
        hash.Add(ShowDataTableLegendKeys);
        hash.Add(RoundedCorners);
        hash.Add(ShowHiddenData);
        hash.Add(BlankDisplayMode);
        return hash.ToHashCode();
    }
}

public sealed class PivotChartOptionsDialog : Window
{
    private readonly ListBox _styleGallery = new();
    private readonly CheckBox _showFieldButtonsBox = new() { Content = "_Show field buttons on chart" };
    private readonly CheckBox _showReportFilterButtonsBox = new() { Content = "Report _filter buttons" };
    private readonly CheckBox _showAxisFieldButtonsBox = new() { Content = "_Axis field buttons" };
    private readonly CheckBox _showValueFieldButtonsBox = new() { Content = "_Value field buttons" };
    private readonly CheckBox _showDataTableBox = new() { Content = "Show data _table" };
    private readonly CheckBox _showDataTableLegendKeysBox = new() { Content = "Show legend _keys" };
    private readonly CheckBox _roundedCornersBox = new() { Content = "_Rounded corners" };
    private readonly CheckBox _showHiddenDataBox = new() { Content = "Show data in _hidden rows and columns" };
    private readonly ComboBox _blankDisplayBox = new();

    public PivotChartOptionsDialogResult Result { get; private set; }

    public PivotChartOptionsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "PivotChart Options";
        Width = 420;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var styleOptions = ChartStyleDialog.GetStyleOptions();
        _styleGallery.ItemsSource = styleOptions;
        _styleGallery.ItemTemplate = CreateStyleGalleryTemplate();
        var itemsPanelFactory = new FrameworkElementFactory(typeof(UniformGrid), "PivotChartStyleGalleryPanel");
        itemsPanelFactory.SetValue(UniformGrid.ColumnsProperty, 4);
        _styleGallery.ItemsPanel = new ItemsPanelTemplate(itemsPanelFactory);
        _styleGallery.SelectedItem = styleOptions.FirstOrDefault(option => option.StyleId == Result.ChartStyleId) ?? styleOptions[0];
        _styleGallery.Height = 126;
        _styleGallery.Margin = new Thickness(0, 0, 0, 8);
        AutomationProperties.SetName(_styleGallery, "PivotChart style gallery");
        _showFieldButtonsBox.IsChecked = Result.ShowFieldButtons;
        _showFieldButtonsBox.Margin = new Thickness(0, 0, 0, 8);
        _showReportFilterButtonsBox.IsChecked = Result.ShowReportFilterButtons;
        _showReportFilterButtonsBox.Margin = new Thickness(18, 0, 0, 6);
        _showAxisFieldButtonsBox.IsChecked = Result.ShowAxisFieldButtons;
        _showAxisFieldButtonsBox.Margin = new Thickness(18, 0, 0, 6);
        _showValueFieldButtonsBox.IsChecked = Result.ShowValueFieldButtons;
        _showValueFieldButtonsBox.Margin = new Thickness(18, 0, 0, 16);
        _showDataTableBox.IsChecked = Result.ShowDataTable;
        _showDataTableBox.Margin = new Thickness(0, 0, 0, 6);
        _showDataTableLegendKeysBox.IsChecked = Result.ShowDataTableLegendKeys;
        _showDataTableLegendKeysBox.Margin = new Thickness(18, 0, 0, 16);
        _roundedCornersBox.IsChecked = Result.RoundedCorners;
        _roundedCornersBox.Margin = new Thickness(0, 0, 0, 6);
        _showHiddenDataBox.IsChecked = Result.ShowHiddenData;
        _showHiddenDataBox.Margin = new Thickness(0, 0, 0, 8);
        _blankDisplayBox.ItemsSource = new[]
        {
            new BlankDisplayChoice("Gaps", ChartBlankDisplayMode.Gap),
            new BlankDisplayChoice("Connect data points with line", ChartBlankDisplayMode.Span),
            new BlankDisplayChoice("Zero", ChartBlankDisplayMode.Zero)
        };
        _blankDisplayBox.DisplayMemberPath = nameof(BlankDisplayChoice.Label);
        _blankDisplayBox.SelectedValuePath = nameof(BlankDisplayChoice.Mode);
        _blankDisplayBox.SelectedValue = Result.BlankDisplayMode;
        _blankDisplayBox.Margin = new Thickness(0, 0, 0, 16);

        var stack = new StackPanel { Margin = new Thickness(16) };
        var stylePanel = PivotDialogLayout.CreateGroupPanel();
        stylePanel.Children.Add(new Label { Content = "Chart _style", Target = _styleGallery, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        stylePanel.Children.Add(_styleGallery);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Chart style", stylePanel));

        var buttonPanel = PivotDialogLayout.CreateGroupPanel();
        buttonPanel.Children.Add(_showFieldButtonsBox);
        buttonPanel.Children.Add(_showReportFilterButtonsBox);
        buttonPanel.Children.Add(_showAxisFieldButtonsBox);
        buttonPanel.Children.Add(_showValueFieldButtonsBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Field buttons", buttonPanel));
        var layoutPanel = PivotDialogLayout.CreateGroupPanel();
        layoutPanel.Children.Add(_showDataTableBox);
        layoutPanel.Children.Add(_showDataTableLegendKeysBox);
        layoutPanel.Children.Add(_roundedCornersBox);
        layoutPanel.Children.Add(_showHiddenDataBox);
        layoutPanel.Children.Add(new Label { Content = "_Blank cells", Target = _blankDisplayBox, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        layoutPanel.Children.Add(_blankDisplayBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Layout", layoutPanel));
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        Content = stack;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotChartOptionsDialogResult FromChart(ChartModel chart) =>
        new(
            NormalizeStyleId(chart.ChartStyleId),
            chart.ShowPivotChartFieldButtons,
            chart.ShowPivotChartReportFilterButtons,
            chart.ShowPivotChartAxisFieldButtons,
            chart.ShowPivotChartValueFieldButtons,
            chart.DataTable is not null,
            chart.DataTable?.ShowLegendKeys == true,
            chart.RoundedCorners,
            chart.ShowDataInHiddenRowsAndColumns,
            chart.BlankDisplayMode);

    public static PivotChartOptionsDialogResult CreateResult(
        string? chartStyleIdText,
        bool showFieldButtons,
        bool showReportFilterButtons = true,
        bool showAxisFieldButtons = true,
        bool showValueFieldButtons = true,
        bool showDataTable = false,
        bool showDataTableLegendKeys = false,
        bool roundedCorners = false,
        bool showHiddenData = false,
        ChartBlankDisplayMode blankDisplayMode = ChartBlankDisplayMode.Gap) =>
        new(
            ParseStyleId(chartStyleIdText),
            showFieldButtons,
            showReportFilterButtons,
            showAxisFieldButtons,
            showValueFieldButtons,
            showDataTable,
            showDataTableLegendKeys,
            roundedCorners,
            showHiddenData,
            blankDisplayMode);

    public static PivotChartOptionsDialogResult CreateResult(
        int? chartStyleId,
        bool showFieldButtons,
        bool showReportFilterButtons = true,
        bool showAxisFieldButtons = true,
        bool showValueFieldButtons = true,
        bool showDataTable = false,
        bool showDataTableLegendKeys = false,
        bool roundedCorners = false,
        bool showHiddenData = false,
        ChartBlankDisplayMode blankDisplayMode = ChartBlankDisplayMode.Gap) =>
        new(
            NormalizeStyleId(chartStyleId),
            showFieldButtons,
            showReportFilterButtons,
            showAxisFieldButtons,
            showValueFieldButtons,
            showDataTable,
            showDataTableLegendKeys,
            roundedCorners,
            showHiddenData,
            blankDisplayMode);

    private void Accept()
    {
        var selectedStyleId = _styleGallery.SelectedItem is ChartStyleOption option
            ? option.StyleId
            : null;
        Result = CreateResult(
            selectedStyleId,
            _showFieldButtonsBox.IsChecked == true,
            _showReportFilterButtonsBox.IsChecked == true,
            _showAxisFieldButtonsBox.IsChecked == true,
            _showValueFieldButtonsBox.IsChecked == true,
            _showDataTableBox.IsChecked == true,
            _showDataTableLegendKeysBox.IsChecked == true,
            _roundedCornersBox.IsChecked == true,
            _showHiddenDataBox.IsChecked == true,
            _blankDisplayBox.SelectedValue is ChartBlankDisplayMode mode ? mode : ChartBlankDisplayMode.Gap);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _styleGallery.Focus();
        Keyboard.Focus(_styleGallery);
    }

    private sealed record BlankDisplayChoice(string Label, ChartBlankDisplayMode Mode);

    private static int? ParseStyleId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return int.TryParse(text.Trim(), out var value) ? NormalizeStyleId(value) : null;
    }

    private static DataTemplate CreateStyleGalleryTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        root.SetValue(StackPanel.MarginProperty, new Thickness(3));
        root.SetValue(StackPanel.WidthProperty, 82.0);

        var preview = new FrameworkElementFactory(typeof(Border));
        preview.SetValue(Border.BorderBrushProperty, SystemColors.ControlDarkBrush);
        preview.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        preview.SetValue(Border.HeightProperty, 28.0);
        preview.SetValue(Border.BackgroundProperty, Brushes.White);

        var bars = new FrameworkElementFactory(typeof(StackPanel));
        bars.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        bars.SetValue(StackPanel.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        bars.SetValue(StackPanel.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Bottom);
        bars.SetValue(StackPanel.MarginProperty, new Thickness(0, 0, 0, 4));
        foreach (var height in new[] { 12.0, 19.0, 15.0 })
        {
            var bar = new FrameworkElementFactory(typeof(Border));
            bar.SetValue(Border.WidthProperty, 8.0);
            bar.SetValue(Border.HeightProperty, height);
            bar.SetValue(Border.MarginProperty, new Thickness(2, 0, 2, 0));
            bar.SetValue(Border.BackgroundProperty, SystemColors.HighlightBrush);
            bars.AppendChild(bar);
        }

        preview.AppendChild(bars);
        root.AppendChild(preview);

        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChartStyleOption.DisplayName)));
        label.SetValue(TextBlock.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        label.SetValue(TextBlock.FontSizeProperty, 10.0);
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        label.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 0));
        root.AppendChild(label);

        return new DataTemplate { VisualTree = root };
    }

    private static int? NormalizeStyleId(int? value)
    {
        if (value is null)
            return null;

        return Math.Clamp(value.Value, 1, 48);
    }
}

public sealed record PivotFieldGroupingDialogResult(
    string SourceFieldName,
    int SourceFieldIndex,
    PivotFieldGrouping Grouping,
    double? GroupStart,
    double? GroupEnd,
    double? GroupInterval,
    bool Ungroup);

public sealed class PivotFieldGroupingDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly ComboBox _groupingBox = new();
    private readonly TextBox _startBox = new();
    private readonly TextBox _endBox = new();
    private readonly TextBox _intervalBox = new();
    private readonly CheckBox _ungroupBox = new() { Content = "_Ungroup selected field" };
    private readonly IReadOnlyList<PivotSourceFieldOption> _fields;

    public PivotFieldGroupingDialogResult Result { get; private set; }

    public PivotFieldGroupingDialog(IEnumerable<string> fieldNames, PivotFieldModel? currentField = null)
    {
        var fieldNameList = fieldNames.ToList();
        _fields = CreateFieldOptions(fieldNameList);
        Result = FromPivotField(fieldNameList, currentField);

        Title = "Group Pivot Field";
        Width = 420;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    public static PivotFieldGroupingDialogResult FromPivotField(IEnumerable<string> fieldNames, PivotFieldModel? currentField)
    {
        var fields = fieldNames.ToList();
        var sourceFieldIndex = Math.Max(0, currentField?.SourceFieldIndex ?? 0);
        var sourceFieldName = sourceFieldIndex < fields.Count ? fields[sourceFieldIndex] : fields.FirstOrDefault() ?? "";

        return CreateResult(
            sourceFieldName,
            sourceFieldIndex,
            currentField?.Grouping ?? PivotFieldGrouping.None,
            currentField?.GroupStart,
            currentField?.GroupEnd,
            currentField?.GroupInterval,
            ungroup: false);
    }

    public static PivotFieldGroupingDialogResult CreateResult(
        string sourceFieldName,
        int sourceFieldIndex,
        PivotFieldGrouping grouping,
        string? groupStartText,
        string? groupEndText,
        string? groupIntervalText,
        bool ungroup) =>
        CreateResult(
            sourceFieldName,
            sourceFieldIndex,
            grouping,
            ParseOptionalDouble(groupStartText),
            ParseOptionalDouble(groupEndText),
            ParseOptionalDouble(groupIntervalText),
            ungroup);

    public static PivotFieldGroupingDialogResult CreateResult(
        string sourceFieldName,
        int sourceFieldIndex,
        PivotFieldGrouping grouping,
        double? groupStart,
        double? groupEnd,
        double? groupInterval,
        bool ungroup)
    {
        if (ungroup)
            return new(sourceFieldName.Trim(), Math.Max(0, sourceFieldIndex), PivotFieldGrouping.None, null, null, null, true);

        var normalizedGrouping = grouping;
        if (normalizedGrouping == PivotFieldGrouping.None)
            return new(sourceFieldName.Trim(), Math.Max(0, sourceFieldIndex), PivotFieldGrouping.None, null, null, null, false);

        var normalizedInterval = normalizedGrouping == PivotFieldGrouping.NumberRange
            ? Math.Max(1, groupInterval ?? 1)
            : groupInterval;

        return new(
            sourceFieldName.Trim(),
            Math.Max(0, sourceFieldIndex),
            normalizedGrouping,
            groupStart,
            groupEnd,
            normalizedInterval,
            false);
    }

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };

        var selectionPanel = PivotDialogLayout.CreateGroupPanel();
        AddCombo(selectionPanel, "_Field", _fieldBox, _fields);
        _fieldBox.DisplayMemberPath = nameof(PivotSourceFieldOption.Name);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Selection", selectionPanel));

        var groupingPanel = PivotDialogLayout.CreateGroupPanel();
        AddCombo(groupingPanel, "_Group by", _groupingBox, Enum.GetValues<PivotFieldGrouping>());
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Group by", groupingPanel));

        var rangePanel = PivotDialogLayout.CreateGroupPanel();
        AddTextBox(rangePanel, "_Starting at", _startBox);
        AddTextBox(rangePanel, "_Ending at", _endBox);
        AddTextBox(rangePanel, "_By", _intervalBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Range", rangePanel));
        _ungroupBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_ungroupBox);
        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return stack;
    }

    private void Load(PivotFieldGroupingDialogResult result)
    {
        _fieldBox.SelectedItem = _fields.FirstOrDefault(field => field.Index == result.SourceFieldIndex)
            ?? _fields.FirstOrDefault();
        _groupingBox.SelectedItem = result.Grouping;
        _startBox.Text = FormatDouble(result.GroupStart);
        _endBox.Text = FormatDouble(result.GroupEnd);
        _intervalBox.Text = FormatDouble(result.GroupInterval);
        _ungroupBox.IsChecked = result.Ungroup;
    }

    private void Accept()
    {
        var selectedField = _fieldBox.SelectedItem as PivotSourceFieldOption
            ?? _fields.FirstOrDefault(field => string.Equals(field.Name, _fieldBox.Text, StringComparison.OrdinalIgnoreCase));
        Result = CreateResult(
            selectedField?.Name ?? _fieldBox.Text,
            selectedField?.Index ?? 0,
            _groupingBox.SelectedItem is PivotFieldGrouping grouping ? grouping : PivotFieldGrouping.None,
            _startBox.Text,
            _endBox.Text,
            _intervalBox.Text,
            _ungroupBox.IsChecked == true);
        DialogResult = true;
    }

    private void FocusInitialKeyboardTarget()
    {
        _fieldBox.Focus();
        Keyboard.Focus(_fieldBox);
    }

    private static IReadOnlyList<PivotSourceFieldOption> CreateFieldOptions(IEnumerable<string> fieldNames) =>
        fieldNames
            .Select((name, index) => new PivotSourceFieldOption(index, name.Trim()))
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToList();

    private static double? ParseOptionalDouble(string? value) =>
        double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string FormatDouble(double? value) =>
        value?.ToString("G", CultureInfo.InvariantCulture) ?? "";

    private static void AddTextBox(Panel stack, string label, TextBox textBox)
    {
        PivotDialogLayout.AddLabeledControl(stack, label, textBox);
    }

    private static void AddCombo<T>(Panel stack, string label, ComboBox comboBox, IEnumerable<T> items)
    {
        comboBox.ItemsSource = items;
        PivotDialogLayout.AddLabeledControl(stack, label, comboBox);
    }

    private sealed record PivotSourceFieldOption(int Index, string Name);
}

