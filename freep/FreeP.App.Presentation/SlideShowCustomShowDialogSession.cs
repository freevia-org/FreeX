using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public enum SlideShowCustomShowDialogField
{
    CustomShows,
    Name,
    OrderedSlides,
    AvailableSlides,
    Validation,
}

public enum SlideShowCustomShowDialogAction
{
    Create,
    Rename,
    UpdateSlides,
    Delete,
    StartShow,
    MoveUp,
    MoveDown,
    Remove,
    AddSlide,
    Close,
}

public static class SlideShowCustomShowDialogSurfaceCatalog
{
    public static PresentationDialogSurfacePlan<
        SlideShowCustomShowDialogField,
        SlideShowCustomShowDialogAction> Surface { get; } = new(
            "Custom Shows",
            "Custom Shows",
            "FreeP.CustomShows.Dialog",
            [
                Field(SlideShowCustomShowDialogField.CustomShows, PresentationDialogControlKind.List,
                    "Custom shows", "Custom show list"),
                Field(SlideShowCustomShowDialogField.Name, PresentationDialogControlKind.Text,
                    "Name", "Custom show name"),
                Field(SlideShowCustomShowDialogField.OrderedSlides, PresentationDialogControlKind.List,
                    "Custom show order", "Slides in custom show"),
                Field(SlideShowCustomShowDialogField.AvailableSlides, PresentationDialogControlKind.List,
                    "Deck slides", "Available deck slides"),
                Field(SlideShowCustomShowDialogField.Validation, PresentationDialogControlKind.Status,
                    string.Empty, "Custom show validation message"),
            ],
            [
                Action(SlideShowCustomShowDialogAction.Create, "Create", "Create custom show", isDefault: true),
                Action(SlideShowCustomShowDialogAction.Rename, "Rename", "Rename custom show"),
                Action(SlideShowCustomShowDialogAction.UpdateSlides, "Update Slides", "Update custom show slides"),
                Action(SlideShowCustomShowDialogAction.Delete, "Delete", "Delete custom show"),
                Action(SlideShowCustomShowDialogAction.StartShow, "Start Show", "Start custom show"),
                Action(SlideShowCustomShowDialogAction.MoveUp, "Move Up", "Move selected slide up"),
                Action(SlideShowCustomShowDialogAction.MoveDown, "Move Down", "Move selected slide down"),
                Action(SlideShowCustomShowDialogAction.Remove, "Remove", "Remove selected slide"),
                Action(SlideShowCustomShowDialogAction.AddSlide, "Add", "Add slide to custom show"),
                Action(SlideShowCustomShowDialogAction.Close, "Close", "Close custom shows", isCancel: true),
            ]);

    private static PresentationDialogFieldPlan<SlideShowCustomShowDialogField> Field(
        SlideShowCustomShowDialogField id,
        PresentationDialogControlKind kind,
        string label,
        string accessibleName) =>
        new(id, kind, label, accessibleName, $"FreeP.CustomShows.{id}");

    private static PresentationDialogActionPlan<SlideShowCustomShowDialogAction> Action(
        SlideShowCustomShowDialogAction id,
        string label,
        string accessibleName,
        bool isDefault = false,
        bool isCancel = false) =>
        new(id, label, accessibleName, $"FreeP.CustomShows.{id}", isDefault, isCancel);
}

public enum SlideShowCustomShowDialogMutationKind
{
    Create,
    Rename,
    UpdateSlides,
    Delete,
    MoveSlide
}

