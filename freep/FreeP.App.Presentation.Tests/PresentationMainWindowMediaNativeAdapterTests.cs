using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationMainWindowMediaNativeAdapterTests
{
    [Fact]
    public void Button_map_preserves_visual_order_and_caption_action_routing()
    {
        var buttons = CreateButtons();

        buttons.InVisualOrder.Should().Equal(
            "caption-create", "caption-replace", "caption-delete",
            "volume", "playback", "timing",
            "bookmark-create", "bookmark-replace", "bookmark-delete", "close");
        buttons.Get(PresentationMediaPaneCaptionAction.Replace).Should().Be("caption-replace");
        buttons.Get(PresentationMediaPaneFormCommand.DeleteBookmark).Should().Be("bookmark-delete");
    }

    [Fact]
    public void Caption_control_map_resolves_fields_and_input_order()
    {
        var controls = new PresentationMediaPaneCaptionNativeControls<string, string>(
            "label-text", "label-input",
            "language-text", "language-input",
            "source-text", "source-input",
            "transcript-text", "transcript-input");

        controls.Get(PresentationMediaPaneCaptionField.Source).Should()
            .Be(("source-text", "source-input"));
        controls.Inputs.Should().Equal(
            "label-input", "language-input", "source-input", "transcript-input");
    }

    [Fact]
    public void Event_binder_routes_native_changes_by_semantic_command()
    {
        var controls = new PresentationMediaPaneCaptionNativeControls<string, string>(
            "label-text", "label-input",
            "language-text", "language-input",
            "source-text", "source-input",
            "transcript-text", "transcript-input");
        var inputActions = new List<Action>();
        var buttonActions = new Dictionary<string, Action>();
        var selectionActions = new Dictionary<string, Action>();
        var router = new RecordingRouter();

        PresentationMediaPaneFormEventBinder.Bind(
            "tracks",
            "bookmarks",
            controls,
            CreateButtons(),
            (_, action) => inputActions.Add(action),
            (button, action) => buttonActions.Add(button, action),
            (combo, action) => selectionActions.Add(combo, action),
            _ => 4,
            _ => 2,
            router);

        inputActions.Should().HaveCount(4);
        buttonActions.Should().HaveCount(10);
        inputActions[0]();
        selectionActions["tracks"]();
        selectionActions["bookmarks"]();
        buttonActions["close"]();
        router.Events.Should().Equal("refresh", "track:4", "bookmark:2", "command:Close");
    }

    private static PresentationMediaPaneNativeButtons<string> CreateButtons() => new(
        "volume", "playback", "timing",
        "bookmark-create", "bookmark-replace", "bookmark-delete",
        "caption-create", "caption-replace", "caption-delete", "close");

    private sealed class RecordingRouter : IPresentationMediaPaneFormEventRouter
    {
        public List<string> Events { get; } = [];

        public void SelectCaptionTrack(int? trackIndex) => Events.Add($"track:{trackIndex}");
        public void SelectBookmark(int? bookmarkIndex) => Events.Add($"bookmark:{bookmarkIndex}");
        public void Refresh() => Events.Add("refresh");
        public void Execute(PresentationMediaPaneFormCommand command) => Events.Add($"command:{command}");
    }
}
