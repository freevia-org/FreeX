using System.Globalization;

namespace FreeX.Core.Formula;

/// <summary>
/// r603: applies Excel's documented two-digit-year window (30-99 -> 19xx, 00-29 -> 20xx) to a
/// culture clone, and does nothing when the culture's calendar cannot express it.
///
/// <para>2029 is a GREGORIAN year, but <see cref="Calendar.TwoDigitYearMax"/> validates its input
/// against the calendar's own era. On an ar-SA machine the current culture's calendar is UmAlQura,
/// whose legal range is 1318-1500, so the plain assignment threw
/// <see cref="ArgumentOutOfRangeException"/> -- uncaught on the CSV read path, on typed date entry,
/// and inside DATEVALUE. Five sites carried the same bare assignment; this is the single place that
/// knows the rule does not apply to every era.</para>
///
/// <para>Where it does not apply, the calendar keeps its own window rather than being forced into a
/// wrong one: Excel's rule is a statement about Gregorian years and has no meaning in another era.</para>
/// </summary>
public static class ExcelTwoDigitYearWindow
{
    /// <summary>Excel's two-digit-year cutoff, as a Gregorian year.</summary>
    public const int Max = 2029;

    /// <summary>
    /// Sets <see cref="Calendar.TwoDigitYearMax"/> on <paramref name="clonedCulture"/> when its
    /// calendar can express <see cref="Max"/>. The culture must already be a clone -- the calendar
    /// of a shared <see cref="CultureInfo"/> is read-only.
    /// </summary>
    public static CultureInfo ApplyTo(CultureInfo clonedCulture)
    {
        var calendar = clonedCulture.DateTimeFormat.Calendar;
        if (SupportsYear(calendar, Max))
            calendar.TwoDigitYearMax = Max;

        return clonedCulture;
    }

    /// <summary>
    /// Whether the calendar can express <paramref name="year"/>, derived from its own supported
    /// range rather than from a list of calendar types, so a calendar added later needs no edit here.
    /// </summary>
    private static bool SupportsYear(Calendar calendar, int year)
    {
        try
        {
            return year >= calendar.GetYear(calendar.MinSupportedDateTime)
                && year <= calendar.GetYear(calendar.MaxSupportedDateTime);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
