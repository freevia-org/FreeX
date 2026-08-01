using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum DrawingObjectVisualKind
{
    Image,
    Shape,
    Chart,
    WordArt,
    SmartArt,
    Group
}

public enum DrawingObjectGeometryKind
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    TextBox,
    Custom
}

public enum DrawingObjectFillKind
{
    None,
    Solid,
    Gradient,
    Pattern
}

public sealed record DrawingObjectGradientStopPlan(int Position, string ColorHex);

public sealed record DrawingObjectFillPlan(
    DrawingObjectFillKind Kind,
    string? ColorHex,
    int GradientAngle,
    IReadOnlyList<DrawingObjectGradientStopPlan> GradientStops,
    string? PatternPreset,
    string? PatternForegroundColorHex,
    string? PatternBackgroundColorHex)
{
    public static DrawingObjectFillPlan None { get; } = new(
        DrawingObjectFillKind.None,
        ColorHex: null,
        GradientAngle: 0,
        GradientStops: [],
        PatternPreset: null,
        PatternForegroundColorHex: null,
        PatternBackgroundColorHex: null);

    public string Summary
    {
        get
        {
            return Kind switch
            {
                DrawingObjectFillKind.Solid => "solid:" + (ColorHex ?? "none"),
                DrawingObjectFillKind.Gradient => "gradient:"
                    + GradientAngle.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ":"
                    + string.Join("/", GradientStops.Select(stop =>
                        stop.Position.ToString(System.Globalization.CultureInfo.InvariantCulture) + "=" + stop.ColorHex)),
                DrawingObjectFillKind.Pattern => "pattern:"
                    + (PatternPreset ?? "none")
                    + ":"
                    + (PatternForegroundColorHex ?? "none")
                    + "/"
                    + (PatternBackgroundColorHex ?? "none"),
                _ => "none"
            };
        }
    }
}

public sealed record DrawingObjectOutlinePlan(
    bool IsVisible,
    string? ColorHex,
    double WidthDip,
    string? DashStyle);

public sealed record DrawingObjectTextRunPlan(
    string Text,
    RunFormatting Formatting,
    int ParagraphIndex,
    int RunIndex);

public sealed record DrawingObjectTextParagraphPlan(
    TextAlignment Alignment,
    IReadOnlyList<DrawingObjectTextRunPlan> Runs);

public sealed record DrawingObjectTextPlan(
    string Text,
    ShapeTextDirection Direction)
{
    public IReadOnlyList<DrawingObjectTextParagraphPlan> Paragraphs { get; init; } = [];

    public bool IsRich => Paragraphs
        .SelectMany(paragraph => paragraph.Runs)
        .Any(run => run.Formatting != RunFormatting.Default);
}

public sealed record DrawingObjectTextGlyphPlan(
    char Character,
    int ParagraphIndex,
    int RunIndex,
    int Offset,
    int LineIndex,
    double X,
    double Y,
    double Width,
    double Height,
    RunFormatting Formatting);

public sealed record DrawingObjectTextCaretStopPlan(
    int ParagraphIndex,
    int RunIndex,
    int Offset,
    int LineIndex,
    double X,
    double Y,
    double Height);

public sealed record DrawingObjectTextLayoutPlan(
    double Width,
    double Height,
    IReadOnlyList<DrawingObjectTextGlyphPlan> Glyphs,
    IReadOnlyList<DrawingObjectTextCaretStopPlan> CaretStops);

/// <summary>
/// Shared line breaking and glyph-position contract for floating shape text. Hosts supply only font
/// measurement; both renderers, caret maps, and selection highlights consume the resulting positions.
/// </summary>
public static class DrawingObjectTextLayoutPlanner
{
    public const double TextInsetDip = 4.0;

    public static DrawingObjectTextPlan BuildTextPlan(Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var paragraphs = shape.TextParagraphs
            .Select((paragraph, paragraphIndex) => new DrawingObjectTextParagraphPlan(
                paragraph.Formatting.Alignment,
                paragraph.Runs
                    .Select((run, runIndex) => new DrawingObjectTextRunPlan(
                        run.Text ?? string.Empty,
                        run.Formatting,
                        paragraphIndex,
                        runIndex))
                    .ToArray()))
            .ToArray();
        var isCompactPlainText = paragraphs is [{ Runs: [{ } run] }]
            && run.Formatting == RunFormatting.Default
            && run.Text == shape.PlainText
            && shape.TextParagraphs[0].Formatting == ParagraphFormatting.Default;
        return new DrawingObjectTextPlan(shape.PlainText, shape.TextDirection)
        {
            Paragraphs = isCompactPlainText ? [] : paragraphs
        };
    }

    public static IReadOnlyList<DrawingObjectTextGlyphPlan> Layout(
        DrawingObjectTextPlan plan,
        double widthDip,
        double heightDip,
        Func<string, RunFormatting, double> measure,
        Func<RunFormatting, double> lineHeight)
        => LayoutPlan(plan, widthDip, heightDip, measure, lineHeight).Glyphs;

