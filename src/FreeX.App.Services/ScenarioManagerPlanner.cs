using FreeX.Core.Model;
using FreeX.App.Presentation.ScenarioManager;

namespace FreeX.App.Services;

public enum ScenarioManagerOperation
{
    OpenManager,
    Show,
    Save,
    Delete,
    SummaryReport,
    Merge
}

public enum ScenarioManagerPlanStatus
{
    Ready,
    NoWorkbook,
    NoVisibleWorksheet,
    ScenarioNameRequired,
    ScenarioNotFound,
    ScenarioNameDuplicate,
    ChangingCellsRequired,
    ChangingCellsOutsideWorkbook,
    ProtectedChangingCells,
    NoScenarios,
    ResultCellsOutsideWorkbook
}

public sealed record ScenarioManagerScenarioChoice(
    string Name,
    int ChangingCellCount,
    string? Comment,
    bool Hidden,
    bool Locked,
    bool IsSelected);

public sealed record ScenarioManagerSaveRequest(
    string Name,
    IReadOnlyList<ScenarioCellValue> ChangingCells,
    string? ReplaceScenarioName = null,
    string? Comment = null,
    bool Hidden = false,
    bool Locked = false);

public sealed record ScenarioManagerPlan(
    ScenarioManagerOperation Operation,
    ScenarioManagerPlanStatus Status,
    string StatusText,
    IReadOnlyList<ScenarioManagerScenarioChoice> Scenarios,
    ScenarioManagerScenarioChoice? SelectedScenario,
    IReadOnlyList<CellAddress> AffectedCells,
    IReadOnlyList<CellAddress> ResultCells)
{
    public bool IsReady => Status == ScenarioManagerPlanStatus.Ready;
}

public static class ScenarioManagerPlanner
{
    public static ScenarioManagerAction GetDefaultAction(int scenarioCount) =>
        scenarioCount == 0 ? ScenarioManagerAction.Save : ScenarioManagerAction.Show;

