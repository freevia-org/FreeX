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
    /// Checks the OS read-only attribute first (the common case: Explorer's Read-only checkbox,
    /// or <c>attrib +r</c>), then falls back to a lightweight open-for-write probe so a read-only
    /// network share, a read-only-mounted volume, or a denied ACL are caught too -- none of those
    /// necessarily set the DOS read-only attribute. A transient sharing violation (e.g. another
    /// process briefly holding an exclusive handle) is deliberately NOT treated as read-only: it
    /// says nothing about the file's durable write permission, and misclassifying it would force
    /// an otherwise-editable file through Save As.
    /// </summary>
    private static bool IsFileWriteRestricted(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        try
        {
            if (!File.Exists(filePath))
                return false;

            if (File.GetAttributes(filePath).HasFlag(FileAttributes.ReadOnly))
                return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            return false;
        }

        try
        {
            using var probe = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch (IOException)
        {
            // Locked by another process, a network hiccup, etc. -- not necessarily a write
            // restriction, so don't force the file read-only on a transient failure.
            return false;
        }
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
