using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Describes one object match used by the slideshow Morph transition.
/// The hosts render the target object into an overlay and interpolate its
/// geometry from the source object's bounds.
/// </summary>
public sealed record SlideShowMorphShapeMatch(
    SlideShape Source,
    SlideShape Target,
    string MatchKey)
{
    /// <summary>
    /// Ordered text correspondences used by byWord/byChar playback. Empty for
    /// byObject and for shapes without a usable text overlap.
    /// </summary>
    public IReadOnlyList<SlideShowMorphTokenMatch> Tokens { get; init; } =
        Array.Empty<SlideShowMorphTokenMatch>();
}

/// <summary>One source/target text token correspondence inside a Morph shape match.</summary>
public sealed record SlideShowMorphTokenMatch(
    int SourceStart,
    int SourceLength,
    int TargetStart,
    int TargetLength,
    string SourceText,
    string TargetText);

/// <summary>Renderer-neutral Morph matching result.</summary>
public sealed record SlideShowMorphPlan(
    string Option,
    IReadOnlyList<SlideShowMorphShapeMatch> Matches,
    int UnmatchedSourceCount,
    int UnmatchedTargetCount)
{
    public bool HasObjectMatches => Matches.Count > 0;
}

public enum SlideShowMorphFallbackReason
{
    None,
    MissingSourceSlide,
    NoObjectMatches,
    NoRenderableGeometry
}

public enum SlideShowMorphOverlayKind
{
    Shape,
    TextBackground,
    TextToken
}

public sealed record SlideShowMorphRect(
    double X,
    double Y,
    double Width,
    double Height)
{
    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public bool IsRenderable => Width >= 0.5 && Height >= 0.5;
}

public sealed record SlideShowMorphOverlayRendererPlan(
    SlideShowMorphOverlayKind Kind,
    uint ShapeId,
    SlideShape RenderShape,
    SlideShowMorphRect SourceBounds,
    SlideShowMorphRect TargetBounds,
    double InitialScaleX,
    double InitialScaleY,
    double InitialTranslateX,
    double InitialTranslateY);

public sealed record SlideShowMorphRendererPlan(
    SlideShowMorphPlan MatchPlan,
    IReadOnlyList<SlideShowMorphOverlayRendererPlan> Overlays,
    SlideShowMorphFallbackReason FallbackReason)
{
    public bool CanRender =>
        FallbackReason == SlideShowMorphFallbackReason.None && Overlays.Count > 0;
}

/// <summary>
/// Builds the object correspondence used by Morph. Stable shape ids and unique
/// authored names are preferred for every option; byWord and byChar can then
/// use unique text overlap when regenerated shape ids leave a text object
/// unmatched. Ambiguous candidates are intentionally left unmatched.
/// </summary>
public static class SlideShowMorphPlanner
{
    public static SlideShowMorphRendererPlan BuildRendererPlan(
        SlideTransition transition,
        Slide? source,
        Slide target,
        double renderWidth,
        double renderHeight,
        double slideWidthDip,
        double slideHeightDip)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(target);

        var matchPlan = Plan(transition, source, target);
        if (source is null)
        {
            return new SlideShowMorphRendererPlan(
                matchPlan,
                Array.Empty<SlideShowMorphOverlayRendererPlan>(),
                SlideShowMorphFallbackReason.MissingSourceSlide);
        }
        if (!matchPlan.HasObjectMatches)
        {
            return new SlideShowMorphRendererPlan(
                matchPlan,
                Array.Empty<SlideShowMorphOverlayRendererPlan>(),
                SlideShowMorphFallbackReason.NoObjectMatches);
        }

