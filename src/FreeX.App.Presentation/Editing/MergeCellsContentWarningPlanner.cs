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
/// Portable presentation metadata for the merge-content warning. The compact and detailed messages
/// preserve each renderer's established text while keeping all wording and action identity out of
/// native control construction.
/// </summary>
public sealed class MergeCellsContentWarningPlan
{
    private readonly IReadOnlyDictionary<MergeCellsContentWarningAction, DialogSurfaceActionPlan<MergeCellsContentWarningAction>>
        _actions;

    internal MergeCellsContentWarningPlan(
        string previewText,
        string? entryCountText,
        IReadOnlyList<DialogSurfaceActionPlan<MergeCellsContentWarningAction>> actions)
    {
        PreviewText = previewText;
        EntryCountText = entryCountText;
        Actions = actions;
        _actions = actions.ToDictionary(action => action.Id);
    }

    public string Title => "Merge Cells";

    public string PrimaryMessage => "Merging cells can discard cell contents.";

    public string CompactGuidanceMessage => "Choose how to handle the selected cell contents.";

    public string DetailedGuidanceMessage =>
        "Only the first cell is kept by default. Choose how FreeX should handle the other selected contents.";

    public string DialogAutomationId => FreeXAutomationIdCatalog.MergeCellsContentWarningDialog;

    public string PreviewText { get; }

    public string? EntryCountText { get; }

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
        var countText = entryDisplayTexts.Count > 0
            ? $"Non-empty cells: {entryDisplayTexts.Count}"
            : null;

        return new MergeCellsContentWarningPlan(preview, countText, Actions);
    }
}
