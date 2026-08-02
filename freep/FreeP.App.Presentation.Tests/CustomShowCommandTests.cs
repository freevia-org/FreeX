using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class CustomShowCommandTests
{
    [Fact]
    public void ApplyCustomShowMutation_CreateUndoAndRedoRestoresDefinition()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide { Title = "Second" });
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));
        var slideIds = presentation.Slides.Select(slide => slide.Id).ToArray();

        var result = editor.ApplyCustomShowMutation(p =>
            SlideShowCustomShowPlanner.CreateCustomShow(p, "Review", slideIds));

        result.Succeeded.Should().BeTrue();
        presentation.CustomShows.Should().ContainSingle();
        presentation.CustomShows[0].SlideIds.Should().Equal(slideIds);

        editor.Undo();
        presentation.CustomShows.Should().BeEmpty();

        editor.Redo();
        presentation.CustomShows.Should().ContainSingle();
        presentation.CustomShows[0].Name.Should().Be("Review");
        presentation.CustomShows[0].SlideIds.Should().Equal(slideIds);
    }

    [Fact]
    public void ReplaceCustomShowsCommand_RestoresRenameAndOrder()
    {
        var presentation = Presentation.CreateEmpty();
        var first = new PresentationCustomShow { Id = 1, Name = "Original" };
        first.SlideIds.Add("slide-1");
        presentation.CustomShows.Add(first);
        var after = new PresentationCustomShow { Id = 1, Name = "Renamed" };
        after.SlideIds.Add("slide-2");
        var bus = new PresentationCommandBus(presentation);

        bus.Execute(new ReplaceCustomShowsCommand(
            new[] { first },
            new[] { after }));

        presentation.CustomShows[0].Name.Should().Be("Renamed");
        presentation.CustomShows[0].SlideIds.Should().Equal("slide-2");
        bus.Undo();
        presentation.CustomShows[0].Name.Should().Be("Original");
        presentation.CustomShows[0].SlideIds.Should().Equal("slide-1");
    }

    [Fact]
    public void ApplyCustomShowMutation_InvalidEditDoesNotPolluteUndoHistory()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(
            presentation,
            new PresentationCommandBus(presentation));

        var result = editor.ApplyCustomShowMutation(p =>
            SlideShowCustomShowPlanner.RenameCustomShow(p, 0, "No show"));

        result.Succeeded.Should().BeFalse();
        editor.CanUndo.Should().BeFalse();
        presentation.CustomShows.Should().BeEmpty();
    }
}
