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
    string Body)
{
    public required SlidePrintCommentVisualPlan Visual { get; init; }
}

public readonly record struct SlidePrintCommentTextPlan(
    string Text,
    LayoutRect Bounds,
    bool IsBold,
    double FontSize);

public readonly record struct SlidePrintCommentVisualPlan(
    LayoutPoint AnchorCenter,
    double MarkerRadius,
    LayoutRect CardBounds,
    SrgbColor FillColor,
    SrgbColor BorderColor,
    double BorderThickness,
    SrgbColor MarkerColor,
    SlidePrintCommentTextPlan Author,
    SlidePrintCommentTextPlan Body);

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

            var body = Trim(comment.Text, 82);
            result.Add(new SlidePrintCommentCalloutPlan(
                anchorX,
                anchorY,
                cardX,
                cardY,
                cardWidth,
                cardHeight,
                author,
                body)
            {
                Visual = new SlidePrintCommentVisualPlan(
                    new LayoutPoint(anchorX, anchorY),
                    MarkerRadius: 3,
                    new LayoutRect(cardX, cardY, cardWidth, cardHeight),
                    new SrgbColor(255, 249, 196),
                    new SrgbColor(192, 160, 0),
                    BorderThickness: 1,
                    new SrgbColor(220, 40, 40),
                    new SlidePrintCommentTextPlan(
                        author,
                        new LayoutRect(cardX + 6, cardY + 3, cardWidth - 12, 9),
                        IsBold: true,
                        FontSize: 8),
                    new SlidePrintCommentTextPlan(
                        body,
                        new LayoutRect(cardX + 6, cardY + 13, cardWidth - 12, 11),
                        IsBold: false,
                        FontSize: 7)),
            });
        }

        return result;
    }

    private static string Trim(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..Math.Max(0, maxLength - 3)] + "...";
    }
}