    public static DrawingObjectTextLayoutPlan LayoutPlan(
        DrawingObjectTextPlan plan,
        double widthDip,
        double heightDip,
        Func<string, RunFormatting, double> measure,
        Func<RunFormatting, double> lineHeight)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(measure);
        ArgumentNullException.ThrowIfNull(lineHeight);

        if (plan.Paragraphs.Count == 0)
        {
            plan = plan with
            {
                Paragraphs = [new DrawingObjectTextParagraphPlan(
                    TextAlignment.Left,
                    [new DrawingObjectTextRunPlan(plan.Text, RunFormatting.Default, 0, 0)])]
            };
        }

        var contentWidth = Math.Max(1, widthDip - TextInsetDip * 2);
        var lines = new List<TextLayoutLine>();
        var current = new TextLayoutLine(0, lineHeight(RunFormatting.Default), TextAlignment.Left);
        lines.Add(current);
        var lineIndex = 1;

        void StartLine(TextAlignment alignment)
        {
            current = new TextLayoutLine(lineIndex++, lineHeight(RunFormatting.Default), alignment);
            lines.Add(current);
        }

        void FinishLine()
        {
            // The current line remains in the list; StartLine replaces it after a hard break.
        }

        bool IsWordStart(IReadOnlyList<DrawingObjectTextRunPlan> paragraphRuns, int runPosition, int offset)
        {
            if (offset > 0)
                return char.IsWhiteSpace(paragraphRuns[runPosition].Text[offset - 1]);

            for (var previousRun = runPosition - 1; previousRun >= 0; previousRun--)
            {
                var previousText = paragraphRuns[previousRun].Text ?? string.Empty;
                if (previousText.Length == 0)
                    continue;
                return char.IsWhiteSpace(previousText[^1]);
            }

            return true;
        }

        double MeasureWord(
            IReadOnlyList<DrawingObjectTextRunPlan> paragraphRuns,
            int runPosition,
            int offset)
        {
            var wordWidth = 0d;
            for (var wordRun = runPosition; wordRun < paragraphRuns.Count; wordRun++)
            {
                var wordText = paragraphRuns[wordRun].Text ?? string.Empty;
                var wordOffset = wordRun == runPosition ? offset : 0;
                for (; wordOffset < wordText.Length; wordOffset++)
                {
                    var wordCharacter = wordText[wordOffset];
                    if (wordCharacter is '\r' or '\n' || char.IsWhiteSpace(wordCharacter))
                        return wordWidth;
                    wordWidth += Math.Max(1, measure(
                        wordCharacter.ToString(), paragraphRuns[wordRun].Formatting));
                }
            }

            return wordWidth;
        }

        for (var paragraphIndex = 0; paragraphIndex < plan.Paragraphs.Count; paragraphIndex++)
        {
            var paragraph = plan.Paragraphs[paragraphIndex];
            current.Alignment = paragraph.Alignment;
            var hasRun = false;
            for (var runPosition = 0; runPosition < paragraph.Runs.Count; runPosition++)
            {
                var run = paragraph.Runs[runPosition];
                var text = run.Text ?? string.Empty;
                hasRun = true;
                current.CaretStops.Add(new TextLayoutCaret(
                    run.ParagraphIndex, run.RunIndex, 0, current.Width, run.Formatting));
                for (var offset = 0; offset < text.Length; offset++)
                {
                    var character = text[offset];
                    if (character is '\r' or '\n')
                    {
                        var breakLength = character == '\r'
                            && offset + 1 < text.Length
                            && text[offset + 1] == '\n'
                            ? 2
                            : 1;
                        current.CaretStops.Add(new TextLayoutCaret(
                            run.ParagraphIndex, run.RunIndex, offset, current.Width, run.Formatting));
                        FinishLine();
                        StartLine(paragraph.Alignment);
                        current.CaretStops.Add(new TextLayoutCaret(
                            run.ParagraphIndex, run.RunIndex, offset + breakLength, current.Width, run.Formatting));
                        offset += breakLength - 1;
                        continue;
                    }

                    if (!char.IsWhiteSpace(character)
                        && IsWordStart(paragraph.Runs, runPosition, offset))
                    {
                        var wordWidth = MeasureWord(paragraph.Runs, runPosition, offset);

                        if (current.Width > 0
                            && wordWidth <= contentWidth
                            && current.Width + wordWidth > contentWidth)
                        {
                            FinishLine();
                            StartLine(paragraph.Alignment);
                            current.CaretStops.Add(new TextLayoutCaret(
                                run.ParagraphIndex, run.RunIndex, offset, current.Width, run.Formatting));
                        }
                    }

                    var glyphWidth = Math.Max(1, measure(character.ToString(), run.Formatting));
                    if (current.Width > 0 && current.Width + glyphWidth > contentWidth)
                    {
                        FinishLine();
                        StartLine(paragraph.Alignment);
                        current.CaretStops.Add(new TextLayoutCaret(
                            run.ParagraphIndex, run.RunIndex, offset, current.Width, run.Formatting));
                    }

                    var glyphHeight = Math.Max(1, lineHeight(run.Formatting));
                    current.LineHeight = Math.Max(current.LineHeight, glyphHeight);
                    current.Glyphs.Add(new TextLayoutGlyph(
                        character, run.ParagraphIndex, run.RunIndex, offset,
                        current.Width, glyphWidth, glyphHeight, run.Formatting));
                    current.Width += glyphWidth;
                    current.CaretStops.Add(new TextLayoutCaret(
                        run.ParagraphIndex, run.RunIndex, offset + 1, current.Width, run.Formatting));
                }
            }

            if (!hasRun)
                current.CaretStops.Add(new TextLayoutCaret(paragraphIndex, 0, 0, current.Width, RunFormatting.Default));

            // Paragraphs are hard line breaks even when the final run is empty. Keep the final line so
            // its caret stop remains addressable, but do not add a phantom line after the last paragraph.
            if (paragraphIndex < plan.Paragraphs.Count - 1)
            {
                FinishLine();
                StartLine(TextAlignment.Left);
            }
        }

