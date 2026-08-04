using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public sealed record SlidePrintCommentCalloutPlan(
    double AnchorX,
    double AnchorY,
    double CardX,
    double CardY,
    double CardWidth,
    double CardHeight,
    string Author,
    string Body);

public static class SlidePrintMarkupPlanner
{
    private const double EmuPerDip = DrawingMlCoordinateUnits.EmuPerPixel;

    public static IReadOnlyList<SlidePrintCommentCalloutPlan> BuildCommentCallouts(
        Presentation presentation,
        Slide slide)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(slide);

        var slideWidth = presentation.SlideSizeCxEmu / EmuPerDip;
        var slideHeight = presentation.SlideSizeCyEmu / EmuPerDip;
        var result = new List<SlidePrintCommentCalloutPlan>(slide.Comments.Count);

        foreach (var comment in slide.Comments)
        {
            var anchorX = Math.Clamp(comment.Xemu / EmuPerDip, 4, Math.Max(4, slideWidth - 4));
            var anchorY = Math.Clamp(comment.Yemu / EmuPerDip, 4, Math.Max(4, slideHeight - 4));
            var cardWidth = Math.Min(180, Math.Max(64, slideWidth - 8));
            var cardHeight = 28;
            var cardX = Math.Clamp(anchorX - 8, 4, Math.Max(4, slideWidth - cardWidth - 4));
            var cardY = Math.Clamp(anchorY + 7, 4, Math.Max(4, slideHeight - cardHeight - 4));
            var author = string.IsNullOrWhiteSpace(comment.Author)
                ? (string.IsNullOrWhiteSpace(comment.Initials) ? "Comment" : comment.Initials.Trim())
                : Trim(comment.Author, 24);

            result.Add(new SlidePrintCommentCalloutPlan(
                anchorX,
                anchorY,
                cardX,
                cardY,
                cardWidth,
                cardHeight,
                author,
                Trim(comment.Text, 82)));
        }

        return result;
    }

    private static string Trim(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..Math.Max(0, maxLength - 3)] + "...";
    }
}
