namespace FreeX.Core.IO;

/// <summary>
/// Shared "does this literal look like a date" shape heuristic, factored out so every FreeX entry
/// point that auto-recognizes an unquoted date literal implements Excel's rule exactly once instead
/// of via near-identical, drift-prone copies. Before R112 this logic was duplicated three times --
/// CSV/TXT import (DelimitedTextWorkbookReader), typed cell entry
/// (FreeX.App.Services.CellEntryParser), and the Text-to-Columns "General" column conversion
/// (FreeX.App.Presentation.TextToColumnsValueConverter) -- and R111 fixing only the first copy left
/// the other two carrying the same bug: a bare, year-less "M/d" or "M-d" token (e.g. "3/4", "1-2")
/// was rejected as a date candidate because the old heuristic required either a letter or 3+ digit
/// groups. Real Excel's General-format auto-recognition treats a two-digit-group slash/dash token
/// as a date candidate too, filling in the current year (the same underlying mechanism as the
/// well-known "gene names turn into dates" class of bugs).
/// </summary>
/// <remarks>
/// This method only decides whether a genuine <see cref="System.DateTime.TryParse(System.ReadOnlySpan{char},System.IFormatProvider?,System.Globalization.DateTimeStyles,out System.DateTime)"/>
/// attempt against the current culture is even worth making -- callers must still perform (and can
/// still reject) that attempt themselves, so a shape match here is necessary but not sufficient for
/// a value to actually become a date.
/// </remarks>
public static class DateEntryShapeRecognizer
{
    /// <param name="field">The trimmed candidate literal.</param>
    /// <param name="dotCountsAsDateSeparator">
    /// Whether '.' should be treated as a date separator for the 3-or-more-digit-group branch (a
    /// genuine dotted date like "31.12.2024" vs. a dotted grouped integer like "1.234.567" is
    /// disambiguated by the caller's own DateTime.TryParse attempt, not here). Callers should pass
    /// <see langword="true"/> when '.' is unconditionally accepted (matching
    /// DelimitedTextWorkbookReader's CSV-import rule) or when '.' happens to be the current
    /// culture's own actual date separator (e.g. de-DE, it-IT); pass <see langword="false"/>
    /// otherwise (e.g. under en-US, where '.' is far more likely to be a decimal point, as in
    /// typed cell entry and Text-to-Columns).
    /// </param>
    /// <param name="colonAlwaysQualifies">
    /// Whether any ':' in the field should immediately qualify it as a date/time candidate,
    /// regardless of digit-group count or date separators. Typed cell entry needs this
    /// (a bare time-of-day literal like "15:30" has no date separator at all and must still reach
    /// the DateTime.TryParse attempt so it can be recognized as a time-of-day serial); CSV import
    /// and Text-to-Columns instead route a standalone time through their own, separate time-parsing
    /// step and must pass <see langword="false"/> here so a colon-only, non-date-separated,
    /// two-digit-group literal like "9:30" is correctly rejected as a date candidate.
    /// </param>
    public static bool LooksLikeDateCandidate(
        ReadOnlySpan<char> field,
        bool dotCountsAsDateSeparator,
        bool colonAlwaysQualifies)
    {
        var digitGroups = 0;
        var inDigitGroup = false;
        var hasDateSeparator = false;
        var hasSlashOrDashSeparator = false;
        var hasLetter = false;
        var hasColon = false;

        foreach (var c in field)
        {
            if (char.IsDigit(c))
            {
                if (!inDigitGroup)
                {
                    digitGroups++;
                    inDigitGroup = true;
                }

                continue;
            }

            inDigitGroup = false;
            hasDateSeparator |= c is '/' or '-' || (dotCountsAsDateSeparator && c == '.');
            hasSlashOrDashSeparator |= c is '/' or '-';
            hasLetter |= char.IsLetter(c);
            hasColon |= c == ':';
        }

        if (digitGroups < 2)
        {
            return false;
        }

        if (hasColon && colonAlwaysQualifies)
        {
            return true;
        }

        if (hasColon && !hasDateSeparator && digitGroups <= 2)
        {
            return false;
        }

        if (hasLetter || digitGroups >= 3)
        {
            return hasLetter || hasDateSeparator;
        }

        // Exactly two digit groups, no letters: the year-less "M/d" (or "d/M") shape -- e.g. "3/4"
        // or "1-2" -- that real Excel's General-format auto-recognition converts to a date using
        // the current year (the caller's own DateTime.TryParse fills in the current year by
        // default when the year is omitted). Only "/" and "-" count as the triggering separator
        // here, deliberately excluding "." even when dotCountsAsDateSeparator is set for the 3+
        // group branch above: in the common cultures where "." is the decimal separator, a plain
        // two-digit-group decimal like "3.14" or "1.5" would otherwise be misparsed as a date
        // instead of a number.
        return hasSlashOrDashSeparator;
    }
}
