using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record PresentationReviewWorkflowSessionCallbacks(
    Action MarkDirty,
    Action RefreshCanvas,
    Action RefreshNotesPane,
    Action<PresentationAccessibilityCheckerPanePlan> RenderAccessibilityCheckerPaneIfVisible,
    Action<PresentationAccessibilityCheckerPanePlan> PresentAccessibilityCheckerPane,
    Action OpenAltTextPane,
    Action OpenHyperlinkDialog,
    Action OpenMediaCaptionPane,
    Action<PresentationCommentPanePlan> RenderCommentPane,
    Action<PresentationAltTextPanePlan> RenderAltTextPaneIfVisible,
    Action<PresentationReadingOrderPlan> RenderReadingOrderPaneIfVisible,
    Action<PresentationReadingOrderPlan> PresentReadingOrderPane,
    Action<PresentationProofingPanePlan> RenderProofingPaneIfVisible,
    Action<PresentationProofingPanePlan> PresentProofingPane,
    Action UpdateAfterCommentMutation,
    Action UpdateAfterCommentNavigation,
    Action UpdateAfterProofingCorrection);

public sealed record PresentationCommentMentionApplicationResult(
    PresentationCommentMentionInsertionPlan InsertionPlan,
    PresentationCommentMutationPlan? MutationPlan);

public sealed record PresentationCommentMentionDispatchResult(
    PresentationCommentMentionPickerPlan PickerPlan,
    PresentationCommentMentionApplicationResult? ApplicationResult)
{
    public bool ShouldShowPicker =>
        ApplicationResult is null && PickerPlan.HasCandidates;
}

/// <summary>
/// Renderer-neutral state and orchestration for the shared FreeP review workflow.
/// Hosts retain their dirty, status, canvas, notes, and pane-rendering callbacks.
/// </summary>
public sealed class PresentationReviewWorkflowSession
{
    private readonly Func<EditingSession> _getEditor;
    private readonly PresentationReviewWorkflowSessionCallbacks _callbacks;
    private readonly PresentationCustomDictionaryStore _dictionaryStore;

