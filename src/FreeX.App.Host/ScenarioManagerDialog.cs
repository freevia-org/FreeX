using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public sealed record ScenarioManagerItem(
    string Name,
    IReadOnlyList<ScenarioCellValue> ChangingCells,
    string? Comment,
    string ChangingCellsText,
    bool Hidden,
    bool Locked);

public sealed partial class ScenarioManagerDialog : Window
{
    private readonly ListBox _scenarioList = new();
    private readonly TextBox _newNameBox = new();
    private readonly TextBox _changingCellsBox = new();
    private readonly TextBox _resultCellsBox = new();
    private readonly TextBox _commentBox = new();
    private readonly CheckBox _lockedBox = new() { Content = "_Prevent changes", Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _hiddenBox = new() { Content = "_Hide", Margin = new Thickness(0, 0, 0, 8) };
    private readonly string _defaultScenarioName;
    private readonly SheetId? _currentSheetId;
    private readonly Func<string, SheetId?>? _resolveSheetIdByName;
    private Button? _addButton;
    private Button? _editButton;
    private Button? _deleteButton;
    private Button? _showButton;

    public ScenarioManagerAction SelectedAction { get; private set; } = ScenarioManagerAction.Show;
    public string? SelectedScenarioName { get; private set; }
    public string? NewScenarioName { get; private set; }
    public string? ChangingCellsText { get; private set; }
    public string? ResultCellsText { get; private set; }
    public string? CommentText { get; private set; }
    public bool ScenarioHidden { get; private set; }
    public bool ScenarioLocked { get; private set; }

    public ScenarioManagerDialog(
        Workbook workbook,
        SheetId? currentSheetId = null,
        Func<string, SheetId?>? resolveSheetIdByName = null)
    {
        _currentSheetId = currentSheetId;
        _resolveSheetIdByName = resolveSheetIdByName;
        Title = "Scenario Manager";
        Width = 360;
        Height = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _defaultScenarioName = workbook.Scenarios.Count == 0 ? "Scenario 1" : $"Scenario {workbook.Scenarios.Count + 1}";

        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(body, 0);
        root.Children.Add(body);

        var left = new StackPanel();
        Grid.SetColumn(left, 0);
        body.Children.Add(left);

        left.Children.Add(new Label { Content = "_Scenarios:", Target = _scenarioList, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4) });
        AutomationProperties.SetName(_scenarioList, "Scenarios");
        AutomationProperties.SetAutomationId(_scenarioList, "ScenarioManagerScenarioList");
        AutomationProperties.SetHelpText(_scenarioList, "Select a scenario to show, edit, or delete.");
        _scenarioList.ItemsSource = BuildScenarioItems(workbook);
        _scenarioList.DisplayMemberPath = nameof(ScenarioManagerItem.Name);
        _scenarioList.SelectionChanged += (_, _) => UpdateSelectionState();
        _scenarioList.MouseDoubleClick += (_, _) => AcceptSelectedScenario();
        _scenarioList.SelectedIndex = _scenarioList.Items.Count > 0 ? 0 : -1;
        _scenarioList.Height = 118;
        left.Children.Add(_scenarioList);

        var editor = new GroupBox
        {
            Header = "Add/Edit Scenario",
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(8)
        };
        left.Children.Add(editor);

        var fields = new Grid();
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.Content = fields;

        AddField(fields, row: 0, "Scenario _name:", _newNameBox);
        _newNameBox.Text = _defaultScenarioName;
        AddField(fields, row: 1, "Changing _cells:", _changingCellsBox);
        AddField(fields, row: 2, "_Result cells:", _resultCellsBox);
        AddField(fields, row: 3, "_Comment:", _commentBox);
        AddCheckBox(fields, row: 4, _lockedBox);
        AddCheckBox(fields, row: 5, _hiddenBox);
        AutomationProperties.SetName(_newNameBox, "Scenario name");
        AutomationProperties.SetAutomationId(_newNameBox, "ScenarioManagerScenarioNameBox");
        AutomationProperties.SetHelpText(_newNameBox, "Enter the scenario name to add or edit.");
        AutomationProperties.SetName(_changingCellsBox, "Changing cells");
        AutomationProperties.SetAutomationId(_changingCellsBox, "ScenarioManagerChangingCellsBox");
        AutomationProperties.SetHelpText(_changingCellsBox, "Enter the worksheet cells whose values change in the scenario.");
        AutomationProperties.SetName(_resultCellsBox, "Result cells");
        AutomationProperties.SetAutomationId(_resultCellsBox, "ScenarioManagerResultCellsBox");
        AutomationProperties.SetHelpText(_resultCellsBox, "Enter optional result cells to include in a scenario summary.");
        AutomationProperties.SetName(_commentBox, "Comment");
        AutomationProperties.SetAutomationId(_commentBox, "ScenarioManagerCommentBox");
        AutomationProperties.SetHelpText(_commentBox, "Enter an optional comment for the scenario.");
        AutomationProperties.SetName(_lockedBox, "Prevent changes");
        AutomationProperties.SetAutomationId(_lockedBox, "ScenarioManagerPreventChangesCheckBox");
        AutomationProperties.SetHelpText(_lockedBox, "Prevent changes to the scenario when the sheet is protected.");
        AutomationProperties.SetName(_hiddenBox, "Hide");
        AutomationProperties.SetAutomationId(_hiddenBox, "ScenarioManagerHideCheckBox");
        AutomationProperties.SetHelpText(_hiddenBox, "Hide the scenario when the sheet is protected.");

        var sideButtons = new StackPanel { Margin = new Thickness(10, 20, 0, 0) };
        Grid.SetColumn(sideButtons, 1);
        body.Children.Add(sideButtons);
        _addButton = AddActionButton(sideButtons, "_Add...", ScenarioManagerAction.Add, isDefault: _scenarioList.Items.Count == 0);
        _editButton = AddActionButton(sideButtons, "_Edit...", ScenarioManagerAction.Edit, isEnabled: false);
        _deleteButton = AddActionButton(sideButtons, "_Delete", ScenarioManagerAction.Delete, isEnabled: false);
        AddActionButton(sideButtons, "_List...", ScenarioManagerAction.List);
        _showButton = AddActionButton(sideButtons, "_Show", ScenarioManagerAction.Show, isEnabled: _scenarioList.SelectedItem is not null, isDefault: _scenarioList.SelectedItem is not null);
        AddActionButton(sideButtons, "S_ummary...", ScenarioManagerAction.Report);
        UpdateSelectionState();

        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(closeRow, 1);
        root.Children.Add(closeRow);
        closeRow.Children.Add(new Button { Content = "_Close", Width = 72, IsCancel = true });

        Content = root;
        Loaded += (_, _) => FocusInitialKeyboardTarget();
    }

