using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
        Content = ObjectSizeDialog.CreateSingleInputContent("Table/Range:", _sourceBox, () =>
        {
            Result = CreateResult(_sourceBox.Text);
            DialogResult = true;
        });
    }

    public static PivotTableDataSourceDialogResult CreateResult(string sourceRangeText) =>
        new(sourceRangeText.Trim());
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
        Width = 360;
        Height = 210;
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
        stack.Children.Add(new TextBlock { Text = "Field", Margin = new Thickness(0, 0, 0, 4) });
        _fieldBox.ItemsSource = fields;
        _fieldBox.Text = field;
        _fieldBox.IsEditable = true;
        _fieldBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_fieldBox);
        stack.Children.Add(new TextBlock { Text = "Name", Margin = new Thickness(0, 0, 0, 4) });
        _nameBox.Text = name;
        _nameBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_nameBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(accept));
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
        Width = 360;
        Height = 210;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Date field", Margin = new Thickness(0, 0, 0, 4) });
        _fieldBox.ItemsSource = fields;
        _fieldBox.Text = field;
        _fieldBox.IsEditable = true;
        _fieldBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_fieldBox);
        stack.Children.Add(new TextBlock { Text = "Name", Margin = new Thickness(0, 0, 0, 4) });
        _nameBox.Text = Result.TimelineName;
        _nameBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_nameBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
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
    private readonly ComboBox _chartTypeBox = new();

    public ChartType SelectedChartType { get; private set; }
    public PivotChartTypeDialogResult Result { get; private set; }

    public PivotChartTypeDialog(ChartType currentType)
    {
        SelectedChartType = currentType;
        Result = CreateResult(currentType);
        Title = "Change PivotChart Type";
        Width = 340;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var options = ChartTypePickerPlanner.GetSupportedOptions();
        _chartTypeBox.ItemsSource = options;
        _chartTypeBox.DisplayMemberPath = nameof(ChartTypePickerOption.DisplayName);
        _chartTypeBox.SelectedItem = options.FirstOrDefault(option => option.Type == currentType);
        _chartTypeBox.Margin = new Thickness(0, 0, 0, 16);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(_chartTypeBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(() =>
        {
            if (_chartTypeBox.SelectedItem is ChartTypePickerOption option)
                SelectedChartType = option.Type;
            Result = CreateResult(SelectedChartType);
            DialogResult = true;
        }));
        Content = stack;
    }

    public static PivotChartTypeDialogResult CreateResult(ChartType chartType) => new(chartType);
}

public sealed record PivotChartOptionsDialogResult(int? ChartStyleId, bool ShowFieldButtons);

public sealed class PivotChartOptionsDialog : Window
{
    private readonly TextBox _styleBox = new();
    private readonly CheckBox _showFieldButtonsBox = new() { Content = "Show field buttons on chart" };

    public PivotChartOptionsDialogResult Result { get; private set; }

