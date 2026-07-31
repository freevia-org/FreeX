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
        (FontScale < 1.0 || LineSpacingReduction > 0.0);
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

public sealed record TextTabLayoutPlan(
    IReadOnlyList<TextTabSegmentPlacement> Segments);

public static class TextLayoutPlanner
{
    public const double DipPerPoint = 96.0 / 72.0;
    public const double DefaultColumnSpacingDip = 48.5;
    public const double DefaultTabStopDip = 96.0;
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
            return new TextAutoFitOverflowPlan(TextAutoFitOverflowMode.StoredFontScale, 1.0, 0.0);

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

    public static ResolvedTextLayout ApplyAutoFitPlan(
        ResolvedTextLayout text,
        TextAutoFitOverflowPlan plan)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!plan.AppliesRuntimeShrink)
            return text;

        return new ResolvedTextLayout
        {
            Paragraphs = text.Paragraphs
                .Select(paragraph => ApplyAutoFitPlan(paragraph, plan))
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
            LnSpcReduction = text.LnSpcReduction,
            ColumnCount = text.ColumnCount,
            ColumnSpacingDip = text.ColumnSpacingDip
        };
    }

    public static ResolvedParagraph ApplyAutoFitPlan(
        ResolvedParagraph paragraph,
        TextAutoFitOverflowPlan plan)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (!plan.AppliesRuntimeShrink)
            return paragraph;

        double scale = plan.FontScale;
        return new ResolvedParagraph
        {
            Runs = paragraph.Runs.Select(run => new ResolvedRun
            {
                Text = run.Text,
                FontFamily = run.FontFamily,
                FontSizePt = run.FontSizePt * scale,
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
            SpaceBeforePt = paragraph.SpaceBeforePt * scale,
            SpaceAfterPt = paragraph.SpaceAfterPt * scale,
            TabStops = paragraph.TabStops,
            BulletText = paragraph.BulletText,
            BulletColor = paragraph.BulletColor,
            BulletFontFamily = paragraph.BulletFontFamily,
            BulletFontSizePt = paragraph.BulletFontSizePt * scale,
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

    public static TextParagraphMeasure CreateParagraphMeasure(
        int paragraphIndex,
        double heightDip,
        double spaceBeforePt,
        double spaceAfterPt,
        double lineSpacingScale = 1.0) =>
        new(
            paragraphIndex,
            heightDip * lineSpacingScale,
            PointsToDip(spaceBeforePt) * lineSpacingScale,
            PointsToDip(spaceAfterPt) * lineSpacingScale);

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
                PlanBulletPlacement(resolvedParagraph, paragraphX, currentY)));
            currentY += (paragraph.HeightDip + paragraph.SpaceAfterDip) * lineSpacingScale;
        }

        return new TextBlockLayoutPlan(area, placements);
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
                PlanBulletPlacement(resolvedParagraph, paragraphX, currentY)));
            currentY += paragraph.HeightDip + paragraph.SpaceAfterDip;
        }

        return new TextBlockLayoutPlan(layout.Area, placements);
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
        double totalWidth = 0;
        for (int i = 0; i < runs.Count; i++)
        {
            directions[i] = runs[i].RightToLeft
                ?? ResolveRunRightToLeft(paragraph.RightToLeft, runs[i].Text);
            widths[i] = Math.Max(0, measureRun(runs[i], directions[i]));
            totalWidth += widths[i];
        }

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
