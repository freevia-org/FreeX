namespace FreeP.App.Compositor;

public enum PresentationMediaPaneFormCommand
{
    ApplyVolume,
    ApplyPlayback,
    ApplyTiming,
    CreateBookmark,
    ReplaceBookmark,
    DeleteBookmark,
    CreateCaption,
    ReplaceCaption,
    DeleteCaption,
    Close,
}

public sealed record PresentationMediaPaneNativeButtons<TButton>(
    TButton VolumeApply,
    TButton PlaybackApply,
    TButton TimingApply,
    TButton BookmarkCreate,
    TButton BookmarkReplace,
    TButton BookmarkDelete,
    TButton CaptionCreate,
    TButton CaptionReplace,
    TButton CaptionDelete,
    TButton Close)
{
    public TButton Get(PresentationMediaPaneCaptionAction action) => action switch
    {
        PresentationMediaPaneCaptionAction.Create => CaptionCreate,
        PresentationMediaPaneCaptionAction.Replace => CaptionReplace,
        PresentationMediaPaneCaptionAction.Delete => CaptionDelete,
        PresentationMediaPaneCaptionAction.Close => Close,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    public TButton Get(PresentationMediaPaneFormCommand command) => command switch
    {
        PresentationMediaPaneFormCommand.ApplyVolume => VolumeApply,
        PresentationMediaPaneFormCommand.ApplyPlayback => PlaybackApply,
        PresentationMediaPaneFormCommand.ApplyTiming => TimingApply,
        PresentationMediaPaneFormCommand.CreateBookmark => BookmarkCreate,
        PresentationMediaPaneFormCommand.ReplaceBookmark => BookmarkReplace,
        PresentationMediaPaneFormCommand.DeleteBookmark => BookmarkDelete,
        PresentationMediaPaneFormCommand.CreateCaption => CaptionCreate,
        PresentationMediaPaneFormCommand.ReplaceCaption => CaptionReplace,
        PresentationMediaPaneFormCommand.DeleteCaption => CaptionDelete,
        PresentationMediaPaneFormCommand.Close => Close,
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, null),
    };

    public IReadOnlyList<TButton> InVisualOrder =>
    [
        CaptionCreate,
        CaptionReplace,
        CaptionDelete,
        VolumeApply,
        PlaybackApply,
        TimingApply,
        BookmarkCreate,
        BookmarkReplace,
        BookmarkDelete,
        Close,
    ];
}

