namespace FreeP.App.Compositor;

public interface IPresentationMainWindowReviewPaneView
{
    bool IsAccessibilityPaneVisible { get; }

    bool IsProofingPaneVisible { get; }

    void SetAccessibilityPaneVisible(bool visible);

    void SetProofingPaneVisible(bool visible);

    void RenderAccessibilityPane(PresentationAccessibilityCheckerPanePlan plan);

    void RenderProofingPane(PresentationProofingPanePlan plan);

    void RefreshPaneAccessibilityMetadata();
}

public sealed record PresentationMainWindowReviewPaneViewBindings(
    Func<bool> IsAccessibilityPaneVisible,
    Func<bool> IsProofingPaneVisible,
    Action<bool> SetAccessibilityPaneVisible,
    Action<bool> SetProofingPaneVisible,
    Action<PresentationAccessibilityCheckerPanePlan> RenderAccessibilityPane,
    Action<PresentationProofingPanePlan> RenderProofingPane,
    Action RefreshPaneAccessibilityMetadata);

public sealed class DelegatingPresentationMainWindowReviewPaneView : IPresentationMainWindowReviewPaneView
{
    private readonly PresentationMainWindowReviewPaneViewBindings _bindings;

    public DelegatingPresentationMainWindowReviewPaneView(
        PresentationMainWindowReviewPaneViewBindings bindings)
    {
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
    }

    public bool IsAccessibilityPaneVisible => _bindings.IsAccessibilityPaneVisible();
    public bool IsProofingPaneVisible => _bindings.IsProofingPaneVisible();
    public void SetAccessibilityPaneVisible(bool visible) => _bindings.SetAccessibilityPaneVisible(visible);
    public void SetProofingPaneVisible(bool visible) => _bindings.SetProofingPaneVisible(visible);
    public void RenderAccessibilityPane(PresentationAccessibilityCheckerPanePlan plan) =>
        _bindings.RenderAccessibilityPane(plan);
    public void RenderProofingPane(PresentationProofingPanePlan plan) => _bindings.RenderProofingPane(plan);
    public void RefreshPaneAccessibilityMetadata() => _bindings.RefreshPaneAccessibilityMetadata();
}

public enum PresentationProofingRowActionKind
{
    ApplyCorrection,
    Ignore,
    IgnoreAll,
    AddToDictionary,
    Select,
}

public sealed record PresentationProofingRowActionRenderPlan(
    PresentationProofingRowActionKind Kind,
    string Label,
    bool IsEnabled,
    string? DisabledReason,
    double MinimumWidth,
    bool HasLeadingSpacing);

public sealed record PresentationCommentMentionNativeItemPlan(
    string Label,
    string SemanticTag,
    PresentationCommentMentionCandidate Candidate);

public sealed record PresentationCommentMentionMenuNativeBindings<TMenu, TItem>(
    Func<TMenu> CreateMenu,
    Func<PresentationCommentMentionNativeItemPlan, TItem> CreateItem,
    Action<TItem, Action> BindClick,
    Action<TMenu, TItem> AddItem);

/// <summary>
/// Owns renderer-neutral review pane lifecycle, command routing, and proofing row interactions.
/// Native hosts retain control construction, visibility projection, and event attachment.
/// </summary>
public sealed class PresentationMainWindowReviewPaneCoordinator
{
    private readonly PresentationReviewWorkflowSession _session;
    private readonly PresentationWorkareaPaneSession _panes;
    private readonly IPresentationMainWindowReviewPaneView _view;

