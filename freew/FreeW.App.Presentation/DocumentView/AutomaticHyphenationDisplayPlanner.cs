using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Plans display-only automatic hyphenation opportunities without inserting soft hyphens into the model.
/// Each returned offset is the paragraph-text position before which a renderer may wrap and paint a hyphen.
/// </summary>
public static class AutomaticHyphenationDisplayPlanner
{
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
