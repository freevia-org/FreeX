using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class HeaderFooterCommandPlannerTests
{
    [Fact]
    public void BuildState_ReadsFlagsAndFooterTextFromCurrentSlide()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.HfVisibility = new HfFlags
        {
            ShowDate = false,
            ShowFooter = true,
            ShowSlideNum = false,
        };
        slide.Shapes.Add(FooterShape("Quarterly review"));

        var state = HeaderFooterCommandPlanner.BuildState(presentation, 0);

        state.ShowDateTime.Should().BeFalse();
        state.ShowFooter.Should().BeTrue();
        state.ShowSlideNumber.Should().BeFalse();
        state.FooterText.Should().Be("Quarterly review");
        state.HasFooterPlaceholder.Should().BeTrue();
    }

    [Fact]
    public void TryApply_CurrentSlide_SetsFlagsAndUpdatesExistingFooterField()
    {
        var editor = MakeEditor();
        var slide = editor.Presentation.Slides[0];
        slide.Shapes.Add(FooterShape("Old"));
        slide.Shapes.Add(DateShape());
        slide.Shapes.Add(SlideNumberShape());

        HeaderFooterCommandPlanner.TryApply(
            editor,
            new HeaderFooterApplyOptions(
                ShowDateTime: false,
                ShowFooter: true,
                ShowSlideNumber: true,
                FooterText: "New footer",
                HeaderFooterApplyScope.CurrentSlide),
            out var plan).Should().BeTrue();

        plan.TargetSlideIndexes.Should().Equal(0);
        var updated = editor.Presentation.Slides[0];
        updated.HfVisibility!.ShowDate.Should().BeFalse();
        updated.HfVisibility.ShowFooter.Should().BeTrue();
        updated.HfVisibility.ShowSlideNum.Should().BeTrue();
        FooterText(updated).Should().Be("New footer");
    }

    [Fact]
    public void TryApply_CurrentSlide_CreatesMissingDateFooterAndSlideNumberPlaceholders()
    {
        var editor = MakeEditor();

        HeaderFooterCommandPlanner.TryApply(
            editor,
            new HeaderFooterApplyOptions(
                ShowDateTime: true,
                ShowFooter: true,
                ShowSlideNumber: true,
                FooterText: "Created footer",
                HeaderFooterApplyScope.CurrentSlide),
            out var plan).Should().BeTrue();

        plan.Limitation.Should().BeNull();
        var slide = editor.Presentation.Slides[0];
        slide.HfVisibility!.ShowDate.Should().BeTrue();
        slide.HfVisibility.ShowFooter.Should().BeTrue();
        slide.HfVisibility.ShowSlideNum.Should().BeTrue();
        HeaderFooterPlaceholders(slide).Should().HaveCount(3);

        var date = SinglePlaceholder(slide, PlaceholderType.DateTime);
        var footer = SinglePlaceholder(slide, PlaceholderType.Footer);
        var slideNumber = SinglePlaceholder(slide, PlaceholderType.SlideNumber);
        FieldType(date).Should().Be("datetime1");
        FieldType(footer).Should().Be("footer");
        FieldText(footer).Should().Be("Created footer");
        FieldType(slideNumber).Should().Be("slidenum");
        slide.Shapes.Select(shape => shape.Id).Should().OnlyHaveUniqueItems();
        HeaderFooterPlaceholders(slide).Should()
            .OnlyContain(shape => shape.ExtentCxEmu > 0 && shape.ExtentCyEmu > 0);
    }

    [Fact]
    public void TryApply_AllSlides_UpdatesEachSlideAndIsUndoable()
    {
        var editor = MakeEditor();
        editor.Presentation.Slides[0].Shapes.Add(FooterShape("One"));
        editor.Presentation.Slides.Add(new Slide());
        editor.Presentation.Slides[1].Shapes.Add(FooterShape("Two"));

        HeaderFooterCommandPlanner.TryApply(
            editor,
            new HeaderFooterApplyOptions(true, true, true, "All", HeaderFooterApplyScope.AllSlides),
            out _).Should().BeTrue();

        editor.Presentation.Slides.Select(FooterText).Should().Equal("All", "All");
        editor.Undo();
        editor.Presentation.Slides.Select(FooterText).Should().Equal("One", "Two");
    }

    [Fact]
    public void TryApply_AllSlides_CreatesMissingPlaceholdersOnEachTargetAndIsUndoable()
    {
        var editor = MakeEditor();
        editor.Presentation.Slides.Add(new Slide());

        HeaderFooterCommandPlanner.TryApply(
            editor,
            new HeaderFooterApplyOptions(true, true, true, "All", HeaderFooterApplyScope.AllSlides),
            out _).Should().BeTrue();

        editor.Presentation.Slides.Should().AllSatisfy(slide =>
        {
            SinglePlaceholder(slide, PlaceholderType.DateTime);
            FieldText(SinglePlaceholder(slide, PlaceholderType.Footer)).Should().Be("All");
            SinglePlaceholder(slide, PlaceholderType.SlideNumber);
        });

        editor.Undo();
        editor.Presentation.Slides.Should()
            .AllSatisfy(slide => HeaderFooterPlaceholders(slide).Should().BeEmpty());
    }

    [Fact]
    public void TryApply_DoesNotDuplicateExistingFieldOnlyHeaderFooterShapes()
    {
        var editor = MakeEditor();
        var fieldOnlyFooter = FooterShape("Old");
        fieldOnlyFooter.Placeholder = null;
        editor.Presentation.Slides[0].Shapes.Add(fieldOnlyFooter);

        HeaderFooterCommandPlanner.TryApply(
            editor,
            new HeaderFooterApplyOptions(
                ShowDateTime: false,
                ShowFooter: true,
                ShowSlideNumber: false,
                FooterText: "Updated",
                HeaderFooterApplyScope.CurrentSlide),
            out _).Should().BeTrue();

        var slide = editor.Presentation.Slides[0];
        slide.Shapes.Where(shape => HasField(shape, "footer")).Should().ContainSingle();
        slide.Shapes.Single(shape => HasField(shape, "footer")).Id.Should().Be(fieldOnlyFooter.Id);
        FooterText(slide).Should().Be("Updated");
    }

    [Fact]
    public void TryApply_UncheckedOptionsDoNotCreateMissingPlaceholders()
    {
        var editor = MakeEditor();

        HeaderFooterCommandPlanner.TryApply(
            editor,
            new HeaderFooterApplyOptions(
                ShowDateTime: false,
                ShowFooter: false,
                ShowSlideNumber: false,
                FooterText: "Ignored",
                HeaderFooterApplyScope.CurrentSlide),
            out _).Should().BeTrue();

        var slide = editor.Presentation.Slides[0];
        HeaderFooterPlaceholders(slide).Should().BeEmpty();
        slide.HfVisibility!.ShowDate.Should().BeFalse();
        slide.HfVisibility.ShowFooter.Should().BeFalse();
        slide.HfVisibility.ShowSlideNum.Should().BeFalse();
    }

    [Fact]
    public void Compose_SkipsHeaderFooterPlaceholderWhenFlagIsDisabled()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.HfVisibility = new HfFlags
        {
            ShowDate = true,
            ShowFooter = false,
            ShowSlideNum = true,
        };
        slide.Shapes.Add(FooterShape("Hidden"));

        var ops = SlideCompositor.Compose(presentation, slide, 0);

        ops.OfType<DrawOp.Shape>().Select(op => op.ShapeId).Should().NotContain(100u);
    }

    private static EditingSession MakeEditor()
    {
        var presentation = Presentation.CreateEmpty();
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private static SlideShape FooterShape(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Text = text,
            Field = new FieldRun { FieldType = "footer", CachedText = text },
        });
        body.Paragraphs.Add(paragraph);

        return new SlideShape
        {
            Id = 100,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = PlaceholderType.Footer },
            TextBody = body,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
        };
    }

    private static SlideShape DateShape() => HeaderFooterShape(101, PlaceholderType.DateTime, "datetime1");

    private static SlideShape SlideNumberShape() => HeaderFooterShape(102, PlaceholderType.SlideNumber, "slidenum");

    private static SlideShape HeaderFooterShape(uint id, PlaceholderType type, string fieldType)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run
        {
            Field = new FieldRun { FieldType = fieldType },
        });
        body.Paragraphs.Add(paragraph);

        return new SlideShape
        {
            Id = id,
            Kind = SlideShapeKind.AutoShape,
            Placeholder = new Placeholder { Type = type },
            TextBody = body,
            ExtentCxEmu = 914400,
            ExtentCyEmu = 457200,
        };
    }

    private static SlideShape SinglePlaceholder(Slide slide, PlaceholderType type) =>
        slide.Shapes.Single(shape => shape.Placeholder?.Type == type);

    private static IEnumerable<SlideShape> HeaderFooterPlaceholders(Slide slide) =>
        slide.Shapes.Where(shape =>
            shape.Placeholder?.Type is PlaceholderType.DateTime or
                PlaceholderType.Footer or
                PlaceholderType.SlideNumber);

    private static bool HasField(SlideShape shape, string fieldType) =>
        shape.TextBody?.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Any(run => run.Field?.FieldType == fieldType) == true;

    private static string? FieldType(SlideShape shape) =>
        shape.TextBody?.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Field?.FieldType)
            .SingleOrDefault(fieldType => fieldType is not null);

    private static string? FieldText(SlideShape shape) =>
        shape.TextBody?.Paragraphs
            .SelectMany(paragraph => paragraph.Runs)
            .Select(run => run.Field?.CachedText ?? run.Text)
            .SingleOrDefault();

    private static string FooterText(Slide slide) =>
        slide.Shapes
            .SelectMany(shape => shape.TextBody?.Paragraphs ?? [])
            .SelectMany(paragraph => paragraph.Runs)
            .Where(run => run.Field?.FieldType == "footer")
            .Select(run => run.Field!.CachedText)
            .Single();
}
