using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public enum ScenarioManagerAction
{
    Add,
    Edit,
    Save,
    Show,
    Delete,
    List,
    Report
}

public static class ScenarioManagerPlanner
{
    public static string GetDefaultAction(int scenarioCount) =>
        scenarioCount == 0 ? "save" : "show";

    public static bool TryParseAction(string input, out ScenarioManagerAction? action)
    {
        action = input.Trim().ToLowerInvariant() switch
        {
            "save" => ScenarioManagerAction.Save,
            "add" => ScenarioManagerAction.Add,
            "edit" => ScenarioManagerAction.Edit,
            "show" or "apply" => ScenarioManagerAction.Show,
            "delete" or "remove" => ScenarioManagerAction.Delete,
            "list" or "manager" => ScenarioManagerAction.List,
            "report" or "summary" => ScenarioManagerAction.Report,
            _ => null
        };

        return action.HasValue;
    }

    public static string GetDefaultScenarioName(int scenarioCount) =>
        $"Scenario {scenarioCount + 1}";

    public static string FormatSavedMessage(string name, int changingCellCount) =>
        $"Scenario '{name.Trim()}' saved for {changingCellCount} changing cell(s).";

    public static string FormatScenarioList(IEnumerable<WorkbookScenario> scenarios) =>
        string.Join(Environment.NewLine, scenarios.Select(s => $"{s.Name}: {s.ChangingCells.Count} changing cell(s)"));
}
