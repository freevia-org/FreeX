using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum ScenarioManagerAction
{
    Save,
    Show,
    List,
    Report
}

public sealed record ScenarioManagerItem(string Name);

public sealed class ScenarioManagerDialog : Window
{
    private readonly ComboBox _scenarioBox = new();
    private readonly TextBox _newNameBox = new();

    public ScenarioManagerAction SelectedAction { get; private set; } = ScenarioManagerAction.Show;
    public string? SelectedScenarioName { get; private set; }
    public string? NewScenarioName { get; private set; }

    public ScenarioManagerDialog(Workbook workbook)
    {
        Title = "Scenario Manager";
        Width = 360;
        Height = 260;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new StackPanel { Margin = new Thickness(12) };
        root.Children.Add(new TextBlock { Text = "Scenarios:", Margin = new Thickness(0, 0, 0, 4) });
        _scenarioBox.ItemsSource = BuildScenarioItems(workbook);
        _scenarioBox.DisplayMemberPath = nameof(ScenarioManagerItem.Name);
        _scenarioBox.SelectedIndex = _scenarioBox.Items.Count > 0 ? 0 : -1;
        root.Children.Add(_scenarioBox);

        root.Children.Add(new TextBlock { Text = "New scenario name:", Margin = new Thickness(0, 12, 0, 4) });
        _newNameBox.Text = workbook.Scenarios.Count == 0 ? "Scenario 1" : $"Scenario {workbook.Scenarios.Count + 1}";
        root.Children.Add(_newNameBox);

        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        root.Children.Add(buttons);
        AddActionButton(buttons, "Add", ScenarioManagerAction.Save);
        AddActionButton(buttons, "Show", ScenarioManagerAction.Show);
        AddActionButton(buttons, "List", ScenarioManagerAction.List);
        AddActionButton(buttons, "Summary", ScenarioManagerAction.Report);
        buttons.Children.Add(new Button { Content = "Close", Width = 72, Margin = new Thickness(4), IsCancel = true });

        Content = root;
    }

    public static IReadOnlyList<ScenarioManagerItem> BuildScenarioItems(Workbook workbook) =>
        workbook.Scenarios.Select(scenario => new ScenarioManagerItem(scenario.Name)).ToList();

    public static bool TryParseAction(string text, out ScenarioManagerAction action)
    {
        action = text.Trim().ToLowerInvariant() switch
        {
            "save" or "add" => ScenarioManagerAction.Save,
            "show" or "apply" => ScenarioManagerAction.Show,
            "list" or "manager" => ScenarioManagerAction.List,
            "report" or "summary" => ScenarioManagerAction.Report,
            _ => default
        };

        return text.Trim().ToLowerInvariant() is "save" or "add" or "show" or "apply" or "list" or "manager" or "report" or "summary";
    }

    private void AddActionButton(Panel panel, string label, ScenarioManagerAction action)
    {
        var button = new Button { Content = label, Width = 72, Margin = new Thickness(4) };
        button.Click += (_, _) => Accept(action);
        panel.Children.Add(button);
    }

    private void Accept(ScenarioManagerAction action)
    {
        SelectedAction = action;
        SelectedScenarioName = (_scenarioBox.SelectedItem as ScenarioManagerItem)?.Name;
        NewScenarioName = _newNameBox.Text;
        DialogResult = true;
    }
}
