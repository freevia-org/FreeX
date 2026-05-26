using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ScenarioManagerItem(
    string Name,
    IReadOnlyList<ScenarioCellValue> ChangingCells,
    string? Comment,
    string ChangingCellsText,
    bool Hidden,
    bool Locked);

public sealed class ScenarioManagerDialog : Window
{
    private readonly ListBox _scenarioList = new();
    private readonly TextBox _newNameBox = new();
    private readonly TextBox _changingCellsBox = new();
    private readonly TextBox _commentBox = new();
    private readonly CheckBox _lockedBox = new() { Content = "_Prevent changes", Margin = new Thickness(0, 0, 0, 6) };
    private readonly CheckBox _hiddenBox = new() { Content = "_Hide", Margin = new Thickness(0, 0, 0, 8) };
    private readonly string _defaultScenarioName;
    private readonly SheetId? _currentSheetId;
    private readonly Func<string, SheetId?>? _resolveSheetIdByName;
    private Button? _editButton;
    private Button? _deleteButton;
    private Button? _showButton;

    public ScenarioManagerAction SelectedAction { get; private set; } = ScenarioManagerAction.Show;
    public string? SelectedScenarioName { get; private set; }
    public string? NewScenarioName { get; private set; }
    public string? ChangingCellsText { get; private set; }
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
        Height = 390;
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
        _scenarioList.ItemsSource = BuildScenarioItems(workbook);
        _scenarioList.DisplayMemberPath = nameof(ScenarioManagerItem.Name);
        _scenarioList.SelectionChanged += (_, _) => UpdateSelectionState();
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
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.Content = fields;

        AddField(fields, row: 0, "Scenario _name:", _newNameBox);
        _newNameBox.Text = _defaultScenarioName;
        AddField(fields, row: 1, "Changing _cells:", _changingCellsBox);
        AddField(fields, row: 2, "_Comment:", _commentBox);
        AddCheckBox(fields, row: 3, _lockedBox);
        AddCheckBox(fields, row: 4, _hiddenBox);

        var sideButtons = new StackPanel { Margin = new Thickness(10, 20, 0, 0) };
        Grid.SetColumn(sideButtons, 1);
        body.Children.Add(sideButtons);
        AddActionButton(sideButtons, "_Add...", ScenarioManagerAction.Add);
        _editButton = AddActionButton(sideButtons, "_Edit...", ScenarioManagerAction.Edit, isEnabled: false);
        _deleteButton = AddActionButton(sideButtons, "_Delete", ScenarioManagerAction.Delete, isEnabled: false);
        AddActionButton(sideButtons, "_List...", ScenarioManagerAction.List);
        _showButton = AddActionButton(sideButtons, "_Show", ScenarioManagerAction.Show, isEnabled: _scenarioList.SelectedItem is not null);
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

    public static IReadOnlyList<ScenarioManagerItem> BuildScenarioItems(Workbook workbook) =>
        workbook.Scenarios.Select(scenario => new ScenarioManagerItem(
            scenario.Name,
            scenario.ChangingCells,
            scenario.Comment,
            FormatScenarioChangingCells(workbook, scenario),
            scenario.Hidden,
            scenario.Locked)).ToList();

    public static bool TryParseAction(string text, out ScenarioManagerAction action)
    {
        if (ScenarioManagerPlanner.TryParseAction(text, out var plannedAction) && plannedAction is { } parsed)
        {
            action = parsed;
            return true;
        }

        action = default;
        return false;
    }

    public static bool RequiresScenarioName(ScenarioManagerAction action) =>
        action is ScenarioManagerAction.Add or ScenarioManagerAction.Edit or ScenarioManagerAction.Save;

    public static bool TryValidateScenarioName(string? name, out string? error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Enter a scenario name.";
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryValidateChangingCells(
        string? changingCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(changingCellsText) ||
            currentSheetId is null ||
            resolveSheetIdByName is null)
        {
            error = null;
            return true;
        }

        if (WorkbookRangeTextCodec.TryParse(currentSheetId.Value, changingCellsText, resolveSheetIdByName, out _))
        {
            error = null;
            return true;
        }

        error = "Enter a valid changing cells reference.";
        return false;
    }

    public static string FormatScenarioChangingCells(Workbook workbook, WorkbookScenario scenario)
    {
        if (scenario.ChangingCells.Count == 0)
            return "";

        var sheetId = scenario.ChangingCells[0].Address.Sheet;
        if (scenario.ChangingCells.Any(cell => cell.Address.Sheet != sheetId))
            return "";

        var range = new GridRange(
            scenario.ChangingCells.Min(cell => cell.Address),
            scenario.ChangingCells.Max(cell => cell.Address));
        return WorkbookRangeTextCodec.Format(range, sheetId, id => workbook.GetSheet(id)?.Name);
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

    private Button AddActionButton(Panel panel, string label, ScenarioManagerAction action, bool isEnabled = true)
    {
        var button = new Button { Content = label, Width = 82, Margin = new Thickness(0, 0, 0, 6), IsEnabled = isEnabled };
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
        if (selected is not null)
        {
            _newNameBox.Text = selected.Name;
            _changingCellsBox.Text = selected.ChangingCellsText;
            _commentBox.Text = selected.Comment ?? "";
            _lockedBox.IsChecked = selected.Locked;
            _hiddenBox.IsChecked = selected.Hidden;
        }
        else if (string.IsNullOrWhiteSpace(_newNameBox.Text))
        {
            _newNameBox.Text = _defaultScenarioName;
            _changingCellsBox.Text = "";
            _commentBox.Text = "";
            _lockedBox.IsChecked = false;
            _hiddenBox.IsChecked = false;
        }

        var hasSelection = selected is not null;
        if (_editButton is not null) _editButton.IsEnabled = hasSelection;
        if (_deleteButton is not null) _deleteButton.IsEnabled = hasSelection;
        if (_showButton is not null) _showButton.IsEnabled = hasSelection;
    }

    private void Accept(ScenarioManagerAction action)
    {
        if (RequiresScenarioName(action) && !TryValidateScenarioName(_newNameBox.Text, out var error))
        {
            ShowInvalidInputWarning(error ?? "Enter scenario details.", _newNameBox);
            return;
        }

        if (RequiresScenarioName(action) &&
            !TryValidateChangingCells(_changingCellsBox.Text, _currentSheetId, _resolveSheetIdByName, out error))
        {
            ShowInvalidInputWarning(error ?? "Enter scenario details.", _changingCellsBox);
            return;
        }

        SelectedAction = action;
        SelectedScenarioName = (_scenarioList.SelectedItem as ScenarioManagerItem)?.Name;
        NewScenarioName = _newNameBox.Text;
        ChangingCellsText = _changingCellsBox.Text;
        CommentText = _commentBox.Text;
        ScenarioLocked = _lockedBox.IsChecked == true;
        ScenarioHidden = _hiddenBox.IsChecked == true;
        DialogResult = true;
    }

    private void ShowInvalidInputWarning(string message, TextBox target)
    {
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        target.Focus();
        target.SelectAll();
        Keyboard.Focus(target);
    }
}
