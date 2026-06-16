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
}
