using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlidePaneSelectionGesture
{
    Replace,
    Toggle,
    Range,
    AddRange,
}

public enum SlidePaneSessionChangeKind
{
    None,
    Projection,
    Selection,
    Drag,
}

public sealed record SlidePaneSelectionSnapshot(
    int ActiveSlideIndex,
    int AnchorSlideIndex,
    IReadOnlyList<int> SelectedSlideIndices)
{
    public bool IsSelected(int slideIndex) =>
        SelectedSlideIndices.BinarySearch(slideIndex) >= 0;
}

public sealed record SlidePanePreviewMetadata(
    string SlideId,
    int SlideIndex,
    string Title,
    int ShapeCount,
    bool IsHidden,
    bool IsSelected,
    bool IsActive);

public sealed record PresentationSlidePaneItemProjection(
    int AccessibilityOrdinal,
    SlidePaneEntry Entry,
    SlidePaneThumbnailVisualPlan? Thumbnail,
    SlidePaneSectionHeaderVisualPlan? SectionHeader,
    SlidePanePreviewMetadata? Preview);

public sealed record SlidePaneStatusPlan(
    int ActiveSlideIndex,
    int SlideCount,
    int SelectedSlideCount,
    string Text,
    string SelectionText);

public sealed record PresentationSlidePaneProjection(
    long Revision,
    SlidePaneSessionProjection Layout,
    SlidePaneSelectionSnapshot Selection,
    IReadOnlyList<PresentationSlidePaneItemProjection> Items,
    SlidePaneBottomAffordancePlan BottomAffordance,
    SlidePaneStatusPlan Status)
{
    public IReadOnlyList<bool> PaneItemIsSlide => Layout.PaneItemIsSlide;
}

public sealed record SlidePaneSessionChangePlan(
    SlidePaneSessionChangeKind Kind,
    PresentationSlidePaneProjection Projection,
    bool ShouldRebuildItems,
    bool ShouldSyncSelection,
    bool ShouldRefreshChrome,
    bool ShouldScrollActiveIntoView);

public sealed record SlidePaneSelectionActionPlan(
    SlidePaneActionKind Kind,
    IReadOnlyList<int> SourceSlideIndices,
    int TargetInsertionIndex,
    bool IsEnabled,
    bool? TargetHiddenState = null);

public sealed record PresentationSlidePaneDragCompletion(
    SlidePaneSelectionActionPlan Action,
    bool ShouldReleaseCapture);

/// <summary>
/// Owns slide-pane projection, identity-stable selection, command planning, and drag state.
/// Renderers only realize the projection and translate native selection and pointer events.
/// </summary>
public sealed class PresentationSlidePaneSession
{
    private readonly Func<EditingSession> _getEditor;
    private readonly HashSet<string> _selectedSlideIds = new(StringComparer.OrdinalIgnoreCase);
    private SlidePaneSessionState _plannerState = SlidePaneSessionState.Empty;
    private string? _activeSlideId;
    private string? _anchorSlideId;
    private string? _dragSourceSlideId;
    private long _revision;

    public PresentationSlidePaneSession(Func<EditingSession> getEditor)
    {
        _getEditor = getEditor ?? throw new ArgumentNullException(nameof(getEditor));
        Projection = BuildProjection();
    }

    public PresentationSlidePaneProjection Projection { get; private set; }

    public SlidePaneSelectionSnapshot Selection => Projection.Selection;

    public SlidePaneStatusPlan Status => Projection.Status;

    public SlidePaneSessionChangePlan ResetPresentation()
    {
        _plannerState = SlidePaneSessionState.Empty;
        _selectedSlideIds.Clear();
        _activeSlideId = null;
        _anchorSlideId = null;
        _dragSourceSlideId = null;
        SelectEditorSlideWhenSelectionIsEmpty();
        return Rebuild(SlidePaneSessionChangeKind.Projection, rebuildItems: true, scrollActive: true);
    }

