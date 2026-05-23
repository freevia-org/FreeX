using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record PivotTableDataSourceDialogResult(string SourceRangeText);

public sealed class PivotTableDataSourceDialog : Window
{
    private readonly TextBox _sourceBox = new();

    public PivotTableDataSourceDialogResult Result { get; private set; }

    public PivotTableDataSourceDialog(string sourceRangeText)
    {
        Result = CreateResult(sourceRangeText);
        Title = "Change PivotTable Data Source";
        Width = 420;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        _sourceBox.Text = Result.SourceRangeText;
        Content = CreateContent();
    }

    public static PivotTableDataSourceDialogResult CreateResult(string sourceRangeText) =>
        new(sourceRangeText.Trim());

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

    private static DockPanel CreateReferenceEditor(TextBox textBox, string automationName)
    {
        var panel = new DockPanel();
        var pickerButton = new Button
        {
            Content = "...",
            Width = 28,
            Margin = new Thickness(0, 0, 6, 0),
            Tag = textBox
        };
        AutomationProperties.SetName(pickerButton, automationName);
        pickerButton.Click += ReferencePickerButton_Click;
        panel.Children.Add(pickerButton);
        panel.Children.Add(textBox);
        return panel;
    }

    private static void ReferencePickerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: TextBox textBox })
            return;

        textBox.Focus();
        textBox.SelectAll();
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
    }

    public static InsertTimelineDialogResult CreateResult(string dateFieldName, string timelineName) =>
        new(dateFieldName.Trim(), timelineName.Trim());

    private void Accept()
    {
        Result = CreateResult(_fieldBox.Text, _nameBox.Text);
        DialogResult = true;
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
    }

    public static PivotChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);

    private ChartTypeGalleryChoice? SelectedGalleryChoice() =>
        _tabs.SelectedIndex == 0
            ? _recommendedGallery.SelectedItem as ChartTypeGalleryChoice
            : _subtypeGallery.SelectedItem as ChartTypeGalleryChoice;
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

public sealed record PivotCalculatedFieldDialogResult(string Name, string Formula)
{
    public PivotCalculatedFieldModel ToModel() => new(Name, Formula);
}

public sealed class PivotCalculatedFieldDialog : Window
{
    private readonly TextBox _nameBox = new();
    private readonly TextBox _formulaBox = new();
    private readonly ListBox _fieldList = new() { Height = 92 };
    private readonly IReadOnlyList<string> _fields;

    public PivotCalculatedFieldDialogResult Result { get; private set; }

    public PivotCalculatedFieldDialog(string name = "", string formula = "", IEnumerable<string>? fieldNames = null)
    {
        _fields = CreateFieldNames(fieldNames ?? []);
        Result = CreateResult(name, formula);
        Title = "Calculated Field";
        Width = 480;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        _nameBox.Text = Result.Name;
        _formulaBox.Text = Result.Formula;
        _fieldList.ItemsSource = _fields;
        if (_fields.Count > 0)
            _fieldList.SelectedIndex = 0;
    }

    public static PivotCalculatedFieldDialogResult CreateResult(string name, string formula) =>
        new(name.Trim(), formula.Trim());

