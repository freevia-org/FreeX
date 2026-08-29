using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Two page-geometry rules that every printing/pagination/export/preview surface must apply
/// identically, extracted to a single shared home (R101) after each rule was independently
/// discovered and fixed in several different renderers over several rounds:
///
/// <list type="number">
/// <item>
/// <b>Header/footer-in-margin (R88/R96/R99/R100).</b> The header (or footer) band sits WITHIN the
/// top (or bottom) margin band, not in addition to it -- the printed body's edge only moves past the
/// plain margin once the header/footer margin is the LARGER of the two. See
/// <see cref="ResolveBodyEdge"/>.
/// </item>
/// <item>
/// <b>Uniform fit-to-page scale (R20/R100).</b> When both the "fit to N pages wide" and "fit to M
/// pages tall" constraints are active (or, defensively, when residual overflow remains on both axes
/// after a configured scale), Excel derives a SINGLE scale -- the smaller (more aggressive shrink) of
/// what each axis alone would need -- and applies that same scale uniformly to both axes. It never
/// resolves each axis to its own independent shrink. See <see cref="ResolveUniformScale"/>.
/// </item>
/// </list>
///
/// Both members are pure, unit-agnostic arithmetic (the caller's inches/pixels/points/fractions are
/// preserved as given) so every renderer -- pagination capacity, PDF export, print/print-preview
/// rendering -- can call the same formula instead of re-deriving it, which is exactly the drift that
/// let the footer half of rule 1 and the residual-overflow half of rule 2 slip through un-mirrored in
/// earlier rounds.
/// </summary>
public static class PageGeometryRules
{
    /// <summary>
    /// Resolves where the printed body's edge (top or bottom) sits once the header/footer band is
    /// accounted for: the header/footer margin only pushes the body edge further out than the plain
    /// page margin when it is larger than that margin -- it never stacks on top of it. Apply this once
    /// per edge (top with header, bottom with footer); the two calls are symmetric and both must be
    /// present together, since fixing only one side (as happened in a past round) leaves the other
    /// silently wrong.
    /// </summary>
    /// <param name="margin">The plain page margin for this edge (top or bottom), in any consistent unit.</param>
    /// <param name="headerOrFooterMargin">The header (with <paramref name="margin"/> = top margin) or
    /// footer (with <paramref name="margin"/> = bottom margin) margin, in the same unit.</param>
    /// <returns>The distance from the page edge to the printed body's edge, in the same unit.</returns>
    public static double ResolveBodyEdge(double margin, double headerOrFooterMargin) =>
        Math.Max(margin, headerOrFooterMargin);

    /// <summary>
    /// Combines two independently-computed axis shrink scales (width and height, expressed as
    /// fractions where 1.0 = no shrink) into the single uniform scale Excel actually applies: the
    /// smaller of the two, so neither axis is ever scaled more than the other and the printed content's
    /// aspect ratio never distorts. Used both for the primary "fit to N wide by M tall" resolution and
    /// for any defensive residual-overflow shrink layered on top of an explicit scale -- both are the
    /// same "take whichever axis needs the bigger shrink, apply it to both" rule.
    /// </summary>
    /// <param name="widthScale">The shrink fraction the horizontal axis alone would need (1.0 = none).</param>
    /// <param name="heightScale">The shrink fraction the vertical axis alone would need (1.0 = none).</param>
    /// <returns>The single uniform scale fraction to apply to both axes.</returns>
    public static double ResolveUniformScale(double widthScale, double heightScale) =>
        Math.Min(widthScale, heightScale);

