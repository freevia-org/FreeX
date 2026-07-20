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
    string MatchKey);

/// <summary>Renderer-neutral Morph matching result.</summary>
public sealed record SlideShowMorphPlan(
    string Option,
    IReadOnlyList<SlideShowMorphShapeMatch> Matches,
    int UnmatchedSourceCount,
    int UnmatchedTargetCount)
{
    public bool HasObjectMatches => Matches.Count > 0;
}

/// <summary>
/// Builds the object correspondence used by Morph. Stable shape ids and unique
/// authored names are preferred for every option; byWord and byChar can then
/// use unique text overlap when regenerated shape ids leave a text object
/// unmatched. Ambiguous candidates are intentionally left unmatched.
/// </summary>
public static class SlideShowMorphPlanner
{
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
            matches.Add(new SlideShowMorphShapeMatch(sourceShape, targetShape, key));
        }
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
        option == "byWord"
            ? LongestCommonSubsequence(
                TokenizeWords(source),
                TokenizeWords(target),
                StringComparer.OrdinalIgnoreCase)
            : LongestCommonSubsequence(
                TokenizeCharacters(source),
                TokenizeCharacters(target),
                EqualityComparer<string>.Default);

    private static IReadOnlyList<string> TokenizeWords(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0)
            .ToArray();

    private static IReadOnlyList<string> TokenizeCharacters(string text) =>
        text.Where(character => !char.IsWhiteSpace(character))
            .Select(character => character.ToString().ToUpperInvariant())
            .ToArray();

    private static int LongestCommonSubsequence(
        IReadOnlyList<string> source,
        IReadOnlyList<string> target,
        IEqualityComparer<string> comparer)
    {
        if (source.Count == 0 || target.Count == 0)
            return 0;

        var previous = new int[target.Count + 1];
        var current = new int[target.Count + 1];
        for (int sourceIndex = 1; sourceIndex <= source.Count; sourceIndex++)
        {
            Array.Clear(current);
            for (int targetIndex = 1; targetIndex <= target.Count; targetIndex++)
            {
                current[targetIndex] = comparer.Equals(source[sourceIndex - 1], target[targetIndex - 1])
                    ? previous[targetIndex - 1] + 1
                    : Math.Max(previous[targetIndex], current[targetIndex - 1]);
            }

            (previous, current) = (current, previous);
        }

        return previous[target.Count];
    }
}
