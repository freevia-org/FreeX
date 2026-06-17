using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// Pure, WPF-free heading-level shift helpers for the outline tools. Given a paragraph's
/// <see cref="Paragraph.StyleId"/>, <see cref="Promote"/> moves it one step toward the top of the
/// outline and <see cref="Demote"/> one step toward the bottom. Heading levels follow the same
/// classification as <see cref="DocumentOutline"/>: <c>Title</c> is the top (level 0) and
/// <c>HeadingN</c> sits at level N.
/// <para>
/// Mapping (documented and covered by tests):
/// <list type="bullet">
/// <item><b>Promote</b> raises a heading one rank toward the top:
/// <c>Heading3 → Heading2 → Heading1 → Title</c>. <c>Title</c> is already the top, so it stays
/// <c>Title</c>. A non-heading / unrecognised / null style has no heading rank to raise, so it is
/// returned unchanged.</item>
/// <item><b>Demote</b> lowers a heading one rank toward the bottom:
/// <c>Title → Heading1 → Heading2 → … → Heading6</c>, capped at <see cref="MaxHeadingLevel"/>
/// (<c>Heading6</c> stays <c>Heading6</c>). A non-heading / unrecognised / null style becomes
/// <c>Heading1</c> (the natural "make this a heading" gesture).</item>
/// </list>
/// </para>
/// The helpers are deterministic and depend only on the style id string, so they are fully unit
/// testable without any document or UI.
/// </summary>
public static class OutlineTools
{
    private const string TitleStyleId = "Title";
    private const string HeadingPrefix = "Heading";

    /// <summary>The deepest heading level <see cref="Demote"/> will produce (a <c>Heading6</c> cap).</summary>
    public const int MaxHeadingLevel = 6;

    /// <summary>
    /// Returns the next-higher heading style id (one rank toward the top of the outline). See the
    /// type remarks for the full mapping. A non-heading / null style is returned unchanged.
    /// </summary>
    public static string? Promote(string? styleId)
    {
        if (!DocumentOutline.TryGetLevel(styleId, out var level))
            return styleId; // not a heading: nothing to promote

        // Title (level 0) is already the top of the outline.
        if (level <= 0)
            return TitleStyleId;

        // Heading1 promotes to Title; deeper headings step up one level.
        return level == 1 ? TitleStyleId : StyleForLevel(level - 1);
    }

    /// <summary>
    /// Returns the next-lower heading style id (one rank toward the bottom of the outline), capped at
    /// <see cref="MaxHeadingLevel"/>. See the type remarks for the full mapping. A non-heading / null
    /// style becomes <c>Heading1</c>.
    /// </summary>
    public static string? Demote(string? styleId)
    {
        if (!DocumentOutline.TryGetLevel(styleId, out var level))
            return StyleForLevel(1); // not a heading: turn it into a top-level heading

        // Title (level 0) demotes to Heading1; deeper headings step down one level, capped.
        var next = Math.Min(level + 1, MaxHeadingLevel);
        return StyleForLevel(next);
    }

    // Build the style id for an outline level: 0 -> Title, N>0 -> "HeadingN".
    private static string StyleForLevel(int level) =>
        level <= 0 ? TitleStyleId : HeadingPrefix + level.ToString(CultureInfo.InvariantCulture);
}
