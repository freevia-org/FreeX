using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

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
    private readonly CheckBox _ungroupBox = new() { Content = UiText.Get("PivotFieldGrouping_UngroupSelectedField") };
    private readonly IReadOnlyList<PivotSourceFieldOption> _fields;

    public PivotFieldGroupingDialogResult Result { get; private set; }

    public PivotFieldGroupingDialog(IEnumerable<string> fieldNames, PivotFieldModel? currentField = null)
    {
        var fieldNameList = fieldNames.ToList();
        _fields = CreateFieldOptions(fieldNameList);
        Result = FromPivotField(fieldNameList, currentField);

        Title = UiText.Get("PivotFieldGrouping_GroupPivotField");
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
        AddCombo(selectionPanel, UiText.Get("PivotFieldGrouping_FieldLabel"), _fieldBox, _fields);
        _fieldBox.DisplayMemberPath = nameof(PivotSourceFieldOption.Name);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotFieldGrouping_SelectionGroup"), selectionPanel));

        var groupingPanel = PivotDialogLayout.CreateGroupPanel();
        AddCombo(groupingPanel, UiText.Get("PivotFieldGrouping_GroupByLabel"), _groupingBox, Enum.GetValues<PivotFieldGrouping>());
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotFieldGrouping_GroupByGroup"), groupingPanel));

        var rangePanel = PivotDialogLayout.CreateGroupPanel();
        AddTextBox(rangePanel, UiText.Get("PivotFieldGrouping_StartingAtLabel"), _startBox);
        AddTextBox(rangePanel, UiText.Get("PivotFieldGrouping_EndingAtLabel"), _endBox);
        AddTextBox(rangePanel, UiText.Get("PivotFieldGrouping_ByLabel"), _intervalBox);
        stack.Children.Add(PivotDialogLayout.CreateGroupBox(UiText.Get("PivotFieldGrouping_RangeGroup"), rangePanel));
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
        var grouping = _groupingBox.SelectedItem is PivotFieldGrouping selectedGrouping
            ? selectedGrouping
            : PivotFieldGrouping.None;
        if (_ungroupBox.IsChecked != true && !TryParseOptionalFiniteDouble(_startBox.Text, out _))
        {
            ShowInvalidInputWarning(UiText.Get("PivotFieldGrouping_EnterValidStartingValue"), _startBox);
            return;
        }

        if (_ungroupBox.IsChecked != true && !TryParseOptionalFiniteDouble(_endBox.Text, out _))
        {
            ShowInvalidInputWarning(UiText.Get("PivotFieldGrouping_EnterValidEndingValue"), _endBox);
            return;
        }

        var groupInterval = 0d;
        if (_ungroupBox.IsChecked != true
            && grouping == PivotFieldGrouping.NumberRange
            && !TryParsePositiveInterval(_intervalBox.Text, out groupInterval))
        {
            ShowInvalidInputWarning(UiText.Get("PivotFieldGrouping_EnterPositiveGroupingInterval"), _intervalBox);
            return;
        }

        var selectedField = _fieldBox.SelectedItem as PivotSourceFieldOption
            ?? _fields.FirstOrDefault(field => string.Equals(field.Name, _fieldBox.Text, StringComparison.OrdinalIgnoreCase));
        Result = CreateResult(
            selectedField?.Name ?? _fieldBox.Text,
            selectedField?.Index ?? 0,
            grouping,
            _startBox.Text,
            _endBox.Text,
            grouping == PivotFieldGrouping.NumberRange ? FormatDouble(groupInterval) : _intervalBox.Text,
            _ungroupBox.IsChecked == true);
        DialogResult = true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
        return false;
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

    private static bool TryParseOptionalFiniteDouble(string? value, out double? parsedValue)
    {
        parsedValue = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed))
        {
            return false;
        }

        parsedValue = parsed;
        return true;
    }

    private static bool TryParsePositiveInterval(string? value, out double interval)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out interval)
            || !double.IsFinite(interval)
            || interval <= 0)
        {
            interval = 0;
            return false;
        }

        return true;
    }

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

