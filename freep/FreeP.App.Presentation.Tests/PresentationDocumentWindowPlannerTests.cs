using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationDocumentWindowPlannerTests
{
    [Fact]
    public void CreateNext_round_trips_an_independent_presentation_snapshot_and_file_state()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Title = "Source";
        var planner = new PresentationDocumentWindowPlanner();

        var plan = planner.CreateNext(presentation, @"C:\work\deck.pptx", isDirty: true);

        plan.CurrentPath.Should().Be(@"C:\work\deck.pptx");
        plan.IsDirty.Should().BeTrue();
        plan.WindowNumber.Should().Be(2);
        plan.WindowSuffix.Should().Be(" : 2");
        plan.Presentation.Should().NotBeSameAs(presentation);
        plan.Presentation.Slides[0].Title.Should().Be("Source");
        plan.Presentation.Slides[0].Title = "Window copy";
        presentation.Slides[0].Title.Should().Be("Source");
    }

    [Fact]
    public void CreateNext_numbers_windows_monotonically_and_normalizes_empty_paths()
    {
        var planner = new PresentationDocumentWindowPlanner();
        var presentation = Presentation.CreateEmpty();

        var first = planner.CreateNext(presentation, " ", isDirty: false);
        first.CurrentPath.Should().BeNull();
        first.WindowNumber.Should().Be(2);
        planner.CreateNext(presentation, null, isDirty: false).WindowNumber.Should().Be(3);
    }
}