        var transform = SlideTransformCore.Compute(
            renderWidth,
            renderHeight,
            slideWidthDip,
            slideHeightDip);
        var overlays = new List<SlideShowMorphOverlayRendererPlan>();
        foreach (var match in matchPlan.Matches)
        {
            if (match.Source.ExtentCxEmu <= 0 || match.Source.ExtentCyEmu <= 0
                || match.Target.ExtentCxEmu <= 0 || match.Target.ExtentCyEmu <= 0)
            {
                continue;
            }

            var sourceBounds = ShapeScreenRect(match.Source, transform);
            var targetBounds = ShapeScreenRect(match.Target, transform);
            var tokenMorph = matchPlan.Option is "byWord" or "byChar"
                && match.Tokens.Count > 0
                && !string.IsNullOrWhiteSpace(match.Source.PlainText)
                && !string.IsNullOrWhiteSpace(match.Target.PlainText);
            if (!tokenMorph)
            {
                AddOverlay(
                    SlideShowMorphOverlayKind.Shape,
                    match.Target,
                    sourceBounds,
                    targetBounds,
                    match.Target.Id);
                continue;
            }

            var background = SlideCloner.CloneShape(match.Target);
            background.TextBody = null;
            AddOverlay(
                SlideShowMorphOverlayKind.TextBackground,
                background,
                sourceBounds,
                targetBounds,
                match.Target.Id);
            foreach (var token in match.Tokens)
            {
                AddOverlay(
                    SlideShowMorphOverlayKind.TextToken,
                    CreateTokenShape(match.Target, token.TargetStart, token.TargetLength),
                    TokenScreenRect(match.Source, token, sourceToken: true, transform),
                    TokenScreenRect(match.Target, token, sourceToken: false, transform),
                    match.Target.Id);
            }
        }

        return new SlideShowMorphRendererPlan(
            matchPlan,
            overlays,
            overlays.Count > 0
                ? SlideShowMorphFallbackReason.None
                : SlideShowMorphFallbackReason.NoRenderableGeometry);

