using Free.Shared.Shell;

namespace FreeW.App.Presentation.Dialogs;

public static class FreeWDialogFocusPlanner
{
    public static readonly DialogFocusPlan<string> CompareDocuments = Create("CompareDocumentsAuthorBox");
    public static readonly DialogFocusPlan<string> Properties = Create("DocumentPropertiesTitle");
    public static readonly DialogFocusPlan<string> TableFormula = Create("TableFormulaFormulaBox");
    public static readonly DialogFocusPlan<string> Zoom = Create("ZoomCustomPercentBox");

    private static DialogFocusPlan<string> Create(string focusTargetAutomationId) => new(
        InitialFocusTarget: focusTargetAutomationId,
        ValidationFocusTarget: focusTargetAutomationId,
        SelectAllOnFocus: true,
        ActionButtons:
        [
            new DialogActionPlan("OK", IsDefault: true),
            new DialogActionPlan("Cancel", IsCancel: true),
        ]);
}
