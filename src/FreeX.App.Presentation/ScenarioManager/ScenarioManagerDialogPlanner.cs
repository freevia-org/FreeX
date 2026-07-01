using FreeX.Core.Model;

namespace FreeX.App.Presentation.ScenarioManager;

public enum ScenarioManagerDialogAction
{
    Add,
    Edit,
    Save,
    Show,
    Delete,
    List,
    Report
}

public enum ScenarioManagerDialogValidationField
{
    ScenarioName,
    ChangingCells,
    ResultCells
}

public enum ScenarioManagerDialogValidationError
{
    None,
    EnterScenarioName,
    EnterValidChangingCellsReference,
    EnterValidResultCellsReference
}

public readonly record struct ScenarioManagerDialogValidation(
    bool IsValid,
    ScenarioManagerDialogValidationError Error)
{
    public static ScenarioManagerDialogValidation Ok { get; } =
        new(true, ScenarioManagerDialogValidationError.None);

    public static ScenarioManagerDialogValidation Fail(ScenarioManagerDialogValidationError error) =>
        new(false, error);
}

public sealed record ScenarioManagerDialogItem(
    string Name,
    IReadOnlyList<ScenarioCellValue> ChangingCells,
    string? Comment,
    string ChangingCellsText,
    bool Hidden,
    bool Locked);

public sealed record ScenarioManagerDialogSelectionFields(
    string ScenarioName,
    string ChangingCellsText,
    string ResultCellsText,
    string CommentText,
    bool Locked,
    bool Hidden);

public sealed record ScenarioManagerDialogAcceptResult(
    ScenarioManagerDialogAction Action,
    string? SelectedScenarioName,
    string NewScenarioName,
    string ChangingCellsText,
    string ResultCellsText,
    string CommentText,
    bool Locked,
    bool Hidden);

public sealed record ScenarioManagerDialogValidationFailure(
    ScenarioManagerDialogValidationError Error,
    ScenarioManagerDialogValidationField Field);

public static class ScenarioManagerDialogPlanner
{
    public static IReadOnlyList<ScenarioManagerDialogItem> BuildItems(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        return workbook.Scenarios.Select(scenario => new ScenarioManagerDialogItem(
            scenario.Name,
            scenario.ChangingCells,
            scenario.Comment,
            FormatChangingCells(workbook, scenario),
            scenario.Hidden,
            scenario.Locked)).ToList();
    }

    public static bool RequiresScenarioName(ScenarioManagerDialogAction action) =>
        action is ScenarioManagerDialogAction.Add
            or ScenarioManagerDialogAction.Edit
            or ScenarioManagerDialogAction.Save;

    public static ScenarioManagerDialogValidation ValidateScenarioName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? ScenarioManagerDialogValidation.Fail(ScenarioManagerDialogValidationError.EnterScenarioName)
            : ScenarioManagerDialogValidation.Ok;

    public static ScenarioManagerDialogValidation ValidateChangingCells(
        string? changingCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName)
    {
        if (string.IsNullOrWhiteSpace(changingCellsText) ||
            currentSheetId is null ||
            resolveSheetIdByName is null)
        {
            return ScenarioManagerDialogValidation.Ok;
        }

        return WorkbookRangeTextCodec.TryParse(currentSheetId.Value, changingCellsText, resolveSheetIdByName, out _)
            ? ScenarioManagerDialogValidation.Ok
            : ScenarioManagerDialogValidation.Fail(ScenarioManagerDialogValidationError.EnterValidChangingCellsReference);
    }

    public static ScenarioManagerDialogValidation ValidateResultCells(
        string? resultCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName)
    {
        if (string.IsNullOrWhiteSpace(resultCellsText))
            return ScenarioManagerDialogValidation.Ok;

        if (currentSheetId is not null &&
            resolveSheetIdByName is not null &&
            WorkbookRangeTextCodec.TryParseMany(currentSheetId.Value, resultCellsText, resolveSheetIdByName, out _))
        {
            return ScenarioManagerDialogValidation.Ok;
        }

        return ScenarioManagerDialogValidation.Fail(ScenarioManagerDialogValidationError.EnterValidResultCellsReference);
    }

    public static string FormatChangingCells(Workbook workbook, WorkbookScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(scenario);

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

    public static ScenarioManagerDialogSelectionFields? ProjectSelectionFields(
        ScenarioManagerDialogItem? selected,
        string currentScenarioNameText,
        string defaultScenarioName)
    {
        if (selected is not null)
        {
            return new ScenarioManagerDialogSelectionFields(
                selected.Name,
                selected.ChangingCellsText,
                ResultCellsText: "",
                selected.Comment ?? "",
                selected.Locked,
                selected.Hidden);
        }

        if (!string.IsNullOrWhiteSpace(currentScenarioNameText))
            return null;

        return new ScenarioManagerDialogSelectionFields(
            defaultScenarioName,
            ChangingCellsText: "",
            ResultCellsText: "",
            CommentText: "",
            Locked: true,
            Hidden: false);
    }

    public static ScenarioManagerDialogValidationFailure? ValidateAcceptRequest(
        ScenarioManagerDialogAction action,
        string? scenarioName,
        string? changingCellsText,
        string? resultCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName)
    {
        if (RequiresScenarioName(action) &&
            !ValidateScenarioName(scenarioName).IsValid)
        {
            return new ScenarioManagerDialogValidationFailure(
                ScenarioManagerDialogValidationError.EnterScenarioName,
                ScenarioManagerDialogValidationField.ScenarioName);
        }

        var changingCellsValidation = ValidateChangingCells(
            changingCellsText,
            currentSheetId,
            resolveSheetIdByName);
        if (RequiresScenarioName(action) && !changingCellsValidation.IsValid)
        {
            return new ScenarioManagerDialogValidationFailure(
                changingCellsValidation.Error,
                ScenarioManagerDialogValidationField.ChangingCells);
        }

        var resultCellsValidation = ValidateResultCells(
            resultCellsText,
            currentSheetId,
            resolveSheetIdByName);
        if (action is ScenarioManagerDialogAction.Report && !resultCellsValidation.IsValid)
        {
            return new ScenarioManagerDialogValidationFailure(
                resultCellsValidation.Error,
                ScenarioManagerDialogValidationField.ResultCells);
        }

        return null;
    }

    public static ScenarioManagerDialogAcceptResult ProjectAcceptResult(
        ScenarioManagerDialogAction action,
        ScenarioManagerDialogItem? selected,
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
