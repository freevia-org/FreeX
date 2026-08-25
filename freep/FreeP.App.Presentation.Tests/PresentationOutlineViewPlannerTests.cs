using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationOutlineViewPlannerTests
{
    [Fact]
    public void Build_projects_visible_title_and_body_paragraph_levels_in_slide_order()
    {
        var presentation = new Presentation();
        var first = new Slide();
        first.Shapes.Add(TextShape("Quarterly review", PlaceholderType.Title));
        first.Shapes.Add(TextShape("Revenue", PlaceholderType.Body, level: 0));
        first.Shapes.Add(TextShape("Europe", PlaceholderType.Body, level: 1));
        first.Shapes.Add(TextShape("hidden", PlaceholderType.Body, hidden: true));
        presentation.Slides.Add(first);
        presentation.Slides.Add(new Slide());

        var plan = PresentationOutlineViewPlanner.Build(presentation);

        plan.Should().HaveCount(2);
        plan[0].Should().Match<PresentationOutlineSlidePlan>(slide =>
            slide.SlideIndex == 0 &&
            slide.SlideLabel == "Slide 1" &&
            slide.Title == "Quarterly review" &&
            slide.Body.SequenceEqual(new[]
            {
                new PresentationOutlineParagraphPlan("Revenue", 0),
                new PresentationOutlineParagraphPlan("Europe", 1),
            }));
        plan[1].Title.Should().Be("Slide 2");
        plan[1].Body.Should().BeEmpty();
    }

    private static SlideShape TextShape(
        string text,
        PlaceholderType type,
        int level = 0,
        bool hidden = false)
    {
        var body = new TextBody();
        var paragraph = new Paragraph { Level = level };
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return new SlideShape
        {
            IsHidden = hidden,
            Placeholder = new Placeholder { Type = type },
            TextBody = body,
        };
    }
}