    private static void AddField(Grid grid, int row, string label, Control field)
    {
        var text = new Label
        {
            Content = label,
            Target = field,
            Padding = new Thickness(0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 8)
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        field.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
    }

    private static void AddCheckBox(Grid grid, int row, CheckBox checkBox)
    {
        Grid.SetRow(checkBox, row);
        Grid.SetColumn(checkBox, 1);
        grid.Children.Add(checkBox);
    }

    private Button AddActionButton(Panel panel, string label, ScenarioManagerAction action, bool isEnabled = true, bool isDefault = false)
    {
        var button = new Button { Content = label, Width = 82, Margin = new Thickness(0, 0, 0, 6), IsEnabled = isEnabled, IsDefault = isDefault };
        button.Click += (_, _) => Accept(action);
        panel.Children.Add(button);
        return button;
    }

    private void FocusInitialKeyboardTarget()
    {
        Control target = _scenarioList.Items.Count > 0 ? _scenarioList : _newNameBox;
        target.Focus();
        Keyboard.Focus(target);
    }

    private void UpdateSelectionState()
    {
        var selected = _scenarioList.SelectedItem as ScenarioManagerItem;
        if (ProjectSelectionFields(selected, _newNameBox.Text, _defaultScenarioName) is { } fields)
        {
            ApplySelectionFields(fields);
        }

        var hasSelection = selected is not null;
        if (_addButton is not null) _addButton.IsDefault = !hasSelection;
        if (_editButton is not null) _editButton.IsEnabled = hasSelection;
        if (_deleteButton is not null) _deleteButton.IsEnabled = hasSelection;
        if (_showButton is not null)
        {
            _showButton.IsEnabled = hasSelection;
            _showButton.IsDefault = hasSelection;
        }
    }

    private void AcceptSelectedScenario()
    {
        if (_scenarioList.SelectedItem is null)
        {
            FocusInitialKeyboardTarget();
            return;
        }

        Accept(ScenarioManagerAction.Show);
    }

    private void Accept(ScenarioManagerAction action)
    {
        if (ValidateAcceptRequest(
                action,
                _newNameBox.Text,
                _changingCellsBox.Text,
                _resultCellsBox.Text,
                _currentSheetId,
                _resolveSheetIdByName) is { } failure)
        {
            ShowInvalidInputWarning(failure.Message, GetValidationTarget(failure.Field));
            return;
        }

        ApplyAcceptResult(ProjectAcceptResult(
            action,
            _scenarioList.SelectedItem as ScenarioManagerItem,
            _newNameBox.Text,
            _changingCellsBox.Text,
            _resultCellsBox.Text,
            _commentBox.Text,
            _lockedBox.IsChecked == true,
            _hiddenBox.IsChecked == true));
        DialogResult = true;
    }

    private void ApplySelectionFields(ScenarioManagerSelectionFields fields)
    {
        _newNameBox.Text = fields.ScenarioName;
        _changingCellsBox.Text = fields.ChangingCellsText;
        _resultCellsBox.Text = fields.ResultCellsText;
        _commentBox.Text = fields.CommentText;
        _lockedBox.IsChecked = fields.Locked;
        _hiddenBox.IsChecked = fields.Hidden;
    }

    private void ApplyAcceptResult(ScenarioManagerAcceptResult result)
    {
        SelectedAction = result.Action;
        SelectedScenarioName = result.SelectedScenarioName;
        NewScenarioName = result.NewScenarioName;
        ChangingCellsText = result.ChangingCellsText;
        ResultCellsText = result.ResultCellsText;
        CommentText = result.CommentText;
        ScenarioLocked = result.Locked;
        ScenarioHidden = result.Hidden;
    }

    private TextBox GetValidationTarget(ScenarioManagerValidationField field) =>
        field switch
        {
            ScenarioManagerValidationField.ScenarioName => _newNameBox,
            ScenarioManagerValidationField.ChangingCells => _changingCellsBox,
            ScenarioManagerValidationField.ResultCells => _resultCellsBox,
            _ => _newNameBox
        };

    private void ShowInvalidInputWarning(string message, TextBox target)
    {
        DialogMessageHelper.ShowWarning(this, message, Title);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