    /// <summary>
    /// R168-shared-headerfooter-band-cap-1: the largest fraction of the page a single header OR
    /// footer band may claim once "size the band to its content" lets it grow past its base line
    /// height to fit a configured picture. Round 166 fixed a header/footer picture's own DIP-unit
    /// conversion (a picture is no longer stored at 1-4x its physical size), but that narrows the
    /// range of inputs reaching the band-height math -- it does not bound them: even a correctly-
    /// converted, genuinely large photo (the auditor's own probe used a 72 DPI image, whose
    /// DIP-converted size is LARGER than its raw pixel count) would otherwise flow straight into the
    /// band height with no upper limit, so the band could balloon to many times the page itself.
    /// There is no Excel precedent to match here -- real Excel does not grow the header/footer margin
    /// to fit an inserted picture at all (an oversized picture there simply overlaps the sheet body);
    /// this app's <c>SizeHeaderFooterBandsToContent</c> behavior is a deliberate departure (see the
    /// existing 48px-band test for a 96x48 default picture, comfortably exceeding the 28.8px default
    /// header margin -- so this bound must stay well above a "page margin"-sized cap or it would
    /// break that already-intentional growth). A quarter of the page leaves the other three quarters
    /// for the printed body even in the worst case of both header and footer maxing out
    /// simultaneously, while comfortably fitting every legitimate case (multi-line text, or a picture
    /// sized through the app's own Format Picture / header-footer picture dialogs).
    /// </summary>
    public const double MaxHeaderFooterBandHeightFraction = 0.25;

    /// <summary>
    /// Resolves a header/footer band's final height: grow the text-derived base height to fit the
    /// tallest picture actually configured in the band, then cap the result at
    /// <see cref="MaxHeaderFooterBandHeightFraction"/> of the page height (never below 1, so a
    /// degenerate page height cannot collapse the band to nothing). An oversized picture must shrink
    /// to fit the band -- see <see cref="ResolveUniformScale"/>, which each caller then applies to
    /// fit the picture into whatever height this returns -- rather than the band ballooning to
    /// swallow the page.
    ///
    /// Extracted to this shared home (R168-shared-headerfooter-band-cap-1) after the identical rule,
    /// and the identical 0.25 bound, had to be written twice: once inline in
    /// <c>WorksheetPrintHeaderFooterGeometryPlanner</c> (the WPF-shared print/print-preview/WPF-PDF
    /// geometry, R167-presentation-headerfooter-band-bound-1) and again in
    /// <c>WorkbookPdfContentBuilder</c> (the Avalonia/Skia PDF export geometry,
    /// R167-services-avalonia-headerfooter-picture-band-1), because those two files build their own
    /// geometry models and share no band type. Only the arithmetic RULE is shared here -- this method
    /// is pure and unit-agnostic (the caller's pixels/points/DIPs are preserved as given), so each
    /// caller keeps its own picture-token / per-section logic and its own unit conversion and merely
    /// delegates the grow-then-cap step, instead of the bound relying on "remember to change both"
    /// (which had already failed twice, across rounds 166 and 167).
    /// </summary>
    /// <param name="baseHeight">The band height the text alone requires (line height * line count),
    /// in any consistent unit.</param>
    /// <param name="tallestPictureHeight">The height of the tallest picture actually configured in
    /// this band, already converted to the same unit; 0 when the band has no picture.</param>
    /// <param name="pageHeight">The full page height in the same unit. Pass
    /// <see cref="double.PositiveInfinity"/> from a caller that has no page context and must stay
    /// uncapped.</param>
    /// <returns>The band height to use, in the same unit.</returns>
    public static double ResolveHeaderFooterBandHeight(
        double baseHeight,
        double tallestPictureHeight,
        double pageHeight) =>
        Math.Min(
            Math.Max(baseHeight, tallestPictureHeight),
            Math.Max(1.0, pageHeight * MaxHeaderFooterBandHeightFraction));

