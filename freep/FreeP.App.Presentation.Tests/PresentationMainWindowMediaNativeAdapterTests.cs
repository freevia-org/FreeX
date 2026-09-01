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

        // r192: every button, not just Close. This asserted a COUNT of ten bindings and then
        // exercised one of them, so swapping any two cases in the Get(PresentationMediaPaneFormCommand)
        // switch left the set a permutation of the same ten distinct buttons -- HaveCount(10) still
        // passed, the dictionary never collided, and the one checked entry was untouched. Clicking
        // "Apply Volume" would have run Apply Timing on BOTH shells with the suite still green.
        // The expectation is a FIXED table of button name to command, not `buttons.Get(command)`.
        // Routing the assertion through Get would use the very switch under test on both sides, so
        // swapping two of its cases moves the expectation with the behaviour and the test stays
        // green -- measured: it does. The button names come from CreateButtons() below, in the
        // order PresentationMediaPaneNativeButtons declares them.
        var expected = new (string Button, PresentationMediaPaneFormCommand Command)[]
        {
            ("volume", PresentationMediaPaneFormCommand.ApplyVolume),
            ("playback", PresentationMediaPaneFormCommand.ApplyPlayback),
            ("timing", PresentationMediaPaneFormCommand.ApplyTiming),
            ("bookmark-create", PresentationMediaPaneFormCommand.CreateBookmark),
            ("bookmark-replace", PresentationMediaPaneFormCommand.ReplaceBookmark),
            ("bookmark-delete", PresentationMediaPaneFormCommand.DeleteBookmark),
            ("caption-create", PresentationMediaPaneFormCommand.CreateCaption),
            ("caption-replace", PresentationMediaPaneFormCommand.ReplaceCaption),
            ("caption-delete", PresentationMediaPaneFormCommand.DeleteCaption),
            ("close", PresentationMediaPaneFormCommand.Close),
        };

        expected.Should().HaveCount(
            Enum.GetValues<PresentationMediaPaneFormCommand>().Length,
            "every command must be covered, including any added later");

        foreach (var (button, command) in expected)
        {
            router.Events.Clear();
            buttonActions[button]();
            router.Events.Should().ContainSingle().Which.Should().Be(
                $"command:{command}",
                "the {0} button must raise exactly that command",
                button);
        }
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
