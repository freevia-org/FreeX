using System.Globalization;
using System.Linq;
using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

public readonly record struct TextLayoutArea(
    double X,
    double Y,
    double Width,
    double Height);

public readonly record struct TextParagraphMeasure(
    int ParagraphIndex,
    double HeightDip,
    double SpaceBeforeDip,
    double SpaceAfterDip)
{
    public double TotalHeightDip => HeightDip + SpaceBeforeDip + SpaceAfterDip;
}

public readonly record struct TextParagraphPlacement(
    int ParagraphIndex,
    int ColumnIndex,
    double X,
    double Y,
    double MaxWidthDip,
    TextBulletPlacement? Bullet)
{
    public TextParagraphRenderRoute RenderRoute { get; init; }

    public TextParagraphPlacement(
        int paragraphIndex,
        int columnIndex,
        double x,
        double y,
        double maxWidthDip)
        : this(paragraphIndex, columnIndex, x, y, maxWidthDip, null)
    {
    }
}

public readonly record struct TextColumnLineMeasure(
    int ParagraphIndex,
    int LineIndex,
    double HeightDip,
    double SpaceBeforeDip,
    double SpaceAfterDip,
    bool IsFirstLine,
    bool IsLastLine)
{
    public double TotalHeightDip => HeightDip + SpaceBeforeDip + SpaceAfterDip;
}

public readonly record struct TextColumnLinePlacement(
    int ParagraphIndex,
    int LineIndex,
    int ColumnIndex,
    double X,
    double Y,
    double MaxWidthDip,
    bool IsFirstLine);

public readonly record struct TextBulletPlacement(
    string Text,
    string FontFamily,
    double FontSizePt,
    SrgbColor Color,
    ImagePart? Image,
    double X,
    double Y)
{
    public bool IsImage => Image is not null;
}

public readonly record struct TextTabSegmentPlacement(
    int RunIndex,
    string Text,
    double X,
    TabStopLeader Leader = TabStopLeader.None);

/// <summary>
/// Renderer-neutral visual placement for one paragraph run.  <see cref="RunIndex"/>
/// remains the logical model index while the returned sequence is in visual order.
/// </summary>
public readonly record struct TextRunPlacement(
    int RunIndex,
    double X,
    double Width,
    bool RightToLeft);

public readonly record struct TextBaselineFragmentMeasure(
    double WidthDip,
    double AscentDip,
    double HeightDip);

public readonly record struct TextBaselineFragmentPlacement(
    int RunIndex,
    string Text,
    double X,
    double Y,
    double WidthDip,
    double AscentDip,
    double HeightDip,
    bool RightToLeft);

public sealed record TextBaselineLinePlan(
    double TopY,
    double BaselineY,
    double WidthDip,
    double HeightDip,
    IReadOnlyList<TextBaselineFragmentPlacement> Fragments);

public readonly record struct TextInlineRunMeasure(
    double WidthDip,
    double AscentDip,
    double HeightDip);

public readonly record struct TextInlineRunPlacement(
    int RunIndex,
    double X,
    double Y,
    double WidthDip,
    double AscentDip,
    double HeightDip,
    bool RightToLeft);

public sealed record TextInlineBaselineLinePlan(
    double TopY,
    double BaselineY,
    double WidthDip,
    double HeightDip,
    IReadOnlyList<TextInlineRunPlacement> Runs);

public readonly record struct TextColumnLayout(
    TextLayoutArea Area,
    int ColumnCount,
    double ColumnSpacingDip,
    double ColumnWidthDip,
    double LineSpacingScale);

public readonly record struct TextOrientationPlan(
    TextVerticalType VerticalType,
    LayoutRect TextBounds,
    TextVerticalRenderMode RenderMode,
    double RotationAngleDegrees,
    double RotationCenterX,
    double RotationCenterY)
{
    public bool IsRotated => Math.Abs(RotationAngleDegrees) > 0.001;
}

public enum TextVerticalRenderMode
{
    Horizontal,
    Rotated,
    StackedUpright
}

public readonly record struct TextGlyphMeasure(
    double WidthDip,
    double HeightDip);

public readonly record struct TextStackedGlyphPlacement(
    int ParagraphIndex,
    int RunIndex,
    string Text,
    double X,
    double Y,
    double WidthDip,
    double HeightDip);

public sealed record TextStackedVerticalLayoutPlan(
    TextLayoutArea Area,
    TextVerticalType VerticalType,
    TextVerticalRenderMode RenderMode,
    IReadOnlyList<TextParagraphMeasure> Paragraphs,
    IReadOnlyList<TextStackedGlyphPlacement> Glyphs)
{
    public double TotalHeightDip => Paragraphs.Sum(p => p.TotalHeightDip);
}

public enum TextAutoFitOverflowMode
{
    NoAutoFit,
    StoredFontScale,
    Fits,
    RuntimeShrink
}

public readonly record struct TextAutoFitOverflowPlan(
    TextAutoFitOverflowMode Mode,
    double FontScale,
    double LineSpacingReduction)
{
    public bool AppliesRuntimeShrink =>
        Mode == TextAutoFitOverflowMode.RuntimeShrink &&
        (Math.Abs(FontScale - 1.0) > 0.001 || LineSpacingReduction > 0.0);
}

public enum TextParagraphRenderRoute
{
    Plain,
    Tabs,
    Effects,
    /// <summary>
    /// Plain text with one or more authored DrawingML baseline offsets.
    /// </summary>
    Baseline,
    /// <summary>
    /// The paragraph contains one or more OMML math runs.
    /// The renderer should call <see cref="FreeP.App.Compositor.Math.MathBoxRenderPlanner.Plan"/>
    /// for each math run and draw the resulting operations inline.
    /// </summary>
    Math
}

public sealed record TextBlockLayoutPlan(
    TextLayoutArea Area,
    IReadOnlyList<TextParagraphPlacement> Paragraphs);

public readonly record struct TextNativeMeasurement<TArtifact>(
    TArtifact Artifact,
    double HeightDip,
    double WidthDip = 0);

public readonly record struct TextParagraphMeasurementRequest(
    int ParagraphIndex,
    ResolvedTextLayout Text,
    ResolvedParagraph Paragraph,
    double MaxWidthDip,
    bool UseIdealMetrics);

public sealed record TextMeasuredBlockLayoutPlan<TArtifact>(
    ResolvedTextLayout RenderText,
    TextAutoFitOverflowPlan AutoFit,
    TextBlockLayoutPlan Layout,
    IReadOnlyDictionary<int, TArtifact> Artifacts);

public enum TextColumnMeasurementPhase
{
    WrapProbe,
    LineLayout,
    Render
}

public readonly record struct TextColumnMeasurementRequest(
    TextColumnMeasurementPhase Phase,
    int ParagraphIndex,
    int LineIndex,
    ResolvedParagraph Paragraph,
    double MaxWidthDip,
    bool Wrap,
    double HorizontalScale);

public sealed record TextContinuousColumnLinePlan<TArtifact>(
    ResolvedParagraph Paragraph,
    TextColumnLinePlacement Placement,
    TArtifact Artifact,
    double HorizontalScale);

public sealed record TextContinuousColumnFlowPlan<TArtifact>(
    bool IsApplicable,
    TextColumnLayout Layout,
    IReadOnlyList<TextContinuousColumnLinePlan<TArtifact>> Lines);

public sealed record TextTabLayoutPlan(
    IReadOnlyList<TextTabSegmentPlacement> Segments);

public static class TextLayoutPlanner
{
    public const double DipPerPoint = 96.0 / 72.0;
    public const double DefaultColumnSpacingDip = 48.5;
    public const double DefaultTabStopDip = 96.0;

