using Free.Shared.AppServices;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum PresentationWorkareaTransition
{
    Bootstrap,
    PresentationReplaced,
    EditorChanged,
    CurrentSlideChanged,
    SelectionChanged,
    ActiveTableCellChanged,
}

public enum PresentationWorkareaOperation
{
    BeforePresentationReplaced,
    BindEditor,
    ResetAnimationSession,
    HideTransientPickers,
    BeforeEditorChanged,
    MarkDirty,
    AfterEditorMarkedDirty,
    RefreshCommandStates,
    RefreshSlidePane,
    RefreshCanvas,
    RefreshNotesPane,
    RefreshDocumentStatusBeforeReview,
    RefreshReviewWorkflowPlans,
    RefreshSmartArtPane,
    RefreshAnimationPaneAfterEditorChanged,
    RefreshAnimationPaneAfterNavigation,
    RefreshAnimationPaneAfterSelection,
    RefreshAnimationPaneAfterPresentationChanged,
    RefreshSelectionPane,
    RefreshAccessibilityMetadata,
    RefreshDocumentStatusAfterReview,
    BeforeCurrentSlideChanged,
    ClearReviewSelection,
    ResetAnimationSelection,
    ClearMediaSelection,
    SyncSlidePaneSelection,
    RefreshSlidePaneChrome,
    RefreshReviewPaneBeforePlans,
    RefreshReviewPaneAfterPlans,
    RefreshVisibleMediaPane,
    RefreshCurrentSlideStatus,
    RefreshAltTextRequest,
    RefreshReadingOrder,
    RefreshAltTextPane,
}

public enum PresentationWorkareaPane
{
    ReviewComments,
    AccessibilityChecker,
    AltText,
    ReadingOrder,
    Proofing,
    MediaCaption,
    SmartArtText,
    Selection,
}

public enum PresentationWorkareaNativeCommand
{
    NewPresentation,
    OpenPresentation,
    SavePresentation,
    SavePresentationAs,
    PrintPresentation,
    StartSlideShowFromBeginning,
    StartSlideShowFromCurrentSlide,
    Copy,
    Cut,
    Paste,
    Find,
    Replace,
}

public enum PresentationWorkareaCommandTarget
{
    Editor,
    NativeEndpoint,
}

public enum PresentationWorkareaEditorCommand
{
    Undo,
    Redo,
    DeleteSelectedShapes,
    DuplicateCurrentSlide,
    SelectAll,
}

public sealed record PresentationWorkareaCommandRoute(
    PresentationWorkareaCommandTarget Target,
    PresentationWorkareaEditorCommand? EditorCommand,
    PresentationWorkareaNativeCommand? NativeCommand);

