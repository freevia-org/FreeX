using Free.Shared.Shell;
using FreeX.App.Presentation.Shell;

namespace FreeX.App.Presentation.Editing;

public enum MergeCellsContentWarningAction
{
    KeepFirstCell,
    ConcatenateAllCells,
    Cancel,
}

/// <summary>
/// Portable presentation metadata for the merge-content warning. Both renderers share the compact
/// message and the cell-text preview (WPF is the ground truth), keeping all wording and action
/// identity out of native control construction.
/// </summary>
public sealed class MergeCellsContentWarningPlan
{
    private readonly IReadOnlyDictionary<MergeCellsContentWarningAction, DialogSurfaceActionPlan<MergeCellsContentWarningAction>>
        _actions;

    internal MergeCellsContentWarningPlan(
        string previewText,
        IReadOnlyList<DialogSurfaceActionPlan<MergeCellsContentWarningAction>> actions)
    {
        PreviewText = previewText;
        Actions = actions;
        _actions = actions.ToDictionary(action => action.Id);
    }

    public string Title => "Merge Cells";

    public string PrimaryMessage => "Merging cells can discard cell contents.";

    public string CompactGuidanceMessage => "Choose how to handle the selected cell contents.";

    public string DialogAutomationId => FreeXAutomationIdCatalog.MergeCellsContentWarningDialog;

    public string PreviewText { get; }

    public IReadOnlyList<DialogSurfaceActionPlan<MergeCellsContentWarningAction>> Actions { get; }

    public DialogSurfaceActionPlan<MergeCellsContentWarningAction> Action(MergeCellsContentWarningAction action) =>
        _actions[action];
}

public static class MergeCellsContentWarningPlanner
{
    private const string KeepFirstLabel = "Keep only first cell";
    private const string ConcatenateLabel = "Concatenate all cells";
    private const string CancelLabel = "Cancel";

    private static readonly IReadOnlyList<DialogSurfaceActionPlan<MergeCellsContentWarningAction>> Actions =
    [
        new(
            MergeCellsContentWarningAction.KeepFirstCell,
            KeepFirstLabel,
            KeepFirstLabel,
            FreeXAutomationIdCatalog.MergeCellsKeepFirstButton,
            IsDefault: true),
        new(
            MergeCellsContentWarningAction.ConcatenateAllCells,
            ConcatenateLabel,
            ConcatenateLabel,
            FreeXAutomationIdCatalog.MergeCellsConcatenateButton),
        new(
            MergeCellsContentWarningAction.Cancel,
            CancelLabel,
            CancelLabel,
            FreeXAutomationIdCatalog.MergeCellsCancelButton,
            IsCancel: true),
    ];

    public static MergeCellsContentWarningPlan Create(IReadOnlyList<string?> entryDisplayTexts)
    {
        ArgumentNullException.ThrowIfNull(entryDisplayTexts);

        var preview = string.Join(", ", entryDisplayTexts
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Take(4));

        return new MergeCellsContentWarningPlan(preview, Actions);
    }
}
