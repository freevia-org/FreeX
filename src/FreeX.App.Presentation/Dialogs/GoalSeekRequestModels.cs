using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed record GoalSeekRequest(CellAddress SetCell, double TargetValue, CellAddress ChangingCell);

public enum WorkbookGoalSeekStatus
{
    Applied,
    NotConverged,
    InvalidRequest,
    ApplyFailed
}

public enum GoalSeekRequestParseError
{
    None,
    SetCellRequired,
    InvalidSetCellAddress,
    InvalidTargetValue,
    ChangingCellRequired,
    InvalidChangingCellAddress,
    CellsMustDiffer
}

public sealed record GoalSeekRequestParseResult(
    GoalSeekRequest? Request,
    GoalSeekRequestParseError Error,
    string InvalidText)
{
    public bool Success => Error == GoalSeekRequestParseError.None;

    public static GoalSeekRequestParseResult Valid(GoalSeekRequest request) =>
        new(request, GoalSeekRequestParseError.None, "");

    public static GoalSeekRequestParseResult Invalid(
        GoalSeekRequestParseError error,
        string invalidText = "") =>
        new(null, error, invalidText);
}
