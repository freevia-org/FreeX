using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public enum WorkbookReadOnlyPromptKind
{
    None,
    ReadOnlyRecommended,
    ReservationPassword
}

public readonly record struct WorkbookReadOnlyOpenPlan(
    WorkbookReadOnlyPromptKind PromptKind,
    string WorkbookName)
{
    public bool ShouldPrompt => PromptKind != WorkbookReadOnlyPromptKind.None;
}

public readonly record struct WorkbookReadOnlyPasswordDecision(
    bool IsReadOnly,
    bool ShouldShowIncorrectPasswordNotice);

public enum WorkbookReadOnlyRecommendationChoice
{
    OpenEditable,
    OpenReadOnly
}

public readonly record struct WorkbookReservationPasswordResponse
{
    private WorkbookReservationPasswordResponse(bool isAccepted, string password)
    {
        IsAccepted = isAccepted;
        Password = password;
    }

    public bool IsAccepted { get; }

    public string Password { get; }

    public static WorkbookReservationPasswordResponse Accepted(string password) =>
        new(isAccepted: true, password ?? throw new ArgumentNullException(nameof(password)));

    public static WorkbookReservationPasswordResponse Cancelled { get; } =
        new(isAccepted: false, string.Empty);

    public static WorkbookReservationPasswordResponse FromPromptResult(string? password) =>
        password is null ? Cancelled : Accepted(password);
}

public enum WorkbookReadOnlyOpenOutcomeKind
{
    Editable,
    ReadOnlyRecommendedAccepted,
    ReadOnlyRecommendedDeclined,
    ReservationPasswordAccepted,
    ReservationPasswordRejected,
    ReservationPasswordCancelled
}

public readonly record struct WorkbookReadOnlyOpenOutcome(
    WorkbookReadOnlyOpenPlan Plan,
    WorkbookReadOnlyOpenOutcomeKind Kind,
    bool IsReadOnly);

public interface IWorkbookReadOnlyOpenPromptPort
{
    WorkbookReadOnlyRecommendationChoice PromptReadOnlyRecommended(WorkbookReadOnlyOpenPlan plan);

    WorkbookReservationPasswordResponse PromptReservationPassword(WorkbookReadOnlyOpenPlan plan);

    void ShowIncorrectReservationPasswordNotice(WorkbookReadOnlyOpenPlan plan);
}

/// <summary>
/// Owns the read-only recommendation state for one workbook window. Native hosts retain only
/// prompt presentation and adapter resolution; open/reset/save policy stays identical.
/// </summary>
public sealed class WorkbookReadOnlySession
{
    private string? _reservationPassword;

    public bool IsReadOnly { get; private set; }

    public WorkbookReadOnlyOpenOutcome RunOpen(
        Workbook workbook,
        IWorkbookReadOnlyOpenPromptPort promptPort)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(promptPort);

        var plan = PlanOpen(workbook);
        switch (plan.PromptKind)
        {
            case WorkbookReadOnlyPromptKind.None:
                return new WorkbookReadOnlyOpenOutcome(
                    plan,
                    WorkbookReadOnlyOpenOutcomeKind.Editable,
                    IsReadOnly);

            case WorkbookReadOnlyPromptKind.ReadOnlyRecommended:
            {
                var choice = promptPort.PromptReadOnlyRecommended(plan);
                var openReadOnly = choice == WorkbookReadOnlyRecommendationChoice.OpenReadOnly;
                ApplyPromptDecision(openReadOnly);
                return new WorkbookReadOnlyOpenOutcome(
                    plan,
                    openReadOnly
                        ? WorkbookReadOnlyOpenOutcomeKind.ReadOnlyRecommendedAccepted
                        : WorkbookReadOnlyOpenOutcomeKind.ReadOnlyRecommendedDeclined,
                    IsReadOnly);
            }

            case WorkbookReadOnlyPromptKind.ReservationPassword:
            {
                var response = promptPort.PromptReservationPassword(plan);
                var decision = ApplyReservationPassword(response.IsAccepted ? response.Password : null);
                if (decision.ShouldShowIncorrectPasswordNotice)
                    promptPort.ShowIncorrectReservationPasswordNotice(plan);

                return new WorkbookReadOnlyOpenOutcome(
                    plan,
                    !response.IsAccepted
                        ? WorkbookReadOnlyOpenOutcomeKind.ReservationPasswordCancelled
                        : decision.IsReadOnly
                            ? WorkbookReadOnlyOpenOutcomeKind.ReservationPasswordRejected
                            : WorkbookReadOnlyOpenOutcomeKind.ReservationPasswordAccepted,
                    IsReadOnly);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(plan), plan.PromptKind, "Unknown read-only prompt kind.");
        }
    }

    public WorkbookReadOnlyOpenPlan PlanOpen(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        Reset();

        var sharing = workbook.FileSharing;
        if (!string.IsNullOrEmpty(sharing?.ReservationPassword))
        {
            _reservationPassword = sharing.ReservationPassword;
            return new WorkbookReadOnlyOpenPlan(
                WorkbookReadOnlyPromptKind.ReservationPassword,
                workbook.Name);
        }

        return new WorkbookReadOnlyOpenPlan(
            sharing?.ReadOnlyRecommended == true
                ? WorkbookReadOnlyPromptKind.ReadOnlyRecommended
                : WorkbookReadOnlyPromptKind.None,
            workbook.Name);
    }

    public void ApplyPromptDecision(bool openReadOnly) => IsReadOnly = openReadOnly;

    public WorkbookReadOnlyPasswordDecision ApplyReservationPassword(string? providedPassword)
    {
        var storedPassword = _reservationPassword;
        _reservationPassword = null;

        var unlocked = !string.IsNullOrEmpty(storedPassword)
            && ProtectionPasswordHelper.VerifyStoredPassword(storedPassword, providedPassword);
        IsReadOnly = !unlocked;
        return new WorkbookReadOnlyPasswordDecision(
            IsReadOnly,
            ShouldShowIncorrectPasswordNotice: !unlocked && providedPassword is not null);
    }

    public void Reset()
    {
        IsReadOnly = false;
        _reservationPassword = null;
    }

    public FileSaveTarget? ResolveExistingSaveTarget(Func<FileSaveTarget?> resolveEditableTarget)
    {
        ArgumentNullException.ThrowIfNull(resolveEditableTarget);
        return IsReadOnly ? null : resolveEditableTarget();
    }
}
