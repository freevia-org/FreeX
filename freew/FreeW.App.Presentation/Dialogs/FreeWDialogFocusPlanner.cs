namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// Shared focus and keyboard contract for dialog routes with an editable WPF authority target.
/// Hosts keep their native focus APIs, but agree on target identity, selection behavior, and action roles.
/// </summary>
public sealed record DialogFocusPlan(
    string InitialFocusTargetAutomationId,
    string ValidationFocusTargetAutomationId,
    bool SelectAllOnFocus,
    IReadOnlyList<DialogActionButtonPlan> ActionButtons);

public static class FreeWDialogFocusPlanner
{
    public static readonly DialogFocusPlan CompareDocuments = Create("CompareDocumentsAuthorBox");
    public static readonly DialogFocusPlan Properties = Create("DocumentPropertiesTitle");
    public static readonly DialogFocusPlan TableFormula = Create("TableFormulaFormulaBox");
    public static readonly DialogFocusPlan Zoom = Create("ZoomCustomPercentBox");

    private static DialogFocusPlan Create(string focusTargetAutomationId) => new(
        InitialFocusTargetAutomationId: focusTargetAutomationId,
        ValidationFocusTargetAutomationId: focusTargetAutomationId,
        SelectAllOnFocus: true,
        ActionButtons:
        [
            new DialogActionButtonPlan("OK", IsDefault: true),
            new DialogActionButtonPlan("Cancel", IsCancel: true),
        ]);
}