    public PresentationMainWindowReviewPaneCoordinator(
        PresentationReviewWorkflowSession session,
        PresentationWorkareaPaneSession panes,
        IPresentationMainWindowReviewPaneView view)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _panes = panes ?? throw new ArgumentNullException(nameof(panes));
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public void RenderAccessibilityPaneIfVisible(PresentationAccessibilityCheckerPanePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_view.IsAccessibilityPaneVisible)
            _view.RenderAccessibilityPane(plan);
    }

    public void PresentAccessibilityPane(PresentationAccessibilityCheckerPanePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _panes.Show(PresentationWorkareaPane.AccessibilityChecker);
        _view.RenderAccessibilityPane(plan);
        _view.SetAccessibilityPaneVisible(true);
    }

    public PresentationProofingPanePlan ShowProofingPane() => _session.ShowProofingPane();

    public void RenderProofingPaneIfVisible(PresentationProofingPanePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_view.IsProofingPaneVisible)
            _view.RenderProofingPane(plan);
    }

    public void PresentProofingPane(PresentationProofingPanePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _panes.Show(PresentationWorkareaPane.Proofing);
        _view.RenderProofingPane(plan);
        _view.SetProofingPaneVisible(true);
        _view.RefreshPaneAccessibilityMetadata();
    }

    public void ExecuteCommentCommand(string commandId)
    {
        switch (commandId)
        {
            case PresentationReviewWorkflowPlanner.AddCommentCommandId:
                _session.AddComment(PresentationPaneTextResources.NewCommentDefault);
                break;
            case PresentationReviewWorkflowPlanner.EditCommentCommandId:
                _session.EditSelectedComment(_session.GetSelectedCommentText());
                break;
            case PresentationReviewWorkflowPlanner.ResolveCommentCommandId:
                _session.ResolveSelectedComment();
                break;
            case PresentationReviewWorkflowPlanner.ReopenCommentCommandId:
                _session.ReopenSelectedComment();
                break;
            case PresentationReviewWorkflowPlanner.DeleteCommentCommandId:
                _session.DeleteSelectedComment();
                break;
            case PresentationReviewWorkflowPlanner.PreviousCommentCommandId:
                _session.NavigateReviewComment(PresentationReviewWorkflowIntentKind.PreviousComment);
                break;
            case PresentationReviewWorkflowPlanner.NextCommentCommandId:
                _session.NavigateReviewComment(PresentationReviewWorkflowIntentKind.NextComment);
                break;
        }
    }

    public void ExecuteProofingRowAction(int rowIndex, PresentationProofingRowActionKind kind)
    {
        _session.SelectProofingIssueRow(rowIndex);
        switch (kind)
        {
            case PresentationProofingRowActionKind.ApplyCorrection:
                _session.ApplySelectedProofingCorrection();
                break;
            case PresentationProofingRowActionKind.Ignore:
                _session.IgnoreSelectedProofingIssue();
                break;
            case PresentationProofingRowActionKind.IgnoreAll:
                _session.IgnoreAllSelectedProofingIssues();
                break;
            case PresentationProofingRowActionKind.AddToDictionary:
                _session.AddSelectedProofingWordToDictionary();
                break;
            case PresentationProofingRowActionKind.Select:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }

    public TMenu BuildCommentMentionMenu<TMenu, TItem>(
        string tag,
        Func<string?> getText,
        Func<int> getCaretIndex,
        PresentationReviewWorkflowIntentKind intent,
        PresentationCommentMentionPickerPlan picker,
        PresentationCommentMentionMenuNativeBindings<TMenu, TItem> native)
    {
        ArgumentNullException.ThrowIfNull(getText);
        ArgumentNullException.ThrowIfNull(getCaretIndex);
        ArgumentNullException.ThrowIfNull(picker);
        ArgumentNullException.ThrowIfNull(native);

        var menu = native.CreateMenu();
        foreach (var candidate in picker.Candidates)
        {
            var plan = new PresentationCommentMentionNativeItemPlan(
                candidate.Label,
                PresentationSemanticIdentityCatalog.BuildCommentMentionCandidateTag(
                    tag,
                    candidate.InsertToken),
                candidate);
            var item = native.CreateItem(plan);
            native.BindClick(item, () => _session.ApplyCommentMention(
                intent,
                getText(),
                getCaretIndex(),
                candidate));
            native.AddItem(menu, item);
        }

        return menu;
    }

    public static IReadOnlyList<PresentationProofingRowActionRenderPlan> BuildProofingRowActions(
        PresentationProofingIssueRowPlan row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return
        [
            From(PresentationProofingRowActionKind.ApplyCorrection, row.CorrectionAction, 72, false),
            From(PresentationProofingRowActionKind.Ignore, row.IgnoreAction, 72, true),
            From(PresentationProofingRowActionKind.IgnoreAll, row.IgnoreAllAction, 72, true),
            From(PresentationProofingRowActionKind.AddToDictionary, row.AddToDictionaryAction, 120, true),
            From(PresentationProofingRowActionKind.Select, row.SelectionAction, 72, true),
        ];
    }

    private static PresentationProofingRowActionRenderPlan From(
        PresentationProofingRowActionKind kind,
        PresentationReviewWorkflowActionPlan action,
        double minimumWidth,
        bool hasLeadingSpacing) =>
        new(kind, action.Label, action.IsEnabled, action.DisabledReason, minimumWidth, hasLeadingSpacing);

    private static PresentationProofingRowActionRenderPlan From(
        PresentationProofingRowActionKind kind,
        PresentationReviewSurfaceActionPlan action,
        double minimumWidth,
        bool hasLeadingSpacing) =>
        new(kind, action.Label, action.IsEnabled, null, minimumWidth, hasLeadingSpacing);
}
