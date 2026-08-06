using System.Globalization;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationMediaPaneSessionTests
{
    [Fact]
    public void TimingPlans_UseCurrentCultureAndNormalizeInvalidValues()
    {
        var formatted = PresentationMediaPaneSession.FormatTiming(12.34567);

        formatted.Should().Be(12.34567.ToString("0.####", CultureInfo.CurrentCulture));
        PresentationMediaPaneSession.ParseTiming(formatted).Should().BeApproximately(12.3457, 0.00001);
        PresentationMediaPaneSession.ParseTiming("-25").Should().Be(0);
        PresentationMediaPaneSession.ParseTiming("not a time").Should().Be(0);
        PresentationMediaPaneSession.ParseTiming("NaN").Should().Be(0);

        var input = PresentationMediaPaneSession.BuildTimingInputPlan(10, 20.5, -5, 7.25);
        input.TrimStartText.Should().Be(PresentationMediaPaneSession.FormatTiming(10));
        input.TrimEndText.Should().Be(PresentationMediaPaneSession.FormatTiming(20.5));
        input.FadeInText.Should().Be(PresentationMediaPaneSession.FormatTiming(0));
        input.FadeOutText.Should().Be(PresentationMediaPaneSession.FormatTiming(7.25));
        PresentationMediaPaneSession.NormalizeVolumePercent(24.6).Should().Be(25);
        PresentationMediaPaneSession.NormalizeVolumePercent(150).Should().Be(100);
        PresentationMediaPaneSession.GetPlaybackStartModeIndex(MediaPlaybackStartMode.Automatically).Should().Be(1);
        PresentationMediaPaneSession.GetPlaybackStartMode(0).Should().Be(MediaPlaybackStartMode.InClickSequence);
    }

    [Fact]
    public void Projection_NormalizesBookmarkSelectionAndProvidesRendererReadyState()
    {
        var (editor, media) = CreateSelectedMediaEditor();
        media.VolumePercent = 35;
        media.PlaybackStartMode = MediaPlaybackStartMode.Automatically;
        media.Loop = true;
        media.ShowWhenStopped = false;
        media.RewindAfterPlaying = true;
        media.PlayFullScreen = true;
        media.StopAfterSlides = 3;
        media.TrimStartMilliseconds = 125;
        media.Bookmarks.Add(new MediaBookmarkInfo { Name = "Intro", TimeMilliseconds = 400 });
        media.Bookmarks.Add(new MediaBookmarkInfo { Name = "Demo", TimeMilliseconds = 900 });
        var session = CreateSession(editor);
        session.SelectBookmark(42);

        var plan = session.BuildProjection();

        plan.HasMedia.Should().BeTrue();
        plan.VolumePercent.Should().Be(35);
        plan.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.Automatically);
        plan.Loop.Should().BeTrue();
        plan.ShowWhenStopped.Should().BeFalse();
        plan.RewindAfterPlaying.Should().BeTrue();
        plan.PlayFullScreen.Should().BeTrue();
        plan.StopAfterSlides.Should().Be(3);
        plan.CanPlayFullScreen.Should().BeTrue();
        plan.CanStopAfterSlides.Should().BeFalse();
        plan.Timing.TrimStartText.Should().Be(PresentationMediaPaneSession.FormatTiming(125));
        plan.Bookmarks.Select(bookmark => bookmark.DisplayText)
            .Should().Equal("1. Intro", "2. Demo");
        plan.SelectedBookmarkIndex.Should().Be(0);
        plan.BookmarkName.Should().Be("Intro");
        plan.BookmarkTimeText.Should().Be(PresentationMediaPaneSession.FormatTiming(400));
        session.SelectedBookmarkIndex.Should().Be(0);
    }

    [Fact]
    public void BookmarkMutationPlans_CloneValidateAndNormalizeSelection()
    {
        var media = new MediaInfo();
        media.Bookmarks.Add(new MediaBookmarkInfo { Name = "Intro", TimeMilliseconds = 100 });
        media.Bookmarks.Add(new MediaBookmarkInfo { Name = "Middle", TimeMilliseconds = 200 });

        var create = PresentationMediaPaneSession.BuildBookmarkMutationPlan(
            media,
            PresentationMediaBookmarkMutationIntentKind.Create,
            selectedBookmarkIndex: 0,
            "  End  ",
            PresentationMediaPaneSession.FormatTiming(300));
        var replace = PresentationMediaPaneSession.BuildBookmarkMutationPlan(
            media,
            PresentationMediaBookmarkMutationIntentKind.Replace,
            selectedBookmarkIndex: 1,
            "Updated",
            PresentationMediaPaneSession.FormatTiming(250));
        var delete = PresentationMediaPaneSession.BuildBookmarkMutationPlan(
            media,
            PresentationMediaBookmarkMutationIntentKind.Delete,
            selectedBookmarkIndex: 1,
            null,
            null);
        var invalid = PresentationMediaPaneSession.BuildBookmarkMutationPlan(
            media,
            PresentationMediaBookmarkMutationIntentKind.Replace,
            selectedBookmarkIndex: 99,
            "Missing",
            "10");

        create.ShouldApply.Should().BeTrue();
        create.SelectedBookmarkIndex.Should().Be(2);
        create.Bookmarks[2].Should().Match<MediaBookmarkInfo>(bookmark =>
            bookmark.Name == "End" && bookmark.TimeMilliseconds == 300);
        replace.ShouldApply.Should().BeTrue();
        replace.Bookmarks[1].Should().Match<MediaBookmarkInfo>(bookmark =>
            bookmark.Name == "Updated" && bookmark.TimeMilliseconds == 250);
        delete.ShouldApply.Should().BeTrue();
        delete.SelectedBookmarkIndex.Should().Be(0);
        delete.Bookmarks.Should().ContainSingle().Which.Name.Should().Be("Intro");
        invalid.ShouldApply.Should().BeFalse();
        invalid.SelectedBookmarkIndex.Should().Be(0);
        media.Bookmarks.Select(bookmark => bookmark.Name).Should().Equal("Intro", "Middle");
    }

    [Fact]
    public void ApplyMethods_CommitThroughEditorAndRunHostCallbacks()
    {
        var (editor, media) = CreateSelectedMediaEditor();
        var callbackCount = 0;
        var session = CreateSession(editor, () => callbackCount++);

        session.ApplyVolume(135).Should().BeTrue();
        session.ApplyPlayback(
            MediaPlaybackStartMode.Automatically,
            loop: true,
            showWhenStopped: false,
            rewindAfterPlaying: true,
            playFullScreen: true,
            stopAfterSlides: 3).Should().BeTrue();
        session.ApplyTiming("125", "250", "500", "750").Should().BeTrue();
        session.ApplyBookmark(
            PresentationMediaBookmarkMutationIntentKind.Create,
            "Chapter",
            "900").Should().BeTrue();

        media.VolumePercent.Should().Be(100);
        media.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.Automatically);
        media.Loop.Should().BeTrue();
        media.ShowWhenStopped.Should().BeFalse();
        media.RewindAfterPlaying.Should().BeTrue();
        media.PlayFullScreen.Should().BeTrue();
        media.StopAfterSlides.Should().Be(3);
        media.TrimStartMilliseconds.Should().Be(125);
        media.TrimEndMilliseconds.Should().Be(250);
        media.FadeInMilliseconds.Should().Be(500);
        media.FadeOutMilliseconds.Should().Be(750);
        media.Bookmarks.Should().ContainSingle().Which.Should().Match<MediaBookmarkInfo>(bookmark =>
            bookmark.Name == "Chapter" && bookmark.TimeMilliseconds == 900);
        session.SelectedBookmarkIndex.Should().Be(0);
        callbackCount.Should().Be(16);
        editor.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void Projection_ExposesAudioOnlyAcrossSlideCapability()
    {
        var (editor, media) = CreateSelectedMediaEditor();
        media.IsVideo = false;

        var plan = CreateSession(editor).BuildProjection();

        plan.CanPlayFullScreen.Should().BeFalse();
        plan.CanStopAfterSlides.Should().BeTrue();
    }

    [Fact]
    public void CaptionAuthoring_OwnsMutationPlanResultAndSelectionLifecycle()
    {
        var (editor, media) = CreateSelectedMediaEditor();
        var callbackCount = 0;
        var session = CreateSession(editor, () => callbackCount++);

        var created = session.ApplyCaptionAuthoring(
            PresentationMediaCaptionAuthoringIntentKind.Create,
            "English",
            "en-US",
            "captions.vtt",
            "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nHello");

        created.Succeeded.Should().BeTrue();
        session.LastCaptionAuthoringMutationPlan.Should().NotBeNull();
        session.LastCaptionTrackMutationResult.Should().BeSameAs(created);
        session.SelectedCaptionTrackIndex.Should().Be(0);
        media.CaptionTracks.Should().ContainSingle();

        session.SelectCaptionTrack(0);
        var deleted = session.ApplyCaptionAuthoring(
            PresentationMediaCaptionAuthoringIntentKind.Delete,
            null,
            null,
            null,
            null);

        deleted.Succeeded.Should().BeTrue();
        media.CaptionTracks.Should().BeEmpty();
        session.SelectedCaptionTrackIndex.Should().BeNull();
        callbackCount.Should().Be(8);
    }

    [Fact]
    public void MainWindowSourceGuards_KeepMediaSemanticsInPresentationSession()
    {
        var root = FindWorkspaceRoot();
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("private readonly PresentationMediaPaneSession _mediaPaneSession;");
            source.Should().Contain("_mediaPaneSession.ApplyCaptionAuthoring(");
            source.Should().Contain("_mediaPaneSession.ApplyVolume(");
            source.Should().Contain("_mediaPaneSession.ApplyPlayback(");
            source.Should().Contain("_mediaPaneSession.ApplyTiming(");
            source.Should().Contain("_mediaPaneSession.ApplyBookmark(");
            source.Should().Contain("_mediaPaneSession.BuildProjection()");
            source.Should().Contain("RenderMediaCaptionPane(");
            source.Should().Contain("RenderMediaBookmarkOptions(");
            source.Should().NotContain("private static double ParseMediaTiming(");
            source.Should().NotContain("private static string FormatMediaTiming(");
            source.Should().NotContain("CloneMediaBookmarksForPane(");
            source.Should().NotContain("NormalizeMediaCaptionSelectionAfterMutation(");
            source.Should().NotContain("Editor.SetSelectedMediaVolume(");
            source.Should().NotContain("Editor.SetSelectedMediaPlaybackOptions(");
            source.Should().NotContain("Editor.SetSelectedMediaTiming(");
            source.Should().NotContain("Editor.SetSelectedMediaBookmarks(");
            source.Should().NotContain("Editor.ApplyMediaCaptionAuthoring(");
        }
    }

    private static PresentationMediaPaneSession CreateSession(
        EditingSession editor,
        Action? callback = null)
    {
        callback ??= () => { };
        return new PresentationMediaPaneSession(
            () => editor,
            new PresentationMediaPaneSessionCallbacks(callback, callback, callback, callback));
    }

    private static (EditingSession Editor, MediaInfo Media) CreateSelectedMediaEditor()
    {
        var presentation = Presentation.CreateEmpty();
        var media = new MediaInfo { IsVideo = true };
        var shape = new SlideShape
        {
            Id = 42,
            Name = "Video",
            Kind = SlideShapeKind.Media,
            Media = media
        };
        presentation.Slides[0].Shapes.Add(shape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(shape.Id);
        return (editor, media);
    }

    private static string FindWorkspaceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the FreeP workspace root.");
    }
}
