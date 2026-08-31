using Free.Shared.IO;
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
    string WorkbookName,
    bool IsFileSystemReadOnly = false)
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
    ReservationPasswordCancelled,
    FileSystemReadOnly
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
        IWorkbookReadOnlyOpenPromptPort promptPort,
        string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(promptPort);

        var plan = PlanOpen(workbook, filePath);
        switch (plan.PromptKind)
        {
            case WorkbookReadOnlyPromptKind.None:
                // Neither embedded workbook flag (ReadOnlyRecommended / ReservationPassword)
                // applies, but the file on disk itself cannot be written back to (OS read-only
                // attribute, a read-only share/volume, or a denied ACL -- see PlanOpen). Unlike
                // the embedded-flag cases, Excel does not interrupt the user with a prompt for
                // this: it silently forces the document read-only. Apply that state directly
                // without invoking promptPort.
                if (plan.IsFileSystemReadOnly)
                {
                    IsReadOnly = true;
                    return new WorkbookReadOnlyOpenOutcome(
                        plan,
                        WorkbookReadOnlyOpenOutcomeKind.FileSystemReadOnly,
                        IsReadOnly);
                }

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

    public WorkbookReadOnlyOpenPlan PlanOpen(Workbook workbook, string? filePath = null)
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

        if (sharing?.ReadOnlyRecommended == true)
        {
            return new WorkbookReadOnlyOpenPlan(
                WorkbookReadOnlyPromptKind.ReadOnlyRecommended,
                workbook.Name);
        }

        // Neither embedded workbook flag applies. That used to mean "fully editable" even when
        // the file itself cannot be written back to (OS read-only attribute, a read-only network
        // share/mounted volume, or an ACL that denies Write) -- Save would then fail with a raw
        // "Access to the path is denied" error only after the user had already invested editing
        // effort, with no up-front indication. Classify that case here too, distinctly from the
        // embedded-flag prompts: it does not go through the prompt port (see RunOpen).
        return new WorkbookReadOnlyOpenPlan(
            WorkbookReadOnlyPromptKind.None,
            workbook.Name,
            IsFileSystemReadOnly: IsFileWriteRestricted(filePath));
    }

    /// <summary>
    /// Best-effort check of whether <paramref name="filePath"/> can currently be written back to.
    /// Delegates to the shared <see cref="FileWriteRestrictionProbe"/>, which FreeW and FreeP open
    /// through as well so all three apps classify a read-only source file identically.
    /// </summary>
    private static bool IsFileWriteRestricted(string? filePath) =>
        FileWriteRestrictionProbe.IsWriteRestricted(filePath);

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
