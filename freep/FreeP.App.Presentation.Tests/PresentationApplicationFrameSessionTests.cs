using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationApplicationFrameSessionTests
{
    [Fact]
    public void ExecuteCommandRoutesEveryApplicationCommandExactlyOnce()
    {
        var executed = new List<FreePKeyboardCommand>();
        Action Track(FreePKeyboardCommand command) => () => executed.Add(command);
        var session = new PresentationApplicationFrameSession(
            CreateFrameCallbacks(new List<string>()),
            new PresentationApplicationCommandCallbacks(
                Track(FreePKeyboardCommand.NewPresentation),
                Track(FreePKeyboardCommand.OpenPresentation),
                Track(FreePKeyboardCommand.SavePresentation),
                Track(FreePKeyboardCommand.SavePresentationAs),
                Track(FreePKeyboardCommand.PrintPresentation),
                Track(FreePKeyboardCommand.Undo),
                Track(FreePKeyboardCommand.Redo),
                Track(FreePKeyboardCommand.DeleteSelectedShapes),
                Track(FreePKeyboardCommand.DuplicateCurrentSlide),
                Track(FreePKeyboardCommand.StartSlideShowFromBeginning),
                Track(FreePKeyboardCommand.StartSlideShowFromCurrentSlide),
                Track(FreePKeyboardCommand.Copy),
                Track(FreePKeyboardCommand.Cut),
                Track(FreePKeyboardCommand.Paste),
                Track(FreePKeyboardCommand.Find),
                Track(FreePKeyboardCommand.Replace),
                Track(FreePKeyboardCommand.SelectAll)));

        foreach (var command in Enum.GetValues<FreePKeyboardCommand>())
            session.ExecuteCommand(command);

        executed.Should().Equal(Enum.GetValues<FreePKeyboardCommand>());
    }

    [Fact]
    public void EditorChangedOwnsTheOrderedSharedWorkareaRefreshPlan()
    {
        var calls = new List<string>();
        var session = CreateSession(calls);

        session.HandleEditorChanged();

        calls.Should().Equal(
            "before-editor-change",
            "mark-dirty",
            "after-mark-dirty",
            "command-state",
            "slide-pane",
            "canvas",
            "notes",
            "status-before-review",
            "review-plans",
            "smart-art",
            "animation-editor-change",
            "selection-pane",
            "accessibility",
            "status-after-review");
    }

    [Fact]
    public void CurrentSlideChangedOwnsSelectionResetAndWorkareaCoordination()
    {
        var calls = new List<string>();
        var session = CreateSession(calls);

        session.HandleCurrentSlideChanged();

        calls.Should().Equal(
            "before-slide-change",
            "clear-review-selection",
            "reset-animation-selection",
            "clear-media-selection",
            "command-state",
            "sync-slide-pane-selection",
            "slide-pane-chrome",
            "canvas",
            "notes",
            "review-pane-before",
            "review-plans",
            "review-pane-after",
            "media-pane",
            "animation-navigation",
            "selection-pane",
            "accessibility",
            "current-slide-status");
    }

    [Fact]
    public void SelectionChangedOwnsPortablePaneProjectionAndVisibilityDecisions()
    {
        var visibleCalls = new List<string>();
        var visible = CreateSession(visibleCalls, smartArtVisible: true, altTextVisible: true);

        visible.HandleEditorSelectionChanged();

        visibleCalls.Should().Equal(
            "command-state",
            "alt-text-request",
            "reading-order",
            "alt-text",
            "smart-art",
            "media-pane",
            "animation-selection",
            "selection-pane",
            "accessibility");

        var hiddenCalls = new List<string>();
        var hidden = CreateSession(hiddenCalls, smartArtVisible: false, altTextVisible: false);

        hidden.HandleEditorSelectionChanged();

        hiddenCalls.Should().NotContain("alt-text").And.NotContain("smart-art");
    }

    [Fact]
    public void ActiveTableCellChangedRefreshesOnlyCommandState()
    {
        var calls = new List<string>();
        var session = CreateSession(calls);

        session.HandleActiveTableCellChanged();

        calls.Should().Equal("command-state");
    }

    [Fact]
    public void AttachMovesLifecycleSubscriptionsToTheReplacementEditorWithoutDuplicates()
    {
        var calls = new List<string>();
        var session = CreateSession(calls);
        var first = CreateEditor();
        var replacement = CreateEditor();

        session.Attach(first);
        session.Attach(first);
        first.Select(1);

        calls.Should().HaveCount(9);

        calls.Clear();
        session.Attach(replacement);
        first.Select(2);
        calls.Should().BeEmpty();

        replacement.Select(3);
        calls.Should().Equal(
            "command-state",
            "alt-text-request",
            "reading-order",
            "alt-text",
            "smart-art",
            "media-pane",
            "animation-selection",
            "selection-pane",
            "accessibility");
    }

    private static PresentationApplicationFrameSession CreateSession(
        List<string> calls,
        bool smartArtVisible = true,
        bool altTextVisible = true) =>
        new(
            CreateFrameCallbacks(calls, smartArtVisible, altTextVisible),
            new PresentationApplicationCommandCallbacks(
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction,
                NoAction));

    private static PresentationApplicationFrameCallbacks CreateFrameCallbacks(
        List<string> calls,
        bool smartArtVisible = true,
        bool altTextVisible = true)
    {
        Action Track(string name) => () => calls.Add(name);
        return new PresentationApplicationFrameCallbacks
        {
            BeforeEditorChanged = Track("before-editor-change"),
            MarkDirty = Track("mark-dirty"),
            AfterEditorMarkedDirty = Track("after-mark-dirty"),
            RefreshCommandStates = Track("command-state"),
            RefreshSlidePane = Track("slide-pane"),
            RefreshCanvas = Track("canvas"),
            RefreshNotesPane = Track("notes"),
            RefreshDocumentStatusBeforeReview = Track("status-before-review"),
            RefreshReviewWorkflowPlans = Track("review-plans"),
            IsSmartArtPaneVisible = () => smartArtVisible,
            RefreshSmartArtPane = Track("smart-art"),
            RefreshAnimationPaneAfterEditorChanged = Track("animation-editor-change"),
            RefreshAnimationPaneAfterNavigation = Track("animation-navigation"),
            RefreshAnimationPaneAfterSelection = Track("animation-selection"),
            RefreshSelectionPane = Track("selection-pane"),
            RefreshAccessibilityMetadata = Track("accessibility"),
            RefreshDocumentStatusAfterReview = Track("status-after-review"),
            BeforeCurrentSlideChanged = Track("before-slide-change"),
            ClearReviewSelection = Track("clear-review-selection"),
            ResetAnimationSelection = Track("reset-animation-selection"),
            ClearMediaSelection = Track("clear-media-selection"),
            SyncSlidePaneSelection = Track("sync-slide-pane-selection"),
            RefreshSlidePaneChrome = Track("slide-pane-chrome"),
            RefreshReviewPaneBeforePlans = Track("review-pane-before"),
            RefreshReviewPaneAfterPlans = Track("review-pane-after"),
            RefreshVisibleMediaPane = Track("media-pane"),
            RefreshCurrentSlideStatus = Track("current-slide-status"),
            RefreshAltTextRequest = Track("alt-text-request"),
            RefreshReadingOrder = Track("reading-order"),
            IsAltTextPaneVisible = () => altTextVisible,
            RefreshAltTextPane = Track("alt-text"),
        };
    }

    private static EditingSession CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static void NoAction()
    {
    }
}
