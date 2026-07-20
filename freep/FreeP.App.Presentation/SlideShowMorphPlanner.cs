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
/// Builds the object correspondence used by Morph. PowerPoint's byObject
/// behavior is the reliable common denominator for the two slideshow hosts:
/// prefer stable shape ids, then authored names, and never match a duplicate
/// key ambiguously.
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
}
