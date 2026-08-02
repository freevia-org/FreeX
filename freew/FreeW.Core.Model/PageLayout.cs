namespace FreeW.Core.Model;

/// <summary>
/// Pure (UI-free) geometry helper that turns a <see cref="PageSettings"/> (expressed in points)
/// into device-independent pixels (DIP, 96 per inch) and computes the printable content area and
/// the number of pages required for a given amount of flowed content.
///
/// WPF measures everything in DIP, while the model stores page geometry in typographic points
/// (72 per inch). The conversion factor is therefore 96/72. Keeping the math here — rather than in
/// the WPF print/preview code — means it can be unit-tested without a WPF dependency, and both the
/// print path and the print-preview window share one source of truth.
/// </summary>
public static class PageLayout
{
    /// <summary>DIP per typographic point (96 DPI / 72 points-per-inch).</summary>
    public const double DipPerPoint = 96.0 / 72.0;

    /// <summary>Converts a measurement in points to device-independent pixels.</summary>
    public static double PointsToDip(double points) => points * DipPerPoint;

    /// <summary>The page size in DIP, honouring the page's width/height (already swapped for landscape).</summary>
    public static (double Width, double Height) PageSizeDip(PageSettings page) =>
        (PointsToDip(page.WidthPt), PointsToDip(page.HeightPt));

    /// <summary>
    /// The effective page margins in DIP (left, top, right, bottom), including the binding gutter.
    /// <paramref name="pageIndex"/> is zero-based and selects the inside edge for mirrored pages.
    /// Word ignores <c>w:gutterAtTop</c> when mirrored margins determine the binding edge.
    /// </summary>
    public static (double Left, double Top, double Right, double Bottom) MarginsDip(
        PageSettings page,
        int pageIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(page);

        var left = PointsToDip(page.MarginLeftPt);
        var top = PointsToDip(page.MarginTopPt);
        var right = PointsToDip(page.MarginRightPt);
        var bottom = PointsToDip(page.MarginBottomPt);
        var gutter = Math.Max(0, PointsToDip(page.GutterPt));

        if (page.GutterAtTop && !page.MirrorMargins)
            top += gutter;
        else if (page.MirrorMargins && Math.Max(0, pageIndex) % 2 == 1)
            right += gutter;
        else
            left += gutter;

        return (left, top, right, bottom);
    }

    /// <summary>
    /// The printable content area in DIP: the page size minus its margins. Never negative — a page
    /// whose margins exceed its dimensions clamps to zero rather than producing a negative box.
    /// </summary>
    public static (double Width, double Height) ContentAreaDip(PageSettings page, int pageIndex = 0)
    {
        var (w, h) = PageSizeDip(page);
        var (l, t, r, b) = MarginsDip(page, pageIndex);
        return (Math.Max(0, w - l - r), Math.Max(0, h - t - b));
    }

    /// <summary>
    /// The number of pages needed to fit <paramref name="contentHeightDip"/> of flowed content into
    /// this page's printable area. Always at least one page. Returns 1 when the content area has no
    /// height (degenerate geometry) so callers never divide by zero.
    /// </summary>
    public static int PageCount(PageSettings page, double contentHeightDip)
    {
        var (_, contentHeight) = ContentAreaDip(page);
        if (contentHeight <= 0)
            return 1;
        if (contentHeightDip <= contentHeight)
            return 1;
        return (int)Math.Ceiling(contentHeightDip / contentHeight);
    }
}