    public static bool TryParseAction(string? input, out ScenarioManagerAction action)
    {
        switch ((input ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "save":
                action = ScenarioManagerAction.Save;
                return true;
            case "add":
                action = ScenarioManagerAction.Add;
                return true;
            case "edit":
                action = ScenarioManagerAction.Edit;
                return true;
            case "show":
            case "apply":
                action = ScenarioManagerAction.Show;
                return true;
            case "delete":
            case "remove":
                action = ScenarioManagerAction.Delete;
                return true;
            case "list":
            case "manager":
                action = ScenarioManagerAction.List;
                return true;
            case "report":
            case "summary":
                action = ScenarioManagerAction.Report;
                return true;
            case "merge":
                action = ScenarioManagerAction.Merge;
                return true;
            default:
                action = default;
                return false;
        }
    }

    public static string GetDefaultScenarioName(int scenarioCount) =>
        $"Scenario {scenarioCount + 1}";

    public static string GetDefaultScenarioName(IEnumerable<string?> existingNames)
    {
        ArgumentNullException.ThrowIfNull(existingNames);

        var names = existingNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .ToArray();
        var existing = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var index = names.Length + 1;
        while (!existing.Add($"Scenario {index}"))
            index++;

        return $"Scenario {index}";
    }

    public static string FormatSavedMessage(string name, int changingCellCount) =>
        $"Scenario '{NormalizeName(name)}' saved for {changingCellCount} changing cell(s).";

    public static string FormatScenarioList(IEnumerable<WorkbookScenario> scenarios) =>
        string.Join(Environment.NewLine, scenarios.Select(s => $"{s.Name}: {s.ChangingCells.Count} changing cell(s)"));

    public static ScenarioManagerPlan CreateDialogPlan(
        Workbook? workbook,
        string? selectedScenarioName = null)
    {
        if (CreateWorkbookUnavailablePlan(
                workbook,
                ScenarioManagerOperation.OpenManager,
                [],
                out var unavailablePlan))
            return unavailablePlan;

        var scenarios = BuildScenarioChoices(workbook!, selectedScenarioName, out var selectedScenario);
        return CreatePlan(
            ScenarioManagerOperation.OpenManager,
            ScenarioManagerPlanStatus.Ready,
            FormatDialogReadyStatus(scenarios.Count, selectedScenario),
            scenarios,
            selectedScenario,
            [],
            []);
    }

    public static ScenarioManagerPlan CreateShowPlan(
        Workbook? workbook,
        string? scenarioName) =>
        CreateScenarioActionPlan(workbook, ScenarioManagerOperation.Show, scenarioName);

    public static ScenarioManagerPlan CreateDeletePlan(
        Workbook? workbook,
        string? scenarioName) =>
        CreateScenarioActionPlan(workbook, ScenarioManagerOperation.Delete, scenarioName);

    public static ScenarioManagerPlan CreateSavePlan(
        Workbook? workbook,
        ScenarioManagerSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (CreateWorkbookUnavailablePlan(
                workbook,
                ScenarioManagerOperation.Save,
                [],
                out var unavailablePlan))
            return unavailablePlan;

        var scenarios = BuildScenarioChoices(workbook!, request.ReplaceScenarioName, out var selectedScenario);
        var name = NormalizeName(request.Name);
        if (string.IsNullOrWhiteSpace(name))
            return CreatePlan(
                ScenarioManagerOperation.Save,
                ScenarioManagerPlanStatus.ScenarioNameRequired,
                "Scenario name cannot be blank.",
                scenarios,
                selectedScenario,
                [],
                []);

        if (request.ChangingCells.Count == 0)
            return CreatePlan(
                ScenarioManagerOperation.Save,
                ScenarioManagerPlanStatus.ChangingCellsRequired,
                "Scenario must include at least one changing cell.",
                scenarios,
                selectedScenario,
                [],
                []);

        if (FindDuplicateScenario(workbook!, name, request.ReplaceScenarioName) is { } duplicate)
            return CreatePlan(
                ScenarioManagerOperation.Save,
                ScenarioManagerPlanStatus.ScenarioNameDuplicate,
                $"A scenario named '{duplicate.Name}' already exists.",
                BuildScenarioChoices(workbook!, duplicate.Name, out selectedScenario),
                selectedScenario,
                [],
                []);

        if (!AllScenarioCellsBelongToWorkbook(workbook!, request.ChangingCells.Select(cell => cell.Address)))
            return CreatePlan(
                ScenarioManagerOperation.Save,
                ScenarioManagerPlanStatus.ChangingCellsOutsideWorkbook,
                "Scenario changing cells must belong to this workbook.",
                scenarios,
                selectedScenario,
                [],
                []);

        if (HasProtectedScenarioCells(workbook!, request.ChangingCells.Select(cell => cell.Address)))
            return CreatePlan(
                ScenarioManagerOperation.Save,
                ScenarioManagerPlanStatus.ProtectedChangingCells,
                "Scenario changing cells are protected on at least one worksheet.",
                scenarios,
                selectedScenario,
                [],
                []);

        var affectedCells = NormalizeAddresses(request.ChangingCells.Select(cell => cell.Address));
        return CreatePlan(
            ScenarioManagerOperation.Save,
            ScenarioManagerPlanStatus.Ready,
            $"Ready to save scenario '{name}' with {affectedCells.Count} {Pluralize(affectedCells.Count, "changing cell")}.",
            scenarios,
            selectedScenario,
            affectedCells,
            []);
    }

    public static ScenarioManagerPlan CreateSummaryReportPlan(
        Workbook? workbook,
        IReadOnlyList<CellAddress>? resultCells = null)
    {
        var requestedResultCells = NormalizeAddresses(resultCells ?? []);
        if (CreateWorkbookUnavailablePlan(
                workbook,
                ScenarioManagerOperation.SummaryReport,
                requestedResultCells,
                out var unavailablePlan))
            return unavailablePlan;

        var scenarios = BuildScenarioChoices(workbook!, null, out var selectedScenario);
        if (workbook!.Scenarios.Count == 0)
            return CreatePlan(
                ScenarioManagerOperation.SummaryReport,
                ScenarioManagerPlanStatus.NoScenarios,
                "Scenario summary requires at least one saved scenario.",
                scenarios,
                selectedScenario,
                [],
                requestedResultCells);

        if (!AllScenarioCellsBelongToWorkbook(workbook!, requestedResultCells))
            return CreatePlan(
                ScenarioManagerOperation.SummaryReport,
                ScenarioManagerPlanStatus.ResultCellsOutsideWorkbook,
                "Scenario result cells must belong to this workbook.",
                scenarios,
                selectedScenario,
                [],
                requestedResultCells);

        var scenarioCells = NormalizeAddresses(workbook.Scenarios.SelectMany(scenario =>
            scenario.ChangingCells.Select(change => change.Address)));
        if (HasProtectedScenarioCells(workbook, scenarioCells))
            return CreatePlan(
                ScenarioManagerOperation.SummaryReport,
                ScenarioManagerPlanStatus.ProtectedChangingCells,
                "Scenario changing cells are protected on at least one worksheet.",
                scenarios,
                selectedScenario,
                scenarioCells,
                requestedResultCells);

        return CreatePlan(
            ScenarioManagerOperation.SummaryReport,
            ScenarioManagerPlanStatus.Ready,
            $"Ready to create a scenario summary for {workbook.Scenarios.Count} {Pluralize(workbook.Scenarios.Count, "scenario")}.",
            scenarios,
            selectedScenario,
            scenarioCells,
            requestedResultCells);
    }

    public static ScenarioManagerPlan CreateMergePlan(
        Workbook? workbook,
        IReadOnlyList<WorkbookScenario>? sourceScenarios)
    {
        var normalizedSource = sourceScenarios ?? [];

        if (CreateWorkbookUnavailablePlan(
                workbook,
                ScenarioManagerOperation.Merge,
                [],
                out var unavailablePlan))
            return unavailablePlan;

        var availableWorkbook = workbook!;
        var scenarios = BuildScenarioChoices(availableWorkbook, null, out var selectedScenario);

        if (normalizedSource.Count == 0)
            return CreatePlan(
                ScenarioManagerOperation.Merge,
                ScenarioManagerPlanStatus.NoScenarios,
                "The source sheet or workbook has no scenarios to merge.",
                scenarios,
                selectedScenario,
                [],
                []);

        var mergeCells = NormalizeAddresses(normalizedSource.SelectMany(
            scenario => scenario.ChangingCells.Select(cell => cell.Address)));

        if (!AllScenarioCellsBelongToWorkbook(availableWorkbook, mergeCells))
            return CreatePlan(
                ScenarioManagerOperation.Merge,
                ScenarioManagerPlanStatus.ChangingCellsOutsideWorkbook,
                "Scenario changing cells must belong to this workbook.",
                scenarios,
                selectedScenario,
                [],
                []);

        if (HasProtectedScenarioCells(availableWorkbook, mergeCells))
            return CreatePlan(
                ScenarioManagerOperation.Merge,
                ScenarioManagerPlanStatus.ProtectedChangingCells,
                "Scenario changing cells are protected on at least one worksheet.",
                scenarios,
                selectedScenario,
                mergeCells,
                []);

        return CreatePlan(
            ScenarioManagerOperation.Merge,
            ScenarioManagerPlanStatus.Ready,
            $"Ready to merge {normalizedSource.Count} {Pluralize(normalizedSource.Count, "scenario")} into this workbook.",
            scenarios,
            selectedScenario,
            mergeCells,
            []);
    }

    private static ScenarioManagerPlan CreateScenarioActionPlan(
        Workbook? workbook,
        ScenarioManagerOperation operation,
        string? scenarioName)
    {
        if (CreateWorkbookUnavailablePlan(workbook, operation, [], out var unavailablePlan))
            return unavailablePlan;

        var availableWorkbook = workbook!;
        var scenarios = BuildScenarioChoices(availableWorkbook, scenarioName, out var selectedScenario);
        var name = NormalizeName(scenarioName);
        if (string.IsNullOrWhiteSpace(name))
            return CreatePlan(
                operation,
                ScenarioManagerPlanStatus.ScenarioNameRequired,
                "Select a scenario before continuing.",
                scenarios,
                selectedScenario,
                [],
                []);

        var scenario = FindScenarioByName(availableWorkbook, name);
        if (scenario is null)
            return CreatePlan(
                operation,
                ScenarioManagerPlanStatus.ScenarioNotFound,
                $"Scenario '{name}' was not found.",
                scenarios,
                selectedScenario,
                [],
                []);

        var affectedCells = NormalizeAddresses(scenario.ChangingCells.Select(cell => cell.Address));
        if (HasProtectedScenarioCells(availableWorkbook, affectedCells))
            return CreatePlan(
                operation,
                ScenarioManagerPlanStatus.ProtectedChangingCells,
                "Scenario changing cells are protected on at least one worksheet.",
                scenarios,
                selectedScenario,
                affectedCells,
                []);

        return CreatePlan(
            operation,
            ScenarioManagerPlanStatus.Ready,
            FormatScenarioActionReadyStatus(operation, scenario.Name, affectedCells.Count),
            scenarios,
            selectedScenario,
            affectedCells,
            []);
    }

    private static bool CreateWorkbookUnavailablePlan(
        Workbook? workbook,
        ScenarioManagerOperation operation,
        IReadOnlyList<CellAddress> resultCells,
        out ScenarioManagerPlan plan)
    {
        if (workbook is null)
        {
            plan = CreatePlan(
                operation,
                ScenarioManagerPlanStatus.NoWorkbook,
                "Open a workbook before using Scenario Manager.",
                [],
                null,
                [],
                resultCells);
            return true;
        }

        if (!workbook.Sheets.Any(sheet => !sheet.IsHidden))
        {
            plan = CreatePlan(
                operation,
                ScenarioManagerPlanStatus.NoVisibleWorksheet,
                "Scenario Manager requires at least one visible worksheet.",
                BuildScenarioChoices(workbook, null, out var selectedScenario),
                selectedScenario,
                [],
                resultCells);
            return true;
        }

        plan = null!;
        return false;
    }

    private static IReadOnlyList<ScenarioManagerScenarioChoice> BuildScenarioChoices(
        Workbook workbook,
        string? selectedScenarioName,
        out ScenarioManagerScenarioChoice? selectedScenario)
    {
        var selectedIndex = FindSelectedScenarioIndex(workbook, selectedScenarioName);
        var choices = new List<ScenarioManagerScenarioChoice>(workbook.Scenarios.Count);
        for (var index = 0; index < workbook.Scenarios.Count; index++)
        {
            var scenario = workbook.Scenarios[index];
            var choice = new ScenarioManagerScenarioChoice(
                scenario.Name,
                scenario.ChangingCells.Count,
                scenario.Comment,
                scenario.Hidden,
                scenario.Locked,
                index == selectedIndex);
            choices.Add(choice);
        }

        selectedScenario = selectedIndex >= 0 ? choices[selectedIndex] : null;
        return choices;
    }

    private static WorkbookScenario? FindDuplicateScenario(
        Workbook workbook,
        string name,
        string? replaceScenarioName)
    {
        var replaceName = NormalizeName(replaceScenarioName);
        foreach (var scenario in workbook.Scenarios)
        {
            if (IsDuplicateScenarioName(scenario, name, replaceName))
                return scenario;
        }

        return null;
    }

    private static WorkbookScenario? FindScenarioByName(Workbook workbook, string name)
    {
        foreach (var scenario in workbook.Scenarios)
        {
            if (ScenarioNameEquals(scenario, name))
                return scenario;
        }

        return null;
    }

    private static int FindSelectedScenarioIndex(Workbook workbook, string? selectedScenarioName)
    {
        var selectedName = NormalizeName(selectedScenarioName);
        var selectedIndex = !string.IsNullOrWhiteSpace(selectedName)
            ? FindScenarioIndexByName(workbook, selectedName)
            : -1;

        return selectedIndex >= 0 || workbook.Scenarios.Count == 0
            ? selectedIndex
            : 0;
    }

    private static int FindScenarioIndexByName(Workbook workbook, string name)
    {
        for (var index = 0; index < workbook.Scenarios.Count; index++)
        {
            if (ScenarioNameEquals(workbook.Scenarios[index], name))
                return index;
        }

        return -1;
    }

    private static bool IsDuplicateScenarioName(
        WorkbookScenario scenario,
        string name,
        string replaceName) =>
        ScenarioNameEquals(scenario, name) &&
        (string.IsNullOrWhiteSpace(replaceName) || !ScenarioNameEquals(scenario, replaceName));

    private static bool ScenarioNameEquals(WorkbookScenario scenario, string name) =>
        string.Equals(scenario.Name, name, StringComparison.OrdinalIgnoreCase);

    private static bool AllScenarioCellsBelongToWorkbook(
        Workbook workbook,
        IEnumerable<CellAddress> addresses)
    {
        foreach (var address in addresses)
        {
            if (workbook.GetSheet(address.Sheet) is null)
                return false;
        }

        return true;
    }

    private static bool HasProtectedScenarioCells(
        Workbook workbook,
        IEnumerable<CellAddress> addresses)
    {
        var checkedSheets = new HashSet<SheetId>();
        foreach (var address in addresses)
        {
            if (!checkedSheets.Add(address.Sheet))
                continue;

            var sheet = workbook.GetSheet(address.Sheet);
            if (sheet is null)
                return false;
            if (sheet.IsProtected &&
                !sheet.ProtectionPermissions.Contains(SheetProtectionPermission.EditScenarios))
                return true;
        }

        return false;
    }

    private static IReadOnlyList<CellAddress> NormalizeAddresses(IEnumerable<CellAddress> addresses)
    {
        var normalized = new List<CellAddress>();
        var seen = new HashSet<CellAddress>();
        foreach (var address in addresses)
        {
            if (seen.Add(address))
                normalized.Add(address);
        }

        return normalized;
    }

    private static ScenarioManagerPlan CreatePlan(
        ScenarioManagerOperation operation,
        ScenarioManagerPlanStatus status,
        string statusText,
        IReadOnlyList<ScenarioManagerScenarioChoice> scenarios,
        ScenarioManagerScenarioChoice? selectedScenario,
        IReadOnlyList<CellAddress> affectedCells,
        IReadOnlyList<CellAddress> resultCells) =>
        new(operation, status, statusText, scenarios, selectedScenario, affectedCells, resultCells);

    private static string FormatDialogReadyStatus(
        int scenarioCount,
        ScenarioManagerScenarioChoice? selectedScenario) =>
        selectedScenario is null
            ? "Ready to manage scenarios. No scenarios are saved yet."
            : $"Ready to manage {scenarioCount} {Pluralize(scenarioCount, "scenario")}; '{selectedScenario.Name}' is selected.";

    private static string FormatScenarioActionReadyStatus(
        ScenarioManagerOperation operation,
        string scenarioName,
        int affectedCellCount)
    {
        var verb = operation switch
        {
            ScenarioManagerOperation.Delete => "delete",
            _ => "show"
        };

        return $"Ready to {verb} scenario '{scenarioName}' affecting {affectedCellCount} {Pluralize(affectedCellCount, "cell")}.";
    }

    private static string NormalizeName(string? name) => name?.Trim() ?? "";

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : $"{singular}s";
}
