using System.IO;
using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPresenterSessionDedupTests
{
    [Fact]
    public void PresenterViewVisualMetrics_PreserveTheWpfAuthorityGeometry()
    {
        PresentationPresenterViewVisualMetrics.WindowWidth.Should().Be(1200);
        PresentationPresenterViewVisualMetrics.WindowHeight.Should().Be(760);
        PresentationPresenterViewVisualMetrics.WindowMinimumWidth.Should().Be(860);
        PresentationPresenterViewVisualMetrics.WindowMinimumHeight.Should().Be(560);
        PresentationPresenterViewVisualMetrics.RootMargin.Should().Be(18);
        PresentationPresenterViewVisualMetrics.NotesRowHeight.Should().Be(180);
        PresentationPresenterViewVisualMetrics.SectionBottomMargin.Should().Be(14);
        PresentationPresenterViewVisualMetrics.HeaderFontSize.Should().Be(18);
        PresentationPresenterViewVisualMetrics.SlideNumberWidth.Should().Be(48);
        PresentationPresenterViewVisualMetrics.SlideNumberHeight.Should().Be(28);
        PresentationPresenterViewVisualMetrics.RecordingStatusFontSize.Should().Be(13);
        PresentationPresenterViewVisualMetrics.CurrentPreviewColumnWeight.Should().Be(2);
        PresentationPresenterViewVisualMetrics.NextPreviewColumnWeight.Should().Be(1);
        PresentationPresenterViewVisualMetrics.PreviewLabelFontSize.Should().Be(14);
        PresentationPresenterViewVisualMetrics.PreviewTitleFontSize.Should().Be(13);
        PresentationPresenterViewVisualMetrics.PreviewBorderThickness.Should().Be(1);
        PresentationPresenterViewVisualMetrics.PreviewPadding.Should().Be(10);
        PresentationPresenterViewVisualMetrics.NotesHeadingFontSize.Should().Be(14);
        PresentationPresenterViewVisualMetrics.ActionButtonMinimumWidth.Should().Be(78);
        PresentationPresenterViewVisualMetrics.PointerModeMinimumWidth.Should().Be(104);
    }

    [Fact]
    public void Session_ExecutesStopThenTimingMoveThenNativeNavigation()
    {
        var presentation = MakePresentation(2);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 5, 10, 0, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            new SlideShowDeterministicRecordingCaptureBackend("portable presenter test"));
        session.SetTimingIntent(SlideShowTimingIntent.RecordTimings, started);
        var events = new List<string>();

        var command = session.PlanAdvance(stopAutoAdvance: true);
        session.CurrentPresentationSlideIndex.Should().Be(0,
            "presenter timing follows the displayed slide until command execution");

        session.ExecuteHostCommand(
            command,
            started.AddMilliseconds(1500),
            new SlideShowHostExecutionCallbacks(
                () => events.Add("stop-auto-advance"),
                _ => events.Add("close"),
                _ => events.Add("play-step"),
                navigation =>
                {
                    events.Add($"navigate:{session.CurrentPresentationSlideIndex}");
                    navigation.SlideIndex.Should().Be(1);
                }));

        events.Should().Equal("stop-auto-advance", "navigate:1");
        session.TimingRecorderState.RecordedTimings.Should().ContainSingle()
            .Which.AdvanceAfterMs.Should().Be(1500);
        presentation.Slides[0].Transition!.AdvanceAfterMs.Should().Be(1500);
    }

    [Fact]
    public void Session_OwnsNumericJumpBufferAndBlackoutTransitions()
    {
        var presentation = MakePresentation(3);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 5, 11, 0, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable presenter test"));

        session.PlanKeyboardInput("D2").ShouldExecuteHostCommand.Should().BeFalse();
        session.SlideNumberBuffer.Should().Be("2");
        var jump = session.PlanKeyboardInput("Enter");
        jump.IsHandled.Should().BeTrue();
        jump.ShouldExecuteHostCommand.Should().BeTrue();
        session.SlideNumberBuffer.Should().BeEmpty();

        session.ExecuteHostCommand(
            jump.HostCommand,
            started.AddSeconds(1),
            NoOpCallbacks());
        session.CurrentPresentationSlideIndex.Should().Be(1);

        var black = session.PlanKeyboardInput("B");
        black.ScreenMode.Should().Be(SlideShowScreenMode.Black);
        session.ScreenMode.Should().Be(SlideShowScreenMode.Black);
        session.IsScreenBlank.Should().BeTrue();

        session.PlanKeyboardInput("B").ScreenMode.Should().Be(SlideShowScreenMode.Normal);
        session.IsScreenBlank.Should().BeFalse();

        session.PlanKeyboardInput("D3");
        session.PlanKeyboardInput("Escape").IsHandled.Should().BeTrue();
        session.SlideNumberBuffer.Should().BeEmpty();
    }

    [Fact]
    public void Session_RoutesPresenterHiddenSlideAndScreenKeysThroughPortableActions()
    {
        var presentation = MakePresentation(2);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 5, 11, 30, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable input test"));
        var events = new List<string>();
        var callbacks = new SlideShowSessionInputExecutionCallbacks(
            () => events.Add("presenter"),
            _ => events.Add("hidden-slide"),
            mode => events.Add($"screen:{mode}"),
            command => events.Add($"command:{command.Kind}"),
            hyperlink => events.Add($"external:{hyperlink.Url}"));

        var presenter = session.PlanKeyboardInput("P", controlPressed: true);
        presenter.ActionKind.Should().Be(SlideShowSessionInputActionKind.TogglePresenterView);
        presenter.IsHandled.Should().BeTrue();
        session.ExecuteInputPlan(presenter, callbacks);

        var hiddenSlide = session.PlanKeyboardInput("H");
        hiddenSlide.ActionKind.Should().Be(SlideShowSessionInputActionKind.RevealHiddenSlide);
        hiddenSlide.IsHandled.Should().BeTrue();
        session.ExecuteInputPlan(hiddenSlide, callbacks);

        var blackout = session.PlanKeyboardInput("B");
        blackout.ActionKind.Should().Be(SlideShowSessionInputActionKind.SetScreenMode);
        blackout.ScreenMode.Should().Be(SlideShowScreenMode.Black);
        session.ExecuteInputPlan(blackout, callbacks);

        events.Should().Equal("presenter", "hidden-slide", "screen:Black");
    }

    [Fact]
    public void Session_RoutesZoomAndHyperlinkClicksWithoutNativePolicyBranches()
    {
        var presentation = MakePresentation(2);
        presentation.Slides[0].NumericId = 256;
        presentation.Slides[1].NumericId = 257;
        presentation.Slides[0].Shapes.Add(new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Zoom,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 914400,
            PreservedObject = new PreservedObjectInfo
            {
                ObjectKind = PreservedObjectKind.Zoom,
                ZoomTargetSlideNumericId = 257,
                ZoomProperties = new ZoomObjectProperties(
                    TransitionDuration: "1200",
                    ShowBackground: false),
            },
        });
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var session = new SlideShowSessionController(
            presentation,
            route,
            new DateTimeOffset(2026, 8, 5, 11, 45, 0, TimeSpan.Zero),
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable input test"));

        var zoom = session.PlanPointerInput(new SlideShowCanvasPointer(
            48,
            48,
            960,
            540,
            new SlideShowSlideMetrics(960, 540)));

        zoom.ActionKind.Should().Be(SlideShowSessionInputActionKind.ExecuteHostCommand);
        zoom.IsHandled.Should().BeTrue();
        zoom.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        zoom.HostCommand.SlideIndex.Should().Be(1);
        zoom.HostCommand.TransitionDurationMs.Should().Be(1200);
        zoom.HostCommand.UseDestinationBackground.Should().BeFalse();

        var external = new Hyperlink { Url = "https://example.com" };
        var externalPlan = session.PlanHyperlinkActivation(external);
        externalPlan.ActionKind.Should().Be(SlideShowSessionInputActionKind.OpenExternalHyperlink);
        externalPlan.Hyperlink.Should().BeSameAs(external);

        var events = new List<string>();
        session.ExecuteInputPlan(
            externalPlan,
            new SlideShowSessionInputExecutionCallbacks(
                () => { },
                _ => { },
                _ => { },
                _ => events.Add("host-command"),
                hyperlink => events.Add($"external:{hyperlink.Url}")));
        events.Should().Equal("external:https://example.com");
    }

    [Fact]
    public void Session_BlankScreenGatesNavigationRevealAndPointerInput()
    {
        var presentation = MakePresentation(3);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var session = new SlideShowSessionController(
            presentation,
            route,
            DateTimeOffset.UtcNow,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable blank-screen test"));

        session.PlanKeyboardInput("B").ScreenMode.Should().Be(SlideShowScreenMode.Black);
        session.PlanKeyboardInput("Right").ActionKind.Should().Be(SlideShowSessionInputActionKind.None);
        session.PlanKeyboardInput("H").ActionKind.Should().Be(SlideShowSessionInputActionKind.None);
        session.PlanPointerInput(new SlideShowCanvasPointer(
                10,
                10,
                960,
                540,
                new SlideShowSlideMetrics(960, 540)))
            .ActionKind.Should().Be(SlideShowSessionInputActionKind.None);
        session.CurrentPresentationSlideIndex.Should().Be(0);

        var close = session.PlanKeyboardInput("Escape");
        close.ShouldExecuteHostCommand.Should().BeTrue();
        close.HostCommand.Kind.Should().Be(SlideShowHostCommandKind.Close);
    }

    [Fact]
    public void Session_HiddenHyperlinkRevealsTargetWithoutMovingPlaybackRoute()
    {
        var presentation = MakePresentation(3);
        presentation.Slides[1].IsHidden = true;
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var session = new SlideShowSessionController(
            presentation,
            route,
            DateTimeOffset.UtcNow,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable hidden-link test"));
        var hyperlink = new Hyperlink { TargetSlideId = presentation.Slides[1].Id };
        var navigated = new List<Hyperlink>();

        var plan = session.PlanHyperlinkActivation(hyperlink);
        plan.ActionKind.Should().Be(SlideShowSessionInputActionKind.RevealHiddenSlide);
        session.ExecuteInputPlan(
            plan,
            new SlideShowSessionInputExecutionCallbacks(
                () => { },
                targetSlideId => session.RevealHiddenSlide(targetSlideId),
                _ => { },
                _ => { },
                _ => { },
                navigated.Add));

        session.RevealedHiddenSlide.Should().BeSameAs(presentation.Slides[1]);
        session.DisplaySlide.Should().BeSameAs(presentation.Slides[1]);
        session.CurrentPresentationSlideIndex.Should().Be(0);
        navigated.Should().Equal(hyperlink);
    }

    [Fact]
    public void PresenterViewSession_CommitsNotesBeforeJumpAndOwnsToolToggles()
    {
        var presentation = MakePresentation(2);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var slideshow = new SlideShowSessionController(
            presentation,
            route,
            started,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable presenter test"));
        var events = new List<string>();
        SlideShowTimingIntent? timingIntent = null;
        SlideShowRecordingMediaIntent? mediaIntent = null;

        var presenter = new SlideShowPresenterViewSession(
            () => slideshow.CreatePresenterState(started.AddSeconds(65)),
            goBack: () => events.Add("back"),
            goNext: () => events.Add("next"),
            setTimingIntent: intent => timingIntent = intent,
            setMediaIntent: intent => mediaIntent = intent,
            goToSlide: slideNumber => events.Add($"jump:{slideNumber}"),
            setNotesText: (slideIndex, text) => events.Add($"notes:{slideIndex}:{text}"));

        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(
                SlideShowPresenterViewAction.GoToSlide,
                "2",
                NotesDirty: true,
                NotesText: "Updated notes"))
            .Should().Be(new SlideShowPresenterViewDispatchResult(true, true, true));
        events.Should().Equal("notes:0:Updated notes", "jump:2");

        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(
                SlideShowPresenterViewAction.RecordTimings))
            .Should().Be(new SlideShowPresenterViewDispatchResult(false, true, true));
        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(
                SlideShowPresenterViewAction.NarrationAndMedia))
            .Should().Be(new SlideShowPresenterViewDispatchResult(false, true, true));
        timingIntent.Should().Be(SlideShowTimingIntent.RecordTimings);
        mediaIntent.Should().Be(SlideShowRecordingMediaIntent.NarrationAndMedia);

        var plan = presenter.BuildViewPlan();
        plan.ElapsedText.Should().Be("01:05");
        plan.CurrentSlideNumber.Should().Be(1);
        plan.CurrentSlideNumberText.Should().Be("1");
        plan.CanGoBack.Should().BeFalse();
        plan.CanAdvance.Should().BeTrue();
        plan.CanSetTimingIntent.Should().BeTrue();
        plan.CanSetMediaIntent.Should().BeTrue();
    }

    [Fact]
    public void PresenterViewRefreshPlan_OwnsFocusSensitiveNoteCommitAndFieldProjection()
    {
        var presentation = MakePresentation(2);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 5, 12, 30, 0, TimeSpan.Zero);
        var slideshow = new SlideShowSessionController(
            presentation,
            route,
            started,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable presenter refresh test"));
        var notes = new List<string?>();
        var presenter = new SlideShowPresenterViewSession(
            () => slideshow.CreatePresenterState(started),
            setNotesText: (_, text) => notes.Add(text));

        var focused = presenter.BuildRefreshPlan(new SlideShowPresenterViewRefreshRequest(
            NotesFocused: true,
            NotesDirty: true,
            NotesText: "Pending",
            SlideNumberFocused: true));

        focused.NotesCommitted.Should().BeFalse();
        focused.ShouldUpdateNotesText.Should().BeFalse();
        focused.ShouldUpdateSlideNumber.Should().BeFalse();
        notes.Should().BeEmpty();

        var unfocused = presenter.BuildRefreshPlan(new SlideShowPresenterViewRefreshRequest(
            NotesFocused: false,
            NotesDirty: true,
            NotesText: "Committed",
            SlideNumberFocused: false));

        unfocused.NotesCommitted.Should().BeTrue();
        unfocused.ShouldUpdateNotesText.Should().BeTrue();
        unfocused.ShouldUpdateSlideNumber.Should().BeTrue();
        unfocused.ViewPlan.CurrentSlideNumber.Should().Be(1);
        unfocused.ViewPlan.CurrentSlideNumberText.Should().Be("1");
        notes.Should().Equal("Committed");
    }

    [Fact]
    public void PresenterViewRefreshPlan_CommitsNotesToThePresentationSlideThatPopulatedTheEditor()
    {
        var presentation = MakePresentation(3);
        var route = new SlideShowPlaybackRoute(
            "Reordered show",
            [presentation.Slides[2], presentation.Slides[0]],
            [2, 0],
            startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);
        var slideshow = new SlideShowSessionController(
            presentation,
            route,
            started,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable presenter note binding test"));
        var committed = new List<(int SlideIndex, string? Text)>();
        var presenter = new SlideShowPresenterViewSession(
            () => slideshow.CreatePresenterState(started),
            setNotesText: (slideIndex, text) => committed.Add((slideIndex, text)));

        var initialState = slideshow.CreatePresenterState(started);
        initialState.CurrentSlide.Should().NotBeNull();
        initialState.CurrentSlide!.SlideIndex.Should().Be(0);
        initialState.CurrentSlide.PresentationSlideIndex.Should().Be(2);
        initialState.NextSlide.Should().NotBeNull();
        initialState.NextSlide!.SlideIndex.Should().Be(1);
        initialState.NextSlide.PresentationSlideIndex.Should().Be(0);

        presenter.BuildRefreshPlan(new SlideShowPresenterViewRefreshRequest(
            NotesFocused: false,
            NotesDirty: false,
            NotesText: string.Empty,
            SlideNumberFocused: false));
        slideshow.ExecuteHostCommand(
            slideshow.PlanAdvance(stopAutoAdvance: true),
            started.AddSeconds(1),
            NoOpCallbacks());

        presenter.CommitNotes(notesDirty: true, notesText: "Edited during auto-advance").Should().BeTrue();
        slideshow.CurrentPresentationSlideIndex.Should().Be(0);
        committed.Should().Equal((2, "Edited during auto-advance"));
    }

    [Fact]
    public void PresenterViewSession_DefinesSharedRefreshCadence()
    {
        SlideShowPresenterViewSession.RefreshInterval.Should()
            .Be(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void PresenterViewSurface_OwnsLabelsActionsAndAccessibilitySemantics()
    {
        var surface = SlideShowPresenterViewSurfaceCatalog.Surface;

        surface.Title.Should().Be("Presenter View");
        surface.Schema.Fields.Select(field => field.Id).Should().OnlyHaveUniqueItems();
        surface.Schema.Actions.Select(action => action.Id).Should().OnlyHaveUniqueItems();
        surface.Schema.Fields.Select(field => field.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Schema.Actions.Select(action => action.AutomationId).Should().OnlyHaveUniqueItems();
        surface.Schema.Fields.Should().OnlyContain(field =>
            !string.IsNullOrWhiteSpace(field.AccessibleName) &&
            !string.IsNullOrWhiteSpace(field.AutomationId));
        surface.Schema.Actions.Should().OnlyContain(action =>
            !string.IsNullOrWhiteSpace(action.AccessibleName) &&
            !string.IsNullOrWhiteSpace(action.AutomationId));
        surface.Action(SlideShowPresenterViewAction.GoToSlide).IsDefault.Should().BeTrue();
        surface.Field(SlideShowPresenterViewField.SlideNumber).HelpText.Should()
            .Be("Enter a slide number and activate Go.");
        surface.FormatElapsed("01:05").Should().Be("Elapsed 01:05");
    }

    [Fact]
    public void PresenterViewDispatch_OwnsNavigationScreenAndRecordingActions()
    {
        var presentation = MakePresentation(2);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);
        var slideshow = new SlideShowSessionController(
            presentation,
            route,
            started,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("portable presenter test"));
        var events = new List<string>();

        var presenter = new SlideShowPresenterViewSession(
            () => slideshow.CreatePresenterState(started),
            goBack: () => events.Add("back"),
            goNext: () => events.Add("next"),
            setScreenMode: mode => events.Add($"screen:{mode}"),
            clearInk: () => events.Add("clear-ink"),
            setTimingIntent: intent => events.Add($"timing:{intent}"),
            setMediaIntent: intent => events.Add($"media:{intent}"),
            applyRecordingReview: () =>
            {
                events.Add("apply-recording");
                return new SlideShowRecordingReviewApplyResult(1, 1);
            },
            setNotesText: (slideIndex, text) => events.Add($"notes:{slideIndex}:{text}"));

        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(
                SlideShowPresenterViewAction.Previous,
                NotesDirty: true,
                NotesText: "Notes"))
            .Should().Be(new SlideShowPresenterViewDispatchResult(true, true, true));
        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(SlideShowPresenterViewAction.Next))
            .Should().Be(new SlideShowPresenterViewDispatchResult(false, true, true));
        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(SlideShowPresenterViewAction.RehearseTimings))
            .Should().Be(new SlideShowPresenterViewDispatchResult(false, true, true));
        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(SlideShowPresenterViewAction.Narration))
            .Should().Be(new SlideShowPresenterViewDispatchResult(false, true, true));
        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(SlideShowPresenterViewAction.ApplyRecording))
            .Should().Be(new SlideShowPresenterViewDispatchResult(false, true, true));
        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(SlideShowPresenterViewAction.ShowScreen));
        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(SlideShowPresenterViewAction.BlackScreen));
        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(SlideShowPresenterViewAction.WhiteScreen));
        presenter.Dispatch(new SlideShowPresenterViewDispatchRequest(SlideShowPresenterViewAction.ClearInk));

        events.Should().Equal(
            "notes:0:Notes",
            "back",
            "next",
            "timing:RehearseTimings",
            "media:Narration",
            "apply-recording",
            "screen:Normal",
            "screen:Black",
            "screen:White",
            "clear-ink");
    }

    [Fact]
    public void NativeHosts_KeepOnlyRendererProjectionInputAndWindowingResponsibilities()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var presenterFiles = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "PresenterViewWindow.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "PresenterViewWindow.cs"),
        };
        var slideShowFiles = new[]
        {
            Read(root, "freep", "FreeP.App.Host", "SlideShowWindow.cs"),
            Read(root, "freep", "FreeP.App.Avalonia", "SlideShowWindow.cs"),
        };

        foreach (var source in presenterFiles)
        {
            source.Should().Contain("SlideShowPresenterViewHostCoordinator");
            source.Should().Contain("SlideShowPresenterViewOperations operations");
            source.Should().Contain("new SlideShowPresenterViewHostCoordinator(operations)");
            source.Should().NotContain("Func<SlideShowPresenterState> stateProvider");
            source.Should().Contain("_coordinator.Refresh(new SlideShowPresenterViewHostRefreshInput(");
            source.Should().Contain("_notesText.IsKeyboardFocusWithin");
            source.Should().Contain("_slideNumberBox.IsKeyboardFocusWithin");
            source.Should().Contain("WindowStartupLocation = WindowStartupLocation.CenterOwner");
            source.Should().Contain("_coordinator.Surface");
            source.Should().Contain("_coordinator.ExecuteAction(");
            source.Should().Contain("_coordinator.Surface.FormatElapsed(");
            source.Should().Contain("_coordinator.NotifyNotesTextChanged()");
            source.Should().Contain("_coordinator.CommitNotes(");
            source.Should().Contain("_coordinator.SelectPointerMode(");
            source.Should().Contain("AutomationProperties.SetName(");
            source.Should().Contain("AutomationProperties.SetAutomationId(");
            source.Should().Contain("DispatcherTimer");
            source.Should().Contain("Interval = SlideShowPresenterViewHostCoordinator.RefreshInterval");
            source.Should().Contain("SlideCanvas");
            source.Should().Contain("if (refresh.ShouldUpdateNotesText)");
            source.Should().Contain("if (refresh.ShouldUpdateSlideNumber");
            source.Should().Contain("plan.CurrentSlideNumberText");
            source.Should().Contain("PresentationPresenterViewVisualMetrics.WindowWidth");
            source.Should().Contain("PresentationPresenterViewVisualMetrics.RootMargin");
            source.Should().Contain("PresentationPresenterViewVisualMetrics.SlideNumberWidth");
            source.Should().Contain("PresentationPresenterViewVisualMetrics.PreviewPadding");
            source.Should().Contain("PresentationPresenterViewVisualMetrics.ActionButtonMinimumWidth");
            source.Should().Contain("_pointerModeCombo.SelectedItem = plan.PointerMode;");
            source.Should().NotContain("currentSlideNumber.ToString(");
            source.Should().NotContain("TimeSpan.FromMilliseconds(250)");
            source.Should().NotContain("if (!_notesText.IsFocused");
            source.Should().NotContain("if (!_notesText.IsKeyboardFocusWithin");
            source.Should().NotContain("if (!_slideNumberBox.IsFocused");
            source.Should().NotContain("if (!_slideNumberBox.IsKeyboardFocusWithin");
            source.Should().NotContain("SlideShowPresenterPointerMode.");
            source.Should().NotContain("SlideShowSlideNumberPlanner");
            source.Should().NotContain("BuildRecordingSummary");
            source.Should().NotContain("TotalArtifactCount");
            source.Should().NotContain("private readonly Func<SlideShowPresenterState> _stateProvider");
            source.Should().NotContain("SlideShowPresenterViewSession");
            source.Should().NotContain("SlideShowPresenterViewDispatchRequest");
            source.Should().NotContain("SlideShowPresenterViewRefreshRequest");
            source.Should().NotContain("_notesDirty");
            source.Should().NotContain("_refreshing");
            foreach (var sharedLiteral in new[]
            {
                "\"Presenter View\"",
                "\"Previous\"",
                "\"Next\"",
                "\"Record timings\"",
                "\"Rehearse timings\"",
                "\"Narration + camera\"",
                "\"Apply recording\"",
                "\"Speaker notes\"",
            })
            {
                source.Should().NotContain(sharedLiteral);
            }
        }

        presenterFiles[0].Should().Contain(
            "VerticalScrollBarVisibility = ScrollBarVisibility.Auto");
        presenterFiles[1].Should().Contain(
            "ScrollViewer.VerticalScrollBarVisibilityProperty");
        presenterFiles[1].Should().Contain("ScrollBarVisibility.Auto");

        foreach (var source in slideShowFiles)
        {
            source.Should().Contain("private readonly SlideShowRuntimeApplication _runtime;");
            source.Should().Contain("_runtime.BindRenderer(new SlideShowRuntimeRendererCallbacks(");
            source.Should().Contain("_runtime.HandleKeyboardInput(");
            source.Should().Contain("_runtime.HandlePointerInput(");
            source.Should().Contain("_runtime.ActivateHyperlink(");
            source.Should().Contain("_runtime.DisplayCurrentSlide(");
            source.Should().Contain("_runtime.StartRendererSession(");
            source.Should().Contain("_runtime.DisplaySlide");
            source.Should().Contain("_runtime.CreatePresenterViewOperations(_setSlideNotesText)");
            source.Should().Contain("var windowPlan = _runtime.WindowPlan;");
            source.Should().Contain("windowPlan.PlanBrowseWindowSize(");
            source.Should().Contain("DispatcherTimer");
            source.Should().Contain("_screenModeOverlay");
            source.Should().Contain("_runtime.AnimationRendererSession.PlanStep(");
            source.Should().Contain("_runtime.AnimationRendererSession.ExecuteStep(");
            source.Should().NotContain("SlideShowPlaybackPlanner.PlanAnimationStep(");
            source.Should().NotContain("foreach (var operation in rendererPlan.Operations)");
            source.Should().NotContain("SlideShowSessionController");
            source.Should().NotContain("SlideShowSessionInputExecutionCallbacks");
            source.Should().NotContain("SlideShowHostExecutionCallbacks");
            source.Should().NotContain("PresentationMediaTranscriptPlanner");
            source.Should().NotContain("SlideShowKioskRestartPlanner");
            source.Should().NotContain("_presentation.ShowType");
            source.Should().NotContain("_presentation.ShowBrowseScrollbar");
            source.Should().NotContain("_presentation.ShowMediaControls");
            source.Should().NotContain("_presentation.ShowWithNarration");
            source.Should().NotContain("Math.Min(1024");
            source.Should().NotContain("Width = 1024;");
            source.Should().NotContain("Height = 768;");
            source.Should().NotContain("private readonly SlideShowController _controller");
            source.Should().NotContain("private string _slideNumberBuffer");
            source.Should().NotContain("private SlideShowScreenMode _screenMode");
            source.Should().NotContain("SlideShowScreenModePlanner.TryPlanKey(");
            source.Should().NotContain("SlideShowHostPlanner.PlanSlideNumberJump(");
            source.Should().NotContain("case SlideShowPointerClickIntentKind.");
            source.Should().NotContain("if (e.Key == Key.P");
            source.Should().NotContain("if (hlink.IsExternal)");
        }
    }

    private static SlideShowHostExecutionCallbacks NoOpCallbacks() => new(
        () => { },
        _ => { },
        _ => { },
        _ => { });

    private static Presentation MakePresentation(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        while (presentation.Slides.Count < slideCount)
        {
            presentation.Slides.Add(new Slide { Title = $"Slide {presentation.Slides.Count + 1}" });
        }

        return presentation;
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
