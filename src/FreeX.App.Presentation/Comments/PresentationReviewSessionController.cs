using FreeX.Core.Model;

namespace FreeX.App.Presentation.Comments;

public sealed record PresentationCommentMutationExecutionResult(
    bool Success,
    string? ErrorMessage = null,
    bool IsNoOp = false);

public interface IPresentationReviewSessionAdapter
{
    Workbook Workbook { get; }
    SheetId ActiveSheetId { get; }
    GridRange? SelectedRange { get; }
    string AuthorName { get; }
    Sheet? ActiveSheet => Workbook.GetSheet(ActiveSheetId);

    PresentationCommentMutationExecutionResult ApplyMutation(
        PresentationCommentMutationPlan plan,
        GridRange fallbackRange);

    void SelectCell(CellAddress address);
}

public sealed class PresentationReviewSessionAdapter : IPresentationReviewSessionAdapter
{
    private readonly Func<Workbook> _workbook;
    private readonly Func<SheetId> _activeSheetId;
    private readonly Func<GridRange?> _selectedRange;
    private readonly Func<string> _authorName;
    private readonly Func<PresentationCommentMutationPlan, GridRange, PresentationCommentMutationExecutionResult> _applyMutation;
    private readonly Action<CellAddress> _selectCell;

    public PresentationReviewSessionAdapter(
        Func<Workbook> workbook,
        Func<SheetId> activeSheetId,
        Func<GridRange?> selectedRange,
        Func<string> authorName,
        Func<PresentationCommentMutationPlan, GridRange, PresentationCommentMutationExecutionResult> applyMutation,
        Action<CellAddress> selectCell)
    {
        _workbook = workbook ?? throw new ArgumentNullException(nameof(workbook));
        _activeSheetId = activeSheetId ?? throw new ArgumentNullException(nameof(activeSheetId));
        _selectedRange = selectedRange ?? throw new ArgumentNullException(nameof(selectedRange));
        _authorName = authorName ?? throw new ArgumentNullException(nameof(authorName));
        _applyMutation = applyMutation ?? throw new ArgumentNullException(nameof(applyMutation));
        _selectCell = selectCell ?? throw new ArgumentNullException(nameof(selectCell));
    }

    public Workbook Workbook => _workbook();
    public SheetId ActiveSheetId => _activeSheetId();
    public GridRange? SelectedRange => _selectedRange();
    public string AuthorName => _authorName();

    public PresentationCommentMutationExecutionResult ApplyMutation(
        PresentationCommentMutationPlan plan,
        GridRange fallbackRange) =>
        _applyMutation(plan, fallbackRange);

    public void SelectCell(CellAddress address) => _selectCell(address);
}

public sealed record PresentationReviewEditTarget(
    CellAddress Address,
    string NoteText,
    ThreadedComment? ThreadedComment);

public sealed record PresentationReviewRefreshPlan(
    bool RefreshViewport,
    bool RefreshCommandStates,
    bool RefreshCommentPanes,
    bool SelectionChanged)
{
    public static PresentationReviewRefreshPlan None { get; } = new(false, false, false, false);
}

public sealed record PresentationReviewMutationResult(
    bool Success,
    string? ErrorMessage,
    bool IsNoOp,
    PresentationReviewRefreshPlan RefreshPlan);

public sealed record PresentationReviewNavigationResult(
    bool Success,
    CellAddress? Target,
    string? ErrorMessage,
    PresentationReviewRefreshPlan RefreshPlan);

public sealed class PresentationReviewSessionController
{
    private readonly IPresentationReviewSessionAdapter _adapter;
    private readonly PresentationCommentMutationService _mutationService;

