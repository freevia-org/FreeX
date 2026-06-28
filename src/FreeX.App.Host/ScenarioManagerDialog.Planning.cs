using FreeX.App.Presentation.ScenarioManager;
using FreeX.App.Services;
using FreeX.Core.Model;

using SharedScenarioManagerDialogPlanner = FreeX.App.Presentation.ScenarioManager.ScenarioManagerDialogPlanner;

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
        SharedScenarioManagerDialogPlanner.BuildItems(workbook).Select(ToHostItem).ToList();

    public static bool TryParseAction(string text, out ScenarioManagerAction action)
    {
        return ScenarioManagerPlanner.TryParseAction(text, out action);
    }

    public static bool RequiresScenarioName(ScenarioManagerAction action) =>
        SharedScenarioManagerDialogPlanner.RequiresScenarioName(ToDialogAction(action));

    public static bool TryValidateScenarioName(string? name, out string? error)
    {
        var validation = SharedScenarioManagerDialogPlanner.ValidateScenarioName(name);
        error = validation.IsValid ? null : LocalizeValidationError(validation.Error);
        return validation.IsValid;
    }

    public static bool TryValidateChangingCells(
        string? changingCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName,
        out string? error)
    {
        var validation = SharedScenarioManagerDialogPlanner.ValidateChangingCells(
            changingCellsText,
            currentSheetId,
            resolveSheetIdByName);
        error = validation.IsValid ? null : LocalizeValidationError(validation.Error);
        return validation.IsValid;
    }

    public static bool TryValidateResultCells(
        string? resultCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName,
        out string? error)
    {
        var validation = SharedScenarioManagerDialogPlanner.ValidateResultCells(
            resultCellsText,
            currentSheetId,
            resolveSheetIdByName);
        error = validation.IsValid ? null : LocalizeValidationError(validation.Error);
        return validation.IsValid;
    }

    public static string FormatScenarioChangingCells(Workbook workbook, WorkbookScenario scenario) =>
        SharedScenarioManagerDialogPlanner.FormatChangingCells(workbook, scenario);

    internal static ScenarioManagerSelectionFields? ProjectSelectionFields(
        ScenarioManagerItem? selected,
        string currentScenarioNameText,
        string defaultScenarioName)
    {
        var fields = SharedScenarioManagerDialogPlanner.ProjectSelectionFields(
            ToPlannerItem(selected),
            currentScenarioNameText,
            defaultScenarioName);
        return fields is null ? null : ToHostSelectionFields(fields);
    }

    internal static ScenarioManagerValidationFailure? ValidateAcceptRequest(
        ScenarioManagerAction action,
        string? scenarioName,
        string? changingCellsText,
        string? resultCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName)
    {
        var failure = SharedScenarioManagerDialogPlanner.ValidateAcceptRequest(
            ToDialogAction(action),
            scenarioName,
            changingCellsText,
            resultCellsText,
            currentSheetId,
            resolveSheetIdByName);
        if (failure is null)
            return null;

        return new ScenarioManagerValidationFailure(
            LocalizeValidationError(failure.Error) ?? GetValidationFallbackText(failure.Field),
            ToHostValidationField(failure.Field));
    }

    internal static ScenarioManagerAcceptResult ProjectAcceptResult(
        ScenarioManagerAction action,
        ScenarioManagerItem? selected,
        string newScenarioName,
        string changingCellsText,
        string resultCellsText,
        string commentText,
        bool locked,
        bool hidden)
    {
        var result = SharedScenarioManagerDialogPlanner.ProjectAcceptResult(
            ToDialogAction(action),
            ToPlannerItem(selected),
            newScenarioName,
            changingCellsText,
            resultCellsText,
            commentText,
            locked,
            hidden);
        return ToHostAcceptResult(result);
    }

    private static ScenarioManagerItem ToHostItem(ScenarioManagerDialogItem item) =>
        new(
            item.Name,
            item.ChangingCells,
            item.Comment,
            item.ChangingCellsText,
            item.Hidden,
            item.Locked);

    private static ScenarioManagerDialogItem? ToPlannerItem(ScenarioManagerItem? item) =>
        item is null
            ? null
            : new ScenarioManagerDialogItem(
                item.Name,
                item.ChangingCells,
                item.Comment,
                item.ChangingCellsText,
                item.Hidden,
                item.Locked);

    private static ScenarioManagerSelectionFields ToHostSelectionFields(
        ScenarioManagerDialogSelectionFields fields) =>
        new(
            fields.ScenarioName,
            fields.ChangingCellsText,
            fields.ResultCellsText,
            fields.CommentText,
            fields.Locked,
            fields.Hidden);

    private static ScenarioManagerAcceptResult ToHostAcceptResult(
        ScenarioManagerDialogAcceptResult result) =>
        new(
            ToServiceAction(result.Action),
            result.SelectedScenarioName,
            result.NewScenarioName,
            result.ChangingCellsText,
            result.ResultCellsText,
            result.CommentText,
            result.Locked,
            result.Hidden);

    private static ScenarioManagerValidationField ToHostValidationField(
        ScenarioManagerDialogValidationField field) =>
        field switch
        {
            ScenarioManagerDialogValidationField.ScenarioName => ScenarioManagerValidationField.ScenarioName,
            ScenarioManagerDialogValidationField.ChangingCells => ScenarioManagerValidationField.ChangingCells,
            ScenarioManagerDialogValidationField.ResultCells => ScenarioManagerValidationField.ResultCells,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };

    private static ScenarioManagerDialogAction ToDialogAction(ScenarioManagerAction action) =>
        action switch
        {
            ScenarioManagerAction.Add => ScenarioManagerDialogAction.Add,
            ScenarioManagerAction.Edit => ScenarioManagerDialogAction.Edit,
            ScenarioManagerAction.Save => ScenarioManagerDialogAction.Save,
            ScenarioManagerAction.Show => ScenarioManagerDialogAction.Show,
            ScenarioManagerAction.Delete => ScenarioManagerDialogAction.Delete,
            ScenarioManagerAction.List => ScenarioManagerDialogAction.List,
            ScenarioManagerAction.Report => ScenarioManagerDialogAction.Report,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

    private static ScenarioManagerAction ToServiceAction(ScenarioManagerDialogAction action) =>
        action switch
        {
            ScenarioManagerDialogAction.Add => ScenarioManagerAction.Add,
            ScenarioManagerDialogAction.Edit => ScenarioManagerAction.Edit,
            ScenarioManagerDialogAction.Save => ScenarioManagerAction.Save,
            ScenarioManagerDialogAction.Show => ScenarioManagerAction.Show,
            ScenarioManagerDialogAction.Delete => ScenarioManagerAction.Delete,
            ScenarioManagerDialogAction.List => ScenarioManagerAction.List,
            ScenarioManagerDialogAction.Report => ScenarioManagerAction.Report,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };

    private static string? LocalizeValidationError(ScenarioManagerDialogValidationError error) =>
        error switch
        {
            ScenarioManagerDialogValidationError.None => null,
            ScenarioManagerDialogValidationError.EnterScenarioName =>
                UiText.Get("ScenarioManager_EnterScenarioName"),
            ScenarioManagerDialogValidationError.EnterValidChangingCellsReference =>
                UiText.Get("ScenarioManager_EnterValidChangingCellsReference"),
            ScenarioManagerDialogValidationError.EnterValidResultCellsReference =>
                UiText.Get("ScenarioManager_EnterValidResultCellsReference"),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, null)
        };

    private static string GetValidationFallbackText(ScenarioManagerDialogValidationField field) =>
        field is ScenarioManagerDialogValidationField.ResultCells
            ? UiText.Get("ScenarioManager_EnterScenarioResultCells")
            : UiText.Get("ScenarioManager_EnterScenarioDetails");
}