        var glyphs = new List<DrawingObjectTextGlyphPlan>();
        var carets = new List<DrawingObjectTextCaretStopPlan>();
        var y = 0d;
        for (var linePosition = 0; linePosition < lines.Count; linePosition++)
        {
            var line = lines[linePosition];
            System.Diagnostics.Debug.Assert(
                line.Index == linePosition,
                $"Floating shape text line indexes must be monotonic; expected {linePosition}, got {line.Index}.");
            var alignmentOffset = line.Alignment switch
            {
                TextAlignment.Center => Math.Max(0, (contentWidth - line.Width) / 2),
                TextAlignment.Right => Math.Max(0, contentWidth - line.Width),
                _ => 0
            };
            foreach (var glyph in line.Glyphs)
            {
                if (y >= heightDip)
                    continue;
                glyphs.Add(new DrawingObjectTextGlyphPlan(
                    glyph.Character,
                    glyph.ParagraphIndex,
                    glyph.RunIndex,
                    glyph.Offset,
                    line.Index,
                    TextInsetDip + alignmentOffset + glyph.X,
                    TextInsetDip + y,
                    glyph.Width,
                    glyph.Height,
                    glyph.Formatting));
            }
            foreach (var caret in line.CaretStops)
            {
                if (y >= heightDip)
                    continue;
                carets.Add(new DrawingObjectTextCaretStopPlan(
                    caret.ParagraphIndex,
                    caret.RunIndex,
                    caret.Offset,
                    line.Index,
                    TextInsetDip + alignmentOffset + caret.X,
                    TextInsetDip + y,
                    line.LineHeight));
            }
            y += line.LineHeight;
        }

        return new DrawingObjectTextLayoutPlan(widthDip, heightDip, glyphs, carets);
    }

    private sealed class TextLayoutLine(int index, double lineHeight, TextAlignment alignment)
    {
        public int Index { get; } = index;
        public double Width { get; set; }
        public double LineHeight { get; set; } = Math.Max(1, lineHeight);
        public TextAlignment Alignment { get; set; } = alignment;
        public List<TextLayoutGlyph> Glyphs { get; } = [];
        public List<TextLayoutCaret> CaretStops { get; } = [];
    }

    private sealed record TextLayoutGlyph(
        char Character,
        int ParagraphIndex,
        int RunIndex,
        int Offset,
        double X,
        double Width,
        double Height,
        RunFormatting Formatting);

    private sealed record TextLayoutCaret(
        int ParagraphIndex,
        int RunIndex,
        int Offset,
        double X,
        RunFormatting Formatting);
}

public sealed record DrawingObjectEffectsPlan(
    bool HasShadow,
    string ShadowColorHex,
    double ShadowBlurDip,
    double ShadowDistanceDip,
    double ShadowDirectionDegrees,
    double ShadowOpacity,
    bool HasGlow,
    string GlowColorHex,
    double GlowRadiusDip,
    double GlowOpacity,
    bool HasSoftEdge,
    double SoftEdgeRadiusDip,
    bool HasReflection,
    bool HasBevel,
    double ReflectionOpacity = 0.38,
    double ReflectionDistanceDip = 4,
    double ReflectionDirectionDegrees = 90,
    double BevelWidthDip = 3,
    double BevelHeightDip = 3)
{
    public static DrawingObjectEffectsPlan None { get; } = new(
        HasShadow: false,
        ShadowColorHex: "#000000",
        ShadowBlurDip: 0,
        ShadowDistanceDip: 0,
        ShadowDirectionDegrees: 0,
        ShadowOpacity: 0,
        HasGlow: false,
        GlowColorHex: "#4472C4",
        GlowRadiusDip: 0,
        GlowOpacity: 0,
        HasSoftEdge: false,
        SoftEdgeRadiusDip: 0,
        HasReflection: false,
        HasBevel: false);

    public bool HasAny => HasShadow || HasGlow || HasSoftEdge || HasReflection || HasBevel;

    public string Summary
    {
        get
        {
            var parts = new List<string>(capacity: 5);
            if (HasShadow) parts.Add("shadow");
            if (HasGlow) parts.Add("glow");
            if (HasSoftEdge) parts.Add("soft-edge");
            if (HasReflection) parts.Add("reflection");
            if (HasBevel) parts.Add("bevel");
            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }
    }
}