public static class PresentationWorkareaCommandRoutePlanner
{
    public static PresentationWorkareaCommandRoute Build(FreePKeyboardCommand command) => command switch
    {
        FreePKeyboardCommand.Undo => Editor(PresentationWorkareaEditorCommand.Undo),
        FreePKeyboardCommand.Redo => Editor(PresentationWorkareaEditorCommand.Redo),
        FreePKeyboardCommand.DeleteSelectedShapes =>
            Editor(PresentationWorkareaEditorCommand.DeleteSelectedShapes),
        FreePKeyboardCommand.DuplicateCurrentSlide =>
            Editor(PresentationWorkareaEditorCommand.DuplicateCurrentSlide),
        FreePKeyboardCommand.SelectAll => Editor(PresentationWorkareaEditorCommand.SelectAll),
        FreePKeyboardCommand.NewPresentation => Native(PresentationWorkareaNativeCommand.NewPresentation),
        FreePKeyboardCommand.OpenPresentation => Native(PresentationWorkareaNativeCommand.OpenPresentation),
        FreePKeyboardCommand.SavePresentation => Native(PresentationWorkareaNativeCommand.SavePresentation),
        FreePKeyboardCommand.SavePresentationAs => Native(PresentationWorkareaNativeCommand.SavePresentationAs),
        FreePKeyboardCommand.PrintPresentation => Native(PresentationWorkareaNativeCommand.PrintPresentation),
        FreePKeyboardCommand.StartSlideShowFromBeginning =>
            Native(PresentationWorkareaNativeCommand.StartSlideShowFromBeginning),
        FreePKeyboardCommand.StartSlideShowFromCurrentSlide =>
            Native(PresentationWorkareaNativeCommand.StartSlideShowFromCurrentSlide),
        FreePKeyboardCommand.Copy => Native(PresentationWorkareaNativeCommand.Copy),
        FreePKeyboardCommand.Cut => Native(PresentationWorkareaNativeCommand.Cut),
        FreePKeyboardCommand.Paste => Native(PresentationWorkareaNativeCommand.Paste),
        FreePKeyboardCommand.Find => Native(PresentationWorkareaNativeCommand.Find),
        FreePKeyboardCommand.Replace => Native(PresentationWorkareaNativeCommand.Replace),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    private static PresentationWorkareaCommandRoute Editor(PresentationWorkareaEditorCommand command) =>
        new(PresentationWorkareaCommandTarget.Editor, command, null);

    private static PresentationWorkareaCommandRoute Native(PresentationWorkareaNativeCommand command) =>
        new(PresentationWorkareaCommandTarget.NativeEndpoint, null, command);
}

public sealed record PresentationWorkareaSnapshot(
    Presentation Presentation,
    EditingSession Editor,
    Slide? CurrentSlide,
    int CurrentSlideIndex,
    int SlideCount,
    IReadOnlyList<uint> SelectedShapeIds);

public sealed record PresentationWorkareaContext(
    PresentationWorkareaTransition Transition,
    PresentationWorkareaSnapshot Snapshot);

public sealed record PresentationWorkareaOperationPlan(
    PresentationWorkareaTransition Transition,
    IReadOnlyList<PresentationWorkareaOperation> Operations);

public sealed record PresentationWorkareaStatusPlan(
    int CurrentSlideIndex,
    int SlideCount,
    string Text);

public sealed record PresentationWorkareaStatusRefreshPlan(
    bool RefreshTitle,
    bool RefreshSlideCount);

public static class PresentationWorkareaStatusRefreshPlanner
{
    public static PresentationWorkareaStatusRefreshPlan BuildBeforeReview(
        PresentationWorkareaTransition transition) => transition switch
        {
            PresentationWorkareaTransition.Bootstrap => new(true, false),
            PresentationWorkareaTransition.PresentationReplaced => new(false, true),
            PresentationWorkareaTransition.EditorChanged => new(true, true),
            _ => new(false, false),
        };

    public static PresentationWorkareaStatusRefreshPlan BuildAfterReview(
        PresentationWorkareaTransition transition) =>
        new(false, transition == PresentationWorkareaTransition.Bootstrap);
}

/// <summary>
/// Renderer endpoint for the native workarea controls and services. The portable session owns
/// sequencing and command meaning; endpoints only realize one requested operation at a time.
/// </summary>
public interface IPresentationWorkareaEndpoint
{
    void Apply(
        PresentationWorkareaOperation operation,
        PresentationWorkareaContext context);

    void ExecuteNativeCommand(PresentationWorkareaNativeCommand command);
}

public static class PresentationWorkareaOperationPlanner
{
    public static PresentationWorkareaOperationPlan BuildBootstrap() =>
        Plan(
            PresentationWorkareaTransition.Bootstrap,
            PresentationWorkareaOperation.BindEditor,
            PresentationWorkareaOperation.RefreshDocumentStatusBeforeReview,
            PresentationWorkareaOperation.RefreshSlidePane,
            PresentationWorkareaOperation.RefreshCanvas,
            PresentationWorkareaOperation.RefreshNotesPane,
            PresentationWorkareaOperation.RefreshReviewPaneBeforePlans,
            PresentationWorkareaOperation.RefreshReviewWorkflowPlans,
            PresentationWorkareaOperation.RefreshReviewPaneAfterPlans,
            PresentationWorkareaOperation.RefreshAnimationPaneAfterPresentationChanged,
            PresentationWorkareaOperation.RefreshDocumentStatusAfterReview);