    public PresentationReviewSessionController(
        IPresentationReviewSessionAdapter adapter,
        PresentationCommentMutationService? mutationService = null)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _mutationService = mutationService ?? new PresentationCommentMutationService();
    }

    public PresentationReviewEditTarget? GetSelectedNoteTarget()
    {
        if (SelectedAddress is not { } address)
            return null;

        var note = string.Empty;
        if (_adapter.ActiveSheet is null ||
            !_adapter.ActiveSheet.Comments.TryGetValue(address, out note))
        {
            return new PresentationReviewEditTarget(address, string.Empty, null);
        }

        return new PresentationReviewEditTarget(address, note, null);
    }

    public PresentationReviewEditTarget? GetSelectedThreadedCommentTarget()
    {
        if (SelectedAddress is not { } address)
            return null;

        ThreadedComment? comment = null;
        _adapter.ActiveSheet?.ThreadedComments.TryGetValue(address, out comment);
        return new PresentationReviewEditTarget(address, string.Empty, comment);
    }

    public PresentationReviewMutationResult ApplyNote(string text)
    {
        if (SelectedAddress is null)
            return Failed("Select a cell first.");

        return Apply(_mutationService.PlanSetNote(_adapter.ActiveSheetId, text));
    }

    public PresentationReviewMutationResult ApplyThreadedComment(ThreadedCommentDialogResult result)
    {
        if (SelectedAddress is not { } address)
            return Failed("Select a cell first.");

        ThreadedComment? existing = null;
        _adapter.ActiveSheet?.ThreadedComments.TryGetValue(address, out existing);
        var plan = _mutationService.PlanThreadedComment(
            _adapter.ActiveSheetId,
            existing,
            result,
            _adapter.AuthorName);
        return plan is null ? NoMutation() : Apply(plan);
    }

    public PresentationReviewMutationResult ResolveThreadedComment(bool resolved)
    {
        if (!HasThreadedCommentAtSelectionCore())
            return Failed("No threaded comment is selected.");

        return Apply(_mutationService.PlanResolveThreadedComment(_adapter.ActiveSheetId, resolved));
    }

    public PresentationReviewMutationResult DeleteNote()
    {
        if (!HasNoteAtSelectionCore())
            return Failed("No note is selected.");

        return Apply(_mutationService.PlanDeleteNote(_adapter.ActiveSheetId));
    }

    public PresentationReviewMutationResult DeleteThreadedComment()
    {
        if (!HasThreadedCommentAtSelectionCore())
            return Failed("No threaded comment is selected.");

        return Apply(_mutationService.PlanDeleteThreadedComment(_adapter.ActiveSheetId));
    }

    public PresentationReviewMutationResult ToggleNoteVisibility(CellAddress address) =>
        Apply(
            _mutationService.PlanToggleNoteVisibility(_adapter.ActiveSheetId),
            new GridRange(address, address));

    public PresentationReviewMutationResult ToggleAllNotesVisibility() =>
        Apply(_mutationService.PlanToggleAllNotesVisibility(_adapter.ActiveSheetId));

    public bool HasNoteAtSelection() => HasNoteAtSelectionCore();

    public bool HasThreadedCommentAtSelection() => HasThreadedCommentAtSelectionCore();

    public PresentationReviewMutationResult ConvertNotesToComments() =>
        Apply(_mutationService.PlanConvertNotesToComments(_adapter.ActiveSheetId));

    public PresentationReviewNavigationResult NavigateNote(bool previous) =>
        Navigate(
            _adapter.ActiveSheet is { } sheet
                ? CommentNavigationPlanner.OrderedNoteAddresses(sheet.Comments)
                : [],
            previous,
            "No notes on the active sheet.");

    public PresentationReviewNavigationResult NavigateThreadedComment(bool previous) =>
        Navigate(
            _adapter.ActiveSheet is { } sheet
                ? CommentNavigationPlanner.OrderedThreadedCommentAddresses(sheet.ThreadedComments)
                : [],
            previous,
            "No threaded comments on the active sheet.");

    public PresentationReviewRefreshPlan CreateRefreshPlan(
        bool mutationApplied = false,
        bool selectionChanged = false) =>
        new(
            RefreshViewport: mutationApplied || selectionChanged,
            RefreshCommandStates: mutationApplied,
            RefreshCommentPanes: mutationApplied,
            SelectionChanged: selectionChanged);

    private PresentationReviewMutationResult Apply(
        PresentationCommentMutationPlan plan,
        GridRange? targetRange = null)
    {
        var address = SelectedAddress ?? new CellAddress(_adapter.ActiveSheetId, 1, 1);
        var fallbackRange = targetRange ?? _adapter.SelectedRange ?? new GridRange(address, address);
        var result = _adapter.ApplyMutation(plan, fallbackRange);
        return new(
            result.Success,
            result.ErrorMessage,
            result.IsNoOp,
            result.Success ? CreateRefreshPlan(mutationApplied: true) : PresentationReviewRefreshPlan.None);
    }

    private PresentationReviewNavigationResult Navigate(
        IReadOnlyList<CellAddress> addresses,
        bool previous,
        string emptyMessage)
    {
        if (addresses.Count == 0)
        {
            return new(false, null, emptyMessage, PresentationReviewRefreshPlan.None);
        }

        var current = SelectedAddress ?? addresses[0];
        var target = CommentNavigationPlanner.FindNext(addresses, current, previous);
        _adapter.SelectCell(target);
        return new(true, target, null, CreateRefreshPlan(selectionChanged: true));
    }

    private PresentationReviewMutationResult Failed(string message) =>
        new(false, message, false, PresentationReviewRefreshPlan.None);

    private PresentationReviewMutationResult NoMutation() =>
        new(true, null, true, PresentationReviewRefreshPlan.None);

    private bool HasNoteAtSelectionCore() =>
        SelectedAddress is { } address && _adapter.ActiveSheet?.Comments.ContainsKey(address) == true;

    private bool HasThreadedCommentAtSelectionCore() =>
        SelectedAddress is { } address && _adapter.ActiveSheet?.ThreadedComments.ContainsKey(address) == true;

    private CellAddress? SelectedAddress => _adapter.SelectedRange?.Start;
}
