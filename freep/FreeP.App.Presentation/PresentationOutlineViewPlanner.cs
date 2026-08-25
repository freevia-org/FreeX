using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Produces the text/navigation projection shown by PowerPoint-style Outline View.
/// The projection is deliberately read-only: editing continues through the existing
/// canvas text editor, while selecting an outline slide uses the shared slide-pane
/// selection workflow.
/// </summary>
public sealed record PresentationOutlineParagraphPlan(string Text, int Level);

public sealed record PresentationOutlineSlidePlan(
    int SlideIndex,
    string SlideLabel,
    string Title,
    IReadOnlyList<PresentationOutlineParagraphPlan> Body);

public static class PresentationOutlineViewPlanner
{
    public static IReadOnlyList<PresentationOutlineSlidePlan> Build(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        return presentation.Slides
            .Select((slide, index) => BuildSlide(slide, index))
            .ToArray();
    }

    private static PresentationOutlineSlidePlan BuildSlide(Slide slide, int slideIndex)
    {
        var visibleShapes = slide.Shapes.Where(shape => !shape.IsHidden).ToArray();
        var titleShape = visibleShapes.FirstOrDefault(IsTitleShape);
        var title = titleShape is null ? string.Empty : FirstText(titleShape);
        var body = visibleShapes
            .Where(shape => !ReferenceEquals(shape, titleShape))
            .SelectMany(BuildParagraphs)
            .ToArray();

        return new(
            slideIndex,
            $"Slide {slideIndex + 1}",
            string.IsNullOrWhiteSpace(title) ? $"Slide {slideIndex + 1}" : title,
            body);
    }

    private static bool IsTitleShape(SlideShape shape) =>
        shape.Placeholder?.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle;

    private static string FirstText(SlideShape shape) =>
        BuildParagraphs(shape).Select(paragraph => paragraph.Text).FirstOrDefault() ?? string.Empty;

    private static IEnumerable<PresentationOutlineParagraphPlan> BuildParagraphs(SlideShape shape)
    {
        if (shape.TextBody is null)
            yield break;

        foreach (var paragraph in shape.TextBody.Paragraphs)
        {
            var text = string.Concat(paragraph.Runs.Select(run => run.Text)).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                yield return new(text, Math.Clamp(paragraph.Level, 0, 8));
        }
    }
}