    public static PresentationWorkareaOperationPlan BuildPresentationReplaced() =>
        Plan(
            PresentationWorkareaTransition.PresentationReplaced,
            PresentationWorkareaOperation.BindEditor,
            PresentationWorkareaOperation.ResetAnimationSession,
            PresentationWorkareaOperation.HideTransientPickers,
            PresentationWorkareaOperation.RefreshSlidePane,
            PresentationWorkareaOperation.RefreshCanvas,
            PresentationWorkareaOperation.RefreshDocumentStatusBeforeReview,
            PresentationWorkareaOperation.RefreshNotesPane,
            PresentationWorkareaOperation.RefreshReviewPaneBeforePlans,
            PresentationWorkareaOperation.RefreshReviewWorkflowPlans,
            PresentationWorkareaOperation.RefreshReviewPaneAfterPlans,
            PresentationWorkareaOperation.RefreshAnimationPaneAfterPresentationChanged,
            PresentationWorkareaOperation.RefreshDocumentStatusAfterReview);

    public static PresentationWorkareaOperationPlan BuildEditorChanged(bool isSmartArtPaneVisible)
    {
        var operations = new List<PresentationWorkareaOperation>
        {
            PresentationWorkareaOperation.BeforeEditorChanged,
            PresentationWorkareaOperation.MarkDirty,
            PresentationWorkareaOperation.AfterEditorMarkedDirty,
            PresentationWorkareaOperation.RefreshCommandStates,
            PresentationWorkareaOperation.RefreshSlidePane,
            PresentationWorkareaOperation.RefreshCanvas,
            PresentationWorkareaOperation.RefreshNotesPane,
            PresentationWorkareaOperation.RefreshDocumentStatusBeforeReview,
            PresentationWorkareaOperation.RefreshReviewWorkflowPlans,
        };
        if (isSmartArtPaneVisible)
            operations.Add(PresentationWorkareaOperation.RefreshSmartArtPane);
        operations.AddRange(
        [
            PresentationWorkareaOperation.RefreshAnimationPaneAfterEditorChanged,
            PresentationWorkareaOperation.RefreshSelectionPane,
            PresentationWorkareaOperation.RefreshAccessibilityMetadata,
            PresentationWorkareaOperation.RefreshDocumentStatusAfterReview,
        ]);
        return new(PresentationWorkareaTransition.EditorChanged, operations);
    }

    public static PresentationWorkareaOperationPlan BuildCurrentSlideChanged() =>
        Plan(
            PresentationWorkareaTransition.CurrentSlideChanged,
            PresentationWorkareaOperation.BeforeCurrentSlideChanged,
            PresentationWorkareaOperation.ClearReviewSelection,
            PresentationWorkareaOperation.ResetAnimationSelection,
            PresentationWorkareaOperation.ClearMediaSelection,
            PresentationWorkareaOperation.RefreshCommandStates,
            PresentationWorkareaOperation.SyncSlidePaneSelection,
            PresentationWorkareaOperation.RefreshSlidePaneChrome,
            PresentationWorkareaOperation.RefreshCanvas,
            PresentationWorkareaOperation.RefreshNotesPane,
            PresentationWorkareaOperation.RefreshReviewPaneBeforePlans,
            PresentationWorkareaOperation.RefreshReviewWorkflowPlans,
            PresentationWorkareaOperation.RefreshReviewPaneAfterPlans,
            PresentationWorkareaOperation.RefreshVisibleMediaPane,
            PresentationWorkareaOperation.RefreshAnimationPaneAfterNavigation,
            PresentationWorkareaOperation.RefreshSelectionPane,
            PresentationWorkareaOperation.RefreshAccessibilityMetadata,
            PresentationWorkareaOperation.RefreshCurrentSlideStatus);