    /// <summary>
    /// Returns the glyph used to paint an RTF tab leader. The box-drawing glyph is
    /// intentional: it is the closest renderer-neutral representation of RTF's
    /// thick-line leader while keeping the host canvases responsible only for paint.
    /// </summary>
    public static char GetTabLeaderGlyph(TabStopLeader leader) =>
        leader switch
        {
            TabStopLeader.Dots => '.',
            TabStopLeader.Hyphens => '-',
            TabStopLeader.Underscore => '_',
            TabStopLeader.ThickLine => '\u2501',
            TabStopLeader.Equal => '=',
            _ => '\0',
        };
    public const double ImportedAptosBodyOriginOffsetY = 6.0;
    public const double RuntimeAutoFitMinimumFontScale = 0.60;
    public const double RuntimeAutoFitMaximumLineSpacingReduction = 0.20;
    /// <summary>PowerPoint's authored baseline runs use a compact script glyph.</summary>
    public const double BaselineRunFontScale = 0.67;

    public static double PointsToDip(double points) => points * DipPerPoint;

    public static bool UsesImportedAptosBodyOrigin(ResolvedTextLayout text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.AutoFitKind == TextAutoFitKind.Shape &&
            text.Paragraphs.Count == 6 &&
            text.Paragraphs.All(paragraph =>
                paragraph.Runs.Count == 1 &&
                string.Equals(paragraph.Runs[0].FontFamily, "Aptos", StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(paragraph.Runs[0].FontSizePt - 18.0) < 0.01 &&
                !paragraph.Runs[0].Bold &&
                !paragraph.Runs[0].Italic &&
                paragraph.BulletKind != BulletKind.None);
    }

    public static double ResolveImportedAptosBodyOriginOffsetY(ResolvedTextLayout text) =>
        UsesImportedAptosBodyOrigin(text) ? ImportedAptosBodyOriginOffsetY : 0.0;

    public static TextOrientationPlan PlanTextOrientation(
        ResolvedTextLayout text,
        LayoutRect bounds)
    {
        ArgumentNullException.ThrowIfNull(text);

        var renderMode = GetVerticalRenderMode(text.VerticalType);
        double angleDegrees = text.VerticalType switch
        {
            TextVerticalType.Vertical270 => -90.0,
            TextVerticalType.Vertical => 90.0,
            _ => 0.0
        };

        var textBounds = angleDegrees == 0.0
            ? bounds
            : new LayoutRect(
                bounds.X + (bounds.Width - bounds.Height) * 0.5,
                bounds.Y + (bounds.Height - bounds.Width) * 0.5,
                bounds.Height,
                bounds.Width);

        return new TextOrientationPlan(
            text.VerticalType,
            textBounds,
            renderMode,
            angleDegrees,
            bounds.X + bounds.Width * 0.5,
            bounds.Y + bounds.Height * 0.5);
    }

    public static TextVerticalRenderMode GetVerticalRenderMode(TextVerticalType verticalType) =>
        verticalType switch
        {
            TextVerticalType.Vertical or TextVerticalType.Vertical270 => TextVerticalRenderMode.Rotated,
            TextVerticalType.EastAsianVertical
                or TextVerticalType.WordArtVertical
                or TextVerticalType.WordArtVerticalRtl => TextVerticalRenderMode.StackedUpright,
            _ => TextVerticalRenderMode.Horizontal
        };

    public static TextLayoutArea GetTextArea(ResolvedTextLayout text, LayoutRect bounds)
    {
        double width = Math.Max(0, bounds.Width - text.InsetLeftDip - text.InsetRightDip);
        double height = Math.Max(0, bounds.Height - text.InsetTopDip - text.InsetBottomDip);

        return new TextLayoutArea(
            bounds.X + text.InsetLeftDip,
            bounds.Y + text.InsetTopDip,
            width,
            height);
    }

    public static double GetLineSpacingScale(ResolvedTextLayout text) =>
        GetLineSpacingScale(text, default);

    public static double GetLineSpacingScale(
        ResolvedTextLayout text,
        TextAutoFitOverflowPlan autoFitPlan)
    {
        double storedScale = 1.0 - Math.Clamp(text.LnSpcReduction, 0.0, 0.95);
        double runtimeScale = 1.0 - Math.Clamp(autoFitPlan.LineSpacingReduction, 0.0, RuntimeAutoFitMaximumLineSpacingReduction);
        return Math.Clamp(storedScale * runtimeScale, 0.05, 1.0);
    }

    public static TextAutoFitOverflowPlan PlanNormalAutoFitOverflow(
        ResolvedTextLayout text,
        double textAreaHeightDip,
        IReadOnlyList<TextParagraphMeasure> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(paragraphs);

        // LA1: only a:normAutofit shrinks TEXT to fit a fixed box. a:spAutoFit (Shape) grows the
        // SHAPE to fit text instead — the text itself must never be runtime-shrunk for it. Treat
        // both "no autofit" and "shape autofit" as NoAutoFit for the purposes of this text-shrink planner.
        if (text.AutoFitKind != TextAutoFitKind.Normal)
            return new TextAutoFitOverflowPlan(TextAutoFitOverflowMode.NoAutoFit, 1.0, 0.0);

        if (text.HasStoredFontScale && text.FontScale > 0)
            return PlanStoredFontScaleOverflow(text, textAreaHeightDip, paragraphs);

        double measuredHeight = paragraphs.Sum(p => p.TotalHeightDip);
        if (textAreaHeightDip <= 0 || measuredHeight <= 0)
            return new TextAutoFitOverflowPlan(TextAutoFitOverflowMode.Fits, 1.0, 0.0);

        double effectiveHeight = measuredHeight * GetLineSpacingScale(text);
        if (effectiveHeight <= textAreaHeightDip + 0.5)
            return new TextAutoFitOverflowPlan(TextAutoFitOverflowMode.Fits, 1.0, 0.0);

        double requiredScale = textAreaHeightDip / effectiveHeight;
        double fontScale = Math.Clamp(requiredScale, RuntimeAutoFitMinimumFontScale, 1.0);
        double projectedHeight = effectiveHeight * fontScale;
        double lineSpacingReduction = 0.0;
        if (projectedHeight > textAreaHeightDip + 0.5)
        {
            double requiredLineScale = textAreaHeightDip / projectedHeight;
            lineSpacingReduction = Math.Clamp(1.0 - requiredLineScale, 0.0, RuntimeAutoFitMaximumLineSpacingReduction);
        }

        return new TextAutoFitOverflowPlan(
            TextAutoFitOverflowMode.RuntimeShrink,
            fontScale,
            lineSpacingReduction);
    }

    /// <summary>
    /// A stored <c>a:normAutofit fontScale</c>/<c>lnSpcReduction</c> reflects what PowerPoint (or an
    /// earlier FreeP session) computed for the box size <em>at the time it was cached</em>. Trusting
    /// it forever leaves stale text scaling after the shape is resized or the text body is edited:
    /// shrink the box and the cached scale under-shrinks (overflow); grow the box and the cached
    /// scale keeps the text needlessly small. Recompute against the CURRENT geometry every time
    /// instead, using the same overflow math as the no-cache path above.
    /// <para>
    /// <see cref="SlideCompositor"/> already multiplies every run (and bullet) font size by the
    /// cached scale when it resolves the text body, so <paramref name="paragraphs"/> arrives here
    /// already shrunk by <c>text.FontScale</c>. Back that out to recover the paragraphs' authored
    /// (100%) height before re-deriving what the current box actually needs, then express the result
    /// as a correction relative to the cached baseline — <see cref="ApplyAutoFitPlan(ResolvedTextLayout, TextAutoFitOverflowPlan)"/>
    /// multiplies the already-scaled run sizes by this correction to reach the recomputed target.
    /// </para>
    /// </summary>
    private static TextAutoFitOverflowPlan PlanStoredFontScaleOverflow(
        ResolvedTextLayout text,
        double textAreaHeightDip,
        IReadOnlyList<TextParagraphMeasure> paragraphs)
    {
        double cachedScale = text.FontScale;
        double unscaledHeight = paragraphs.Sum(p =>
            p.HeightDip / cachedScale + p.SpaceBeforeDip + p.SpaceAfterDip);
        if (textAreaHeightDip <= 0 || unscaledHeight <= 0)
            return new TextAutoFitOverflowPlan(TextAutoFitOverflowMode.StoredFontScale, 1.0, 0.0);

        double unscaledEffectiveHeight = unscaledHeight * GetLineSpacingScale(text);
        double targetFontScale = 1.0;
        double targetLineSpacingReduction = 0.0;
        if (unscaledEffectiveHeight > textAreaHeightDip + 0.5)
        {
            double requiredScale = textAreaHeightDip / unscaledEffectiveHeight;
            targetFontScale = Math.Clamp(requiredScale, RuntimeAutoFitMinimumFontScale, 1.0);
            double projectedHeight = unscaledEffectiveHeight * targetFontScale;
            if (projectedHeight > textAreaHeightDip + 0.5)
            {
                double requiredLineScale = textAreaHeightDip / projectedHeight;
                targetLineSpacingReduction = Math.Clamp(1.0 - requiredLineScale, 0.0, RuntimeAutoFitMaximumLineSpacingReduction);
            }
        }

        // The recomputed target already accounts for the (possibly stale) cached scale, since the
        // measured heights were derived from it. Express the result as a multiplicative correction
        // relative to that cached baseline; when nothing has changed since the cache was produced,
        // the correction collapses to ~1.0 and the cached scale is kept as-is (no object churn, no
        // double-scaling — same outcome PowerPoint's own cache would give for an unchanged box).
        double correctionScale = targetFontScale / cachedScale;
        bool needsCorrection = Math.Abs(correctionScale - 1.0) > 0.001 || targetLineSpacingReduction > 0.0;
        if (!needsCorrection)
            return new TextAutoFitOverflowPlan(TextAutoFitOverflowMode.StoredFontScale, 1.0, 0.0);

        return new TextAutoFitOverflowPlan(
            TextAutoFitOverflowMode.RuntimeShrink,
            correctionScale,
            targetLineSpacingReduction);
    }

    /// <summary>
    /// Resolves the rendered bounds for DrawingML <c>a:spAutoFit</c>. Unlike
    /// <c>a:normAutofit</c>, the text keeps its authored metrics and the shape grows
    /// to contain the measured paragraphs. Multi-column text remains on the existing
    /// route because its fragment allocation needs a separate geometry contract.
    /// </summary>
    public static LayoutRect PlanShapeAutoFitBounds(
        ResolvedTextLayout text,
        LayoutRect bounds,
        IReadOnlyList<TextParagraphMeasure> paragraphs)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(paragraphs);

        if (text.AutoFitKind != TextAutoFitKind.Shape || text.ColumnCount > 1)
            return bounds;

        double requiredHeight = text.InsetTopDip + text.InsetBottomDip +
            paragraphs.Sum(paragraph => paragraph.TotalHeightDip);
        if (requiredHeight <= bounds.Height + 0.5)
            return bounds;

        double delta = requiredHeight - bounds.Height;
        double y = text.Anchor switch
        {
            VerticalAnchor.Middle => bounds.Y - delta / 2.0,
            VerticalAnchor.Bottom => bounds.Y - delta,
            _ => bounds.Y
        };
        return new LayoutRect(bounds.X, y, bounds.Width, requiredHeight);
    }