public sealed record SlideShowCustomShowDialogMutationRequest(
    SlideShowCustomShowDialogMutationKind Kind,
    int CustomShowIndex,
    string? Name,
    IReadOnlyList<string?> SlideIds,
    int SourceSlideIndex,
    string? SourceSlideId,
    int TargetSlideIndex)
{
    public static SlideShowCustomShowDialogMutationRequest Create(
        string? name,
        IEnumerable<string?> slideIds) =>
        new(
            SlideShowCustomShowDialogMutationKind.Create,
            -1,
            name,
            Snapshot(slideIds),
            -1,
            null,
            -1);

    public static SlideShowCustomShowDialogMutationRequest Rename(
        int customShowIndex,
        string? name) =>
        new(
            SlideShowCustomShowDialogMutationKind.Rename,
            customShowIndex,
            name,
            Array.Empty<string?>(),
            -1,
            null,
            -1);

    public static SlideShowCustomShowDialogMutationRequest UpdateSlides(
        int customShowIndex,
        IEnumerable<string?> slideIds) =>
        new(
            SlideShowCustomShowDialogMutationKind.UpdateSlides,
            customShowIndex,
            null,
            Snapshot(slideIds),
            -1,
            null,
            -1);

    public static SlideShowCustomShowDialogMutationRequest Delete(int customShowIndex) =>
        new(
            SlideShowCustomShowDialogMutationKind.Delete,
            customShowIndex,
            null,
            Array.Empty<string?>(),
            -1,
            null,
            -1);

    public static SlideShowCustomShowDialogMutationRequest MoveSlide(
        int customShowIndex,
        int sourceSlideIndex,
        string? sourceSlideId,
        int targetSlideIndex) =>
        new(
            SlideShowCustomShowDialogMutationKind.MoveSlide,
            customShowIndex,
            null,
            Array.Empty<string?>(),
            sourceSlideIndex,
            sourceSlideId,
            targetSlideIndex);

    public SlideShowCustomShowMutationResult Apply(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        return Kind switch
        {
            SlideShowCustomShowDialogMutationKind.Create =>
                SlideShowCustomShowPlanner.CreateCustomShow(presentation, Name, SlideIds),
            SlideShowCustomShowDialogMutationKind.Rename =>
                SlideShowCustomShowPlanner.RenameCustomShow(presentation, CustomShowIndex, Name),
            SlideShowCustomShowDialogMutationKind.UpdateSlides =>
                SlideShowCustomShowPlanner.UpdateCustomShowSlides(presentation, CustomShowIndex, SlideIds),
            SlideShowCustomShowDialogMutationKind.Delete =>
                SlideShowCustomShowPlanner.DeleteCustomShow(presentation, CustomShowIndex),
            SlideShowCustomShowDialogMutationKind.MoveSlide =>
                SlideShowCustomShowPlanner.MoveCustomShowSlide(
                    presentation,
                    CustomShowIndex,
                    SourceSlideIndex,
                    SourceSlideId,
                    TargetSlideIndex),
            _ => throw new ArgumentOutOfRangeException(nameof(Kind), Kind, "Unknown custom-show mutation kind.")
        };
    }

    private static IReadOnlyList<string?> Snapshot(IEnumerable<string?> slideIds)
    {
        ArgumentNullException.ThrowIfNull(slideIds);
        return slideIds.ToArray();
    }
}

public sealed record SlideShowCustomShowDialogSessionCallbacks(
    Func<SlideShowCustomShowSessionState, SlideShowCustomShowSessionPlan> BuildPlan,
    Func<SlideShowCustomShowDialogMutationRequest, SlideShowCustomShowMutationResult> ApplyMutation,
    Func<string?, bool> TryStartShow);

public enum SlideShowCustomShowDialogRenderScope
{
    None,
    Full,
    SelectedShow,
    SlideSelection
}

public sealed record SlideShowCustomShowDialogSessionTransition(
    SlideShowCustomShowSessionPlan Plan,
    string? ValidationMessage,
    SlideShowCustomShowDialogRenderScope RenderScope,
    bool ShouldClose = false,
    SlideShowCustomShowDialogMutationRequest? MutationRequest = null,
    SlideShowCustomShowMutationResult? MutationResult = null);

public static class SlideShowCustomShowDialogTransitionDispatcher
{
    public static void Dispatch(
        SlideShowCustomShowDialogSessionTransition transition,
        Action<SlideShowCustomShowSessionPlan> renderFull,
        Action<SlideShowCustomShowSessionPlan> renderSelectedShow,
        Action<SlideShowCustomShowSessionPlan> renderSlideSelection,
        Action<string?> setValidation,
        Action close)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(renderFull);
        ArgumentNullException.ThrowIfNull(renderSelectedShow);
        ArgumentNullException.ThrowIfNull(renderSlideSelection);
        ArgumentNullException.ThrowIfNull(setValidation);
        ArgumentNullException.ThrowIfNull(close);