    public static PresentationWorkareaOperationPlan BuildSelectionChanged(
        bool isAltTextPaneVisible,
        bool isSmartArtPaneVisible)
    {
        var operations = new List<PresentationWorkareaOperation>
        {
            PresentationWorkareaOperation.RefreshCommandStates,
            PresentationWorkareaOperation.RefreshAltTextRequest,
            PresentationWorkareaOperation.RefreshReadingOrder,
        };
        if (isAltTextPaneVisible)
            operations.Add(PresentationWorkareaOperation.RefreshAltTextPane);
        if (isSmartArtPaneVisible)
            operations.Add(PresentationWorkareaOperation.RefreshSmartArtPane);
        operations.AddRange(
        [
            PresentationWorkareaOperation.RefreshVisibleMediaPane,
            PresentationWorkareaOperation.RefreshAnimationPaneAfterSelection,
            PresentationWorkareaOperation.RefreshSelectionPane,
            PresentationWorkareaOperation.RefreshAccessibilityMetadata,
        ]);
        return new(PresentationWorkareaTransition.SelectionChanged, operations);
    }

    public static PresentationWorkareaOperationPlan BuildActiveTableCellChanged() =>
        Plan(
            PresentationWorkareaTransition.ActiveTableCellChanged,
            PresentationWorkareaOperation.RefreshCommandStates);

    private static PresentationWorkareaOperationPlan Plan(
        PresentationWorkareaTransition transition,
        params PresentationWorkareaOperation[] operations) =>
        new(transition, operations);
}

/// <summary>
/// Owns the active presentation/editor pair and all renderer-neutral workarea transitions.
/// </summary>
public sealed class PresentationWorkareaSession : IDisposable
{
    private readonly IPresentationWorkareaEndpoint _endpoint;
    private bool _initialized;
    private bool _disposed;

    public PresentationWorkareaSession(
        IPresentationWorkareaEndpoint endpoint,
        Presentation? presentation = null)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        Presentation = presentation ?? Presentation.CreateEmpty();
        Editor = CreateEditor(Presentation);
        SlidePaneSession = new PresentationSlidePaneSession(() => Editor);
        Attach(Editor);
    }

    public Presentation Presentation { get; private set; }

    public EditingSession Editor { get; private set; }

    public PresentationSlidePaneSession SlidePaneSession { get; }

    public PresentationWorkareaPaneSession Panes { get; } = new();

