using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Framework-neutral ownership for one in-canvas rich-text transaction. Native controls retain
/// input, selection realization, clipboard, IME, undo, and document rendering responsibilities.
/// </summary>
public sealed class InCanvasRichTextEditSession : InCanvasRichTextEditBuffer
{
    private readonly InCanvasTextEditPlanner? _shapePlanner;
    private readonly InCanvasTableCellTextEditPlanner? _tableCellPlanner;
    private bool _completed;

    private InCanvasRichTextEditSession(
        TextBody? body,
        InCanvasTextEditPlanner? shapePlanner = null,
        InCanvasTableCellTextEditPlanner? tableCellPlanner = null)
        : base(body)
    {
        _shapePlanner = shapePlanner;
        _tableCellPlanner = tableCellPlanner;
    }

    public static InCanvasRichTextEditSession Create(TextBody? body) => new(body);

    public static InCanvasRichTextEditSession BeginShape(InCanvasTextEditStartPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsReady || plan.EditPlanner is null)
            throw new ArgumentException("A ready shape edit plan is required.", nameof(plan));

        return new InCanvasRichTextEditSession(plan.OriginalBody, shapePlanner: plan.EditPlanner);
    }

    public static InCanvasRichTextEditSession BeginTableCell(TableCellEditStartPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.IsReady || plan.EditPlanner is null)
            throw new ArgumentException("A ready table-cell edit plan is required.", nameof(plan));

        return new InCanvasRichTextEditSession(plan.OriginalBody, tableCellPlanner: plan.EditPlanner);
    }

    /// <summary>Adopts the latest native document snapshot before a portable decision is made.</summary>
    public void SynchronizeBody(TextBody? body)
    {
        EnsureOpen();
        ResetBody(body);
    }

    public InCanvasTextEditDecision Commit(TextBody? editedBody = null)
    {
        if (_completed)
            return new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);

        if (editedBody is not null)
            SynchronizeBody(editedBody);

        _completed = true;
        var body = Body;
        if (_shapePlanner is not null)
            return _shapePlanner.CommitRichText(body);
        if (_tableCellPlanner is not null)
            return _tableCellPlanner.CommitRichText(body);
        return new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);
    }

    public InCanvasTextEditDecision Cancel()
    {
        if (_completed)
            return new InCanvasTextEditDecision(InCanvasTextEditOutcome.Unchanged, null);

        _completed = true;
        return _shapePlanner?.Cancel()
            ?? _tableCellPlanner?.Cancel()
            ?? new InCanvasTextEditDecision(InCanvasTextEditOutcome.Canceled, null);
    }

    public TableCellNavigationPlan PlanTableCellNavigation(
        Slide? slide,
        IReadOnlyList<uint> selectedShapeIds,
        (int Row, int Col)? activeCell,
        TableCellNavigationDirection direction)
    {
        EnsureOpen();
        return TableCellEditPlanner.PlanNavigation(slide, selectedShapeIds, activeCell, direction);
    }

    public new void ReplacePlainText(string? editedText)
    {
        EnsureOpen();
        base.ReplacePlainText(editedText);
    }

    public bool ToggleTextFormat(
        TableCellTextFormatKind kind,
        (int Start, int End)? selection) =>
        ApplyNativeBodyMutation(body => InCanvasTextEditPlanner.ApplyTextFormat(body, kind, selection));

    public bool ApplyValueFormat(
        TableCellTextValueFormatKind kind,
        object? value,
        (int Start, int End)? selection) =>
        ApplyNativeBodyMutation(body =>
            InCanvasTextEditPlanner.ApplyTextValueFormat(body, kind, value, selection));

    public bool ApplyParagraphAlignment(
        TextAlign alignment,
        (int Start, int End)? selection) =>
        ApplyNativeBodyMutation(body =>
            InCanvasTextEditPlanner.ApplyParagraphAlignment(body, alignment, selection));

    public bool ToggleParagraphBullets((int Start, int End)? selection) =>
        ApplyNativeBodyMutation(body =>
            InCanvasTextEditPlanner.ApplyParagraphBulletToggle(body, selection));

    public bool ToggleParagraphNumbering((int Start, int End)? selection) =>
        ApplyNativeBodyMutation(body =>
            InCanvasTextEditPlanner.ApplyParagraphNumberingToggle(body, selection));

    public bool ApplyParagraphListPreset(
        TableCellListPresetDescriptor preset,
        (int Start, int End)? selection)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return ApplyNativeBodyMutation(body =>
            InCanvasTextEditPlanner.ApplyParagraphListPreset(body, selection, preset));
    }

    public bool ApplyParagraphPictureBullet(
        PresentationPictureBulletPayload payload,
        (int Start, int End)? selection)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (!payload.IsValid)
            return false;

        return ApplyNativeBodyMutation(body =>
            InCanvasTextEditPlanner.ApplyParagraphPictureBullet(
                body,
                selection,
                PresentationPictureBulletAuthoringPlanner.CreateImagePart(payload)));
    }

    public bool ApplyParagraphIndent(bool increase, (int Start, int End)? selection) =>
        ApplyNativeBodyMutation(body =>
            InCanvasTextEditPlanner.ApplyParagraphIndent(body, increase, selection));

    private bool ApplyNativeBodyMutation(Func<TextBody, TextBody> mutate)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(mutate);
        ResetBody(mutate(Body));
        return true;
    }

    private void EnsureOpen()
    {
        if (_completed)
            throw new InvalidOperationException("The rich-text edit session is already complete.");
    }
}