        switch (transition.RenderScope)
        {
            case SlideShowCustomShowDialogRenderScope.None:
                break;
            case SlideShowCustomShowDialogRenderScope.Full:
                renderFull(transition.Plan);
                break;
            case SlideShowCustomShowDialogRenderScope.SelectedShow:
                renderSelectedShow(transition.Plan);
                break;
            case SlideShowCustomShowDialogRenderScope.SlideSelection:
                renderSlideSelection(transition.Plan);
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(transition),
                    transition.RenderScope,
                    "Unknown custom-show dialog render scope.");
        }

        setValidation(transition.ValidationMessage);
        if (transition.ShouldClose)
            close();
    }
}

public sealed record SlideShowCustomShowDialogViewState(
    string? Name,
    IReadOnlyList<string> SelectedSlideIds,
    int SelectedShowIndex,
    int SelectedSlideIndex);

public interface ISlideShowCustomShowDialogView
{
    SlideShowCustomShowDialogViewState CaptureState();

    void RenderFullPlan(SlideShowCustomShowSessionPlan plan);

    void RenderSelectedShowPlan(SlideShowCustomShowSessionPlan plan);

    void ApplySlideSelection(SlideShowCustomShowSessionPlan plan);

    void SetValidation(string? message);

    void CloseDialog();
}

/// <summary>
/// Routes custom-show dialog selections and actions through the portable session. Renderers retain
/// native control construction, pointer handling, focus, and modal lifetime.
/// </summary>
public sealed class SlideShowCustomShowDialogController
{
    private readonly SlideShowCustomShowDialogSession _session;
    private readonly ISlideShowCustomShowDialogView _view;

    public SlideShowCustomShowDialogController(
        SlideShowCustomShowDialogSession session,
        ISlideShowCustomShowDialogView view)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _view = view ?? throw new ArgumentNullException(nameof(view));
    }

    public PresentationDialogSurfacePlan<
        SlideShowCustomShowDialogField,
        SlideShowCustomShowDialogAction> Surface => _session.Surface;

    public void Initialize() => ApplyTransition(_session.InitialTransition);

    public void SelectShow()
    {
        var state = _view.CaptureState();
        ApplyTransition(_session.SelectShow(state.SelectedShowIndex));
    }

    public void SelectSlide()
    {
        var state = _view.CaptureState();
        ApplyTransition(_session.SelectSlide(state.SelectedSlideIndex));
    }

    public void Create()
    {
        var state = _view.CaptureState();
        ApplyTransition(_session.Create(state.Name, state.SelectedSlideIds));
    }

    public void Rename()
    {
        var state = _view.CaptureState();
        ApplyTransition(_session.Rename(state.Name));
    }

    public void UpdateSlides()
    {
        var state = _view.CaptureState();
        ApplyTransition(_session.UpdateSlides(state.SelectedSlideIds));
    }

    public void AddSlideOccurrence(string slideId) =>
        ApplyTransition(_session.AddSlideOccurrence(slideId));

    public void RemoveSelectedSlide() =>
        ApplyTransition(_session.RemoveSelectedSlide());

    public void MoveSelectedSlide(int offset) =>
        ApplyTransition(_session.MoveSelectedSlide(offset));

    public void Delete() => ApplyTransition(_session.Delete());

    public void StartShow() => ApplyTransition(_session.StartShow());

    public SlideShowCustomShowDragReorderPlan Reorder(
        int sourceSlideIndex,
        int targetDropIndex)
    {
        var transition = _session.Reorder(sourceSlideIndex, targetDropIndex);
        ApplyTransition(transition.SessionTransition);
        return transition.ReorderPlan;
    }

    private void ApplyTransition(SlideShowCustomShowDialogSessionTransition transition) =>
        SlideShowCustomShowDialogTransitionDispatcher.Dispatch(
            transition,
            _view.RenderFullPlan,
            _view.RenderSelectedShowPlan,
            _view.ApplySlideSelection,
            _view.SetValidation,
            _view.CloseDialog);
}