    public SlidePaneSessionChangePlan RefreshFromEditorChange()
    {
        ReconcileSelectionWithPresentation();
        return Rebuild(SlidePaneSessionChangeKind.Projection, rebuildItems: true, scrollActive: false);
    }

    public SlidePaneSessionChangePlan SynchronizeEditorActiveSlide()
    {
        var editor = _getEditor();
        var slides = editor.Presentation.Slides;
        if (!IsValidSlideIndex(slides.Count, editor.CurrentSlideIndex))
            return RefreshFromEditorChange();

        var activeId = slides[editor.CurrentSlideIndex].Id;
        if (!StringComparer.OrdinalIgnoreCase.Equals(_activeSlideId, activeId))
        {
            _selectedSlideIds.Clear();
            _selectedSlideIds.Add(activeId);
            _activeSlideId = activeId;
            _anchorSlideId = activeId;
        }
        else
        {
            _selectedSlideIds.Add(activeId);
        }

        return Rebuild(SlidePaneSessionChangeKind.Selection, rebuildItems: false, scrollActive: true);
    }

    public SlidePaneSessionChangePlan ApplySelectionGesture(
        int slideIndex,
        SlidePaneSelectionGesture gesture)
    {
        var slides = _getEditor().Presentation.Slides;
        if (!IsValidSlideIndex(slides.Count, slideIndex))
            return Unchanged();

        var slideId = slides[slideIndex].Id;
        var activateClickedSlide = true;
        switch (gesture)
        {
            case SlidePaneSelectionGesture.Toggle:
                activateClickedSlide = ToggleSelection(slideId);
                break;
            case SlidePaneSelectionGesture.Range:
                SelectRange(slideIndex, additive: false);
                break;
            case SlidePaneSelectionGesture.AddRange:
                SelectRange(slideIndex, additive: true);
                break;
            default:
                SelectOnly(slideId);
                break;
        }

        if (activateClickedSlide)
            _activeSlideId = slideId;
        if (gesture is SlidePaneSelectionGesture.Replace or SlidePaneSelectionGesture.Toggle)
            _anchorSlideId = slideId;
        EnsureActiveSelection();
        return Rebuild(SlidePaneSessionChangeKind.Selection, rebuildItems: false, scrollActive: false);
    }

    public SlidePaneSessionChangePlan ApplyNativeSelection(
        IReadOnlyCollection<int> selectedSlideIndices,
        int activeSlideIndex)
    {
        ArgumentNullException.ThrowIfNull(selectedSlideIndices);

        var slides = _getEditor().Presentation.Slides;
        var normalized = selectedSlideIndices
            .Where(index => IsValidSlideIndex(slides.Count, index))
            .Distinct()
            .Order()
            .ToArray();
        if (!IsValidSlideIndex(slides.Count, activeSlideIndex))
            activeSlideIndex = normalized.LastOrDefault(-1);
        if (!IsValidSlideIndex(slides.Count, activeSlideIndex))
            return Unchanged();

        _selectedSlideIds.Clear();
        foreach (var index in normalized)
            _selectedSlideIds.Add(slides[index].Id);
        _activeSlideId = slides[activeSlideIndex].Id;
        _selectedSlideIds.Add(_activeSlideId);

        if (_anchorSlideId is null || !_selectedSlideIds.Contains(_anchorSlideId))
            _anchorSlideId = _activeSlideId;

        return Rebuild(SlidePaneSessionChangeKind.Selection, rebuildItems: false, scrollActive: false);
    }

    public SlidePaneSessionChangePlan ToggleSection(string sectionId)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
            return Unchanged();

