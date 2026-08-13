using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Presentation;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed class PivotFieldGroupingDialog : Window
{
    private readonly ComboBox _fieldBox = new();
    private readonly ComboBox _groupingBox = new();
    private readonly TextBox _startBox = new();
    private readonly TextBox _endBox = new();
    private readonly TextBox _intervalBox = new();
    private readonly CheckBox _ungroupBox = new() { Content = UiText.Get("PivotFieldGrouping_UngroupSelectedField") };
    private readonly IReadOnlyList<PivotSourceFieldOption> _fields;

    public PivotGroupFieldSubmission Result { get; private set; }

    public PivotFieldGroupingDialog(IEnumerable<string> fieldNames, PivotFieldModel? currentField = null)
    {
        var fieldNameList = fieldNames.ToList();
        _fields = CreateFieldOptions(fieldNameList);
        Result = PivotGroupFieldPlanner.CaptureSubmission(fieldNameList, currentField);

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

    private void Load(PivotGroupFieldSubmission result)
    {
        _fieldBox.SelectedItem = FindFieldBySourceIndexOrFirst(_fields, result.Field.SourceFieldIndex);
        _groupingBox.SelectedItem = result.Field.Grouping;
        _startBox.Text = PivotGroupFieldPlanner.FormatBound(result.Field.GroupStart);
        _endBox.Text = PivotGroupFieldPlanner.FormatBound(result.Field.GroupEnd);
        _intervalBox.Text = PivotGroupFieldPlanner.FormatBound(result.Field.GroupInterval);
        _ungroupBox.IsChecked = result.Ungroup;
    }

    private void Accept()
    {
        var grouping = _groupingBox.SelectedItem is PivotFieldGrouping selectedGrouping
            ? selectedGrouping
            : PivotFieldGrouping.None;
        var selectedField = GetSelectedField();
        if (!PivotGroupFieldPlanner.TryCreateSubmission(
                selectedField?.Name ?? _fieldBox.Text,
                selectedField?.Index ?? 0,
                grouping,
                _ungroupBox.IsChecked == true,
                _startBox.Text,
                _endBox.Text,
                _intervalBox.Text,
                out var submission,
                out var error))
        {
            var (message, target) = error switch
            {
                PivotGroupFieldPlanner.InvalidEndMessage =>
                    (UiText.Get("PivotFieldGrouping_EnterValidEndingValue"), _endBox),
                PivotGroupFieldPlanner.InvalidIntervalMessage =>
                    (UiText.Get("PivotFieldGrouping_EnterPositiveGroupingInterval"), _intervalBox),
                _ => (UiText.Get("PivotFieldGrouping_EnterValidStartingValue"), _startBox),
            };
            ShowInvalidInputWarning(message, target);
            return;
        }

        Result = submission!;
        DialogResult = true;
    }

    private bool ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogFocus.ShowWarningAndFocus(this, message, Title, target);
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

    private PivotSourceFieldOption? GetSelectedField() =>
        _fieldBox.SelectedItem as PivotSourceFieldOption
        ?? FindFieldByName(_fields, _fieldBox.Text);

    private static PivotSourceFieldOption? FindFieldBySourceIndexOrFirst(
        IReadOnlyList<PivotSourceFieldOption> fields,
        int sourceFieldIndex)
    {
        var normalizedIndex = Math.Max(0, sourceFieldIndex);
        foreach (var field in fields)
            if (field.Index == normalizedIndex)
                return field;

        return fields.Count > 0 ? fields[0] : null;
    }

    private static PivotSourceFieldOption? FindFieldByName(
        IReadOnlyList<PivotSourceFieldOption> fields,
        string fieldName)
    {
        foreach (var field in fields)
            if (string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                return field;

        return null;
    }

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