/// <summary>
/// Applies custom-show session plans to renderer-owned controls. The callbacks are deliberately
/// limited to native value transport; selection defaulting and action enablement stay shared.
/// </summary>
public sealed class SlideShowCustomShowDialogFormSession<TControl>
    where TControl : class
{
    private readonly TControl _showList;
    private readonly TControl _orderedSlideList;
    private readonly TControl _nameControl;
    private readonly TControl _validationControl;
    private readonly Action<TControl, object?> _setItemsSource;
    private readonly Action<TControl, int> _setSelectedIndex;
    private readonly Func<TControl, int> _getSelectedIndex;
    private readonly Func<TControl, object?> _getSelectedItem;
    private readonly Action<TControl, string> _setText;
    private readonly Action<TControl, bool> _setChecked;
    private readonly Func<TControl, bool> _isChecked;
    private readonly Action<TControl, bool> _setEnabled;
    private readonly List<(string SlideId, TControl Control)> _availableSlides = [];
    private readonly Dictionary<SlideShowCustomShowDialogAction, TControl> _actions = [];

    public SlideShowCustomShowDialogFormSession(
        TControl showList,
        TControl orderedSlideList,
        TControl nameControl,
        TControl validationControl,
        Action<TControl, object?> setItemsSource,
        Action<TControl, int> setSelectedIndex,
        Func<TControl, int> getSelectedIndex,
        Func<TControl, object?> getSelectedItem,
        Action<TControl, string> setText,
        Action<TControl, bool> setChecked,
        Func<TControl, bool> isChecked,
        Action<TControl, bool> setEnabled)
    {
        _showList = showList ?? throw new ArgumentNullException(nameof(showList));
        _orderedSlideList = orderedSlideList ?? throw new ArgumentNullException(nameof(orderedSlideList));
        _nameControl = nameControl ?? throw new ArgumentNullException(nameof(nameControl));
        _validationControl = validationControl ?? throw new ArgumentNullException(nameof(validationControl));
        _setItemsSource = setItemsSource ?? throw new ArgumentNullException(nameof(setItemsSource));
        _setSelectedIndex = setSelectedIndex ?? throw new ArgumentNullException(nameof(setSelectedIndex));
        _getSelectedIndex = getSelectedIndex ?? throw new ArgumentNullException(nameof(getSelectedIndex));
        _getSelectedItem = getSelectedItem ?? throw new ArgumentNullException(nameof(getSelectedItem));
        _setText = setText ?? throw new ArgumentNullException(nameof(setText));
        _setChecked = setChecked ?? throw new ArgumentNullException(nameof(setChecked));
        _isChecked = isChecked ?? throw new ArgumentNullException(nameof(isChecked));
        _setEnabled = setEnabled ?? throw new ArgumentNullException(nameof(setEnabled));
    }

    public int SelectedShowIndex =>
        _getSelectedItem(_showList) is SlideShowCustomShowSessionShowItemPlan selected
            ? selected.Index
            : -1;

    public int SelectedSlideIndex => _getSelectedIndex(_orderedSlideList);

    public void SelectSlide(int index) => _setSelectedIndex(_orderedSlideList, index);

    public void RegisterAction(SlideShowCustomShowDialogAction action, TControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        _actions[action] = control;
    }

    public void ClearAvailableSlides() => _availableSlides.Clear();

    public void RegisterAvailableSlide(string slideId, TControl control)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slideId);
        ArgumentNullException.ThrowIfNull(control);
        _availableSlides.Add((slideId, control));
    }

    public IReadOnlyList<string> SelectedSlideIds() =>
        _availableSlides
            .Where(entry => _isChecked(entry.Control))
            .Select(entry => entry.SlideId)
            .ToArray();

    public void ApplyFullPlan(SlideShowCustomShowSessionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _setItemsSource(_showList, plan.CustomShows);
        var selectedListIndex = plan.SelectedShow is null
            ? plan.CustomShows.Count == 0 ? -1 : 0
            : FindShowListIndex(plan.CustomShows, plan.SelectedShow.Index);
        _setSelectedIndex(_showList, selectedListIndex);
        ApplySelectedShowPlan(plan);
    }

    public void ApplySelectedShowPlan(SlideShowCustomShowSessionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _setText(_nameControl, plan.SelectedShow?.Name ?? string.Empty);
        var selectedIds = plan.SelectedSlideIds.ToHashSet(StringComparer.Ordinal);
        foreach (var (slideId, control) in _availableSlides)
            _setChecked(control, selectedIds.Contains(slideId));

        _setItemsSource(_orderedSlideList, plan.SelectedSlides);
        SetEnabled(SlideShowCustomShowDialogAction.Rename, plan.CanRename);
        SetEnabled(SlideShowCustomShowDialogAction.UpdateSlides, plan.CanUpdateSlides);
        SetEnabled(SlideShowCustomShowDialogAction.Delete, plan.CanDelete);
        SetEnabled(SlideShowCustomShowDialogAction.StartShow, plan.CanStart);
        ApplySlideSelection(plan);
    }

    public void ApplySlideSelection(SlideShowCustomShowSessionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var selectedIndex = plan.SelectedSlideIndex >= 0 &&
            plan.SelectedSlideIndex < plan.SelectedSlides.Count
                ? plan.SelectedSlideIndex
                : -1;
        if (_getSelectedIndex(_orderedSlideList) != selectedIndex)
            _setSelectedIndex(_orderedSlideList, selectedIndex);

        SetEnabled(SlideShowCustomShowDialogAction.MoveUp, plan.CanMoveUp);
        SetEnabled(SlideShowCustomShowDialogAction.MoveDown, plan.CanMoveDown);
        SetEnabled(SlideShowCustomShowDialogAction.Remove, plan.CanRemove);
    }

    public void SetValidation(string? message) =>
        _setText(_validationControl, message ?? string.Empty);

    private void SetEnabled(SlideShowCustomShowDialogAction action, bool isEnabled)
    {
        if (_actions.TryGetValue(action, out var control))
            _setEnabled(control, isEnabled);
    }

    private static int FindShowListIndex(
        IReadOnlyList<SlideShowCustomShowSessionShowItemPlan> shows,
        int showIndex)
    {
        for (var index = 0; index < shows.Count; index++)
        {
            if (shows[index].Index == showIndex)
                return index;
        }

        return shows.Count == 0 ? -1 : 0;
    }
}

