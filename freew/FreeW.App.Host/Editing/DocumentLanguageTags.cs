using System;
using System.Windows.Markup;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Resolves language tags read from document data (<c>w:lang</c>) to WPF <see cref="XmlLanguage"/>
/// values. Document tags are untrusted: Word writes legacy Windows locale names such as
/// <c>es-ES_tradnl</c> (underscore sort suffix) that are not valid RFC 3066 xml:lang values and make
/// <see cref="XmlLanguage.GetLanguage"/> throw <see cref="ArgumentException"/>. Rather than dropping the
/// language entirely, the tag is normalized to the closest valid form so proofing still picks the right
/// dictionary. Never throws; returns <c>null</c> when nothing usable can be derived.
/// </summary>
internal static class DocumentLanguageTags
{
    /// <summary>
    /// Normalizes a document language tag to a syntactically valid xml:lang value, or <c>null</c> when
    /// the tag is empty or cannot be repaired. Pure string logic (no WPF dependency).
    /// </summary>
    internal static string? Normalize(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var trimmed = tag.Trim();
        // Legacy Windows alternate-sort names: "es-ES_tradnl", "de-DE_phoneb", "zh-CN_stroke", ... The
        // suffix after '_' is a sort order, not a language/region subtag -- drop it.
        var underscore = trimmed.IndexOf('_');
        if (underscore >= 0)
        {
            var head = trimmed[..underscore];
            // "en_US"-style separators (no hyphen before the underscore) are a region, not a sort suffix.
            trimmed = head.Contains('-', StringComparison.Ordinal)
                ? head
                : trimmed.Replace('_', '-');
        }

        return IsValidXmlLang(trimmed) ? trimmed : PrimarySubtag(trimmed);
    }

    /// <summary>Resolves <paramref name="tag"/> to an <see cref="XmlLanguage"/>, or <c>null</c>.</summary>
    internal static XmlLanguage? TryGetXmlLanguage(string? tag)
    {
        if (Normalize(tag) is not { } normalized)
            return null;
        try
        {
            return XmlLanguage.GetLanguage(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? PrimarySubtag(string tag)
    {
        var hyphen = tag.IndexOf('-');
        var primary = hyphen >= 0 ? tag[..hyphen] : tag;
        return IsValidXmlLang(primary) ? primary : null;
    }

    // RFC 3066 shape XmlLanguage enforces: hyphen-separated subtags of 1-8 ASCII alphanumerics, the
    // first purely alphabetic.
    private static bool IsValidXmlLang(string tag)
    {
        if (tag.Length == 0)
            return false;
        var first = true;
        foreach (var subtag in tag.Split('-'))
        {
            if (subtag.Length is 0 or > 8)
                return false;
            foreach (var ch in subtag)
            {
                var ok = first ? char.IsAsciiLetter(ch) : char.IsAsciiLetterOrDigit(ch);
                if (!ok)
                    return false;
            }
            first = false;
        }
        return true;
    }
}
