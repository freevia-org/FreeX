using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlideShowPresenterViewNativeControls<TText, TInput, TChoice, TButton, TPreview>(
    TText Status,
    TText Elapsed,
    TText CurrentLabel,
    TText NextLabel,
    TText RecordingStatus,
    TInput Notes,
    TInput SlideNumber,
    TChoice PointerMode,
    TPreview CurrentPreview,
    TPreview NextPreview,
    IReadOnlyDictionary<SlideShowPresenterViewAction, TButton> ActionButtons);

public sealed record SlideShowPresenterViewNativeAccessors<TText, TInput, TChoice, TButton, TPreview>(
    Func<TInput, string?> ReadInputText,
    Func<TInput, bool> IsInputFocused,
    Action<TText, string> SetText,
    Action<TInput, string> SetInputText,
    Action<TButton, string> SetButtonText,
    Action<TButton, bool> SetButtonEnabled,
    Action<TChoice, SlideShowPresenterPointerMode> SetPointerMode,
    Action<TPreview, Slide?> SetPreviewSlide,
    Action<TPreview> RefreshPreview);

/// <summary>
/// Owns the portable sequencing between presenter-view state and native controls.
/// Native hosts retain control construction, focus inspection, timers, and rendering.
/// </summary>
public sealed class SlideShowPresenterViewNativeBinding<TText, TInput, TChoice, TButton, TPreview>
{
    private readonly SlideShowPresenterViewHostCoordinator _coordinator;
    private readonly SlideShowPresenterViewNativeControls<TText, TInput, TChoice, TButton, TPreview> _controls;
    private readonly SlideShowPresenterViewNativeAccessors<TText, TInput, TChoice, TButton, TPreview> _accessors;

    public SlideShowPresenterViewNativeBinding(
        SlideShowPresenterViewHostCoordinator coordinator,
        SlideShowPresenterViewNativeControls<TText, TInput, TChoice, TButton, TPreview> controls,
        SlideShowPresenterViewNativeAccessors<TText, TInput, TChoice, TButton, TPreview> accessors)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _controls = controls ?? throw new ArgumentNullException(nameof(controls));
        _accessors = accessors ?? throw new ArgumentNullException(nameof(accessors));
    }

    public void NotifyNotesTextChanged() => _coordinator.NotifyNotesTextChanged();

    public void SelectPointerMode(SlideShowPresenterPointerMode mode) =>
        _coordinator.SelectPointerMode(mode, Refresh);

    public void ExecuteAction(SlideShowPresenterViewAction action) =>
        _coordinator.ExecuteAction(
            action,
            new(
                _accessors.ReadInputText(_controls.SlideNumber),
                _accessors.ReadInputText(_controls.Notes)),
            Refresh);

    public void Refresh()
    {
        _coordinator.Refresh(
            new(
                _accessors.IsInputFocused(_controls.Notes),
                _accessors.ReadInputText(_controls.Notes),
                _accessors.IsInputFocused(_controls.SlideNumber)),
            ApplyRefreshPlan);
    }

    public void Open(Action startRefreshTimer)
    {
        ArgumentNullException.ThrowIfNull(startRefreshTimer);
        Refresh();
        startRefreshTimer();
    }

    public void Close(Action stopRefreshTimer)
    {
        ArgumentNullException.ThrowIfNull(stopRefreshTimer);
        CommitNotes();
        stopRefreshTimer();
    }

    public void CommitNotes() =>
        _coordinator.CommitNotes(_accessors.ReadInputText(_controls.Notes));

    private void ApplyRefreshPlan(SlideShowPresenterViewRefreshPlan refresh)
    {
        var plan = refresh.ViewPlan;
        _accessors.SetText(_controls.Status, plan.StatusText);
        _accessors.SetText(_controls.Elapsed, _coordinator.Surface.FormatElapsed(plan.ElapsedText));
        _accessors.SetText(_controls.CurrentLabel, plan.CurrentSlideLabel);
        _accessors.SetText(_controls.NextLabel, plan.NextSlideLabel);
        if (refresh.ShouldUpdateNotesText)
        {
            _accessors.SetInputText(_controls.Notes, plan.NotesText);
        }

        if (refresh.ShouldUpdateSlideNumber && plan.CurrentSlideNumberText is not null)
        {
            _accessors.SetInputText(_controls.SlideNumber, plan.CurrentSlideNumberText);
        }

        foreach (var actionState in SlideShowPresenterViewActionProjection.Build(
                     plan,
                     plan.CanGoBack,
                     plan.CanAdvance,
                     _coordinator.CanGoToSlide,
                     _coordinator.CanSetScreenMode,
                     _coordinator.CanClearInk))
        {
            var button = _controls.ActionButtons[actionState.Action];
            _accessors.SetButtonText(button, actionState.Label);
            _accessors.SetButtonEnabled(button, actionState.IsEnabled);
        }

        _accessors.SetText(_controls.RecordingStatus, plan.RecordingStatusText);
        _accessors.SetPointerMode(_controls.PointerMode, plan.PointerMode);
        _accessors.SetPreviewSlide(_controls.CurrentPreview, plan.CurrentSlide);
        _accessors.SetPreviewSlide(_controls.NextPreview, plan.NextSlide);
        _accessors.RefreshPreview(_controls.CurrentPreview);
        _accessors.RefreshPreview(_controls.NextPreview);
    }
}

public static class SlideShowPresenterViewHeaderComposition
{
    public static IReadOnlyDictionary<SlideShowPresenterViewAction, TButton> Compose<TButton>(
        SlideShowPresenterViewHostCoordinator coordinator,
        Action addSlideNumber,
        Action addPointerMode,
        Func<PresentationDialogActionPlan<SlideShowPresenterViewAction>, SlideShowPresenterViewAction, bool, TButton> createActionButton,
        Action<TButton> addActionButton)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(addSlideNumber);
        ArgumentNullException.ThrowIfNull(addPointerMode);
        ArgumentNullException.ThrowIfNull(createActionButton);
        ArgumentNullException.ThrowIfNull(addActionButton);

        var buttons = new Dictionary<SlideShowPresenterViewAction, TButton>();
        foreach (var item in SlideShowPresenterViewActionProjection.HeaderItems)
        {
            if (item.Kind == SlideShowPresenterViewHeaderItemKind.SlideNumber)
            {
                addSlideNumber();
                continue;
            }

            if (item.Kind == SlideShowPresenterViewHeaderItemKind.PointerMode)
            {
                addPointerMode();
                continue;
            }

            var action = item.Action!.Value;
            var button = createActionButton(
                coordinator.Surface.Action(action),
                action,
                SlideShowPresenterViewActionProjection.IsInitiallyEnabled(
                    action,
                    coordinator.CanGoToSlide,
                    coordinator.CanSetScreenMode,
                    coordinator.CanClearInk));
            buttons.Add(action, button);
            addActionButton(button);
        }

        return buttons;
    }
}