public sealed record SlideShowCustomShowDialogReorderTransition(
    SlideShowCustomShowDragReorderPlan ReorderPlan,
    SlideShowCustomShowDialogSessionTransition SessionTransition);

/// <summary>
/// Renderer-neutral state and orchestration for the custom-show authoring dialog.
/// Hosts retain native controls, list-item projection, focus, and pointer handling.
/// </summary>
public sealed class SlideShowCustomShowDialogSession
{
    private readonly SlideShowCustomShowDialogSessionCallbacks _callbacks;

    public SlideShowCustomShowDialogSession(
        SlideShowCustomShowDialogSessionCallbacks callbacks,
        SlideShowCustomShowSessionState? initialState = null)
    {
        _callbacks = callbacks ?? throw new ArgumentNullException(nameof(callbacks));
        ArgumentNullException.ThrowIfNull(callbacks.BuildPlan);
        ArgumentNullException.ThrowIfNull(callbacks.ApplyMutation);
        ArgumentNullException.ThrowIfNull(callbacks.TryStartShow);

        Plan = callbacks.BuildPlan(initialState ?? new SlideShowCustomShowSessionState());
    }

    public SlideShowCustomShowSessionPlan Plan { get; private set; }

    public PresentationDialogSurfacePlan<
        SlideShowCustomShowDialogField,
        SlideShowCustomShowDialogAction> Surface =>
        SlideShowCustomShowDialogSurfaceCatalog.Surface;

    public string? ValidationMessage { get; private set; }

    public SlideShowCustomShowDialogSessionTransition InitialTransition =>
        Transition(SlideShowCustomShowDialogRenderScope.Full);

    public SlideShowCustomShowDialogSessionTransition Refresh(
        int selectedCustomShowIndex,
        int selectedSlideIndex = -1) =>
        RefreshCore(
            new SlideShowCustomShowSessionState(selectedCustomShowIndex, selectedSlideIndex),
            SlideShowCustomShowDialogRenderScope.Full,
            clearValidation: true);