    public static ResolvedTextLayout ApplyAutoFitPlan(
        ResolvedTextLayout text,
        TextAutoFitOverflowPlan plan)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!plan.AppliesRuntimeShrink)
            return text;

        // When text.FontScale already carries a cached normAutofit scale, run/bullet font sizes
        // were pre-multiplied by it in SlideCompositor — plan.FontScale here is only the
        // *correction* relative to that baseline (see PlanStoredFontScaleOverflow). Paragraph
        // spacing (SpaceBeforePt/AfterPt), by contrast, was never pre-scaled, so it needs the
        // *absolute* target (baseline * correction) to land on the same final proportion as the
        // fonts. Pass the baseline through so ApplyAutoFitPlan(ResolvedParagraph, ...) can tell
        // the two apart; for the no-cache path baseline is 1.0 and both notions coincide.
        double baselineFontScale = text.HasStoredFontScale && text.FontScale > 0 ? text.FontScale : 1.0;

        return new ResolvedTextLayout
        {
            Paragraphs = text.Paragraphs
                .Select(paragraph => ApplyAutoFitPlan(paragraph, plan, baselineFontScale))
                .ToArray(),
            Anchor = text.Anchor,
            InsetLeftDip = text.InsetLeftDip,
            InsetRightDip = text.InsetRightDip,
            InsetTopDip = text.InsetTopDip,
            InsetBottomDip = text.InsetBottomDip,
            Wrap = text.Wrap,
            WarpPreset = text.WarpPreset,
            WarpAdjusts = text.WarpAdjusts,
            Text3dEffects = text.Text3dEffects,
            VerticalType = text.VerticalType,
            AutoFitKind = text.AutoFitKind,
            HasStoredFontScale = text.HasStoredFontScale,
            FontScale = text.FontScale * plan.FontScale,
            // The runtime plan's LineSpacingReduction now fully represents the reduction the
            // CURRENT geometry needs (recomputed fresh — see PlanStoredFontScaleOverflow); keep
            // the stale cached LnSpcReduction from double-applying on top of it via
            // GetLineSpacingScale's storedScale term.
            LnSpcReduction = 0.0,
            ColumnCount = text.ColumnCount,
            ColumnSpacingDip = text.ColumnSpacingDip
        };
    }

    public static ResolvedParagraph ApplyAutoFitPlan(
        ResolvedParagraph paragraph,
        TextAutoFitOverflowPlan plan) =>
        ApplyAutoFitPlan(paragraph, plan, baselineFontScale: 1.0);

    private static ResolvedParagraph ApplyAutoFitPlan(
        ResolvedParagraph paragraph,
        TextAutoFitOverflowPlan plan,
        double baselineFontScale)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (!plan.AppliesRuntimeShrink)
            return paragraph;

        // Run/bullet font sizes are already scaled by baselineFontScale (see ApplyAutoFitPlan
        // above), so plan.FontScale — relative to that baseline — is the right multiplier for
        // them. Paragraph spacing was never pre-scaled, so it needs the absolute target instead.
        double fontScale = plan.FontScale;
        double spaceScale = plan.FontScale * baselineFontScale;
        return new ResolvedParagraph
        {
            Runs = paragraph.Runs.Select(run => new ResolvedRun
            {
                Text = run.Text,
                FontFamily = run.FontFamily,
                FontSizePt = run.FontSizePt * fontScale,
                BaselineOffset = run.BaselineOffset,
                Bold = run.Bold,
                Italic = run.Italic,
                Underline = run.Underline,
                Strikethrough = run.Strikethrough,
                RightToLeft = run.RightToLeft,
                Color = run.Color,
                TextFill = run.TextFill,
                TextOutline = run.TextOutline,
                TextShadow = run.TextShadow,
                TextReflection = run.TextReflection,
                TextGlow = run.TextGlow,
                TextSoftEdge = run.TextSoftEdge,
                MathLayout = run.MathLayout
            }).ToArray(),
            Align = paragraph.Align,
            Level = paragraph.Level,
            BulletKind = paragraph.BulletKind,
            BulletChar = paragraph.BulletChar,
            SpaceBeforePt = paragraph.SpaceBeforePt * spaceScale,
            SpaceAfterPt = paragraph.SpaceAfterPt * spaceScale,
            LineSpacingPercent = paragraph.LineSpacingPercent,
            LineSpacingPointsExact = paragraph.LineSpacingPointsExact,
            TabStops = paragraph.TabStops,
            BulletText = paragraph.BulletText,
            BulletColor = paragraph.BulletColor,
            BulletFontFamily = paragraph.BulletFontFamily,
            BulletFontSizePt = paragraph.BulletFontSizePt * fontScale,
            BulletImage = paragraph.BulletImage,
            IndentDip = paragraph.IndentDip,
            HangingDip = paragraph.HangingDip
        };
    }

    public static TextParagraphRenderRoute PlanParagraphRenderRoute(
        ResolvedParagraph paragraph,
        ResolvedTextLayout text)
    {
        // Theme 27: OMML math runs — render inline using MathBoxRenderPlanner.
        if (paragraph.Runs.Any(r => r.IsMathRun))
            return TextParagraphRenderRoute.Math;

        if (text.WarpPreset is not null || HasTextEffects(paragraph))
            return TextParagraphRenderRoute.Effects;

        if (HasTabCharacters(paragraph))
            return TextParagraphRenderRoute.Tabs;

        return paragraph.Runs.Any(run => run.BaselineOffset.HasValue)
            ? TextParagraphRenderRoute.Baseline
            : TextParagraphRenderRoute.Plain;
    }

    /// <summary>
    /// Converts DrawingML ST_Percentage baseline units to slide-space DIPs.
    /// The token is one thousandth of a percent of the run's font size.
    /// Positive values raise the run; negative values lower it.
    /// </summary>
    public static double BaselineOffsetToDip(int? baselineOffset, double fontSizePt) =>
        baselineOffset.GetValueOrDefault() / 100000.0 * PointsToDip(fontSizePt);

    public static IReadOnlyList<string> SplitColumnText(
        string text,
        double maxWidthDip,
        bool wrap,
        Func<string, double> measureText)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureText);

        if (!wrap || maxWidthDip <= 0)
            return new[] { text };

        var words = text.Replace('\r', ' ').Replace('\n', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return new[] { string.Empty };

        var lines = new List<string>();
        string current = string.Empty;
        foreach (var word in words)
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && measureText(candidate) > maxWidthDip)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }

    public static ResolvedParagraph CloneParagraphWithText(
        ResolvedParagraph paragraph,
        ResolvedRun run,
        string text)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(text);

        return new ResolvedParagraph
        {
            Runs = new[]
            {
                new ResolvedRun
                {
                    Text = text,
                    FontFamily = run.FontFamily,
                    FontSizePt = run.FontSizePt,
                    BaselineOffset = run.BaselineOffset,
                    Bold = run.Bold,
                    Italic = run.Italic,
                    Underline = run.Underline,
                    Strikethrough = run.Strikethrough,
                    Color = run.Color,
                    TextFill = run.TextFill,
                    TextOutline = run.TextOutline,
                    TextShadow = run.TextShadow,
                    TextReflection = run.TextReflection,
                    TextGlow = run.TextGlow,
                    TextSoftEdge = run.TextSoftEdge,
                    MathLayout = run.MathLayout
                }
            },
            Align = paragraph.Align,
            RightToLeft = paragraph.RightToLeft,
            Level = paragraph.Level,
            BulletKind = paragraph.BulletKind,
            BulletChar = paragraph.BulletChar,
            BulletImage = paragraph.BulletImage,
            SpaceBeforePt = paragraph.SpaceBeforePt,
            SpaceAfterPt = paragraph.SpaceAfterPt,
            TabStops = paragraph.TabStops,
            BulletText = paragraph.BulletText,
            BulletColor = paragraph.BulletColor,
            BulletFontFamily = paragraph.BulletFontFamily,
            BulletFontSizePt = paragraph.BulletFontSizePt,
            IndentDip = paragraph.IndentDip,
            HangingDip = paragraph.HangingDip
        };
    }

    /// <summary>
    /// Plans wrapped baseline fragments while native renderers provide text metrics.
    /// The full-run measurement used for aligned and RTL placement intentionally
    /// preserves the existing renderer behavior.
    /// </summary>
    public static IReadOnlyList<TextBaselineLinePlan> PlanBaselineLines(
        ResolvedParagraph paragraph,
        double startX,
        double startY,
        double maxWidthDip,
        Func<ResolvedRun, string, bool, TextBaselineFragmentMeasure> measureText)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(measureText);

        var lines = new List<TextBaselineLineBuilder> { new() };

        void NewLine() => lines.Add(new TextBaselineLineBuilder());

        void AddMeasured(int runIndex, ResolvedRun run, string text)
        {
            bool rightToLeft = ResolveRunRightToLeft(paragraph.RightToLeft, text);
            var measure = measureText(run, text, rightToLeft);
            var line = lines[^1];
            if (line.Fragments.Count > 0 && line.WidthDip + measure.WidthDip > maxWidthDip)
            {
                NewLine();
                line = lines[^1];
            }

            line.Fragments.Add(new TextBaselineFragmentBuilder(
                runIndex,
                text,
                measure,
                rightToLeft));
            line.WidthDip += measure.WidthDip;
            line.AscentDip = Math.Max(line.AscentDip, measure.AscentDip);
            line.HeightDip = Math.Max(line.HeightDip, measure.HeightDip);
        }

        for (int runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
        {
            var run = paragraph.Runs[runIndex];
            for (int index = 0; index < run.Text.Length;)
            {
                char first = run.Text[index];
                if (first is '\r' or '\n')
                {
                    if (first == '\r' && index + 1 < run.Text.Length && run.Text[index + 1] == '\n')
                        index++;
                    NewLine();
                    index++;
                    continue;
                }

                bool whitespace = char.IsWhiteSpace(first);
                int end = index + 1;
                while (end < run.Text.Length && run.Text[end] is not '\r' and not '\n' &&
                       char.IsWhiteSpace(run.Text[end]) == whitespace)
                {
                    end++;
                }

                string token = run.Text[index..end];
                var line = lines[^1];
                bool rightToLeft = ResolveRunRightToLeft(paragraph.RightToLeft, token);
                double tokenWidth = measureText(run, token, rightToLeft).WidthDip;
                if (whitespace &&
                    (line.Fragments.Count == 0 || line.WidthDip + tokenWidth > maxWidthDip))
                {
                    index = end;
                    continue;
                }

                if (!whitespace && tokenWidth > maxWidthDip)
                {
                    foreach (char character in token)
                        AddMeasured(runIndex, run, character.ToString());
                }
                else
                {
                    AddMeasured(runIndex, run, token);
                }

                index = end;
            }
        }

        var plans = new List<TextBaselineLinePlan>(lines.Count);
        double lineY = startY;
        foreach (var line in lines)
        {
            double baselineY = lineY + line.AscentDip;
            var fragments = new List<TextBaselineFragmentPlacement>(line.Fragments.Count);
            if (line.Fragments.Count > 0)
            {
                var lineParagraph = new ResolvedParagraph
                {
                    Runs = line.Fragments
                        .Select(fragment => paragraph.Runs[fragment.RunIndex])
                        .ToArray(),
                    Align = paragraph.Align,
                    RightToLeft = paragraph.RightToLeft
                };
                var placements = PlanRunPlacements(
                    lineParagraph,
                    startX,
                    maxWidthDip,
                    (run, rightToLeft) => measureText(run, run.Text, rightToLeft).WidthDip);
                foreach (var placement in placements)
                {
                    var fragment = line.Fragments[placement.RunIndex];
                    fragments.Add(new TextBaselineFragmentPlacement(
                        fragment.RunIndex,
                        fragment.Text,
                        placement.X,
                        baselineY - fragment.Measure.AscentDip - BaselineOffsetToDip(
                            paragraph.Runs[fragment.RunIndex].BaselineOffset,
                            paragraph.Runs[fragment.RunIndex].FontSizePt),
                        fragment.Measure.WidthDip,
                        fragment.Measure.AscentDip,
                        fragment.Measure.HeightDip,
                        fragment.RightToLeft));
                }
            }

            plans.Add(new TextBaselineLinePlan(
                lineY,
                baselineY,
                line.WidthDip,
                line.HeightDip,
                fragments));
            lineY += Math.Max(1, line.HeightDip);
        }

        return plans;
    }

    /// <summary>
    /// Plans one baseline-aligned inline line while native renderers provide
    /// text and math metrics. Run placements remain in visual order.
    /// </summary>
    public static TextInlineBaselineLinePlan PlanInlineBaselineLine(
        ResolvedParagraph paragraph,
        double startX,
        double startY,
        double availableWidthDip,
        Func<int, ResolvedRun, bool, TextInlineRunMeasure> measureRun)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(measureRun);

        var measures = new TextInlineRunMeasure[paragraph.Runs.Count];
        var widths = new double[paragraph.Runs.Count];
        var directions = new bool[paragraph.Runs.Count];
        double lineAscentDip = 0;
        double lineHeightDip = 0;
        double lineWidthDip = 0;
        for (int runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
        {
            var run = paragraph.Runs[runIndex];
            bool rightToLeft = run.RightToLeft
                ?? ResolveRunRightToLeft(paragraph.RightToLeft, run.Text);
            var measure = measureRun(runIndex, run, rightToLeft);
            double widthDip = Math.Max(0, measure.WidthDip);

            measures[runIndex] = measure with { WidthDip = widthDip };
            widths[runIndex] = widthDip;
            directions[runIndex] = rightToLeft;
            lineWidthDip += widthDip;
            lineAscentDip = Math.Max(lineAscentDip, measure.AscentDip);
            lineHeightDip = Math.Max(lineHeightDip, measure.HeightDip);
        }

        double baselineY = startY + lineAscentDip;
        var placements = PlanMeasuredRunPlacements(
            paragraph,
            startX,
            availableWidthDip,
            widths,
            directions);
        var runs = new List<TextInlineRunPlacement>(placements.Count);
        foreach (var placement in placements)
        {
            var measure = measures[placement.RunIndex];
            runs.Add(new TextInlineRunPlacement(
                placement.RunIndex,
                placement.X,
                baselineY - measure.AscentDip,
                measure.WidthDip,
                measure.AscentDip,
                measure.HeightDip,
                placement.RightToLeft));
        }

        return new TextInlineBaselineLinePlan(
            startY,
            baselineY,
            lineWidthDip,
            lineHeightDip,
            runs);
    }

    public static TextParagraphMeasure CreateParagraphMeasure(
        int paragraphIndex,
        double heightDip,
        double spaceBeforePt,
        double spaceAfterPt,
        double lineSpacingScale = 1.0,
        double paragraphLineSpacingScale = 1.0) =>
        new(
            paragraphIndex,
            heightDip * lineSpacingScale * paragraphLineSpacingScale,
            PointsToDip(spaceBeforePt) * lineSpacingScale,
            PointsToDip(spaceAfterPt) * lineSpacingScale);

    /// <summary>
    /// Resolves the authored <c>a:lnSpc</c> line-spacing multiplier for a single paragraph,
    /// relative to its own naturally-measured height. 1.0 (no-op) when the paragraph has no
    /// explicit line spacing (inherits default single spacing).
    /// <paramref name="naturalHeightDip"/> is the paragraph's measured height at single spacing
    /// (e.g. FormattedText.Height) — required to convert an exact-points value into a scale
    /// factor relative to that measurement.
    /// </summary>
    public static double ResolveParagraphLineSpacingScale(ResolvedParagraph paragraph, double naturalHeightDip)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (paragraph.LineSpacingPercent is { } pct && pct > 0)
            return pct / 100.0;
        if (paragraph.LineSpacingPointsExact is { } pts && pts > 0 && naturalHeightDip > 0)
            return PointsToDip(pts) / naturalHeightDip;
        return 1.0;
    }

    public static TextBlockLayoutPlan PlanTableCellText(
        ResolvedTextLayout text,
        LayoutRect bounds,
        TableCellAnchor anchor,
        IReadOnlyList<TextParagraphMeasure> paragraphs)
    {
        var area = GetTextArea(text, bounds);
        double totalHeight = paragraphs.Sum(p => p.TotalHeightDip);
        double currentY = ComputeStartY(area, totalHeight, anchor);

        var placements = new List<TextParagraphPlacement>(paragraphs.Count);
        foreach (var paragraph in paragraphs)
        {
            currentY += paragraph.SpaceBeforeDip;
            placements.Add(new TextParagraphPlacement(
                paragraph.ParagraphIndex,
                0,
                area.X,
                currentY,
                area.Width));
            currentY += paragraph.HeightDip + paragraph.SpaceAfterDip;
        }

        return new TextBlockLayoutPlan(area, placements);
    }

    public static TextBlockLayoutPlan PlanBodyText(
        ResolvedTextLayout text,
        LayoutRect bounds,
        IReadOnlyList<TextParagraphMeasure> paragraphs) =>
        PlanBodyText(text, bounds, paragraphs, default);

    public static TextBlockLayoutPlan PlanBodyText(
        ResolvedTextLayout text,
        LayoutRect bounds,
        IReadOnlyList<TextParagraphMeasure> paragraphs,
        TextAutoFitOverflowPlan autoFitPlan)
    {
        var area = GetTextArea(text, bounds);
        double lineSpacingScale = GetLineSpacingScale(text, autoFitPlan);
        double totalHeight = paragraphs.Sum(p => p.TotalHeightDip * lineSpacingScale);
        double currentY = ComputeStartY(area, totalHeight, text.Anchor);

        var placements = new List<TextParagraphPlacement>(paragraphs.Count);
        foreach (var paragraph in paragraphs)
        {
            if ((uint)paragraph.ParagraphIndex >= (uint)text.Paragraphs.Count)
                continue;

            var resolvedParagraph = text.Paragraphs[paragraph.ParagraphIndex];
            currentY += paragraph.SpaceBeforeDip * lineSpacingScale;
            double paragraphX = area.X + resolvedParagraph.IndentDip;
            placements.Add(new TextParagraphPlacement(
                paragraph.ParagraphIndex,
                0,
                paragraphX,
                currentY,
                Math.Max(1, area.Width - resolvedParagraph.IndentDip),
                PlanBulletPlacement(resolvedParagraph, paragraphX, currentY))
            {
                RenderRoute = PlanParagraphRenderRoute(resolvedParagraph, text)
            });
            currentY += (paragraph.HeightDip + paragraph.SpaceAfterDip) * lineSpacingScale;
        }

        return new TextBlockLayoutPlan(area, placements);
    }

    public static TextMeasuredBlockLayoutPlan<TArtifact> PlanMeasuredBodyText<TArtifact>(
        ResolvedTextLayout text,
        LayoutRect bounds,
        Func<TextParagraphMeasurementRequest, TextNativeMeasurement<TArtifact>> measureParagraph)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureParagraph);

        var initialArea = GetTextArea(text, bounds);
        var (initialMeasures, _) = MeasureParagraphs(
            text,
            initialArea.Width,
            lineSpacingScale: 1.0,
            applyParagraphLineSpacing: true,
            measureParagraph);
        var autoFit = PlanNormalAutoFitOverflow(text, initialArea.Height, initialMeasures);
        var renderText = ApplyAutoFitPlan(text, autoFit);
        var renderArea = GetTextArea(renderText, bounds);
        var (measures, artifacts) = MeasureParagraphs(
            renderText,
            renderArea.Width,
            lineSpacingScale: 1.0,
            applyParagraphLineSpacing: true,
            measureParagraph);

        return new TextMeasuredBlockLayoutPlan<TArtifact>(
            renderText,
            autoFit,
            PlanBodyText(renderText, bounds, measures, autoFit),
            artifacts);
    }

    public static TextColumnLayout GetColumnLayout(ResolvedTextLayout text, LayoutRect bounds) =>
        GetColumnLayout(text, bounds, default);

    public static TextColumnLayout GetColumnLayout(
        ResolvedTextLayout text,
        LayoutRect bounds,
        TextAutoFitOverflowPlan autoFitPlan)
    {
        var area = GetTextArea(text, bounds);
        int columnCount = Math.Max(1, text.ColumnCount);
        double spacingDip = text.ColumnSpacingDip > 0
            ? text.ColumnSpacingDip
            : DefaultColumnSpacingDip;
        double columnWidth = Math.Max(
            1,
            (area.Width - (columnCount - 1) * spacingDip) / columnCount);

        return new TextColumnLayout(
            area,
            columnCount,
            spacingDip,
            columnWidth,
            GetLineSpacingScale(text, autoFitPlan));
    }

    public static double GetAutoFitCapacityHeight(TextColumnLayout layout) =>
        Math.Max(0, layout.Area.Height) * Math.Max(1, layout.ColumnCount);

    public static TextBlockLayoutPlan PlanColumns(
        ResolvedTextLayout text,
        TextColumnLayout layout,
        IReadOnlyList<TextParagraphMeasure> paragraphs)
    {
        int column = 0;
        double currentY = layout.Area.Y;
        double columnX = layout.Area.X;
        double columnBottom = layout.Area.Y + layout.Area.Height;

        var placements = new List<TextParagraphPlacement>(paragraphs.Count);
        foreach (var paragraph in paragraphs)
        {
            if ((uint)paragraph.ParagraphIndex >= (uint)text.Paragraphs.Count)
                continue;

            if (currentY + paragraph.TotalHeightDip > columnBottom &&
                column < layout.ColumnCount - 1)
            {
                column++;
                columnX = layout.Area.X + column * (layout.ColumnWidthDip + layout.ColumnSpacingDip);
                currentY = layout.Area.Y;
            }

            currentY += paragraph.SpaceBeforeDip;
            var resolvedParagraph = text.Paragraphs[paragraph.ParagraphIndex];
            double paragraphX = columnX + resolvedParagraph.IndentDip;
            placements.Add(new TextParagraphPlacement(
                paragraph.ParagraphIndex,
                column,
                paragraphX,
                currentY,
                Math.Max(1, layout.ColumnWidthDip - resolvedParagraph.IndentDip),
                PlanBulletPlacement(resolvedParagraph, paragraphX, currentY))
            {
                RenderRoute = PlanParagraphRenderRoute(resolvedParagraph, text)
            });
            currentY += paragraph.HeightDip + paragraph.SpaceAfterDip;
        }

        return new TextBlockLayoutPlan(layout.Area, placements);
    }

    public static TextMeasuredBlockLayoutPlan<TArtifact> PlanMeasuredColumns<TArtifact>(
        ResolvedTextLayout text,
        LayoutRect bounds,
        Func<TextParagraphMeasurementRequest, TextNativeMeasurement<TArtifact>> measureParagraph)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureParagraph);

        var initialLayout = GetColumnLayout(text, bounds);
        var (initialMeasures, _) = MeasureParagraphs(
            text,
            initialLayout.ColumnWidthDip,
            lineSpacingScale: 1.0,
            applyParagraphLineSpacing: false,
            measureParagraph);
        var autoFit = PlanNormalAutoFitOverflow(
            text,
            GetAutoFitCapacityHeight(initialLayout),
            initialMeasures);
        var renderText = ApplyAutoFitPlan(text, autoFit);
        var renderLayout = GetColumnLayout(renderText, bounds, autoFit);
        var (measures, artifacts) = MeasureParagraphs(
            renderText,
            renderLayout.ColumnWidthDip,
            renderLayout.LineSpacingScale,
            applyParagraphLineSpacing: false,
            measureParagraph);

        return new TextMeasuredBlockLayoutPlan<TArtifact>(
            renderText,
            autoFit,
            PlanColumns(renderText, renderLayout, measures),
            artifacts);
    }

    public static bool CanUseContinuousColumnFlow(ResolvedTextLayout text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.ColumnCount > 1 &&
            text.AutoFitKind == TextAutoFitKind.None &&
            !text.HasStoredFontScale &&
            text.Paragraphs.All(paragraph =>
                paragraph.Runs.Count == 1 &&
                PlanParagraphRenderRoute(paragraph, text) == TextParagraphRenderRoute.Plain);
    }

    public static TextContinuousColumnFlowPlan<TArtifact> PlanMeasuredContinuousColumnFlow<TArtifact>(
        ResolvedTextLayout text,
        LayoutRect bounds,
        Func<TextColumnMeasurementRequest, TextNativeMeasurement<TArtifact>> measure,
        Func<ResolvedParagraph, double>? horizontalScaleResolver = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);

        if (!CanUseContinuousColumnFlow(text))
        {
            return new TextContinuousColumnFlowPlan<TArtifact>(
                false,
                default,
                Array.Empty<TextContinuousColumnLinePlan<TArtifact>>());
        }

        var layout = GetColumnLayout(text, bounds);
        var fragments = new Dictionary<(int ParagraphIndex, int LineIndex),
            (ResolvedParagraph Paragraph, double HorizontalScale)>();
        var lineMeasures = new List<TextColumnLineMeasure>();

        for (int paragraphIndex = 0; paragraphIndex < text.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = text.Paragraphs[paragraphIndex];
            var run = paragraph.Runs[0];
            double horizontalScale = Math.Clamp(
                horizontalScaleResolver?.Invoke(paragraph) ?? 1.0,
                0.01,
                100.0);
            var lines = SplitColumnText(
                run.Text,
                layout.ColumnWidthDip / horizontalScale,
                text.Wrap,
                candidate => measure(new TextColumnMeasurementRequest(
                    TextColumnMeasurementPhase.WrapProbe,
                    paragraphIndex,
                    -1,
                    CloneParagraphWithText(paragraph, run, candidate),
                    0,
                    false,
                    horizontalScale)).WidthDip);

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var fragment = CloneParagraphWithText(paragraph, run, lines[lineIndex]);
                var native = measure(new TextColumnMeasurementRequest(
                    TextColumnMeasurementPhase.LineLayout,
                    paragraphIndex,
                    lineIndex,
                    fragment,
                    layout.ColumnWidthDip,
                    text.Wrap,
                    horizontalScale));
                fragments[(paragraphIndex, lineIndex)] = (fragment, horizontalScale);
                lineMeasures.Add(new TextColumnLineMeasure(
                    paragraphIndex,
                    lineIndex,
                    native.HeightDip,
                    lineIndex == 0 ? PointsToDip(paragraph.SpaceBeforePt) : 0,
                    lineIndex == lines.Count - 1 ? PointsToDip(paragraph.SpaceAfterPt) : 0,
                    lineIndex == 0,
                    lineIndex == lines.Count - 1));
            }
        }

        var linesToRender = new List<TextContinuousColumnLinePlan<TArtifact>>();
        foreach (var placement in PlanColumnLines(text, layout, lineMeasures))
        {
            var (fragment, horizontalScale) =
                fragments[(placement.ParagraphIndex, placement.LineIndex)];
            bool useScaledUnwrappedArtifact = horizontalScale < 1.0;
            var native = measure(new TextColumnMeasurementRequest(
                TextColumnMeasurementPhase.Render,
                placement.ParagraphIndex,
                placement.LineIndex,
                fragment,
                useScaledUnwrappedArtifact ? 0 : placement.MaxWidthDip,
                useScaledUnwrappedArtifact ? false : text.Wrap,
                horizontalScale));
            linesToRender.Add(new TextContinuousColumnLinePlan<TArtifact>(
                fragment,
                placement,
                native.Artifact,
                horizontalScale));
        }

        return new TextContinuousColumnFlowPlan<TArtifact>(true, layout, linesToRender);
    }

    public static IReadOnlyList<TextColumnLinePlacement> PlanColumnLines(
        ResolvedTextLayout text,
        TextColumnLayout layout,
        IReadOnlyList<TextColumnLineMeasure> lines)
    {
        int column = 0;
        double currentY = layout.Area.Y;
        double columnX = layout.Area.X;
        double columnBottom = layout.Area.Y + layout.Area.Height;

        var placements = new List<TextColumnLinePlacement>(lines.Count);
        foreach (var line in lines)
        {
            if ((uint)line.ParagraphIndex >= (uint)text.Paragraphs.Count)
                continue;

            if (currentY + line.TotalHeightDip > columnBottom &&
                column < layout.ColumnCount - 1)
            {
                column++;
                columnX = layout.Area.X + column * (layout.ColumnWidthDip + layout.ColumnSpacingDip);
                currentY = layout.Area.Y;
            }

            currentY += line.SpaceBeforeDip;
            var paragraph = text.Paragraphs[line.ParagraphIndex];
            double paragraphX = columnX + paragraph.IndentDip;
            placements.Add(new TextColumnLinePlacement(
                line.ParagraphIndex,
                line.LineIndex,
                column,
                paragraphX,
                currentY,
                Math.Max(1, layout.ColumnWidthDip - paragraph.IndentDip),
                line.IsFirstLine));
            currentY += line.HeightDip + line.SpaceAfterDip;
        }

        return placements;
    }

    private static (
        List<TextParagraphMeasure> Measures,
        Dictionary<int, TArtifact> Artifacts) MeasureParagraphs<TArtifact>(
        ResolvedTextLayout text,
        double maxWidthDip,
        double lineSpacingScale,
        bool applyParagraphLineSpacing,
        Func<TextParagraphMeasurementRequest, TextNativeMeasurement<TArtifact>> measureParagraph)
    {
        var measures = new List<TextParagraphMeasure>();
        var artifacts = new Dictionary<int, TArtifact>();
        for (int paragraphIndex = 0; paragraphIndex < text.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = text.Paragraphs[paragraphIndex];
            if (paragraph.Runs.Count == 0)
                continue;

            var native = measureParagraph(new TextParagraphMeasurementRequest(
                paragraphIndex,
                text,
                paragraph,
                maxWidthDip,
                text.AutoFitKind == TextAutoFitKind.None));
            artifacts[paragraphIndex] = native.Artifact;
            measures.Add(CreateParagraphMeasure(
                paragraphIndex,
                native.HeightDip,
                paragraph.SpaceBeforePt,
                paragraph.SpaceAfterPt,
                lineSpacingScale,
                applyParagraphLineSpacing
                    ? ResolveParagraphLineSpacingScale(paragraph, native.HeightDip)
                    : 1.0));
        }

        return (measures, artifacts);
    }

    public static TextStackedVerticalLayoutPlan PlanStackedVerticalText(
        ResolvedTextLayout text,
        LayoutRect bounds,
        Func<ResolvedRun, string, TextGlyphMeasure> measureGlyph) =>
        PlanStackedVerticalText(text, bounds, measureGlyph, default);

    public static TextStackedVerticalLayoutPlan PlanStackedVerticalText(
        ResolvedTextLayout text,
        LayoutRect bounds,
        Func<ResolvedRun, string, TextGlyphMeasure> measureGlyph,
        TextAutoFitOverflowPlan autoFitPlan)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measureGlyph);

        var area = GetTextArea(text, bounds);
        var renderMode = GetVerticalRenderMode(text.VerticalType);
        if (renderMode != TextVerticalRenderMode.StackedUpright)
        {
            return new TextStackedVerticalLayoutPlan(
                area,
                text.VerticalType,
                renderMode,
                Array.Empty<TextParagraphMeasure>(),
                Array.Empty<TextStackedGlyphPlacement>());
        }

        double lineSpacingScale = GetLineSpacingScale(text, autoFitPlan);
        var paragraphPlans = new List<StackedParagraphPlan>();
        var paragraphMeasures = new List<TextParagraphMeasure>();

        for (int paragraphIndex = 0; paragraphIndex < text.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = text.Paragraphs[paragraphIndex];
            var glyphs = CreateStackedGlyphMeasures(paragraph, measureGlyph);
            double height = glyphs.Sum(g => g.AdvanceDip);
            paragraphPlans.Add(new StackedParagraphPlan(paragraphIndex, glyphs, height));
            paragraphMeasures.Add(TextLayoutPlanner.CreateParagraphMeasure(
                paragraphIndex,
                height,
                paragraph.SpaceBeforePt,
                paragraph.SpaceAfterPt,
                lineSpacingScale));
        }

        double totalHeight = paragraphMeasures.Sum(p => p.TotalHeightDip);
        double currentY = ComputeStartY(area, totalHeight, text.Anchor);
        var placements = new List<TextStackedGlyphPlacement>();

        for (int i = 0; i < paragraphPlans.Count; i++)
        {
            var paragraphPlan = paragraphPlans[i];
            if ((uint)paragraphPlan.ParagraphIndex >= (uint)text.Paragraphs.Count)
                continue;

            var paragraph = text.Paragraphs[paragraphPlan.ParagraphIndex];
            var paragraphMeasure = paragraphMeasures[i];
            currentY += paragraphMeasure.SpaceBeforeDip;

            double columnLeft = area.X + paragraph.IndentDip;
            double columnWidth = Math.Max(1, area.Width - paragraph.IndentDip);

            foreach (var glyph in paragraphPlan.Glyphs)
            {
                double x = columnLeft + Math.Max(0, (columnWidth - glyph.Measure.WidthDip) * 0.5);
                placements.Add(new TextStackedGlyphPlacement(
                    paragraphPlan.ParagraphIndex,
                    glyph.RunIndex,
                    glyph.Text,
                    x,
                    currentY,
                    glyph.Measure.WidthDip,
                    glyph.Measure.HeightDip));
                currentY += glyph.AdvanceDip * lineSpacingScale;
            }

            currentY += paragraphMeasure.SpaceAfterDip;
        }

        return new TextStackedVerticalLayoutPlan(
            area,
            text.VerticalType,
            renderMode,
            paragraphMeasures,
            placements);
    }

    public static TextTabLayoutPlan PlanTabStops(
        ResolvedParagraph paragraph,
        double startX,
        Func<ResolvedRun, string, double> measureText)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(measureText);

        return PlanTabStops(paragraph, startX, paragraph.TabStops, measureText);
    }

    public static TextTabLayoutPlan PlanTabStops(
        ResolvedParagraph paragraph,
        double startX,
        IReadOnlyList<ResolvedTabStop> tabStops,
        Func<ResolvedRun, string, double> measureText)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(tabStops);
        ArgumentNullException.ThrowIfNull(measureText);

        var tokens = CreateTabTokens(paragraph);
        double currentX = startX;
        var pendingLeader = TabStopLeader.None;
        var placements = new List<TextTabSegmentPlacement>(tokens.Count);

        for (int tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            var run = paragraph.Runs[token.RunIndex];

            if (token.IsTab)
            {
                pendingLeader = FindNextTabStop(tabStops, currentX - startX)?.Leader
                    ?? TabStopLeader.None;
                currentX = AdvanceToTabStop(
                    paragraph,
                    tokens,
                    tokenIndex,
                    currentX,
                    startX,
                    tabStops,
                    measureText);
            }

            if (token.Text.Length == 0)
                continue;

            placements.Add(new TextTabSegmentPlacement(
                token.RunIndex,
                token.Text,
                currentX,
                pendingLeader));
            pendingLeader = TabStopLeader.None;
            currentX += measureText(run, token.Text);
        }

        return new TextTabLayoutPlan(placements);
    }

    /// <summary>
    /// Places runs in visual order while preserving their logical indices.  A right-to-left
    /// paragraph lays out its run boxes from the right edge toward the left; each run also gets
    /// its own strong direction so an embedded Latin run is not painted with RTL glyph order.
    /// WPF and Avalonia use this same geometry and only translate the direction flag to their
    /// native text API.
    /// </summary>
    public static IReadOnlyList<TextRunPlacement> PlanRunPlacements(
        ResolvedParagraph paragraph,
        double startX,
        double availableWidth,
        Func<ResolvedRun, bool, double> measureRun)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(measureRun);

        var runs = paragraph.Runs;
        if (runs.Count == 0)
            return Array.Empty<TextRunPlacement>();

        var widths = new double[runs.Count];
        var directions = new bool[runs.Count];
        for (int i = 0; i < runs.Count; i++)
        {
            directions[i] = runs[i].RightToLeft
                ?? ResolveRunRightToLeft(paragraph.RightToLeft, runs[i].Text);
            widths[i] = Math.Max(0, measureRun(runs[i], directions[i]));
        }

        return PlanMeasuredRunPlacements(
            paragraph,
            startX,
            availableWidth,
            widths,
            directions);
    }

    private static IReadOnlyList<TextRunPlacement> PlanMeasuredRunPlacements(
        ResolvedParagraph paragraph,
        double startX,
        double availableWidth,
        IReadOnlyList<double> widths,
        IReadOnlyList<bool> directions)
    {
        var runs = paragraph.Runs;
        double totalWidth = widths.Sum();

        double alignWidth = availableWidth > 0 ? availableWidth : totalWidth;
        double leadingOffset = paragraph.Align switch
        {
            TextAlign.Center => Math.Max(0, (alignWidth - totalWidth) / 2.0),
            TextAlign.Right => Math.Max(0, alignWidth - totalWidth),
            _ => 0,
        };

        var placements = new List<TextRunPlacement>(runs.Count);
        if (!paragraph.RightToLeft)
        {
            double x = startX + leadingOffset;
            for (int i = 0; i < runs.Count; i++)
            {
                placements.Add(new TextRunPlacement(i, x, widths[i], directions[i]));
                x += widths[i];
            }
        }
        else
        {
            // Logical run 0 is the rightmost visual box in an RTL paragraph.  Build
            // those boxes from the right edge in logical order, then return them sorted
            // by X so native renderers can draw in a stable left-to-right order.
            double x = startX + leadingOffset + totalWidth;
            for (int i = 0; i < runs.Count; i++)
            {
                x -= widths[i];
                placements.Add(new TextRunPlacement(i, x, widths[i], directions[i]));
            }

            placements.Sort(static (left, right) => left.X.CompareTo(right.X));
        }

        return placements;
    }

    /// <summary>Returns the base direction for a run using its first strong character.</summary>
    public static bool ResolveRunRightToLeft(bool paragraphRightToLeft, string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            foreach (char c in text)
            {
                if (IsRtlStrongCharacter(c))
                    return true;
                if (IsLtrStrongCharacter(c))
                    return false;
            }
        }

        return paragraphRightToLeft;
    }

    private readonly record struct TextBaselineFragmentBuilder(
        int RunIndex,
        string Text,
        TextBaselineFragmentMeasure Measure,
        bool RightToLeft);

    private sealed class TextBaselineLineBuilder
    {
        public List<TextBaselineFragmentBuilder> Fragments { get; } = new();
        public double WidthDip { get; set; }
        public double AscentDip { get; set; }
        public double HeightDip { get; set; }
    }

    private static bool IsRtlStrongCharacter(char c) =>
        c is >= '\u0590' and <= '\u08ff'
            or >= '\ufb1d' and <= '\ufdff'
            or >= '\ufe70' and <= '\ufefc';

    private static bool IsLtrStrongCharacter(char c) =>
        !IsRtlStrongCharacter(c) && (char.IsLetter(c) || char.IsNumber(c));

    private static double ComputeStartY(
        TextLayoutArea area,
        double totalHeight,
        TableCellAnchor anchor) =>
        anchor switch
        {
            TableCellAnchor.Middle => area.Y + Math.Max(0, (area.Height - totalHeight) / 2),
            TableCellAnchor.Bottom => area.Y + Math.Max(0, area.Height - totalHeight),
            _ => area.Y
        };

    private static double ComputeStartY(
        TextLayoutArea area,
        double totalHeight,
        VerticalAnchor anchor) =>
        anchor switch
        {
            VerticalAnchor.Middle => area.Y + Math.Max(0, (area.Height - totalHeight) / 2),
            VerticalAnchor.Bottom => area.Y + Math.Max(0, area.Height - totalHeight),
            _ => area.Y
        };

    private static bool HasTextEffects(ResolvedParagraph paragraph) =>
        paragraph.Runs.Any(run =>
            run.TextFill is not null ||
            run.TextOutline is not null ||
            run.TextShadow is not null ||
            run.TextReflection is not null ||
            run.TextGlow is not null ||
            run.TextSoftEdge is not null);

    private static bool HasTabCharacters(ResolvedParagraph paragraph) =>
        paragraph.Runs.Any(run => run.Text.Contains('\t'));

    private static TextBulletPlacement? PlanBulletPlacement(
        ResolvedParagraph paragraph,
        double paragraphX,
        double paragraphY)
    {
        if (paragraph.BulletImage is null && string.IsNullOrEmpty(paragraph.BulletText))
            return null;

        return new TextBulletPlacement(
            paragraph.BulletText,
            paragraph.BulletFontFamily,
            paragraph.BulletFontSizePt,
            paragraph.BulletColor,
            paragraph.BulletImage,
            paragraphX - paragraph.HangingDip,
            paragraphY);
    }

    private static double AdvanceToTabStop(
        ResolvedParagraph paragraph,
        IReadOnlyList<TextTabToken> tokens,
        int tokenIndex,
        double currentX,
        double startX,
        IReadOnlyList<ResolvedTabStop> tabStops,
        Func<ResolvedRun, string, double> measureText)
    {
        double relativeX = currentX - startX;
        var matchedStop = FindNextTabStop(tabStops, relativeX);
        double stopDip = matchedStop?.PositionDip
            ?? Math.Floor(relativeX / DefaultTabStopDip + 1.0) * DefaultTabStopDip;

        double alignOffset = GetTabAlignmentOffset(
            paragraph,
            tokens,
            tokenIndex,
            matchedStop?.Alignment ?? TabStopAlignment.Left,
            measureText);

        return Math.Max(currentX, startX + stopDip + alignOffset);
    }

    private static ResolvedTabStop? FindNextTabStop(
        IReadOnlyList<ResolvedTabStop> tabStops,
        double relativeX)
    {
        foreach (var tabStop in tabStops)
        {
            if (tabStop.PositionDip > relativeX + 0.5)
                return tabStop;
        }

        return null;
    }

    private static double GetTabAlignmentOffset(
        ResolvedParagraph paragraph,
        IReadOnlyList<TextTabToken> tokens,
        int tokenIndex,
        TabStopAlignment alignment,
        Func<ResolvedRun, string, double> measureText)
    {
        if (alignment == TabStopAlignment.Left)
            return 0;

        double segmentWidth = 0;
        double decimalPrefixWidth = 0;
        bool foundDecimal = false;
        for (int scanIndex = tokenIndex; scanIndex < tokens.Count; scanIndex++)
        {
            var token = tokens[scanIndex];
            if (scanIndex > tokenIndex && token.IsTab)
                break;

            if (token.Text.Length == 0)
                continue;

            var tokenRun = paragraph.Runs[token.RunIndex];
            if (!foundDecimal)
            {
                int decimalIndex = token.Text.IndexOf('.');
                if (decimalIndex >= 0)
                {
                    decimalPrefixWidth += measureText(tokenRun, token.Text[..(decimalIndex + 1)]);
                    foundDecimal = true;
                }
                else
                {
                    decimalPrefixWidth += measureText(tokenRun, token.Text);
                }
            }

            segmentWidth += measureText(tokenRun, token.Text);
        }

        if (segmentWidth <= 0)
            return 0;

        return alignment switch
        {
            TabStopAlignment.Right => -segmentWidth,
            TabStopAlignment.Center => -segmentWidth / 2.0,
            TabStopAlignment.Decimal => foundDecimal ? -decimalPrefixWidth : -segmentWidth,
            _ => 0
        };
    }

    private static List<TextTabToken> CreateTabTokens(ResolvedParagraph paragraph)
    {
        var tokens = new List<TextTabToken>();
        for (int runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
        {
            var run = paragraph.Runs[runIndex];
            if (run.Text.Length == 0)
                continue;

            var segments = run.Text.Split('\t');
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                tokens.Add(new TextTabToken(
                    runIndex,
                    segments[segmentIndex],
                    segmentIndex > 0));
            }
        }

        return tokens;
    }

    private readonly record struct TextTabToken(
        int RunIndex,
        string Text,
        bool IsTab);

    private static List<StackedGlyphMeasure> CreateStackedGlyphMeasures(
        ResolvedParagraph paragraph,
        Func<ResolvedRun, string, TextGlyphMeasure> measureGlyph)
    {
        var glyphs = new List<StackedGlyphMeasure>();
        for (int runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
        {
            var run = paragraph.Runs[runIndex];
            if (string.IsNullOrEmpty(run.Text))
                continue;

            var enumerator = StringInfo.GetTextElementEnumerator(run.Text);
            while (enumerator.MoveNext())
            {
                string glyphText = enumerator.GetTextElement();
                if (glyphText is "\r" or "\n")
                    continue;

                var measure = measureGlyph(run, glyphText);
                double advance = Math.Max(measure.HeightDip, PointsToDip(run.FontSizePt));
                glyphs.Add(new StackedGlyphMeasure(
                    runIndex,
                    glyphText,
                    measure,
                    advance));
            }
        }

        return glyphs;
    }

    private sealed record StackedParagraphPlan(
        int ParagraphIndex,
        IReadOnlyList<StackedGlyphMeasure> Glyphs,
        double HeightDip);

    private readonly record struct StackedGlyphMeasure(
        int RunIndex,
        string Text,
        TextGlyphMeasure Measure,
        double AdvanceDip);
}
