using System.Collections.Generic;
using System.Reflection;
using System.Windows.Controls;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Round 134: presenter-view notes must commit against the slide the notes box was
/// populated FOR, not whatever the live "current slide" is when the edit is flushed.
/// With auto-advance running, the presenter can be mid-edit on slide N when the show
/// moves on to slide N+1; RefreshFromState deliberately leaves a dirty/focused box
/// unpainted, so <c>_stateProvider().CurrentSlide</c> can already disagree with what
/// is on screen by the time CommitNotes runs.
/// </summary>
public sealed class PresenterViewNotesCommitTests
{
    private static readonly FieldInfo NotesTextField = typeof(PresenterViewWindow)
        .GetField("_notesText", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo CommitNotesMethod = typeof(PresenterViewWindow)
        .GetMethod("CommitNotes", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static SlideShowPresenterState MakeState(int currentSlideIndex, Slide[] slides, string notesText)
    {
        var current = slides[currentSlideIndex];
        var hasNext = currentSlideIndex + 1 < slides.Length;
        var next = hasNext ? slides[currentSlideIndex + 1] : null;
        return new SlideShowPresenterState(
            new SlideShowHostState(
                slides.Length,
                currentSlideIndex,
                HasSlides: true,
                IsFirstSlide: currentSlideIndex == 0,
                IsLastSlide: currentSlideIndex == slides.Length - 1,
                HasPendingSteps: false,
                StatusText: $"Slide {currentSlideIndex + 1} of {slides.Length}"),
            new SlideShowPresenterSlideState(currentSlideIndex, current.Id, current.Title ?? string.Empty, current),
            next is null
                ? null
                : new SlideShowPresenterSlideState(currentSlideIndex + 1, next.Id, next.Title ?? string.Empty, next),
            notesText,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            SlideShowPresenterDisplayIntent.FullScreen,
            SlideShowPresenterToolPlanner.BuildPlan());
    }

    /// <summary>
    /// The bug: type into the notes box for slide 0, let auto-advance move the live
    /// current slide to slide 1 before the edit is flushed, then commit. The edit must
    /// land on slide 0 (what the presenter was actually looking at), never on slide 1.
    /// </summary>
    [StaFact]
    public void CommitNotes_AfterCurrentSlideChangesMidEdit_WritesToSlideTheBoxWasPopulatedFor()
    {
        var slideA = new Slide { Id = "a", Title = "Slide A" };
        var slideB = new Slide { Id = "b", Title = "Slide B" };
        var slides = new[] { slideA, slideB };

        var currentIndex = 0;
        var notesBySlide = new Dictionary<int, string> { [0] = "Original notes for slide 0", [1] = "Original notes for slide 1" };

        var calls = new List<(int Index, string? Text)>();
        var window = new PresenterViewWindow(
            new Presentation(),
            () => MakeState(currentIndex, slides, notesBySlide[currentIndex]),
            setNotesText: (index, text) =>
            {
                calls.Add((index, text));
                notesBySlide[index] = text ?? string.Empty;
            });

        // Populate the notes box for slide 0, exactly as Loaded/the refresh timer would.
        window.RefreshFromState();
        var notesBox = (TextBox)NotesTextField.GetValue(window)!;
        notesBox.Text.Should().Be("Original notes for slide 0");

        // Presenter types into the box while it holds slide 0's notes; TextChanged marks
        // it dirty because _refreshing is false here.
        notesBox.Text = "Edited notes meant for slide 0";

        // Auto-advance fires: the live current slide moves to 1 while the box is still
        // dirty for slide 0 (RefreshFromState would skip repainting it in this state).
        currentIndex = 1;

        CommitNotesMethod.Invoke(window, null);

        calls.Should().ContainSingle();
        calls[0].Index.Should().Be(0, "the edit was made while the box held slide 0's notes, not the live current slide");
        calls[0].Text.Should().Be("Edited notes meant for slide 0");
        notesBySlide[1].Should().Be("Original notes for slide 1", "slide 1's original notes must survive untouched");
    }

    /// <summary>
    /// Sibling no-regression: the normal, no-auto-advance path (edit and commit against
    /// the same, unchanged current slide) must keep working after the fix.
    /// </summary>
    [StaFact]
    public void CommitNotes_WithoutSlideChange_StillWritesToCurrentSlide()
    {
        var slideA = new Slide { Id = "a", Title = "Slide A" };
        var slides = new[] { slideA };

        var calls = new List<(int Index, string? Text)>();
        var window = new PresenterViewWindow(
            new Presentation(),
            () => MakeState(0, slides, "Original notes"),
            setNotesText: (index, text) => calls.Add((index, text)));

        window.RefreshFromState();
        var notesBox = (TextBox)NotesTextField.GetValue(window)!;
        notesBox.Text = "Updated notes for slide 0";

        CommitNotesMethod.Invoke(window, null);

        calls.Should().ContainSingle();
        calls[0].Index.Should().Be(0);
        calls[0].Text.Should().Be("Updated notes for slide 0");
    }
}