public sealed record DrawingObjectWordArtPlan(
    string Text,
    WordArtStyle Style,
    WordArtWarp Warp,
    double FontSizeDip,
    string FontFamily,
    DrawingObjectFillPlan Fill,
    DrawingObjectOutlinePlan Outline,
    bool Bold,
    string WarpHint)
{
    public string FillColorHex =>
        Fill.ColorHex
        ?? Fill.GradientStops.FirstOrDefault()?.ColorHex
        ?? Fill.PatternForegroundColorHex
        ?? "#1F4E79";

    public string? OutlineColorHex => Outline.IsVisible ? Outline.ColorHex : null;

    public bool HasPatternFill => Fill.Kind == DrawingObjectFillKind.Pattern;

    public string StyleSummary =>
        "style:" + Style
        + ";fill:" + Fill.Summary
        + ";outline:" + (Outline.IsVisible ? Outline.ColorHex ?? "visible" : "none")
        + ";bold:" + (Bold ? "true" : "false")
        + ";warp:" + WarpHint;
}

public sealed record DrawingObjectWordArtGlyphPlacementPlan(
    double CenterXNormalized,
    double CenterYNormalized,
    double RotationRadians);

public sealed record DrawingObjectWordArtPlacementPlan(
    WordArtWarp Warp,
    IReadOnlyList<DrawingObjectWordArtGlyphPlacementPlan> Glyphs);

public sealed record DrawingObjectInlineWordArtPlan(
    DrawingObjectWordArtPlan WordArt,
    DrawingObjectEffectsPlan Effects)
{
    public string Summary => WordArt.StyleSummary + ";effects:" + Effects.Summary;
}

public sealed record DrawingObjectImagePlan(
    ImageFormat Format,
    int ByteLength,
    bool HasCrop,
    bool HasAdjustments,
    bool HasRecolor,
    bool HasEffects,
    bool HasArtisticEffect);

public sealed record DrawingObjectGroupChildVisualPlan(
    int ChildIndex,
    double OffsetXDip,
    double OffsetYDip,
    DrawingObjectVisualPlan Visual);

public sealed record DrawingObjectVisualPlan(
    DrawingObjectVisualKind Kind,
    DocumentFloatRect Rect,
    ImageWrapping Wrapping,
    int ZOrderIndex,
    bool BehindText,
    double RotationAngle,
    bool FlipH,
    bool FlipV,
    DrawingObjectGeometryKind? GeometryKind,
    CustomGeometry? CustomGeometry,
    DrawingObjectFillPlan Fill,
    DrawingObjectOutlinePlan Outline,
    DrawingObjectTextPlan? Text,
    DrawingObjectWordArtPlan? WordArt,
    DrawingObjectEffectsPlan Effects,
    IReadOnlyList<DrawingObjectGroupChildVisualPlan> GroupChildren,
    DrawingObjectImagePlan? Image,
    ChartVisualPlan? Chart,
    SmartArtVisualPlan? SmartArt);

public static class DrawingObjectVisualPlanner
{
    private const double DipPerPoint = 96.0 / 72.0;
    private const double WordArtOutlineWidthDip = DipPerPoint * 0.75;

