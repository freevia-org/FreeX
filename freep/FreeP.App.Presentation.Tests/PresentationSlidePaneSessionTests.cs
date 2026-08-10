using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationSlidePaneSessionTests
{
    [Fact]
    public void ProjectionCombinesEntriesPreviewMetadataSelectionAndStatus()
    {
        var editor = CreateEditor("Title", "Agenda");
        var session = new PresentationSlidePaneSession(() => editor);

        var projection = session.Projection;

        projection.Items.Should().HaveCount(2);
        projection.Items[0].Preview.Should().Be(new SlidePanePreviewMetadata(
            editor.Presentation.Slides[0].Id,
            0,
            "Title",
            1,
            IsHidden: false,
            IsSelected: true,
            IsActive: true));
        projection.Selection.SelectedSlideIndices.Should().Equal(0);
        projection.BottomAffordance.Action.IsEnabled.Should().BeTrue();
        projection.Status.Should().Be(new SlidePaneStatusPlan(
            0,
            2,
            1,
            "Slide 1 of 2",
            "1 slide selected"));
    }

    [Fact]
    public void RangeAndToggleSelectionRemainAttachedToSlideIdentityAcrossInsert()
    {
        var editor = CreateEditor("A", "B", "C", "D", "E");
        var session = new PresentationSlidePaneSession(() => editor);

        session.ApplySelectionGesture(1, SlidePaneSelectionGesture.Replace);
        session.ApplySelectionGesture(3, SlidePaneSelectionGesture.Range);
        session.ApplySelectionGesture(2, SlidePaneSelectionGesture.Toggle);

        session.Selection.ActiveSlideIndex.Should().Be(3);
        session.Selection.AnchorSlideIndex.Should().Be(2);
        session.Selection.SelectedSlideIndices.Should().Equal(1, 3);

        editor.Bus.Execute(new DuplicateSlideCommand(0));
        session.RefreshFromEditorChange();

        session.Selection.SelectedSlideIndices.Should().Equal(2, 4);
        session.Projection.Items
            .Where(item => item.Preview?.IsSelected == true)
            .Select(item => item.Preview!.Title)
            .Should().Equal("B", "D");
    }

    [Fact]
    public void WorkareaBatchDuplicateSelectsDuplicatesAndUndoesAsOneCommand()
    {
        var endpoint = new RecordingEndpoint();
        using var workarea = new PresentationWorkareaSession(
            endpoint,
            CreatePresentation("A", "B", "C", "D"));

        workarea.ApplySlidePaneNativeSelection([1, 3], activeSlideIndex: 3);
        workarea.ExecuteSlidePaneAction(SlidePaneActionKind.DuplicateSlide, contextSlideIndex: 3)
            .Should().BeTrue();

        workarea.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("A", "B", "B", "C", "D", "D");
        workarea.SlidePaneSession.Selection.SelectedSlideIndices.Should().Equal(2, 5);
        workarea.SlidePaneSession.Selection.ActiveSlideIndex.Should().Be(5);

        workarea.Editor.Undo();

        workarea.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("A", "B", "C", "D");
    }

    [Fact]
    public void BatchDeleteAndMoveUseSelectionAsOneUndoableUnit()
    {
        var editor = CreateEditor("A", "B", "C", "D", "E");
        var session = new PresentationSlidePaneSession(() => editor);
        session.ApplyNativeSelection([1, 2], activeSlideIndex: 2);

        var move = session.BuildAction(SlidePaneActionKind.MoveSlide, 2, targetInsertionIndex: 5);
        session.TryExecuteAction(move).Should().BeTrue();
        session.RefreshFromEditorChange();

        editor.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("A", "D", "E", "B", "C");
        session.Selection.SelectedSlideIndices.Should().Equal(3, 4);
        editor.Undo();
        editor.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("A", "B", "C", "D", "E");

        session.RefreshFromEditorChange();
        session.ApplyNativeSelection([1, 3], activeSlideIndex: 3);
        var delete = session.BuildAction(SlidePaneActionKind.DeleteSlide, 3);
        session.TryExecuteAction(delete).Should().BeTrue();
        session.RefreshFromEditorChange();

        editor.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("A", "C", "E");
        editor.Undo();
        editor.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("A", "B", "C", "D", "E");
    }

    [Fact]
    public void DragMapsVisibleCollapsedSectionPositionsBackToModelIndices()
    {
        var presentation = CreatePresentation("A", "B", "C", "D");
        var section = new PresentationSection { Name = "Intro" };
        section.SlideIds.Add(presentation.Slides[0].Id);
        section.SlideIds.Add(presentation.Slides[1].Id);
        presentation.Sections.Add(section);
        var editor = CreateEditor(presentation);
        var session = new PresentationSlidePaneSession(() => editor);
        session.ToggleSection(SlidePanePlanner.GetSectionIdentity(section, 0));

        session.BeginDrag(sourceSlideIndex: 3, startPointerY: 0);
        var update = session.UpdateDrag(
            pointerYWithinItem: SlidePanePlanner.DefaultDragStartThreshold + 1,
            pointerYWithinPane: 0);

        update.DropVisualPlan.SourceSlideIndex.Should().Be(3);
        update.DropVisualPlan.TargetSlideIndex.Should().Be(2);
        update.DropVisualPlan.IsMoveEnabled.Should().BeTrue();
    }

    [Fact]
    public void ContextRouteKeepsSectionGroupingAndSlideExecutionPortable()
    {
        var editor = CreateEditor("A", "B");
        var session = new PresentationSlidePaneSession(() => editor);

        var sectionRoute = session.BuildContextCommandRoute(
            FreePContextMenuCommand.AddSection,
            slideIndex: 1,
            sectionIndex: -1);
        var duplicateRoute = session.BuildContextCommandRoute(
            FreePContextMenuCommand.DuplicateSlide,
            slideIndex: 1,
            sectionIndex: -1);

        sectionRoute.SectionExecution.Should().NotBeNull();
        sectionRoute.SectionExecution!.RequiresNamePrompt.Should().BeTrue();
        duplicateRoute.SlideAction!.Kind.Should().Be(SlidePaneActionKind.DuplicateSlide);
    }

    [Fact]
    public void NativeSelectionOnCurrentSlidePublishesSelectionAndChromeOperations()
    {
        var endpoint = new RecordingEndpoint();
        using var workarea = new PresentationWorkareaSession(
            endpoint,
            CreatePresentation("A", "B", "C"));
        endpoint.Operations.Clear();

        var change = workarea.ApplySlidePaneNativeSelection([0, 2], activeSlideIndex: 0);

        change.Projection.Status.SelectedSlideCount.Should().Be(2);
        endpoint.Operations.Should().Equal(
            PresentationWorkareaOperation.SyncSlidePaneSelection,
            PresentationWorkareaOperation.RefreshSlidePaneChrome);
    }

    private static Presentation CreatePresentation(params string[] titles)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Title = titles[0];
        foreach (var title in titles.Skip(1))
            presentation.Slides.Add(new Slide { Title = title });
        return presentation;
    }

    private static EditingSession CreateEditor(params string[] titles) =>
        CreateEditor(CreatePresentation(titles));

    private static EditingSession CreateEditor(Presentation presentation) =>
        new(presentation, new PresentationCommandBus(presentation));

    private sealed class RecordingEndpoint : IPresentationWorkareaEndpoint
    {
        public List<PresentationWorkareaOperation> Operations { get; } = [];

        public void Apply(
            PresentationWorkareaOperation operation,
            PresentationWorkareaContext context) =>
            Operations.Add(operation);

        public void ExecuteNativeCommand(PresentationWorkareaNativeCommand command)
        {
        }
    }
}
