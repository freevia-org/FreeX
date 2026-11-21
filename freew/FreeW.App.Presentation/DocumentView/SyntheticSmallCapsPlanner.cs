namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Word does not use a font's OpenType small-capital glyphs: <c>w:smallCaps</c> always shows each lower-case
/// letter as its upper-case glyph at a reduced size. This splits a run's text into the segments such a render
/// needs, so a font without small-cap glyphs (Bookman Old Style, Times New Roman, ...) still renders correctly.
/// Display-only: the model text is never altered.
/// </summary>
public static class SyntheticSmallCapsPlanner
{
    /// <summary>Word's size for a synthesized small capital relative to the run's font size.</summary>
    public const double SmallCapScale = 0.8;

    /// <summary>One display segment: its text and whether it is drawn as a reduced-size capital.</summary>
    public readonly record struct Segment(string Text, bool IsSmallCapital);

    /// <summary>
    /// Splits <paramref name="text"/> into alternating runs of unchanged characters and lower-case characters
    /// replaced by their upper-case form (flagged <see cref="Segment.IsSmallCapital"/>). Returns an empty list when
    /// the text has no lower-case letter, so the caller keeps the plain run.
    /// </summary>
    public static IReadOnlyList<Segment> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var segments = new List<Segment>();
        var buffer = new System.Text.StringBuilder();
        var small = false;
        var anySmall = false;

        void Flush()
        {
            if (buffer.Length == 0)
                return;
            segments.Add(new Segment(buffer.ToString(), small));
            buffer.Clear();
        }

        foreach (var rune in text.EnumerateRunes())
        {
            var upper = System.Text.Rune.IsLower(rune)
                ? System.Text.Rune.ToUpperInvariant(rune)
                : rune;
            var isSmall = upper != rune;
            if (isSmall != small)
            {
                Flush();
                small = isSmall;
            }

            anySmall |= isSmall;
            buffer.Append(upper.ToString());
        }

        Flush();
        return anySmall ? segments : [];
    }
}