public sealed record PresentationMediaPaneCaptionNativeControls<TLabel, TInput>(
    TLabel LabelText,
    TInput LabelInput,
    TLabel LanguageText,
    TInput LanguageInput,
    TLabel SourceText,
    TInput SourceInput,
    TLabel TranscriptText,
    TInput TranscriptInput)
{
    public (TLabel Label, TInput Input) Get(PresentationMediaPaneCaptionField field) => field switch
    {
        PresentationMediaPaneCaptionField.Label => (LabelText, LabelInput),
        PresentationMediaPaneCaptionField.Language => (LanguageText, LanguageInput),
        PresentationMediaPaneCaptionField.Source => (SourceText, SourceInput),
        PresentationMediaPaneCaptionField.Transcript => (TranscriptText, TranscriptInput),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    public IReadOnlyList<TInput> Inputs => [LabelInput, LanguageInput, SourceInput, TranscriptInput];
}

public interface IPresentationMediaPaneFormEventRouter
{
    void SelectCaptionTrack(int? trackIndex);

    void SelectBookmark(int? bookmarkIndex);

    void Refresh();

    void Execute(PresentationMediaPaneFormCommand command);
}

public sealed class PresentationMediaPaneFormEventRouter : IPresentationMediaPaneFormEventRouter
{
    private readonly PresentationMediaPaneHostCoordinator _coordinator;

    public PresentationMediaPaneFormEventRouter(PresentationMediaPaneHostCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public void SelectCaptionTrack(int? trackIndex) => _coordinator.SelectCaptionTrack(trackIndex);

    public void SelectBookmark(int? bookmarkIndex) => _coordinator.SelectBookmark(bookmarkIndex);

    public void Refresh() => _coordinator.Refresh();

    public void Execute(PresentationMediaPaneFormCommand command)
    {
        switch (command)
        {
            case PresentationMediaPaneFormCommand.ApplyVolume:
                _coordinator.ApplyVolume();
                break;
            case PresentationMediaPaneFormCommand.ApplyPlayback:
                _coordinator.ApplyPlayback();
                break;
            case PresentationMediaPaneFormCommand.ApplyTiming:
                _coordinator.ApplyTiming();
                break;
            case PresentationMediaPaneFormCommand.CreateBookmark:
                _coordinator.ApplyBookmark(PresentationMediaBookmarkMutationIntentKind.Create);
                break;
            case PresentationMediaPaneFormCommand.ReplaceBookmark:
                _coordinator.ApplyBookmark(PresentationMediaBookmarkMutationIntentKind.Replace);
                break;
            case PresentationMediaPaneFormCommand.DeleteBookmark:
                _coordinator.ApplyBookmark(PresentationMediaBookmarkMutationIntentKind.Delete);
                break;
            case PresentationMediaPaneFormCommand.CreateCaption:
                _coordinator.ApplyCaption(PresentationMediaCaptionAuthoringIntentKind.Create);
                break;
            case PresentationMediaPaneFormCommand.ReplaceCaption:
                _coordinator.ApplyCaption(PresentationMediaCaptionAuthoringIntentKind.Replace);
                break;
            case PresentationMediaPaneFormCommand.DeleteCaption:
                _coordinator.ApplyCaption(PresentationMediaCaptionAuthoringIntentKind.Delete);
                break;
            case PresentationMediaPaneFormCommand.Close:
                _coordinator.Hide();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, null);
        }
    }
}

public static class PresentationMediaPaneFormEventBinder
{
    public static void Bind<TLabel, TInput, TButton, TComboBox>(
        TComboBox captionTracks,
        TComboBox bookmarks,
        PresentationMediaPaneCaptionNativeControls<TLabel, TInput> captionControls,
        PresentationMediaPaneNativeButtons<TButton> buttons,
        Action<TInput, Action> bindInputChanged,
        Action<TButton, Action> bindButtonActivation,
        Action<TComboBox, Action> bindSelectionChanged,
        Func<TComboBox, int?> readCaptionTrack,
        Func<TComboBox, int?> readBookmark,
        IPresentationMediaPaneFormEventRouter router)
    {
        ArgumentNullException.ThrowIfNull(router);
        foreach (var input in captionControls.Inputs)
            bindInputChanged(input, router.Refresh);

        bindSelectionChanged(captionTracks, () => router.SelectCaptionTrack(readCaptionTrack(captionTracks)));
        bindSelectionChanged(bookmarks, () => router.SelectBookmark(readBookmark(bookmarks)));
        foreach (var command in Enum.GetValues<PresentationMediaPaneFormCommand>())
            bindButtonActivation(buttons.Get(command), () => router.Execute(command));
    }
}

public sealed record PresentationMediaCaptionTrackNativeBindings<TItem>(
    Action Clear,
    Func<PresentationMediaCaptionAuthoringTrackPlan, TItem> CreateItem,
    Action<TItem, PresentationPaneAccessibilityItemPlan> ApplyAccessibility,
    Action<TItem> AddItem,
    Action<bool> SetEnabled,
    Action<int> SetSelectedIndex);

public static class PresentationMediaCaptionTrackNativeAdapter
{
    public static void Render<TItem>(
        PresentationMediaCaptionAuthoringPanePlan plan,
        PresentationMediaCaptionTrackNativeBindings<TItem> native)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(native);
        native.Clear();
        foreach (var (track, index) in plan.Tracks.Select((track, index) => (track, index)))
        {
            var item = native.CreateItem(track);
            native.ApplyAccessibility(
                item,
                PresentationPaneAccessibilityPlanner.PlanItem(
                    PresentationPaneAccessibilityPlanner.MediaCaptionPaneId,
                    index,
                    track.Label,
                    track.IsSelected,
                    track.AccessibilityKey));
            native.AddItem(item);
        }

        native.SetEnabled(plan.Tracks.Count > 0);
        native.SetSelectedIndex(plan.SelectedTrackListIndex);
    }
}

public sealed record PresentationMediaBookmarkNativeBindings<TItem>(
    Action Clear,
    Func<PresentationMediaBookmarkPaneItemPlan, TItem> CreateItem,
    Action<TItem> AddItem,
    Action<int> SetSelectedIndex,
    Action<string> SetName,
    Action<string> SetTime,
    Action<bool> SetListEnabled,
    Action<bool> SetNameEnabled,
    Action<bool> SetTimeEnabled,
    Action<bool> SetCreateEnabled,
    Action<bool> SetReplaceEnabled,
    Action<bool> SetDeleteEnabled);

public static class PresentationMediaBookmarkNativeAdapter
{
    public static void Render<TItem>(
        PresentationMediaPaneProjection plan,
        PresentationMediaBookmarkNativeBindings<TItem> native)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(native);
        native.Clear();
        foreach (var bookmark in plan.Bookmarks)
            native.AddItem(native.CreateItem(bookmark));
        native.SetSelectedIndex(plan.SelectedBookmarkIndex ?? -1);
        native.SetName(plan.BookmarkName);
        native.SetTime(plan.BookmarkTimeText);
        native.SetListEnabled(plan.HasMedia);
        native.SetNameEnabled(plan.HasMedia);
        native.SetTimeEnabled(plan.HasMedia);
        native.SetCreateEnabled(plan.HasMedia);
        native.SetReplaceEnabled(plan.HasSelectedBookmark);
        native.SetDeleteEnabled(plan.HasSelectedBookmark);
    }
}
