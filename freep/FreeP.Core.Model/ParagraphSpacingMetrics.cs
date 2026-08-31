namespace FreeP.Core.Model;

/// <summary>
/// Shared basis for percent-authored paragraph spacing (<c>a:spcBef</c>/<c>a:spcAft</c> with
/// <c>a:spcPct</c>). ECMA-376 defines the percentage against a single line's height — the same
/// "one line" notion <c>a:lnSpc/a:spcPct</c> uses. No stage of the pipeline measures a bare
/// single line, so every consumer estimates it from the paragraph's largest font size; this type
/// keeps that estimate, and the spcPts-wins-over-spcPct precedence, in one place rather than
/// repeated across the compositor, the layout planner, the .pptx writer and the clipboard writers.
/// </summary>
public static class ParagraphSpacingMetrics
{
    /// <summary>Multiple of the font size that makes up a single line's height.</summary>
    public const double LineHeightFactor = 1.2;

    /// <summary>Font size assumed for a run that carries none.</summary>
    public const double DefaultFontSizePt = 18.0;

    /// <summary>A single line's height in points at the given font size.</summary>
    public static double SingleLineHeightPoints(double fontSizePt) =>
        fontSizePt > 0 ? fontSizePt * LineHeightFactor : 0;

    /// <summary>
    /// The paragraph's largest authored run font size in points, falling back to
    /// <paramref name="fallbackPt"/> for runs (or paragraphs) that carry none.
    /// </summary>
    public static double MaxRunFontSizePoints(Paragraph paragraph, double fallbackPt = DefaultFontSizePt)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        double maxPt = 0;
        foreach (var run in paragraph.Runs)
            maxPt = Math.Max(maxPt, run.FontSizePt ?? fallbackPt);
        return maxPt > 0 ? maxPt : fallbackPt;
    }

    /// <summary>
    /// Resolves an authored points/percent spacing pair to absolute points against
    /// <paramref name="singleLineHeightPt"/>. The explicit points value wins: spcPts and spcPct
    /// are mutually exclusive per ECMA-376 and spcPts is the more specific child.
    /// </summary>
    public static double ResolvePoints(double? points, double? percent, double singleLineHeightPt) =>
        points ?? (percent is { } pct && pct > 0 ? pct / 100.0 * singleLineHeightPt : 0);

    /// <summary>
    /// The paragraph's effective space-before in points. <paramref name="fontSizePt"/> overrides
    /// the font size the percent basis is computed from (pass an autofit-scaled size where run
    /// sizes are scaled separately); null uses the paragraph's own largest run size.
    /// </summary>
    public static double ResolveSpaceBeforePoints(Paragraph paragraph, double? fontSizePt = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return ResolvePoints(
            paragraph.SpaceBeforePt,
            paragraph.SpaceBeforePercent,
            SingleLineHeightPoints(fontSizePt ?? MaxRunFontSizePoints(paragraph)));
    }

    /// <summary>
    /// The paragraph's effective space-after in points. See <see cref="ResolveSpaceBeforePoints"/>.
    /// </summary>
    public static double ResolveSpaceAfterPoints(Paragraph paragraph, double? fontSizePt = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return ResolvePoints(
            paragraph.SpaceAfterPt,
            paragraph.SpaceAfterPercent,
            SingleLineHeightPoints(fontSizePt ?? MaxRunFontSizePoints(paragraph)));
    }
}