    public static string InsertFormulaReference(string formula, string reference, int selectionStart, int selectionLength) =>
        PivotFormulaInsertion.InsertFormulaToken(formula, reference, selectionStart, selectionLength);

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        var formulaPanel = PivotDialogLayout.CreateGroupPanel();
        AddTextBox(formulaPanel, "_Name", _nameBox);
        AddTextBox(formulaPanel, "_Formula:", _formulaBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Name and formula", formulaPanel));

        var fieldsPanel = PivotDialogLayout.CreateGroupPanel();
        PivotDialogLayout.AddLabeledControl(fieldsPanel, "Available _fields", _fieldList);
        var insertFieldButton = new Button
        {
            Content = "Insert _Field",
            Width = 110,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        insertFieldButton.Click += (_, _) => InsertSelectedField();
        _fieldList.MouseDoubleClick += (_, _) => InsertSelectedField();
        fieldsPanel.Children.Add(insertFieldButton);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Fields", fieldsPanel));

        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return stack;
    }

    private void Accept()
    {
        Result = CreateResult(_nameBox.Text, _formulaBox.Text);
        DialogResult = true;
    }

    private void InsertSelectedField()
    {
        if (_fieldList.SelectedItem is not string fieldName)
            return;

        InsertFormulaText(fieldName);
    }

    private void InsertFormulaText(string reference)
    {
        var inserted = InsertFormulaReference(
            _formulaBox.Text,
            reference,
            _formulaBox.SelectionStart,
            _formulaBox.SelectionLength);
        var caretIndex = Math.Min(inserted.Length, _formulaBox.SelectionStart + reference.Length);
        _formulaBox.Text = inserted;
        _formulaBox.Focus();
        _formulaBox.SelectionStart = caretIndex;
        _formulaBox.SelectionLength = 0;
    }

    private static IReadOnlyList<string> CreateFieldNames(IEnumerable<string> fieldNames) =>
        fieldNames
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void AddTextBox(Panel stack, string label, TextBox textBox)
    {
        PivotDialogLayout.AddLabeledControl(stack, label, textBox);
    }
}

public sealed record PivotCalculatedItemDialogResult(
    string SourceFieldName,
    int SourceFieldIndex,
    string Name,
    string Formula)
{
    public PivotCalculatedItemModel ToModel() => new(SourceFieldIndex, Name, Formula);
}

public sealed class PivotCalculatedItemDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly ListBox _fieldList = new() { Height = 80 };
    private readonly ListBox _itemList = new() { Height = 80 };
    private readonly TextBox _nameBox = new();
    private readonly TextBox _formulaBox = new();
    private readonly IReadOnlyList<PivotCalculatedItemSourceFieldOption> _fields;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<string>> _itemsBySourceFieldIndex;

    public PivotCalculatedItemDialogResult Result { get; private set; }