        void AddOverlay(
            SlideShowMorphOverlayKind kind,
            SlideShape renderShape,
            SlideShowMorphRect sourceBounds,
            SlideShowMorphRect targetBounds,
            uint shapeId)
        {
            if (!sourceBounds.IsRenderable || !targetBounds.IsRenderable)
                return;

            overlays.Add(new SlideShowMorphOverlayRendererPlan(
                kind,
                shapeId,
                renderShape,
                sourceBounds,
                targetBounds,
                sourceBounds.Width / targetBounds.Width,
                sourceBounds.Height / targetBounds.Height,
                sourceBounds.CenterX - targetBounds.CenterX,
                sourceBounds.CenterY - targetBounds.CenterY));
        }
    }

    public static SlideShowMorphPlan Plan(
        SlideTransition transition,
        Slide? source,
        Slide target)
    {
        ArgumentNullException.ThrowIfNull(transition);
        ArgumentNullException.ThrowIfNull(target);

        var option = NormalizeOption(transition.MorphOption);
        if (source is null)
        {
            return new SlideShowMorphPlan(
                option,
                Array.Empty<SlideShowMorphShapeMatch>(),
                0,
                target.Shapes.Count);
        }

        var sourceShapes = source.Shapes.ToList();
        var targetShapes = target.Shapes.ToList();
        var matches = new List<SlideShowMorphShapeMatch>();
        var usedSource = new HashSet<SlideShape>();
        var usedTarget = new HashSet<SlideShape>();

        // Stable OOXML ids are the strongest identity signal within a deck.
        foreach (var targetShape in targetShapes.Where(s => s.Id != 0))
        {
            var sourceShape = sourceShapes.FirstOrDefault(s =>
                !usedSource.Contains(s) && s.Id == targetShape.Id);
            if (sourceShape is null) continue;

            AddMatch(sourceShape, targetShape, $"id:{targetShape.Id}");
        }

        // Names allow Morph to work across slides where PowerPoint regenerated
        // shape ids. A duplicate name is intentionally left unmatched.
        foreach (var targetShape in targetShapes.Where(s =>
                     !usedTarget.Contains(s) && !string.IsNullOrWhiteSpace(s.Name)))
        {
            var key = NormalizeName(targetShape.Name);
            var candidates = sourceShapes.Where(s =>
                    !usedSource.Contains(s) &&
                    string.Equals(NormalizeName(s.Name), key, StringComparison.Ordinal))
                .ToList();
            if (candidates.Count != 1) continue;

            AddMatch(candidates[0], targetShape, $"name:{key}");
        }

        if (option is "byWord" or "byChar")
        {
            foreach (var targetShape in targetShapes.Where(s =>
                         !usedTarget.Contains(s) && !string.IsNullOrWhiteSpace(s.PlainText)))
            {
                var candidates = sourceShapes
                    .Where(s => !usedSource.Contains(s) && !string.IsNullOrWhiteSpace(s.PlainText))
                    .Select(sourceShape =>
                        (Shape: sourceShape,
                         Score: TextMatchScore(sourceShape.PlainText, targetShape.PlainText, option)))
                    .Where(candidate => candidate.Score > 0)
                    .OrderByDescending(candidate => candidate.Score)
                    .ToList();

                if (candidates.Count == 0 ||
                    candidates.Count > 1 && candidates[0].Score == candidates[1].Score)
                {
                    continue;
                }

                AddMatch(
                    candidates[0].Shape,
                    targetShape,
                    $"{option}:text:{candidates[0].Score}");
            }
        }

        return new SlideShowMorphPlan(
            option,
            matches,
            sourceShapes.Count - usedSource.Count,
            targetShapes.Count - usedTarget.Count);

        void AddMatch(SlideShape sourceShape, SlideShape targetShape, string key)
        {
            usedSource.Add(sourceShape);
            usedTarget.Add(targetShape);
            matches.Add(new SlideShowMorphShapeMatch(sourceShape, targetShape, key)
            {
                Tokens = option is "byWord" or "byChar"
                    ? BuildTokenMatches(sourceShape.PlainText, targetShape.PlainText, option)
                    : Array.Empty<SlideShowMorphTokenMatch>()
            });
        }
    }

    /// <summary>
    /// Creates a renderable copy containing one target token while preserving
    /// the target shape geometry and formatting context.
    /// </summary>
    public static SlideShape CreateTokenShape(SlideShape shape, int targetStart, int targetLength)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var copy = SlideCloner.CloneShape(shape);
        if (copy.TextBody is null)
            return copy;

        // Token overlays are composited above the separately animated shape
        // background. Remove shape-owned paint so each overlay contributes
        // text only instead of repeatedly repainting the full shape.
        copy.Fill = null;
        copy.Outline = null;
        copy.Effects = null;

        string text = shape.PlainText;
        int start = Math.Clamp(targetStart, 0, text.Length);
        int length = Math.Clamp(targetLength, 0, text.Length - start);
        string token = text.Substring(start, length);
        var paragraph = copy.TextBody.Paragraphs.FirstOrDefault();
        var run = paragraph?.Runs.FirstOrDefault();
        if (paragraph is null || run is null)
        {
            copy.TextBody.Paragraphs.Clear();
            paragraph = new Paragraph();
            run = new Run();
            paragraph.Runs.Add(run);
            copy.TextBody.Paragraphs.Add(paragraph);
        }

        paragraph.Runs.Clear();
        run.Text = token;
        paragraph.Runs.Add(run);
        for (int index = copy.TextBody.Paragraphs.Count - 1; index > 0; index--)
            copy.TextBody.Paragraphs.RemoveAt(index);
        return copy;
    }

    private static SlideShowMorphRect ShapeScreenRect(
        SlideShape shape,
        SlideTransformCore transform)
    {
        var topLeft = transform.SlideToScreen(
            SlideTransformCore.EmuToDip(shape.OffsetXEmu),
            SlideTransformCore.EmuToDip(shape.OffsetYEmu));
        return new SlideShowMorphRect(
            topLeft.X,
            topLeft.Y,
            transform.ScaleDipToScreen(SlideTransformCore.EmuToDip(shape.ExtentCxEmu)),
            transform.ScaleDipToScreen(SlideTransformCore.EmuToDip(shape.ExtentCyEmu)));
    }

    private static SlideShowMorphRect TokenScreenRect(
        SlideShape shape,
        SlideShowMorphTokenMatch token,
        bool sourceToken,
        SlideTransformCore transform)
    {
        var text = shape.PlainText;
        var start = Math.Clamp(
            sourceToken ? token.SourceStart : token.TargetStart,
            0,
            text.Length);
        var length = sourceToken ? token.SourceLength : token.TargetLength;
        var lineStart = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var lineEnd = text.IndexOf('\n', start);
        if (lineEnd < 0)
            lineEnd = text.Length;

        var lineLength = Math.Max(1, lineEnd - lineStart);
        var lineIndex = text[..start].Count(character => character == '\n');
        var lineCount = Math.Max(1, text.Count(character => character == '\n') + 1);
        var shapeBounds = ShapeScreenRect(shape, transform);
        const double horizontalInset = 0.06;
        var textWidth = shapeBounds.Width * (1 - horizontalInset * 2);
        var x = shapeBounds.X + shapeBounds.Width * horizontalInset
            + textWidth * (start - lineStart) / lineLength;
        var y = shapeBounds.Y + shapeBounds.Height * lineIndex / lineCount;
        var width = Math.Max(1, textWidth * Math.Max(1, length) / lineLength);
        var height = Math.Max(1, shapeBounds.Height / lineCount);
        return new SlideShowMorphRect(x, y, width, height);
    }

    private static string NormalizeOption(string? option) =>
        option?.Trim().ToLowerInvariant() switch
        {
            "byword" => "byWord",
            "bychar" => "byChar",
            "byobject" => "byObject",
            _ => "byObject"
        };

    private static string NormalizeName(string name) =>
        string.Join(' ', name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();

    private static int TextMatchScore(string source, string target, string option) =>
        BuildTokenMatches(source, target, option).Count;

    private static IReadOnlyList<SlideShowMorphTokenMatch> BuildTokenMatches(
        string source,
        string target,
        string option)
    {
        var sourceTokens = Tokenize(source, option);
        var targetTokens = Tokenize(target, option);
        if (sourceTokens.Count == 0 || targetTokens.Count == 0)
            return Array.Empty<SlideShowMorphTokenMatch>();

        var pairs = LongestCommonSubsequencePairs(sourceTokens, targetTokens);
        return pairs
            .Select(pair => new SlideShowMorphTokenMatch(
                pair.Source.Start,
                pair.Source.Length,
                pair.Target.Start,
                pair.Target.Length,
                pair.Source.Value,
                pair.Target.Value))
            .ToArray();
    }

    private sealed record TextToken(string Value, int Start, int Length, string Key);

    private static IReadOnlyList<TextToken> Tokenize(string text, string option) =>
        option == "byWord"
            ? TokenizeWords(text)
            : text.Select((character, index) => (character, index))
                .Where(item => !char.IsWhiteSpace(item.character))
                .Select(item => new TextToken(
                    item.character.ToString(),
                    item.index,
                    1,
                    item.character.ToString().ToUpperInvariant()))
                .ToArray();

    private static IReadOnlyList<TextToken> TokenizeWords(string text)
    {
        var tokens = new List<TextToken>();
        int index = 0;
        while (index < text.Length)
        {
            while (index < text.Length && !char.IsLetterOrDigit(text[index]))
                index++;
            int start = index;
            while (index < text.Length && char.IsLetterOrDigit(text[index]))
                index++;
            if (index > start)
            {
                string value = text[start..index];
                tokens.Add(new TextToken(
                    value,
                    start,
                    index - start,
                    value.ToUpperInvariant()));
            }
        }

        return tokens;
    }

    private static IReadOnlyList<(TextToken Source, TextToken Target)> LongestCommonSubsequencePairs(
        IReadOnlyList<TextToken> source,
        IReadOnlyList<TextToken> target)
    {
        var lengths = new int[source.Count + 1, target.Count + 1];
        for (int sourceIndex = source.Count - 1; sourceIndex >= 0; sourceIndex--)
        {
            for (int targetIndex = target.Count - 1; targetIndex >= 0; targetIndex--)
            {
                lengths[sourceIndex, targetIndex] = string.Equals(
                    source[sourceIndex].Key,
                    target[targetIndex].Key,
                    StringComparison.OrdinalIgnoreCase)
                    ? lengths[sourceIndex + 1, targetIndex + 1] + 1
                    : Math.Max(lengths[sourceIndex + 1, targetIndex], lengths[sourceIndex, targetIndex + 1]);
            }
        }

        var pairs = new List<(TextToken Source, TextToken Target)>();
        int sourceCursor = 0;
        int targetCursor = 0;
        while (sourceCursor < source.Count && targetCursor < target.Count)
        {
            if (string.Equals(source[sourceCursor].Key, target[targetCursor].Key, StringComparison.OrdinalIgnoreCase))
            {
                pairs.Add((source[sourceCursor], target[targetCursor]));
                sourceCursor++;
                targetCursor++;
            }
            else if (lengths[sourceCursor + 1, targetCursor] >= lengths[sourceCursor, targetCursor + 1])
                sourceCursor++;
            else
                targetCursor++;
        }

        return pairs;
    }
}
