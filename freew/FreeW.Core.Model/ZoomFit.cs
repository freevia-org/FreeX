namespace FreeW.Core.Model;

/// <summary>
/// Pure (WPF-free) geometry for Word's "Zoom" dialog fit options. Given the page geometry (already in
/// device-independent pixels at 100% zoom) and the viewport the page floats in, computes the zoom factor
/// (1.0 == 100%) that makes the page fill the viewport for each fit mode:
///
/// <list type="bullet">
///   <item><b>Page width</b> — the whole page (edge to edge) fits the viewport width.</item>
///   <item><b>Text width</b> — only the printable content area (page minus left/right margins) fits the
///   viewport width, so the text column fills the available room (Word zooms further than Page width).</item>
///   <item><b>Whole page</b> — the whole page fits within the viewport in both dimensions (the smaller of
///   the width-fit and height-fit factors), so an entire page is visible.</item>
/// </list>
///
/// All results are clamped to the supported zoom range (<see cref="ZoomLevels.Min"/>..<see cref="ZoomLevels.Max"/>),
/// matching the status-bar slider and the rest of the zoom plumbing. The arithmetic lives in the model so it
/// can be unit-tested without WPF; the host (MainWindow) supplies the live viewport size and applies the
/// factor to <c>DocumentView.ZoomLevel</c>.
/// </summary>
public static class ZoomFit
{
    /// <summary>
    /// The zoom factor that fits the full page width (<paramref name="pageWidthDip"/>) into a viewport
    /// <paramref name="viewportWidthDip"/> wide. Degenerate inputs (non-positive sizes) fall back to
    /// <see cref="ZoomLevels.Default"/>.
    /// </summary>
    public static double PageWidth(double pageWidthDip, double viewportWidthDip) =>
        FitAxis(pageWidthDip, viewportWidthDip);

    /// <summary>
    /// The zoom factor that fits the printable text column (page width minus the left/right margins) into a
    /// viewport <paramref name="viewportWidthDip"/> wide. Degenerate inputs fall back to
    /// <see cref="ZoomLevels.Default"/>.
    /// </summary>
    public static double TextWidth(double contentWidthDip, double viewportWidthDip) =>
        FitAxis(contentWidthDip, viewportWidthDip);

    /// <summary>
    /// The zoom factor that fits the whole page within the viewport in both dimensions: the smaller of the
    /// width-fit and height-fit factors, so an entire page is visible. Degenerate inputs fall back to
    /// <see cref="ZoomLevels.Default"/>.
    /// </summary>
    public static double WholePage(
        double pageWidthDip,
        double pageHeightDip,
        double viewportWidthDip,
        double viewportHeightDip)
    {
        var widthFit = RawFit(pageWidthDip, viewportWidthDip);
        var heightFit = RawFit(pageHeightDip, viewportHeightDip);
        if (widthFit is null && heightFit is null)
            return ZoomLevels.Default;
        var fit = System.Math.Min(widthFit ?? double.MaxValue, heightFit ?? double.MaxValue);
        return ZoomLevels.Clamp(fit);
    }

    // Fit one axis: the factor that scales target to fill viewport, clamped; falls back to default when the
    // geometry is degenerate (so a not-yet-measured viewport never produces a wild zoom).
    private static double FitAxis(double targetDip, double viewportDip) =>
        RawFit(targetDip, viewportDip) is { } fit ? ZoomLevels.Clamp(fit) : ZoomLevels.Default;

    // The unclamped fit ratio, or null when either size is non-positive (degenerate / unmeasured).
    private static double? RawFit(double targetDip, double viewportDip) =>
        targetDip > 0 && viewportDip > 0 ? viewportDip / targetDip : null;
}