    public PivotChartOptionsDialog(ChartModel chart)
    {
        Result = FromChart(chart);
        Title = "PivotChart Options";
        Width = 360;
        Height = 190;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        _styleBox.Text = Result.ChartStyleId?.ToString() ?? "";
        _styleBox.Margin = new Thickness(0, 0, 0, 12);
        _showFieldButtonsBox.IsChecked = Result.ShowFieldButtons;
        _showFieldButtonsBox.Margin = new Thickness(0, 0, 0, 16);

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Chart style ID", Margin = new Thickness(0, 0, 0, 4) });
        stack.Children.Add(_styleBox);
        stack.Children.Add(_showFieldButtonsBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        Content = stack;
    }

    public static PivotChartOptionsDialogResult FromChart(ChartModel chart) =>
        new(NormalizeStyleId(chart.ChartStyleId), chart.ShowPivotChartFieldButtons);

    public static PivotChartOptionsDialogResult CreateResult(string? chartStyleIdText, bool showFieldButtons) =>
        new(ParseStyleId(chartStyleIdText), showFieldButtons);

    private void Accept()
    {
        Result = CreateResult(_styleBox.Text, _showFieldButtonsBox.IsChecked == true);
        DialogResult = true;
    }

    private static int? ParseStyleId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return int.TryParse(text.Trim(), out var value) ? NormalizeStyleId(value) : null;
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
    private readonly CheckBox _ungroupBox = new() { Content = "Ungroup selected field" };
    private readonly IReadOnlyList<PivotSourceFieldOption> _fields;

    public PivotFieldGroupingDialogResult Result { get; private set; }

    public PivotFieldGroupingDialog(IEnumerable<string> fieldNames, PivotFieldModel? currentField = null)
    {
        var fieldNameList = fieldNames.ToList();
        _fields = CreateFieldOptions(fieldNameList);
        Result = FromPivotField(fieldNameList, currentField);

        Title = "Group Pivot Field";
        Width = 360;
        Height = 340;
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
        AddCombo(stack, "Field", _fieldBox, _fields);
        _fieldBox.DisplayMemberPath = nameof(PivotSourceFieldOption.Name);

        AddCombo(stack, "Group by", _groupingBox, Enum.GetValues<PivotFieldGrouping>());
        AddTextBox(stack, "Starting at", _startBox);
        AddTextBox(stack, "Ending at", _endBox);
        AddTextBox(stack, "By", _intervalBox);
        _ungroupBox.Margin = new Thickness(0, 0, 0, 16);
        stack.Children.Add(_ungroupBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
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
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) });
        textBox.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(textBox);
    }

    private static void AddCombo<T>(Panel stack, string label, ComboBox comboBox, IEnumerable<T> items)
    {
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) });
        comboBox.ItemsSource = items;
        comboBox.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(comboBox);
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

    public PivotCalculatedFieldDialogResult Result { get; private set; }

    public PivotCalculatedFieldDialog(string name = "", string formula = "")
    {
        Result = CreateResult(name, formula);
        Title = "Calculated Field";
        Width = 360;
        Height = 220;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        _nameBox.Text = Result.Name;
        _formulaBox.Text = Result.Formula;
    }

    public static PivotCalculatedFieldDialogResult CreateResult(string name, string formula) =>
        new(name.Trim(), formula.Trim());

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        AddTextBox(stack, "Name", _nameBox);
        AddTextBox(stack, "Formula", _formulaBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }

    private void Accept()
    {
        Result = CreateResult(_nameBox.Text, _formulaBox.Text);
        DialogResult = true;
    }

    private static void AddTextBox(Panel stack, string label, TextBox textBox)
    {
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        textBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(textBox);
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
    private readonly TextBox _nameBox = new();
    private readonly TextBox _formulaBox = new();
    private readonly IReadOnlyList<PivotCalculatedItemSourceFieldOption> _fields;

    public PivotCalculatedItemDialogResult Result { get; private set; }

    public PivotCalculatedItemDialog(IEnumerable<string> fieldNames, int selectedSourceFieldIndex = 0, string name = "", string formula = "")
    {
        _fields = CreateFieldOptions(fieldNames);
        var selectedField = _fields.FirstOrDefault(field => field.Index == Math.Max(0, selectedSourceFieldIndex))
            ?? _fields.FirstOrDefault();
        Result = CreateResult(selectedField?.Name ?? "", selectedField?.Index ?? 0, name, formula);

        Title = "Calculated Item";
        Width = 360;
        Height = 260;
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

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Field", Margin = new Thickness(0, 0, 0, 4) });
        _fieldBox.ItemsSource = _fields;
        _fieldBox.DisplayMemberPath = nameof(PivotCalculatedItemSourceFieldOption.Name);
        _fieldBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(_fieldBox);
        AddTextBox(stack, "Name", _nameBox);
        AddTextBox(stack, "Formula", _formulaBox);
        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }

    private void Load(PivotCalculatedItemDialogResult result)
    {
        _fieldBox.SelectedItem = _fields.FirstOrDefault(field => field.Index == result.SourceFieldIndex)
            ?? _fields.FirstOrDefault();
        _nameBox.Text = result.Name;
        _formulaBox.Text = result.Formula;
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

    private static IReadOnlyList<PivotCalculatedItemSourceFieldOption> CreateFieldOptions(IEnumerable<string> fieldNames) =>
        fieldNames
            .Select((name, index) => new PivotCalculatedItemSourceFieldOption(index, name.Trim()))
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToList();

    private static void AddTextBox(Panel stack, string label, TextBox textBox)
    {
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        textBox.Margin = new Thickness(0, 0, 0, 12);
        stack.Children.Add(textBox);
    }

    private sealed record PivotCalculatedItemSourceFieldOption(int Index, string Name);
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
    PivotReportLayout ReportLayout);

public sealed class PivotTableOptionsDialog : Window
{
    private static readonly string[] StyleNames =
    [
        "PivotStyleLight16",
        "PivotStyleMedium2",
        "PivotStyleMedium9",
        "PivotStyleDark4"
    ];

    private readonly CheckBox _rowGrandTotalsBox = new() { Content = "Show row grand totals" };
    private readonly CheckBox _columnGrandTotalsBox = new() { Content = "Show column grand totals" };
    private readonly CheckBox _subtotalsBox = new() { Content = "Show subtotals" };
    private readonly ComboBox _subtotalPlacementBox = new();
    private readonly CheckBox _repeatItemLabelsBox = new() { Content = "Repeat item labels" };
    private readonly CheckBox _blankLineBox = new() { Content = "Insert blank line after each item" };
    private readonly ComboBox _reportLayoutBox = new();
    private readonly ComboBox _styleBox = new();
    private readonly CheckBox _rowHeadersBox = new() { Content = "Row headers" };
    private readonly CheckBox _columnHeadersBox = new() { Content = "Column headers" };
    private readonly CheckBox _rowStripesBox = new() { Content = "Banded rows" };
    private readonly CheckBox _columnStripesBox = new() { Content = "Banded columns" };

    public PivotTableOptionsDialogResult Result { get; private set; }

    public PivotTableOptionsDialog(PivotTableModel pivotTable)
    {
        Result = FromPivotTable(pivotTable);
        Title = "PivotTable Options";
        Width = 430;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Content = CreateContent();
        Load(Result);
    }

    public static PivotTableOptionsDialogResult FromPivotTable(PivotTableModel pivotTable) =>
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
            pivotTable.ReportLayout);

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
        PivotReportLayout reportLayout) =>
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
            reportLayout);

    private StackPanel CreateContent()
    {
        var stack = new StackPanel { Margin = new Thickness(16) };
        AddSectionHeader(stack, "Layout");
        AddCheckBox(stack, _rowGrandTotalsBox);
        AddCheckBox(stack, _columnGrandTotalsBox);
        AddCheckBox(stack, _subtotalsBox);
        AddCombo(stack, "Subtotal placement", _subtotalPlacementBox, Enum.GetValues<PivotSubtotalPlacement>());
        AddCombo(stack, "Report layout", _reportLayoutBox, Enum.GetValues<PivotReportLayout>());
        AddCheckBox(stack, _repeatItemLabelsBox);
        AddCheckBox(stack, _blankLineBox);

        AddSectionHeader(stack, "Style");
        AddCombo(stack, "PivotTable style", _styleBox, StyleNames);
        AddCheckBox(stack, _rowHeadersBox);
        AddCheckBox(stack, _columnHeadersBox);
        AddCheckBox(stack, _rowStripesBox);
        AddCheckBox(stack, _columnStripesBox);

        stack.Children.Add(InsertChartDialog.CreateButtonRow(Accept));
        return stack;
    }

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

    private static void AddCombo<T>(Panel stack, string label, ComboBox comboBox, IEnumerable<T> items)
    {
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 3, 0, 4) });
        comboBox.ItemsSource = items;
        comboBox.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(comboBox);
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
        _styleBox.SelectedItem = StyleNames.Contains(result.StyleName) ? result.StyleName : StyleNames[0];
        _rowHeadersBox.IsChecked = result.ShowRowHeaders;
        _columnHeadersBox.IsChecked = result.ShowColumnHeaders;
        _rowStripesBox.IsChecked = result.ShowRowStripes;
        _columnStripesBox.IsChecked = result.ShowColumnStripes;
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
                : PivotReportLayout.Tabular);
        DialogResult = true;
    }
}
