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
}