    /// <summary>
    /// Resolves Excel's Page Setup &gt; Header/Footer &gt; "Scale with document" checkbox
    /// (<c>Sheet.HeaderFooterScaleWithDocument</c>, default checked) into the multiplier a renderer
    /// should apply to header/footer TEXT font size and line spacing. The flag governs ONLY the
    /// header/footer text's own size -- it has no effect on the grid/content scale
    /// (<paramref name="contentScaleRatio"/>) itself, which every renderer always applies regardless
    /// of this flag, and it never affects an inserted header/footer picture's own size. When checked
    /// (the default), header/footer text shrinks/grows by the exact same ratio as the page's grid
    /// content; when unchecked, Excel keeps header/footer text at its authored size no matter how the
    /// page content is scaled (so this returns 1.0 -- a no-op). Extracted to this shared home
    /// (R112-presentation-headerfooter-scale-with-document-shared-1) so the native desktop print/
    /// print-preview renderer and the portable PDF export tier -- which each need to derive this
    /// exact same multiplier from their own independently-resolved content scale ratio -- consult one
    /// formula instead of two copies that can silently drift apart.
    /// </summary>
    /// <param name="scaleWithDocument">Sheet.HeaderFooterScaleWithDocument.</param>
    /// <param name="contentScaleRatio">The renderer's own fully-resolved grid/content scale ratio for
    /// this page (1.0 = no scaling), already reflecting the sheet's Scale%/Fit-to-pages setting and
    /// any defensive residual-overflow shrink.</param>
    /// <returns>The multiplier to apply to header/footer font size and line spacing.</returns>
    public static double ResolveHeaderFooterFontScale(bool scaleWithDocument, double contentScaleRatio) =>
        scaleWithDocument ? contentScaleRatio : 1.0;

    /// <summary>
    /// Returns whether <paramref name="value"/> (a row or column index) falls inside a print-title
    /// repeat range, e.g. so a caller walking a print range can skip title rows/columns it will already
    /// account for separately via <see cref="CountRepeatItems"/>.
    /// </summary>
    public static bool IsWithinRepeatRange(WorksheetRepeatRange? repeatRange, uint value) =>
        repeatRange is { } range && value >= range.Start && value <= range.End;

    /// <summary>
    /// Counts the rows/columns in a repeat (print titles) range, clipped to <paramref name="maxItem"/>,
    /// that will actually be reprinted on every page. When <paramref name="isHidden"/> is supplied,
    /// hidden rows/columns within the repeat range are excluded -- they take no print space, so
    /// including them would overstate the fit-to-N-pages target the same way it would understate the
    /// free per-page budget (R102-presentation-pagination-fit-to-pages-hidden-exclusion). Extracted to
    /// this shared home (R102) after the identical rule independently existed in
    /// <c>PagePaginationPlanner</c> (desktop print path) and <c>SheetPdfPageSetupResolver</c> (PDF
    /// export path) and had drifted on this exact hidden-item handling.
    /// </summary>
    public static uint CountRepeatItems(WorksheetRepeatRange? repeat, uint maxItem, Func<uint, bool>? isHidden = null)
    {
        if (repeat is not { } range || range.Start == 0 || range.Start > maxItem || range.End < range.Start)
            return 0;

        var end = Math.Min(range.End, maxItem);
        if (isHidden is null)
            return end - range.Start + 1;

        uint count = 0;
        for (var value = range.Start; value <= end; value++)
        {
            if (!isHidden(value))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Counts the rows/columns in [<paramref name="start"/>, <paramref name="end"/>] that are body
    /// (non-title) items to be paginated by a fit-to-N-pages request. When <paramref name="isHidden"/>
    /// is supplied, hidden rows/columns are excluded -- matching what the accumulation-break walk /
    /// print layout planner actually place onto each printed page. See <see cref="CountRepeatItems"/>
    /// for the sibling rule and its shared-home history (R102).
    /// </summary>
    public static uint CountBodyItems(uint start, uint end, WorksheetRepeatRange? repeat, Func<uint, bool>? isHidden = null)
    {
        if (end < start) return 0;

        if (isHidden is null)
        {
            var count = end - start + 1;
            if (repeat is not { } range || range.End < start || range.Start > end)
                return count;

            var overlapStart = Math.Max(start, range.Start);
            var overlapEnd = Math.Min(end, range.End);
            return overlapEnd >= overlapStart
                ? count - (overlapEnd - overlapStart + 1)
                : count;
        }

        uint visibleCount = 0;
        for (var value = start; value <= end; value++)
        {
            if (IsWithinRepeatRange(repeat, value) || isHidden(value))
                continue;

            visibleCount++;
        }

        return visibleCount;
    }
}
