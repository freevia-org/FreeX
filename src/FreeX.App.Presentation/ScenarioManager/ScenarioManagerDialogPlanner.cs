using FreeX.Core.Model;
using Free.Shared.Localization;

namespace FreeX.App.Presentation.ScenarioManager;

public enum ScenarioManagerAction
{
    Add,
    Edit,
    Save,
    Show,
    Delete,
    List,
    Report,
    Merge
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
    ScenarioManagerAction Action,
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
    public static LocalizedTextDescriptor? DescribeValidationError(
        ScenarioManagerDialogValidationError error) =>
        error switch
        {
            ScenarioManagerDialogValidationError.None => null,
            ScenarioManagerDialogValidationError.EnterScenarioName =>
                LocalizedTextDescriptor.Resource("ScenarioManager_EnterScenarioName"),
            ScenarioManagerDialogValidationError.EnterValidChangingCellsReference =>
                LocalizedTextDescriptor.Resource("ScenarioManager_EnterValidChangingCellsReference"),
            ScenarioManagerDialogValidationError.EnterValidResultCellsReference =>
                LocalizedTextDescriptor.Resource("ScenarioManager_EnterValidResultCellsReference"),
            _ => LocalizedTextDescriptor.Resource("ScenarioManager_EnterScenarioDetails"),
        };

    public static ValidationPresentationDescriptor<ScenarioManagerDialogValidationField> DescribeValidationFailure(
        ScenarioManagerDialogValidationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new(
            DescribeValidationError(failure.Error) ??
            LocalizedTextDescriptor.Resource(
                failure.Field == ScenarioManagerDialogValidationField.ResultCells
                    ? "ScenarioManager_EnterScenarioResultCells"
                    : "ScenarioManager_EnterScenarioDetails"),
            failure.Field);
    }

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

    public static bool RequiresScenarioName(ScenarioManagerAction action) =>
        action is ScenarioManagerAction.Add
            or ScenarioManagerAction.Edit
            or ScenarioManagerAction.Save;

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

        return WorkbookRangeTextCodec.TryParseMany(currentSheetId.Value, changingCellsText, resolveSheetIdByName, out _)
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

        // Preserve the exact (possibly multi-area, possibly cross-sheet) set of changing cells.
        // Do NOT collapse to a single bounding GridRange: that would silently absorb cells that
        // were never part of the scenario the next time the dialog recaptures the changing cells.
        var currentSheetId = scenario.ChangingCells[0].Address.Sheet;
        var segments = new List<string>();
        foreach (var group in scenario.ChangingCells
            .Select(cell => cell.Address)
            .Distinct()
            .GroupBy(address => address.Sheet))
        {
            foreach (var range in ToMinimalRanges(group))
                segments.Add(WorkbookRangeTextCodec.Format(range, currentSheetId, id => workbook.GetSheet(id)?.Name));
        }

        return string.Join(",", segments);
    }

    /// <summary>
    /// Groups a set of same-sheet cell addresses into the smallest number of GridRanges that
    /// exactly cover the given cells - never more, never fewer. A single rectangular block of
    /// cells collapses to one range; anything else (a scattered or "hole"-containing set) is
    /// emitted as individual single-cell ranges so no extra cell is ever absorbed.
    /// </summary>
    private static IEnumerable<GridRange> ToMinimalRanges(IEnumerable<CellAddress> addresses)
    {
        var cells = addresses.ToList();
        if (cells.Count == 0)
            yield break;

        var bounds = new GridRange(
            cells.Aggregate((a, b) => new CellAddress(a.Sheet, Math.Min(a.Row, b.Row), Math.Min(a.Col, b.Col))),
            cells.Aggregate((a, b) => new CellAddress(a.Sheet, Math.Max(a.Row, b.Row), Math.Max(a.Col, b.Col))));

        if (bounds.CellCount == cells.Count)
        {
            // The cells exactly fill their bounding rectangle - safe to represent as one range.
            yield return bounds;
            yield break;
        }

        foreach (var cell in cells.OrderBy(c => c.Row).ThenBy(c => c.Col))
            yield return new GridRange(cell, cell);
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
        ScenarioManagerAction action,
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
        if (action is ScenarioManagerAction.Report && !resultCellsValidation.IsValid)
        {
            return new ScenarioManagerDialogValidationFailure(
                resultCellsValidation.Error,
                ScenarioManagerDialogValidationField.ResultCells);
        }

        return null;
    }

    public static ScenarioManagerDialogAcceptResult ProjectAcceptResult(
        ScenarioManagerAction action,
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