    public SlideShowCustomShowDialogSessionTransition SelectShow(int customShowIndex) =>
        RefreshCore(
            SlideShowCustomShowSessionPlanner.SelectShow(customShowIndex),
            SlideShowCustomShowDialogRenderScope.SelectedShow,
            clearValidation: true);

    public SlideShowCustomShowDialogSessionTransition SelectSlide(int selectedSlideIndex)
    {
        var normalizedIndex = selectedSlideIndex >= 0 && selectedSlideIndex < Plan.SelectedSlides.Count
            ? selectedSlideIndex
            : -1;
        Plan = Plan with
        {
            SelectedSlideIndex = normalizedIndex,
            CanMoveUp = normalizedIndex > 0,
            CanMoveDown = normalizedIndex >= 0 && normalizedIndex < Plan.SelectedSlides.Count - 1,
            CanRemove = normalizedIndex >= 0
        };
        return Transition(SlideShowCustomShowDialogRenderScope.SlideSelection);
    }

    public SlideShowCustomShowDialogSessionTransition Create(
        string? name,
        IEnumerable<string?> slideIds) =>
        ApplyMutation(
            SlideShowCustomShowDialogMutationRequest.Create(name, slideIds),
            result => new SlideShowCustomShowSessionState(result.CustomShowIndex, result.SelectedSlideIndex));

    public SlideShowCustomShowDialogSessionTransition Rename(string? name)
    {
        if (Plan.SelectedShow is null)
        {
            return Validation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
        }

        return ApplyMutation(
            SlideShowCustomShowDialogMutationRequest.Rename(Plan.SelectedShow.Index, name),
            result => new SlideShowCustomShowSessionState(result.CustomShowIndex, result.SelectedSlideIndex));
    }

    public SlideShowCustomShowDialogSessionTransition UpdateSlides(IEnumerable<string?> slideIds)
    {
        if (Plan.SelectedShow is null)
        {
            return Validation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
        }

        return ApplyMutation(
            SlideShowCustomShowDialogMutationRequest.UpdateSlides(Plan.SelectedShow.Index, slideIds),
            result => new SlideShowCustomShowSessionState(result.CustomShowIndex, result.SelectedSlideIndex));
    }

    public SlideShowCustomShowDialogSessionTransition AddSlideOccurrence(string slideId)
    {
        if (Plan.SelectedShow is null)
        {
            return Validation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
        }

        var selectedSlideIndex = Plan.SelectedSlideIds.Count;
        return ApplyMutation(
            SlideShowCustomShowDialogMutationRequest.UpdateSlides(
                Plan.SelectedShow.Index,
                Plan.SelectedSlideIds.Append(slideId)),
            result => new SlideShowCustomShowSessionState(result.CustomShowIndex, selectedSlideIndex));
    }

    public SlideShowCustomShowDialogSessionTransition RemoveSelectedSlide()
    {
        if (Plan.SelectedShow is null)
        {
            return Validation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
        }

        var selectedSlideIndex = Plan.SelectedSlideIndex;
        if (selectedSlideIndex < 0 || selectedSlideIndex >= Plan.SelectedSlideIds.Count)
        {
            return Validation(SlideShowCustomShowPlanner.MissingCustomShowSlideMessage);
        }

        var slideIds = Plan.SelectedSlideIds.ToList();
        slideIds.RemoveAt(selectedSlideIndex);
        var nextSelectedSlideIndex = slideIds.Count == 0
            ? -1
            : Math.Min(selectedSlideIndex, slideIds.Count - 1);
        return ApplyMutation(
            SlideShowCustomShowDialogMutationRequest.UpdateSlides(Plan.SelectedShow.Index, slideIds),
            result => new SlideShowCustomShowSessionState(result.CustomShowIndex, nextSelectedSlideIndex));
    }

    public SlideShowCustomShowDialogSessionTransition MoveSelectedSlide(int offset)
    {
        if (Plan.SelectedShow is null)
        {
            return Validation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
        }

        if (Plan.SelectedSlideIndex < 0 || Plan.SelectedSlideIndex >= Plan.SelectedSlides.Count)
        {
            return Validation(SlideShowCustomShowPlanner.MissingCustomShowSlideMessage);
        }

        var targetDropIndex = Plan.SelectedSlideIndex + offset + (offset > 0 ? 1 : 0);
        return Reorder(Plan.SelectedSlideIndex, targetDropIndex).SessionTransition;
    }

