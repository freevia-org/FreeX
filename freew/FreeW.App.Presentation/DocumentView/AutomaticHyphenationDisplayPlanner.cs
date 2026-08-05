using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Plans display-only automatic hyphenation opportunities without inserting soft hyphens into the model.
/// Each returned offset is the paragraph-text position before which a renderer may wrap and paint a hyphen.
/// </summary>
public static class AutomaticHyphenationDisplayPlanner
{
    public const double DefaultHyphenationZonePt = 18;

    public static IReadOnlyList<int> BuildBreakOffsets(
        string displayText,
        PageSettings page,
        ParagraphFormatting paragraphFormatting)
    {
        ArgumentNullException.ThrowIfNull(displayText);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(paragraphFormatting);

        if (!page.AutoHyphenation
            || paragraphFormatting.SuppressAutoHyphens
            || displayText.Length == 0)
        {
            return [];
        }

        var hyphenated = HyphenateForDisplay(displayText, page.DoNotHyphenateCaps);
        if (hyphenated.IndexOf(Hyphenator.SoftHyphen) < 0)
            return [];

        var offsets = new List<int>();
        var sourceOffset = 0;
        foreach (var character in hyphenated)
        {
            if (character == Hyphenator.SoftHyphen)
            {
                if (sourceOffset < displayText.Length
                    && displayText[sourceOffset] == Hyphenator.SoftHyphen)
                {
                    sourceOffset++;
                }
                else
                {
                    offsets.Add(sourceOffset);
                }
            }
            else
                sourceOffset++;
        }

        return offsets;
    }

    /// <summary>
    /// Decides whether a measured line may consume an automatic break. The zone applies only when
    /// an ordinary word break is available; an overlong first word has no trailing whole-word gap.
    /// </summary>
    public static bool AllowsAutomaticLineBreak(
        PageSettings page,
        int consecutiveHyphenatedLines,
        bool hasOrdinaryWordBreak,
        double ordinaryTrailingWhitespacePt)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!page.AutoHyphenation)
            return false;

        if (page.ConsecutiveHyphenLimit > 0
            && consecutiveHyphenatedLines >= page.ConsecutiveHyphenLimit)
        {
            return false;
        }

        if (!hasOrdinaryWordBreak)
            return true;

        var zonePt = page.HyphenationZonePt > 0
            ? page.HyphenationZonePt
            : DefaultHyphenationZonePt;
        return Math.Max(0, ordinaryTrailingWhitespacePt) > zonePt;
    }

    private static string HyphenateForDisplay(string text, bool doNotHyphenateCaps)
    {
        if (!doNotHyphenateCaps)
            return Hyphenator.HyphenateText(text);

        var builder = new System.Text.StringBuilder(text.Length + 8);
        var start = 0;
        for (var index = 0; index <= text.Length; index++)
        {
            var atEnd = index == text.Length;
            if (!atEnd && !char.IsWhiteSpace(text[index]))
                continue;

            if (index > start)
            {
                var token = text.Substring(start, index - start);
                builder.Append(IsAllCaps(token) ? token : Hyphenator.HyphenateText(token));
            }

            if (!atEnd)
                builder.Append(text[index]);
            start = index + 1;
        }

        return builder.ToString();
    }

    private static bool IsAllCaps(string token)
    {
        var sawLetter = false;
        foreach (var character in token)
        {
            if (!char.IsLetter(character))
                continue;
            sawLetter = true;
            if (!char.IsUpper(character))
                return false;
        }

        return sawLetter;
    }
}
