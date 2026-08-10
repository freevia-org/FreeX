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
        // Merge pulls scenarios from another sheet/workbook rather than the dialog's own name/
        // changing-cells fields, and has no ScenarioManagerDialogAction counterpart to route
        // through -- handle it directly instead of calling ToDialogAction (which has no Merge arm).
        action != ScenarioManagerAction.Merge &&
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
        // Merge has no dialog-form fields to validate (its source scenarios come from another
        // sheet/workbook, not the name/changing-cells/result-cells text boxes) and no
        // ScenarioManagerDialogAction counterpart -- never route it through ToDialogAction.
        if (action == ScenarioManagerAction.Merge)
            return null;

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
        bool hidden)
    {
        // Merge has no ScenarioManagerDialogAction counterpart (it isn't part of the
        // Add/Edit/Save/Show/Delete/List/Report dialog form) and today no dialog button ever
        // calls Accept(ScenarioManagerAction.Merge) -- ScenarioManagerDialog has no Merge button
        // yet, so this branch is unreachable in shipped UI. It exists purely so a future Merge
        // trigger (or a direct call) can never hit ToDialogAction's throw for Merge; the
        // placeholder ScenarioManagerDialogAction.List here is never surfaced as a real selection.
        if (action == ScenarioManagerAction.Merge)
        {
            return new ScenarioManagerDialogAcceptResult(
                ScenarioManagerDialogAction.List,
                selected?.Name,
                newScenarioName,
                changingCellsText,
                resultCellsText,
                commentText,
                locked,
                hidden);
        }

        return SharedScenarioManagerDialogPlanner.ProjectAcceptResult(
            ToDialogAction(action),
            selected,
            newScenarioName,
            changingCellsText,
            resultCellsText,
            commentText,
            locked,
            hidden);
    }

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
        SharedScenarioManagerDialogPlanner
            .DescribeValidationError(error)?
            .Resolve(UiText.Get, UiText.Format);

    private static string GetValidationFallbackText(ScenarioManagerDialogValidationField field) =>
        SharedScenarioManagerDialogPlanner
            .DescribeValidationFailure(new ScenarioManagerDialogValidationFailure(
                ScenarioManagerDialogValidationError.None,
                field))
            .Message
            .Resolve(UiText.Get, UiText.Format);
}
