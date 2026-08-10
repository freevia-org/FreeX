using FreeX.Core.Calc;

namespace FreeX.App.Services;

public sealed record WorkbookGoalSeekProposal(
    GoalSeekRequest Request,
    GoalSeekResult? SeekResult,
    string? ErrorMessage)
{
    public bool Success => SeekResult is not null && ErrorMessage is null;

    public static WorkbookGoalSeekProposal Invalid(GoalSeekRequest request, string errorMessage) =>
        new(request, null, errorMessage);

    public static WorkbookGoalSeekProposal Ready(GoalSeekRequest request, GoalSeekResult seekResult) =>
        new(request, seekResult, null);
}

public sealed record WorkbookGoalSeekResult(
    WorkbookGoalSeekStatus Status,
    GoalSeekRequest Request,
    GoalSeekResult? SeekResult,
    WorkbookCellEditResult? EditResult,
    string? ErrorMessage)
{
    public bool Success => Status == WorkbookGoalSeekStatus.Applied;

    public bool Converged => SeekResult?.Converged == true;

    public bool Applied => EditResult?.Success == true;

    public static WorkbookGoalSeekResult Invalid(GoalSeekRequest request, string errorMessage) =>
        new(WorkbookGoalSeekStatus.InvalidRequest, request, null, null, errorMessage);

    public static WorkbookGoalSeekResult NotConverged(GoalSeekRequest request, GoalSeekResult seekResult) =>
        new(WorkbookGoalSeekStatus.NotConverged, request, seekResult, null, null);

    public static WorkbookGoalSeekResult AppliedResult(
        GoalSeekRequest request,
        GoalSeekResult seekResult,
        WorkbookCellEditResult editResult) =>
        new(WorkbookGoalSeekStatus.Applied, request, seekResult, editResult, null);

    public static WorkbookGoalSeekResult ApplyFailed(
        GoalSeekRequest request,
        GoalSeekResult seekResult,
        WorkbookCellEditResult editResult) =>
        new(
            WorkbookGoalSeekStatus.ApplyFailed,
            request,
            seekResult,
            editResult,
            editResult.ErrorMessage ?? "Goal Seek result could not be applied.");
}
