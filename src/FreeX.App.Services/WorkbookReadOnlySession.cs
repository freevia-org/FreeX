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

/// <summary>
/// Owns the read-only recommendation state for one workbook window. Native hosts retain only
/// prompt presentation and adapter resolution; open/reset/save policy stays identical.
/// </summary>
public sealed class WorkbookReadOnlySession
{
    private string? _reservationPassword;

    public bool IsReadOnly { get; private set; }

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
