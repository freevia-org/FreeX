using System.Linq;
using FreeW.App.Presentation.Editing;
using SkiaSharp;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// The Avalonia outline view shows the same toolbar glyphs and row markers as WPF (both come from
/// <see cref="OutlineViewPlanner"/>). Avalonia has no embedded face for the geometric-shape glyphs, so they only
/// render when the platform font fallback (Skia <see cref="SKFontManager.MatchCharacter(int)"/>, which is what
/// Avalonia's Skia font manager calls) resolves a face that really contains each code point. The headless
/// drawing backend used by the other tests substitutes a stub font manager and cannot detect tofu, so this goes
/// straight to Skia and fails on any platform whose fallback chain leaves a glyph unresolved.
/// </summary>
public sealed class OutlineViewGlyphCoverageTests
{
    private static string[] SharedGlyphs() =>
        Enum.GetValues<OutlineCommand>()
            .Select(OutlineViewPlanner.CommandGlyph)
            .Concat([OutlineViewPlanner.RowMarkers.ExpandedHeading, OutlineViewPlanner.RowMarkers.CollapsedHeading])
            .Select(glyph => glyph.Trim())
            .Distinct()
            .ToArray();

    [Fact]
    public void Planner_glyphs_resolve_to_a_real_face_through_the_platform_fallback_chain()
    {
        foreach (var glyph in SharedGlyphs())
        {
            var codePoint = char.ConvertToUtf32(glyph, 0);
            using var matched = SKFontManager.Default.MatchCharacter(codePoint);
            matched.Should().NotBeNull($"U+{codePoint:X4} ({glyph}) needs a platform fallback face or it renders as tofu");
            matched!.ContainsGlyph(codePoint).Should().BeTrue(
                $"fallback face '{matched.FamilyName}' must contain U+{codePoint:X4} ({glyph})");
        }
    }
}
