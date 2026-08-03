namespace FreeW.App.Presentation.Dialogs;

/// <summary>
/// Shared semantic contract for a dialog action row. Hosts create their native buttons, while this
/// plan keeps user-facing labels, ordering, and Enter/Escape roles aligned.
/// </summary>
public sealed record DialogActionButtonPlan(
    string Label,
    bool IsDefault = false,
    bool IsCancel = false);

public static class DocumentInspectorDialogPlanner
{
    public static IReadOnlyList<DialogActionButtonPlan> ActionButtons { get; } =
    [
        new("Remove Selected", IsDefault: true),
        new("Close", IsCancel: true),
    ];
}
