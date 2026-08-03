namespace FreeX.App.Presentation.Dialogs;

/// <summary>
/// Shared geometry for the compact Goal Seek Status dialog. WPF remains the visual authority;
/// Avalonia consumes these values so the status workflow keeps one cross-shell layout contract.
/// </summary>
public static class GoalSeekStatusDialogPlanner
{
    public const double WindowWidth = 380;
    public const double ConvergedWindowHeight = 190;
    public const double NotConvergedWindowHeight = 170;
    public const double ContentMargin = 16;
    public const double SummaryLineHeight = 32;
    public const double SummaryTopCompensation = -5;
    public const double SummaryBottomMargin = 5;
    public const double ButtonHeight = 20;
    public const double ButtonGap = 8;
    public const double KeepResultButtonWidth = 104;
    public const double RestoreOriginalValuesButtonWidth = 152;
    public const double OkButtonWidth = 76;

    public static double WindowHeight(bool converged) =>
        converged ? ConvergedWindowHeight : NotConvergedWindowHeight;
}
