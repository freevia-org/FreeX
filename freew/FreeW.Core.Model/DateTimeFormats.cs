using System.Globalization;

namespace FreeW.Core.Model;

/// <summary>
/// A single named date/time format offered by the Insert &gt; Date &amp; Time picker: a human-readable
/// <see cref="Label"/> and the <see cref="Text"/> that gets inserted at the caret. Pure data.
/// </summary>
public readonly record struct DateTimeFormat(string Label, string Text);

/// <summary>
/// Pure, WPF-free formatting of a <see cref="DateTime"/> into the strings offered by the Insert &gt;
/// Date &amp; Time dialog. Takes the moment as a parameter (the UI passes <c>DateTime.Now</c>) so the
/// formatting is deterministic and unit-testable. Lives in the model project for that reason.
/// </summary>
public static class DateTimeFormats
{
    /// <summary>
    /// Build the list of formatted date/time options for <paramref name="moment"/>, using
    /// <paramref name="culture"/> (defaults to the current culture) for the standard format strings.
    /// Order is short date, long date, short time, long time, and a combined date + time.
    /// </summary>
    public static IReadOnlyList<DateTimeFormat> Build(DateTime moment, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return
        [
            new DateTimeFormat("Short date", moment.ToString("d", culture)),
            new DateTimeFormat("Long date", moment.ToString("D", culture)),
            new DateTimeFormat("Short time", moment.ToString("t", culture)),
            new DateTimeFormat("Long time", moment.ToString("T", culture)),
            new DateTimeFormat("Date and time", moment.ToString("f", culture)),
        ];
    }

    /// <summary>
    /// Build the Word <c>\@</c> picture string for the DATE/TIME field that corresponds to the
    /// menu item at <paramref name="selectedIndex"/> (0=short date, 1=long date, 2=short time,
    /// 3=long time, 4=date+time), using <paramref name="culture"/>'s
    /// <see cref="DateTimeFormatInfo"/> patterns so the field re-renders in the same format that
    /// was displayed to the user — not in a hardcoded US-English format.
    ///
    /// <para>
    /// Word's <c>\@</c> picture syntax is a superset of .NET custom date/time format specifiers
    /// for the common tokens (d, dd, ddd, dddd, M, MM, MMM, MMMM, yy, yyyy, h, hh, H, HH, m,
    /// mm, s, ss). The only divergence handled here is the AM/PM designator: .NET uses <c>tt</c>
    /// (or <c>t</c>); Word uses <c>am/pm</c> (lowercase) or <c>AM/PM</c> (uppercase).
    /// </para>
    /// </summary>
    public static string BuildFieldPicture(int selectedIndex, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var dtf = culture.DateTimeFormat;

        var pattern = selectedIndex switch
        {
            0 => dtf.ShortDatePattern,
            1 => dtf.LongDatePattern,
            2 => dtf.ShortTimePattern,
            3 => dtf.LongTimePattern,
            // "f" = long date + " " + short time (matches DateTime.ToString("f", culture))
            _ => dtf.LongDatePattern + " " + dtf.ShortTimePattern,
        };

        return NetPatternToWordPicture(pattern);
    }

    /// <summary>
    /// Converts a .NET custom date/time format pattern to a Word field <c>\@</c> picture string.
    /// The two representations share all core tokens; the only substitution required is the
    /// AM/PM designator: .NET <c>tt</c> → Word <c>am/pm</c>; .NET <c>t</c> → Word <c>am/pm</c>.
    /// Quoted literal segments (single-quoted in .NET, e.g. <c>'de'</c>) are rewritten to
    /// double-quoted form for Word (<c>"de"</c>). The percent escape (<c>%d</c> etc. for
    /// single-character custom specifiers) is stripped as Word does not use it.
    /// </summary>
    public static string NetPatternToWordPicture(string pattern)
    {
        // Walk character-by-character to handle quoted literals and avoid naive string.Replace
        // collisions (e.g. "tt" must become "am/pm" not two "am/pm am/pm" pieces from "t"→"am/pm").
        var sb = new System.Text.StringBuilder(pattern.Length + 8);
        int i = 0;
        while (i < pattern.Length)
        {
            char c = pattern[i];

            // Single-quoted literal segment in .NET → double-quoted literal in Word.
            // The common case in culture patterns is a separator like '.' or ':' or ', '.
            // We also handle the '' escape (two consecutive single-quotes = literal apostrophe).
            if (c == '\'')
            {
                sb.Append('"');
                i++;
                while (i < pattern.Length)
                {
                    if (pattern[i] == '\'')
                    {
                        // '' inside a literal = escaped single-quote → emit one apostrophe.
                        if (i + 1 < pattern.Length && pattern[i + 1] == '\'')
                        {
                            sb.Append('\'');
                            i += 2;
                        }
                        else
                        {
                            // Closing single-quote — end of literal.
                            i++;
                            break;
                        }
                    }
                    else
                    {
                        sb.Append(pattern[i]);
                        i++;
                    }
                }
                sb.Append('"');
                continue;
            }

            // Backslash-escaped single character in .NET → emit the character literally.
            if (c == '\\' && i + 1 < pattern.Length)
            {
                sb.Append(pattern[i + 1]);
                i += 2;
                continue;
            }

            // Percent prefix for single-char custom specifier in .NET (e.g. %d) — drop the %.
            if (c == '%' && i + 1 < pattern.Length)
            {
                i++; // skip %, let next iteration emit the specifier
                continue;
            }

            // AM/PM designator: .NET "tt" → Word "am/pm"; .NET "t" → Word "am/pm".
            // Check "tt" before "t" so we consume both characters when both are present.
            if (c == 't')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == 't')
                {
                    sb.Append("am/pm");
                    i += 2;
                }
                else
                {
                    sb.Append("am/pm");
                    i++;
                }
                continue;
            }

            // All other characters (d, M, y, h, H, m, s, spaces, separators) pass through
            // unchanged — they are identical in .NET and Word picture syntax.
            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }
}
