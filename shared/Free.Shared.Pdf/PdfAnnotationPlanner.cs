namespace Free.Shared.Pdf;

internal sealed record PdfLinkAnnotationPlan(
    double Left,
    double Top,
    double Right,
    double Bottom,
    string? Uri,
    string? Tooltip,
    string? DestinationName);

internal static class PdfAnnotationPlanner
{
    public static IReadOnlyList<PdfLinkAnnotationPlan> BuildLinkAnnotations(PdfContentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return BuildLinkAnnotations(page.WidthPoints, page.HeightPoints, page.LinkOverlays);
    }

    public static IReadOnlyList<PdfLinkAnnotationPlan> BuildLinkAnnotations(
        double pageWidth,
        double pageHeight,
        IReadOnlyList<PdfLinkOverlay>? overlays)
    {
        if (overlays is not { Count: > 0 })
            return [];

        var links = new List<PdfLinkAnnotationPlan>(overlays.Count);
        foreach (var overlay in overlays)
        {
            if (!double.IsFinite(overlay.X)
                || !double.IsFinite(overlay.Y)
                || !double.IsFinite(overlay.Width)
                || !double.IsFinite(overlay.Height)
                || overlay.Width <= 0
                || overlay.Height <= 0)
            {
                continue;
            }

            var uri = overlay.Uri?.Trim();
            var destinationName = overlay.DestinationName?.Trim();
            if (string.IsNullOrEmpty(uri) && string.IsNullOrEmpty(destinationName))
                continue;

            var left = Math.Clamp(overlay.X, 0, pageWidth);
            var right = Math.Clamp(overlay.X + overlay.Width, 0, pageWidth);
            var top = Math.Clamp(overlay.Y, 0, pageHeight);
            var bottom = Math.Clamp(overlay.Y + overlay.Height, 0, pageHeight);
            if (right <= left || bottom <= top)
                continue;

            links.Add(new PdfLinkAnnotationPlan(
                left,
                top,
                right,
                bottom,
                uri,
                overlay.Tooltip,
                destinationName));
        }

        return links;
    }

    public static IReadOnlyList<PdfNamedDestination> BuildNamedDestinations(PdfContentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.NamedDestinations is not { Count: > 0 })
            return [];

        return page.NamedDestinations
            .Where(destination => !string.IsNullOrWhiteSpace(destination.Name)
                && double.IsFinite(destination.X)
                && double.IsFinite(destination.Y))
            .Select(destination => destination with
            {
                Name = destination.Name.Trim(),
                X = Math.Clamp(destination.X, 0, page.WidthPoints),
                Y = Math.Clamp(destination.Y, 0, page.HeightPoints),
            })
            .ToArray();
    }
}