        _plannerState = SlidePanePlanner.ToggleSection(_plannerState, sectionId);
        return Rebuild(SlidePaneSessionChangeKind.Projection, rebuildItems: true, scrollActive: false);
    }

    public SlidePaneContextCommandRoutePlan BuildContextCommandRoute(
        FreePContextMenuCommand command,
        int slideIndex,
        int sectionIndex) =>
        SlidePanePlanner.BuildContextCommandRoute(
            command,
            _getEditor().Presentation.Slides,
            _getEditor().Presentation.Sections,
            slideIndex,
            sectionIndex);

    public SlidePaneSelectionActionPlan BuildAction(
        SlidePaneActionKind kind,
        int contextSlideIndex,
        int targetInsertionIndex = -1)
    {
        var slides = _getEditor().Presentation.Slides;
        var sources = ResolveActionSources(contextSlideIndex, kind);
        var validSources = sources.Count > 0 && sources.All(index => IsValidSlideIndex(slides.Count, index));

        return kind switch
        {
            SlidePaneActionKind.InsertAfterSlide => new(
                kind,
                sources.Take(1).ToArray(),
                contextSlideIndex + 1,
                validSources),
            SlidePaneActionKind.DuplicateSlide => new(
                kind,
                sources,
                targetInsertionIndex,
                validSources),
            SlidePaneActionKind.DeleteSlide => new(
                kind,
                sources,
                targetInsertionIndex,
                validSources && sources.Count < slides.Count),
            SlidePaneActionKind.ToggleHiddenSlide => new(
                kind,
                sources,
                targetInsertionIndex,
                validSources,
                validSources ? !slides[contextSlideIndex].IsHidden : null),
            SlidePaneActionKind.MoveSlide => new(
                kind,
                sources,
                targetInsertionIndex,
                validSources && CanMoveSelection(slides, sources, targetInsertionIndex)),
            _ => new(kind, sources, targetInsertionIndex, false),
        };
    }

    public SlidePaneSelectionActionPlan BuildKeyboardAction(SlidePaneKeyboardIntentKind intent)
    {
        var active = Selection.ActiveSlideIndex;
        var count = _getEditor().Presentation.Slides.Count;
        return intent switch
        {
            SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide =>
                BuildAction(SlidePaneActionKind.InsertAfterSlide, active),
            SlidePaneKeyboardIntentKind.DuplicateCurrentSlide =>
                BuildAction(SlidePaneActionKind.DuplicateSlide, active),
            SlidePaneKeyboardIntentKind.DeleteCurrentSlide =>
                BuildAction(SlidePaneActionKind.DeleteSlide, active),
            SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier =>
                BuildAction(SlidePaneActionKind.MoveSlide, active, Selection.SelectedSlideIndices.Min() - 1),
            SlidePaneKeyboardIntentKind.MoveCurrentSlideLater =>
                BuildAction(SlidePaneActionKind.MoveSlide, active, Selection.SelectedSlideIndices.Max() + 2),
            _ => new(SlidePaneActionKind.MoveSlide, [], active, false),
        };
    }

    public bool TryExecuteAction(SlidePaneSelectionActionPlan action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!action.IsEnabled)
            return false;

        return action.Kind switch
        {
            SlidePaneActionKind.InsertAfterSlide => ExecuteInsert(action),
            SlidePaneActionKind.DuplicateSlide => ExecuteDuplicate(action),
            SlidePaneActionKind.DeleteSlide => ExecuteDelete(action),
            SlidePaneActionKind.ToggleHiddenSlide => ExecuteToggleHidden(action),
            SlidePaneActionKind.MoveSlide => ExecuteMove(action),
            _ => false,
        };
    }

    public bool TryExecuteSectionAction(
        SlideSectionActionExecutionPlan execution,
        string? promptedName = null) =>
        SlideSectionPlanner.TryApplyAction(_getEditor(), execution, promptedName);

    public SlidePaneDragUpdatePlan BeginDrag(int sourceSlideIndex, double startPointerY)
    {
        var visibleSlideIndices = VisibleSlideIndices();
        var visibleSourceIndex = visibleSlideIndices.IndexOf(sourceSlideIndex);
        _dragSourceSlideId = IsValidSlideIndex(_getEditor().Presentation.Slides.Count, sourceSlideIndex)
            ? _getEditor().Presentation.Slides[sourceSlideIndex].Id
            : null;
        _plannerState = _plannerState with
        {
            DragSession = SlidePanePlanner.BeginDragSession(visibleSourceIndex, startPointerY),
        };
        Rebuild(SlidePaneSessionChangeKind.Drag, rebuildItems: false, scrollActive: false);
        return new SlidePaneDragUpdatePlan(
            _plannerState.DragSession,
            TranslateDropVisual(SlidePanePlanner.BuildDropVisualPlan(
                Projection.PaneItemIsSlide,
                visibleSourceIndex,
                visibleSourceIndex,
                SlidePanePlanner.DefaultSlideItemHeight)),
            false);
    }

    public SlidePaneDragUpdatePlan UpdateDrag(
        double pointerYWithinItem,
        double pointerYWithinPane,
        double slideItemHeight = SlidePanePlanner.DefaultSlideItemHeight,
        double nonSlideItemHeight = SlidePanePlanner.DefaultSectionHeaderHeight)
    {
        var update = SlidePanePlanner.UpdateDragSession(
            _plannerState.DragSession,
            Projection.PaneItemIsSlide,
            pointerYWithinItem,
            pointerYWithinPane,
            slideItemHeight,
            nonSlideItemHeight);
        _plannerState = _plannerState with { DragSession = update.State };
        Rebuild(SlidePaneSessionChangeKind.Drag, rebuildItems: false, scrollActive: false);
        return update with { DropVisualPlan = TranslateDropVisual(update.DropVisualPlan) };
    }

    public PresentationSlidePaneDragCompletion CompleteDrag()
    {
        var visibleTarget = _plannerState.DragSession.TargetSlideIndex;
        var completion = SlidePanePlanner.CompleteDragSession(
            _plannerState.DragSession,
            VisibleSlideIndices().Count);
        var sourceIndex = IndexOfSlideId(_dragSourceSlideId);
        var modelTarget = MapVisibleInsertionToModelIndex(visibleTarget);
        _plannerState = _plannerState with { DragSession = completion.State };
        _dragSourceSlideId = null;
        Rebuild(SlidePaneSessionChangeKind.Drag, rebuildItems: false, scrollActive: false);
        return new(
            BuildAction(SlidePaneActionKind.MoveSlide, sourceIndex, modelTarget),
            completion.ShouldReleaseCapture);
    }

    public SlidePaneSessionChangePlan CancelDrag()
    {
        _plannerState = _plannerState with
        {
            DragSession = SlidePanePlanner.CancelDragSession(_plannerState.DragSession),
        };
        _dragSourceSlideId = null;
        return Rebuild(SlidePaneSessionChangeKind.Drag, rebuildItems: false, scrollActive: false);
    }

    private bool ExecuteInsert(SlidePaneSelectionActionPlan action)
    {
        var editor = _getEditor();
        var primary = SlidePanePlanner.BuildContextActions(
                editor.Presentation.Slides.Count,
                action.SourceSlideIndices[0])
            .Single(candidate => candidate.Kind == SlidePaneActionKind.InsertAfterSlide);
        var applied = SlidePanePlanner.TryApplyAction(editor, primary);
        if (applied)
            SelectOnly(editor.CurrentSlide?.Id);
        return applied;
    }

    private bool ExecuteDuplicate(SlidePaneSelectionActionPlan action)
    {
        var editor = _getEditor();
        if (action.SourceSlideIndices.Count == 1)
        {
            var source = action.SourceSlideIndices[0];
            var primary = SlidePanePlanner.BuildContextActions(editor.Presentation.Slides.Count, source)
                .Single(candidate => candidate.Kind == SlidePaneActionKind.DuplicateSlide);
            var applied = SlidePanePlanner.TryApplyAction(editor, primary);
            if (applied)
                SelectOnly(editor.CurrentSlide?.Id);
            return applied;
        }

        var sourceIds = action.SourceSlideIndices
            .Select(index => editor.Presentation.Slides[index].Id)
            .ToArray();
        var commands = action.SourceSlideIndices
            .Order()
            .Select((index, offset) => (IPresentationCommand)new DuplicateSlideCommand(index + offset))
            .ToArray();
        editor.Bus.Execute(new BatchCommand("Duplicate Slides", commands));

        var duplicateIds = sourceIds
            .Select(id => editor.Presentation.Slides[IndexOfSlideId(id) + 1].Id)
            .ToArray();
        SetSelectedSlideIds(duplicateIds);
        _activeSlideId = duplicateIds[Math.Max(0, Array.IndexOf(sourceIds, _activeSlideId))];
        _anchorSlideId = _activeSlideId;
        editor.SelectSlide(IndexOfSlideId(_activeSlideId));
        return true;
    }

    private bool ExecuteDelete(SlidePaneSelectionActionPlan action)
    {
        var editor = _getEditor();
        if (action.SourceSlideIndices.Count == 1)
        {
            var source = action.SourceSlideIndices[0];
            var primary = SlidePanePlanner.BuildContextActions(editor.Presentation.Slides.Count, source)
                .Single(candidate => candidate.Kind == SlidePaneActionKind.DeleteSlide);
            var applied = SlidePanePlanner.TryApplyAction(editor, primary);
            if (applied)
                SelectOnly(editor.CurrentSlide?.Id);
            return applied;
        }

        var nextActiveIndex = Math.Min(action.SourceSlideIndices.Min(), editor.Presentation.Slides.Count - action.SourceSlideIndices.Count - 1);
        var commands = action.SourceSlideIndices
            .OrderDescending()
            .Select(index => (IPresentationCommand)new DeleteSlideCommand(index))
            .ToArray();
        editor.Bus.Execute(new BatchCommand("Delete Slides", commands));
        nextActiveIndex = Math.Clamp(nextActiveIndex, 0, editor.Presentation.Slides.Count - 1);
        SelectOnly(editor.Presentation.Slides[nextActiveIndex].Id);
        editor.SelectSlide(nextActiveIndex);
        return true;
    }

    private bool ExecuteToggleHidden(SlidePaneSelectionActionPlan action)
    {
        var editor = _getEditor();
        if (action.SourceSlideIndices.Count == 1)
        {
            var primary = SlidePanePlanner.BuildHiddenSlideAction(
                editor.Presentation.Slides,
                action.SourceSlideIndices[0]);
            return SlidePanePlanner.TryApplyAction(editor, primary);
        }

        var target = action.TargetHiddenState == true;
        var commands = action.SourceSlideIndices
            .Where(index => editor.Presentation.Slides[index].IsHidden != target)
            .Select(index => (IPresentationCommand)new SetSlideHiddenCommand(index, target))
            .ToArray();
        if (commands.Length == 0)
            return false;
        editor.Bus.Execute(new BatchCommand(target ? "Hide Slides" : "Show Slides", commands));
        return true;
    }

    private bool ExecuteMove(SlidePaneSelectionActionPlan action)
    {
        var editor = _getEditor();
        if (action.SourceSlideIndices.Count == 1)
        {
            var primary = SlidePanePlanner.PlanMoveAction(
                editor.Presentation.Slides.Count,
                action.SourceSlideIndices[0],
                action.TargetInsertionIndex);
            var applied = SlidePanePlanner.TryApplyAction(editor, primary);
            if (applied)
                SelectOnly(editor.CurrentSlide?.Id);
            return applied;
        }

        var selectedIds = action.SourceSlideIndices
            .Select(index => editor.Presentation.Slides[index].Id)
            .ToArray();
        var desiredOrder = BuildMovedOrder(
            editor.Presentation.Slides.Select(slide => slide.Id).ToArray(),
            selectedIds,
            action.TargetInsertionIndex);
        var simulatedOrder = editor.Presentation.Slides.Select(slide => slide.Id).ToList();
        var commands = new List<IPresentationCommand>();
        for (var targetIndex = 0; targetIndex < desiredOrder.Count; targetIndex++)
        {
            var currentIndex = simulatedOrder.IndexOf(desiredOrder[targetIndex]);
            if (currentIndex == targetIndex)
                continue;

            commands.Add(new MoveSlideCommand(currentIndex, targetIndex));
            var id = simulatedOrder[currentIndex];
            simulatedOrder.RemoveAt(currentIndex);
            simulatedOrder.Insert(targetIndex, id);
        }

        if (commands.Count == 0)
            return false;
        editor.Bus.Execute(new BatchCommand("Move Slides", commands));
        SetSelectedSlideIds(selectedIds);
        EnsureActiveSelection();
        editor.SelectSlide(IndexOfSlideId(_activeSlideId));
        return true;
    }

    private PresentationSlidePaneProjection BuildProjection()
    {
        ReconcileSelectionWithPresentation();
        var editor = _getEditor();
        var slides = editor.Presentation.Slides;
        var selection = BuildSelectionSnapshot(slides);
        _plannerState = SlidePanePlanner.SetSelectedSlide(_plannerState, selection.ActiveSlideIndex);
        var layout = SlidePanePlanner.BuildSessionProjection(
            slides,
            editor.Presentation.Sections,
            _plannerState);
        var items = new List<PresentationSlidePaneItemProjection>(layout.Entries.Count);

        for (var ordinal = 0; ordinal < layout.Entries.Count; ordinal++)
        {
            var entry = layout.Entries[ordinal];
            if (entry.Kind == SlidePaneEntryKind.SectionHeader)
            {
                items.Add(new(
                    ordinal,
                    entry,
                    null,
                    SlidePanePlanner.BuildSectionHeaderVisualPlan(entry),
                    null));
                continue;
            }

            var slide = slides[entry.SlideIndex];
            var isSelected = selection.IsSelected(entry.SlideIndex);
            var thumbnail = SlidePanePlanner.BuildThumbnailVisualPlan(
                entry,
                slide,
                selection.ActiveSlideIndex,
                isSelected);
            items.Add(new(
                ordinal,
                entry,
                thumbnail,
                null,
                new SlidePanePreviewMetadata(
                    slide.Id,
                    entry.SlideIndex,
                    thumbnail.TitleText,
                    thumbnail.ShapeCount,
                    slide.IsHidden,
                    thumbnail.IsSelected,
                    thumbnail.IsActive)));
        }

        var selectedCount = selection.SelectedSlideIndices.Count;
        var statusText = slides.Count == 0
            ? "No slides"
            : $"Slide {selection.ActiveSlideIndex + 1} of {slides.Count}";
        var selectionText = selectedCount == 1
            ? "1 slide selected"
            : $"{selectedCount} slides selected";

        return new PresentationSlidePaneProjection(
            ++_revision,
            layout,
            selection,
            items,
            SlidePanePlanner.BuildBottomNewSlideAffordance(slides.Count, selection.ActiveSlideIndex),
            new SlidePaneStatusPlan(
                selection.ActiveSlideIndex,
                slides.Count,
                selectedCount,
                statusText,
                selectionText));
    }

    private SlidePaneSessionChangePlan Rebuild(
        SlidePaneSessionChangeKind kind,
        bool rebuildItems,
        bool scrollActive)
    {
        Projection = BuildProjection();
        return new(
            kind,
            Projection,
            rebuildItems,
            ShouldSyncSelection: true,
            ShouldRefreshChrome: true,
            ShouldScrollActiveIntoView: scrollActive);
    }

    private SlidePaneSessionChangePlan Unchanged() => new(
        SlidePaneSessionChangeKind.None,
        Projection,
        ShouldRebuildItems: false,
        ShouldSyncSelection: false,
        ShouldRefreshChrome: false,
        ShouldScrollActiveIntoView: false);

    private SlidePaneSelectionSnapshot BuildSelectionSnapshot(IReadOnlyList<Slide> slides)
    {
        var selected = slides
            .Select((slide, index) => new { slide.Id, Index = index })
            .Where(item => _selectedSlideIds.Contains(item.Id))
            .Select(item => item.Index)
            .ToArray();
        var activeIndex = IndexOfSlideId(slides, _activeSlideId);
        var anchorIndex = IndexOfSlideId(slides, _anchorSlideId);
        return new(activeIndex, anchorIndex, selected);
    }

    private void ReconcileSelectionWithPresentation()
    {
        var slides = _getEditor().Presentation.Slides;
        var ids = slides.Select(slide => slide.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _selectedSlideIds.RemoveWhere(id => !ids.Contains(id));
        if (_activeSlideId is not null && !ids.Contains(_activeSlideId))
            _activeSlideId = null;
        if (_anchorSlideId is not null && !ids.Contains(_anchorSlideId))
            _anchorSlideId = null;
        SelectEditorSlideWhenSelectionIsEmpty();
        EnsureActiveSelection();
    }

    private void SelectEditorSlideWhenSelectionIsEmpty()
    {
        if (_selectedSlideIds.Count > 0 && _activeSlideId is not null)
            return;

        var editor = _getEditor();
        if (!IsValidSlideIndex(editor.Presentation.Slides.Count, editor.CurrentSlideIndex))
            return;

        SelectOnly(editor.Presentation.Slides[editor.CurrentSlideIndex].Id);
    }

    private void EnsureActiveSelection()
    {
        if (_activeSlideId is not null && _selectedSlideIds.Contains(_activeSlideId))
            return;

        _activeSlideId = _selectedSlideIds
            .Select(id => new { Id = id, Index = IndexOfSlideId(id) })
            .Where(item => item.Index >= 0)
            .OrderBy(item => item.Index)
            .Select(item => item.Id)
            .FirstOrDefault();
        _anchorSlideId ??= _activeSlideId;
    }

    private void SelectOnly(string? slideId)
    {
        _selectedSlideIds.Clear();
        if (slideId is null)
        {
            _activeSlideId = null;
            _anchorSlideId = null;
            return;
        }

        _selectedSlideIds.Add(slideId);
        _activeSlideId = slideId;
        _anchorSlideId = slideId;
    }

    private void SetSelectedSlideIds(IEnumerable<string> slideIds)
    {
        _selectedSlideIds.Clear();
        foreach (var id in slideIds)
            _selectedSlideIds.Add(id);
    }

    private bool ToggleSelection(string slideId)
    {
        if (_selectedSlideIds.Contains(slideId) && _selectedSlideIds.Count > 1)
        {
            _selectedSlideIds.Remove(slideId);
            return false;
        }
        else
        {
            _selectedSlideIds.Add(slideId);
            return true;
        }
    }

    private void SelectRange(int slideIndex, bool additive)
    {
        var slides = _getEditor().Presentation.Slides;
        var anchorIndex = IndexOfSlideId(slides, _anchorSlideId);
        if (!IsValidSlideIndex(slides.Count, anchorIndex))
            anchorIndex = slideIndex;
        if (!additive)
            _selectedSlideIds.Clear();

        for (var index = Math.Min(anchorIndex, slideIndex); index <= Math.Max(anchorIndex, slideIndex); index++)
            _selectedSlideIds.Add(slides[index].Id);
    }

    private IReadOnlyList<int> ResolveActionSources(int contextSlideIndex, SlidePaneActionKind kind)
    {
        var selection = Selection.SelectedSlideIndices;
        if (kind == SlidePaneActionKind.InsertAfterSlide || !selection.Contains(contextSlideIndex))
            return IsValidSlideIndex(_getEditor().Presentation.Slides.Count, contextSlideIndex)
                ? [contextSlideIndex]
                : [];
        return selection;
    }

    private IReadOnlyList<int> VisibleSlideIndices() => Projection.Items
        .Where(item => item.Entry.Kind == SlidePaneEntryKind.Slide)
        .Select(item => item.Entry.SlideIndex)
        .ToArray();

    private int MapVisibleInsertionToModelIndex(int visibleInsertionIndex)
    {
        var visible = VisibleSlideIndices();
        if (visible.Count == 0)
            return 0;
        if (visibleInsertionIndex <= 0)
            return visible[0];
        if (visibleInsertionIndex >= visible.Count)
            return visible[^1] + 1;
        return visible[visibleInsertionIndex];
    }

    private SlidePaneDropVisualPlan TranslateDropVisual(SlidePaneDropVisualPlan plan)
    {
        var sourceIndex = IndexOfSlideId(_dragSourceSlideId);
        var targetIndex = plan.IsTargetValid
            ? MapVisibleInsertionToModelIndex(plan.TargetSlideIndex)
            : -1;
        var move = BuildAction(SlidePaneActionKind.MoveSlide, sourceIndex, targetIndex);
        return plan with
        {
            SourceSlideIndex = sourceIndex,
            TargetSlideIndex = targetIndex,
            IsMoveEnabled = move.IsEnabled,
            AutomationDescription = targetIndex >= 0
                ? $"Move selected slides to position {targetIndex + 1}"
                : "No slide drop target",
        };
    }

    private int IndexOfSlideId(string? slideId) =>
        IndexOfSlideId(_getEditor().Presentation.Slides, slideId);

    private static int IndexOfSlideId(IReadOnlyList<Slide> slides, string? slideId)
    {
        if (slideId is null)
            return -1;
        for (var index = 0; index < slides.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(slides[index].Id, slideId))
                return index;
        }
        return -1;
    }

    private static bool CanMoveSelection(
        IReadOnlyList<Slide> slides,
        IReadOnlyList<int> sourceIndices,
        int targetInsertionIndex)
    {
        if (targetInsertionIndex < 0 || targetInsertionIndex > slides.Count)
            return false;
        var original = slides.Select(slide => slide.Id).ToArray();
        var selected = sourceIndices.Select(index => original[index]).ToArray();
        return !original.SequenceEqual(BuildMovedOrder(original, selected, targetInsertionIndex));
    }

    private static IReadOnlyList<string> BuildMovedOrder(
        IReadOnlyList<string> originalOrder,
        IReadOnlyCollection<string> selectedIds,
        int targetInsertionIndex)
    {
        var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedInOrder = originalOrder.Where(selected.Contains).ToArray();
        var remaining = originalOrder.Where(id => !selected.Contains(id)).ToList();
        var selectedBeforeTarget = originalOrder
            .Take(Math.Clamp(targetInsertionIndex, 0, originalOrder.Count))
            .Count(selected.Contains);
        var adjustedTarget = Math.Clamp(targetInsertionIndex - selectedBeforeTarget, 0, remaining.Count);
        remaining.InsertRange(adjustedTarget, selectedInOrder);
        return remaining;
    }

    private static bool IsValidSlideIndex(int slideCount, int slideIndex) =>
        slideIndex >= 0 && slideIndex < slideCount;
}

internal static class SlidePaneSelectionIndexExtensions
{
    public static int BinarySearch(this IReadOnlyList<int> values, int value)
    {
        var low = 0;
        var high = values.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = values[middle].CompareTo(value);
            if (comparison == 0)
                return middle;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return ~low;
    }

    public static int IndexOf(this IReadOnlyList<int> values, int value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
                return index;
        }
        return -1;
    }
}
