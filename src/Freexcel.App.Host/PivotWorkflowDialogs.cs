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

