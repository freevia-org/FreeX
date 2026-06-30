using FreeX.App.Presentation.ScenarioManager;
using FreeX.App.Services;
using FreeX.Core.Model;

using SharedScenarioManagerDialogPlanner = FreeX.App.Presentation.ScenarioManager.ScenarioManagerDialogPlanner;

namespace FreeX.App.Host;

public sealed partial class ScenarioManagerDialog
{
    public static IReadOnlyList<ScenarioManagerDialogItem> BuildScenarioItems(Workbook workbook) =>
        SharedScenarioManagerDialogPlanner.BuildItems(workbook);

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

    internal static ScenarioManagerDialogSelectionFields? ProjectSelectionFields(
        ScenarioManagerDialogItem? selected,
        string currentScenarioNameText,
        string defaultScenarioName) =>
        SharedScenarioManagerDialogPlanner.ProjectSelectionFields(
            selected,
            currentScenarioNameText,
            defaultScenarioName);

    internal static ScenarioManagerDialogValidationFailure? ValidateAcceptRequest(
        ScenarioManagerAction action,
        string? scenarioName,
        string? changingCellsText,
        string? resultCellsText,
        SheetId? currentSheetId,
        Func<string, SheetId?>? resolveSheetIdByName)
    {
        return SharedScenarioManagerDialogPlanner.ValidateAcceptRequest(
            ToDialogAction(action),
            scenarioName,
            changingCellsText,
            resultCellsText,
            currentSheetId,
            resolveSheetIdByName);
    }

    internal static ScenarioManagerDialogAcceptResult ProjectAcceptResult(
        ScenarioManagerAction action,
        ScenarioManagerDialogItem? selected,
        string newScenarioName,
        string changingCellsText,
        string resultCellsText,
        string commentText,
        bool locked,
        bool hidden) =>
        SharedScenarioManagerDialogPlanner.ProjectAcceptResult(
            ToDialogAction(action),
            selected,
            newScenarioName,
            changingCellsText,
            resultCellsText,
            commentText,
            locked,
            hidden);

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
