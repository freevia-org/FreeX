using System.Windows;
using System.Windows.Documents;

using FreeX.Core.Model;

namespace FreeX.App.Host;

internal sealed record PdfLinkOverlay(
    string Target,
    HyperlinkTargetKind TargetKind,
    double X,
    double Y,
    double Width,
    double Height,
    CellAddress? SourceAddress = null,
    CellAddress? TargetAddress = null);

internal sealed record PdfCellDestinationOverlay(
    CellAddress Address,
    double X,
    double Y,
    double Width,
    double Height);

internal static class PdfLinkOverlayExtractor
{
    public static IReadOnlyList<PdfLinkOverlay> Extract(FixedPage page)
    {
        var overlays = new List<PdfLinkOverlay>();
        PdfOverlayVisualTreeWalker.Visit(page, (element, x, y) => Extract(element, x, y, overlays));

        return overlays;
    }

    private static void Extract(UIElement element, double x, double y, List<PdfLinkOverlay> overlays)
    {
        if (element is VisualHost { LinkOverlays.Count: > 0 } visualHost)
        {
            foreach (var overlay in visualHost.LinkOverlays)
            {
                overlays.Add(overlay with
                {
                    X = x + overlay.X,
                    Y = y + overlay.Y
                });
            }
        }
    }
}

internal static class PdfCellDestinationOverlayExtractor
{
    public static IReadOnlyList<PdfCellDestinationOverlay> Extract(FixedPage page)
    {
        var overlays = new List<PdfCellDestinationOverlay>();
        PdfOverlayVisualTreeWalker.Visit(page, (element, x, y) => Extract(element, x, y, overlays));

        return overlays;
    }

    private static void Extract(UIElement element, double x, double y, List<PdfCellDestinationOverlay> overlays)
    {
        if (element is VisualHost { CellDestinationOverlays.Count: > 0 } visualHost)
        {
            foreach (var overlay in visualHost.CellDestinationOverlays)
            {
                overlays.Add(overlay with
                {
                    X = x + overlay.X,
                    Y = y + overlay.Y
                });
            }
        }
    }
}
