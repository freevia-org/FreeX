namespace FreeX.App.Presentation.Shell;

/// <summary>
/// Renderer-neutral ribbon state for View Side by Side, Reset Window Position, and
/// Synchronous Scrolling. Hosts supply only registered workbook-window counts and the
/// requester's coordinator state; native dialogs and unrelated top-level windows never
/// participate in command availability.
/// </summary>
public readonly record struct WorkbookSideBySideCommandStatePlan(
    bool ViewSideBySideEnabled,
    bool ViewSideBySideChecked,
    bool ResetWindowPositionEnabled,
    bool SynchronousScrollingEnabled,
    bool SynchronousScrollingChecked);

public static class WorkbookSideBySideCommandStatePlanner
{
    public static WorkbookSideBySideCommandStatePlan Build(
        int visibleWorkbookWindowCount,
        bool anyPairActive,
        bool requesterInPair,
        bool synchronousScrollingActiveForRequester)
    {
        var inPair = anyPairActive && requesterInPair;
        var syncActive = inPair && synchronousScrollingActiveForRequester;
        return new WorkbookSideBySideCommandStatePlan(
            ViewSideBySideEnabled: inPair || (!anyPairActive && visibleWorkbookWindowCount > 1),
            ViewSideBySideChecked: inPair,
            ResetWindowPositionEnabled: inPair,
            SynchronousScrollingEnabled: inPair,
            SynchronousScrollingChecked: syncActive);
    }
}