    public static DrawingObjectVisualPlan BuildVisualPlan(
        Shape shape,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.Shape,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            shape.RotationAngle,
            shape.FlipH,
            shape.FlipV,
            shape.HasCustomGeometry ? DrawingObjectGeometryKind.Custom : ToGeometryKind(shape.Kind),
            shape.HasCustomGeometry ? shape.CustomGeometry : null,
            BuildFillPlan(shape),
            BuildOutlinePlan(shape),
            shape.HasText ? DrawingObjectTextLayoutPlanner.BuildTextPlan(shape) : null,
            WordArt: null,
            Effects: BuildEffectsPlan(shape.Effects),
            GroupChildren: [],
            Image: null,
            Chart: null,
            SmartArt: null);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        WordArt wordArt,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(wordArt);
        ArgumentNullException.ThrowIfNull(snapshot);
        var inlinePlan = BuildInlineWordArtPlan(wordArt);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.WordArt,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            RotationAngle: wordArt.RotationAngle,
            FlipH: wordArt.FlipH,
            FlipV: wordArt.FlipV,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: inlinePlan.WordArt,
            Effects: inlinePlan.Effects,
            GroupChildren: [],
            Image: null,
            Chart: null,
            SmartArt: null);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        InlineImage image,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.Image,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            image.RotationAngle,
            image.FlipH,
            image.FlipV,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: null,
            Effects: DrawingObjectEffectsPlan.None,
            GroupChildren: [],
            Image: BuildImagePlan(image),
            Chart: null,
            SmartArt: null);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        Chart chart,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.Chart,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            RotationAngle: chart.RotationAngle,
            FlipH: chart.FlipH,
            FlipV: chart.FlipV,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: null,
            Effects: DrawingObjectEffectsPlan.None,
            GroupChildren: [],
            Image: null,
            Chart: ChartSmartArtVisualPlanner.BuildChartPlan(chart),
            SmartArt: null);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        SmartArt smartArt,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(smartArt);
        ArgumentNullException.ThrowIfNull(snapshot);

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.SmartArt,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            RotationAngle: smartArt.RotationAngle,
            FlipH: smartArt.FlipH,
            FlipV: smartArt.FlipV,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: null,
            Effects: DrawingObjectEffectsPlan.None,
            GroupChildren: [],
            Image: null,
            Chart: null,
            SmartArt: ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt));
    }

    public static DrawingObjectInlineWordArtPlan BuildInlineWordArtPlan(WordArt wordArt)
    {
        ArgumentNullException.ThrowIfNull(wordArt);

        return new DrawingObjectInlineWordArtPlan(
            BuildWordArtPlan(wordArt),
            BuildWordArtEffectsPlan(wordArt.Style));
    }

    public static DrawingObjectWordArtPlacementPlan BuildWordArtPlacementPlan(
        WordArtWarp warp,
        IReadOnlyList<double> glyphWidths,
        double boundsWidthDip,
        double boundsHeightDip)
    {
        ArgumentNullException.ThrowIfNull(glyphWidths);
        if (glyphWidths.Count == 0 || boundsWidthDip <= 0 || boundsHeightDip <= 0)
            return new DrawingObjectWordArtPlacementPlan(warp, []);

        var totalWidth = glyphWidths.Sum();
        if (totalWidth <= 0 || warp is not (WordArtWarp.ArchUp or WordArtWarp.Wave1))
            return new DrawingObjectWordArtPlacementPlan(warp, []);

        var halfSpan = totalWidth / 2;
        var normalizedHalfSpan = Math.Max(1, halfSpan);
        var normalizedTotalWidth = Math.Max(1, totalWidth);
        var currentX = boundsWidthDip / 2 - halfSpan;
        var archDepth = Math.Min(boundsHeightDip * 0.28, Math.Max(3, totalWidth * 0.12));
        var waveAmplitude = Math.Min(boundsHeightDip * 0.08, Math.Max(2, totalWidth * 0.0275));
        var placements = new List<DrawingObjectWordArtGlyphPlacementPlan>(glyphWidths.Count);

        foreach (var width in glyphWidths)
        {
            var centerX = currentX + width / 2;
            var normalizedX = (centerX - boundsWidthDip / 2) / normalizedHalfSpan;
            double centerY;
            double tangent;
            if (warp == WordArtWarp.ArchUp)
            {
                centerY = boundsHeightDip / 2 - archDepth / 2 + archDepth * normalizedX * normalizedX;
                tangent = 2 * archDepth * normalizedX / normalizedHalfSpan;
            }
            else
            {
                var progress = (centerX - (boundsWidthDip / 2 - halfSpan)) / normalizedTotalWidth;
                var phase = Math.PI * 2 * progress;
                centerY = boundsHeightDip / 2 + waveAmplitude * Math.Sin(phase);
                tangent = waveAmplitude * Math.PI * 2 * Math.Cos(phase) / normalizedTotalWidth;
            }

            placements.Add(new DrawingObjectWordArtGlyphPlacementPlan(
                centerX / boundsWidthDip,
                centerY / boundsHeightDip,
                Math.Atan(tangent)));
            currentX += width;
        }

        return new DrawingObjectWordArtPlacementPlan(warp, placements);
    }

    public static DrawingObjectVisualPlan BuildVisualPlan(
        DrawingGroup group,
        DocumentFloatingObjectSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(snapshot);

        var children = new List<DrawingObjectGroupChildVisualPlan>();
        var childSnapshots = DocumentViewLayoutPlanner.BuildFloatingGroupChildSnapshots(group, snapshot.Rect);
        foreach (var childSnapshot in childSnapshots)
        {
            if (childSnapshot.ChildIndex < 0 || childSnapshot.ChildIndex >= group.Children.Count)
                continue;

            var child = group.Children[childSnapshot.ChildIndex];
            DrawingObjectVisualPlan? childPlan = child switch
            {
                InlineImage image => BuildVisualPlan(image, ChildSnapshot(snapshot, childSnapshot, image)),
                Shape shape => BuildVisualPlan(shape, ChildSnapshot(snapshot, childSnapshot, shape)),
                Chart chart => BuildVisualPlan(chart, ChildSnapshot(snapshot, childSnapshot, chart)),
                WordArt wordArt => BuildVisualPlan(wordArt, ChildSnapshot(snapshot, childSnapshot, wordArt)),
                SmartArt smartArt => BuildVisualPlan(smartArt, ChildSnapshot(snapshot, childSnapshot, smartArt)),
                DrawingGroup nestedGroup => BuildVisualPlan(nestedGroup, ChildSnapshot(snapshot, childSnapshot, nestedGroup)),
                _ => null
            };

            if (childPlan is null)
                continue;

            children.Add(new DrawingObjectGroupChildVisualPlan(
                childSnapshot.ChildIndex,
                childSnapshot.Rect.XDip - snapshot.Rect.XDip,
                childSnapshot.Rect.YDip - snapshot.Rect.YDip,
                childPlan));
        }

        return new DrawingObjectVisualPlan(
            DrawingObjectVisualKind.Group,
            snapshot.Rect,
            snapshot.Wrapping,
            snapshot.ZOrderIndex,
            snapshot.BehindText,
            RotationAngle: group.RotationAngle,
            FlipH: group.FlipH,
            FlipV: group.FlipV,
            GeometryKind: null,
            CustomGeometry: null,
            Fill: DrawingObjectFillPlan.None,
            Outline: new DrawingObjectOutlinePlan(false, null, 0, null),
            Text: null,
            WordArt: null,
            Effects: DrawingObjectEffectsPlan.None,
            GroupChildren: children,
            Image: null,
            Chart: null,
            SmartArt: null);
    }

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        InlineImage image) =>
        new(
            DocumentFloatingObjectKind.Image,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping,
            image.RotationAngle,
            image.FlipH,
            image.FlipV);

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        Shape shape) =>
        new(
            DocumentFloatingObjectKind.Shape,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping,
            shape.RotationAngle,
            shape.FlipH,
            shape.FlipV);

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        Chart _) =>
        new(
            DocumentFloatingObjectKind.Chart,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping);

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        WordArt wordArt) =>
        new(
            DocumentFloatingObjectKind.WordArt,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping,
            wordArt.RotationAngle,
            wordArt.FlipH,
            wordArt.FlipV);

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        SmartArt _) =>
        new(
            DocumentFloatingObjectKind.SmartArt,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping);

    private static DocumentFloatingObjectSnapshot ChildSnapshot(
        DocumentFloatingObjectSnapshot groupSnapshot,
        DocumentFloatingGroupChildSnapshot childSnapshot,
        DrawingGroup group) =>
        new(
            DocumentFloatingObjectKind.Group,
            groupSnapshot.BlockIndex,
            groupSnapshot.RunIndex,
            childSnapshot.Rect,
            groupSnapshot.BehindText,
            groupSnapshot.ZOrderIndex,
            groupSnapshot.Wrapping,
            group.RotationAngle,
            group.FlipH,
            group.FlipV);

    private static DrawingObjectGeometryKind ToGeometryKind(ShapeKind kind) =>
        kind switch
        {
            ShapeKind.Ellipse => DrawingObjectGeometryKind.Ellipse,
            ShapeKind.RoundedRectangle => DrawingObjectGeometryKind.RoundedRectangle,
            ShapeKind.TextBox => DrawingObjectGeometryKind.TextBox,
            _ => DrawingObjectGeometryKind.Rectangle
        };

    private static DrawingObjectFillPlan BuildFillPlan(Shape shape)
    {
        if (shape.ExtendedFill is { } fill)
        {
            return fill.Kind switch
            {
                ShapeFillKind.NoFill => DrawingObjectFillPlan.None,
                ShapeFillKind.Gradient => new DrawingObjectFillPlan(
                    DrawingObjectFillKind.Gradient,
                    ColorHex: null,
                    fill.GradientAngle,
                    fill.GradientStops
                        .Select(stop => new DrawingObjectGradientStopPlan(stop.Position, NormalizeHex(stop.ColorHex, "#000000")))
                        .ToList(),
                    PatternPreset: null,
                    PatternForegroundColorHex: null,
                    PatternBackgroundColorHex: null),
                ShapeFillKind.Pattern => new DrawingObjectFillPlan(
                    DrawingObjectFillKind.Pattern,
                    ColorHex: null,
                    GradientAngle: 0,
                    GradientStops: [],
                    fill.PatternPreset,
                    NormalizeHex(fill.PatternFgColorHex, "#4472C4"),
                    NormalizeHex(fill.PatternBgColorHex, "#FFFFFF")),
                _ => DrawingObjectFillPlan.None
            };
        }

        return string.IsNullOrWhiteSpace(shape.FillColorHex)
            ? DrawingObjectFillPlan.None
            : new DrawingObjectFillPlan(
                DrawingObjectFillKind.Solid,
                NormalizeHex(shape.FillColorHex, "#000000"),
                GradientAngle: 0,
                GradientStops: [],
                PatternPreset: null,
                PatternForegroundColorHex: null,
                PatternBackgroundColorHex: null);
    }

    private static DrawingObjectOutlinePlan BuildOutlinePlan(Shape shape)
    {
        if (string.IsNullOrWhiteSpace(shape.OutlineColorHex))
            return new DrawingObjectOutlinePlan(false, null, 0, shape.OutlineDash);

        return new DrawingObjectOutlinePlan(
            true,
            NormalizeHex(shape.OutlineColorHex, "#808080"),
            Math.Max(0.75, shape.OutlineWidthPt * DipPerPoint),
            shape.OutlineDash);
    }

    private static DrawingObjectEffectsPlan BuildEffectsPlan(ShapeEffectLst? effects)
    {
        if (effects is null)
            return DrawingObjectEffectsPlan.None;

        return new DrawingObjectEffectsPlan(
            effects.HasShadow,
            NormalizeHex(effects.ShadowColorHex, "#000000"),
            EmuToDip(effects.ShadowBlurRad),
            EmuToDip(effects.ShadowDist),
            effects.ShadowDir / 60000.0,
            Math.Clamp(effects.ShadowAlpha / 100000.0, 0, 1),
            effects.HasGlow,
            NormalizeHex(effects.GlowColorHex, "#4472C4"),
            EmuToDip(effects.GlowRad),
            Math.Clamp(effects.GlowAlpha / 100000.0, 0, 1),
            effects.HasSoftEdge,
            EmuToDip(effects.SoftEdgeRad),
            effects.HasReflection,
            effects.HasBevel,
            Math.Clamp(effects.ReflectionStartAlpha / 100000.0, 0, 1),
            EmuToDip(effects.ReflectionDist),
            effects.ReflectionDir / 60000.0,
            EmuToDip(effects.BevelW),
            EmuToDip(effects.BevelH));
    }

    private static DrawingObjectWordArtPlan BuildWordArtPlan(WordArt wordArt)
    {
        var (fill, outline, _) = BuildWordArtStylePlan(wordArt.Style);
        var normalAutoFitScale = wordArt.TextFitMode == WordArtTextFitMode.NormalAutoFit
            && wordArt.NormalAutoFitFontScale is > 0
            ? Math.Clamp(wordArt.NormalAutoFitFontScale.Value, 1000, 100000) / 100000d
            : 1;
        return new DrawingObjectWordArtPlan(
            wordArt.Text,
            wordArt.Style,
            wordArt.Warp,
            Math.Max(1, wordArt.FontSizePt * DipPerPoint * normalAutoFitScale),
            string.IsNullOrWhiteSpace(wordArt.FontFamily) ? "Calibri" : wordArt.FontFamily,
            fill,
            outline,
            wordArt.Bold,
            BuildWordArtWarpHint(wordArt.Warp));
    }

    private static DrawingObjectImagePlan BuildImagePlan(InlineImage image) =>
        new(
            image.Format,
            image.Bytes.Length,
            image.HasCrop,
            image.HasAdjustments,
            image.HasRecolor,
            image.HasEffects,
            image.HasArtisticEffect);

    private static DrawingObjectEffectsPlan BuildWordArtEffectsPlan(WordArtStyle style) =>
        style switch
        {
            WordArtStyle.Shadow => WordArtShadow("#2E2E2E", 50800, 38100, 0.4),
            WordArtStyle.ChromeTwo => WordArtShadow("#1F4E79", 38100, 25400, 0.3),
            WordArtStyle.ShadowOrange => WordArtShadow("#ED7D31", 50800, 38100, 0.5),
            WordArtStyle.GlowBlue or WordArtStyle.GlowGold => new DrawingObjectEffectsPlan(
                HasShadow: false,
                ShadowColorHex: "#000000",
                ShadowBlurDip: 0,
                ShadowDistanceDip: 0,
                ShadowDirectionDegrees: 0,
                ShadowOpacity: 0,
                HasGlow: true,
                GlowColorHex: style == WordArtStyle.GlowGold ? "#C09000" : "#2E75B6",
                GlowRadiusDip: EmuToDip(101600),
                GlowOpacity: 0.6,
                HasSoftEdge: false,
                SoftEdgeRadiusDip: 0,
                HasReflection: false,
                HasBevel: false),
            WordArtStyle.Reflection => DrawingObjectEffectsPlan.None with { HasReflection = true },
            WordArtStyle.Bevel => DrawingObjectEffectsPlan.None with { HasBevel = true },
            _ => DrawingObjectEffectsPlan.None
        };

    private static DrawingObjectEffectsPlan WordArtShadow(
        string colorHex,
        int blurRad,
        int dist,
        double opacity) =>
        new(
            HasShadow: true,
            ShadowColorHex: colorHex,
            ShadowBlurDip: EmuToDip(blurRad),
            ShadowDistanceDip: EmuToDip(dist),
            ShadowDirectionDegrees: 45,
            ShadowOpacity: opacity,
            HasGlow: false,
            GlowColorHex: "#4472C4",
            GlowRadiusDip: 0,
            GlowOpacity: 0,
            HasSoftEdge: false,
            SoftEdgeRadiusDip: 0,
            HasReflection: false,
            HasBevel: false);

    private static (DrawingObjectFillPlan Fill, DrawingObjectOutlinePlan Outline, bool Bold) BuildWordArtStylePlan(WordArtStyle style) =>
        style switch
        {
            WordArtStyle.GradientFill => (
                GradientFill(5400000, (0, "#4472C4"), (100000, "#ED7D31")),
                NoWordArtOutline(),
                true),
            WordArtStyle.GradFillMulti => (
                GradientFill(5400000, (0, "#FF6000"), (50000, "#C00000"), (100000, "#7030A0")),
                NoWordArtOutline(),
                true),
            WordArtStyle.FillGold => (
                GradientFill(5400000, (0, "#C09000"), (100000, "#8B6200")),
                NoWordArtOutline(),
                true),
            WordArtStyle.ChromeOne => (
                GradientFill(5400000, (0, "#C0C0C0"), (35000, "#FFFFFF"), (65000, "#A0A8B0"), (100000, "#E8E8E8")),
                WordArtOutline("#242424", WordArtOutlineWidthDip * 2),
                true),
            WordArtStyle.PatternFill => (
                new DrawingObjectFillPlan(
                    DrawingObjectFillKind.Pattern,
                    ColorHex: null,
                    GradientAngle: 0,
                    GradientStops: [],
                    PatternPreset: "diagCross",
                    PatternForegroundColorHex: "#1F4E79",
                    PatternBackgroundColorHex: "#FFFFFF"),
                WordArtOutline("#1F4E79"),
                true),
            WordArtStyle.Outline => (SolidFill("#1F4E79"), WordArtOutline("#2E2E2E"), true),
            WordArtStyle.ChromeTwo => (SolidFill("#FFFFFF"), WordArtOutline("#1F4E79", DipPerPoint), true),
            WordArtStyle.FillWhite => (SolidFill("#FFFFFF"), WordArtOutline("#242424"), true),
            WordArtStyle.ShadowOrange or WordArtStyle.Bevel => (SolidFill("#ED7D31"), NoWordArtOutline(), true),
            WordArtStyle.GlowBlue or WordArtStyle.GlowGold => (SolidFill("#242424"), NoWordArtOutline(), true),
            WordArtStyle.FillBlue or WordArtStyle.Shadow or WordArtStyle.Reflection => (SolidFill("#1F4E79"), NoWordArtOutline(), true),
            _ => (SolidFill("#1F4E79"), NoWordArtOutline(), true)
        };

    private static DrawingObjectFillPlan SolidFill(string colorHex) =>
        new(
            DrawingObjectFillKind.Solid,
            colorHex,
            GradientAngle: 0,
            GradientStops: [],
            PatternPreset: null,
            PatternForegroundColorHex: null,
            PatternBackgroundColorHex: null);

    private static DrawingObjectFillPlan GradientFill(int angle, params (int Position, string ColorHex)[] stops) =>
        new(
            DrawingObjectFillKind.Gradient,
            ColorHex: null,
            angle,
            stops.Select(stop => new DrawingObjectGradientStopPlan(stop.Position, stop.ColorHex)).ToList(),
            PatternPreset: null,
            PatternForegroundColorHex: null,
            PatternBackgroundColorHex: null);

    private static DrawingObjectOutlinePlan NoWordArtOutline() =>
        new(false, null, 0, null);

    private static DrawingObjectOutlinePlan WordArtOutline(string colorHex, double widthDip = WordArtOutlineWidthDip) =>
        new(true, colorHex, widthDip, null);

    private static string BuildWordArtWarpHint(WordArtWarp warp) =>
        warp switch
        {
            WordArtWarp.None => "none",
            WordArtWarp.ArchUp or WordArtWarp.ArchDown or WordArtWarp.Circle or WordArtWarp.Button => "arc",
            WordArtWarp.Wave1 or WordArtWarp.Wave2 => "wave",
            WordArtWarp.Inflate or WordArtWarp.Deflate or WordArtWarp.InflateBottom => "inflate",
            WordArtWarp.ChevronUp or WordArtWarp.ChevronDown => "chevron",
            WordArtWarp.FadeRight or WordArtWarp.FadeLeft => "fade",
            WordArtWarp.SlantUp or WordArtWarp.SlantDown => "slant",
            _ => "custom"
        };

    private static double EmuToDip(int emu) =>
        emu / 12700.0 * DipPerPoint;

    private static string NormalizeHex(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        var hex = value.Trim();
        if (hex.StartsWith('#'))
            hex = hex[1..];
        if (hex.Length == 8)
            hex = hex[2..];
        if (hex.Length != 6)
            return fallback;

        return int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out _)
            ? "#" + hex.ToUpperInvariant()
            : fallback;
    }
}
