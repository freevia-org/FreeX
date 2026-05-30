using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record ScenarioManagerSelectionFields(
    string ScenarioName,
    string ChangingCellsText,
    string ResultCellsText,
    string CommentText,
    bool Locked,
    bool Hidden);

internal sealed record ScenarioManagerAcceptResult(
    ScenarioManagerAction Action,
    string? SelectedScenarioName,
    string NewScenarioName,
    string ChangingCellsText,
    string ResultCellsText,
    string CommentText,
    bool Locked,
    bool Hidden);

internal enum ScenarioManagerValidationField
{
    ScenarioName,
    ChangingCells,
    ResultCells
}

internal sealed record ScenarioManagerValidationFailure(string Message, ScenarioManagerValidationField Field);

public sealed partial class ScenarioManagerDialog
{
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
            error = UiText.Get("ScenarioManager_EnterScenarioName");
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

        error = UiText.Get("ScenarioManager_EnterValidChangingCellsReference");
        return false;
    }

    public static bool TryValidateResultCells(
        string? resultCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(resultCellsText))
        {
            error = null;
            return true;
        }

        if (currentSheetId is not null &&
            resolveSheetIdByName is not null &&
            WorkbookRangeTextCodec.TryParseMany(currentSheetId.Value, resultCellsText, resolveSheetIdByName, out _))
        {
            error = null;
            return true;
        }

        error = UiText.Get("ScenarioManager_EnterValidResultCellsReference");
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

    internal static ScenarioManagerSelectionFields? ProjectSelectionFields(
        ScenarioManagerItem? selected,
        string currentScenarioNameText,
        string defaultScenarioName)
    {
        if (selected is not null)
        {
            return new ScenarioManagerSelectionFields(
                selected.Name,
                selected.ChangingCellsText,
                ResultCellsText: "",
                selected.Comment ?? "",
                selected.Locked,
                selected.Hidden);
        }

        if (!string.IsNullOrWhiteSpace(currentScenarioNameText))
            return null;

        return new ScenarioManagerSelectionFields(
            defaultScenarioName,
            ChangingCellsText: "",
            ResultCellsText: "",
            CommentText: "",
            Locked: false,
            Hidden: false);
    }

    internal static ScenarioManagerValidationFailure? ValidateAcceptRequest(
        ScenarioManagerAction action,
        string? scenarioName,
        string? changingCellsText,
        string? resultCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName)
    {
        if (RequiresScenarioName(action) && !TryValidateScenarioName(scenarioName, out var error))
        {
            return new ScenarioManagerValidationFailure(error ?? UiText.Get("ScenarioManager_EnterScenarioDetails"), ScenarioManagerValidationField.ScenarioName);
        }

        if (RequiresScenarioName(action) &&
            !TryValidateChangingCells(changingCellsText, currentSheetId, resolveSheetIdByName, out error))
        {
            return new ScenarioManagerValidationFailure(error ?? UiText.Get("ScenarioManager_EnterScenarioDetails"), ScenarioManagerValidationField.ChangingCells);
        }

        if (action is ScenarioManagerAction.Report &&
            !TryValidateResultCells(resultCellsText, currentSheetId, resolveSheetIdByName, out error))
        {
            return new ScenarioManagerValidationFailure(error ?? UiText.Get("ScenarioManager_EnterScenarioResultCells"), ScenarioManagerValidationField.ResultCells);
        }

        return null;
    }

    internal static ScenarioManagerAcceptResult ProjectAcceptResult(
        ScenarioManagerAction action,
        ScenarioManagerItem? selected,
        string newScenarioName,
        string changingCellsText,
        string resultCellsText,
        string commentText,
        bool locked,
        bool hidden) =>
        new(
            action,
            selected?.Name,
            newScenarioName,
            changingCellsText,
            resultCellsText,
            commentText,
            locked,
            hidden);
}