    public PresentationWorkareaSnapshot Snapshot => new(
        Presentation,
        Editor,
        Editor.CurrentSlide,
        Editor.CurrentSlideIndex,
        Presentation.Slides.Count,
        Editor.SelectedShapeIds.ToArray());

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized)
            return;

        _initialized = true;
        SlidePaneSession.ResetPresentation();
        Execute(PresentationWorkareaOperationPlanner.BuildBootstrap());
    }

    public void ReplacePresentation(Presentation presentation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(presentation);

        Apply(
            PresentationWorkareaOperation.BeforePresentationReplaced,
            PresentationWorkareaTransition.PresentationReplaced);
        Detach(Editor);
        Presentation = presentation;
        Editor = CreateEditor(presentation);
        Attach(Editor);
        SlidePaneSession.ResetPresentation();
        _initialized = true;
        Execute(PresentationWorkareaOperationPlanner.BuildPresentationReplaced());
    }

    public void ExecuteCommand(FreePKeyboardCommand command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var route = PresentationWorkareaCommandRoutePlanner.Build(command);
        switch (route.Target)
        {
            case PresentationWorkareaCommandTarget.Editor when route.EditorCommand is { } editorCommand:
                ExecuteEditorCommand(editorCommand);
                return;
            case PresentationWorkareaCommandTarget.NativeEndpoint when route.NativeCommand is { } nativeCommand:
                _endpoint.ExecuteNativeCommand(nativeCommand);
                return;
            default:
                throw new InvalidOperationException($"Invalid workarea command route for {command}.");
        }
    }

    private void ExecuteEditorCommand(PresentationWorkareaEditorCommand command)
    {
        switch (command)
        {
            case PresentationWorkareaEditorCommand.Undo:
                Editor.Undo();
                return;
            case PresentationWorkareaEditorCommand.Redo:
                Editor.Redo();
                return;
            case PresentationWorkareaEditorCommand.DeleteSelectedShapes:
                Editor.DeleteSelected();
                return;
            case PresentationWorkareaEditorCommand.DuplicateCurrentSlide:
                Editor.DuplicateCurrentSlide();
                return;
            case PresentationWorkareaEditorCommand.SelectAll:
                Editor.SelectAll();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }

    public PresentationWorkareaStatusPlan BuildStatusPlan(string? dataFolderLabel = null)
    {
        var current = SlidePaneSession.Status.ActiveSlideIndex;
        var count = SlidePaneSession.Status.SlideCount;
        return new(
            current,
            count,
            SisterAppStatusBarTextPlanner.FormatPresentationSlideStatus(
                current,
                count,
                dataFolderLabel ?? string.Empty));
    }

    public bool CanOpenDomainDialog(PresentationDomainDialogKind dialogKind) =>
        PresentationDomainDialogLaunchPlanner.CanOpen(Editor, dialogKind);

    public SlidePaneSessionChangePlan ApplySlidePaneSelectionGesture(
        int slideIndex,
        SlidePaneSelectionGesture gesture) =>
        ApplySlidePaneSelection(SlidePaneSession.ApplySelectionGesture(slideIndex, gesture));

    public SlidePaneSessionChangePlan ApplySlidePaneNativeSelection(
        IReadOnlyCollection<int> selectedSlideIndices,
        int activeSlideIndex) =>
        ApplySlidePaneSelection(SlidePaneSession.ApplyNativeSelection(
            selectedSlideIndices,
            activeSlideIndex));

    public SlidePaneSessionChangePlan ToggleSlidePaneSection(string sectionId)
    {
        var change = SlidePaneSession.ToggleSection(sectionId);
        if (change.ShouldRebuildItems)
            Apply(PresentationWorkareaOperation.RefreshSlidePane, PresentationWorkareaTransition.SelectionChanged);
        return change;
    }

    public SlidePaneContextCommandRoutePlan BuildSlidePaneContextCommandRoute(
        FreePContextMenuCommand command,
        int slideIndex,
        int sectionIndex) =>
        SlidePaneSession.BuildContextCommandRoute(command, slideIndex, sectionIndex);

    public bool ExecuteSlidePaneAction(
        SlidePaneActionKind kind,
        int contextSlideIndex,
        int targetInsertionIndex = -1)
    {
        var action = SlidePaneSession.BuildAction(kind, contextSlideIndex, targetInsertionIndex);
        return ExecuteSlidePaneAction(action);
    }

    public bool ExecuteSlidePaneKeyboardAction(SlidePaneKeyboardIntentKind intent) =>
        ExecuteSlidePaneAction(SlidePaneSession.BuildKeyboardAction(intent));

    public bool ExecuteSlidePaneSectionAction(
        SlideSectionActionExecutionPlan execution,
        string? promptedName = null)
    {
        var applied = SlidePaneSession.TryExecuteSectionAction(execution, promptedName);
        if (applied)
            RefreshSlidePaneAfterCommand();
        return applied;
    }

    public SlidePaneDragUpdatePlan BeginSlidePaneDrag(int sourceSlideIndex, double startPointerY) =>
        SlidePaneSession.BeginDrag(sourceSlideIndex, startPointerY);

    public SlidePaneDragUpdatePlan UpdateSlidePaneDrag(
        double pointerYWithinItem,
        double pointerYWithinPane,
        double slideItemHeight = SlidePanePlanner.DefaultSlideItemHeight,
        double nonSlideItemHeight = SlidePanePlanner.DefaultSectionHeaderHeight) =>
        SlidePaneSession.UpdateDrag(
            pointerYWithinItem,
            pointerYWithinPane,
            slideItemHeight,
            nonSlideItemHeight);

    public bool CompleteSlidePaneDrag(out bool shouldReleaseCapture)
    {
        var completion = SlidePaneSession.CompleteDrag();
        shouldReleaseCapture = completion.ShouldReleaseCapture;
        return completion.ShouldReleaseCapture && ExecuteSlidePaneAction(completion.Action);
    }

    public SlidePaneSessionChangePlan CancelSlidePaneDrag() =>
        SlidePaneSession.CancelDrag();

    public void Dispose()
    {
        if (_disposed)
            return;

        Detach(Editor);
        _disposed = true;
    }

    private static EditingSession CreateEditor(Presentation presentation) =>
        new(presentation, new PresentationCommandBus(presentation));

    private void Attach(EditingSession editor)
    {
        editor.Changed += HandleEditorChanged;
        editor.CurrentSlideChanged += HandleCurrentSlideChanged;
        editor.SelectionChanged += HandleSelectionChanged;
        editor.ActiveTableCellChanged += HandleActiveTableCellChanged;
    }

    private void Detach(EditingSession editor)
    {
        editor.Changed -= HandleEditorChanged;
        editor.CurrentSlideChanged -= HandleCurrentSlideChanged;
        editor.SelectionChanged -= HandleSelectionChanged;
        editor.ActiveTableCellChanged -= HandleActiveTableCellChanged;
    }

    private void HandleEditorChanged()
    {
        SlidePaneSession.RefreshFromEditorChange();
        Execute(PresentationWorkareaOperationPlanner.BuildEditorChanged(
            Panes.IsVisible(PresentationWorkareaPane.SmartArtText)));
    }

    private void HandleCurrentSlideChanged(object? sender, EventArgs e)
    {
        SlidePaneSession.SynchronizeEditorActiveSlide();
        Execute(PresentationWorkareaOperationPlanner.BuildCurrentSlideChanged());
    }

    private void HandleSelectionChanged(object? sender, EventArgs e) =>
        Execute(PresentationWorkareaOperationPlanner.BuildSelectionChanged(
            Panes.IsVisible(PresentationWorkareaPane.AltText),
            Panes.IsVisible(PresentationWorkareaPane.SmartArtText)));

    private void HandleActiveTableCellChanged(object? sender, EventArgs e) =>
        Execute(PresentationWorkareaOperationPlanner.BuildActiveTableCellChanged());

    private void Execute(PresentationWorkareaOperationPlan plan)
    {
        var context = new PresentationWorkareaContext(plan.Transition, Snapshot);
        foreach (var operation in plan.Operations)
            _endpoint.Apply(operation, context);
    }

    private void Apply(
        PresentationWorkareaOperation operation,
        PresentationWorkareaTransition transition) =>
        _endpoint.Apply(operation, new PresentationWorkareaContext(transition, Snapshot));

    private SlidePaneSessionChangePlan ApplySlidePaneSelection(SlidePaneSessionChangePlan change)
    {
        if (change.Kind == SlidePaneSessionChangeKind.None)
            return change;

        var activeSlideIndex = change.Projection.Selection.ActiveSlideIndex;
        if (activeSlideIndex >= 0 && activeSlideIndex != Editor.CurrentSlideIndex)
        {
            Editor.SelectSlide(activeSlideIndex);
            return change;
        }

        Apply(PresentationWorkareaOperation.SyncSlidePaneSelection, PresentationWorkareaTransition.SelectionChanged);
        Apply(PresentationWorkareaOperation.RefreshSlidePaneChrome, PresentationWorkareaTransition.SelectionChanged);
        return change;
    }

    private bool ExecuteSlidePaneAction(SlidePaneSelectionActionPlan action)
    {
        var applied = SlidePaneSession.TryExecuteAction(action);
        if (applied)
            RefreshSlidePaneAfterCommand();
        return applied;
    }

    private void RefreshSlidePaneAfterCommand()
    {
        SlidePaneSession.RefreshFromEditorChange();
        Apply(PresentationWorkareaOperation.RefreshSlidePane, PresentationWorkareaTransition.EditorChanged);
    }
}
