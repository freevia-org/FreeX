using FreeX.Core.Model;
using FreeX.Core.IO;

namespace FreeX.App.Services;

public readonly record struct WorkbookReadOnlyOpenPlan(
    bool ShouldPrompt,
    string WorkbookName);

/// <summary>
/// Owns the read-only recommendation state for one workbook window. Native hosts retain only
/// prompt presentation and adapter resolution; open/reset/save policy stays identical.
/// </summary>
public sealed class WorkbookReadOnlySession
{
    public bool IsReadOnly { get; private set; }

    public WorkbookReadOnlyOpenPlan PlanOpen(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        Reset();

        var sharing = workbook.FileSharing;
        var shouldPrompt = sharing is not null &&
            (sharing.ReadOnlyRecommended == true || !string.IsNullOrEmpty(sharing.ReservationPassword));
        return new WorkbookReadOnlyOpenPlan(shouldPrompt, workbook.Name);
    }

    public void ApplyPromptDecision(bool openReadOnly) => IsReadOnly = openReadOnly;

    public void Reset() => IsReadOnly = false;

    public FileSaveTarget? ResolveExistingSaveTarget(Func<FileSaveTarget?> resolveEditableTarget)
    {
        ArgumentNullException.ThrowIfNull(resolveEditableTarget);
        return IsReadOnly ? null : resolveEditableTarget();
    }
}
