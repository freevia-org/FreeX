using FreeP.Core.Model;

namespace FreeP.Core.IO;

/// <summary>
/// r461: content the .pptx writer cannot represent, found before a save so the user can be told.
/// </summary>
/// <remarks>
/// The mirror of r454's load-side warnings, and of the lossy-save gates both sibling apps already
/// have (FreeX's <c>LossyFormatFeatureLossPlanner</c>, FreeW's <c>DocumentSaveCompatibilityPlanner</c>).
/// FreeP had neither, because its only save formats are native -- which is true of the FORMAT and
/// false of the CONTENT: the editor can hold things the writer does not serialise, and dropping them
/// without a word is the same silent loss this review has been fixing on the read side.
/// </remarks>
public static class PptxSaveWarnings
{
    /// <summary>
    /// Describes what would be lost by writing <paramref name="presentation"/> to a .pptx. Empty for
    /// content the writer fully supports, which is the ordinary case -- a warning that fires on
    /// healthy documents trains the user to dismiss the one that matters.
    /// </summary>
    public static IReadOnlyList<string> Describe(Presentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var warnings = new List<string>();

        // Inline pictures inside a text run. ExternalRichTextClipboardPlanner and
        // ExternalXamlClipboardPlanner create these when rich text carrying a picture is pasted from
        // another application, so an ordinary paste-then-save reaches this. The writer has no
        // representation for them: the picture is not written to the package at all and the run is
        // left holding the bare U+FFFC object-replacement character, so the user's picture becomes a
        // stray glyph and the image is gone from the file for good.
        var inlinePictures = CountInlinePictures(presentation);
        if (inlinePictures > 0)
        {
            warnings.Add(
                inlinePictures == 1
                    ? "One inline picture inside a text box cannot be saved to PowerPoint format and " +
                      "will be lost. Insert it as a picture instead to keep it."
                    : $"{inlinePictures} inline pictures inside text boxes cannot be saved to " +
                      "PowerPoint format and will be lost. Insert them as pictures instead to keep them.");
        }

        return warnings;
    }

    private static int CountInlinePictures(Presentation presentation) =>
        presentation.Slides
            .SelectMany(slide => EnumerateShapes(slide.Shapes))
            .Select(shape => shape.TextBody)
            .Where(body => body is not null)
            .SelectMany(body => body!.Paragraphs)
            .SelectMany(paragraph => paragraph.Runs)
            .Count(run => run.InlineImage is not null);

    /// <summary>Shapes including group children, since a pasted run can sit inside a group.</summary>
    private static IEnumerable<SlideShape> EnumerateShapes(IEnumerable<SlideShape> shapes)
    {
        foreach (var shape in shapes)
        {
            yield return shape;

            foreach (var child in EnumerateShapes(shape.Children))
                yield return child;
        }
    }
}
