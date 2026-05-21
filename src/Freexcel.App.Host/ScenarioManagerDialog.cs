using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public sealed record ScenarioManagerItem(string Name);

public sealed class ScenarioManagerDialog : Window
{
    private readonly ListBox _scenarioList = new();
    private readonly TextBox _newNameBox = new();
    private readonly TextBox _changingCellsBox = new();
    private readonly TextBox _commentBox = new();

    public ScenarioManagerAction SelectedAction { get; private set; } = ScenarioManagerAction.Show;
    public string? SelectedScenarioName { get; private set; }
    public string? NewScenarioName { get; private set; }

    public ScenarioManagerDialog(Workbook workbook)
    {
        Title = "Scenario Manager";
        Width = 360;
        Height = 390;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

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

        left.Children.Add(new TextBlock { Text = "Scenarios:", Margin = new Thickness(0, 0, 0, 4) });
        _scenarioList.ItemsSource = BuildScenarioItems(workbook);
        _scenarioList.DisplayMemberPath = nameof(ScenarioManagerItem.Name);
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

        AddField(fields, row: 0, "Scenario name:", _newNameBox);
        _newNameBox.Text = workbook.Scenarios.Count == 0 ? "Scenario 1" : $"Scenario {workbook.Scenarios.Count + 1}";
        AddField(fields, row: 1, "Changing cells:", _changingCellsBox);
        AddField(fields, row: 2, "Comment:", _commentBox);

        var sideButtons = new StackPanel { Margin = new Thickness(10, 20, 0, 0) };
        Grid.SetColumn(sideButtons, 1);
        body.Children.Add(sideButtons);
        AddActionButton(sideButtons, "Add...", ScenarioManagerAction.Save);
        AddActionButton(sideButtons, "Edit...", ScenarioManagerAction.Save);
        AddActionButton(sideButtons, "Delete", ScenarioManagerAction.List, isEnabled: false);
        AddActionButton(sideButtons, "Merge...", ScenarioManagerAction.List, isEnabled: false);
        AddActionButton(sideButtons, "Show", ScenarioManagerAction.Show);
        AddActionButton(sideButtons, "Summary...", ScenarioManagerAction.Report);

        var closeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetRow(closeRow, 1);
        root.Children.Add(closeRow);
        closeRow.Children.Add(new Button { Content = "Close", Width = 72, IsCancel = true });

        Content = root;
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
        var text = new TextBlock
        {
            Text = label,
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

    private void AddActionButton(Panel panel, string label, ScenarioManagerAction action, bool isEnabled = true)
    {
        var button = new Button { Content = label, Width = 82, Margin = new Thickness(0, 0, 0, 6), IsEnabled = isEnabled };
        button.Click += (_, _) => Accept(action);
        panel.Children.Add(button);
    }

    private void Accept(ScenarioManagerAction action)
    {
        SelectedAction = action;
        SelectedScenarioName = (_scenarioList.SelectedItem as ScenarioManagerItem)?.Name;
        NewScenarioName = _newNameBox.Text;
        DialogResult = true;
    }
}