    public SlideShowCustomShowDialogReorderTransition Reorder(
        int sourceSlideIndex,
        int targetDropIndex)
    {
        var reorderPlan = SlideShowCustomShowSessionPlanner.BuildDragReorderPlan(
            Plan,
            sourceSlideIndex,
            targetDropIndex);
        if (!reorderPlan.IsValid)
        {
            return new SlideShowCustomShowDialogReorderTransition(
                reorderPlan,
                Validation(reorderPlan.ErrorMessage));
        }

        if (!reorderPlan.ShouldApplyMutation)
        {
            ValidationMessage = null;
            return new SlideShowCustomShowDialogReorderTransition(
                reorderPlan,
                SelectSlide(reorderPlan.SelectedSlideIndex));
        }

        var request = SlideShowCustomShowDialogMutationRequest.MoveSlide(
            Plan.SelectedShow!.Index,
            reorderPlan.SourceSlideIndex,
            reorderPlan.SourceSlideId,
            reorderPlan.TargetSlideIndex);
        return new SlideShowCustomShowDialogReorderTransition(
            reorderPlan,
            ApplyMutation(
                request,
                result => new SlideShowCustomShowSessionState(
                    result.CustomShowIndex,
                    result.SelectedSlideIndex)));
    }

    public SlideShowCustomShowDialogSessionTransition Delete()
    {
        if (Plan.SelectedShow is null)
        {
            return Validation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
        }

        var deletedIndex = Plan.SelectedShow.Index;
        return ApplyMutation(
            SlideShowCustomShowDialogMutationRequest.Delete(deletedIndex),
            _ => new SlideShowCustomShowSessionState(Math.Max(0, deletedIndex - 1)));
    }

    public SlideShowCustomShowDialogSessionTransition StartShow()
    {
        if (Plan.SelectedShow is null)
        {
            return Validation(SlideShowCustomShowPlanner.MissingCustomShowMessage);
        }

        if (!_callbacks.TryStartShow(Plan.SelectedShow.Name))
        {
            return Validation(SlideShowCustomShowPlanner.EmptyCustomShowMessage);
        }

        ValidationMessage = null;
        return Transition(SlideShowCustomShowDialogRenderScope.None, shouldClose: true);
    }

    private SlideShowCustomShowDialogSessionTransition ApplyMutation(
        SlideShowCustomShowDialogMutationRequest request,
        Func<SlideShowCustomShowMutationResult, SlideShowCustomShowSessionState> selectState)
    {
        var result = _callbacks.ApplyMutation(request);
        if (!result.Succeeded)
        {
            ValidationMessage = result.ErrorMessage;
            return Transition(
                SlideShowCustomShowDialogRenderScope.None,
                mutationRequest: request,
                mutationResult: result);
        }

        var transition = RefreshCore(
            selectState(result),
            SlideShowCustomShowDialogRenderScope.Full,
            clearValidation: true);
        return transition with
        {
            MutationRequest = request,
            MutationResult = result
        };
    }

    private SlideShowCustomShowDialogSessionTransition RefreshCore(
        SlideShowCustomShowSessionState state,
        SlideShowCustomShowDialogRenderScope renderScope,
        bool clearValidation)
    {
        Plan = _callbacks.BuildPlan(state);
        if (clearValidation)
        {
            ValidationMessage = null;
        }

        return Transition(renderScope);
    }

    private SlideShowCustomShowDialogSessionTransition Validation(string? message)
    {
        ValidationMessage = message;
        return Transition(SlideShowCustomShowDialogRenderScope.None);
    }

    private SlideShowCustomShowDialogSessionTransition Transition(
        SlideShowCustomShowDialogRenderScope renderScope,
        bool shouldClose = false,
        SlideShowCustomShowDialogMutationRequest? mutationRequest = null,
        SlideShowCustomShowMutationResult? mutationResult = null) =>
        new(
            Plan,
            ValidationMessage,
            renderScope,
            shouldClose,
            mutationRequest,
            mutationResult);
}