    public PresentationReviewWorkflowSession(
        Func<EditingSession> getEditor,
        PresentationReviewWorkflowSessionCallbacks callbacks,
        PresentationCustomDictionaryStore? dictionaryStore = null)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        // Defaults to an in-memory, non-persisting store so constructing a session in a unit test
        // never touches the real user data folder; production hosts opt into persistence by passing
        // PresentationCustomDictionaryStore.Load().
        _dictionaryStore = dictionaryStore ?? new PresentationCustomDictionaryStore(storePath: null);
        if (_dictionaryStore.Words.Count > 0)
            ProofingDictionaryState = new PresentationProofingDictionaryState([.. _dictionaryStore.Words]);
    }

    public int? SelectedCommentIndex { get; set; }

    public int? SelectedProofingIssueRowIndex { get; private set; }

    public PresentationProofingIgnoreState ProofingIgnoreState { get; private set; } =
        PresentationProofingIgnoreState.Empty;

    public PresentationProofingDictionaryState ProofingDictionaryState { get; private set; } =
        PresentationProofingDictionaryState.Empty;

    public PresentationCommentPanePlan? LastCommentPanePlan { get; private set; }

    public PresentationCommentNavigationPlan? LastCommentNavigationPlan { get; private set; }

    public PresentationCommentMentionPickerPlan? LastCommentMentionPickerPlan { get; private set; }

    public PresentationCommentMentionInsertionPlan? LastCommentMentionInsertionPlan { get; private set; }

    public PresentationMediaTranscriptPlan? LastMediaTranscriptPlan { get; private set; }

    public PresentationAccessibilitySummaryPlan? LastAccessibilitySummaryPlan { get; private set; }

    public PresentationAccessibilityCheckerPanePlan? LastAccessibilityCheckerPanePlan { get; private set; }

    public PresentationAccessibilityCheckerNavigationPlan? LastAccessibilityCheckerNavigationPlan { get; private set; }

    public PresentationSlideTitleMutationPlan? LastSlideTitleMutationPlan { get; private set; }

    public PresentationChartTitleMutationPlan? LastChartTitleMutationPlan { get; private set; }

    public PresentationTableHeaderRowMutationPlan? LastTableHeaderRowMutationPlan { get; private set; }

    public PresentationTableStructureReviewPlan? LastTableStructureReviewPlan { get; private set; }

    public PresentationTableStructureReviewDisplayPlan? LastTableStructureReviewDisplayPlan { get; private set; }

    public PresentationAltTextRequestPlan? LastAltTextRequestPlan { get; private set; }

    public PresentationAltTextPanePlan? LastAltTextPanePlan { get; private set; }

    public PresentationReadingOrderPlan? LastReadingOrderPlan { get; private set; }

    public PresentationProofingRequestPlan? LastProofingRequestPlan { get; private set; }

    public PresentationProofingExecutionPlan? LastProofingExecutionPlan { get; private set; }

    public PresentationProofingPanePlan? LastProofingPanePlan { get; private set; }

    public void RefreshReviewWorkflowPlans()
    {
        var editor = _getEditor();
        var presentation = editor.Presentation;
        LastCommentPanePlan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            presentation.Slides,
            editor.CurrentSlideIndex,
            SelectedCommentIndex);
        RefreshAccessibilityCheckerPlans();
        RefreshAltTextPlansCore(null, null, null);
        _callbacks.RenderAltTextPaneIfVisible(LastAltTextPanePlan!);
        RefreshReadingOrderPlan();
        RefreshProofingRequestPlan();
    }

    public PresentationCommentPanePlan ShowReviewCommentsPane()
    {
        LastCommentPanePlan = BuildCommentPanePlan();
        _callbacks.RenderCommentPane(LastCommentPanePlan);
        return LastCommentPanePlan;
    }

    public PresentationCommentPanePlan SetSelectedReviewCommentIndex(int? commentIndex)
    {
        SelectedCommentIndex = commentIndex;
        return ShowReviewCommentsPane();
    }

    public void SelectReviewComment(int commentIndex)
    {
        SelectedCommentIndex = commentIndex;
        ShowReviewCommentsPane();
        RefreshReviewWorkflowPlans();
    }

    public PresentationCommentPanePlan BuildCommentPanePlan()
    {
        var editor = _getEditor();
        LastCommentPanePlan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            editor.Presentation.Slides,
            editor.CurrentSlideIndex,
            SelectedCommentIndex);
        return LastCommentPanePlan;
    }

    public PresentationCommentNavigationPlan NavigateReviewComment(
        PresentationReviewWorkflowIntentKind intent)
    {
        var editor = _getEditor();
        var plan = PresentationReviewWorkflowPlanner.BuildCommentNavigationPlan(
            editor.Presentation.Slides,
            editor.CurrentSlideIndex,
            SelectedCommentIndex,
            intent);
        LastCommentNavigationPlan = plan;
        if (!plan.ShouldNavigate)
            return plan;

        if (editor.CurrentSlideIndex != plan.TargetSlideIndex)
            editor.SelectSlide(plan.TargetSlideIndex);

        SelectedCommentIndex = plan.TargetCommentIndex;
        ShowReviewCommentsPane();
        RefreshReviewWorkflowPlans();
        _callbacks.UpdateAfterCommentNavigation();
        return plan;
    }

    public PresentationCommentMutationPlan DeleteSelectedComment()
        => ApplySelectedCommentMutation(PresentationReviewWorkflowIntentKind.DeleteComment, null, null);

    public PresentationCommentMutationPlan AddComment(
        string? text,
        DateTime? timestamp = null,
        string? author = null,
        string? initials = null,
        long xemu = 0,
        long yemu = 0)
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.AddComment,
            null,
            null,
            addText: text,
            addTimestamp: timestamp,
            addAuthor: author,
            addInitials: initials,
            addXemu: xemu,
            addYemu: yemu);

    public PresentationCommentMutationPlan EditSelectedComment(
        string? text,
        string? author = null,
        string? initials = null)
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.EditComment,
            null,
            null,
            editText: text,
            editAuthor: author,
            editInitials: initials);

    public PresentationCommentMutationPlan ResolveSelectedComment(
        DateTime? resolvedAt = null,
        string? resolvedBy = null)
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.ResolveComment,
            resolvedAt,
            resolvedBy);

    public PresentationCommentMutationPlan ReopenSelectedComment()
        => ApplySelectedCommentMutation(PresentationReviewWorkflowIntentKind.ReopenComment, null, null);

    /// <summary>
    /// Resolves the real author identity to stamp on a new comment, a reply, or a resolution --
    /// the presentation's Properties.Author, falling back to the OS account name. Shared by both
    /// hosts (WPF and Avalonia) so neither shell carries its own copy of this resolution logic;
    /// callers fall through to null (and from there to the planner's own "FreeP User" default)
    /// only when neither source yields a usable name.
    /// </summary>
    public string? ResolveCommentAuthor()
    {
        var documentAuthor = _getEditor().Presentation.Properties.Author;
        if (!string.IsNullOrWhiteSpace(documentAuthor))
            return documentAuthor.Trim();

        var osAuthor = Environment.UserName;
        return string.IsNullOrWhiteSpace(osAuthor) ? null : osAuthor.Trim();
    }

    public PresentationCommentMutationPlan ReplyToSelectedComment(
        string? text,
        DateTime? timestamp = null,
        string? author = null,
        string? initials = null)
        => ApplySelectedCommentMutation(
            PresentationReviewWorkflowIntentKind.ReplyComment,
            null,
            null,
            text,
            timestamp,
            author,
            initials);

    public PresentationCommentMentionPickerPlan BuildCommentMentionPickerPlan(
        string? query = null,
        string? currentAuthor = null,
        string? currentInitials = null)
    {
        var editor = _getEditor();
        LastCommentMentionPickerPlan = PresentationReviewWorkflowPlanner.BuildCommentMentionPickerPlan(
            editor.Presentation.Slides,
            query,
            currentAuthor,
            currentInitials);
        return LastCommentMentionPickerPlan;
    }

    public PresentationCommentMentionPickerPlan BuildCommentMentionPickerPlanForInput(
        string? text,
        int caretIndex,
        string? currentAuthor = null,
        string? currentInitials = null)
    {
        var editor = _getEditor();
        LastCommentMentionPickerPlan =
            PresentationReviewWorkflowPlanner.BuildCommentMentionPickerPlanForInsertionContext(
                editor.Presentation.Slides,
                text,
                NormalizeCommentInputCaret(text, caretIndex),
                currentAuthor,
                currentInitials);
        return LastCommentMentionPickerPlan;
    }

    public PresentationCommentMentionDispatchResult DispatchCommentMentionPicker(
        PresentationReviewWorkflowIntentKind intent,
        string? text,
        int caretIndex,
        string? currentAuthor = null,
        string? currentInitials = null)
    {
        var picker = BuildCommentMentionPickerPlanForInput(
            text,
            caretIndex,
            currentAuthor,
            currentInitials);
        var application = picker.ShouldAutoApplyDefaultCandidate
            ? ApplyCommentMention(intent, text, caretIndex, picker.DefaultCandidate)
            : null;
        return new PresentationCommentMentionDispatchResult(picker, application);
    }

    public PresentationCommentMentionInsertionPlan InsertCommentMention(
        string? text,
        int caretIndex,
        PresentationCommentMentionCandidate? candidate)
    {
        LastCommentMentionInsertionPlan = PresentationReviewWorkflowPlanner.BuildCommentMentionInsertionPlan(
            text,
            caretIndex,
            candidate);
        return LastCommentMentionInsertionPlan;
    }

    public PresentationCommentMentionApplicationResult ApplyCommentMention(
        PresentationReviewWorkflowIntentKind intent,
        string? text,
        int caretIndex,
        PresentationCommentMentionCandidate? candidate)
    {
        if (intent is not PresentationReviewWorkflowIntentKind.EditComment and
            not PresentationReviewWorkflowIntentKind.ReplyComment)
        {
            throw new ArgumentOutOfRangeException(
                nameof(intent),
                intent,
                "Comment mention insertion supports edit and reply intents only.");
        }

        var insertion = InsertCommentMention(
            text,
            NormalizeCommentInputCaret(text, caretIndex),
            candidate);
        if (!insertion.ShouldApply)
            return new PresentationCommentMentionApplicationResult(insertion, null);

        var mutation = intent == PresentationReviewWorkflowIntentKind.EditComment
            ? EditSelectedComment(insertion.UpdatedText)
            : ReplyToSelectedComment(insertion.UpdatedText);
        return new PresentationCommentMentionApplicationResult(insertion, mutation);
    }

    public PresentationCommentMutationPlan InsertMentionInSelectedComment(
        int caretIndex,
        PresentationCommentMentionCandidate? candidate,
        string? author = null,
        string? initials = null)
    {
        LastCommentMentionInsertionPlan = InsertCommentMention(
            GetSelectedCommentText(),
            caretIndex,
            candidate);
        if (!LastCommentMentionInsertionPlan.ShouldApply)
        {
            var editor = _getEditor();
            return new PresentationCommentMutationPlan(
                PresentationReviewWorkflowIntentKind.EditComment,
                false,
                editor.CurrentSlideIndex,
                SelectedCommentIndex,
                null,
                LastCommentMentionInsertionPlan.ValidationMessage);
        }

        return EditSelectedComment(LastCommentMentionInsertionPlan.UpdatedText, author, initials);
    }

    private static int NormalizeCommentInputCaret(string? text, int caretIndex)
    {
        var length = (text ?? string.Empty).Length;
        return caretIndex == 0 && length > 0
            ? length
            : Math.Clamp(caretIndex, 0, length);
    }

    public string? GetSelectedCommentText()
        => SelectedCommentIndex is { } index ? GetCommentText(index) : null;

    public PresentationAccessibilityCheckerPanePlan RefreshAccessibilityCheckerPlans()
    {
        var presentation = _getEditor().Presentation;
        LastMediaTranscriptPlan = PresentationMediaTranscriptPlanner.BuildTranscriptPlan(presentation);
        LastAccessibilitySummaryPlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        LastAccessibilityCheckerPanePlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
                presentation,
                LastAccessibilitySummaryPlan,
                LastAccessibilityCheckerPanePlan?.SelectedRowIndex);
        _callbacks.RenderAccessibilityCheckerPaneIfVisible(LastAccessibilityCheckerPanePlan);
        return LastAccessibilityCheckerPanePlan;
    }

    public PresentationAccessibilityCheckerPanePlan ShowAccessibilityCheckerPane()
    {
        var plan = RefreshAccessibilityCheckerPlans();
        _callbacks.PresentAccessibilityCheckerPane(plan);
        return plan;
    }

    public PresentationAccessibilityCheckerPanePlan SelectAccessibilityCheckerRow(int rowIndex)
    {
        var current = RefreshAccessibilityCheckerPlans();
        var normalized = PresentationReviewWorkflowPlanner.NormalizeAccessibilityCheckerRowSelection(
            current,
            rowIndex);
        LastAccessibilityCheckerPanePlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
                _getEditor().Presentation,
                LastAccessibilitySummaryPlan!,
                normalized >= 0 ? normalized : null);
        LastAccessibilityCheckerNavigationPlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerNavigationPlan(
                LastAccessibilityCheckerPanePlan,
                normalized >= 0 ? normalized : null);
        ApplyAccessibilityCheckerNavigation(LastAccessibilityCheckerNavigationPlan);

        if (LastAccessibilityCheckerPanePlan.SelectedRow?.CommandHint !=
            PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId)
        {
            ClearTableStructureReview();
        }

        _callbacks.PresentAccessibilityCheckerPane(LastAccessibilityCheckerPanePlan);
        return LastAccessibilityCheckerPanePlan;
    }

    public PresentationAccessibilityCheckerPanePlan ApplyAccessibilityCheckerRowAction(int rowIndex)
    {
        var plan = SelectAccessibilityCheckerRow(rowIndex);
        var row = plan.SelectedRow;
        if (row?.CommandHint == PresentationReviewWorkflowPlanner.AltTextCommandId)
        {
            _callbacks.OpenAltTextPane();
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.SetSlideTitleCommandId)
        {
            LastSlideTitleMutationPlan =
                PresentationReviewWorkflowPlanner.TryApplySlideTitleMutation(_getEditor(), row.SlideIndex);
            RefreshAccessibilityCheckerPlans();
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.SetTableHeaderRowCommandId)
        {
            LastTableHeaderRowMutationPlan =
                PresentationReviewWorkflowPlanner.TryApplyTableHeaderRowMutation(
                    _getEditor(),
                    row.SlideIndex,
                    row.ShapeId);
            RefreshAccessibilityCheckerPlans();
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId)
        {
            OpenTableStructureReview(row);
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.InsertLinkCommandId)
        {
            _callbacks.OpenHyperlinkDialog();
        }
        else if (row?.CommandHint == PresentationReviewWorkflowPlanner.ChartTitleCommandId)
        {
            LastChartTitleMutationPlan =
                PresentationReviewWorkflowPlanner.TryApplyChartTitleMutation(
                    _getEditor(),
                    row.SlideIndex,
                    row.ShapeId);
            RefreshAccessibilityCheckerPlans();
        }
        else if (row?.CommandHint == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneOpenCommandId
            || row?.Category == "Media")
        {
            _callbacks.OpenMediaCaptionPane();
        }

        return LastAccessibilityCheckerPanePlan!;
    }

    private void OpenTableStructureReview(PresentationAccessibilityCheckerRowPlan row)
    {
        var presentation = _getEditor().Presentation;
        LastTableStructureReviewPlan = PresentationReviewWorkflowPlanner.BuildTableStructureReviewPlan(
            presentation,
            row.SlideIndex,
            row.ShapeId);
        LastTableStructureReviewDisplayPlan =
            PresentationReviewWorkflowPlanner.BuildTableStructureReviewDisplayPlan(
                LastTableStructureReviewPlan);
        RefreshAccessibilityCheckerPlans();
        LastAccessibilityCheckerPanePlan =
            PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
                presentation,
                LastAccessibilitySummaryPlan!,
                row.RowIndex);
        _callbacks.PresentAccessibilityCheckerPane(LastAccessibilityCheckerPanePlan);
    }

    private void ApplyAccessibilityCheckerNavigation(
        PresentationAccessibilityCheckerNavigationPlan plan)
    {
        if (!plan.ShouldNavigate)
            return;

        var editor = _getEditor();
        editor.SelectSlide(plan.TargetSlideIndex);
        if (plan.ShouldSelectShape && plan.TargetShapeId is { } shapeId)
            editor.Select(shapeId);
    }

    private void ClearTableStructureReview()
    {
        LastTableStructureReviewPlan = null;
        LastTableStructureReviewDisplayPlan = null;
    }

    public void RefreshAltTextPlans(
        string? proposedDescription,
        string? proposedTitle,
        bool? isDecorative)
        => RefreshAltTextPlansCore(proposedDescription, proposedTitle, isDecorative);

    public PresentationAltTextMutationPlan ApplySelectedShapeAlternativeText(
        string? description,
        string? title = null,
        bool isDecorative = false)
    {
        var editor = _getEditor();
        uint? selectedShapeId = editor.SelectedShapeIds.Count == 1
            ? editor.SelectedShapeIds[0]
            : null;
        var plan = PresentationReviewWorkflowPlanner.BuildAltTextMutationPlan(
            editor.CurrentSlide,
            editor.CurrentSlideIndex,
            selectedShapeId,
            description,
            title,
            isDecorative);
        if (plan.ShouldApply)
        {
            editor.SetSelectedShapeAlternativeText(plan.Description, plan.Title, plan.IsDecorative);
            LastAltTextRequestPlan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(
                editor.CurrentSlide,
                plan.ShapeId,
                plan.Description,
                plan.Title,
                plan.IsDecorative);
            LastAltTextPanePlan = PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(
                editor.CurrentSlide,
                plan.ShapeId,
                plan.Description,
                plan.Title,
                plan.IsDecorative);
            RefreshAccessibilityCheckerPlans();
        }

        return plan;
    }

    public PresentationReadingOrderPlan RefreshReadingOrderPlan()
    {
        var plan = RefreshReadingOrderPlanCore();
        _callbacks.RenderReadingOrderPaneIfVisible(plan);
        return plan;
    }

    public PresentationReadingOrderPlan ShowReadingOrderPane()
    {
        var plan = RefreshReadingOrderPlanCore();
        _callbacks.PresentReadingOrderPane(plan);
        return plan;
    }

    public PresentationReadingOrderMutationPlan ApplyReadingOrderMove(
        PresentationReviewWorkflowIntentKind intent)
    {
        var editor = _getEditor();
        var plan = PresentationReviewWorkflowPlanner.TryApplyReadingOrderMove(editor, intent);
        RefreshReadingOrderPlan();
        return plan;
    }

    public PresentationReadingOrderSelectionPlan SelectReadingOrderItem(uint shapeId)
    {
        var editor = _getEditor();
        var plan = PresentationReviewWorkflowPlanner.TryApplyReadingOrderSelection(editor, shapeId);
        RefreshReadingOrderPlan();
        return plan;
    }

    public PresentationProofingPanePlan ShowProofingPane()
    {
        RefreshProofingRequestPlan();
        _callbacks.PresentProofingPane(LastProofingPanePlan!);
        return LastProofingPanePlan!;
    }

    public PresentationProofingPanePlan SelectProofingIssueRow(int rowIndex)
    {
        RefreshProofingRequestPlan();
        var plan = LastProofingPanePlan!;
        var normalized = plan.Rows.Any(row => row.RowIndex == rowIndex)
            ? rowIndex
            : plan.SelectedRowIndex;
        SelectedProofingIssueRowIndex = normalized >= 0 ? normalized : null;
        LastProofingPanePlan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            LastProofingExecutionPlan!,
            SelectedProofingIssueRowIndex,
            ProofingIgnoreState,
            ProofingDictionaryState);
        _callbacks.PresentProofingPane(LastProofingPanePlan);
        return LastProofingPanePlan;
    }

    public PresentationProofingCorrectionMutationPlan ApplyProofingCorrection(
        PresentationProofingScopeDescriptor scope,
        int start,
        int length,
        string? replacement)
    {
        var editor = _getEditor();
        var plan = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            editor.Presentation,
            scope,
            start,
            length,
            replacement);
        if (plan.ShouldApply)
        {
            _callbacks.MarkDirty();
            _callbacks.RefreshCanvas();
            _callbacks.RefreshNotesPane();
            RefreshReviewWorkflowPlans();
            _callbacks.UpdateAfterProofingCorrection();
        }

        return plan;
    }

    public PresentationProofingCorrectionMutationPlan ApplySelectedProofingCorrection()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();

        if (LastProofingPanePlan?.SelectedRow is not { } selectedRow)
            return MissingProofingCorrectionPlan();

        var previousSelection = LastProofingPanePlan.SelectedRowIndex;
        var mutation = ApplyProofingCorrection(
            selectedRow.Scope,
            selectedRow.Start,
            selectedRow.Length,
            selectedRow.SuggestedReplacement);
        if (mutation.ShouldApply)
        {
            var refreshed = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
                LastProofingExecutionPlan!,
                ignoreState: ProofingIgnoreState,
                dictionaryState: ProofingDictionaryState);
            LastProofingPanePlan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
                LastProofingExecutionPlan!,
                PresentationReviewWorkflowPlanner.NormalizeProofingSelectionAfterCorrection(
                    previousSelection,
                    refreshed),
                ProofingIgnoreState,
                ProofingDictionaryState);
            SelectedProofingIssueRowIndex = LastProofingPanePlan.SelectedRowIndex >= 0
                ? LastProofingPanePlan.SelectedRowIndex
                : null;
            _callbacks.RenderProofingPaneIfVisible(LastProofingPanePlan);
        }

        return mutation;
    }

    public PresentationProofingPanePlan IgnoreSelectedProofingIssue()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();

        var previousSelection = LastProofingPanePlan!.SelectedRowIndex;
        ProofingIgnoreState = PresentationReviewWorkflowPlanner.AddProofingIgnoredIssue(
            ProofingIgnoreState,
            LastProofingPanePlan.SelectedRow);
        return RefreshProofingPaneAfterIgnore(previousSelection);
    }

    public PresentationProofingPanePlan IgnoreAllSelectedProofingIssues()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();

        var previousSelection = LastProofingPanePlan!.SelectedRowIndex;
        ProofingIgnoreState = PresentationReviewWorkflowPlanner.AddProofingIgnoredIssueGroup(
            ProofingIgnoreState,
            LastProofingPanePlan.SelectedRow);
        return RefreshProofingPaneAfterIgnore(previousSelection);
    }

    public PresentationProofingPanePlan AddSelectedProofingWordToDictionary()
    {
        if (LastProofingPanePlan is null)
            ShowProofingPane();

        var previousSelection = LastProofingPanePlan!.SelectedRowIndex;
        var previousWords = ProofingDictionaryState.NormalizedWords;
        ProofingDictionaryState = PresentationReviewWorkflowPlanner.AddProofingDictionaryWord(
            ProofingDictionaryState,
            LastProofingPanePlan.SelectedRow);
        if (ProofingDictionaryState.NormalizedWords.Count > previousWords.Count)
            _dictionaryStore.Add(ProofingDictionaryState.NormalizedWords[^1]);
        return RefreshProofingPaneAfterIgnore(previousSelection);
    }

    private PresentationProofingPanePlan RefreshProofingPaneAfterIgnore(int previousSelection)
    {
        var refreshed = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            LastProofingExecutionPlan!,
            ignoreState: ProofingIgnoreState,
            dictionaryState: ProofingDictionaryState);
        LastProofingPanePlan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            LastProofingExecutionPlan!,
            PresentationReviewWorkflowPlanner.NormalizeProofingSelectionAfterIgnore(
                previousSelection,
                refreshed),
            ProofingIgnoreState,
            ProofingDictionaryState);
        SelectedProofingIssueRowIndex = LastProofingPanePlan.SelectedRowIndex >= 0
            ? LastProofingPanePlan.SelectedRowIndex
            : null;
        _callbacks.RenderProofingPaneIfVisible(LastProofingPanePlan);
        return LastProofingPanePlan;
    }

    private void RefreshAltTextPlansCore(
        string? proposedDescription,
        string? proposedTitle,
        bool? isDecorative)
    {
        var editor = _getEditor();
        uint? selectedShapeId = editor.SelectedShapeIds.Count == 1
            ? editor.SelectedShapeIds[0]
            : null;
        LastAltTextRequestPlan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(
            editor.CurrentSlide,
            selectedShapeId,
            proposedDescription,
            proposedTitle,
            isDecorative);
        LastAltTextPanePlan = PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(
            editor.CurrentSlide,
            selectedShapeId,
            proposedDescription,
            proposedTitle,
            isDecorative);
    }

    private PresentationReadingOrderPlan RefreshReadingOrderPlanCore()
    {
        var editor = _getEditor();
        LastReadingOrderPlan = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(
            editor.CurrentSlide,
            editor.CurrentSlideIndex,
            editor.SelectedShapeIds);
        return LastReadingOrderPlan;
    }

    public void RefreshProofingRequestPlan()
    {
        var presentation = _getEditor().Presentation;
        LastProofingExecutionPlan = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        LastProofingRequestPlan = PresentationReviewWorkflowPlanner.BuildProofingRequestPlan(presentation);
        LastProofingPanePlan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            LastProofingExecutionPlan,
            SelectedProofingIssueRowIndex,
            ProofingIgnoreState,
            ProofingDictionaryState);
        SelectedProofingIssueRowIndex = LastProofingPanePlan.SelectedRowIndex >= 0
            ? LastProofingPanePlan.SelectedRowIndex
            : null;
        _callbacks.RenderProofingPaneIfVisible(LastProofingPanePlan);
    }

    private PresentationCommentMutationPlan ApplySelectedCommentMutation(
        PresentationReviewWorkflowIntentKind intent,
        DateTime? resolvedAt,
        string? resolvedBy,
        string? replyText = null,
        DateTime? replyTimestamp = null,
        string? replyAuthor = null,
        string? replyInitials = null,
        string? addText = null,
        DateTime? addTimestamp = null,
        string? addAuthor = null,
        string? addInitials = null,
        long addXemu = 0,
        long addYemu = 0,
        string? editText = null,
        string? editAuthor = null,
        string? editInitials = null)
    {
        var editor = _getEditor();
        var slides = editor.Presentation.Slides;
        var plan = PresentationCommentMutationService.BuildPlan(
            slides,
            new PresentationCommentMutationRequest(
                intent,
                editor.CurrentSlideIndex,
                SelectedCommentIndex,
                Text: intent switch
                {
                    PresentationReviewWorkflowIntentKind.AddComment => addText,
                    PresentationReviewWorkflowIntentKind.EditComment => editText,
                    PresentationReviewWorkflowIntentKind.ReplyComment => replyText,
                    _ => null
                },
                Timestamp: intent switch
                {
                    PresentationReviewWorkflowIntentKind.AddComment => addTimestamp,
                    PresentationReviewWorkflowIntentKind.ReplyComment => replyTimestamp,
                    _ => null
                },
                Author: intent switch
                {
                    PresentationReviewWorkflowIntentKind.AddComment => addAuthor,
                    PresentationReviewWorkflowIntentKind.EditComment => editAuthor,
                    PresentationReviewWorkflowIntentKind.ReplyComment => replyAuthor,
                    _ => null
                },
                Initials: intent switch
                {
                    PresentationReviewWorkflowIntentKind.AddComment => addInitials,
                    PresentationReviewWorkflowIntentKind.EditComment => editInitials,
                    PresentationReviewWorkflowIntentKind.ReplyComment => replyInitials,
                    _ => null
                },
                Xemu: addXemu,
                Yemu: addYemu,
                ResolvedAt: resolvedAt,
                ResolvedBy: resolvedBy));

        if (plan.ShouldApply)
        {
            var slide = plan.SlideIndex >= 0 && plan.SlideIndex < slides.Count
                ? slides[plan.SlideIndex]
                : null;
            if (slide is not null)
            {
                var isAdd = intent == PresentationReviewWorkflowIntentKind.AddComment;
                var isDelete = intent == PresentationReviewWorkflowIntentKind.DeleteComment;
                var index = isAdd ? slide.Comments.Count : plan.CommentIndex ?? -1;
                if (index >= 0)
                {
                    var before = isAdd
                        ? null
                        : index < slide.Comments.Count ? slide.Comments[index] : null;
                    var after = isDelete ? null : plan.Comment;
                    if (before is not null || after is not null)
                    {
                        editor.Bus.Execute(new CommentMutationCommand(
                            CommentMutationLabel(intent),
                            plan.SlideIndex,
                            index,
                            before,
                            after));

                        SelectedCommentIndex = PresentationReviewWorkflowPlanner.NormalizeCommentSelectionAfterMutation(
                            slides,
                            plan,
                            SelectedCommentIndex);
                        _callbacks.MarkDirty();
                        ShowReviewCommentsPane();
                        RefreshReviewWorkflowPlans();
                        _callbacks.UpdateAfterCommentMutation();
                    }
                }
            }
        }

        return plan;
    }

    private static string CommentMutationLabel(PresentationReviewWorkflowIntentKind intent) => intent switch
    {
        PresentationReviewWorkflowIntentKind.AddComment => "Add Comment",
        PresentationReviewWorkflowIntentKind.EditComment => "Edit Comment",
        PresentationReviewWorkflowIntentKind.DeleteComment => "Delete Comment",
        PresentationReviewWorkflowIntentKind.ResolveComment => "Resolve Comment",
        PresentationReviewWorkflowIntentKind.ReopenComment => "Reopen Comment",
        PresentationReviewWorkflowIntentKind.ReplyComment => "Reply to Comment",
        _ => "Edit Comment"
    };

    public string? GetCommentText(int commentIndex)
    {
        var comments = _getEditor().CurrentSlide?.Comments;
        return comments is not null && commentIndex >= 0 && commentIndex < comments.Count
            ? comments[commentIndex].Text
            : null;
    }

    private static PresentationProofingCorrectionMutationPlan MissingProofingCorrectionPlan()
        => new(
            false,
            new PresentationProofingScopeDescriptor(
                PresentationProofingScopeKind.SlideTitle,
                -1,
                null,
                null,
                null,
                null,
                null,
                string.Empty,
                string.Empty,
                string.Empty),
            0,
            0,
            string.Empty,
            null,
            PresentationReviewWorkflowPlanner.ProofingMissingIssueMessage);
}
