using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Converts Word's run character spacing (<c>w:spacing</c>, expanded/condensed) into the extra advance, in
/// device-independent pixels, that the compositor adds after every character. Positive values expand,
/// negative values condense. Pure so the Windows and Avalonia hosts (and the tests) share one rule.
/// </summary>
public static class RunCharacterSpacingPlanner
{
    /// <summary>Spacing smaller than this (in points) is below anything Word can store and paints nothing.</summary>
    public const double MinimumVisiblePt = 0.01;

    /// <summary>Word's own limit for expanded/condensed spacing (1584 pt).</summary>
    public const double MaximumAbsolutePt = 1584;

    /// <summary>
    /// The extra advance in DIPs appended to every character of the run, or 0 when the run is unspaced
    /// (or its stored value is not a finite number).
    /// </summary>
    public static double ResolveAdvanceDip(RunFormatting formatting, double dipPerPoint = 4.0 / 3.0)
    {
        ArgumentNullException.ThrowIfNull(formatting);
        var pt = formatting.CharacterSpacingPt;
        if (!double.IsFinite(pt) || Math.Abs(pt) < MinimumVisiblePt || !double.IsFinite(dipPerPoint) || dipPerPoint <= 0)
            return 0;

        return Math.Clamp(pt, -MaximumAbsolutePt, MaximumAbsolutePt) * dipPerPoint;
    }

    /// <summary>
    /// True when a code point takes part in spacing: Word adds the spacing after each visible character (and
    /// space) but not after zero-width format characters (joiners, soft hyphens) or control characters.
    /// </summary>
    public static bool IsSpacedElement(string textElement)
    {
        if (string.IsNullOrEmpty(textElement))
            return false;

        var category = CharUnicodeInfo.GetUnicodeCategory(textElement, 0);
        return category is not (UnicodeCategory.Format or UnicodeCategory.Control);
    }

    /// <summary>The number of text elements of <paramref name="text"/> that receive the spacing advance.</summary>
    public static int CountSpacedElements(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var count = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            if (IsSpacedElement(elements.GetTextElement()))
                count++;
        }

        return count;
    }

    /// <summary>
    /// True when the text can be painted by a spaced, per-character box: no tab, line break or other control
    /// character that the line layout (tab leaders, soft breaks) has to own.
    /// </summary>
    public static bool CanPaintSpaced(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var ch in text)
        {
            if (ch is '\t' or '\n' or '\r' or '\v' or '\f')
                return false;
        }

        return true;
    }
}