    public PivotCalculatedItemDialog(
        IEnumerable<string> fieldNames,
        int selectedSourceFieldIndex = 0,
        string name = "",
        string formula = "",
        IReadOnlyDictionary<int, IEnumerable<string>>? itemNamesBySourceFieldIndex = null)
    {
        _fields = CreateFieldOptions(fieldNames);
        _itemsBySourceFieldIndex = CreateItemOptions(itemNamesBySourceFieldIndex);
        var selectedField = _fields.FirstOrDefault(field => field.Index == Math.Max(0, selectedSourceFieldIndex))
            ?? _fields.FirstOrDefault();
        Result = CreateResult(selectedField?.Name ?? "", selectedField?.Index ?? 0, name, formula);

        Title = "Calculated Item";
        Width = 500;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static PivotCalculatedItemDialogResult CreateResult(
        string sourceFieldName,
        int sourceFieldIndex,
        string name,
        string formula) =>
        new(sourceFieldName.Trim(), Math.Max(0, sourceFieldIndex), name.Trim(), formula.Trim());

    public static string InsertFormulaReference(string formula, string reference, int selectionStart, int selectionLength) =>
        PivotFormulaInsertion.InsertFormulaToken(formula, reference, selectionStart, selectionLength);

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        var itemPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldBox.ItemsSource = _fields;
        _fieldBox.DisplayMemberPath = nameof(PivotCalculatedItemSourceFieldOption.Name);
        _fieldBox.SelectionChanged += (_, _) => RefreshItemList();
        PivotDialogLayout.AddLabeledControl(itemPanel, "Source _field", _fieldBox);
        AddTextBox(itemPanel, "_Name", _nameBox);
        AddTextBox(itemPanel, "Item _formula", _formulaBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Field and item", itemPanel));

        var insertPanel = PivotDialogLayout.CreateGroupPanel();
        _fieldList.ItemsSource = _fields;
        _fieldList.DisplayMemberPath = nameof(PivotCalculatedItemSourceFieldOption.Name);
        _fieldList.MouseDoubleClick += (_, _) => InsertSelectedField();
        PivotDialogLayout.AddLabeledControl(insertPanel, "Available _fields", _fieldList);
        insertPanel.Children.Add(CreateInsertButton("Insert _Field", InsertSelectedField));
        PivotDialogLayout.AddLabeledControl(insertPanel, "Available _items", _itemList);
        _itemList.MouseDoubleClick += (_, _) => InsertSelectedItem();
        insertPanel.Children.Add(CreateInsertButton("Insert _Item", InsertSelectedItem));
        stack.Children.Add(PivotDialogLayout.CreateGroupBox("Insert into formula", insertPanel));

        stack.Children.Add(PivotDialogLayout.CreateButtonRow(Accept));
        return stack;
    }

    private void Load(PivotCalculatedItemDialogResult result)
    {
        _fieldBox.SelectedItem = _fields.FirstOrDefault(field => field.Index == result.SourceFieldIndex)
            ?? _fields.FirstOrDefault();
        _nameBox.Text = result.Name;
        _formulaBox.Text = result.Formula;
        _fieldList.SelectedItem = _fieldBox.SelectedItem;
        if (_fieldList.SelectedItem is null && _fields.Count > 0)
            _fieldList.SelectedIndex = 0;
        RefreshItemList();
    }

    private void Accept()
    {
        var selectedField = _fieldBox.SelectedItem as PivotCalculatedItemSourceFieldOption;
        Result = CreateResult(
            selectedField?.Name ?? "",
            selectedField?.Index ?? 0,
            _nameBox.Text,
            _formulaBox.Text);
        DialogResult = true;
    }

    private void RefreshItemList()
    {
        var selectedField = _fieldBox.SelectedItem as PivotCalculatedItemSourceFieldOption;
        var items = selectedField is not null &&
            _itemsBySourceFieldIndex.TryGetValue(selectedField.Index, out var sourceItems)
                ? sourceItems
                : [];
        _itemList.ItemsSource = items;
        _itemList.SelectedIndex = items.Count > 0 ? 0 : -1;
    }

    private void InsertSelectedField()
    {
        var selectedField = _fieldList.SelectedItem as PivotCalculatedItemSourceFieldOption
            ?? _fieldBox.SelectedItem as PivotCalculatedItemSourceFieldOption;
        if (selectedField is null)
            return;

        InsertFormulaText(selectedField.Name);
    }

    private void InsertSelectedItem()
    {
        if (_itemList.SelectedItem is not string itemName)
            return;

        InsertFormulaText(itemName);
    }

    private void InsertFormulaText(string reference)
    {
        var inserted = InsertFormulaReference(
            _formulaBox.Text,
            reference,
            _formulaBox.SelectionStart,
            _formulaBox.SelectionLength);
        var caretIndex = Math.Min(inserted.Length, _formulaBox.SelectionStart + reference.Length);
        _formulaBox.Text = inserted;
        _formulaBox.Focus();
        _formulaBox.SelectionStart = caretIndex;
        _formulaBox.SelectionLength = 0;
    }

    private static IReadOnlyList<PivotCalculatedItemSourceFieldOption> CreateFieldOptions(IEnumerable<string> fieldNames) =>
        fieldNames
            .Select((name, index) => new PivotCalculatedItemSourceFieldOption(index, name.Trim()))
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToList();

    private static IReadOnlyDictionary<int, IReadOnlyList<string>> CreateItemOptions(
        IReadOnlyDictionary<int, IEnumerable<string>>? itemNamesBySourceFieldIndex) =>
        itemNamesBySourceFieldIndex?.ToDictionary(
            pair => Math.Max(0, pair.Key),
            pair => (IReadOnlyList<string>)pair.Value
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()) ?? new Dictionary<int, IReadOnlyList<string>>();

    private static Button CreateInsertButton(string content, Action action)
    {
        var button = new Button
        {
            Content = content,
            Width = 110,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private static void AddTextBox(Panel stack, string label, TextBox textBox)
    {
        PivotDialogLayout.AddLabeledControl(stack, label, textBox);
    }

    private sealed record PivotCalculatedItemSourceFieldOption(int Index, string Name);
}

file static class PivotFormulaInsertion
{
    public static string InsertFormulaToken(string formula, string reference, int selectionStart, int selectionLength)
    {
        var safeFormula = formula ?? "";
        var safeReference = reference ?? "";
        var start = Math.Clamp(selectionStart, 0, safeFormula.Length);
        var length = Math.Clamp(selectionLength, 0, safeFormula.Length - start);
        return safeFormula.Remove(start, length).Insert(start, safeReference);
    }
}

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
    bool MergeAndCenterLabels = false);

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
    private readonly ComboBox _styleBox = new();
    private readonly CheckBox _rowHeadersBox = new() { Content = "Row _headers" };
    private readonly CheckBox _columnHeadersBox = new() { Content = "Column hea_ders" };
    private readonly CheckBox _fieldHeadersBox = new() { Content = "Display field _captions and filter drop-downs", IsChecked = true };
    private readonly CheckBox _contextualTooltipsBox = new() { Content = "Show contextual _tooltips", IsChecked = true };
    private readonly CheckBox _propertiesInTooltipsBox = new() { Content = "Show _properties in tooltips", IsChecked = true };
    private readonly CheckBox _classicLayoutBox = new() { Content = "_Classic PivotTable layout (enables dragging of fields in the grid)" };
    private readonly CheckBox _rowStripesBox = new() { Content = "Banded _rows" };
    private readonly CheckBox _columnStripesBox = new() { Content = "Banded c_olumns" };
    private readonly TextBox _emptyCellsBox = new() { Width = 120 };
    private readonly CheckBox _autofitColumnsBox = new() { Content = "_Autofit column widths on update", IsChecked = true };
    private readonly CheckBox _preserveFormattingBox = new() { Content = "_Preserve cell formatting on update", IsChecked = true };
    private readonly CheckBox _refreshOnOpenBox = new() { Content = "_Refresh data when opening the file" };
    private readonly CheckBox _saveSourceDataBox = new() { Content = "_Save source data with file", IsChecked = true };
    private readonly CheckBox _enableRefreshBox = new() { Content = "_Enable refresh", IsChecked = true };
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
            mergeAndCenterLabels: pivotTable.MergeAndCenterLabels);

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
        bool mergeAndCenterLabels = false) =>
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
            mergeAndCenterLabels);

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
        AddCheckBox(dataPanel, new CheckBox { Content = "Preserve source sort and _filter settings", IsChecked = true });
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
        _rowStripesBox.IsChecked = result.ShowRowStripes;
        _columnStripesBox.IsChecked = result.ShowColumnStripes;
        _emptyCellsBox.Text = result.EmptyValueText ?? "";
        _autofitColumnsBox.IsChecked = result.AutofitColumnsOnUpdate;
        _preserveFormattingBox.IsChecked = result.PreserveFormattingOnUpdate;
        _refreshOnOpenBox.IsChecked = result.RefreshOnOpen;
        _saveSourceDataBox.IsChecked = result.SaveSourceData;
        _enableRefreshBox.IsChecked = result.EnableRefresh;
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
            _mergeLabelsBox.IsChecked == true);
        DialogResult = true;
    }

    private static string? NormalizeEmptyValueText(string? text) => NormalizeOptionalText(text);

    private static int ParseCompactRowLabelIndent(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? NormalizeCompactRowLabelIndent(value)
            : 1;

    private static int NormalizeCompactRowLabelIndent(int indent) => Math.Clamp(indent, 0, 15);

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
