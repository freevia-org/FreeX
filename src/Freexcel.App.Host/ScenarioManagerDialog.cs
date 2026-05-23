using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ScenarioManagerItem(string Name);

public sealed class ScenarioManagerDialog : Window
{
    private readonly ListBox _scenarioList = new();
    private readonly TextBox _newNameBox = new();
    private readonly TextBox _changingCellsBox = new();
    private readonly TextBox _commentBox = new();
    private readonly string _defaultScenarioName;
    private Button? _editButton;
    private Button? _deleteButton;
    private Button? _showButton;

    public ScenarioManagerAction SelectedAction { get; private set; } = ScenarioManagerAction.Show;
    public string? SelectedScenarioName { get; private set; }
    public string? NewScenarioName { get; private set; }
    public string? ChangingCellsText { get; private set; }
    public string? CommentText { get; private set; }

    public ScenarioManagerDialog(Workbook workbook)
    {
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
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.Content = fields;

        AddField(fields, row: 0, "Scenario _name:", _newNameBox);
        _newNameBox.Text = _defaultScenarioName;
        AddField(fields, row: 1, "Changing _cells:", _changingCellsBox);
        AddField(fields, row: 2, "_Comment:", _commentBox);

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
        workbook.Scenarios.Select(scenario => new ScenarioManagerItem(scenario.Name)).ToList();

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
        }
        else if (string.IsNullOrWhiteSpace(_newNameBox.Text))
        {
            _newNameBox.Text = _defaultScenarioName;
        }

        var hasSelection = selected is not null;
        if (_editButton is not null) _editButton.IsEnabled = hasSelection;
        if (_deleteButton is not null) _deleteButton.IsEnabled = hasSelection;
        if (_showButton is not null) _showButton.IsEnabled = hasSelection;
    }

    private void Accept(ScenarioManagerAction action)
    {
        SelectedAction = action;
        SelectedScenarioName = (_scenarioList.SelectedItem as ScenarioManagerItem)?.Name;
        NewScenarioName = _newNameBox.Text;
        ChangingCellsText = _changingCellsBox.Text;
        CommentText = _commentBox.Text;
        DialogResult = true;
    }
}
