using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowCustomShowDialogControllerTests
{
    [Fact]
    public void Controller_OwnsInitializationSelectionAndValidationDispatch()
    {
        var presentation = MakePresentation();
        AddShow(presentation, "First", "intro");
        AddShow(presentation, "Second", "deep");
        var view = new FakeView
        {
            State = new("First", Array.Empty<string>(), 1, 0),
        };
        var controller = CreateController(presentation, view, _ => true);

        controller.Initialize();
        controller.SelectShow();
        controller.SelectSlide();
        controller.Rename();

        view.RenderCalls.Should().Equal("full", "selected", "slide");
        view.LastPlan!.SelectedShow!.Name.Should().Be("Second");
        view.LastPlan.SelectedSlideIndex.Should().Be(0);
        view.ValidationMessage.Should().Be(
            SlideShowCustomShowPlanner.DuplicateCustomShowNameMessage);
        view.CaptureCount.Should().Be(3);
        view.CloseCount.Should().Be(0);
    }

    [Fact]
    public void Controller_RoutesAuthoringReorderDeleteAndStartActions()
    {
        var presentation = MakePresentation();
        AddShow(presentation, "First", "intro");
        var startedNames = new List<string?>();
        var view = new FakeView
        {
            State = new("Review", new[] { "appendix", "intro" }, 0, 0),
        };
        var controller = CreateController(
            presentation,
            view,
            name =>
            {
                startedNames.Add(name);
                return true;
            });

        controller.Initialize();
        controller.Create();
        view.State = view.State with { Name = "Final" };
        controller.Rename();
        view.State = view.State with { SelectedSlideIds = new[] { "intro", "deep" } };
        controller.UpdateSlides();
        controller.AddSlideOccurrence("appendix");
        view.State = view.State with { SelectedSlideIndex = 0 };
        controller.SelectSlide();
        controller.MoveSelectedSlide(1);
        controller.RemoveSelectedSlide();
        var reorder = controller.Reorder(sourceSlideIndex: 1, targetDropIndex: 0);
        controller.Delete();
        controller.StartShow();

        reorder.ShouldApplyMutation.Should().BeTrue();
        presentation.CustomShows.Should().ContainSingle();
        presentation.CustomShows[0].Name.Should().Be("First");
        startedNames.Should().Equal("First");
        view.ValidationMessage.Should().BeNull();
        view.CloseCount.Should().Be(1);
        view.RenderCalls.Count(call => call == "full").Should().Be(9);
    }

    [Fact]
    public void Renderers_DelegateActionsAndTransitionsButRetainNativeRenderingAndPointerOwnership()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = Read(root, "FreeP.App.Host");
        var avalonia = Read(root, "FreeP.App.Avalonia");

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("ISlideShowCustomShowDialogView")
                .And.Contain("SlideShowCustomShowDialogController _controller")
                .And.Contain("_controller.Initialize()")
                .And.Contain("_controller.SelectShow()")
                .And.Contain("_controller.SelectSlide()")
                .And.Contain("_controller.Create")
                .And.Contain("_controller.Rename")
                .And.Contain("_controller.UpdateSlides")
                .And.Contain("_controller.AddSlideOccurrence(")
                .And.Contain("_controller.RemoveSelectedSlide")
                .And.Contain("_controller.MoveSelectedSlide(")
                .And.Contain("_controller.Delete")
                .And.Contain("_controller.StartShow")
                .And.Contain("_controller.Reorder(")
                .And.Contain("RebuildSlides(")
                .And.Contain("_formSession.ApplyFullPlan(plan)")
                .And.Contain("_formSession.ApplySelectedShowPlan(plan)")
                .And.Contain("_formSession.ApplySlideSelection(plan)")
                .And.NotContain("private readonly SlideShowCustomShowDialogSession _session")
                .And.NotContain("private void ApplyTransition(")
                .And.NotContain("SlideShowCustomShowDialogTransitionDispatcher.Dispatch(")
                .And.NotContain("_session.SelectShow(")
                .And.NotContain("_session.SelectSlide(")
                .And.NotContain("_session.Create(")
                .And.NotContain("_session.Rename(")
                .And.NotContain("_session.UpdateSlides(")
                .And.NotContain("_session.Delete(")
                .And.NotContain("_session.StartShow(");
        }

        wpf.Should().Contain("PreviewMouseLeftButtonDown")
            .And.Contain("DragDrop.DoDragDrop(")
            .And.Contain("FindVisualAncestor<ListBoxItem>");
        avalonia.Should().Contain("PointerPressed")
            .And.Contain("PointerCaptureLost")
            .And.Contain("FindControlAncestor<ListBoxItem>");
    }

    private static SlideShowCustomShowDialogController CreateController(
        Presentation presentation,
        FakeView view,
        Func<string?, bool> tryStartShow)
    {
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = new SlideShowCustomShowSession(() => editor).CreateDialogSession(tryStartShow);
        return new SlideShowCustomShowDialogController(session, view);
    }

    private static Presentation MakePresentation()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "intro", Title = "Intro" });
        presentation.Slides.Add(new Slide { Id = "deep", Title = "Deep dive" });
        presentation.Slides.Add(new Slide { Id = "appendix", Title = "Appendix" });
        return presentation;
    }

    private static void AddShow(Presentation presentation, string name, params string[] slideIds)
    {
        var show = new PresentationCustomShow { Name = name };
        show.SlideIds.AddRange(slideIds);
        presentation.CustomShows.Add(show);
    }

    private static string Read(string root, string project) =>
        File.ReadAllText(Path.Combine(root, "freep", project, "CustomShowDialog.cs"));

    private sealed class FakeView : ISlideShowCustomShowDialogView
    {
        public SlideShowCustomShowDialogViewState State { get; set; } =
            new(null, Array.Empty<string>(), -1, -1);

        public int CaptureCount { get; private set; }
        public int CloseCount { get; private set; }
        public string? ValidationMessage { get; private set; }
        public SlideShowCustomShowSessionPlan? LastPlan { get; private set; }
        public List<string> RenderCalls { get; } = [];

        public SlideShowCustomShowDialogViewState CaptureState()
        {
            CaptureCount++;
            return State;
        }

        public void RenderFullPlan(SlideShowCustomShowSessionPlan plan) =>
            Render("full", plan);

        public void RenderSelectedShowPlan(SlideShowCustomShowSessionPlan plan) =>
            Render("selected", plan);

        public void ApplySlideSelection(SlideShowCustomShowSessionPlan plan) =>
            Render("slide", plan);

        public void SetValidation(string? message) => ValidationMessage = message;

        public void CloseDialog() => CloseCount++;

        private void Render(string call, SlideShowCustomShowSessionPlan plan)
        {
            RenderCalls.Add(call);
            LastPlan = plan;
        }
    }
}
