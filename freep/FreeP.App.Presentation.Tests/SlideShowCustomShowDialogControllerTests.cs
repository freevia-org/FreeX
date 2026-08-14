using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowCustomShowDialogControllerTests
{
    [Fact]
    public void VisualMetrics_OwnSharedLayoutAndNativeWindowChromeCompensation()
    {
        SlideShowCustomShowDialogVisualMetrics.WpfWindowWidth.Should().Be(640);
        SlideShowCustomShowDialogVisualMetrics.WpfWindowHeight.Should().Be(440);
        SlideShowCustomShowDialogVisualMetrics.AvaloniaWindowWidth.Should().Be(625.3333333333334);
        SlideShowCustomShowDialogVisualMetrics.AvaloniaWindowHeight.Should().Be(402.6666666666667);
        SlideShowCustomShowDialogVisualMetrics.MinimumWindowWidth.Should().Be(560);
        SlideShowCustomShowDialogVisualMetrics.MinimumWindowHeight.Should().Be(360);
        SlideShowCustomShowDialogVisualMetrics.RootInset.Should().Be(14);
        SlideShowCustomShowDialogVisualMetrics.ShowListColumnWidth.Should().Be(210);
        SlideShowCustomShowDialogVisualMetrics.ShowListRightGap.Should().Be(10);
        SlideShowCustomShowDialogVisualMetrics.NameMinimumWidth.Should().Be(260);
        SlideShowCustomShowDialogVisualMetrics.NameBottomMargin.Should().Be(8);
        SlideShowCustomShowDialogVisualMetrics.OrderedSlidesMinimumHeight.Should().Be(92);
        SlideShowCustomShowDialogVisualMetrics.OrderedSlidesRowHeight.Should().Be(118);
        SlideShowCustomShowDialogVisualMetrics.LabelBottomMargin.Should().Be(4);
        SlideShowCustomShowDialogVisualMetrics.OrderHeaderTopMargin.Should().Be(2);
        SlideShowCustomShowDialogVisualMetrics.AvailableSlidesTopMargin.Should().Be(8);
        SlideShowCustomShowDialogVisualMetrics.ValidationTopMargin.Should().Be(4);
        SlideShowCustomShowDialogVisualMetrics.ValidationBottomMargin.Should().Be(8);
        SlideShowCustomShowDialogVisualMetrics.ActionRowTopMargin.Should().Be(12);
        SlideShowCustomShowDialogVisualMetrics.ActionSpacing.Should().Be(6);
        SlideShowCustomShowDialogVisualMetrics.AvailableSlideVerticalMargin.Should().Be(2);
        SlideShowCustomShowDialogVisualMetrics.AvailableSlideControlHeight.Should().Be(20);
        SlideShowCustomShowDialogVisualMetrics.AddSlideButtonMinimumWidth.Should().Be(58);
        SlideShowCustomShowDialogVisualMetrics.ActionButtonMinimumWidth.Should().Be(82);
        SlideShowCustomShowDialogVisualMetrics.ActionButtonHorizontalPadding.Should().Be(8);
        SlideShowCustomShowDialogVisualMetrics.ActionButtonVerticalPadding.Should().Be(3);
    }

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
        var composition = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "SlideShowCustomShowDialogNativeComposition.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("SlideShowCustomShowDialogNativeComposition<")
                .And.Contain("_renderer.Controller.Initialize()")
                .And.Contain("_renderer.Controller.SelectShow()")
                .And.Contain("_renderer.Controller.SelectSlide()")
                .And.Contain("_renderer.Controller.Create")
                .And.Contain("_renderer.Buttons.Rename")
                .And.Contain("_renderer.Buttons.Update")
                .And.Contain("_renderer.Buttons.MoveUp")
                .And.Contain("_renderer.Buttons.MoveDown")
                .And.Contain("_renderer.Buttons.Remove")
                .And.Contain("_renderer.Buttons.Delete")
                .And.Contain("_renderer.Buttons.Start")
                .And.Contain("_renderer.Actions.Execute(")
                .And.Contain("_renderer.Controller.Reorder(")
                .And.Contain("CreateAvailableSlideRow(")
                .And.Contain("_renderer.AvailableSlides.Controls")
                .And.NotContain("ISlideShowCustomShowDialogView.CaptureState()")
                .And.NotContain("_formSession.ApplyFullPlan(plan)")
                .And.NotContain("_formSession.ApplySelectedShowPlan(plan)")
                .And.NotContain("_formSession.ApplySlideSelection(plan)")
                .And.Contain("SlideShowCustomShowDialogVisualMetrics.MinimumWindowWidth")
                .And.Contain("SlideShowCustomShowDialogVisualMetrics.RootInset")
                .And.Contain("SlideShowCustomShowDialogVisualMetrics.ShowListColumnWidth")
                .And.Contain("SlideShowCustomShowDialogVisualMetrics.OrderedSlidesRowHeight")
                .And.Contain("SlideShowCustomShowDialogVisualMetrics.AvailableSlideControlHeight")
                .And.Contain("SlideShowCustomShowDialogVisualMetrics.ActionButtonHorizontalPadding")
                .And.NotContain("private readonly SlideShowCustomShowDialogSession _session")
                .And.NotContain("private readonly SlideShowCustomShowDialogController _controller")
                .And.NotContain("SlideShowCustomShowDialogViewAdapter<")
                .And.NotContain("private void ApplyTransition(")
                .And.NotContain("SlideShowCustomShowDialogTransitionDispatcher.Dispatch(")
                .And.NotContain("_session.SelectShow(")
                .And.NotContain("_session.SelectSlide(")
                .And.NotContain("_session.Create(")
                .And.NotContain("_session.Rename(")
                .And.NotContain("_session.UpdateSlides(")
                .And.NotContain("_session.Delete(")
                .And.NotContain("_session.StartShow(")
                .And.NotContain("MinWidth = 560")
                .And.NotContain("MinHeight = 360")
                .And.NotContain("new Thickness(14)")
                .And.NotContain("new GridLength(210)")
                .And.NotContain("new GridLength(118)")
                .And.NotContain("MinWidth = 82")
                .And.NotContain("new Thickness(8, 3");
        }

        composition.Should().Contain("new SlideShowCustomShowDialogViewAdapter<TControl>(");
        composition.Should().Contain("Controller = new(");
        composition.Should().Contain("Actions = new(Controller, close)");
        composition.Should().Contain("Buttons = new(");
        composition.Should().Contain("AvailableSlides = new(");
        composition.Should().Contain("AvailableSlides.Render(slides)");

        wpf.Should().Contain("SlideShowCustomShowDialogVisualMetrics.WpfWindowWidth");
        avalonia.Should().Contain("SlideShowCustomShowDialogVisualMetrics.AvaloniaWindowWidth");

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
